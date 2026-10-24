using System.IO;
using NUnit.Framework;
using Capsule.Package;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleTiledImageTests
    {
        [TestCase(768,86)]
        [TestCase(480,180)]
        [TestCase(1100,360)]
        [TestCase(4096,4096)]
        public void RepeatedCropKeepsItsScaleAndCoversResizedPanel(float width,float height)
        {
            var root=new GameObject("Tiled backing",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            var texture=new Texture2D(320,320);
            var sprite=Sprite.Create(texture,new Rect(40,60,240,200),Vector2.one*.5f);
            try
            {
                var image=root.GetComponent<Image>();image.sprite=sprite;image.rectTransform.sizeDelta=new Vector2(width,height);
                var tiling=root.AddComponent<CapsuleTiledImage>();tiling.SetTileSize(240,240);
                using var mesh=new VertexHelper();tiling.ModifyMesh(mesh);
                float area=0;
                for(int i=0;i<mesh.currentVertCount;i+=4)
                {
                    var a=new UIVertex();var b=new UIVertex();mesh.PopulateUIVertex(ref a,i);mesh.PopulateUIVertex(ref b,i+2);
                    float dx=b.position.x-a.position.x,dy=b.position.y-a.position.y;
                    area+=dx*dy;
                    Assert.That((b.uv0.x-a.uv0.x)*320/dx,Is.EqualTo(1).Within(.001));
                    Assert.That((b.uv0.y-a.uv0.y)*320/dy,Is.EqualTo(200f/240).Within(.001));
                    Assert.That(a.uv0.x,Is.InRange(40f/320,280f/320));
                    Assert.That(b.uv0.x,Is.InRange(40f/320,280f/320));
                    Assert.That(a.position.x,Is.GreaterThanOrEqualTo(-width/2));
                    Assert.That(b.position.x,Is.LessThanOrEqualTo(width/2));
                }
                Assert.That(area,Is.EqualTo(width*height).Within(1));
                tiling.SetTileSize(64,64);tiling.ModifyMesh(mesh);
                Assert.That(mesh.currentVertCount,Is.LessThanOrEqualTo(16900));
                tiling.SetTileSize(0,0);mesh.Clear();mesh.AddVert(UIVertex.simpleVert);tiling.ModifyMesh(mesh);
                Assert.That(mesh.currentVertCount,Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

    }
}
