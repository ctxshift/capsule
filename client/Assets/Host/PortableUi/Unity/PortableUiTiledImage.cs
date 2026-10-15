using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Centered repeated source crop. Shared textures keep their sampling policy.
    public sealed class PortableUiTiledImage : BaseMeshEffect
    {
        float tileWidth,tileHeight;
        public void SetTileSize(float width,float height)
        {
            if(tileWidth==width && tileHeight==height)return;
            if((width!=0 || height!=0) && !(width>=64 && width<=4096 && height>=64 && height<=4096))
                throw new System.ArgumentOutOfRangeException(nameof(width));
            tileWidth=width;tileHeight=height;if(graphic)graphic.SetVerticesDirty();
        }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive() || tileWidth==0 || !(graphic is Image image) || !image.sprite)return;
            var rect=image.GetPixelAdjustedRect();
            var source=image.sprite.textureRect;
            var texture=image.sprite.texture;
            float originX=(rect.width-tileWidth)/2,originY=(rect.height-tileHeight)/2;
            float firstX=originX-Mathf.Ceil(originX/tileWidth)*tileWidth;
            float firstY=originY-Mathf.Ceil(originY/tileHeight)*tileHeight;
            mesh.Clear();
            // The 4096-unit node bound and 64-unit tile minimum keep this below 17k vertices.
            for(float y=firstY;y<rect.height;y+=tileHeight)
                for(float x=firstX;x<rect.width;x+=tileWidth)
                {
                    float left=Mathf.Max(0,x),right=Mathf.Min(rect.width,x+tileWidth);
                    float bottom=Mathf.Max(0,y),top=Mathf.Min(rect.height,y+tileHeight);
                    int start=mesh.currentVertCount;
                    Add(left,bottom);Add(left,top);Add(right,top);Add(right,bottom);
                    mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start+2,start+3,start);
                    void Add(float px,float py)
                    {
                        var vertex=UIVertex.simpleVert;
                        vertex.position=new Vector3(rect.xMin+px,rect.yMin+py);vertex.color=image.color;
                        vertex.uv0=new Vector2((source.x+(px-x)/tileWidth*source.width)/texture.width,
                            (source.y+(py-y)/tileHeight*source.height)/texture.height);
                        mesh.AddVert(vertex);
                    }
                }
        }
    }
}
