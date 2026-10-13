using System;
namespace PortableUi
{
    public interface IUiFuelDiagnostics { ulong LastFuel { get; } }
    public interface IUiLayoutDiagnostics
    {
        double LastLayoutMilliseconds { get; }
        double LastCoordinatorMilliseconds { get; }
        double LastForwardMilliseconds { get; }
        int LastCalculations { get; }
        int LastMeasurements { get; }
    }
    public interface IUiSchedulingDiagnostics
    {
        bool SupportsScheduling { get; }
        // Absolute Stopwatch timestamp; null means no pending jobs or timers.
        long? NextWakeTimestamp { get; }
    }
    public interface IUiGuest : IDisposable
    {
        UiSession Session { get; }
        Action<string> InitializationTrace { get; set; }
        bool Initialize(out string error);
        bool Update(out string error);
        bool SetViewport(int width, int height, out string error);
    }
}
