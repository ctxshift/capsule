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
            => Capture(font,characters,size,lineHeight,Array.Empty<TMP_FontAsset>());

        // The host supplies the same ordered, finite fallback faces it assigns
        // to TMP. No OS lookup or recursive fallback traversal occurs here.
        public static YogaFontMetrics Capture(TMP_FontAsset font, string characters, float size, float lineHeight, IReadOnlyList<TMP_FontAsset> fallbacks)
        {
            if (!font || characters == null || characters.Length > 4096 || size <= 0 || size > 256)
                throw new ArgumentException("Font capture bounds.");
            if(fallbacks==null || fallbacks.Count>4)throw new ArgumentException("Font fallback bounds.");
            var faces=new List<TMP_FontAsset>{font};
            foreach(var fallback in fallbacks)
            {
                if(!fallback || faces.Contains(fallback))throw new ArgumentException("Font fallback unavailable or duplicated.");
                faces.Add(fallback);
            }
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
            foreach(var fallback in fallbacks)
            {
                var missing=new System.Text.StringBuilder();
                for(int i=0;i<characters.Length;i++)
                {
                    int code=char.ConvertToUtf32(characters,i);if(code>65535)++i;
                    bool found=false;foreach(var face in faces){if(face==fallback)break;if(face.characterLookupTable.ContainsKey((uint)code)){found=true;break;}}
                    if(!found&&!fallback.characterLookupTable.ContainsKey((uint)code))missing.Append(char.ConvertFromUtf32(code));
                }
                if(missing.Length!=0)fallback.TryAddCharacters(missing.ToString(),out _,true);
            }
            float scale = size / font.faceInfo.pointSize * font.faceInfo.scale;
            var advances = new Dictionary<int, float>();
            var byFace=new Dictionary<TMP_FontAsset,Dictionary<uint,List<int>>>();
            foreach(var face in faces)byFace.Add(face,new Dictionary<uint,List<int>>());
            for (int i = 0; i < characters.Length; ++i)
            {
                int code = char.ConvertToUtf32(characters, i); if (code > 65535) ++i;
                if (advances.ContainsKey(code)) continue;
                TMP_Character character=null;TMP_FontAsset owner=null;
                foreach(var face in faces)if(face.characterLookupTable.TryGetValue((uint)code,out character)){owner=face;break;}
                if(!owner)throw new InvalidOperationException("Captured glyph unavailable: U+"+code.ToString("X"));
                float faceScale=size/owner.faceInfo.pointSize*owner.faceInfo.scale;
                advances.Add(code, character.glyph.metrics.horizontalAdvance * faceScale * character.scale);
                var glyphs=byFace[owner];
                if (!glyphs.TryGetValue(character.glyphIndex, out var codes)) glyphs.Add(character.glyphIndex, codes = new List<int>());
                codes.Add(code);
            }
            var pairs = new Dictionary<ulong, float>();
            foreach(var face in faces)
            {
            var glyphs=byFace[face];scale=size/face.faceInfo.pointSize*face.faceInfo.scale;
            foreach (var pair in face.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                if (!glyphs.TryGetValue(pair.firstAdjustmentRecord.glyphIndex, out var first) ||
                    !glyphs.TryGetValue(pair.secondAdjustmentRecord.glyphIndex, out var second)) continue;
                float advance = (pair.firstAdjustmentRecord.glyphValueRecord.xAdvance + pair.secondAdjustmentRecord.glyphValueRecord.xAdvance) * scale;
                foreach (int a in first) foreach (int b in second) pairs[YogaFontMetrics.PairKey(a, b)] = advance;
            }
            }
            return new YogaFontMetrics(advances, pairs, lineHeight);
        }
    }
}
