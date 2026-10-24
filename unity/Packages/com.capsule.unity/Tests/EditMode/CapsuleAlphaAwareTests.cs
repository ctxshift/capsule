using NUnit.Framework;
using System.IO;
using Capsule;
using Capsule.Package;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleAlphaAwareTests
    {
        sealed class ImageFactory : INativeUiWidgetFactory, INativeUiStyleAdapter
        {
            readonly CapsuleControlSkin skin;
            readonly Sprite sprite;
            public ImageFactory(CapsuleControlSkin skin, Sprite sprite) { this.skin = skin; this.sprite = sprite; }
            public NativeUiWidget Create(UiNodeKind kind, Transform parent)
            {
                var obj = new GameObject("Themed image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                obj.transform.SetParent(parent, false);
                return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, null);
            }
            public bool TryGetSprite(string key, out Sprite result) { result = sprite; return true; }
            public void ApplyStyle(NativeUiWidget widget, UiNode node) => skin.Apply(widget, node);
        }





        [TestCase(false)]
        [TestCase(true)]
        public void MinificationAveragesTheSourceFootprintWithoutHiddenRgbBleed(bool transparent)
        {
            var shader = Resources.Load<Shader>("CapsuleAlphaAware");
            Assert.That(shader && shader.isSupported, Is.True);
            // Each output pixel covers two repetitions. A four-texel sampler
            // aliases this pattern to half coverage instead of one quarter.
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    pixels[y * 64 + x] = x % 4 == 0 ? new Color32(255, 255, 255, 255)
                        : transparent ? new Color32(0, 255, 255, 0) : new Color32(0, 0, 0, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var material = new Material(shader);
            var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var read = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            target.Create();
            var previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            try
            {
                var background = new Color(.04f, .08f, .12f, 1);
                RenderTexture.active = target;
                GL.sRGBWrite = false;
                GL.Clear(true, true, background);
                Graphics.Blit(texture, target, material, 0);
                read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                read.Apply();
                float remainder = transparent ? .75f : 0;
                foreach (var actual in read.GetPixels())
                {
                    Assert.That(actual.r, Is.EqualTo(.25f + background.r * remainder).Within(.012));
                    Assert.That(actual.g, Is.EqualTo(.25f + background.g * remainder).Within(.012));
                    Assert.That(actual.b, Is.EqualTo(.25f + background.b * remainder).Within(.012));
                    Assert.That(actual.a, Is.EqualTo(1).Within(.012));
                }
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousWrite;
                target.Release();
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PremultiplicationPrecedesFilteringAndPreservesTintAlphaBackgroundAndClip()
        {
            var shader=Resources.Load<Shader>("CapsuleAlphaAware");
            Assert.That(shader&&shader.isSupported,Is.True);
            var texture=new Texture2D(2,1,TextureFormat.RGBA32,false,false){wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(new[]{new Color32(160,80,40,255),new Color32(0,255,255,0)});texture.Apply();
            var material=new Material(shader);
            var target=new RenderTexture(64,8,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();
            var previous=RenderTexture.active;bool previousWrite=GL.sRGBWrite;
            try
            {
                foreach(var tint in new[]{Color.white,new Color(.5f,.75f,1,.4f)})
                {
                    var background=new Color(.03f,.05f,.2f,1);
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,background);material.SetColor("_Color",tint);
                    Graphics.Blit(texture,target,material,0);
                    var actual=Read();float alpha=1-(31.5f/64*2-.5f);
                    var source=new Color(160/255f,80/255f,40/255f,1).linear;
                    // Unity Color properties convert sRGB tint RGB to linear;
                    // alpha remains scalar, matching the default image material.
                    var linearTint=tint.linear;
                    var expected=new Color(source.r*alpha*linearTint.r*tint.a+background.r*(1-alpha*tint.a),
                        source.g*alpha*linearTint.g*tint.a+background.g*(1-alpha*tint.a),
                        source.b*alpha*linearTint.b*tint.a+background.b*(1-alpha*tint.a),1);
                    AssertColor(actual,expected);
                }
                material.EnableKeyword("UNITY_UI_CLIP_RECT");material.SetVector("_ClipRect",new Vector4(100,100,101,101));
                var clippedBackground=new Color(.15f,.02f,.07f,1);
                RenderTexture.active=target;GL.Clear(true,true,clippedBackground);Graphics.Blit(texture,target,material,0);
                AssertColor(Read(),clippedBackground);
            }
            finally
            {
                RenderTexture.active=previous;GL.sRGBWrite=previousWrite;
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(material);Object.DestroyImmediate(texture);
            }
            Color Read()
            {
                var read=new Texture2D(64,8,TextureFormat.RGBA32,false,true);
                try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,64,8),0,0);read.Apply();return read.GetPixel(31,4);}
                finally{Object.DestroyImmediate(read);}
            }
            void AssertColor(Color actual,Color expected)
            {
                Assert.That(actual.r,Is.EqualTo(expected.r).Within(.012));
                Assert.That(actual.g,Is.EqualTo(expected.g).Within(.012));
                Assert.That(actual.b,Is.EqualTo(expected.b).Within(.012));
                Assert.That(actual.a,Is.EqualTo(expected.a).Within(.012));
            }
        }
    }
}
