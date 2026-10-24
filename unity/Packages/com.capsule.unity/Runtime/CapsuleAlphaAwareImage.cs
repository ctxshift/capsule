using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Unity
{
    // Converts ordinary UI image materials to the shared premultiplied sampler.
    // The original texture/sprite and its UV slicing remain unchanged.
    public sealed class CapsuleAlphaAwareImage : MonoBehaviour
    {
        Graphic graphic;
        Material source, owned;
        bool active = true;
        public void SetPolicy(bool alphaAware)
        {
            if(active==alphaAware)return;
            Release();active=alphaAware;
        }
        public void Apply(Graphic value)
        {
            if(!active)return;
            var material=value.material;
            if(material&&material.shader.name=="Portable UI/Alpha Aware")return;
            if(!material||material.shader.name!="UI/Default")return;
            // A previous ordinary-image tint owns a clone of UI/Default. Undo
            // that clone first so the sampler never retains a destroyed source.
            value.GetComponent<CapsuleBrightness>()?.ResetMaterial();
            material=value.material;
            Release();graphic=value;source=material;
            var shader=Resources.Load<Shader>("CapsuleAlphaAware");
            if(!shader)throw new System.InvalidOperationException("Missing alpha-aware UI shader.");
            owned=new Material(shader){name="Portable UI owned premultiplied image",hideFlags=HideFlags.DontSave};
            owned.CopyPropertiesFromMaterial(source);
            value.material=owned;value.SetMaterialDirty();
        }
        void Release()
        {
            if(owned&&graphic)graphic.GetComponent<CapsuleBrightness>()?.ResetMaterial();
            if(graphic&&graphic.material==owned)graphic.material=source;
            if(owned){if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}
            owned=null;source=null;
        }
        void OnDestroy()=>Release();
    }
}
