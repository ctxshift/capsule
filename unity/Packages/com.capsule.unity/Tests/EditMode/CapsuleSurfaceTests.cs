using System;
using System.Collections.Generic;
using NUnit.Framework;
using Capsule;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleSurfaceTests
    {
        sealed class EmptyTheme : IUiTheme { public bool AllowsStyle(string key) => key == ""; public bool AllowsAsset(string key) => key == ""; }
        [TestCase(0, 768, true, true)]
        [TestCase(64, 0, true, true)]
        [TestCase(-1, 768, true, false)]
        [TestCase(0, 768, false, false)]
        public void ComputedLayoutAllowsEmptySpacers(int width, int height, bool computed, bool accepted)
        {
            var layout = computed ? new UiLayout(0,0,1,1,-1,-1,0,0,0,0,0,0,0,0,0) : null;
            var node = new UiNode((1UL<<32)|(1UL<<16)|1,0,UiNodeKind.Box,new UiRect(0,0,width,height),"","","",0,true,UiEventMask.None,0,layout);
            Assert.That(UiSnapshotValidator.Validate(new UiSnapshot(1,new[]{node}),1,new EmptyTheme(),out _),Is.EqualTo(accepted));
        }
        sealed class Factory : INativeUiWidgetFactory, INativeUiSurfaceFactory
        {
            public readonly List<Lease> Leases = new List<Lease>();
            public bool Fail;
            public NativeUiWidget Create(UiNodeKind kind, Transform parent) => throw new InvalidOperationException("Unexpected widget");
            public bool TryGetSprite(string key, out Sprite sprite) { sprite = null; return false; }
            public INativeUiSurfaceLease Acquire(string key)
            {
                if (Fail || (key != "preview.one" && key != "preview.two")) throw new InvalidOperationException("Unadmitted source");
                var lease = new Lease(); Leases.Add(lease); return lease;
            }
        }
        sealed class Lease : INativeUiSurfaceLease
        {
            public RenderTexture Texture;
            public int Resizes, Disposals;
            public bool Visible;
            public int Scrolls;
            public readonly List<UiPointerPhase> Input = new List<UiPointerPhase>();
            public Texture Resize(int width, int height)
            {
                ++Resizes;
                var replacement = new RenderTexture(width, height, 0); replacement.Create();
                if (Texture) { Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); }
                Texture = replacement; return Texture;
            }
            public void SetVisible(bool visible) => Visible = visible;
            public void Pointer(UiPointerPhase phase, int id, Vector2 point, PointerEventData.InputButton button) => Input.Add(phase);
            public void Scroll(Vector2 point, Vector2 delta) => ++Scrolls;
            public void Dispose()
            {
                ++Disposals;
                if (Texture) { Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); }
                Texture = null;
            }
        }
        static UiSnapshot Snapshot(string key = "preview.one", bool visible = true, int size = 256, bool invalid = false) =>
            new UiSnapshot(1, new[] {
                new UiNode(1,0,UiNodeKind.ClipBox,new UiRect(0,0,512*64,512*64),"","","",0,true,UiEventMask.None,1),
                new UiNode(2,1,UiNodeKind.Viewport,new UiRect(0,0,size*64,size*64),"",key,"",0,visible,UiEventMask.None,2),
                new UiNode(3,1,invalid ? UiNodeKind.Image : UiNodeKind.Box,new UiRect(0,0,64,64),"",invalid?"missing":"","",0,true,UiEventMask.None,3),
            });
        [TestCase("lifecycle")]
        [TestCase("replacement")]
        [TestCase("rollback")]
        [TestCase("resize")]
        [TestCase("input")]
        [TestCase("missing")]
        public void LiveSurfacePreservesNativeBoundaries(string scenario)
        {
            var root = new GameObject("Live surface test", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(1440,900);
            var previousEvents = EventSystem.current;
            var events = new GameObject("Surface input", typeof(EventSystem));
            var factory = new Factory();
            using var target = new CapsuleNativeTarget(rect, factory, scenario == "missing" ? null : factory);
            try
            {
                if (scenario == "missing")
                { Assert.That(target.TryCommit(Snapshot(), out var missing), Is.False); Assert.That(missing, Does.Contain("not configured")); return; }
                Assert.That(target.TryCommit(Snapshot(), out var error), Is.True, error); target.Activate();
                Assert.That(target.TryGetNativeWidget(2, out var surface), Is.True);
                var raw = surface.GetComponent<RawImage>(); var lease = factory.Leases[0];
                Assert.That(raw.texture, Is.SameAs(lease.Texture));
                Assert.That(surface.GetComponentInParent<RectMask2D>(), Is.Not.Null, "Viewport participates in normal parent clipping");
                if (scenario == "lifecycle")
                {
                    long commits = target.CommitCount;
                    Graphics.Blit(Texture2D.whiteTexture, lease.Texture);
                    Assert.That(raw.texture, Is.SameAs(lease.Texture)); Assert.That(target.CommitCount, Is.EqualTo(commits), "GPU content updates without a guest commit");
                    Assert.That(target.TryCommit(Snapshot(visible:false), out error), Is.True,error);
                    Assert.That(lease.Visible, Is.False); Assert.That(lease.Disposals, Is.Zero);
                    Assert.That(target.TryCommit(Snapshot(), out error), Is.True,error);
                    Assert.That(lease.Visible, Is.True); Assert.That(factory.Leases.Count, Is.EqualTo(1));
                }
                else if (scenario == "replacement")
                {
                    Assert.That(target.TryCommit(Snapshot("preview.two"), out error), Is.True,error);
                    target.TryGetNativeWidget(2,out var replacement);
                    Assert.That(replacement, Is.Not.SameAs(surface)); Assert.That(lease.Disposals, Is.EqualTo(1));
                    Assert.That(replacement.GetComponent<RawImage>().texture, Is.SameAs(factory.Leases[1].Texture));
                }
                else if (scenario == "rollback")
                {
                    Assert.That(target.TryCommit(Snapshot("preview.two",invalid:true), out error), Is.False);
                    target.TryGetNativeWidget(2,out var retained);
                    Assert.That(retained, Is.SameAs(surface)); Assert.That(lease.Disposals, Is.Zero);
                    Assert.That(factory.Leases[1].Disposals, Is.EqualTo(1));
                    factory.Fail = true; Assert.That(target.TryCommit(Snapshot("preview.two"), out error), Is.False);
                    Assert.That(raw.texture, Is.SameAs(lease.Texture));
                }
                else if (scenario == "resize")
                {
                    int calls=lease.Resizes; Assert.That(target.TryCommit(Snapshot(),out error),Is.True,error);
                    Assert.That(lease.Resizes,Is.EqualTo(calls));
                    Assert.That(target.TryCommit(Snapshot(size:4096),out error),Is.True,error);
                    Assert.That(lease.Texture.width,Is.LessThanOrEqualTo(CapsuleSurfaceView.MaxDimension));
                    Assert.That((long)lease.Texture.width*lease.Texture.height,Is.LessThanOrEqualTo(CapsuleSurfaceView.MaxPixels));
                }
                else
                {
                    var view=surface.GetComponent<CapsuleSurfaceView>();
                    var data=new PointerEventData(events.GetComponent<EventSystem>()) {pointerId=7,button=PointerEventData.InputButton.Left,
                        position=RectTransformUtility.WorldToScreenPoint(null,surface.TransformPoint(surface.rect.center))};
                    view.OnPointerDown(data); view.OnDrag(data); target.SuspendInput(); view.OnPointerUp(data);
                    Assert.That(lease.Input,Is.EqualTo(new[]{UiPointerPhase.Down,UiPointerPhase.Move,UiPointerPhase.Cancel}));
                    view.OnPointerDown(data); Assert.That(lease.Input.Count,Is.EqualTo(3));
                    target.Activate(); view.OnPointerDown(data);
                    Assert.That(target.TryCommit(Snapshot(visible:false),out error),Is.True,error);
                    Assert.That(lease.Input[lease.Input.Count-1],Is.EqualTo(UiPointerPhase.Cancel));
                }
                target.Dispose();
                foreach(var allocated in factory.Leases) Assert.That(allocated.Disposals,Is.EqualTo(1),"Every prepared lease disposed once");
            }
            finally { target.Dispose(); UnityEngine.Object.DestroyImmediate(events); if (previousEvents) EventSystem.current = previousEvents; UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
