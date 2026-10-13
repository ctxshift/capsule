using System;

namespace PortableUi.Package
{
    public enum UiPackageImageFormat { Png, Jpeg }

    public sealed class UiPackageImageDescriptor
    {
        public UiPackageImageFormat Format { get; }
        public int Width { get; }
        public int Height { get; }
        public int DecodedBytes { get; }
        internal UiPackageImageDescriptor(UiPackageImageFormat format, int width, int height)
        {
            Format = format; Width = width; Height = height;
            DecodedBytes = checked(width * height * 4);
        }

        public static bool TryInspect(byte[] bytes, string path, out UiPackageImageDescriptor image)
            => TryInspect(bytes, path, UiPackageLimits.Community, out image);

        public static bool TryInspect(byte[] bytes, string path, UiPackageLimits limits,
            out UiPackageImageDescriptor image)
        {
            image = null;
            if (bytes == null || limits == null) return false;
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".png") return TryPng(bytes, limits, out image);
            if (extension == ".jpg" || extension == ".jpeg") return TryJpeg(bytes, limits, out image);
            return false;
        }

        static bool Dimensions(UiPackageImageFormat format, int width, int height,
            UiPackageLimits limits, out UiPackageImageDescriptor image)
        {
            image = null;
            if (width < 1 || height < 1 || width > limits.MaxImageDimension || height > limits.MaxImageDimension ||
                (long)width * height * 4 > 16 * 1024 * 1024) return false;
            image = new UiPackageImageDescriptor(format, width, height);
            return true;
        }

        static int Be32(byte[] b, int at) =>
            (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        static int Be16(byte[] b, int at) => (b[at] << 8) | b[at + 1];

        static bool TryPng(byte[] b, UiPackageLimits limits, out UiPackageImageDescriptor image)
        {
            image = null;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (b.Length < 33) return false;
            for (int i = 0; i < signature.Length; i++)
                if (b[i] != signature[i]) return false;
            if (Be32(b, 8) != 13 || b[12] != 'I' || b[13] != 'H' ||
                b[14] != 'D' || b[15] != 'R') return false;
            return Dimensions(UiPackageImageFormat.Png, Be32(b, 16), Be32(b, 20), limits, out image);
        }

        static bool TryJpeg(byte[] b, UiPackageLimits limits, out UiPackageImageDescriptor image)
        {
            image = null;
            if (b.Length < 12 || b[0] != 0xFF || b[1] != 0xD8) return false;
            int at = 2;
            while (at + 3 < b.Length)
            {
                if (b[at++] != 0xFF) return false;
                while (at < b.Length && b[at] == 0xFF) at++;
                if (at >= b.Length) return false;
                byte marker = b[at++];
                if (marker == 0xD9 || marker == 0xDA) return false;
                if (at + 2 > b.Length) return false;
                int length = Be16(b, at);
                if (length < 2 || length > b.Length - at) return false;
                if ((marker >= 0xC0 && marker <= 0xC3) ||
                    (marker >= 0xC5 && marker <= 0xC7) ||
                    (marker >= 0xC9 && marker <= 0xCB) ||
                    (marker >= 0xCD && marker <= 0xCF))
                {
                    if (length < 7) return false;
                    return Dimensions(UiPackageImageFormat.Jpeg,
                        Be16(b, at + 5), Be16(b, at + 3), limits, out image);
                }
                at += length;
            }
            return false;
        }
    }
}
