using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Converts ordinary UI image materials to the shared premultiplied sampler.
    // The original texture/sprite and its UV slicing remain unchanged.
    public sealed class PortableUiAlphaAwareImage : MonoBehaviour
    {
        Graphic graphic;
        Material source, owned;
        public void Apply(Graphic value)
        {
            var material=value.material;
            if(material&&material.shader.name=="Portable UI/Alpha Aware")return;
            if(!material||material.shader.name!="UI/Default")return;
            Release();graphic=value;source=material;
            var shader=Resources.Load<Shader>("PortableUiAlphaAware");
            if(!shader)throw new System.InvalidOperationException("Missing alpha-aware UI shader.");
            owned=new Material(shader){name="Portable UI owned premultiplied image",hideFlags=HideFlags.DontSave};
            owned.CopyPropertiesFromMaterial(source);
            value.material=owned;value.SetMaterialDirty();
        }
        void Release()
        {
            if(graphic&&graphic.material==owned)graphic.material=source;
            if(owned){if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}
            owned=null;source=null;
        }
        void OnDestroy()=>Release();
    }
}
