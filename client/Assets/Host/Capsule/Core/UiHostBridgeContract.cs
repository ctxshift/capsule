using System;
using System.Diagnostics;

namespace Capsule
{
    // portable_ui_v1 operation IDs. Availability never conveys a capability grant.
    public enum UiHostOperation
    {
        Reserve = 0, Submit = 1, NextEvent = 2, NextObservation = 3,
        RequestAction = 4, ReadData = 5, AcquireImage = 6
    }

    // Capsule results only; WASI operations retain their separate errno contract.
    public enum UiHostResult
    {
        Success = 0, Rejected = -1, InvalidArgument = -2, Unsupported = -3,
        QuotaExceeded = -4, Unavailable = -5, Internal = -6
    }

    public static class UiHostBridgeContract
    {
        public const int MaxCallsPerInvocation = 64;
        public const int ExtendedCallsPerInvocation = 2048;
        public const int MaxSubmitBytes = 65536;
        public const int EventCapacity = 272;
        public const int TextEventCapacity = 16 + UiTextEdit.MaxPayloadBytes;
        public const int ObservationCapacity = 264;
        // Carrier linear-memory capacity, not a per-feature or community topic quota.
        public const int MaxObservationBytes = 4 * 1024 * 1024;
        // Linear-memory safety ceiling; generated feature codecs enforce member-specific bounds.
        public const int MaxPayloadBytes = 4194304;
    }

    // Actual managed callback work, including failures, but excluding the C bridge's
    // pre-dispatch quota trap, recorded separately in QuotaRejections. Bytes count
    // only completed copies across guest memory. Per-operation metrics exclude traps.
    public readonly struct UiHostCallMetrics
    {
        public readonly long Calls, Failures, BytesRead, BytesWritten, ElapsedTicks, QuotaRejections;
        public double Milliseconds => ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        public UiHostCallMetrics(long calls, long failures, long read, long written, long ticks, long quotaRejections = 0)
        { Calls = calls; Failures = failures; BytesRead = read; BytesWritten = written; ElapsedTicks = ticks; QuotaRejections = quotaRejections; }
        public static UiHostCallMetrics operator +(UiHostCallMetrics a, UiHostCallMetrics b) =>
            new UiHostCallMetrics(a.Calls + b.Calls, a.Failures + b.Failures,
                a.BytesRead + b.BytesRead, a.BytesWritten + b.BytesWritten, a.ElapsedTicks + b.ElapsedTicks, a.QuotaRejections + b.QuotaRejections);
        public static UiHostCallMetrics operator -(UiHostCallMetrics a, UiHostCallMetrics b) =>
            new UiHostCallMetrics(a.Calls - b.Calls, a.Failures - b.Failures,
                a.BytesRead - b.BytesRead, a.BytesWritten - b.BytesWritten, a.ElapsedTicks - b.ElapsedTicks, a.QuotaRejections - b.QuotaRejections);
    }

    public interface IUiHostCallDiagnostics
    {
        bool HasHostCallMetrics { get; }
        UiHostCallMetrics LastHostCalls { get; }
        UiHostCallMetrics TotalHostCalls { get; }
    }
}
