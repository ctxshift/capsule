using Capsule;
using System.Runtime.InteropServices;

static partial class Check
{
    sealed class SegmentTheme : IUiTheme { public bool AllowsAsset(string key) => true; public bool AllowsStyle(string key) => true; }
    sealed class SegmentGuest : IUiGuest, IUiSchedulingDiagnostics
    {
        readonly IUiObservationAdapter observations;
        readonly IUiCommitTarget output;
        public UiObservationPayload Received;
        public UiSession Session { get; } = new UiSession(1, new SegmentTheme());
        public Action<string> InitializationTrace { get; set; }
        public bool SupportsScheduling => true;
        public long? NextWakeTimestamp => null;
        public SegmentGuest(IUiObservationAdapter observations, IUiCommitTarget output) { this.observations = observations; this.output = output; }
        public bool Initialize(out string error) { error = null; return true; }
        public bool Update(out string error)
        {
            error = null;
            if (((IUiSegmentedObservationAdapter)observations).TryDequeueSegments(out _, out _, out var payload)) Received = payload;
            return output.TryCommit(new UiSnapshot(1, Array.Empty<UiNode>()), out error);
        }
        public bool SetViewport(int width, int height, out string error) { error = null; return true; }
        public void Dispose() { }
    }
    static void SegmentedWorkerCases()
    {
        using var host = new UiAsyncHostSession();
        SegmentGuest guest = null;
        long generation = host.RequestLoad((output, observations, __) => guest = new SegmentGuest(observations, output));
        void Accept()
        {
            UiAsyncCompletion completion = null;
            True(SpinWait.SpinUntil(() => host.TryGetCompletion(out completion), 3000), "bounded segmented worker completion");
            bool Commit(UiAsyncCompletion _, out string error) { error = null; return true; }
            True(host.TryApply(completion, Commit, null, out _) == UiAsyncApplyResult.Accepted, "segmented completion accepted");
        }
        Accept();
        var payload = new UiObservationPayload(new byte[] { 1, 2 }, new byte[] { 3, 4, 5 });
        True(host.EnqueueSegmentedObservation(generation, 7, 1, payload, false), "segmented observation queued");
        Accept();
        True(ReferenceEquals(payload, guest.Received) && !payload.IsMaterialized, "worker transfers segments without flattening");
        IntPtr memory = Marshal.AllocHGlobal(5);
        try { guest.Received.WriteTo(memory); var actual = new byte[5]; Marshal.Copy(memory, actual, 0, 5); True(actual.SequenceEqual(new byte[] {1,2,3,4,5}), "native copy concatenates segments exactly"); }
        finally { Marshal.FreeHGlobal(memory); }
        True(host.EnqueueOwnedObservation(generation, 7, 1, new byte[] {9}), "legacy owned observation queued");
        Accept();
        True(guest.Received.Length == 1 && guest.Received.AsContiguous()[0] == 9, "legacy observation still delivered");
        True(!host.EnqueueSegmentedObservation(generation + 1, 7, 1, payload), "wrong generation cannot queue segments");
        host.Dispose();
        True(!host.EnqueueSegmentedObservation(generation, 7, 1, payload), "disposed host rejects segments");
    }
}
