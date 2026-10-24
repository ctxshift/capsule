using NUnit.Framework;
using Capsule;
using Capsule.Unity;
using UnityEngine;

namespace Capsule.Tests
{
    public sealed class CapsuleTranslationTests
    {
        sealed class Factory : INativeUiWidgetFactory, INativeUiStyleAdapter
        {
            public int Styles;
            public NativeUiWidget Create(UiNodeKind kind,Transform parent)
            {var rect=new GameObject("Window",typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);return new NativeUiWidget(rect,null,null,null);}
            public bool TryGetSprite(string key,out Sprite sprite){sprite=null;return false;}
            public void ApplyStyle(NativeUiWidget widget,UiNode node)=>++Styles;
        }
        [Test]
        public void RetainedPresentationSurvivesSourceDestructionAndRejectsInput()
        {
            var source = new GameObject("Source canvas", typeof(RectTransform));
            var destination = new GameObject("Retained canvas", typeof(RectTransform));
            using var target = new CapsuleNativeTarget((RectTransform)source.transform, new Factory());
            try
            {
                var snapshot = new UiSnapshot(1, new[] {
                    new UiNode(1, 0, UiNodeKind.Box, new UiRect(0, 0, 6400, 6400), "", "", "", 0, true, UiEventMask.None, 0) });
                Assert.That(target.TryCommit(snapshot, out var error), Is.True, error);
                target.Activate();
                target.TryGetNativeWidget(1, out var widget);
                target.RetainPresentation((RectTransform)destination.transform);
                Object.DestroyImmediate(source);
                Assert.That(widget && widget.gameObject.activeInHierarchy, Is.True);
                Assert.That(widget.IsChildOf(destination.transform), Is.True);
                Assert.That(target.Queue(1, UiEventMask.Activate), Is.False);
                target.Dispose();
                Assert.That(widget == null, Is.True);
            }
            finally { target.Dispose(); if (source) Object.DestroyImmediate(source); Object.DestroyImmediate(destination); }
        }

        [Test]
        public void WindowTranslationRetainsChildrenAndStillAppliesOtherChanges()
        {
            var root=new GameObject("Translation test",typeof(RectTransform));var factory=new Factory();
            using var target=new CapsuleNativeTarget((RectTransform)root.transform,factory);
            UiSnapshot Snapshot(int x,string style="",int width=100,bool visible=true)=>new UiSnapshot(1,new[]{
                new UiNode(1,0,UiNodeKind.Window,new UiRect(x*64,0,width*64,100*64),style,"","",0,visible,UiEventMask.None,0),
                new UiNode(2,1,UiNodeKind.Box,new UiRect(5*64,5*64,20*64,20*64),"","","",0,true,UiEventMask.None,1)});
            try
            {
                Assert.That(target.TryCommit(Snapshot(0),out var error),Is.True,error);
                target.TryGetNativeWidget(1,out var window);target.TryGetNativeWidget(2,out var child);
                int styles=factory.Styles;
                Assert.That(target.TryCommit(Snapshot(50),out error),Is.True,error);
                Assert.That(window.anchoredPosition.x,Is.EqualTo(50));Assert.That(factory.Styles,Is.EqualTo(styles));
                target.TryGetNativeWidget(2,out var retained);Assert.That(retained,Is.SameAs(child));
                Assert.That(target.TryCommit(Snapshot(60,"changed"),out error),Is.True,error);
                Assert.That(factory.Styles,Is.GreaterThan(styles));styles=factory.Styles;
                Assert.That(target.TryCommit(Snapshot(70,"changed",120),out error),Is.True,error);
                Assert.That(window.rect.width,Is.EqualTo(120));Assert.That(factory.Styles,Is.GreaterThan(styles));
                Assert.That(target.TryCommit(Snapshot(80,"changed",120,false),out error),Is.True,error);
                Assert.That(window.gameObject.activeSelf,Is.False);
            }
            finally{target.Dispose();Object.DestroyImmediate(root);}
        }
    }
}
