using System;

namespace PortableUi.Yoga
{
    // Opt-in worker-owned decorator. The transport remains responsible for final
    // main-thread acceptance and retires the guest if native presentation fails.
    public sealed class YogaUiGuest : IUiGuest, IUiFuelDiagnostics, IUiCallFuelDiagnostics, IUiHostCallDiagnostics, IUiSchedulingDiagnostics, IUiLayoutDiagnostics, IUiCommitTarget
    {
        readonly IUiGuest inner;
        readonly IUiCommitTarget output;
        readonly YogaCoordinator coordinator;
        readonly uint owner;
        long revision;
        bool disposed;
        public double LastLayoutMilliseconds { get; private set; }
        public double LastCoordinatorMilliseconds { get; private set; }
        public double LastForwardMilliseconds { get; private set; }
        public int LastCalculations { get; private set; }
        public int LastMeasurements { get; private set; }
        public YogaUiGuest(uint owner, IUiTheme theme, YogaTextCatalog metrics, IUiCommitTarget output, Func<IUiCommitTarget, IUiGuest> factory, int layoutNodeLimit = UiSession.DefaultLayoutNodes)
        {
            this.owner = owner; this.output = output;
            coordinator = new YogaCoordinator(metrics); coordinator.Register(owner, theme, layoutNodeLimit);
            try { inner = factory(this); }
            catch { coordinator.Dispose(); throw; }
        }
        public UiSession Session => inner.Session;
        public Action<string> InitializationTrace { get => inner.InitializationTrace; set => inner.InitializationTrace = value; }
        public ulong LastFuel => (inner as IUiFuelDiagnostics)?.LastFuel ?? 0;
        public ulong TotalCallFuel => (inner as IUiCallFuelDiagnostics)?.TotalCallFuel ?? 0;
        public bool HasCallFuel => (inner as IUiCallFuelDiagnostics)?.HasCallFuel ?? false;
        public bool HasHostCallMetrics => (inner as IUiHostCallDiagnostics)?.HasHostCallMetrics ?? false;
        public UiHostCallMetrics LastHostCalls => (inner as IUiHostCallDiagnostics)?.LastHostCalls ?? default;
        public UiHostCallMetrics TotalHostCalls => (inner as IUiHostCallDiagnostics)?.TotalHostCalls ?? default;
        public bool SupportsScheduling => (inner as IUiSchedulingDiagnostics)?.SupportsScheduling ?? false;
        public long? NextWakeTimestamp => (inner as IUiSchedulingDiagnostics)?.NextWakeTimestamp;
        public bool Initialize(out string error) => inner.Initialize(out error);
        public bool Update(out string error)
        {
            LastLayoutMilliseconds = LastCoordinatorMilliseconds = LastForwardMilliseconds = 0;
            LastCalculations = LastMeasurements = 0;
            return inner.Update(out error);
        }
        public bool SetViewport(int width, int height, out string error) => inner.SetViewport(width, height, out error);
        public bool TryCommit(UiSnapshot snapshot, out string error)
        {
            error = null;
            if (disposed) { error = "Yoga guest disposed."; return false; }
            long next = 0; bool presented = false, acknowledged = false;
            try
            {
                next = checked(++revision);
                if (!coordinator.Enqueue(owner, next, snapshot, YogaCoordinator.ReadIntent(snapshot), out error)) return false;
                var results = coordinator.Drain();
                if (results.Length != 1) throw new InvalidOperationException("Yoga guest transaction boundary.");
                var result = results[0];
                presented = true;
                LastLayoutMilliseconds += result.LayoutMilliseconds; LastCoordinatorMilliseconds += result.CoordinatorMilliseconds;
                LastCalculations += result.Calculations; LastMeasurements += result.Measurements;
                if (result.Error != null) { error = result.Error; coordinator.Acknowledge(owner, next, false); acknowledged = true; return false; }
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                bool accepted = output.TryCommit(result.Snapshot, out error);
                LastForwardMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                coordinator.Acknowledge(owner, next, accepted);
                acknowledged = true;
                return accepted;
            }
            catch (Exception failure) { error = failure.Message; return false; }
            finally
            {
                if (presented && !acknowledged) coordinator.Acknowledge(owner, next, false);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { inner.Dispose(); } finally { coordinator.Dispose(); }
        }
    }
}
