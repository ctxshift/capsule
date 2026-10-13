using System;
using System.Collections.Generic;
using PortableUi.Yoga;
using TMPro;

namespace PortableUi.Unity
{
    public static class PortableUiFontMetrics
    {
        // Call only on Unity main. Glyph population is explicit and completed
        // before immutable metrics leave this boundary; fallback is not implied.
        public static YogaFontMetrics Capture(TMP_FontAsset font, string characters, float size, float lineHeight)
        {
            if (!font || characters == null || characters.Length > 4096 || size <= 0 || size > 256)
                throw new ArgumentException("Font capture bounds.");
            // TMP returns false (and echoes the whole string as "missing") when
            // every requested glyph already exists. Populate only absent glyphs,
            // then verify the actual lookup table instead of that return flag.
            var pending = new System.Text.StringBuilder();
            for(int i=0;i<characters.Length;i++)
            {
                int code=char.ConvertToUtf32(characters,i);if(code>65535)++i;
                if(!font.characterLookupTable.ContainsKey((uint)code))pending.Append(char.ConvertFromUtf32(code));
            }
            if(pending.Length!=0)font.TryAddCharacters(pending.ToString(),out _,true);
            float scale = size / font.faceInfo.pointSize * font.faceInfo.scale;
            var advances = new Dictionary<int, float>();
            var glyphs = new Dictionary<uint, List<int>>();
            for (int i = 0; i < characters.Length; ++i)
            {
                int code = char.ConvertToUtf32(characters, i); if (code > 65535) ++i;
                if (advances.ContainsKey(code)) continue;
                if (!font.characterLookupTable.TryGetValue((uint)code, out var character)) throw new InvalidOperationException("Captured glyph unavailable: U+"+code.ToString("X"));
                advances.Add(code, character.glyph.metrics.horizontalAdvance * scale * character.scale);
                if (!glyphs.TryGetValue(character.glyphIndex, out var codes)) glyphs.Add(character.glyphIndex, codes = new List<int>());
                codes.Add(code);
            }
            var pairs = new Dictionary<ulong, float>();
            foreach (var pair in font.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                if (!glyphs.TryGetValue(pair.firstAdjustmentRecord.glyphIndex, out var first) ||
                    !glyphs.TryGetValue(pair.secondAdjustmentRecord.glyphIndex, out var second)) continue;
                float advance = (pair.firstAdjustmentRecord.glyphValueRecord.xAdvance + pair.secondAdjustmentRecord.glyphValueRecord.xAdvance) * scale;
                foreach (int a in first) foreach (int b in second) pairs[YogaFontMetrics.PairKey(a, b)] = advance;
            }
            return new YogaFontMetrics(advances, pairs, lineHeight);
        }
    }
}
