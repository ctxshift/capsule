using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Capsule;
using Capsule.Wasmtime;

static partial class WholeObservationProof
{
    sealed class Host : IUiSegmentedObservationAdapter, IUiActionAdapter, IUiCommitTarget, IUiTheme
    {
        public byte[] Pending;
        public UiObservationPayload Segments;
        public bool TryDequeueSegments(out ushort topic, out ushort schema, out UiObservationPayload payload)
        { topic = 7; schema = 1; payload = Segments; Segments = null; if(payload != null) { Reads++; return true; } if (!TryDequeue(out topic, out schema, out var bytes)) return false; payload = UiObservationPayload.Owned(bytes); return true; }
        public int Reads;
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] payload)
        { topic = 7; schema = 1; payload = Pending; Pending = null; if (payload != null) Reads++; return payload != null; }
        public bool TryRequest(ushort capability, ulong sourceHandle, byte[] payload) => false;
        public bool TryCommit(UiSnapshot snapshot, out string error) { error = null; return true; }
        public bool AllowsAsset(string key) => true;
        public bool AllowsStyle(string key) => true;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string carrierPath)
    {
        string metricsPath = Environment.GetEnvironmentVariable("CAPSULE_OBSERVATION_METRICS");
        if (!string.IsNullOrEmpty(metricsPath)) { Measure(carrierPath, metricsPath); return; }
        var host = new Host();
        var registry = new UiAddonRegistry();
        var registration = registry.Register("example.whole-observations", UiAddonTrust.Community,
            new UiCapabilityGrant(new[] { new UiObservationCapability(7, 1) }, Array.Empty<ushort>(), null));
        string script = "globalThis.portableUiUpdate=()=>{const b=portableUi.nextObservation();if(b.length!==100008)throw Error('length');const v=new DataView(b.buffer,b.byteOffset,b.byteLength);if(v.getUint32(4,true)!==100000)throw Error('envelope');for(let i=8;i<b.length;i++)if(b[i]!==((i-8)%251))throw Error('payload');if(portableUi.nextObservation().length)throw Error('duplicate');};";
        using var guest = new WasmtimeUiGuest(File.ReadAllBytes(carrierPath), Array.Empty<byte>(), Encoding.UTF8.GetBytes(script), registry.Activate(registration), host, host, host, host, executionFuel: ulong.MaxValue);
        Check(guest.Initialize(out var error), "Initialize: " + error);
        host.Pending = Enumerable.Range(0, 100000).Select(index => (byte)(index % 251)).ToArray();
        Check(guest.Update(out error), "Whole payload through actual carrier: " + error);
        Check(host.Reads == 1, "Consumed once");
        Check(guest.LastHostCalls.BytesWritten == 100008, "Exactly one payload copy across sandbox boundary");
        var segmented = new UiObservationPayload(Enumerable.Range(0, 8).Select(i => (byte)i).ToArray(), Enumerable.Range(8, 99992).Select(i => (byte)(i % 251)).ToArray());
        host.Segments = segmented;
        Check(guest.Update(out error), "Segmented payload through actual carrier: " + error);
        Check(!segmented.IsMaterialized && host.Reads == 2 && guest.LastHostCalls.BytesWritten == 100008, "Segments reach Wasm without intermediate payload copy");
        // A queried record survives a legacy short buffer, without a second dequeue.
        host.Segments = new UiObservationPayload(new byte[8], new byte[992]);
        var import = typeof(WasmtimeUiGuest).GetMethod("Import", BindingFlags.NonPublic | BindingFlags.Instance);
        IntPtr memory = Marshal.AllocHGlobal(2048);
        try
        {
            int Call(long capacity) => (int)import.Invoke(guest, new object[] { 3, 0L, capacity, 0L, 0L, memory, 2048UL });
            Check(Call(0) == 1008 && Call(0) == 1008, "Stable length query");
            Check(Call(264) == -2, "Short buffer rejects without loss");
            Check(Call(1008) == 1008 && host.Reads == 3, "Retry consumes retained record once");
            Check(Call(0) == 0, "Empty after delivery");
        }
        finally { Marshal.FreeHGlobal(memory); }
        Console.WriteLine("PASS contiguous/segmented whole observations: 100000 payload bytes, 100008 sandbox bytes written, zero intermediate envelope copies, short-buffer retention");
    }
}
