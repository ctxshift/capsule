using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Builds a complete inactive candidate tree before replacing the live tree.
    public sealed class PortableUiNativeTarget : IUiCommitTarget, IDisposable
    {
        readonly RectTransform canvas;
        readonly INativeUiWidgetFactory factory;
        readonly RectTransform layer;
        RectTransform current;
        UiSnapshot currentSnapshot;
        Dictionary<ulong, NativeUiWidget> currentWidgets = new Dictionary<ulong, NativeUiWidget>();
        bool active;
        bool disposed;
        public UiSession Session { get; set; }

        public PortableUiNativeTarget(RectTransform canvasRoot, INativeUiWidgetFactory widgetFactory)
        {
            canvas = canvasRoot ? canvasRoot : throw new ArgumentNullException(nameof(canvasRoot));
            factory = widgetFactory ?? throw new ArgumentNullException(nameof(widgetFactory));
            layer = NewRoot("Portable UI guest layer", canvas);
            layer.gameObject.SetActive(false);
        }

        public void Activate() { active = true; if (layer) layer.gameObject.SetActive(true); }

        public bool TryGetNode(ulong handle, out UiNode node)
        {
            if (currentSnapshot != null)
                foreach (var candidate in currentSnapshot.Nodes)
                    if (candidate.Handle == handle) { node = candidate; return true; }
            node = null;
            return false;
        }

        public bool TryFindOnlySlot(out ulong handle)
        {
            handle = 0;
            if (currentSnapshot == null) return false;
            foreach (var node in currentSnapshot.Nodes)
                if (node.Kind == UiNodeKind.Slot)
                { if (handle != 0) { handle = 0; return false; } handle = node.Handle; }
            return handle != 0;
        }

        public bool TryCommit(UiSnapshot proposed, out string error)
        {
            error = null;
            if (disposed) { error = "Native target disposed."; return false; }
            var staged = new Dictionary<ulong, NativeUiWidget>();
            RectTransform stage = null;
            try
            {
                // Prepare new widgets off-tree before touching retained keyed objects.
                foreach (var node in proposed.Nodes)
                {
                    if (!string.IsNullOrEmpty(node.AssetKey) &&
                        (!factory.TryGetSprite(node.AssetKey, out var sprite) || !sprite))
                        throw new InvalidOperationException("Asset key unavailable: " + node.AssetKey);
                    if (currentWidgets.ContainsKey(node.Handle)) continue;
                    if (!stage) { stage = NewRoot("Candidate", layer); stage.gameObject.SetActive(false); }
                    NativeUiWidget widget;
                    if (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox)
                    {
                        var box = new GameObject("Box", typeof(RectTransform));
                        box.transform.SetParent(stage, false);
                        if (node.Kind == UiNodeKind.ClipBox) box.AddComponent<RectMask2D>();
                        widget = new NativeUiWidget((RectTransform)box.transform, null, null, null);
                    }
                    else widget = factory.Create(node.Kind, stage);
                    if (!widget.Rect) throw new InvalidOperationException("Factory returned no RectTransform.");
                    ApplyNode(widget, node);
                    widget.Rect.gameObject.AddComponent<PortableUiNodeInput>().Bind(this, node.Handle, node.Events);
                    if (widget.CloseButton)
                    {
                        ulong id = node.Handle;
                        widget.CloseButton.onClick.AddListener(() => Queue(id, UiEventMask.Close));
                    }
                    staged.Add(node.Handle, widget);
                }
                var nextWidgets = new Dictionary<ulong, NativeUiWidget>();
                foreach (var node in proposed.Nodes)
                {
                    var widget = currentWidgets.TryGetValue(node.Handle, out var retained) ? retained : staged[node.Handle];
                    if (currentWidgets.ContainsKey(node.Handle)) ApplyNode(widget, node);
                    nextWidgets.Add(node.Handle, widget);
                }
                if (!current) current = NewRoot("Active", layer);
                foreach (var node in proposed.Nodes)
                {
                    var rect = nextWidgets[node.Handle].Rect;
                    rect.SetParent(node.Parent == 0 ? current : nextWidgets[node.Parent].Rect, false);
                    rect.SetAsLastSibling();
                }
                foreach (var old in currentWidgets)
                    if (!nextWidgets.ContainsKey(old.Key))
                    {
                        old.Value.Rect.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(old.Value.Rect.gameObject);
                    }
                currentWidgets = nextWidgets;
                currentSnapshot = proposed;
                return true;
            }
            catch (Exception exception)
            {
                // Restore retained properties/parents if a native adapter rejects the batch.
                if (currentSnapshot != null)
                    foreach (var old in currentSnapshot.Nodes)
                        try
                        {
                            var widget = currentWidgets[old.Handle];
                            ApplyNode(widget, old);
                            widget.Rect.SetParent(old.Parent == 0 ? current : currentWidgets[old.Parent].Rect, false);
                            widget.Rect.SetAsLastSibling();
                        }
                        catch { }
                foreach (var item in staged.Values)
                    if (item.Rect) { item.Rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(item.Rect.gameObject); }
                error = exception.Message;
                return false;
            }
            finally { if (stage) UnityEngine.Object.Destroy(stage.gameObject); }
        }

        public bool TryGetNativeWidget(ulong handle, out RectTransform rect)
        {
            rect = null;
            if (!currentWidgets.TryGetValue(handle, out var widget) || !widget.Rect) return false;
            rect = widget.Rect;
            return true;
        }

        void ApplyNode(NativeUiWidget widget, UiNode node)
        {
            SetRect(widget.Rect, node.Rect);
            if (widget.Label) { widget.Label.text = node.Text ?? ""; widget.Label.richText = false; }
            if (widget.Image && !string.IsNullOrEmpty(node.AssetKey))
            {
                if (!factory.TryGetSprite(node.AssetKey, out var sprite) || !sprite)
                    throw new InvalidOperationException("Asset key unavailable: " + node.AssetKey);
                widget.Image.sprite = sprite;
                widget.Image.enabled = true;
            }
            else if (widget.Image && (node.Kind == UiNodeKind.Image ||
                node.Kind == UiNodeKind.Portrait || node.Kind == UiNodeKind.Slot))
            {
                widget.Image.sprite = null;
                widget.Image.enabled = false;
            }
            if (widget.Fill) widget.Fill.fillAmount = node.Progress / 10000f;
            if (factory is INativeUiStyleAdapter styled) styled.ApplyStyle(widget, node);
            var input = widget.Rect.GetComponent<PortableUiNodeInput>();
            if (input) input.Bind(this, node.Handle, node.Events);
            widget.Rect.gameObject.SetActive(node.Visible);
            widget.Rect.SetAsLastSibling();
        }

        public void Queue(ulong handle, UiEventMask kind)
        { if (active && !disposed) Session?.QueueEvent(handle, kind, Array.Empty<byte>()); }

        static RectTransform NewRoot(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static void SetRect(RectTransform rect, UiRect fixedRect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(fixedRect.X / 64f, -fixedRect.Y / 64f);
            rect.sizeDelta = new Vector2(fixedRect.Width / 64f, fixedRect.Height / 64f);
            rect.localScale = Vector3.one;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (layer) { layer.gameObject.SetActive(false); UnityEngine.Object.Destroy(layer.gameObject); }
        }
    }
}
