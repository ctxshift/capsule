using System;
using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Rotates sampled geometry inside the unchanged layout and input rectangle.
    // The Sprite, texture and UVs remain borrowed and unchanged.
    public sealed class PortableUiImageRotation : BaseMeshEffect
    {
        int degrees;
        public void SetRotation(int value)
        {
            if(value!=0 && value!=90 && value!=180 && value!=270)throw new ArgumentOutOfRangeException(nameof(value));
            if(degrees==value)return;
            degrees=value;if(graphic)graphic.SetVerticesDirty();
        }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive() || degrees==0 || mesh.currentVertCount==0)return;
            var image=graphic as Image;
            if(!image || image.type!=Image.Type.Simple)return;
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            var vertex=new UIVertex();
            for(int i=0;i<mesh.currentVertCount;i++){mesh.PopulateUIVertex(ref vertex,i);min=Vector2.Min(min,vertex.position);max=Vector2.Max(max,vertex.position);}
            if(max.x<=min.x || max.y<=min.y)return;
            var box=image.rectTransform.rect;
            if(image.preserveAspect && image.sprite)
            {
                var source=image.sprite.rect;
                float aspect=degrees==90||degrees==270?source.height/source.width:source.width/source.height;
                float width=Mathf.Min(box.width,box.height*aspect),height=width/aspect;
                box=new Rect(box.center-new Vector2(width,height)/2,new Vector2(width,height));
            }
            for(int i=0;i<mesh.currentVertCount;i++)
            {
                mesh.PopulateUIVertex(ref vertex,i);
                float x=(vertex.position.x-min.x)/(max.x-min.x),y=(vertex.position.y-min.y)/(max.y-min.y);
                Vector2 point=degrees==90?new Vector2(y,1-x):degrees==180?new Vector2(1-x,1-y):new Vector2(1-y,x);
                vertex.position=new Vector3(box.x+point.x*box.width,box.y+point.y*box.height,vertex.position.z);
                mesh.SetUIVertex(vertex,i);
            }
        }
    }
}
