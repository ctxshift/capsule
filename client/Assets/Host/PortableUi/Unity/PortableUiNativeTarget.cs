using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        ulong focusedWindow;
        GameObject returnFocus;
        static PortableUiNativeTarget keyboardOwner;
        public UiSession Session { get; set; }
        // Async integration installs a main-thread queue sink, never a worker session.
        public Func<ulong, UiEventMask, byte[], bool> InputSink { get; set; }
        public long CommitCount { get; private set; }

        public bool TryDesignPoint(Vector2 screen, Camera camera, out Vector2 point)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, camera, out var local))
            { point = default; return false; }
            point = new Vector2(local.x - canvas.rect.xMin, canvas.rect.yMax - local.y);
            return true;
        }

        public PortableUiNativeTarget(RectTransform canvasRoot, INativeUiWidgetFactory widgetFactory)
        {
            canvas = canvasRoot ? canvasRoot : throw new ArgumentNullException(nameof(canvasRoot));
            factory = widgetFactory ?? throw new ArgumentNullException(nameof(widgetFactory));
            layer = NewRoot("Portable UI guest layer", canvas);
            layer.gameObject.AddComponent<PortableUiKeyboardInput>().Bind(this);
            layer.gameObject.SetActive(false);
        }

        public void Activate() { active = true; keyboardOwner = this; if (layer) layer.gameObject.SetActive(true); MaintainFocus(); }
        public void SuspendInput()
        {
            active=false;
            if(keyboardOwner==this)keyboardOwner=null;
            if(!layer)return;
            foreach(var select in layer.GetComponentsInChildren<TMP_Dropdown>(true))select.Hide();
            foreach(var selectable in layer.GetComponentsInChildren<Selectable>(true))selectable.interactable=false;
            foreach(var graphic in layer.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            foreach(var input in layer.GetComponentsInChildren<PortableUiNodeInput>(true))input.enabled=false;
        }

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
                    if (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox || node.Kind == UiNodeKind.Button)
                    {
                        var box = new GameObject("Box", typeof(RectTransform));
                        box.transform.SetParent(stage, false);
                        if (node.Kind == UiNodeKind.ClipBox) box.AddComponent<RectMask2D>();
                        Image hit = null;
                        // Every retained box can acquire subscriptions in a later snapshot.
                        {
                            hit = box.AddComponent<Image>(); hit.color = Color.clear;
                            if (node.Kind == UiNodeKind.Button)
                            {
                                var control = box.AddComponent<Button>(); control.targetGraphic = hit;
                                control.navigation = new Navigation { mode = Navigation.Mode.None };
                            }
                        }
                        widget = new NativeUiWidget((RectTransform)box.transform, hit, null, null);
                    }
                    else if (node.Kind == UiNodeKind.Range) widget = CreateRange(stage, node.Handle);
                    else if (node.Kind == UiNodeKind.Select) widget = CreateSelect(stage, node.Handle);
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
                ApplyPresentation(proposed, nextWidgets);
                var removed = new HashSet<ulong>();
                foreach(var old in currentWidgets) if(!nextWidgets.ContainsKey(old.Key))removed.Add(old.Key);
                foreach (var old in currentWidgets)
                    if (!nextWidgets.ContainsKey(old.Key))
                    {
                        // Retire each removed subtree once. DestroyImmediate in
                        // authoring/tests would otherwise invalidate child entries.
                        if (TryGetNode(old.Key,out var oldNode) && removed.Contains(oldNode.Parent)) continue;
                        old.Value.Rect.gameObject.SetActive(false);
                        DestroyOwned(old.Value.Rect.gameObject);
                    }
                currentWidgets = nextWidgets;
                currentSnapshot = proposed;
                ++CommitCount;
                MaintainFocus();
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
                    if (item.Rect) { item.Rect.gameObject.SetActive(false); DestroyOwned(item.Rect.gameObject); }
                if (currentSnapshot != null) ApplyPresentation(currentSnapshot, currentWidgets);
                error = exception.Message;
                return false;
            }
            finally { if (stage) DestroyOwned(stage.gameObject); }
        }

        public bool TryGetNativeWidget(ulong handle, out RectTransform rect)
        {
            rect = null;
            if (!currentWidgets.TryGetValue(handle, out var widget) || !widget.Rect) return false;
            rect = widget.Rect;
            return true;
        }

        void ApplyPresentation(UiSnapshot snapshot, Dictionary<ulong, NativeUiWidget> widgets)
        {
            var factors = new Dictionary<Transform, float>();
            var inherited = new Dictionary<ulong, float>();
            foreach (var node in snapshot.Nodes)
            {
                float factor = Mathf.Clamp((node.Parent == 0 ? 1 : inherited[node.Parent]) * node.Brightness / 10000f, .8f, 1.2f);
                inherited[node.Handle] = factor; factors[widgets[node.Handle].Rect] = factor;
            }
            int count = 0;
            foreach (var graphic in current.GetComponentsInChildren<Graphic>(true))
            {
                if (++count > 2048) throw new InvalidOperationException("Native theme graphic quota.");
                Transform ancestor = graphic.transform;
                while (ancestor && !factors.ContainsKey(ancestor)) ancestor = ancestor.parent;
                if (!ancestor) continue;
                // Sampling policy belongs to the host theme/asset binding. A
                // shared capability must not change unaffected artwork's LOD.
                var sampling=graphic.GetComponent<PortableUiAlphaAwareImage>();
                if(sampling)sampling.Apply(graphic);
                float factor = factors[ancestor];
                var effect = graphic.GetComponent<PortableUiBrightness>();
                if (!effect && factor == 1) continue;
                if (!effect) effect = graphic.gameObject.AddComponent<PortableUiBrightness>();
                effect.Apply(graphic, factor);
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
            if (node.Kind == UiNodeKind.Range)
            {
                if (!int.TryParse(node.Text, out int step) || step < 1 || step > 10000 || 10000 % step != 0)
                    throw new InvalidOperationException("Range step must divide normalized 10000 units.");
                var range = widget.Rect.GetComponent<Slider>();
                range.SetValueWithoutNotify(0);
                range.maxValue = 10000 / step;
                range.SetValueWithoutNotify(node.Progress / (float)step);
            }
            if (node.Kind == UiNodeKind.Select)
            {
                var options = node.Text.Split('\n');
                if (options.Length < 1 || options.Length > 16 || node.Progress >= options.Length)
                    throw new InvalidOperationException("Select option/index quota.");
                foreach (var option in options) if (option.Length > 64) throw new InvalidOperationException("Select label quota.");
                var select = widget.Rect.GetComponent<TMP_Dropdown>();
                bool changed = select.options.Count != options.Length;
                for (int i = 0; !changed && i < options.Length; i++) changed = select.options[i].text != options[i];
                if (changed)
                {
                    select.Hide(); select.ClearOptions(); select.AddOptions(new List<string>(options));
                }
                select.SetValueWithoutNotify(node.Progress); select.RefreshShownValue();
            }
            if (factory is INativeUiStyleAdapter styled) styled.ApplyStyle(widget, node);
            if (widget.Image && (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox || node.Kind == UiNodeKind.Button))
                widget.Image.raycastTarget = node.Events != UiEventMask.None;
            var input = widget.Rect.GetComponent<PortableUiNodeInput>();
            if (input) input.Bind(this, node.Handle, node.Events);
            widget.Rect.gameObject.SetActive(node.Visible);
            widget.Rect.SetAsLastSibling();
        }

        public bool Queue(ulong handle, UiEventMask kind, byte[] payload = null)
        {
            if (!active || disposed) return false;
            payload = payload ?? Array.Empty<byte>();
            if (!UiInputPayload.Valid(kind, payload)) return false;
            if (InputSink != null) return InputSink(handle, kind, payload);
            return Session != null && Session.QueueEvent(handle, kind, payload).Success;
        }

        public void QueueFocusPath(ulong handle)
        {
            PortableUiControlFeedback.KeyboardFocus=false;
            keyboardOwner = this;
            for (int depth = 0; depth < 24 && TryGetNode(handle, out var node); depth++)
            {
                if ((node.Events & UiEventMask.Focus) != 0) Queue(handle, UiEventMask.Focus);
                if (node.Parent == 0) break;
                handle = node.Parent;
            }
        }

        public void RouteKey(UiInputKey key, byte modifiers)
        {
            if (!active || disposed || currentSnapshot == null || keyboardOwner != this) return;
            PortableUiControlFeedback.KeyboardFocus=true;
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var widget in currentWidgets.Values)
            {
                var popup = widget.Rect.GetComponent<TMP_Dropdown>();
                if (popup && popup.IsExpanded) return; // Native popup owns keys/cancel.
            }
            if (selected && !selected.transform.IsChildOf(layer)) return;
            if (selected && (selected.GetComponent<TMP_InputField>() || selected.GetComponent<InputField>())) return;
            if (selected && selected.TryGetComponent<TMP_Dropdown>(out var select) &&
                (key == UiInputKey.Up || key == UiInputKey.Down || key == UiInputKey.Left || key == UiInputKey.Right))
            { select.value = Mathf.Clamp(select.value + (key == UiInputKey.Up || key == UiInputKey.Left ? -1 : 1), 0, select.options.Count-1); return; }
            // EventSystem Submit owns activation; never send a second key activation.
            if (selected && selected.GetComponent<Selectable>() &&
                (key == UiInputKey.Enter || key == UiInputKey.Space)) return;
            if (key == UiInputKey.Tab)
            {
                var controls = new List<Selectable>();
                Transform scope = current;
                for (int i = currentSnapshot.Nodes.Count - 1; i >= 0; i--)
                {
                    var node = currentSnapshot.Nodes[i];
                    if (node.Kind == UiNodeKind.Window && currentWidgets.TryGetValue(node.Handle, out var window) && window.Rect.gameObject.activeInHierarchy)
                    { scope = window.Rect; break; }
                }
                foreach (var control in scope.GetComponentsInChildren<Selectable>())
                    if (control.IsActive() && control.IsInteractable()) controls.Add(control);
                if (controls.Count == 0) return;
                int index = controls.FindIndex(control => control.gameObject == selected);
                int next = index < 0 ? ((modifiers & 1) != 0 ? controls.Count - 1 : 0) :
                    (index + ((modifiers & 1) != 0 ? -1 : 1) + controls.Count) % controls.Count;
                controls[next].Select(); return;
            }
            if (selected)
            {
                foreach (var pair in currentWidgets)
                    if (selected.transform == pair.Value.Rect && TryGetNode(pair.Key, out var node) &&
                        (node.Events & UiEventMask.Key) != 0)
                    { Queue(node.Handle, UiEventMask.Key, UiInputPayload.Key(key, true, modifiers)); return; }
            }
            for (int i = currentSnapshot.Nodes.Count - 1; i >= 0; i--)
            {
                var node = currentSnapshot.Nodes[i];
                if ((node.Events & UiEventMask.Key) != 0 && currentWidgets[node.Handle].Rect.gameObject.activeInHierarchy)
                { Queue(node.Handle, UiEventMask.Key, UiInputPayload.Key(key, true, modifiers)); return; }
            }
        }

        void MaintainFocus()
        {
            if (!active || currentSnapshot == null || !EventSystem.current) return;
            ulong top = 0;
            foreach (var node in currentSnapshot.Nodes)
                if (node.Kind == UiNodeKind.Window && currentWidgets[node.Handle].Rect.gameObject.activeInHierarchy) top = node.Handle;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (top == focusedWindow && (top == 0 || selected && selected.activeInHierarchy &&
                selected.transform.IsChildOf(currentWidgets[top].Rect) && selected.TryGetComponent<Selectable>(out var selectable) && selectable.IsInteractable())) return;
            if (focusedWindow == 0 && top != 0) returnFocus = selected;
            focusedWindow = top;
            if (top == 0)
            {
                EventSystem.current.SetSelectedGameObject(returnFocus && returnFocus.activeInHierarchy ? returnFocus : null);
                returnFocus = null; return;
            }
            if (!currentWidgets.TryGetValue(top, out var window)) return;
            if (selected && selected.activeInHierarchy && selected.transform.IsChildOf(window.Rect)) return;
            foreach (var control in window.Rect.GetComponentsInChildren<Selectable>())
                if (control.IsActive() && control.IsInteractable()) { control.Select(); break; }
        }

        static RectTransform NewRoot(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        NativeUiWidget CreateRange(Transform parent, ulong handle)
        {
            var obj = new GameObject("Range", typeof(RectTransform), typeof(Slider));
            var root = (RectTransform)obj.transform; root.SetParent(parent, false);
            Image Part(string name, float left, float right, float height)
            {
                var part = new GameObject(name, typeof(RectTransform), typeof(PortableUiRoundedImage));
                var rect = (RectTransform)part.transform; rect.SetParent(root, false);
                rect.anchorMin = new Vector2(left, .5f); rect.anchorMax = new Vector2(right, .5f);
                rect.sizeDelta = new Vector2(0, height); rect.anchoredPosition = Vector2.zero;
                return part.GetComponent<Image>();
            }
            var track = Part("Range Track", 0, 1, 8);
            var fill = Part("Range Fill", 0, 1, 8);
            var fillArea=new GameObject("Range Fill Area",typeof(RectTransform));
            var fillAreaRect=(RectTransform)fillArea.transform;fillAreaRect.SetParent(root,false);
            fillAreaRect.anchorMin=new Vector2(0,.5f);fillAreaRect.anchorMax=new Vector2(1,.5f);
            fillAreaRect.sizeDelta=new Vector2(0,8);fillAreaRect.anchoredPosition=Vector2.zero;
            fill.rectTransform.SetParent(fillAreaRect,false);fill.rectTransform.sizeDelta=Vector2.zero;
            // Slider drives both anchor axes. A fixed-height parent supplies the
            // control height; a stretched child's sizeDelta must not add to it.
            var thumb = Part("Range Handle", 0, 0, 16); thumb.rectTransform.sizeDelta = new Vector2(15,0);
            var handleArea = new GameObject("Range Handle Area",typeof(RectTransform));
            var area=(RectTransform)handleArea.transform;area.SetParent(root,false);
            area.anchorMin=Vector2.zero;area.anchorMax=Vector2.one;area.offsetMin=new Vector2(8,0);area.offsetMax=new Vector2(-8,0);
            thumb.rectTransform.SetParent(area,false);
            var slider = obj.GetComponent<Slider>(); slider.minValue = 0; slider.maxValue = 100;
            slider.wholeNumbers = true; slider.fillRect = fill.rectTransform; slider.handleRect = thumb.rectTransform;
            slider.targetGraphic = thumb; slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(value => Queue(handle, UiEventMask.ValueChange,
                UiInputPayload.Value((ushort)Mathf.RoundToInt(value / slider.maxValue * 10000))));
            return new NativeUiWidget(root, track, null, null);
        }

        NativeUiWidget CreateSelect(Transform parent, ulong handle)
        {
            var obj = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            obj.name = "Select"; obj.transform.SetParent(parent, false);
            var select = obj.GetComponent<TMP_Dropdown>();
            select.navigation = new Navigation { mode = Navigation.Mode.None };
            select.onValueChanged.AddListener(value => Queue(handle, UiEventMask.ValueChange, UiInputPayload.Value((ushort)value)));
            return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, null);
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
            if (keyboardOwner == this) keyboardOwner = null;
            if (layer) { layer.gameObject.SetActive(false); DestroyOwned(layer.gameObject); }
        }
        static void DestroyOwned(GameObject value)
        { if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value); }
    }
}
