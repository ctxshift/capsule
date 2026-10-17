using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Unity
{
    // Eight source regions: horizontal rails may sample a shorter undecorated span.
    public sealed class CapsuleFrameRails : BaseMeshEffect
    {
        float railWidth,edge;
        public void SetRails(float sourceWidth,float displayEdge)
        {
            if(railWidth==sourceWidth && edge==displayEdge)return;
            railWidth=sourceWidth;edge=displayEdge;
            if(graphic)graphic.SetVerticesDirty();
        }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive() || railWidth<=0 || !(graphic is Image image) || !image.sprite)return;
            var sprite=image.sprite;
            var source=sprite.textureRect;
            var border=sprite.border;
            var rect=image.GetPixelAdjustedRect();
            float ex=Mathf.Min(edge,rect.width/2),ey=Mathf.Min(edge,rect.height/2);
            float[] xs={rect.xMin,rect.xMin+ex,rect.xMax-ex,rect.xMax};
            float[] ys={rect.yMin,rect.yMin+ey,rect.yMax-ey,rect.yMax};
            float[] us={source.xMin,source.xMin+border.x,source.xMax-border.z,source.xMax};
            float[] vs={source.yMin,source.yMin+border.y,source.yMax-border.w,source.yMax};
            mesh.Clear();
            for(int row=0;row<3;row++)for(int column=0;column<3;column++)
            {
                if(row==1 && column==1)continue;
                float right=column==1 ? us[1]+railWidth : us[column+1];
                int start=mesh.currentVertCount;
                Add(xs[column],ys[row],us[column],vs[row]);
                Add(xs[column],ys[row+1],us[column],vs[row+1]);
                Add(xs[column+1],ys[row+1],right,vs[row+1]);
                Add(xs[column+1],ys[row],right,vs[row]);
                mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start+2,start+3,start);
            }
            void Add(float x,float y,float u,float v)
            {
                var vertex=UIVertex.simpleVert;vertex.position=new Vector3(x,y);vertex.color=image.color;
                vertex.uv0=new Vector2(u/sprite.texture.width,v/sprite.texture.height);mesh.AddVert(vertex);
            }
        }
    }
}
