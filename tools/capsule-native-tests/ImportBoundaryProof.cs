using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Capsule;
using Capsule.Wasmtime;

static class ImportBoundaryProof
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr CreateProfile(IntPtr callback, IntPtr data, int limit);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr CreateLegacy(IntPtr callback, IntPtr data);
    static int checks;
    static void Check(bool value, string message)
    { ++checks; if (!value) throw new InvalidOperationException(message); }
    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => string.IsNullOrEmpty(key);
        public bool AllowsAsset(string key) => string.IsNullOrEmpty(key);
    }
    sealed class Host : IUiObservationAdapter, IUiActionAdapter, IUiCommitTarget
    {
        public readonly Queue<(ushort Topic, ushort Schema, byte[] Payload)> Observations = new();
        public int Dequeues, Requests;
        public bool Throw;
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] bytes)
        {
            ++Dequeues;
            if (Throw) throw new InvalidOperationException("adapter-failure");
            if (!Observations.TryDequeue(out var item)) { topic = schema = 0; bytes = null; return false; }
            topic = item.Topic; schema = item.Schema; bytes = item.Payload; return true;
        }
        public bool TryRequest(ushort capability, ulong source, byte[] payload) { ++Requests; return true; }
        public bool TryCommit(UiSnapshot snapshot, out string error) { error = null; return true; }
    }
    sealed class Memory : IDisposable
    {
        readonly WasmtimeUiGuest guest;
        readonly IntPtr memory = Marshal.AllocHGlobal(512);
        static readonly MethodInfo import = typeof(WasmtimeUiGuest).GetMethod("Import", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public Memory(WasmtimeUiGuest guest) { this.guest = guest; Reset(); }
        public void Reset() => Marshal.Copy(Enumerable.Repeat((byte)0xa5, 512).ToArray(), 0, memory, 512);
        public byte[] Bytes() { var bytes = new byte[512]; Marshal.Copy(memory, bytes, 0, bytes.Length); return bytes; }
        public int Call(UiHostOperation operation, long a = 32, long b = 0, long c = 0, long d = 0) =>
            (int)import.Invoke(guest, new object[] { (int)operation, a, b, c, d, memory, 512UL })!;
        public void Dispose() => Marshal.FreeHGlobal(memory);
    }
    static byte[] Create(ulong handle, UiEventMask events = UiEventMask.Activate)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            writer.Write(handle); writer.Write(0UL); writer.Write((byte)UiNodeKind.Window);
            writer.Write(0); writer.Write(0); writer.Write(6400); writer.Write(6400);
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)0);
            writer.Write((ushort)events);
        }
        using var packet = new MemoryStream();
        using var output = new BinaryWriter(packet);
        output.Write(0x31495550u); output.Write((ushort)1); output.Write((ushort)0);
        output.Write((ushort)1); output.Write((ushort)0); output.Write((uint)(20 + payload.Length));
        output.Write((byte)UiOpcode.Create); output.Write((byte)0); output.Write((ushort)payload.Length);
        output.Write(payload.ToArray()); return packet.ToArray();
    }
    public static void Run(string carrierPath, string bridgePath)
    {
        byte[] carrier = File.ReadAllBytes(carrierPath);
        var readRegistry = new UiAddonRegistry();
        var readRegistration = readRegistry.Register("example.read-boundary", UiAddonTrust.Community,
            new UiCapabilityGrant(null, new ushort[] { 7, 8 }, null, readOnlyActionCapabilities: new ushort[] { 8 }));
        var readHost = new Host();
        using (var readGuest = new WasmtimeUiGuest(carrier, Array.Empty<byte>(), Encoding.UTF8.GetBytes("globalThis.portableUiUpdate=()=>{portableUi.requestAction(0n,8,new Uint8Array(8));};"),
            readRegistry.Activate(readRegistration), new Theme(), readHost, readHost, readHost))
        {
            Check(readGuest.Initialize(out var readError) && readGuest.Update(out readError), "Read-only request crosses real carrier without UI input: " + readError);
            Check(readHost.Requests == 1, "Read-only adapter invoked once");
            using var memory = new Memory(readGuest);
            Check(memory.Call(UiHostOperation.RequestAction, 0, 7, 0, 8) != 0 && readHost.Requests == 1, "Gameplay grant still requires a live node");
            Check(memory.Call(UiHostOperation.RequestAction, 0, 9, 0, 8) != 0 && readHost.Requests == 1, "Ungrantable reads rejected before adapter");
        }
        var registry = new UiAddonRegistry();
        var registration = registry.Register("example.import-boundary", UiAddonTrust.Community,
            new UiCapabilityGrant(new[] { new UiObservationCapability(7, 1) }, new ushort[] { 7 }, null));
        WasmtimeUiGuest Guest(Host host, string code = "globalThis.portableUiUpdate=()=>{};", int limit = 64) =>
            new(carrier, Array.Empty<byte>(), Encoding.UTF8.GetBytes(code), registry.Activate(registration),
                new Theme(), host, host, host, hostCallsPerInvocation: limit);
        var textHost = new Host();
        using (var guest = Guest(textHost, "globalThis.portableUiUpdate=()=>{const e=portableUi.nextEvent();if(e.length!==540||e[2]!==0||e[3]!==4||e[539]!==120)throw Error('Text transport truncated');};"))
        {
            Check(guest.Initialize(out var error), "Text carrier initializes: " + error);
            guest.Session.ReserveHandle(out var source);
            Check(guest.Session.Submit(Create(source, UiEventMask.TextEdit), textHost).Success, "Text event source");
            var text = new UiTextEdit(UiTextEditPhase.Change, new string('x', 512), "", 512, 512, false, true).Encode();
            Check(guest.Session.QueueEvent(source, UiEventMask.TextEdit, text).Success, "Text event queued");
            Check(guest.Update(out error), "Full text reaches actual QuickJS: " + error);
            Check(guest.Session.PendingEventCount == 0, "Text event consumed once");
        }
        foreach (int invalid in new[] { -1, 0, 63, 65, 255, 257, int.MaxValue })
        {
            bool rejected = false;
            try { using var bad = Guest(new Host(), limit: invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid managed host profile rejected: " + invalid);
        }
        string Calls(int count) => $"globalThis.portableUiUpdate=()=>{{for(let i=0;i<{count};i++)portableUi.nextEvent();}};";
        foreach (int limit in new[] { 64, 256 })
        {
            using var bounded = Guest(new Host(), Calls(limit), limit);
            using var excessive = Guest(new Host(), Calls(limit + 1), limit);
            Check(bounded.Initialize(out var error), "Boundary initializes: " + error);
            Check(excessive.Initialize(out error), "Overflow initializes: " + error);
            for (int i = 0; i < 2; i++)
            {
                Check(bounded.Update(out error), "Exact allowance succeeds and resets: " + error);
                Check(bounded.LastHostCalls.Calls == limit && bounded.LastHostCalls.QuotaRejections == 0, "Exact callback accounting");
            }
            Check(!excessive.Update(out error) && error.Contains("host call quota"), "Allowance plus one traps");
            Check(excessive.LastHostCalls.Calls == limit && excessive.LastHostCalls.Failures == 0 && excessive.LastHostCalls.QuotaRejections == 1,
                "Predispatch rejection measured separately from callbacks");
            Check(bounded.Update(out error), "Other instance survives overflow: " + error);
            Check(!excessive.Update(out _) && excessive.TotalHostCalls.QuotaRejections == 1, "Terminal retry does not charge again");
        }
        using (var community = Guest(new Host(), Calls(65)))
        using (var core = Guest(new Host(), Calls(256), 256))
        {
            Check(community.Initialize(out var error) && core.Initialize(out error), "Mixed profiles initialize: " + error);
            Check(!community.Update(out _) && core.Update(out error), "Extended guest cannot elevate default peer: " + error);
            Check(community.HostCallsPerInvocation == 64 && core.HostCallsPerInvocation == 256, "Host selections remain per instance");
        }
        IntPtr library = NativeLibrary.Load(bridgePath);
        try
        {
            var create = Marshal.GetDelegateForFunctionPointer<CreateProfile>(NativeLibrary.GetExport(library, "pui_create_with_host_call_limit"));
            foreach (int invalid in new[] { -1, 0, 63, 65, 255, 257, int.MaxValue })
                Check(create(IntPtr.Zero, IntPtr.Zero, invalid) == IntPtr.Zero, "Native ABI rejects invalid profile before allocation");
            using var legacy = Guest(new Host(), Calls(65));
            // Exercise the retained legacy ABI with the same rooted production callback.
            var guestType = typeof(WasmtimeUiGuest);
            var nativeField = guestType.GetField("native", BindingFlags.Instance | BindingFlags.NonPublic)!;
            guestType.GetMethod("pui_destroy", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { nativeField.GetValue(legacy) });
            nativeField.SetValue(legacy, IntPtr.Zero);
            var callback = (Delegate)guestType.GetField("Callback", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var handle = (GCHandle)guestType.GetField("self", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(legacy)!;
            var createLegacy = Marshal.GetDelegateForFunctionPointer<CreateLegacy>(NativeLibrary.GetExport(library, "pui_create"));
            IntPtr pointer = createLegacy(Marshal.GetFunctionPointerForDelegate(callback), GCHandle.ToIntPtr(handle));
            nativeField.SetValue(legacy, pointer);
            Check(pointer != IntPtr.Zero && legacy.Initialize(out _), "Legacy ABI initializes");
            Check(!legacy.Update(out var error) && error.Contains("host call quota") && legacy.LastHostCalls.Calls == 64,
                "Legacy ABI remains exactly64 after extended instances");
        }
        finally { NativeLibrary.Free(library); }
        var host = new Host();
        using (var guest = Guest(host))
        using (var memory = new Memory(guest))
        {
            Check(guest.Initialize(out var error), "Actual native initialization: " + error);
            Check(guest.Session.ReserveHandle(out ulong handle).Success, "Reserve event source");
            Check(guest.Session.Submit(Create(handle), host).Success, "Commit actual event source");
            Check(guest.Session.QueueEvent(handle, UiEventMask.Activate, Array.Empty<byte>()).Success, "Queue input");
            host.Observations.Enqueue((7, 1, new byte[] { 9, 8, 7 }));
            var before = guest.TotalHostCalls;
            foreach (long size in new long[] { -1, 0, 1, 15, 271, 273, 4294967568 })
            {
                Check(memory.Call(UiHostOperation.NextEvent, b: size) == -2, "Short/noncanonical event capacity rejected");
                Check(guest.Session.TryPeekEvent(out _), "Rejected event capacity never dequeues");
                Check(memory.Bytes().All(value => value == 0xa5), "Rejected capacity preserves all guard bytes");
            }
            foreach (long size in new long[] { -1, 7, 263, 265, 4294967560 })
            {
                Check(memory.Call(UiHostOperation.NextObservation, b: size) == -2, "Short/noncanonical observation capacity rejected");
                Check(host.Dequeues == 0 && host.Observations.Count == 1, "Rejected capacity never calls adapter");
            }
            Check(memory.Call(UiHostOperation.NextObservation, a: 500, b: 264) == -2 && host.Dequeues == 0,
                "Linear memory bounds checked before dequeue");
            var rejected = guest.TotalHostCalls - before;
            Check(rejected.BytesRead == 0 && rejected.BytesWritten == 0 && rejected.Calls == rejected.Failures,
                "Rejected memory copies excluded from byte metrics");
            Check(memory.Call(UiHostOperation.NextEvent, b: 272) == 16, "Valid event delivered after short-buffer retries");
            Check(!guest.Session.TryPeekEvent(out _), "Event dequeued exactly once");
            Check(memory.Bytes()[48] == 0xa5 && memory.Bytes()[31] == 0xa5, "Event copy stops at actual length");
            memory.Reset();
            Check(memory.Call(UiHostOperation.NextObservation, b: 264) == 11, "Observation delivered after retries");
            var bytes = memory.Bytes();
            Check(bytes[32] == 7 && bytes[34] == 1 && bytes[36] == 3 && bytes[40] == 9 && bytes[43] == 0xa5,
                "Observation header/payload and trailing guard correct");
            Check(memory.Call(UiHostOperation.NextObservation, b: 264) == 0, "Empty observation result");
            Check(memory.Call(UiHostOperation.RequestAction, (long)handle, 8, 500, 30) == -1 && host.Requests == 0,
                "Denied grant rejects before payload copy");
            before = guest.TotalHostCalls;
            Check(memory.Call(UiHostOperation.RequestAction, (long)handle, 7, 32, 3) == 0 && host.Requests == 1,
                "Granted live source forwards copied intent");
            Check((guest.TotalHostCalls - before).BytesRead == 3, "Accepted action copy counted");
            Check(guest.Session.QueueEvent(handle, UiEventMask.Activate, Array.Empty<byte>()).Success, "Queue before reload");
            guest.Session.Unload(host);
            Check(memory.Call(UiHostOperation.NextEvent, b: 272) == 0, "Reload retires stale queued source");
            before = guest.TotalHostCalls;
            Check(memory.Call(UiHostOperation.RequestAction, (long)handle, 7, 32, 3) == -1 && host.Requests == 1,
                "Stale source rejected after copied payload validation");
            Check((guest.TotalHostCalls - before).BytesRead == 3, "Copied but rejected request still counted");
            Check(memory.Call(UiHostOperation.ReadData) == -3 && memory.Call(UiHostOperation.AcquireImage) == -3,
                "Unsupported package operations have explicit result");
            host.Observations.Enqueue((8, 1, new byte[1]));
            Check(memory.Call(UiHostOperation.NextObservation, b: 264) == -1, "Denied observation not exposed");
            host.Observations.Enqueue((7, 1, new byte[257]));
            Check(memory.Call(UiHostOperation.NextObservation, b: 264) == -2, "Oversized observation rejected");
            Check(guest.ReadHostCalls(UiHostOperation.NextEvent).BytesWritten == 16, "Per-operation copied bytes");
            before = guest.TotalHostCalls;
            Check(memory.Call((UiHostOperation)8, a: 510, b: 32) == 21, "WASI memory error retains FAULT errno domain");
            Check(memory.Call((UiHostOperation)9, a: 2) == 28, "WASI invalid clock retains INVAL errno domain");
            var wasi = guest.TotalHostCalls - before;
            Check(wasi.Calls == 2 && wasi.Failures == 2 && wasi.BytesWritten == 0,
                "WASI rejected callbacks counted without copied bytes");
            guest.Dispose();
            Check(memory.Call(UiHostOperation.Reserve) == -5, "Disposed guest cannot reserve");
            Check(guest.TotalHostCalls.ElapsedTicks > 0, "Callback time includes rejected work");
        }
        using (var guest = Guest(new Host(), "globalThis.portableUiUpdate=()=>{for(let i=0;i<65;i++)portableUi.nextEvent();};"))
        {
            Check(guest.Initialize(out var error), "Quota guest initializes: " + error);
            Check(!guest.Update(out error) && error.Contains("host call quota"), "Actual native bridge rejects invocation65");
            Check(guest.LastHostCalls.Calls == 64 && guest.LastHostCalls.Failures == 0,
                "Predispatch quota trap is excluded from managed callback failures");
            var total = guest.TotalHostCalls;
            Check(!guest.Update(out _), "Terminal guest rejects subsequent update");
            Check(guest.TotalHostCalls.Calls == total.Calls, "Terminal retry does not execute callbacks");
        }
        using (var guest = Guest(new Host()))
        using (var memory = new Memory(guest))
        {
            Check(guest.Initialize(out var error), "Malformed submit guest initializes: " + error);
            var before = guest.TotalHostCalls;
            Check(memory.Call(UiHostOperation.Submit, b: 65537) == -2, "Oversized submit rejected before copying");
            Check((guest.TotalHostCalls - before).BytesRead == 0, "Oversized submit copies no bytes");
            Check(memory.Call(UiHostOperation.Submit, b: 16) == -1, "Malformed copied packet rejected");
            Check((guest.TotalHostCalls - before).BytesRead == 16, "Malformed packet actual copy counted");
            Check(!guest.Update(out _), "Malformed submit makes guest terminal");
        }
        using (var guest = Guest(new Host { Throw = true }))
        using (var memory = new Memory(guest))
        {
            Check(guest.Initialize(out var error), "Exception guest initializes: " + error);
            var before = guest.TotalHostCalls;
            Check(memory.Call(UiHostOperation.NextObservation, b: 264) == -6, "Adapter exception becomes internal result");
            var delta = guest.TotalHostCalls - before;
            Check(delta.Calls == 1 && delta.Failures == 1 && delta.ElapsedTicks > 0, "Exception measured in finally");
            Check(!guest.Update(out error) && error.EndsWith("adapter-failure"), "Internal callback failure retires guest: " + error);
        }
        Console.WriteLine($"PASS native import boundary: {checks} checks");
    }
}
