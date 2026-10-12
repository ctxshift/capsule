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
            if (SameTopology(proposed) && TryIncremental(proposed, out error)) return true;
            if (SameTopology(proposed)) return false;
            RectTransform stage = null;
            try
            {
                stage = NewRoot("Candidate", layer);
                stage.gameObject.SetActive(false);
                var created = new Dictionary<ulong, NativeUiWidget>();
                foreach (var node in proposed.Nodes)
                {
                    Transform parent = node.Parent == 0 ? stage : created[node.Parent].Rect;
                    var widget = factory.Create(node.Kind, parent);
                    if (!widget.Rect) throw new InvalidOperationException("Factory returned no RectTransform.");
                    ApplyNode(widget, node);
                    widget.Rect.gameObject.AddComponent<PortableUiNodeInput>()
                        .Bind(this, node.Handle, node.Events);
                    if (widget.CloseButton)
                    {
                        ulong id = node.Handle;
                        widget.CloseButton.onClick.AddListener(() => Queue(id, UiEventMask.Close));
                    }
                    created.Add(node.Handle, widget);
                }
                var previous = current;
                current = stage;
                currentSnapshot = proposed;
                currentWidgets = created;
                stage.gameObject.SetActive(true);
                if (previous) { previous.gameObject.SetActive(false); UnityEngine.Object.Destroy(previous.gameObject); }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (stage) { stage.gameObject.SetActive(false); UnityEngine.Object.Destroy(stage.gameObject); }
                return false;
            }
        }

        bool SameTopology(UiSnapshot next)
        {
            if (currentSnapshot == null || currentSnapshot.Nodes.Count != next.Nodes.Count) return false;
            foreach (var node in next.Nodes)
            {
                if (!currentWidgets.ContainsKey(node.Handle)) return false;
                bool found = false;
                foreach (var old in currentSnapshot.Nodes)
                    if (old.Handle == node.Handle)
                    { found = old.Parent == node.Parent && old.Kind == node.Kind; break; }
                if (!found) return false;
            }
            return true;
        }

        bool TryIncremental(UiSnapshot next, out string error)
        {
            error = null;
            try
            {
                // Validate keys before mutating any live node.
                foreach (var node in next.Nodes)
                    if (!string.IsNullOrEmpty(node.AssetKey) &&
                        (!factory.TryGetSprite(node.AssetKey, out var sprite) || !sprite))
                        throw new InvalidOperationException("Asset key unavailable: " + node.AssetKey);
                foreach (var node in next.Nodes) ApplyNode(currentWidgets[node.Handle], node);
                currentSnapshot = next;
                return true;
            }
            catch (Exception exception)
            {
                // All normally fallible asset lookups were validated first; restore prior values on
                // an unexpected native component exception.
                foreach (var old in currentSnapshot.Nodes)
                    try { ApplyNode(currentWidgets[old.Handle], old); } catch { }
                error = exception.Message;
                return false;
            }
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
