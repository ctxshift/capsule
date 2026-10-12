using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using PortableUi.Package;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace PortableUi.Wacs
{
    // A single interpreted guest. This assembly has no Unity or Host dependency.
    public sealed class WacsUiGuest : IDisposable
    {
        const string Namespace = "portable_ui_v1";
        const string GuestName = "portable_ui_guest";
        const int GasPerExport = 200000;
        const int QuickJsInitGas = 2000000;
        const int QuickJsEvalGas = 5000000;
        const int QuickJsUpdateGas = 5000000;
        public const string QuickJsSha256 =
            "005FACE7448194E77DBC2A730C69F2AE5FAF60B9B39D42AC1D1236C6EF67DA7D";
        const int MaxHostCallsPerExport = 64;
        readonly UiSession session;
        readonly IUiCommitTarget target;
        readonly IUiObservationAdapter observations;
        readonly IUiActionAdapter actions;
        readonly UiCapabilityGrant grant;
        readonly IUiInputIntentAdapter inputs;
        readonly UiPackageLease package;
        readonly byte[] javascript;
        readonly byte[] sdk;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        readonly WasmRuntime runtime;
        MemoryInstance memory;
        int calls;
        bool disposed;
        bool terminal;
        string importFailure;

        public UiSession Session => session;
        public string LastImportFailure => importFailure ?? string.Empty;

        public WacsUiGuest(TrustedWasmCarrier carrier, uint ownerNonce, IUiTheme theme,
            IUiCommitTarget commitTarget, IUiObservationAdapter observationAdapter,
            IUiActionAdapter actionAdapter, UiPackageLease packageLease = null)
            : this(carrier, ownerNonce, theme, commitTarget, observationAdapter,
                actionAdapter, packageLease, null, null) { }

        public static WacsUiGuest FromJavaScript(byte[] hostCarrier, byte[] hostSdk, byte[] script,
            UiAddonActivation activation, IUiTheme theme, IUiCommitTarget commitTarget,
            IUiObservationAdapter observationAdapter, IUiActionAdapter actionAdapter,
            UiPackageLease packageLease = null, IUiInputIntentAdapter inputAdapter = null)
        {
            if (activation == null) throw new ArgumentNullException(nameof(activation));
            if (activation.Grant.HasInput && inputAdapter == null)
                throw new ArgumentNullException(nameof(inputAdapter));
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (hostSdk == null) throw new ArgumentNullException(nameof(hostSdk));
            if (!TrustedWasmCarrier.TryCreate(hostCarrier, QuickJsSha256,
                    out var approved, out var failure))
                throw new ArgumentException(failure, nameof(hostCarrier));
            return new WacsUiGuest(approved, activation.ConsumeOwner(), theme, commitTarget,
                observationAdapter, actionAdapter, packageLease, script, hostSdk, activation.Grant, inputAdapter);
        }

        WacsUiGuest(TrustedWasmCarrier carrier, uint ownerNonce, IUiTheme theme,
            IUiCommitTarget commitTarget, IUiObservationAdapter observationAdapter,
            IUiActionAdapter actionAdapter, UiPackageLease packageLease, byte[] script,
            byte[] hostSdk, UiCapabilityGrant capabilityGrant = null, IUiInputIntentAdapter inputAdapter = null)
        {
            if (commitTarget == null || observationAdapter == null || actionAdapter == null)
                throw new ArgumentNullException("Guest adapters must be supplied.");
            if (script != null && (script.Length == 0 || script.Length > 65536))
                throw new ArgumentException("JavaScript bundle must be 1..65536 bytes.", nameof(script));
            if (script != null && (hostSdk.Length == 0 || hostSdk.Length > 65536))
                throw new ArgumentException("Host JS SDK must be 1..65536 bytes.", nameof(hostSdk));
            if (script != null)
            {
                try { StrictUtf8.GetString(script); }
                catch (DecoderFallbackException)
                { throw new ArgumentException("JavaScript bundle must be UTF-8.", nameof(script)); }
                try { StrictUtf8.GetString(hostSdk); }
                catch (DecoderFallbackException)
                { throw new ArgumentException("Host JS SDK must be UTF-8.", nameof(hostSdk)); }
            }
            if (!WacsModulePolicy.TryParse(carrier, out var module, out var failure,
                    script != null))
                throw new ArgumentException(failure, nameof(carrier));
            session = new UiSession(ownerNonce, theme);
            target = commitTarget;
            observations = observationAdapter;
            actions = actionAdapter;
            grant = capabilityGrant;
            inputs = inputAdapter;
            package = packageLease;
            javascript = script == null ? null : (byte[])script.Clone();
            sdk = hostSdk == null ? null : (byte[])hostSdk.Clone();
            runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int>>((Namespace, "ui_reserve_handle"), Reserve);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "ui_submit"), Submit);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "ui_next_event"), NextEvent);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "game_next_observation"), NextObservation);
            runtime.BindHostFunction<Func<long, int, int, int, int>>((Namespace, "game_request_action"), RequestAction);
            runtime.BindHostFunction<Func<int, int, int, int, int>>((Namespace, "package_read_data"), ReadData);
            runtime.BindHostFunction<Func<int, int, int, int, int>>((Namespace, "package_acquire_image"), AcquireImage);
            if (javascript != null) BindQuickJsWasi();
            // Start sections were rejected by policy; no unmetered guest code runs here.
            runtime.RegisterModule(GuestName, runtime.InstantiateModule(module,
                new RuntimeOptions { SkipStartFunction = true }));
            if (!runtime.TryGetExportedMemory((GuestName, "memory"), out memory))
                throw new ArgumentException("Guest must export its bounded memory.", nameof(carrier));
        }

        public bool Initialize(out string failure)
        {
            if (javascript != null) return InitializeJavaScript(out failure);
            if (!Invoke("ui_abi_version", 1, out failure)) return false;
            return Invoke("ui_init", 0, out failure);
        }

        public bool Update(out string failure) => javascript == null ?
            Invoke("ui_update", 0, out failure) :
            Invoke("portable_ui_update", 0, out failure, QuickJsUpdateGas);

        bool InitializeJavaScript(out string failure)
        {
            if (!Invoke("portable_ui_init", 0, out failure, QuickJsInitGas)) return false;
            if (!EvaluateJavaScript(sdk, "portable_ui_bootstrap", out failure)) return false;
            return EvaluateJavaScript(javascript, "portable_ui_eval", out failure);
        }

        bool EvaluateJavaScript(byte[] source, string export, out string failure)
        {
            try
            {
                if (!runtime.TryGetExportedFunction((GuestName, "portable_ui_alloc"), out var allocAddress) ||
                    !runtime.TryGetExportedFunction((GuestName, export), out var evalAddress))
                { failure = "QuickJS carrier exports missing."; return false; }
                var alloc = runtime.CreateInvokerFunc<int, int>(allocAddress,
                    new InvokerOptions { GasLimit = GasPerExport });
                int pointer = alloc(source.Length + 1);
                if (pointer == 0 || !Bounds(pointer, source.Length + 1, 65537))
                { failure = "QuickJS source allocation failed."; return false; }
                Buffer.BlockCopy(source, 0, memory.Data, pointer, source.Length);
                memory.Data[pointer + source.Length] = 0;
                var evaluate = runtime.CreateInvokerFunc<int, int, int>(evalAddress,
                    new InvokerOptions { GasLimit = QuickJsEvalGas });
                calls = 0;
                int result = evaluate(pointer, source.Length);
                string sourceError = result == 0 ? null : ReadJavaScriptError();
                FreeJavaScriptBuffer(pointer);
                if (result != 0)
                { failure = sourceError ?? "JavaScript evaluation failed: " + result; return false; }
                failure = null;
                return true;
            }
            catch (Exception error)
            {
                terminal = true;
                while (error is TargetInvocationException && error.InnerException != null)
                    error = error.InnerException;
                failure = "QuickJS initialization trapped: " + error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        bool Invoke(string name, int expected, out string failure,
            int gasLimit = GasPerExport)
        {
            failure = null;
            if (disposed) { failure = "Guest disposed."; return false; }
            if (terminal) { failure = "Guest trapped and cannot resume."; return false; }
            importFailure = null;
            calls = 0;
            try
            {
                if (!runtime.TryGetExportedFunction((GuestName, name), out var address))
                { failure = "Missing export " + name + "."; return false; }
                var call = runtime.CreateInvokerFunc<int>(address,
                    new InvokerOptions { GasLimit = gasLimit });
                int result = call();
                if (result != expected)
                { failure = importFailure ?? ReadJavaScriptError() ??
                        (name + " returned " + result + "."); return false; }
                return true;
            }
            catch (Exception error)
            {
                if (javascript != null) terminal = true;
                while (error is TargetInvocationException && error.InnerException != null)
                    error = error.InnerException;
                failure = importFailure ?? (name + " trapped: " + error.GetType().Name + ": " + error.Message);
                return false;
            }
        }

        void Charge()
        {
            if (++calls > MaxHostCallsPerExport)
                throw new InvalidOperationException("Guest host-call quota exceeded.");
        }

        bool Bounds(int pointer, int length, int maximum)
        {
            if (length < 0 || length > maximum || memory == null || memory.Data == null)
                return false;
            ulong start = unchecked((uint)pointer);
            return start + (uint)length <= (ulong)memory.Data.LongLength;
        }

        int Reject(string reason, int code = 1)
        { importFailure = reason; return code; }

        int Reserve(int pointer)
        {
            Charge();
            if (!Bounds(pointer, 8, 8)) return Reject("Invalid reserve output pointer.");
            var result = session.ReserveHandle(out var handle);
            if (!result.Success) return Reject(result.Detail, (int)result.Error);
            int start = unchecked((int)(uint)pointer);
            for (int i = 0; i < 8; ++i) memory.Data[start + i] = (byte)(handle >> (8 * i));
            return 0;
        }

        int Submit(int pointer, int length)
        {
            Charge();
            if (!Bounds(pointer, length, 65536)) return Reject("Invalid command buffer.");
            var bytes = new byte[length];
            Buffer.BlockCopy(memory.Data, unchecked((int)(uint)pointer), bytes, 0, length);
            var result = session.Submit(bytes, target);
            return result.Success ? 0 : Reject(result.Detail, (int)result.Error);
        }

        int NextEvent(int pointer, int capacity)
        {
            Charge();
            if (capacity < 272 || !Bounds(pointer, capacity, 272))
                return -Reject("Invalid event buffer.");
            var buffer = new byte[272];
            var result = session.WriteNextEvent(buffer, out int length);
            if (!result.Success) return -Reject(result.Detail, (int)result.Error);
            if (length != 0) Buffer.BlockCopy(buffer, 0, memory.Data,
                unchecked((int)(uint)pointer), length);
            return length;
        }

        int NextObservation(int pointer, int capacity)
        {
            Charge();
            if (capacity < 264 || !Bounds(pointer, capacity, 272))
                return -Reject("Invalid observation buffer.");
            if (!observations.TryDequeue(out var topic, out var schema, out var payload)) return 0;
            if (grant != null && !grant.AllowsObservation(topic, schema))
                return -Reject("Observation capability denied.");
            if (payload == null || payload.Length > 256)
                return -Reject("Observation payload quota exceeded.");
            int start = unchecked((int)(uint)pointer);
            var bytes = memory.Data;
            bytes[start] = (byte)topic; bytes[start + 1] = (byte)(topic >> 8);
            bytes[start + 2] = (byte)schema; bytes[start + 3] = (byte)(schema >> 8);
            bytes[start + 4] = (byte)payload.Length;
            bytes[start + 5] = (byte)(payload.Length >> 8);
            bytes[start + 6] = 0; bytes[start + 7] = 0;
            Buffer.BlockCopy(payload, 0, bytes, start + 8, payload.Length);
            return payload.Length + 8;
        }

        int RequestAction(long handle, int capability, int pointer, int length)
        {
            Charge();
            if (capability <= 0 || capability > ushort.MaxValue || !Bounds(pointer, length, 256))
                return Reject("Invalid action request.");
            // Zero source is the private wire discriminator for an input intent.
            // It never falls back to the UI action route or inherits action grants.
            bool input = handle == 0 && grant != null;
            if (grant != null && !(input ? grant.AllowsInput((ushort)capability) :
                    grant.AllowsAction((ushort)capability)))
                return Reject(input ? "Input capability denied." : "Action capability denied.");
            var bytes = new byte[length];
            Buffer.BlockCopy(memory.Data, unchecked((int)(uint)pointer), bytes, 0, length);
            if (input)
            {
                try { return inputs != null && inputs.TryRequestInput((ushort)capability, bytes)
                    ? 0 : Reject("Input rejected by adapter."); }
                catch (Exception error) { return Reject(error.Message); }
            }
            var result = session.RequestAction(unchecked((ulong)handle),
                (ushort)capability, bytes, actions);
            return result.Success ? 0 : Reject(result.Detail, (int)result.Error);
        }

        string ReadKey(int pointer, int length)
        {
            if (length < 1 || length > 96 || !Bounds(pointer, length, 96)) return null;
            try { return StrictUtf8.GetString(memory.Data, unchecked((int)(uint)pointer), length); }
            catch (DecoderFallbackException) { return null; }
        }

        int ReadData(int keyPointer, int keyLength, int outputPointer, int capacity)
        {
            Charge();
            string key = ReadKey(keyPointer, keyLength);
            if (package == null || key == null || capacity < 0 || capacity > 65536 ||
                !Bounds(outputPointer, capacity, 65536) ||
                !package.TryCopyData(key, out var bytes) || bytes.Length > capacity)
                return -Reject("Package data unavailable or output buffer too small.");
            Buffer.BlockCopy(bytes, 0, memory.Data, unchecked((int)(uint)outputPointer), bytes.Length);
            return bytes.Length;
        }

        int AcquireImage(int keyPointer, int keyLength, int outputPointer, int capacity)
        {
            Charge();
            string key = ReadKey(keyPointer, keyLength);
            if (package == null || key == null || capacity < 33 ||
                capacity > 96 || !Bounds(outputPointer, capacity, 96) ||
                !package.TryAcquireImageKey(key, out var assetKey, out var failure))
                return -Reject("Package image unavailable or output buffer too small.");
            byte[] encoded = StrictUtf8.GetBytes(assetKey);
            if (encoded.Length > capacity) return -Reject("Package image output buffer too small.");
            Buffer.BlockCopy(encoded, 0, memory.Data, unchecked((int)(uint)outputPointer), encoded.Length);
            return encoded.Length;
        }

        string ReadJavaScriptError()
        {
            if (javascript == null || terminal || memory == null) return null;
            try
            {
                if (!runtime.TryGetExportedFunction((GuestName, "portable_ui_alloc"), out var allocAddress) ||
                    !runtime.TryGetExportedFunction((GuestName, "portable_ui_error"), out var errorAddress))
                    return null;
                int pointer = runtime.CreateInvokerFunc<int, int>(allocAddress,
                    new InvokerOptions { GasLimit = GasPerExport })(512);
                if (pointer == 0 || !Bounds(pointer, 512, 512)) return null;
                int length = runtime.CreateInvokerFunc<int, int, int>(errorAddress,
                    new InvokerOptions { GasLimit = GasPerExport })(pointer, 512);
                string detail = length > 0 && length < 512 ?
                    StrictUtf8.GetString(memory.Data, pointer, length) : null;
                FreeJavaScriptBuffer(pointer);
                return detail;
            }
            catch { terminal = true; return null; }
        }

        void FreeJavaScriptBuffer(int pointer)
        {
            if (terminal || pointer == 0) return;
            if (!runtime.TryGetExportedFunction((GuestName, "portable_ui_free"), out var address))
                throw new InvalidOperationException("QuickJS free export missing.");
            runtime.CreateInvokerAction<int>(address,
                new InvokerOptions { GasLimit = GasPerExport })(pointer);
        }

        void BindQuickJsWasi()
        {
            const string wasi = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<int, int, int>>((wasi, "args_get"), WasiArgsGet);
            runtime.BindHostFunction<Func<int, int, int>>((wasi, "args_sizes_get"), WasiArgsSizes);
            runtime.BindHostFunction<Func<int, long, int, int>>((wasi, "clock_time_get"), WasiClock);
            runtime.BindHostFunction<Func<int, int, int>>((wasi, "fd_fdstat_get"), WasiFdStat);
            runtime.BindHostFunction<Func<int, long, int, int, int>>((wasi, "fd_seek"), WasiSeek);
            runtime.BindHostFunction<Func<int, int, int, int, int>>((wasi, "fd_write"), WasiWrite);
            runtime.BindHostFunction<Action<int>>((wasi, "proc_exit"), WasiExit);
            runtime.BindHostFunction<Func<int, int>>((wasi, "fd_close"), WasiClose);
        }

        int WasiArgsGet(int argv, int buffer)
        {
            Charge();
            return Bounds(argv, 0, 0) && Bounds(buffer, 0, 0) ? 0 : 28;
        }

        int WasiArgsSizes(int count, int bytes)
        {
            Charge();
            if (!Bounds(count, 4, 4) || !Bounds(bytes, 4, 4)) return 28;
            Array.Clear(memory.Data, count, 4);
            Array.Clear(memory.Data, bytes, 4);
            return 0;
        }

        int WasiClock(int clock, long precision, int output)
        {
            Charge();
            if ((clock != 0 && clock != 1) || !Bounds(output, 8, 8)) return 28;
            ulong nanos = clock == 0 ?
                unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) * 1000000UL :
                unchecked((ulong)(Stopwatch.GetTimestamp() *
                    (1000000000.0 / Stopwatch.Frequency)));
            for (int i = 0; i < 8; ++i)
                memory.Data[output + i] = (byte)(nanos >> (8 * i));
            return 0;
        }

        int WasiFdStat(int descriptor, int output) { Charge(); return 8; }
        int WasiSeek(int descriptor, long offset, int whence, int output)
        { Charge(); return 8; }
        int WasiWrite(int descriptor, int vectors, int count, int output)
        { Charge(); return 8; }
        int WasiClose(int descriptor) { Charge(); return 8; }
        void WasiExit(int status)
        { Charge(); throw new InvalidOperationException("QuickJS process exit is unavailable."); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.Unload(target);
            if (runtime is IDisposable disposable) disposable.Dispose();
        }
    }
}
