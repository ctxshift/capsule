using System;
using System.Collections;
using NUnit.Framework;
using Capsule;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Reflection;

namespace Capsule.Tests
{
    public sealed class CapsuleWorldPresentationTests
    {

        [UnityTest]
        public IEnumerator LeasedGroundImageConformsToRaycastSlopeAndClipsMissingSupport()
        {
            var cameraObject = new GameObject("Ground camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 3, 3);
            camera.transform.LookAt(new Vector3(0, 0, 10));
            var canvas = new GameObject("Ground canvas", typeof(RectTransform), typeof(Canvas));
            var actor = new GameObject("Ground actor", typeof(SphereCollider));
            actor.transform.position = new Vector3(0, .5f, 10);
            var slope = new GameObject("Sloped support", typeof(MeshFilter), typeof(MeshCollider));
            var support = new Mesh { vertices = new[] {
                new Vector3(-2,-.4f,8), new Vector3(2,.4f,8),
                new Vector3(-2,-.4f,12), new Vector3(2,.4f,12) },
                triangles = new[] {0,2,1,1,2,3} };
            slope.GetComponent<MeshFilter>().sharedMesh = support;
            slope.GetComponent<MeshCollider>().sharedMesh = support;
            var box = new GameObject("Ground anchor", typeof(RectTransform), typeof(Image), typeof(CapsuleWorldView));
            box.transform.SetParent(canvas.transform, false);
            var child = new GameObject("Addon ring", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(box.transform, false);
            var texture = new Texture2D(4,4);
            var sprite = Sprite.Create(texture,new Rect(0,0,4,4),new Vector2(.5f,.5f));
            child.GetComponent<Image>().sprite = sprite;
            var world = new CapsuleWorldRegistry(1,1,camera);
            CapsuleWorldRegistry replacement = null;
            Vector3? Ground(Vector3 sample)
            {
                if(sample.x > .6f) return null;
                var hits = Physics.RaycastAll(sample + Vector3.up * 3, Vector3.down, 8,
                    ~0, QueryTriggerInteraction.Ignore);
                foreach(var hit in hits)
                    if(hit.collider && hit.collider.transform != actor.transform && hit.normal.y > .5f)
                        return hit.point;
                return null;
            }
            world.GrantAttachment("actor.life1","ground",actor.transform,()=>true,()=>1f,Ground);
            const ulong id=(1UL<<32)|(1UL<<16)|1;
            var anchor = new UiWorldAnchor("actor.life1","ground");
            var node = new UiNode(id,0,UiNodeKind.Box,new UiRect(0,0,128*64,128*64),"","","",0,true,0,0,
                worldAnchor:anchor);
            var view = box.GetComponent<CapsuleWorldView>();
            try
            {
                Physics.SyncTransforms();
                view.Apply(node,world,Time.unscaledTimeAsDouble);
                view.Present(Time.unscaledTimeAsDouble);
                var projection = GameObject.Find("Capsule leased ground projection");
                Assert.That(projection,Is.Not.Null);
                var mesh = projection.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(view.Shown,Is.True);
                Assert.That(mesh.triangles.Length,Is.GreaterThan(0).And.LessThan(64*6),
                    "Missing support must clip ring triangles");
                float low=float.MaxValue, high=float.MinValue;
                foreach(int index in mesh.triangles)
                {low=Mathf.Min(low,mesh.vertices[index].y);high=Mathf.Max(high,mesh.vertices[index].y);}
                Assert.That(high-low,Is.GreaterThan(.15f),"Projected vertices must follow the slope");
                Assert.That(child.GetComponent<Image>().enabled,Is.True,
                    "Addon image remains an ordinary owned asset source");
                box.SetActive(false);
                Assert.That(projection.GetComponent<MeshRenderer>().enabled,Is.False);
                box.SetActive(true);
                view.Present(Time.unscaledTimeAsDouble);
                Assert.That(view.Shown,Is.True);
                world.Dispose();
                view.Present(Time.unscaledTimeAsDouble);
                Assert.That(view.Shown,Is.False,"Addon deactivation must retire projection");
                replacement = new CapsuleWorldRegistry(1,1,camera);
                replacement.GrantAttachment("actor.life1","ground",actor.transform,()=>true,()=>1f,Ground);
                view.Apply(node,replacement,Time.unscaledTimeAsDouble);
                view.Present(Time.unscaledTimeAsDouble);
                Assert.That(view.Shown,Is.True);
                replacement.Revoke("actor.life1","ground");
                view.Present(Time.unscaledTimeAsDouble);
                Assert.That(view.Shown,Is.False,"Revoked lease must retire projection");
            }
            finally
            {
                world.Dispose();
                replacement?.Dispose();
                UnityEngine.Object.Destroy(box);
                UnityEngine.Object.Destroy(child);
                UnityEngine.Object.Destroy(canvas);
                UnityEngine.Object.Destroy(actor);
                UnityEngine.Object.Destroy(slope);
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(support);
                UnityEngine.Object.Destroy(sprite);
                UnityEngine.Object.Destroy(texture);
            }
            yield return null;
            Assert.That(GameObject.Find("Capsule leased ground projection"),Is.Null,
                "Addon node destruction must dispose its detached world mesh");
        }
        sealed class Factory : INativeUiWidgetFactory
        {
            public NativeUiWidget Create(UiNodeKind kind,Transform parent)
            {
                var obj=new GameObject("World fixture",typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent,false);
                return new NativeUiWidget((RectTransform)obj.transform,obj.GetComponent<Image>(),null,null);
            }
            public bool TryGetSprite(string key,out Sprite sprite){sprite=null;return false;}
        }
        [UnityTest]
        public IEnumerator BoundedPlateStackingStaysInViewportAndResetsEachFrame()
        {
            using (var world = new CapsuleWorldRegistry(1,1,null))
            {
                world.AvoidOverlapAttachment = "plate";
                var anchor = new UiWorldAnchor("actor", "plate");
                var origin = new Vector2(50,-50); var size = new Vector2(170,48); var viewport = new Vector2(640,360);
                var bounds = new System.Collections.Generic.List<Rect>();
                for (ulong i=1;i<=6;i++)
                {
                    Assert.That(world.TryPlace(anchor,i,origin,size,viewport,out var point),Is.True);
                    var rect = new Rect(point.x,point.y-size.y,size.x,size.y);
                    foreach(var old in bounds) Assert.That(old.Overlaps(rect),Is.False);
                    Assert.That(rect.yMax,Is.LessThanOrEqualTo(0)); Assert.That(rect.yMin,Is.GreaterThanOrEqualTo(-viewport.y)); bounds.Add(rect);
                }
                Assert.That(world.TryPlace(anchor,7,origin,size,new Vector2(100,20),out _),Is.False);
                yield return null;
                Assert.That(world.TryPlace(anchor,8,origin,size,viewport,out var reset),Is.True); Assert.That(reset,Is.EqualTo(origin));
                Assert.That(world.TryPlace(new UiWorldAnchor("actor","effect"),9,origin,size,viewport,out var unstacked),Is.True); Assert.That(unstacked,Is.EqualTo(origin));
            }
        }
        [UnityTest]
        public IEnumerator EffectBurstReusesBoundedViewsAndDoesNotReplayOnRerender()
        {
            var canvas=new GameObject("Effect canvas",typeof(RectTransform),typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var target=new CapsuleNativeTarget((RectTransform)canvas.transform,new Factory());
            var nodes=new UiNode[65];const ulong root=(9UL<<32)|(1UL<<16)|1;
            nodes[0]=new UiNode(root,0,UiNodeKind.Box,new UiRect(0,0,6400,6400),"","","",0,true,0,0);
            void SetEffects(uint revision)
            {
                for(int i=1;i<nodes.Length;i++)nodes[i]=new UiNode(root+(ulong)i,root,UiNodeKind.Box,new UiRect(0,0,640,640),"","","",0,true,0,i,effect:new UiPresentationEffect(revision,1000,500,0,-640));
            }
            try
            {
                SetEffects(1);Assert.That(target.TryCommit(new UiSnapshot(1,nodes),out var error),Is.True,error);target.Activate();
                var views=canvas.GetComponentsInChildren<CapsuleWorldView>();Assert.That(views.Length,Is.EqualTo(64));
                double end=Time.unscaledTimeAsDouble+2;
                foreach(var view in views){view.Present(end);Assert.That(view.Shown,Is.False);}
                Assert.That(target.TryCommit(new UiSnapshot(1,nodes),out error),Is.True,error);
                foreach(var view in views)Assert.That(view.Shown,Is.False);
                SetEffects(2);Assert.That(target.TryCommit(new UiSnapshot(1,nodes),out error),Is.True,error);
                Assert.That(canvas.GetComponentsInChildren<CapsuleWorldView>(),Is.EqualTo(views));
                foreach(var view in views)Assert.That(view.Shown,Is.True);
                Assert.That(target.TryCommit(new UiSnapshot(1,Array.Empty<UiNode>()),out error),Is.True,error);
                yield return null;Assert.That(canvas.GetComponentsInChildren<CapsuleWorldView>().Length,Is.Zero);
            }
            finally{target.Dispose();UnityEngine.Object.DestroyImmediate(canvas);}
        }
        [UnityTest]
        public IEnumerator CameraProjectionPermissionsClocksAndGenerationRetirement()
        {
            var previousEvents=EventSystem.current;
            var eventObject=new GameObject("World events",typeof(EventSystem));EventSystem.current=eventObject.GetComponent<EventSystem>();
            var canvas=new GameObject("World canvas",typeof(RectTransform),typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var cameraObject=new GameObject("World camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
            camera.nearClipPlane=0.1f;camera.farClipPlane=200;
            var actor=new GameObject("Admitted actor");actor.transform.position=new Vector3(0,0,10);
            bool permitted=true,unoccluded=true;
            var world=new CapsuleWorldRegistry(7,1,camera){Visibility=_=>unoccluded};
            world.GrantAttachment("actor.life1","head",actor.transform,()=>permitted);
            var target=new CapsuleNativeTarget((RectTransform)canvas.transform,new Factory(),worldRegistry:world);
            const ulong root=(7UL<<32)|(1UL<<16)|1, id=root+1;
            var anchor=new UiWorldAnchor("actor.life1","head",far:6400,referenceDistance:640,minScale:32,maxScale:128,occlusion:true);
            UiNode Root()=>new UiNode(root,0,UiNodeKind.Box,new UiRect(0,0,800*64,600*64),"","","",0,true,0,0);
            UiNode Node(UiWorldAnchor a,UiPresentationEffect effect=null)=>new UiNode(id,root,UiNodeKind.Box,new UiRect(0,0,80*64,20*64),"","","",0,true,UiEventMask.Activate,1,worldAnchor:a,effect:effect);
            void Commit(UiNode node){Assert.That(target.TryCommit(new UiSnapshot(1,new[]{Root(),node}),out var error),Is.True,error);}
            try
            {
                var forbidden=Node(new UiWorldAnchor("not.granted","head"));
                Assert.That(target.TryCommit(new UiSnapshot(1,new[]{Root(),forbidden}),out _),Is.False);
                Commit(Node(anchor));target.Activate();yield return null;
                Assert.That(target.TryGetNativeWidget(id,out var rect),Is.True);
                var view=rect.GetComponent<CapsuleWorldView>();var group=rect.GetComponent<CanvasGroup>();
                Assert.That(view.Shown,Is.True);Assert.That(rect.localScale.x,Is.EqualTo(1).Within(0.01));
                var initial=rect.position;long commits=target.CommitCount;
                camera.transform.position=new Vector3(1,0,0);yield return null;yield return null;
                Assert.That(rect.position.x,Is.LessThan(initial.x));Assert.That(target.CommitCount,Is.EqualTo(commits));
                camera.transform.position=Vector3.zero;
                actor.transform.position=new Vector3(0,0,1);view.Present(Time.unscaledTimeAsDouble);Assert.That(rect.localScale.x,Is.EqualTo(2));
                actor.transform.position=new Vector3(0,0,50);view.Present(Time.unscaledTimeAsDouble);Assert.That(rect.localScale.x,Is.EqualTo(0.5f));
                foreach(var position in new[]{new Vector3(0,0,-1),new Vector3(0,0,0.01f),new Vector3(0,0,101),new Vector3(100,0,10)})
                {actor.transform.position=position;view.Present(Time.unscaledTimeAsDouble);Assert.That(view.Shown,Is.False);}
                actor.transform.position=new Vector3(0,0,10);unoccluded=false;view.Present(Time.unscaledTimeAsDouble);Assert.That(view.Shown,Is.False);
                unoccluded=true;view.Present(Time.unscaledTimeAsDouble);Assert.That(view.Shown,Is.True);
                int calls=0;target.InputSink=(_,__,___)=>{calls++;return true;};
                Assert.That(target.Queue(id,UiEventMask.Activate),Is.True);
                var effect=new UiPresentationEffect(1,1000,500,640,-1280);double started=Time.unscaledTimeAsDouble;
                Commit(Node(anchor,effect));view.Present(started+0.75);
                Assert.That(group.alpha,Is.EqualTo(0.5f).Within(0.05));
                EventSystem.current.SetSelectedGameObject(rect.gameObject);
                Commit(Node(anchor,effect));view.Present(started+1.1);
                Assert.That(view.Shown,Is.False);Assert.That(group.blocksRaycasts,Is.False);Assert.That(target.Queue(id,UiEventMask.Activate),Is.False);Assert.That(calls,Is.EqualTo(1));
                Assert.That(EventSystem.current.currentSelectedGameObject,Is.Null);
                view.Present(started);Assert.That(view.Shown,Is.False,"Clock rollback must not revive expired effects");
                Commit(Node(anchor,new UiPresentationEffect(2,1000)));view.Present(started+1.1);Assert.That(view.Shown,Is.True);
                Commit(Node(anchor));Assert.That(group.alpha,Is.EqualTo(1));
                permitted=false;view.Present(started+1.2);Assert.That(view.Shown,Is.False);
                permitted=true;world.Revoke("actor.life1","head");world.GrantAttachment("actor.life1","head",actor.transform,()=>true);
                Commit(Node(anchor));view.Present(started+1.3);Assert.That(view.Shown,Is.False,"Regrant cannot revive the old lease");
                Assert.That(target.TryCommit(new UiSnapshot(1,Array.Empty<UiNode>()),out _),Is.True);
                Commit(Node(anchor));target.TryGetNativeWidget(id,out rect);view=rect.GetComponent<CapsuleWorldView>();view.Present(Time.unscaledTimeAsDouble);Assert.That(view.Shown,Is.True);
                UnityEngine.Object.DestroyImmediate(actor);view.Present(Time.unscaledTimeAsDouble);Assert.That(view.Shown,Is.False);
                var other=new UiNode((7UL<<32)|(2UL<<16)|1,0,UiNodeKind.Box,new UiRect(0,0,6400,6400),"","","",0,true,0,0,worldAnchor:anchor);
                Assert.That(target.TryCommit(new UiSnapshot(2,new[]{other}),out _),Is.False);
                target.Dispose();Assert.That(world.Acquire(id,anchor),Is.Null);
                yield return null;Assert.That(rect==null,Is.True);
            }
            finally {target.Dispose();if(actor)UnityEngine.Object.DestroyImmediate(actor);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(canvas);UnityEngine.Object.DestroyImmediate(eventObject);if(previousEvents)EventSystem.current=previousEvents;}
        }
    }
}
