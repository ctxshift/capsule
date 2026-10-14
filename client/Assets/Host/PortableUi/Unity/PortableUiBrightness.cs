using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Owns only this Graphic's material. HDR tint multiplies sampled RGB without
    // quantizing it through Color32 vertices; the original alpha is preserved.
    public sealed class PortableUiBrightness : MonoBehaviour
    {
        public float ThemeFactor { get; set; } = 1;
        Graphic graphic;
        Material source, owned;
        int property;
        Color baseline;
        float last = float.NaN;
        static Material Read(Graphic value) => value is TMP_Text text ? text.fontSharedMaterial : value.material;
        static void Write(Graphic value, Material material)
        { if(value is TMP_Text text)text.fontSharedMaterial=material;else value.material=material; }
        public void Apply(Graphic value, float factor)
        {
            if (graphic != value || Read(value) != owned)
            {
                Release(); graphic = value; source = Read(value);
                property = Shader.PropertyToID(value is TMP_Text ? "_FaceColor" : "_Color");
                if (!source || !source.HasProperty(property))
                    throw new System.InvalidOperationException("Graphic shader lacks RGB presentation tint.");
                baseline = source.GetColor(property);
                owned = new Material(source) { name = "Portable UI owned RGB tint", hideFlags = HideFlags.DontSave };
                Write(value,owned); last = float.NaN;
            }
            if (factor == last) return;
            last = factor;
            owned.SetColor(property, new Color(baseline.r * factor, baseline.g * factor, baseline.b * factor, baseline.a));
            graphic.SetMaterialDirty();
        }
        void Release()
        {
            if (graphic && Read(graphic) == owned) Write(graphic,source);
            if (owned) { if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned); }
            owned = null; source = null;
        }
        // Sampling policy changes must unwind the tint before replacing its source.
        public void ResetMaterial() => Release();
        void OnDestroy() => Release();
    }
}
