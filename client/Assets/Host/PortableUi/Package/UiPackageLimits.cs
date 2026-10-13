using System;

namespace PortableUi.Package
{
    // Chosen by the host before loading bytes, never by an addon manifest or ID.
    // Decoded accounting covers base RGBA pixels, not transient decoder allocations.
    public sealed class UiPackageLimits
    {
        public static readonly UiPackageLimits Community = new UiPackageLimits(
            4 * 1024 * 1024, 32 * 1024 * 1024, 2048);

        public int MaxTotalBytes { get; }
        public int MaxDecodedImageBytes { get; }
        public int MaxImageDimension { get; }

        public UiPackageLimits(int maxTotalBytes, int maxDecodedImageBytes, int maxImageDimension)
        {
            if (maxTotalBytes < 1 || maxDecodedImageBytes < 4 ||
                maxImageDimension < 1 || maxImageDimension > 32768)
                throw new ArgumentOutOfRangeException(nameof(maxTotalBytes), "Asset budgets must be positive and dimensions bounded.");
            MaxTotalBytes = maxTotalBytes;
            MaxDecodedImageBytes = maxDecodedImageBytes;
            MaxImageDimension = maxImageDimension;
        }
    }
}
