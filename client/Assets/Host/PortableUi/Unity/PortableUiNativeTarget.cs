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
        readonly INativeUiSurfaceFactory surfaces;
        readonly PortableUiWorldRegistry world;
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
        // A surrounding game shell can own Escape while retaining native widget keys.
        public bool ExternalEscapeRouting {get;set;}
        internal bool SurfaceInputActive => active && !disposed;

        public bool TryDesignPoint(Vector2 screen, Camera camera, out Vector2 point)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, camera, out var local))
            { point = default; return false; }
            point = new Vector2(local.x - canvas.rect.xMin, canvas.rect.yMax - local.y);
            return true;
        }

        public PortableUiNativeTarget(RectTransform canvasRoot, INativeUiWidgetFactory widgetFactory, INativeUiSurfaceFactory surfaceFactory = null, PortableUiWorldRegistry worldRegistry = null)
        {
            canvas = canvasRoot ? canvasRoot : throw new ArgumentNullException(nameof(canvasRoot));
            factory = widgetFactory ?? throw new ArgumentNullException(nameof(widgetFactory));
            surfaces = surfaceFactory; world = worldRegistry;
            layer = NewRoot("Portable UI guest layer", canvas);
            layer.gameObject.AddComponent<PortableUiKeyboardInput>().Bind(this);
            layer.gameObject.SetActive(false);
        }

        public void Activate()
        {
            active = true; keyboardOwner = this;
            if (layer)
            {
                layer.gameObject.SetActive(true);
                foreach (var surface in layer.GetComponentsInChildren<PortableUiSurfaceView>(true)) surface.Synchronize();
            }
            MaintainFocus();
        }
        public void SuspendInput()
        {
            active=false;
            if(keyboardOwner==this)keyboardOwner=null;
            if(!layer)return;
            foreach(var field in layer.GetComponentsInChildren<PortableUiTextInput>(true))field.RetireFocus();
            foreach(var select in layer.GetComponentsInChildren<TMP_Dropdown>(true))select.Hide();
            foreach(var selectable in layer.GetComponentsInChildren<Selectable>(true))selectable.interactable=false;
            foreach(var graphic in layer.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            foreach(var input in layer.GetComponentsInChildren<PortableUiNodeInput>(true))input.enabled=false;
            foreach(var surface in layer.GetComponentsInChildren<PortableUiSurfaceView>(true))surface.Cancel();
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
            if (proposed == null || !UiPresentation.Validate(proposed,out error)) return false;
            foreach (var node in proposed.Nodes)
                if (node.WorldAnchor != null && (world == null ||
                    !(TryGetNode(node.Handle,out var old) && old.WorldAnchor?.Entity==node.WorldAnchor.Entity && old.WorldAnchor?.Attachment==node.WorldAnchor.Attachment) && !world.Allows(node.Handle,node.WorldAnchor)))
                { error="World attachment is not admitted for this owner and generation."; return false; }
            if (TryWindowTranslation(proposed)) return true;
            var staged = new Dictionary<ulong, NativeUiWidget>();
            RectTransform stage = null;
            var selectedBefore = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var focusPath = selectedBefore && selectedBefore.transform.IsChildOf(layer)
                ? selectedBefore.GetComponentsInParent<Selectable>() : Array.Empty<Selectable>();
            try
            {
                // Prepare new widgets off-tree before touching retained keyed objects.
                foreach (var node in proposed.Nodes)
                {
                    if (node.Kind == UiNodeKind.Viewport && surfaces == null)
                        throw new InvalidOperationException("Live surface provider is not configured.");
                    if (node.Kind != UiNodeKind.Viewport && !string.IsNullOrEmpty(node.AssetKey) &&
                        (!factory.TryGetSprite(node.AssetKey, out var sprite) || !sprite))
                        throw new InvalidOperationException("Asset key unavailable: " + node.AssetKey);
                    if (currentWidgets.TryGetValue(node.Handle, out var existing) &&
                        (node.Kind != UiNodeKind.Viewport || existing.Rect.GetComponent<PortableUiSurfaceView>().SurfaceKey == node.AssetKey)) continue;
                    if (!stage) { stage = NewRoot("Candidate", layer); stage.gameObject.SetActive(false); }
                    NativeUiWidget widget;
                    if (node.Kind == UiNodeKind.Viewport)
                    {
                        var surface = new GameObject("Live viewport", typeof(RectTransform), typeof(RawImage), typeof(PortableUiSurfaceView));
                        surface.transform.SetParent(stage, false);
                        var rect = (RectTransform)surface.transform; SetRect(rect, node.Rect);
                        surface.GetComponent<PortableUiSurfaceView>().Prepare(surfaces.Acquire(node.AssetKey), node.AssetKey, this, node.Handle);
                        widget = new NativeUiWidget(rect, null, null, null);
                    }
                    else if (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox || node.Kind == UiNodeKind.Button)
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
                    else if (node.Kind == UiNodeKind.TextInput) widget = CreateTextInput(stage, node.Handle);
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
                    bool isStaged = staged.TryGetValue(node.Handle, out var prepared);
                    var widget = isStaged ? prepared : currentWidgets[node.Handle];
                    if (!isStaged) ApplyNode(widget, node);
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
                foreach (var widget in nextWidgets.Values)
                    widget.Rect.GetComponent<PortableUiSurfaceView>()?.Synchronize();
                var removed = new HashSet<ulong>();
                foreach(var old in currentWidgets) if(!nextWidgets.TryGetValue(old.Key,out var next) || next.Rect != old.Value.Rect)removed.Add(old.Key);
                foreach (var old in currentWidgets)
                    if (removed.Contains(old.Key))
                    {
                        // Retire each removed subtree once. DestroyImmediate in
                        // authoring/tests would otherwise invalidate child entries.
                        if (TryGetNode(old.Key,out var oldNode) && removed.Contains(oldNode.Parent)) continue;
                        old.Value.Rect.gameObject.SetActive(false);
                        DestroyOwned(old.Value.Rect.gameObject);
                    }
                currentWidgets = nextWidgets;
                currentSnapshot = proposed;
                foreach(var widget in currentWidgets.Values)widget.Rect.GetComponent<PortableUiWorldView>()?.Present(Time.unscaledTimeAsDouble);
                ++CommitCount;
                MaintainFocus(focusPath);
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

        bool TryWindowTranslation(UiSnapshot proposed)
        {
            if(currentSnapshot==null || proposed==null || proposed.Generation!=currentSnapshot.Generation ||
                proposed.Nodes.Count!=currentSnapshot.Nodes.Count)return false;
            bool moved=false;
            for(int i=0;i<proposed.Nodes.Count;i++)
            {
                var next=proposed.Nodes[i];var old=currentSnapshot.Nodes[i];
                if(next.Handle!=old.Handle || next.Parent!=old.Parent || next.Kind!=old.Kind || next.Order!=old.Order ||
                    next.StyleKey!=old.StyleKey || next.AssetKey!=old.AssetKey || next.Text!=old.Text ||
                    next.TextInput != old.TextInput || next.WorldAnchor != old.WorldAnchor || next.Effect != old.Effect ||
                    next.Progress!=old.Progress || next.Visible!=old.Visible || next.Events!=old.Events || next.Brightness!=old.Brightness ||
                    next.Rect.Width!=old.Rect.Width || next.Rect.Height!=old.Rect.Height)return false;
                if(next.Rect.X!=old.Rect.X || next.Rect.Y!=old.Rect.Y)
                {
                    if(next.Kind!=UiNodeKind.Window || !currentWidgets.TryGetValue(next.Handle,out var widget) || !widget.Rect)return false;
                    moved=true;
                }
            }
            if(!moved)return false;
            // All presentation, child geometry and sibling order are unchanged.
            // Unity moves the retained subtree without rebinding textures/styles/text.
            for(int i=0;i<proposed.Nodes.Count;i++)
            {
                var next=proposed.Nodes[i];var old=currentSnapshot.Nodes[i];
                if(next.Rect.X!=old.Rect.X || next.Rect.Y!=old.Rect.Y)SetRect(currentWidgets[next.Handle].Rect,next.Rect);
            }
            currentSnapshot=proposed;++CommitCount;return true;
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
                if (effect) factor *= effect.ThemeFactor;
                if (!effect && factor == 1) continue;
                if (!effect) effect = graphic.gameObject.AddComponent<PortableUiBrightness>();
                effect.Apply(graphic, factor);
            }
        }

        void ApplyNode(NativeUiWidget widget, UiNode node)
        {
            SetRect(widget.Rect, node.Rect);
            if (widget.Label && node.Kind != UiNodeKind.TextInput) { widget.Label.text = node.Text ?? ""; widget.Label.richText = false; }
            if (node.Kind == UiNodeKind.TextInput) widget.Rect.GetComponent<PortableUiTextInput>().Apply(node);
            var presentation=widget.Rect.GetComponent<PortableUiWorldView>();
            if(node.WorldAnchor!=null || node.Effect!=null || presentation)
            {
                if(!presentation)presentation=widget.Rect.gameObject.AddComponent<PortableUiWorldView>();
                presentation.Apply(node,world,Time.unscaledTimeAsDouble);
            }
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
            if (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox)
            {
                bool focusable = (node.Events & UiEventMask.Focus) != 0;
                var selectable = widget.Rect.GetComponent<Selectable>();
                if (focusable && !selectable)
                {
                    selectable = widget.Rect.gameObject.AddComponent<Selectable>();
                    selectable.navigation = new Navigation { mode = Navigation.Mode.None };
                    selectable.transition = Selectable.Transition.None;
                }
                if (selectable) selectable.enabled = focusable;
            }
            if (widget.Image && (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox || node.Kind == UiNodeKind.Button))
                // Global keyboard listeners must not cover the world with an invisible mouse target.
                // Focus remains pointer-active so clickable keyboard controls can still be selected.
                widget.Image.raycastTarget = (node.Events & ~UiEventMask.Key) != UiEventMask.None;
            var input = widget.Rect.GetComponent<PortableUiNodeInput>();
            if (input) input.Bind(this, node.Handle, node.Events);
            widget.Rect.gameObject.SetActive(node.Visible);
            widget.Rect.SetAsLastSibling();
        }

        public bool Queue(ulong handle, UiEventMask kind, byte[] payload = null)
        {
            if (!active || disposed) return false;
            if(currentWidgets.TryGetValue(handle,out var source))
            {
                if(!source.Rect.gameObject.activeInHierarchy)return false;
                for(Transform cursor=source.Rect;cursor && cursor!=layer;cursor=cursor.parent)
                {var presentation=cursor.GetComponent<PortableUiWorldView>();if(presentation && !presentation.Shown)return false;}
            }
            payload = payload ?? Array.Empty<byte>();
            if (!UiInputPayload.Valid(kind, payload)) return false;
            if (InputSink != null) return InputSink(handle, kind, payload);
            return Session != null && Session.QueueEvent(handle, kind, payload).Success;
        }

        public void QueueWheelPath(ulong handle, byte[] payload)
        {
            for (int depth = 0; depth < 24 && TryGetNode(handle, out var node); depth++)
            {
                if (!node.Visible) return;
                if ((node.Events & UiEventMask.Wheel) != 0)
                { Queue(handle, UiEventMask.Wheel, payload); return; }
                if (node.Parent == 0) return;
                handle = node.Parent;
            }
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

        public void RouteTextTab(PortableUiTextInput field, bool backwards)
        {
            if (!active || disposed || !field || !field.transform.IsChildOf(layer)) return;
            var controls = new List<Selectable>();
            Transform scope = field.transform;
            while (scope.parent && scope.parent != current) scope = scope.parent;
            foreach (var control in scope.GetComponentsInChildren<Selectable>())
                if (control.IsActive() && control.IsInteractable()) controls.Add(control);
            if (controls.Count < 2) return;
            int index = controls.IndexOf(field);
            controls[(index + (backwards ? -1 : 1) + controls.Count) % controls.Count].Select();
        }

        void MaintainFocus(Selectable[] retainedFocusPath = null)
        {
            if (!active || currentSnapshot == null || !EventSystem.current) return;
            ulong top = 0;
            foreach (var node in currentSnapshot.Nodes)
                if (node.Kind == UiNodeKind.Window && currentWidgets[node.Handle].Rect.gameObject.activeInHierarchy) top = node.Handle;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (top == focusedWindow && selected && !selected.transform.IsChildOf(layer)) return;
            if (top == focusedWindow && (top == 0 || selected && selected.activeInHierarchy &&
                selected.transform.IsChildOf(currentWidgets[top].Rect) && selected.TryGetComponent<Selectable>(out var selectable) && selectable.IsActive() && selectable.IsInteractable())) return;
            if (focusedWindow == 0 && top != 0) returnFocus = selected;
            focusedWindow = top;
            if (top == 0)
            {
                EventSystem.current.SetSelectedGameObject(returnFocus && returnFocus.activeInHierarchy ? returnFocus : null);
                returnFocus = null; return;
            }
            if (!currentWidgets.TryGetValue(top, out var window)) return;
            if (selected && selected.activeInHierarchy && selected.transform.IsChildOf(window.Rect) && selected.TryGetComponent<Selectable>(out var retained) && retained.IsActive() && retained.IsInteractable()) return;
            if (retainedFocusPath != null)
                foreach (var ancestor in retainedFocusPath)
                    if (ancestor && ancestor.IsActive() && ancestor.IsInteractable() && ancestor.transform.IsChildOf(window.Rect))
                    { ancestor.Select(); return; }
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

        NativeUiWidget CreateTextInput(Transform parent, ulong handle)
        {
            var obj = new GameObject("Text Input", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var viewport = NewRoot("Text Viewport", obj.transform);
            viewport.offsetMin = new Vector2(6, 2); viewport.offsetMax = new Vector2(-6, -2);
            viewport.gameObject.AddComponent<RectMask2D>();
            var textWidget = factory.Create(UiNodeKind.Text, viewport);
            if (!textWidget.Label) throw new InvalidOperationException("Text factory must provide a TMP label.");
            var label = textWidget.Label;
            label.richText = false; label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var field = obj.AddComponent<PortableUiTextInput>();
            field.textViewport = viewport; field.textComponent = label;
            field.targetGraphic = obj.GetComponent<Image>();
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.Bind(this, handle);
            return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, label);
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
            world?.Dispose();
            if (keyboardOwner == this) keyboardOwner = null;
            if (layer) { layer.gameObject.SetActive(false); DestroyOwned(layer.gameObject); }
            (factory as INativeUiScopeLifetime)?.Dispose();
        }
        static void DestroyOwned(GameObject value)
        {
            // Inactive candidates may never receive Unity's OnDestroy callback.
            foreach (var surface in value.GetComponentsInChildren<PortableUiSurfaceView>(true)) surface.Release();
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
