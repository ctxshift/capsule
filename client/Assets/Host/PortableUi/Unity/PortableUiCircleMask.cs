using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Solid ellipse geometry doubles as a stencil mask for themed surface children.
    [RequireComponent(typeof(Image))]
    public sealed class PortableUiCircleMask : BaseMeshEffect, ICanvasRaycastFilter
    {
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            mesh.Clear();
            var rect = ((RectTransform)transform).rect;
            var half = rect.size * .5f;
            const int segments = 64;
            mesh.AddVert(rect.center, graphic.color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (2 * Mathf.PI / segments);
                mesh.AddVert(rect.center + new Vector2(Mathf.Cos(angle) * half.x, Mathf.Sin(angle) * half.y), graphic.color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++) mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
        }
        public bool IsRaycastLocationValid(Vector2 point, Camera camera)
        {
            if (!IsActive()) return true;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, point, camera, out var local)) return false;
            var half = rect.rect.size * .5f;
            if (half.x <= 0 || half.y <= 0) return false;
            local -= rect.rect.center;
            return local.x * local.x / (half.x * half.x) + local.y * local.y / (half.y * half.y) <= 1;
        }
    }
}
