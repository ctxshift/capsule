using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Capsule;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleWorldPointerTests
    {
        sealed class Factory : INativeUiWidgetFactory
        {
            public NativeUiWidget Create(UiNodeKind kind, Transform parent)
            {
                var obj = new GameObject("Pointer fixture", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                obj.transform.SetParent(parent, false);
                return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, null);
            }
            public bool TryGetSprite(string key, out Sprite sprite) { sprite = null; return false; }
        }

        [UnityTest]
        public IEnumerator KeyboardWorkspacePassesWorldClicksButChildControlsStillBlock()
        {
            var root = new GameObject("World pointer canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var previousEvents = EventSystem.current;
            var events = new GameObject("World pointer events", typeof(EventSystem));
            using var target = new CapsuleNativeTarget((RectTransform)root.transform, new Factory());
            int keys = 0;
            target.InputSink = (handle, kind, bytes) => { if (handle == 1 && kind == UiEventMask.Key) keys++; return true; };
            UiSnapshot Snapshot(UiEventMask rootEvents) => new UiSnapshot(1, new[] {
                new UiNode(1, 0, UiNodeKind.Box, new UiRect(0, 0, Screen.width * 64, Screen.height * 64), "", "", "", 0, true, rootEvents, 1),
                new UiNode(2, 1, UiNodeKind.Box, new UiRect(10 * 64, 10 * 64, 80 * 64, 80 * 64), "", "", "", 0, true, UiEventMask.Pointer | UiEventMask.Hover, 2)
            });
            try
            {
                // First reproduce the original all-screen blocker, then replace its event contract.
                Assert.That(target.TryCommit(Snapshot(UiEventMask.Key | UiEventMask.Pointer), out var error), Is.True, error);
                target.Activate();
                yield return null;
                var raycaster = root.GetComponent<GraphicRaycaster>();
                var hits = new List<RaycastResult>();
                var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { position = new Vector2(Screen.width * .75f, Screen.height * .25f) };
                Canvas.ForceUpdateCanvases();
                raycaster.Raycast(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(target.TryCommit(Snapshot(UiEventMask.Key), out error), Is.True, error);
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var button in new[] { PointerEventData.InputButton.Left, PointerEventData.InputButton.Right })
                {
                    pointer.button = button;
                    hits.Clear(); raycaster.Raycast(pointer, hits);
                    Assert.That(hits, Is.Empty, "World clicks must reach camera orbit/steering.");
                }
                target.TryGetNativeWidget(2, out var child);
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, child.TransformPoint(child.rect.center));
                hits.Clear(); raycaster.Raycast(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(hits[0].gameObject, Is.EqualTo(child.gameObject), "Real UI controls still intercept mouse input.");
                target.RouteKey(UiInputKey.Escape, 0);
                Assert.That(keys, Is.EqualTo(1), "Keyboard routing must survive without a raycast target.");
            }
            finally
            {
                target.Dispose();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(events);
                if (previousEvents) EventSystem.current = previousEvents;
            }
        }
    }
}
