using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Unity
{
    // Bounded native control geometry, independent of theme assets.
    public sealed class CapsuleRoundedImage : Image
    {
        public float Radius = 4;
        public float BorderWidth;
        public Color BorderColor = Color.clear;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            float radius = Mathf.Clamp(Radius, 0, Mathf.Min(rect.width, rect.height) / 2);
            const int count = 24;
            var outside = new Vector2[count];
            var inside = new Vector2[count];
            float border = Mathf.Clamp(BorderWidth, 0, radius);
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax-radius : rect.xMin+radius,
                    corner < 2 ? rect.yMax-radius : rect.yMin+radius);
                for (int segment=0;segment<6;segment++)
                {
                    float angle=(corner*90+segment*18)*Mathf.Deg2Rad;
                    var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    int index=corner*6+segment;
                    outside[index]=center+direction*radius;
                    inside[index]=center+direction*Mathf.Max(0,radius-border);
                }
            }
            mesh.AddVert(rect.center,color,Vector2.zero);
            for(int i=0;i<count;i++)mesh.AddVert(inside[i],color,Vector2.zero);
            for(int i=0;i<count;i++)mesh.AddTriangle(0,i+1,(i+1)%count+1);
            if(border<=0)return;
            int start=mesh.currentVertCount;
            for(int i=0;i<count;i++)
            {mesh.AddVert(inside[i],BorderColor,Vector2.zero);mesh.AddVert(outside[i],BorderColor,Vector2.zero);}
            for(int i=0;i<count;i++)
            {
                int a=start+i*2,b=start+(i+1)%count*2;
                mesh.AddTriangle(a,a+1,b+1);mesh.AddTriangle(a,b+1,b);
            }
        }
    }
}
