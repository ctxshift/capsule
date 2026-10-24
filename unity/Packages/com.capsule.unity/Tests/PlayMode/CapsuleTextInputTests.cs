using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Capsule;
using Capsule.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class TextCompositionInput : BaseInput
    {
        public string Composition = "";
        public override string compositionString => Composition;
    }
    public sealed class CapsuleTextInputTests
    {
        sealed class Factory : INativeUiWidgetFactory, INativeUiScopeLifetime
        {
            readonly TMP_FontAsset font = TMP_FontAsset.CreateFontAsset("Arial", "Regular", 90);
            public NativeUiWidget Create(UiNodeKind kind, Transform parent)
            {
                var obj = new GameObject("Text fixture", typeof(RectTransform)); obj.transform.SetParent(parent, false);
                TMP_Text text = kind == UiNodeKind.Text ? obj.AddComponent<TextMeshProUGUI>() : null;
                if (text) { Assert.That(font, Is.Not.Null, "Windows Arial test font is required; no font is redistributed."); text.font = font; }
                return new NativeUiWidget((RectTransform)obj.transform, null, null, text);
            }
            public bool TryGetSprite(string key, out Sprite sprite) { sprite = null; return false; }
            public void Dispose()
            {
                if (!font) return;
                foreach (var atlas in font.atlasTextures) if (atlas) UnityEngine.Object.Destroy(atlas);
                if (font.material) UnityEngine.Object.Destroy(font.material);
                UnityEngine.Object.Destroy(font);
            }
        }
        [UnityTest]
        public IEnumerator ChatDraftKeepsFocusAcrossUnrelatedWindowsAndStaleEchoes()
        {
            var prior = EventSystem.current;
            var events = new GameObject("Chat events", typeof(EventSystem));
            EventSystem.current = events.GetComponent<EventSystem>();
            var canvas = new GameObject("Chat canvas", typeof(RectTransform), typeof(Canvas));
            using var target = new CapsuleNativeTarget((RectTransform)canvas.transform, new Factory());
            target.InputSink = (handle, kind, bytes) => true;
            var nodes = new[] {
                new UiNode(1, 0, UiNodeKind.TextInput, new UiRect(0, 0, 200*64, 40*64), "", "", "", 0, true, UiEventMask.TextEdit, 0,
                    textInput: new UiTextInputOptions(128, 128, false, false, 0)),
                new UiNode(2, 0, UiNodeKind.Window, new UiRect(0, 100*64, 300*64, 200*64), "", "", "Inventory", 0, true, UiEventMask.None, 1),
                new UiNode(3, 2, UiNodeKind.Button, new UiRect(0, 0, 100*64, 40*64), "", "", "", 0, true, UiEventMask.Activate, 2) };
            try
            {
                Assert.That(target.TryCommit(new UiSnapshot(1, nodes), out var error), Is.True, error);
                target.Activate();
                target.TryGetNativeWidget(1, out var rect);
                var field = rect.GetComponent<CapsuleTextInput>();
                field.Select(); field.ActivateInputField(); yield return null;
                string draft = "";
                foreach (char letter in "/unstick")
                {
                    string staleEcho = draft;
                    field.ProcessNativeEvent(new Event { type = EventType.KeyDown, character = letter });
                    draft += letter;
                    yield return null;
                    nodes[0] = nodes[0].With(text: staleEcho);
                    Assert.That(target.TryCommit(new UiSnapshot(1, nodes), out error), Is.True, error);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(field.gameObject));
                    Assert.That(field.isFocused, Is.True);
                    Assert.That(field.text, Is.EqualTo(draft),
                        "An earlier addon draft echo cannot erase newer native input");
                }
                Assert.That(field.text, Is.EqualTo("/unstick"));
            }
            finally
            {
                target.Dispose(); UnityEngine.Object.DestroyImmediate(canvas); UnityEngine.Object.DestroyImmediate(events);
                if (prior) EventSystem.current = prior;
            }
        }

        [UnityTest]
        public IEnumerator NativeDraftCompositionFocusLimitsAndCleanup()
        {
            var previous = EventSystem.current;
            var eventObject = new GameObject("Text events", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule), typeof(TextCompositionInput));
            var events = eventObject.GetComponent<EventSystem>(); EventSystem.current = events;
            var composition = eventObject.GetComponent<TextCompositionInput>();
            eventObject.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().inputOverride = composition;
            var canvas = new GameObject("Text canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var received = new List<UiTextEdit>(); int keys = 0;
            using var target = new CapsuleNativeTarget((RectTransform)canvas.transform, new Factory());
            target.InputSink = (handle, kind, bytes) => {
                if (kind == UiEventMask.TextEdit && UiTextEdit.TryDecode(bytes, out var edit)) received.Add(edit);
                if (kind == UiEventMask.Key) ++keys;
                return true;
            };
            var root = new UiNode(1, 0, UiNodeKind.Window, new UiRect(0,0,300*64,100*64), "", "", "", 0, true, UiEventMask.Key, 1);
            var node = new UiNode(2, 1, UiNodeKind.TextInput, new UiRect(0,0,240*64,40*64), "", "", "", 0, true, UiEventMask.TextEdit, 2,
                textInput: new UiTextInputOptions(8, 8, false, false, 0));
            void Apply() => Assert.That(target.TryCommit(new UiSnapshot(1, new[] {root,node}), out var error), Is.True, error);
            try
            {
                Apply(); target.Activate(); target.TryGetNativeWidget(2, out var rect);
                var field = rect.GetComponent<CapsuleTextInput>();
                field.Select(); field.ActivateInputField(); yield return null; yield return null;
                Assert.That(field.isFocused, Is.True);
                Assert.That(CapsuleTextInput.OwnsKeyboard, Is.True);
                yield return null;
                target.RouteKey(UiInputKey.J,0); Assert.That(keys, Is.Zero);
                field.ProcessNativeEvent(new Event {type=EventType.KeyDown,keyCode=KeyCode.A,character='a'});
                yield return null;
                Assert.That(received.Last(e=>e.Phase==UiTextEditPhase.Change).Text, Is.EqualTo("a"));
                field.selectionStringAnchorPosition=0;field.selectionStringFocusPosition=1;
                Apply(); Assert.That(field.text,Is.EqualTo("a"));
                Assert.That(field.selectionStringAnchorPosition,Is.Zero); Assert.That(field.selectionStringFocusPosition,Is.EqualTo(1));

                composition.Composition="日"; yield return null;
                field.ProcessNativeEvent(new Event {type=EventType.KeyDown,keyCode=KeyCode.Return});
                field.OnSubmit(new BaseEventData(events));
                Assert.That(received.Any(e=>e.Phase==UiTextEditPhase.Submit),Is.False);
                Assert.That(field.isFocused,Is.True);
                Assert.That(received.Any(e=>e.Phase==UiTextEditPhase.Composition && e.Composition=="日"),Is.True);
                field.ProcessNativeEvent(new Event {type=EventType.KeyUp,keyCode=KeyCode.Return});
                composition.Composition=""; field.text="日"; yield return null; yield return null;
                field.OnSubmit(new BaseEventData(events));
                Assert.That(received.Last(e=>e.Phase==UiTextEditPhase.Submit).Text,Is.EqualTo("日"));
                int firstSubmits = received.Count(e=>e.Phase==UiTextEditPhase.Submit);
                yield return null; field.ProcessNativeEvent(new Event {type=EventType.KeyDown,keyCode=KeyCode.Return});
                Assert.That(received.Count(e=>e.Phase==UiTextEditPhase.Submit),Is.EqualTo(firstSubmits),"Held Enter cannot repeat submit.");
                field.ProcessNativeEvent(new Event {type=EventType.KeyUp,keyCode=KeyCode.Return});
                field.text="😀😀😀";yield return null;
                Assert.That(field.text,Is.EqualTo("日"),"Reject a whole edit exceeding UTF-8 budget.");

                node=node.With(text:"42",textInput:new UiTextInputOptions(8,8,false,true,1));Apply();yield return null;
                Assert.That(received.Last(e=>e.Phase==UiTextEditPhase.Change).UserInput,Is.False);
                field.ProcessNativeEvent(new Event {type=EventType.KeyDown,keyCode=KeyCode.X,character='x'});yield return null;
                Assert.That(field.text,Is.EqualTo("42"));
                node=node.With(textInput:new UiTextInputOptions(8,8,true,true,1));Apply();
                field.text="9";yield return null;Assert.That(field.text,Is.EqualTo("42"));
                int submits=received.Count(e=>e.Phase==UiTextEditPhase.Submit);
                field.OnSubmit(new BaseEventData(events));Assert.That(received.Count(e=>e.Phase==UiTextEditPhase.Submit),Is.EqualTo(submits));
                field.ProcessNativeEvent(new Event {type=EventType.KeyDown,keyCode=KeyCode.Escape});
                Assert.That(events.currentSelectedGameObject,Is.Null);
                Assert.That(received.Last().Phase,Is.EqualTo(UiTextEditPhase.Blur));
                Assert.That(CapsuleTextInput.OwnsKeyboard,Is.True,"Escape stays consumed for its frame.");
                yield return null;Assert.That(CapsuleTextInput.OwnsKeyboard,Is.False);
                field.Select();field.ActivateInputField();yield return null;
                target.SuspendInput();Assert.That(field.isFocused,Is.False);Assert.That(events.currentSelectedGameObject,Is.Null);
                target.Dispose();int count=received.Count;field.onValueChanged.Invoke("stale");yield return null;
                Assert.That(received.Count,Is.EqualTo(count));Assert.That(field==null,Is.True);
            }
            finally
            {
                target.Dispose(); UnityEngine.Object.DestroyImmediate(eventObject);
                if(previous)EventSystem.current=previous;
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }
    }
}
