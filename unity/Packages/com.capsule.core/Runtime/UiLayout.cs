using System;

namespace Capsule
{
    // Version-one layout intent carried by packet v2. Values are 1/64 units;
    // only dimensions accept -1 (auto). No browser CSS or platform objects.
    public sealed class UiLayout
    {
        public byte Row { get; }
        public byte Absolute { get; }
        public byte Align { get; }
        public byte Justify { get; }
        public int Width { get; }
        public int Height { get; }
        public int Left { get; }
        public int Top { get; }
        public int Grow { get; }
        public int Shrink { get; }
        public int Gap { get; }
        public int PaddingLeft { get; }
        public int PaddingTop { get; }
        public int PaddingRight { get; }
        public int PaddingBottom { get; }
        public UiLayout(byte row, byte absolute, byte align, byte justify, int width, int height,
            int left, int top, int grow, int shrink, int gap, int paddingLeft, int paddingTop, int paddingRight, int paddingBottom)
        {
            Row = row; Absolute = absolute; Align = align; Justify = justify; Width = width; Height = height;
            Left = left; Top = top; Grow = grow; Shrink = shrink; Gap = gap;
            PaddingLeft = paddingLeft; PaddingTop = paddingTop; PaddingRight = paddingRight; PaddingBottom = paddingBottom;
            if (!Valid()) throw new ArgumentException("Layout intent outside bounded subset.");
        }
        public bool Valid() => Row <= 1 && Absolute <= 1 && Align >= 1 && Align <= 4 && Justify <= 3 &&
            Width >= -1 && Width <= 4096 * 64 && Height >= -1 && Height <= 4096 * 64 &&
            Left >= -8192 * 64 && Left <= 8192 * 64 && Top >= -8192 * 64 && Top <= 8192 * 64 &&
            Grow >= 0 && Grow <= 100 * 64 && Shrink >= 0 && Shrink <= 100 * 64 &&
            Nonnegative(Gap) && Nonnegative(PaddingLeft) && Nonnegative(PaddingTop) && Nonnegative(PaddingRight) && Nonnegative(PaddingBottom);
        static bool Nonnegative(int value) => value >= 0 && value <= 4096 * 64;
    }
}
