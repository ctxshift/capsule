using System;
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
        const int MaxHostCallsPerExport = 64;
        readonly UiSession session;
        readonly IUiCommitTarget target;
        readonly IUiObservationAdapter observations;
        readonly IUiActionAdapter actions;
        readonly UiPackageLease package;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        readonly WasmRuntime runtime;
        MemoryInstance memory;
        int calls;
        bool disposed;
        string importFailure;

        public UiSession Session => session;
        public string LastImportFailure => importFailure ?? string.Empty;

        public WacsUiGuest(byte[] copiedSource, uint ownerNonce, IUiTheme theme,
            IUiCommitTarget commitTarget, IUiObservationAdapter observationAdapter,
            IUiActionAdapter actionAdapter, UiPackageLease packageLease = null)
        {
            if (commitTarget == null || observationAdapter == null || actionAdapter == null)
                throw new ArgumentNullException("Guest adapters must be supplied.");
            if (!WacsModulePolicy.TryParse(copiedSource, out var module, out var failure))
                throw new ArgumentException(failure, nameof(copiedSource));
            session = new UiSession(ownerNonce, theme);
            target = commitTarget;
            observations = observationAdapter;
            actions = actionAdapter;
            package = packageLease;
            runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int>>((Namespace, "ui_reserve_handle"), Reserve);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "ui_submit"), Submit);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "ui_next_event"), NextEvent);
            runtime.BindHostFunction<Func<int, int, int>>((Namespace, "game_next_observation"), NextObservation);
            runtime.BindHostFunction<Func<long, int, int, int, int>>((Namespace, "game_request_action"), RequestAction);
            runtime.BindHostFunction<Func<int, int, int, int, int>>((Namespace, "package_read_data"), ReadData);
            runtime.BindHostFunction<Func<int, int, int, int, int>>((Namespace, "package_acquire_image"), AcquireImage);
            // Start sections were rejected by policy; no unmetered guest code runs here.
            runtime.RegisterModule(GuestName, runtime.InstantiateModule(module,
                new RuntimeOptions { SkipStartFunction = true }));
            if (!runtime.TryGetExportedMemory((GuestName, "memory"), out memory))
                throw new ArgumentException("Guest must export its bounded memory.", nameof(copiedSource));
        }

        public bool Initialize(out string failure)
        {
            if (!Invoke("ui_abi_version", 1, out failure)) return false;
            return Invoke("ui_init", 0, out failure);
        }

        public bool Update(out string failure) => Invoke("ui_update", 0, out failure);

        bool Invoke(string name, int expected, out string failure)
        {
            failure = null;
            if (disposed) { failure = "Guest disposed."; return false; }
            importFailure = null;
            calls = 0;
            try
            {
                if (!runtime.TryGetExportedFunction((GuestName, name), out var address))
                { failure = "Missing export " + name + "."; return false; }
                var call = runtime.CreateInvokerFunc<int>(address,
                    new InvokerOptions { GasLimit = GasPerExport });
                int result = call();
                if (result != expected)
                { failure = importFailure ?? (name + " returned " + result + "."); return false; }
                return true;
            }
            catch (Exception error)
            {
                failure = importFailure ?? (name + " trapped: " + error.Message);
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
            var bytes = new byte[length];
            Buffer.BlockCopy(memory.Data, unchecked((int)(uint)pointer), bytes, 0, length);
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

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.Unload(target);
            if (runtime is IDisposable disposable) disposable.Dispose();
        }
    }
}
