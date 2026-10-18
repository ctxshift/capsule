using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Capsule.Unity
{
    // A leased image is mapped onto sampled world support, never onto a screen ellipse.
    internal sealed class CapsuleGroundProjection : IDisposable
    {
        const int Segments = 64;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly Material material;
        readonly MeshRenderer renderer;
        readonly Vector3[] vertices = new Vector3[Segments * 2];
        readonly Vector2[] uvs = new Vector2[Segments * 2];
        readonly bool[] supported = new bool[Segments * 2];
        readonly int[] triangles = new int[Segments * 6];
        Vector3 lastCenter;
        float lastRadius, nextSample;
        Sprite lastSprite;
        public bool Shown => renderer && renderer.enabled;
        public Mesh Mesh => mesh;

        public CapsuleGroundProjection()
        {
            root = new GameObject("Capsule leased ground projection", typeof(MeshFilter), typeof(MeshRenderer));
            root.hideFlags = HideFlags.DontSave;
            mesh = new Mesh { name = "Leased ground image" };
            mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.GetComponent<MeshRenderer>();
            material = new Material(Shader.Find("Sprites/Default")) { name = "Capsule ground image" };
            renderer.sharedMaterial = material;
            renderer.enabled = false;
        }

        public void Present(CapsuleWorldRegistry.Attachment lease, Vector3 center, Image image, bool visible, float alpha)
        {
            if (!renderer) return;
            var sprite = image ? image.sprite : null;
            float radius = 0;
            try { if (lease?.SurfaceRadius != null) radius = lease.SurfaceRadius(); }
            catch { radius = 0; }
            visible &= sprite && lease?.SurfacePoint != null && float.IsFinite(radius) && radius > .1f && radius <= 20f;
            renderer.enabled = visible;
            if (!visible) return;
            // The Canvas image supplies artwork and tint; its anchored parent is hidden below.
            var tint = image.color;
            tint.a *= alpha;
            material.color = tint;
            if (sprite != lastSprite)
            {
                material.mainTexture = sprite.texture;
                lastSprite = sprite;
                nextSample = 0;
            }
            if (Time.unscaledTime < nextSample && Vector3.Distance(center,lastCenter) < .025f &&
                Mathf.Abs(radius-lastRadius) < .025f) return;
            nextSample = Time.unscaledTime + .1f;
            lastCenter = center; lastRadius = radius;
            var uv = DataUtility.GetOuterUV(sprite);
            for (int i = 0; i < Segments; i++)
            {
                float angle = 2f * Mathf.PI * i / Segments;
                float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
                for (int ring = 0; ring < 2; ring++)
                {
                    int index = i * 2 + ring;
                    float fraction = ring == 0 ? .72f : 1f;
                    var sample = center + new Vector3(x * radius * fraction, 0, z * radius * fraction);
                    Vector3? point;
                    try { point = lease.SurfacePoint(sample); }
                    catch { point = null; }
                    supported[index] = point.HasValue && float.IsFinite(point.Value.x) &&
                        float.IsFinite(point.Value.y) && float.IsFinite(point.Value.z);
                    vertices[index] = supported[index] ? point.Value + Vector3.up * .025f : sample;
                    uvs[index] = new Vector2(Mathf.Lerp(uv.x,uv.z,.5f + x * fraction * .5f),
                        Mathf.Lerp(uv.y,uv.w,.5f + z * fraction * .5f));
                }
            }
            int count = 0;
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 2, b = ((i + 1) % Segments) * 2;
                if (supported[a] && supported[b] && supported[a+1])
                { triangles[count++]=a; triangles[count++]=b; triangles[count++]=a+1; }
                if (supported[a+1] && supported[b] && supported[b+1])
                { triangles[count++]=a+1; triangles[count++]=b; triangles[count++]=b+1; }
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            if (count != triangles.Length) Array.Clear(triangles,count,triangles.Length-count);
            mesh.SetTriangles(triangles,0,count,0);
            mesh.RecalculateBounds();
            renderer.enabled = count > 0;
        }

        public void Hide() { if (renderer) renderer.enabled = false; }
        public void Dispose()
        {
            if (root) UnityEngine.Object.Destroy(root);
            if (mesh) UnityEngine.Object.Destroy(mesh);
            if (material) UnityEngine.Object.Destroy(material);
        }
    }
}
