using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Capsule.Unity
{
    // TMP owns editing, selection and clipboard. The adapter only fences semantic
    // events and programmatic commands at the addon boundary.
    public sealed class CapsuleTextInput : TMP_InputField
    {
        CapsuleNativeTarget owner;
        ulong handle;
        UiTextInputOptions options = UiTextInputOptions.Default;
        string commanded, accepted = "", composition = "";
        uint revision;
        int anchor, focus, compositionFrame = -2, submitFrame = -2;
        bool entered, programmaticPending, enterHeld, openingEnterHeld;
        readonly Event nativeEvent = new Event();
        static int consumedFrame = -1;
        // The surrounding screen may finish editing on submit (chat) or keep focus (forms).
        public bool BlurOnSubmit { get; set; }
        public static bool OwnsKeyboard
        {
            get
            {
                if (consumedFrame == Time.frameCount) return true;
                var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected ? selected.GetComponent<CapsuleTextInput>() : null;
                return field && field.isActiveAndEnabled;
            }
        }

        string NativeComposition => readOnly ? "" : EventSystem.current && EventSystem.current.currentInputModule && EventSystem.current.currentInputModule.input
            ? EventSystem.current.currentInputModule.input.compositionString : Input.compositionString;

        public void Bind(CapsuleNativeTarget target, ulong source) { owner = target; handle = source; }
        public void Apply(UiNode node)
        {
            options = node.TextInput;
            readOnly = options.ReadOnly;
            // Validate a whole native operation, without truncating Unicode pairs.
            characterLimit = 0;
            lineType = LineType.SingleLine;
            restoreOriginalTextOnEscape = false;
            if (composition.Length != 0 && (readOnly || commanded != node.Text || revision != options.Revision))
            {
                if (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
                composition = "";
            }
            if (commanded != node.Text || revision != options.Revision || !options.Accepts(accepted))
            {
                bool initial = commanded == null;
                // While editing, an addon snapshot can echo an earlier draft after
                // newer native characters were accepted. Only a new revision may
                // replace that local draft before focus leaves the field.
                bool nativeDraftOwnsRevision = !initial && isFocused &&
                    revision == options.Revision && options.Accepts(accepted);
                commanded = node.Text; revision = options.Revision;
                if (!nativeDraftOwnsRevision)
                {
                    accepted = node.Text;
                    SetTextWithoutNotify(accepted);
                    anchor = focus = accepted.Length;
                    selectionStringAnchorPosition = anchor; selectionStringFocusPosition = focus;
                    programmaticPending = !initial;
                }
            }
        }

        void Emit(UiTextEditPhase phase, bool userInput = true)
        {
            if (owner == null || !isActiveAndEnabled) return;
            bool composing = composition.Length != 0;
            var value = new UiTextEdit(phase, accepted, composition, (ushort)anchor, (ushort)focus, composing, userInput);
            try { owner.Queue(handle, UiEventMask.TextEdit, value.Encode()); }
            catch (ArgumentException) { /* Provisional platform text can exceed the transport quota. */ }
        }
        static int Boundary(string text, int offset)
        {
            offset = Mathf.Clamp(offset, 0, text.Length);
            if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset])) --offset;
            return offset;
        }
        void AcceptDraft()
        {
            if (composition.Length != 0) return;
            if (text != accepted && (!options.Accepts(text) || readOnly))
            {
                SetTextWithoutNotify(accepted);
                selectionStringAnchorPosition = anchor; selectionStringFocusPosition = focus;
                return;
            }
            bool changed = accepted != text;
            accepted = text;
            anchor = Boundary(accepted, selectionStringAnchorPosition); focus = Boundary(accepted, selectionStringFocusPosition);
            if (changed) Emit(UiTextEditPhase.Change);
        }
        void ObserveComposition()
        {
            string next = NativeComposition ?? "";
            if (next.Length != 0 || composition.Length != 0) compositionFrame = Time.frameCount;
            if (composition == next) return;
            composition = next;
            if (composition.Length == 0) AcceptDraft();
            Emit(UiTextEditPhase.Composition);
        }
        protected override void LateUpdate()
        {
            base.LateUpdate();
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasReleasedThisFrame || keyboard.numpadEnterKey.wasReleasedThisFrame))
            { openingEnterHeld = false; enterHeld = false; }
            if (programmaticPending) { programmaticPending = false; Emit(UiTextEditPhase.Change, false); }
            if (!isFocused) return;
            ObserveComposition(); AcceptDraft();
        }

        // TMP's KeyPressed finishes on Enter even while IME is composing. Filter
        // only semantic keys here; all text operations still use TMP's editor.
        public bool ProcessNativeEvent(Event data)
        {
            if (!isActiveAndEnabled || !IsInteractable()) return false;
            consumedFrame = Time.frameCount;
            bool enter = data.keyCode == KeyCode.Return || data.keyCode == KeyCode.KeypadEnter;
            if (data.rawType == EventType.KeyUp)
            {
                if (enter) { enterHeld = false; openingEnterHeld = false; }
                return true;
            }
            ObserveComposition();
            bool semantic = data.keyCode == KeyCode.Return || data.keyCode == KeyCode.KeypadEnter || data.keyCode == KeyCode.Escape || data.keyCode == KeyCode.Tab;
            if (semantic)
            {
                if (enter && enterHeld) return true;
                if (enter) enterHeld = true;
                if (composition.Length != 0 || compositionFrame == Time.frameCount) return true;
                if (data.keyCode == KeyCode.Return || data.keyCode == KeyCode.KeypadEnter) SubmitDraft();
                else if (data.keyCode == KeyCode.Escape) CancelDraft();
                else { AcceptDraft(); owner?.RouteTextTab(this, data.shift); }
                return true;
            }
            KeyPressed(data);
            return false;
        }
        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!IsInteractable()) return;
            if (!isFocused) { base.OnUpdateSelected(eventData); return; }
            while (Event.PopEvent(nativeEvent))
            {
                if (nativeEvent.rawType == EventType.KeyDown || nativeEvent.rawType == EventType.KeyUp) ProcessNativeEvent(nativeEvent);
                else if ((nativeEvent.rawType == EventType.ValidateCommand || nativeEvent.rawType == EventType.ExecuteCommand) && nativeEvent.commandName == "SelectAll") SelectAll();
            }
            ObserveComposition(); AcceptDraft(); ForceLabelUpdate(); eventData.Use();
        }
        void SubmitDraft()
        {
            if (openingEnterHeld || !isFocused || readOnly || composition.Length != 0 || compositionFrame == Time.frameCount || submitFrame == Time.frameCount) return;
            AcceptDraft(); submitFrame = Time.frameCount; Emit(UiTextEditPhase.Submit);
            if (BlurOnSubmit)
            {
                if (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
                else DeactivateInputField();
            }
        }
        void CancelDraft()
        {
            if (!isFocused || composition.Length != 0 || compositionFrame == Time.frameCount) return;
            AcceptDraft(); Emit(UiTextEditPhase.Cancel);
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject) EventSystem.current.SetSelectedGameObject(null);
            else DeactivateInputField();
        }
        // The Enter that opened a field belongs to focus, not to its first submission.
        public void SuppressOpeningEnter()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            openingEnterHeld = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
        }
        internal void RouteSubmitKey() { ObserveComposition(); SubmitDraft(); }
        internal void RouteCancelKey() => CancelDraft();
        public override void OnSubmit(BaseEventData data)
        {
            ObserveComposition();
            if (!enterHeld) { enterHeld = true; SubmitDraft(); }
            data?.Use();
        }
        public override void OnCancel(BaseEventData data) { ObserveComposition(); CancelDraft(); data?.Use(); }
        public override void OnSelect(BaseEventData data)
        {
            base.OnSelect(data); entered = true; Emit(UiTextEditPhase.Focus);
        }
        public override void OnDeselect(BaseEventData data)
        {
            consumedFrame = Time.frameCount;
            base.OnDeselect(data); composition = ""; AcceptDraft();
            if (entered) Emit(UiTextEditPhase.Blur);
            entered = false; enterHeld = false;
        }
        protected override void OnDisable()
        {
            if (entered) consumedFrame = Time.frameCount;
            entered = false; enterHeld = false; composition = ""; programmaticPending = false;
            base.OnDisable();
        }
        void OnApplicationFocus(bool focused)
        {
            if (!focused) RetireFocus();
        }
        public void RetireFocus()
        {
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            else if (isFocused) DeactivateInputField();
            composition = ""; enterHeld = false;
        }
    }
}
