using System.IO;
using NUnit.Framework;
using Capsule.Package;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleFrameRailsTests
    {
        [Test]
        public void RailMeshUsesShortSourceSpanAndKeepsFixedCorners()
        {
            var root=new GameObject("Frame rails",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            var texture=new Texture2D(2172,724);
            var sprite=Sprite.Create(texture,new Rect(32,203,2109,321),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,new Vector4(100,100,100,100));
            try
            {
                var image=root.GetComponent<Image>();image.sprite=sprite;
                image.rectTransform.sizeDelta=new Vector2(768,86);
                var rails=root.AddComponent<CapsuleFrameRails>();rails.SetRails(800,18);
                using var mesh=new VertexHelper();rails.ModifyMesh(mesh);
                Assert.That(mesh.currentVertCount,Is.EqualTo(32));
                var v=new UIVertex();
                mesh.PopulateUIVertex(ref v,2);
                Assert.That(v.position.x,Is.EqualTo(-366));
                Assert.That(v.position.y,Is.EqualTo(-25));
                mesh.PopulateUIVertex(ref v,6);
                Assert.That(v.position.x,Is.EqualTo(366));
                Assert.That(v.uv0.x,Is.EqualTo(932f/2172).Within(.00001));
                mesh.PopulateUIVertex(ref v,10);
                Assert.That(v.uv0.x,Is.EqualTo(2141f/2172).Within(.00001));
                rails.SetRails(0,0);mesh.Clear();mesh.AddVert(UIVertex.simpleVert);
                rails.ModifyMesh(mesh);
                Assert.That(mesh.currentVertCount,Is.EqualTo(1),"Retained non-rail images keep their own mesh.");
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

    }
}
