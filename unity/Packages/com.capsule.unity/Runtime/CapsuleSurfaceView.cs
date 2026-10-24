using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Capsule.Unity
{
    // Per-guest host factory. Implementations own their GPU/render resources.
    public interface INativeUiSurfaceFactory
    {
        INativeUiSurfaceLease Acquire(string admittedKey);
    }
    public interface INativeUiSurfaceLease : IDisposable
    {
        // Main thread only. Return a live texture; no image encoding/readback.
        Texture Resize(int pixelWidth, int pixelHeight);
        void SetVisible(bool visible);
        void Pointer(UiPointerPhase phase, int pointerId, Vector2 localPoint, PointerEventData.InputButton button);
        void Scroll(Vector2 localPoint, Vector2 delta);
    }

    [RequireComponent(typeof(RawImage))]
    public sealed class CapsuleSurfaceView : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler, IScrollHandler
    {
        // Bounded adapter defaults. The provider also owns its format/MSAA budget.
        public const int MaxDimension = 2048, MaxPixels = 2097152;
        INativeUiSurfaceLease lease;
        CapsuleNativeTarget target;
        RawImage image;
        Canvas canvas;
        ulong handle;
        int width, height, pointerId;
        float scale;
        bool captured, visible, prepared;
        Vector2 lastPoint;
        PointerEventData.InputButton button;
        public string SurfaceKey { get; private set; }

        internal void Prepare(INativeUiSurfaceLease value, string key, CapsuleNativeTarget owner, ulong id)
        {
            if (value == null) throw new InvalidOperationException("Surface provider returned no lease.");
            lease = value; target = owner; handle = id; SurfaceKey = key;
            image = GetComponent<RawImage>(); image.raycastTarget = true;
            image.onCullStateChanged.AddListener(OnCull);
            canvas = GetComponentInParent<Canvas>();
            try
            {
                Resize();
                lease.SetVisible(false);
                prepared = true;
            }
            catch { Release(); throw; }
        }

        void Resize()
        {
            if (lease == null || !image) return;
            float nextScale = canvas ? canvas.scaleFactor : 1;
            var size = image.rectTransform.rect.size;
            float requestedWidth = Mathf.Max(1, size.x * nextScale), requestedHeight = Mathf.Max(1, size.y * nextScale);
            float factor = Mathf.Min(1, MaxDimension / requestedWidth, MaxDimension / requestedHeight,
                Mathf.Sqrt(MaxPixels / (requestedWidth * requestedHeight)));
            int nextWidth = Mathf.Max(1, Mathf.FloorToInt(requestedWidth * factor));
            int nextHeight = Mathf.Max(1, Mathf.FloorToInt(requestedHeight * factor));
            scale = nextScale;
            if (nextWidth == width && nextHeight == height) return;
            var texture = lease.Resize(nextWidth, nextHeight);
            if (!texture || texture.width > MaxDimension || texture.height > MaxDimension ||
                (long)texture.width * texture.height > MaxPixels)
                throw new InvalidOperationException("Surface texture exceeds native pixel bounds or is unavailable.");
            image.texture = texture; width = nextWidth; height = nextHeight;
        }
        void LateUpdate()
        {
            if (lease == null) return;
            // Canvas scaling can change without changing a child's design rectangle.
            if (canvas && canvas.scaleFactor != scale) Resize();
        }
        void OnRectTransformDimensionsChange() { if (prepared) Resize(); }
        void OnCanvasHierarchyChanged()
        { canvas = GetComponentInParent<Canvas>(); if (prepared) Resize(); }
        void OnEnable() { if (prepared) Show(); }
        internal void Synchronize()
        {
            if (!prepared) return;
            Resize();
            image.raycastTarget = target.SurfaceInputActive;
            if (!isActiveAndEnabled) Cancel();
            Show();
        }
        void OnDisable() { Cancel(); SetVisible(false); }
        void OnCull(bool culled) { if (culled) Cancel(); Show(); }
        void Show() => SetVisible(prepared && isActiveAndEnabled && image && !image.canvasRenderer.cull);
        void SetVisible(bool value)
        {
            if (lease == null || visible == value) return;
            visible = value; lease.SetVisible(value);
        }
        bool Point(PointerEventData data)
        {
            if (!image || !RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform,
                data.position, data.pressEventCamera, out var point) || !float.IsFinite(point.x) || !float.IsFinite(point.y)) return false;
            lastPoint = new Vector2(Mathf.Clamp(point.x, -8192, 8192), Mathf.Clamp(point.y, -8192, 8192));
            return true;
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (!prepared || !visible || !target.SurfaceInputActive || captured ||
                !Point(data) ||
                !RectTransformUtility.RectangleContainsScreenPoint(image.rectTransform, data.position, data.pressEventCamera)) return;
            target.QueueFocusPath(handle);
            pointerId = data.pointerId; button = data.button; captured = true;
            lease.Pointer(UiPointerPhase.Down, pointerId, lastPoint, button);
        }
        public void OnBeginDrag(PointerEventData data) { }
        public void OnDrag(PointerEventData data)
        {
            if (!captured || data.pointerId != pointerId || !target.SurfaceInputActive || !Point(data)) return;
            lease.Pointer(UiPointerPhase.Move, pointerId, lastPoint, button);
        }
        public void OnPointerUp(PointerEventData data) => Finish(data);
        public void OnEndDrag(PointerEventData data) => Finish(data);
        void Finish(PointerEventData data)
        {
            if (!captured || data.pointerId != pointerId) return;
            Point(data); captured = false;
            lease.Pointer(UiPointerPhase.Up, pointerId, lastPoint, button);
        }
        public void OnScroll(PointerEventData data)
        {
            if (!prepared || !visible || !target.SurfaceInputActive || !Point(data) ||
                !float.IsFinite(data.scrollDelta.x) || !float.IsFinite(data.scrollDelta.y)) return;
            lease.Scroll(lastPoint, new Vector2(Mathf.Clamp(data.scrollDelta.x, -128, 128),
                Mathf.Clamp(data.scrollDelta.y, -128, 128)));
        }
        public void OnCancel(BaseEventData data) => Cancel();
        internal void Cancel()
        {
            if (!captured) return;
            captured = false; lease?.Pointer(UiPointerPhase.Cancel, pointerId, lastPoint, button);
        }
        void OnApplicationFocus(bool focus) { if (!focus) Cancel(); }
        void OnApplicationPause(bool paused) { if (paused) Cancel(); }
        void OnDestroy() => Release();
        internal void Release()
        {
            Cancel(); SetVisible(false);
            if (image) { image.onCullStateChanged.RemoveListener(OnCull); image.texture = null; }
            var old = lease; lease = null; prepared = false; old?.Dispose();
        }
    }
}
