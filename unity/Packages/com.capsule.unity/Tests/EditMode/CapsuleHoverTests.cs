using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Capsule;
using Capsule.Package;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleHoverTests
    {






        sealed class Factory : INativeUiWidgetFactory
        {
            public NativeUiWidget Create(UiNodeKind kind, Transform parent)
            {
                var obj = new GameObject("Hover fixture", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                obj.transform.SetParent(parent, false);
                return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, null);
            }
            public bool TryGetSprite(string key, out Sprite sprite) { sprite = null; return false; }
        }

        [Test]
        public void NativePointerExitDeliversBoundedLeaveWithoutChangingLegacyEnter()
        {
            var root = new GameObject("Owned hover test", typeof(RectTransform));
            var received = new List<byte[]>();
            using var target = new CapsuleNativeTarget((RectTransform)root.transform, new Factory());
            target.InputSink = (handle, kind, bytes) => { Assert.That(kind, Is.EqualTo(UiEventMask.Hover)); received.Add(bytes); return true; };
            try
            {
                var node = new UiNode(1, 0, UiNodeKind.Box, new UiRect(0, 0, 4096, 4096), "", "", "", 0, true, UiEventMask.Hover, 1);
                Assert.That(target.TryCommit(new UiSnapshot(1, new[] { node }), out var error), Is.True, error);
                target.Activate();
                Assert.That(target.TryGetNativeWidget(1, out var rect), Is.True);
                ExecuteEvents.Execute(rect.gameObject, new PointerEventData(null), ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(rect.gameObject, new PointerEventData(null), ExecuteEvents.pointerExitHandler);
                Assert.That(received.Count, Is.EqualTo(2));
                Assert.That(received[0], Is.Empty, "Legacy enter wire shape remains valid.");
                Assert.That(received[1], Is.EqualTo(new byte[] { 1, 0 }));
                Assert.That(UiInputPayload.Valid(UiEventMask.Hover, received[1]), Is.True);
                target.SuspendInput();
                ExecuteEvents.Execute(rect.gameObject, new PointerEventData(null), ExecuteEvents.pointerExitHandler);
                Assert.That(received.Count, Is.EqualTo(2), "Suspended views cannot send stale leave events.");
            }
            finally { target.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void SecondaryPointerUsesDomButtonNumberForDownUpAndCancel()
        {
            var root=new GameObject("Owned secondary pointer test",typeof(RectTransform));
            var received=new List<byte[]>();
            using var target=new CapsuleNativeTarget((RectTransform)root.transform,new Factory());
            target.InputSink=(handle,kind,bytes)=>{Assert.That(kind,Is.EqualTo(UiEventMask.Pointer));received.Add(bytes);return true;};
            try
            {
                var node=new UiNode(1,0,UiNodeKind.Box,new UiRect(0,0,4096,4096),"","","",0,true,UiEventMask.Pointer,1);
                Assert.That(target.TryCommit(new UiSnapshot(1,new[]{node}),out var error),Is.True,error);target.Activate();
                Assert.That(target.TryGetNativeWidget(1,out var rect),Is.True);
                var right=new PointerEventData(null){button=PointerEventData.InputButton.Right,pointerId=1,position=Vector2.zero};
                ExecuteEvents.Execute(rect.gameObject,right,ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(rect.gameObject,right,ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(rect.gameObject,right,ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(rect.gameObject,new BaseEventData(null),ExecuteEvents.cancelHandler);
                Assert.That(received.Select(bytes=>bytes[1]),Is.EqualTo(new byte[]{1,3,1,4}));
                Assert.That(received.All(bytes=>bytes[2]==2),Is.True,"Secondary is DOM button2, not Unity enum1.");
            }
            finally {target.Dispose();Object.DestroyImmediate(root);}
        }
    }
}
