using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PortableUi.Wasmtime
{
    // Experimental native C ABI backend; no reflection-generated delegates or managed Wasmtime dependency.
    public sealed class WasmtimeUiGuest : IUiGuest, IUiFuelDiagnostics, IUiSchedulingDiagnostics
    {
        const string Library = "portable_ui_wasmtime";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HostCallback(IntPtr data, int op, long a, long b, long c, long d, IntPtr memory, ulong size);
        static readonly HostCallback Callback = Dispatch;
        static readonly object BootstrapLock = new object();
        static bool bootstrapped;
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_bootstrap(byte[] bytes, int length);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_bootstrap_error();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_create(HostCallback callback, IntPtr data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_call(IntPtr guest, [MarshalAs(UnmanagedType.LPStr)] string name, int a, int b, int nargs, int results, ulong fuel, out int result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_memory(IntPtr guest, out ulong size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ulong pui_fuel(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_error(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void pui_destroy(IntPtr guest);
        readonly byte[] sdk, source;
        readonly IUiCommitTarget target;
        readonly IUiObservationAdapter observations;
        readonly IUiActionAdapter actions;
        readonly UiCapabilityGrant grant;
        IntPtr native;
        GCHandle self;
        bool terminal, disposed;
        string callbackError;
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
        public WasmtimeUiGuest(byte[] carrier, byte[] sdk, byte[] source, UiAddonActivation activation, IUiTheme theme, IUiCommitTarget target, IUiObservationAdapter observations, IUiActionAdapter actions, bool scheduling = false)
        {
            if (source == null || source.Length > 65536 || sdk == null || sdk.Length > 524288) throw new ArgumentException("Source quota");
            using (var sha = SHA256.Create()) if (BitConverter.ToString(sha.ComputeHash(carrier)).Replace("-", "") != "0975D550E2B8A9BA9CBBBEB6B5914157C793631218C222F8BDDF12EB70905835") throw new ArgumentException("Pinned carrier changed");
            this.sdk = (byte[])sdk.Clone();
            this.source = (byte[])source.Clone();
            this.target = target;
            this.observations = observations;
            this.actions = actions;
            SupportsScheduling = scheduling;
            grant = activation.Grant;
            Session = new UiSession(activation.ConsumeOwner(), theme);
            lock (BootstrapLock)
            {
                if (!bootstrapped)
                {
                    if (pui_bootstrap(carrier, carrier.Length) != 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(pui_bootstrap_error()));
                    bootstrapped = true;
                }
            }
            self = GCHandle.Alloc(this);
            native = pui_create(Callback, GCHandle.ToIntPtr(self));
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
                return -1;
            }
        }
        static IntPtr Address(IntPtr memory, ulong size, long pointer, int length, int max)
        {
            ulong offset = unchecked((uint)pointer);
            if (length < 0 || length > max || offset + (ulong)length > size) throw new InvalidOperationException("Guest memory bounds");
            return IntPtr.Add(memory, checked((int)offset));
        }
        int Import(int op, long a, long b, long c, long d, IntPtr memory, ulong size)
        {
            if (op == 0)
            {
                var output = Address(memory, size, a, 8, 8);
                var result = Session.ReserveHandle(out var handle);
                if (!result.Success) return -1;
                Marshal.WriteInt64(output, unchecked((long)handle));
                return 0;
            }
            if (op == 1)
            {
                if (b < 0 || b > 65536) return -1;
                var input = Address(memory, size, a, (int)b, 65536);
                var bytes = new byte[(int)b];
                Marshal.Copy(input, bytes, 0, bytes.Length);
                var submitted = Session.Submit(bytes, target);
                if (!submitted.Success) callbackError = "Native UI submission rejected: " + submitted.Detail;
                return submitted.Success ? 0 : -1;
            }
            if (op == 2)
            {
                Address(memory, size, a, (int)b, 272);
                var bytes = new byte[272];
                var result = Session.WriteNextEvent(bytes, out int n);
                if (!result.Success) return -1;
                Marshal.Copy(bytes, 0, Address(memory, size, a, n, 272), n);
                return n;
            }
            if (op == 3)
            {
                Address(memory, size, a, (int)b, 264);
                if (!observations.TryDequeue(out var topic, out var schema, out var payload)) return 0;
                if (!grant.AllowsObservation(topic, schema) || payload == null || payload.Length > 256) return -1;
                var bytes = new byte[payload.Length + 8];
                bytes[0] = (byte)topic;
                bytes[1] = (byte)(topic >> 8);
                bytes[2] = (byte)schema;
                bytes[3] = (byte)(schema >> 8);
                bytes[4] = (byte)payload.Length;
                bytes[5] = (byte)(payload.Length >> 8);
                Buffer.BlockCopy(payload, 0, bytes, 8, payload.Length);
                Marshal.Copy(bytes, 0, Address(memory, size, a, bytes.Length, 264), bytes.Length);
                return bytes.Length;
            }
            if (op == 4)
            {
                if (b < 1 || b > 65535 || !grant.AllowsAction((ushort)b) || d < 0 || d > 256) return -1;
                var bytes = new byte[(int)d];
                Marshal.Copy(Address(memory, size, c, bytes.Length, 256), bytes, 0, bytes.Length);
                return Session.RequestAction(unchecked((ulong)a), (ushort)b, bytes, actions).Success ? 0 : -1;
            }
            if (op == 5 || op == 6) return -1;
            // This experimental component-fixture route has no package I/O grants.
            if (op == 7)
            {
                Address(memory, size, a, 0, 0);
                Address(memory, size, b, 0, 0);
                return 0;
            }
            if (op == 8)
            {
                Marshal.WriteInt32(Address(memory, size, a, 4, 4), 0);
                Marshal.WriteInt32(Address(memory, size, b, 4, 4), 0);
                return 0;
            }
            if (op == 9)
            {
                if (a != 0 && a != 1) return 28;
                ulong nanos = a == 0 ? unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) * 1000000UL : unchecked((ulong)(Stopwatch.GetTimestamp() * (1000000000.0 / Stopwatch.Frequency)));
                Marshal.WriteInt64(Address(memory, size, c, 8, 8), unchecked((long)nanos));
                return 0;
            }
            return 8;

        }
        bool Call(string name, int a, int b, int nargs, int results, ulong fuel, out int value, out string error)
        {
            value = 0;
            if (terminal || disposed)
            {
                error = "Guest is terminal or disposed";
                return false;
            }
            int code = pui_call(native, name, a, b, nargs, results, fuel, out value);
            LastFuel = pui_fuel(native);
            if (code != 0 || callbackError != null)
            {
                terminal = true;
                error = callbackError ?? Marshal.PtrToStringAnsi(pui_error(native));
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
            return Evaluate(source, "portable_ui_eval", 20000000, out error);
        }
        public bool Update(out string error)
        {
            ++UpdateCalls;
            if (!Call(SupportsScheduling ? "portable_ui_update_v2" : "portable_ui_update", 0, 0, 0, 1, 20000000, out int result, out error)) return false;
            if (SupportsScheduling && result >= 0)
            {
                NextWakeTimestamp = result == 0 ? (long?)null : Stopwatch.GetTimestamp() +
                    (long)((result - 1) * (Stopwatch.Frequency / 1000.0));
                return true;
            }
            if (result == 0) return true;
            error = ReadError();
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
                pui_destroy(native);
                native = IntPtr.Zero;
                if (self.IsAllocated) self.Free();
            }
        }

    }

}

