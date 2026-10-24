using System.Text;
using Capsule.Package;

static partial class Check
{
    static int passed;
    static void True(bool value, string name)
    {
        if (!value) throw new Exception(name);
        passed++;
    }

    static byte[] Json(string s) => Encoding.UTF8.GetBytes(s);
    static byte[] Png(int width = 8, int height = 8)
    {
        var b = new byte[33];
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        signature.CopyTo(b, 0);
        b[11] = 13; b[12] = (byte)'I'; b[13] = (byte)'H';
        b[14] = (byte)'D'; b[15] = (byte)'R';
        for (int i = 0; i < 4; ++i) { b[16 + i] = (byte)(width >> (24 - i * 8)); b[20 + i] = (byte)(height >> (24 - i * 8)); }
        return b;
    }

    sealed class FakeDecoder : IUiImageDecoder
    {
        public int Calls;
        public FakeImage Last;
        public IUiDecodedImage Decode(byte[] encoded, UiPackageImageDescriptor expected)
        {
            Calls++;
            return Last = new FakeImage(expected);
        }
    }
    sealed class FakeImage : IUiDecodedImage
    {
        readonly UiPackageImageDescriptor descriptor;
        public FakeImage(UiPackageImageDescriptor d) { descriptor = d; }
        public int Width => descriptor.Width;
        public int Height => descriptor.Height;
        public long ResidentBytes => descriptor.DecodedBytes;
        public bool Disposed;
        public void Dispose() { Disposed = true; }
    }

}
