using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Capsule;
using Capsule.Package;

static partial class Check
{
    sealed class MetricsProvider : UiFeatureProvider
    {
        public MetricsProvider() : base(new UiFeatureDefinition("@example/metrics", "1.0.0", new string('d', 64), Array.Empty<UiFeatureDependency>(), new[] { "read" })) { AddState("State", "read", new byte[1]); }
        public void Update(byte[] bytes) => Publish("State", bytes);
    }
    static void TransportMetrics(string output)
    {
        var results = new List<object>();
        foreach (int length in new[] { 555, 23903 })
        foreach (int count in new[] { 1, 8 })
        foreach (bool blocked in new[] { false, true })
        foreach (bool segmented in new[] { false, true })
        {
            using var provider = new MetricsProvider();
            var registry = new UiFeatureRegistry();
            using var registration = registry.Register(provider.Definition, provider);
            var requirement = new UiFeatureRequirement(provider.Definition.Name, "1.0.*", "1.0.0", provider.Definition.Fingerprint, false, new[] { "read" });
            var selection = registry.Select(new[] { requirement }, new[] { new UiFeatureGrant(provider.Definition.Name, "read") });
            var sessions = Enumerable.Range(1, count).Select(i => new UiFeatureSession(selection, (uint)i)).ToArray();
            IntPtr destination = Marshal.AllocHGlobal(length + 4096);
            Func<ushort, byte[], bool> accept = (_, bytes) => { Marshal.Copy(bytes, 0, destination, bytes.Length); return true; };
            Func<ushort, UiObservationPayload, bool> acceptSegments = (_, bytes) => { bytes.WriteTo(destination); return true; };
            Func<ushort, UiObservationPayload, bool> rejectSegments = (_, _) => false;
            Func<ushort, byte[], bool> reject = (_, _) => false;
            void Pump(UiFeatureSession session, bool rejectFrame) { if (segmented) session.PumpSegments(rejectFrame ? rejectSegments : acceptSegments); else session.Pump(rejectFrame ? reject : accept); }
            foreach (var session in sessions) Pump(session, false);
            var payload = new byte[length];
            var times = new double[1000];
            long allocated = 0;
            for (int i = -100; i < times.Length; i++)
            {
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                provider.Update(payload);
                foreach (var session in sessions)
                {
                    if (blocked) { Pump(session, true); Pump(session, true); }
                    Pump(session, false);
                }
                if (i >= 0) { times[i] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency; allocated += GC.GetAllocatedBytesForCurrentThread() - bytesBefore; }
            }
            Array.Sort(times);
            results.Add(new { segmented, payloadBytes = length, addonSessions = count, rejectedAttempts = blocked ? 2 : 0, medianMs = times[500], p95Ms = times[950], allocatedBytesPerPublication = allocated / times.Length });
            foreach (var session in sessions) session.Dispose();
            Marshal.FreeHGlobal(destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { scope = "Native provider fanout, session framing and copy to unmanaged memory; excludes codec, worker scheduling, guest execution, rendering and network. Identical payloads for contiguous and segmented paths.", runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Wrote transport metrics: " + output);
    }
}
