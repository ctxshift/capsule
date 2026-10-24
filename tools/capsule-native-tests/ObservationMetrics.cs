using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Capsule;
using Capsule.Wasmtime;

static partial class WholeObservationProof
{
    static void Measure(string carrierPath, string output)
    {
        const int warmup = 20, rounds = 200;
        var results = new List<object>();
        foreach (int payloadBytes in new[] { 555, 23903 })
        foreach (bool segmented in new[] { false, true })
        {
            var host = new Host();
            var registry = new UiAddonRegistry();
            var registration = registry.Register("example.observation-metrics", UiAddonTrust.Community,
                new UiCapabilityGrant(new[] {new UiObservationCapability(7, 1)}, Array.Empty<ushort>(), null));
            string script = "globalThis.portableUiUpdate=()=>{const b=portableUi.nextObservation();if(b.length!==" + (payloadBytes + 16) + ")throw Error('length');if(b[16]!==7||b[b.length-1]!==7)throw Error('payload');};";
            using var guest = new WasmtimeUiGuest(File.ReadAllBytes(carrierPath), Array.Empty<byte>(), Encoding.UTF8.GetBytes(script), registry.Activate(registration), host, host, host, host, executionFuel: ulong.MaxValue);
            Check(guest.Initialize(out var error), "Initialize metrics: " + error);
            var body = Enumerable.Repeat((byte)7, payloadBytes).ToArray();
            var times = new double[rounds];
            long allocated = 0, sandboxBytes = 0;
            int[] collections = new int[3];
            for (int i = -warmup; i < rounds; i++)
            {
                if (i == 0) for (int generation = 0; generation < 3; generation++) collections[generation] = GC.CollectionCount(generation);
                long allocationStart = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var observation = new UiObservationPayload(new byte[8], body);
                if (segmented) host.Segments = observation; else host.Pending = observation.AsContiguous();
                Check(guest.Update(out error), "Native observation: " + error);
                Check(guest.LastHostCalls.BytesWritten == payloadBytes + 16, "Exactly one sandbox payload write");
                if (i >= 0)
                {
                    times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    allocated += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                    sandboxBytes += guest.LastHostCalls.BytesWritten;
                }
                Check(!segmented || !observation.IsMaterialized, "No intermediate flattening");
            }
            for (int generation = 0; generation < 3; generation++) collections[generation] = GC.CollectionCount(generation) - collections[generation];
            Array.Sort(times);
            results.Add(new { payloadBytes, segmented, medianMs = times[rounds / 2], p95Ms = times[(int)(rounds * .95)], managedBytesPerUpdate = allocated / rounds, sandboxBytesPerUpdate = sandboxBytes / rounds, managedCollections = collections });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { warmup, rounds, scope = "Single-addon header framing, real Wasm carrier transfer and guest JavaScript receipt. Excludes contract encode/decode, worker queue, render and network. Managed allocation/GC counters exclude unmanaged QuickJS/Wasm heap.", results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS native observation metrics: " + output);
    }
}
