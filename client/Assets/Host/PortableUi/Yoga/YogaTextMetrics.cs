using System;
using System.Collections.Generic;

namespace PortableUi.Yoga
{
    // The Unity adapter constructs this on main from the exact font instance.
    // Copies contain no font assets, Unity objects, mutable lookup references,
    // or dynamic fallback requests. Unknown characters reject the transaction.
    public sealed class YogaFontMetrics
    {
        readonly Dictionary<int, float> advances;
        readonly Dictionary<ulong, float> pairs;
        public float LineHeight { get; }
        public YogaFontMetrics(IDictionary<int, float> glyphAdvances, IDictionary<ulong, float> pairAdvances, float lineHeight)
        {
            if (glyphAdvances == null || glyphAdvances.Count > 4096 || pairAdvances == null || pairAdvances.Count > 16384 ||
                !Finite(lineHeight) || lineHeight <= 0 || lineHeight > 4096) throw new ArgumentException("Font metrics quota.");
            advances = new Dictionary<int, float>(glyphAdvances); pairs = new Dictionary<ulong, float>(pairAdvances); LineHeight = lineHeight;
            foreach (var value in advances.Values) if (!Finite(value) || value < 0 || value > 4096) throw new ArgumentException("Invalid glyph advance.");
            foreach (var value in pairs.Values) if (!Finite(value) || Math.Abs(value) > 4096) throw new ArgumentException("Invalid pair adjustment.");
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static ulong PairKey(int first, int second) => ((ulong)(uint)first << 32) | (uint)second;
        public Dictionary<int, float> CopyAdvances() => new Dictionary<int, float>(advances);
        public Dictionary<ulong, float> CopyPairs() => new Dictionary<ulong, float>(pairs);
        internal float Advance(int codepoint)
        {
            if (!advances.TryGetValue(codepoint, out float value)) throw new ArgumentException("Uncaptured text glyph U+" + codepoint.ToString("X"));
            return value;
        }
        internal float Pair(int first, int second) => pairs.TryGetValue(PairKey(first, second), out float value) ? value : 0;
    }

    public sealed class YogaTextBudget
    {
        public const int MaxGlyphSteps = 262144, MaxCalls = 1024;
        public int GlyphSteps { get; private set; }
        public int Calls { get; private set; }
        internal void Call() { if (++Calls > MaxCalls) throw new InvalidOperationException("Text measure call quota."); }
        internal void Step() { if (++GlyphSteps > MaxGlyphSteps) throw new InvalidOperationException("Text measure glyph-work quota."); }
    }

    public static class YogaTextMeasure
    {
        public static void Measure(string text, YogaFontMetrics font, YogaTextBudget budget, float width, int widthMode,
            float height, int heightMode, out float measuredWidth, out float measuredHeight)
        {
            budget.Call();
            if (text == null || text.Length > 512 || widthMode < 0 || widthMode > 2 || heightMode < 0 || heightMode > 2 ||
                (widthMode != 0 && (float.IsNaN(width) || float.IsInfinity(width) || width < 0)) ||
                (heightMode != 0 && (float.IsNaN(height) || float.IsInfinity(height) || height < 0))) throw new ArgumentException("Text measurement envelope.");
            float limit = widthMode == 0 ? float.PositiveInfinity : width;
            float line = 0, maximum = 0, word = 0, pendingSpace = 0;
            int lines = 1, previous = -1;
            // Greedy word wrap, bounded fallback within an overlong word. Space
            // advances are retained between words and excluded at line ends.
            void FlushWord()
            {
                if (word == 0) return;
                if (line > 0 && line + pendingSpace + word > limit) { maximum = Math.Max(maximum, line); ++lines; line = 0; }
                if (line > 0) line += pendingSpace;
                line += word; word = pendingSpace = 0;
            }
            for (int i = 0; i < text.Length; ++i)
            {
                budget.Step(); int code = char.ConvertToUtf32(text, i); if (code > 65535) ++i;
                if (code == '\r') continue;
                if (code == '\n') { FlushWord(); maximum = Math.Max(maximum, line); line = pendingSpace = 0; ++lines; previous = -1; continue; }
                float advance = font.Advance(code) + (previous < 0 ? 0 : font.Pair(previous, code)); previous = code;
                if (code == ' ') { FlushWord(); pendingSpace += advance; continue; }
                if (word + advance > limit && word > 0)
                {
                    // An unbreakable word wider than the constraint is broken
                    // by characters, just as TMP's enabled word wrapping does.
                    if (line > 0) { maximum = Math.Max(maximum, line); ++lines; line = 0; }
                    maximum = Math.Max(maximum, word); ++lines; word = 0; pendingSpace = 0;
                }
                word += advance;
            }
            FlushWord(); maximum = Math.Max(maximum, line);
            measuredWidth = widthMode == 1 ? width : widthMode == 2 ? Math.Min(width, maximum) : maximum;
            float naturalHeight = lines * font.LineHeight;
            measuredHeight = heightMode == 1 ? height : heightMode == 2 ? Math.Min(height, naturalHeight) : naturalHeight;
        }
    }

    public sealed class YogaTextCatalog
    {
        readonly Dictionary<string, YogaFontMetrics> fonts;
        public YogaTextCatalog(IDictionary<string, YogaFontMetrics> source)
        {
            if (source == null || source.Count > 64) throw new ArgumentException("Text style quota.");
            fonts = new Dictionary<string, YogaFontMetrics>(source, StringComparer.Ordinal);
        }
        internal YogaFontMetrics Require(string style) => fonts.TryGetValue(style, out var font) ? font : throw new ArgumentException("Uncaptured text style: " + style);
    }
}
