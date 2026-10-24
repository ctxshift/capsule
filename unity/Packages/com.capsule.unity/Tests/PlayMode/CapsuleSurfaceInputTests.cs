using System;
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
    public sealed class CapsuleSurfaceInputTests
    {
        sealed class Provider : INativeUiWidgetFactory, INativeUiSurfaceFactory, INativeUiSurfaceLease
        {
            public int Downs, Scrolls, Disposals;
            public NativeUiWidget Create(UiNodeKind kind, Transform parent) => throw new NotSupportedException();
            public bool TryGetSprite(string key, out Sprite sprite) { sprite = null; return false; }
            public INativeUiSurfaceLease Acquire(string key) => this;
            public Texture Resize(int width, int height) => Texture2D.whiteTexture;
            public void SetVisible(bool visible) { }
            public void Pointer(UiPointerPhase phase, int id, Vector2 point, PointerEventData.InputButton button)
            { if (phase == UiPointerPhase.Down) ++Downs; }
            public void Scroll(Vector2 point, Vector2 delta) => ++Scrolls;
            public void Dispose() => ++Disposals;
        }
        [UnityTest]
        public IEnumerator OverlayInterceptsSceneInput()
        {
            var root = new GameObject("Surface input proof", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var previous = EventSystem.current;
            var events = new GameObject("Surface events", typeof(EventSystem));
            var provider = new Provider();
            using var target = new CapsuleNativeTarget((RectTransform)root.transform, provider, provider);
            try
            {
                var snapshot = new UiSnapshot(1, new[] {
                    new UiNode(1,0,UiNodeKind.Viewport,new UiRect(0,0,256*64,256*64),"","surface","",0,true,UiEventMask.None,1) });
                Assert.That(target.TryCommit(snapshot,out var error),Is.True,error); target.Activate();
                target.TryGetNativeWidget(1,out var surface);
                var overlay = new GameObject("Overlay",typeof(RectTransform),typeof(Image),typeof(Button));
                var rect = (RectTransform)overlay.transform;
                rect.SetParent(surface,false); rect.sizeDelta=new Vector2(128,128);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var data = new PointerEventData(events.GetComponent<EventSystem>()) {
                    position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)) };
                var hits=new List<RaycastResult>();
                root.GetComponent<GraphicRaycaster>().Raycast(data,hits);
                Assert.That(hits.Count,Is.GreaterThan(0));
                Assert.That(hits[0].gameObject,Is.SameAs(overlay));
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,ExecuteEvents.pointerDownHandler);
                Assert.That(provider.Downs,Is.Zero);
                overlay.SetActive(false);
                yield return null;
                hits.Clear(); root.GetComponent<GraphicRaycaster>().Raycast(data,hits);
                Assert.That(hits[0].gameObject,Is.SameAs(surface.gameObject));
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,ExecuteEvents.pointerDownHandler);
                data.scrollDelta=Vector2.up;
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,ExecuteEvents.scrollHandler);
                Assert.That(provider.Downs,Is.EqualTo(1)); Assert.That(provider.Scrolls,Is.EqualTo(1));
                target.Dispose(); Assert.That(provider.Disposals,Is.EqualTo(1));
            }
            finally
            {
                target.Dispose(); UnityEngine.Object.DestroyImmediate(events);
                if(previous)EventSystem.current=previous;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
