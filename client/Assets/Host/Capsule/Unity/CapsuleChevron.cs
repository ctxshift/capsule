using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Unity
{
    public sealed class CapsuleChevron : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var r = rectTransform.rect;
            Vector2 a = new Vector2(r.xMin, r.yMax), b = new Vector2(r.center.x, r.yMin), c = new Vector2(r.xMax, r.yMax);
            void Line(Vector2 from, Vector2 to)
            {
                var normal = new Vector2(-(to-from).y, (to-from).x).normalized * .9f;
                int start = mesh.currentVertCount;
                mesh.AddVert(from-normal,color,Vector2.zero); mesh.AddVert(from+normal,color,Vector2.zero);
                mesh.AddVert(to+normal,color,Vector2.zero); mesh.AddVert(to-normal,color,Vector2.zero);
                mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
            }
            Line(a,b); Line(b,c);
        }
    }
}
