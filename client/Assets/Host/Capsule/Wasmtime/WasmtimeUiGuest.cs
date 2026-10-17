using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Capsule.Wasmtime
{
    // Trusted host injection for the retained-call prototype. Default callers
    // keep the existing synchronous ABI; standalone proofs provide a runner.
    public interface IWasmtimeInvocationRunner
    {
        ulong LastFuel { get; }
        int Call(IntPtr guest, string name, int a, int b, int args, int results, ulong allowance, out int value);
        void Destroy(IntPtr guest);
    }
    // Experimental native C ABI backend; no reflection-generated delegates or managed Wasmtime dependency.
    public sealed class WasmtimeUiGuest : IUiGuest, IUiFuelDiagnostics, IUiCallFuelDiagnostics, IUiHostCallDiagnostics, IUiSchedulingDiagnostics
    {
        public const ulong AddonExecutionFuel = 200000000;
        readonly ulong executionFuel;
        public const string QuickJsSha256 = "AEF934D6361DF6AA6BECBD9A80DD49643CA161F1D22DB0567DF2CB065477EEA9";
        const string Library = "portable_ui_wasmtime";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HostCallback(IntPtr data, int op, long a, long b, long c, long d, IntPtr memory, ulong size);
        static readonly HostCallback Callback = Dispatch;
        static readonly object BootstrapLock = new object();
        static bool bootstrapped;
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_bootstrap(byte[] bytes, int length);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_bootstrap_error();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_create_with_host_call_limit(HostCallback callback, IntPtr data, int limit);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ulong pui_host_call_rejections(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_call(IntPtr guest, [MarshalAs(UnmanagedType.LPStr)] string name, int a, int b, int nargs, int results, ulong fuel, out int result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_memory(IntPtr guest, out ulong size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ulong pui_fuel(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_error(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void pui_destroy(IntPtr guest);
        readonly byte[] sdk, source;
        readonly byte[][] modules;
        readonly IUiCommitTarget target;
        readonly IUiObservationAdapter observations;
        readonly IUiActionAdapter actions;
        readonly UiCapabilityGrant grant;
        readonly IWasmtimeInvocationRunner invocationRunner;
        IntPtr native;
        GCHandle self;
        bool terminal, disposed;
        string callbackError;
        UiObservationPayload pendingObservation;
        ushort pendingTopic, pendingSchema;
        public bool SupportsScheduling { get; }
        public long? NextWakeTimestamp { get; private set; }
        public long UpdateCalls { get; private set; }
        public UiSession Session
        {
            get;
        }
        public Action<string> InitializationTrace
        {
            get;
            set;
        }
        public ulong LastFuel
        {
            get;
            private set;
        }
        public ulong TotalCallFuel { get; private set; }
        public bool HasCallFuel => true;
        public bool HasHostCallMetrics => true;
        public int HostCallsPerInvocation { get; }
        public UiHostCallMetrics LastHostCalls { get; private set; }
        public UiHostCallMetrics TotalHostCalls { get; private set; }
        readonly UiHostCallMetrics[] operationMetrics = new UiHostCallMetrics[7];
        long copiedRead, copiedWritten;
        public UiHostCallMetrics ReadHostCalls(UiHostOperation operation)
        {
            int index = (int)operation;
            if (index < 0 || index >= operationMetrics.Length) throw new ArgumentOutOfRangeException(nameof(operation));
            return operationMetrics[index];
        }
        public WasmtimeUiGuest(byte[] carrier, byte[] sdk, byte[] source, UiAddonActivation activation, IUiTheme theme, IUiCommitTarget target, IUiObservationAdapter observations, IUiActionAdapter actions, bool scheduling = false, IWasmtimeInvocationRunner invocationRunner = null, int hostCallsPerInvocation = UiHostBridgeContract.MaxCallsPerInvocation, int layoutNodeLimit = UiSession.DefaultLayoutNodes, UiSurfaceAdmission surfaceAdmission = null, byte[][] scriptModules = null, ulong executionFuel = AddonExecutionFuel)
        {
            if (hostCallsPerInvocation != UiHostBridgeContract.MaxCallsPerInvocation && hostCallsPerInvocation != UiHostBridgeContract.ExtendedCallsPerInvocation)
                throw new ArgumentOutOfRangeException(nameof(hostCallsPerInvocation));
            HostCallsPerInvocation = hostCallsPerInvocation;
            if (executionFuel == 0) throw new ArgumentOutOfRangeException(nameof(executionFuel));
            this.executionFuel = executionFuel;
            if (source == null || source.Length > 65536 || sdk == null || sdk.Length > 524288) throw new ArgumentException("Source quota");
            using (var sha = SHA256.Create()) if (BitConverter.ToString(sha.ComputeHash(carrier)).Replace("-", "") != QuickJsSha256) throw new ArgumentException("Pinned carrier changed");
            scriptModules = scriptModules ?? Array.Empty<byte[]>();
            if (scriptModules.Length > 8) throw new ArgumentException("Script module count quota");
            modules = new byte[scriptModules.Length][];
            int total = source.Length;
            for (int i = 0; i < modules.Length; ++i)
            {
                var bytes = scriptModules[i];
                if (bytes == null || bytes.Length > 65536) throw new ArgumentException("Script module quota");
                total = checked(total + bytes.Length);
                if (total > 262144) throw new ArgumentException("Aggregate source quota");
                modules[i] = (byte[])bytes.Clone();
            }
            this.sdk = (byte[])sdk.Clone();
            this.source = (byte[])source.Clone();
            this.target = target;
            this.observations = observations;
            this.actions = actions;
            this.invocationRunner = invocationRunner;
            SupportsScheduling = scheduling;
            grant = activation.Grant;
            Session = new UiSession(activation.ConsumeOwner(), theme, layoutNodeLimit, surfaceAdmission);
            lock (BootstrapLock)
            {
                if (!bootstrapped)
                {
                    if (pui_bootstrap(carrier, carrier.Length) != 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(pui_bootstrap_error()));
                    bootstrapped = true;
                }
            }
            self = GCHandle.Alloc(this);
            native = pui_create_with_host_call_limit(Callback, GCHandle.ToIntPtr(self), hostCallsPerInvocation);
            if (native == IntPtr.Zero)
            {
                self.Free();
                throw new InvalidOperationException(Marshal.PtrToStringAnsi(pui_bootstrap_error()));
            }

        }
#if UNITY_5_3_OR_NEWER
        [AOT.MonoPInvokeCallback(typeof(HostCallback))]
#endif
        static int Dispatch(IntPtr data, int op, long a, long b, long c, long d, IntPtr memory, ulong size)
        {
            var guest = (WasmtimeUiGuest)GCHandle.FromIntPtr(data).Target;
            try
            {
                return guest.Import(op, a, b, c, d, memory, size);
            }
            catch (Exception error)
            {
                guest.callbackError = error.Message;
                return op <= 6 ? (int)UiHostResult.Internal : 29;
            }
        }
        sealed class GuestMemoryException : Exception { public GuestMemoryException() : base("Guest memory bounds") { } }
        static IntPtr Address(IntPtr memory, ulong size, long pointer, int length, int max)
        {
            ulong offset = unchecked((uint)pointer);
            if (length < 0 || length > max || offset > int.MaxValue || offset + (ulong)length > size) throw new GuestMemoryException();
            return IntPtr.Add(memory, checked((int)offset));
        }
        int Import(int op, long a, long b, long c, long d, IntPtr memory, ulong size)
        {
            long started = Stopwatch.GetTimestamp();
            copiedRead = copiedWritten = 0;
            int result = op <= 6 ? (int)UiHostResult.Internal : 29; // WASI IO for internal failure.
            try
            {
                result = terminal || disposed ? (op <= 6 ? (int)UiHostResult.Unavailable : 8) : ImportCore(op, a, b, c, d, memory, size);
                return result;
            }
            catch (GuestMemoryException) { return result = op <= 6 ? (int)UiHostResult.InvalidArgument : 21; } // WASI FAULT.
            catch (Exception error) { callbackError = error.Message; return result; }
            finally
            {
                var metrics = new UiHostCallMetrics(1, (op <= 6 ? result < 0 : result != 0) ? 1 : 0,
                    copiedRead, copiedWritten, Stopwatch.GetTimestamp() - started);
                TotalHostCalls += metrics;
                if (op >= 0 && op < operationMetrics.Length) operationMetrics[op] += metrics;
            }
        }
        static int Result(UiResult result) => result.Success ? (int)UiHostResult.Success
            : result.Error == UiError.QuotaExceeded ? (int)UiHostResult.QuotaExceeded : (int)UiHostResult.Rejected;
        int ImportCore(int op, long a, long b, long c, long d, IntPtr memory, ulong size)
        {
            if (op == (int)UiHostOperation.Reserve)
            {
                var output = Address(memory, size, a, 8, 8);
                var result = Session.ReserveHandle(out var handle);
                if (!result.Success) return Result(result);
                Marshal.WriteInt64(output, unchecked((long)handle)); copiedWritten += 8;
                return 0;
            }
            if (op == (int)UiHostOperation.Submit)
            {
                if (b < 0 || b > UiHostBridgeContract.MaxSubmitBytes) return (int)UiHostResult.InvalidArgument;
                var input = Address(memory, size, a, (int)b, 65536);
                var bytes = new byte[(int)b];
                Marshal.Copy(input, bytes, 0, bytes.Length); copiedRead += bytes.Length;
                var submitted = Session.Submit(bytes, target);
                if (!submitted.Success) callbackError = "Native UI submission rejected: " + submitted.Detail;
                return Result(submitted);
            }
            if (op == (int)UiHostOperation.NextEvent)
            {
                if (b != UiHostBridgeContract.EventCapacity && b != UiHostBridgeContract.TextEventCapacity) return (int)UiHostResult.InvalidArgument;
                Address(memory, size, a, (int)b, UiHostBridgeContract.TextEventCapacity);
                var bytes = new byte[(int)b];
                var result = Session.WriteNextEvent(bytes, out int n);
                if (!result.Success) return Result(result);
                Marshal.Copy(bytes, 0, Address(memory, size, a, n, UiHostBridgeContract.TextEventCapacity), n); copiedWritten += n;
                return n;
            }
            if (op == (int)UiHostOperation.NextObservation)
            {
                // Capacity zero queries length without consuming the pending record.
                // Legacy callers retain their 264-byte buffer. A short buffer cannot lose data.
                if (b != 0 && b != UiHostBridgeContract.ObservationCapacity &&
                    (pendingObservation == null || b != pendingObservation.Length + 8)) return (int)UiHostResult.InvalidArgument;
                if (b != 0) Address(memory, size, a, (int)b, UiHostBridgeContract.MaxObservationBytes + 8);
                if (observations == null) return (int)UiHostResult.Unavailable;
                if (pendingObservation == null)
                {
                    if (observations is IUiSegmentedObservationAdapter segmented)
                    {
                        if (!segmented.TryDequeueSegments(out pendingTopic, out pendingSchema, out pendingObservation)) return 0;
                    }
                    else
                    {
                        if (!observations.TryDequeue(out pendingTopic, out pendingSchema, out var bytes)) return 0;
                        if (bytes != null) pendingObservation = UiObservationPayload.Owned(bytes);
                    }
                }
                if (!grant.AllowsObservation(pendingTopic, pendingSchema)) { pendingObservation = null; return (int)UiHostResult.Rejected; }
                var payload = pendingObservation;
                if (payload == null || payload.Length > UiHostBridgeContract.MaxObservationBytes) { pendingObservation = null; return (int)UiHostResult.InvalidArgument; }
                int length = payload.Length + 8;
                if (b == 0) return length;
                if (b < length) return (int)UiHostResult.InvalidArgument;
                IntPtr destination = Address(memory, size, a, length, UiHostBridgeContract.MaxObservationBytes + 8);
                Marshal.WriteInt16(destination, 0, unchecked((short)pendingTopic));
                Marshal.WriteInt16(destination, 2, unchecked((short)pendingSchema));
                Marshal.WriteInt32(destination, 4, payload.Length);
                payload.WriteTo(IntPtr.Add(destination, 8));
                copiedWritten += length;
                pendingObservation = null;
                return length;
            }
            if (op == (int)UiHostOperation.RequestAction)
            {
                if (b < 1 || b > 65535 || d < 0 || d > UiHostBridgeContract.MaxPayloadBytes) return (int)UiHostResult.InvalidArgument;
                if (!grant.AllowsAction((ushort)b)) return (int)UiHostResult.Rejected;
                if (actions == null) return (int)UiHostResult.Unavailable;
                var bytes = new byte[(int)d];
                Marshal.Copy(Address(memory, size, c, bytes.Length, 256), bytes, 0, bytes.Length); copiedRead += bytes.Length;
                // Only explicit host read grants permit requests without a live node.
                // The host adapter still enforces member permission and activation identity.
                if (a == 0 && grant.AllowsReadOnlyAction((ushort)b))
                    return actions.TryRequest((ushort)b, 0, bytes) ? (int)UiHostResult.Success : (int)UiHostResult.Rejected;
                return Result(Session.RequestAction(unchecked((ulong)a), (ushort)b, bytes, actions));
            }
            // Package operations are deliberately unavailable in the current host profile.
            if (op == (int)UiHostOperation.ReadData || op == (int)UiHostOperation.AcquireImage) return (int)UiHostResult.Unsupported;
            if (op == 7)
            {
                Address(memory, size, a, 0, 0);
                Address(memory, size, b, 0, 0);
                return 0;
            }
            if (op == 8)
            {
                Marshal.WriteInt32(Address(memory, size, a, 4, 4), 0); copiedWritten += 4;
                Marshal.WriteInt32(Address(memory, size, b, 4, 4), 0); copiedWritten += 4;
                return 0;
            }
            if (op == 9)
            {
                if (a != 0 && a != 1) return 28;
                ulong nanos = a == 0 ? unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) * 1000000UL : unchecked((ulong)(Stopwatch.GetTimestamp() * (1000000000.0 / Stopwatch.Frequency)));
                Marshal.WriteInt64(Address(memory, size, c, 8, 8), unchecked((long)nanos)); copiedWritten += 8;
                return 0;
            }
            return 8;

        }
        bool Call(string name, int a, int b, int nargs, int results, ulong fuel, out int value, out string error)
        {
            // Core lifecycle helpers can execute addon code too (for example viewport subscribers).
            if (executionFuel == ulong.MaxValue) fuel = ulong.MaxValue;
            value = 0;
            if (terminal || disposed)
            {
                error = "Guest is terminal or disposed";
                return false;
            }
            int code;
            var beforeHostCalls = TotalHostCalls;
            try
            {
                code = invocationRunner == null
                    ? pui_call(native, name, a, b, nargs, results, fuel, out value)
                    : invocationRunner.Call(native, name, a, b, nargs, results, fuel, out value);
            }
            finally
            {
                long rejected = checked((long)pui_host_call_rejections(native));
                TotalHostCalls += new UiHostCallMetrics(0, 0, 0, 0, 0, rejected - TotalHostCalls.QuotaRejections);
                LastHostCalls = TotalHostCalls - beforeHostCalls;
            }
            LastFuel = invocationRunner == null ? pui_fuel(native) : invocationRunner.LastFuel;
            TotalCallFuel = checked(TotalCallFuel + LastFuel);
            if (code != 0 || callbackError != null)
            {
                terminal = true;
                error = name + ": " + (callbackError ?? Marshal.PtrToStringAnsi(pui_error(native)));
                return false;
            }
            error = "";
            return true;

        }
        bool Evaluate(byte[] bytes, string name, ulong fuel, out string error)
        {
            if (!Call("portable_ui_alloc", bytes.Length + 1, 0, 1, 1, 200000, out int pointer, out error)) return false;
            if (pointer == 0)
            {
                error = "QuickJS source allocation failed";
                terminal = true;
                return false;
            }
            var memory = pui_memory(native, out ulong size);
            Marshal.Copy(bytes, 0, Address(memory, size, pointer, bytes.Length, 524288), bytes.Length);
            Marshal.WriteByte(Address(memory, size, pointer + bytes.Length, 1, 1), 0);
            bool ok = Call(name, pointer, bytes.Length, 2, 1, fuel, out int result, out error);
            ulong used = LastFuel;
            if (ok && result != 0)
            {
                error = ReadError();
                terminal = true;
                ok = false;
            }
            if (!terminal) Call("portable_ui_free", pointer, 0, 1, 0, 200000, out _, out _);
            LastFuel = used;
            return ok;

        }
        string ReadError()
        {
            if (!Call("portable_ui_alloc", 512, 0, 1, 1, 200000, out int p, out _)) return "JS error";
            Call("portable_ui_error", p, 512, 2, 1, 200000, out int n, out _);
            var memory = pui_memory(native, out ulong size);
            if (n < 0 || n > 511) return "Invalid QuickJS diagnostic length";
            var bytes = new byte[n];
            Marshal.Copy(Address(memory, size, p, bytes.Length, 512), bytes, 0, bytes.Length);
            Call("portable_ui_free", p, 0, 1, 0, 200000, out _, out _);
            return Encoding.UTF8.GetString(bytes);
        }
        public bool Initialize(out string error)
        {
            InitializationTrace?.Invoke("QuickJS init begin");
            if (!Call("portable_ui_init", 0, 0, 0, 1, 2000000, out int result, out error)) return false;
            if (result != 0)
            {
                error = "QuickJS initialization rejected: " + result;
                terminal = true;
                return false;
            }
            InitializationTrace?.Invoke("Host SDK eval begin");
            if (!Evaluate(sdk, "portable_ui_bootstrap", 200000000, out error)) return false;
            InitializationTrace?.Invoke("Addon eval begin");
            ulong start = TotalCallFuel;
            foreach (var module in modules)
                if (!EvaluateAddon(module, start, out error)) return false;
            return EvaluateAddon(source, start, out error);
        }
        bool EvaluateAddon(byte[] bytes, ulong start, out string error)
        {
            ulong used = TotalCallFuel - start;
            // Include allocation/free overhead in the single activation allowance.
            const ulong overhead = 400000;
            if (used + overhead >= executionFuel)
            { terminal = true; error = "Addon activation fuel exhausted"; return false; }
            return Evaluate(bytes, "portable_ui_eval", executionFuel - used - overhead, out error);
        }
        public bool Update(out string error)
        {
            ++UpdateCalls;
            if (!Call(SupportsScheduling ? "portable_ui_update_v2" : "portable_ui_update", 0, 0, 0, 1, executionFuel, out int result, out error)) return false;
            if (SupportsScheduling && result >= 0)
            {
                NextWakeTimestamp = result == 0 ? (long?)null : Stopwatch.GetTimestamp() +
                    (long)((result - 1) * (Stopwatch.Frequency / 1000.0));
                return true;
            }
            if (result == 0) return true;
            ulong updateFuel = LastFuel;
            error = ReadError();
            LastFuel = updateFuel;
            terminal = true;
            return false;
        }
        public bool SetViewport(int width, int height, out string error) => Evaluate(Encoding.UTF8.GetBytes("globalThis.__portableSetViewport?.(" + width + "," + height + ");"), "portable_ui_eval", 5000000, out error);
        public void Dispose()
        {
            if (disposed) return;
            try
            {
                if (!terminal) Evaluate(Encoding.UTF8.GetBytes("globalThis.portableUiDispose?.();"), "portable_ui_eval", 5000000, out _);
                Session.Unload(target);
            }
            finally
            {
                disposed = true;
                if (invocationRunner == null) pui_destroy(native);
                else invocationRunner.Destroy(native);
                native = IntPtr.Zero;
                if (self.IsAllocated) self.Free();
            }
        }

    }

}

