using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace PortableUi.Unity
{
    // Host presentation resource. It copies render data, never actor behaviours.
    public sealed class AnimatedCharacterSurface : MonoBehaviour, INativeUiSurfaceFactory
    {
        Transform source;
        AnimationClip idle;
        string key;
        Action<Camera> configureCamera;
        Action<Light> configureLight;
        float initialYaw = 180;
        bool portraitFraming;
        readonly List<Lease> leases = new List<Lease>();
        static readonly ProfilerMarker AnimationMarker = new ProfilerMarker("Capsule.Character.Animation");
        ulong revision;
        bool baseline;
        public int LiveLeases => leases.Count;
        public int VisualRevision { get; private set; }
        public float Yaw => leases.Count == 0 ? 0 : leases[0].Yaw;
        public double AnimationTime => leases.Count == 0 ? 0 : leases[0].AnimationTime;
        public int RenderPartCount => leases.Count == 0 ? 0 : leases[0].RenderPartCount;
        public bool PlayingCue => leases.Count != 0 && leases[0].PlayingCue;

        public void Bind(string surfaceKey, Transform visualSource, AnimationClip idleClip)
        {
            if (string.IsNullOrEmpty(surfaceKey) || !visualSource || !idleClip)
                throw new ArgumentException("A surface key, visual source and idle clip are required.");
            Prepare(surfaceKey,idleClip);
            SetOwner(visualSource);
        }
        public void Prepare(string surfaceKey, AnimationClip idleClip, Action<Camera> cameraConfiguration = null,
            float startingYaw = 180, bool portrait = false, Action<Light> lightConfiguration = null)
        {
            if(string.IsNullOrEmpty(surfaceKey) || !idleClip)
                throw new ArgumentException("A surface key and idle clip are required.");
            if(leases.Count!=0)throw new InvalidOperationException("Retire live leases before rebinding the character owner.");
            Clear(); key=surfaceKey; idle=idleClip; configureCamera=cameraConfiguration;
            initialYaw=startingYaw; portraitFraming=portrait;
            configureLight=lightConfiguration;
        }
        // Preserve the UI's texture leases while replacing or removing an owner.
        public void SetOwner(Transform visualSource)
        {
            if(!idle)throw new InvalidOperationException("Prepare the character surface first.");
            source=visualSource; baseline=false; revision=0;
            foreach(var lease in leases)lease.ReplaceOwner();
        }
        // Call only after confirmed visuals have been applied. First/repeated revisions do not celebrate.
        public void Observe(ulong confirmedRevision, AnimationClip cue = null)
        {
            if (!source || (baseline && confirmedRevision <= revision)) return;
            bool playCue=baseline && cue; revision=confirmedRevision; baseline=true;
            ++VisualRevision;
            foreach(var lease in leases) { lease.Refresh(); if(playCue)lease.Play(cue); }
        }
        public INativeUiSurfaceLease Acquire(string admittedKey)
        {
            if (!idle || admittedKey != key || leases.Count >= 2)
                throw new InvalidOperationException("Character surface unavailable or lease quota reached.");
            // Two leases permit transactional guest replacement; both are independently rendered.
            var lease=new Lease(this); leases.Add(lease); return lease;
        }
        void LateUpdate()
        {
            if (!source && !ReferenceEquals(source,null))SetOwner(null);
            foreach(var lease in leases)lease.Tick();
        }
        public void Clear()
        {
            var retiring=leases.ToArray(); leases.Clear();
            foreach(var lease in retiring)lease.Dispose();
            source=null; idle=null; baseline=false;
        }
        void OnDestroy() => Clear();

        sealed class Lease : INativeUiSurfaceLease
        {
            static readonly bool[] occupied = new bool[8];
            readonly AnimatedCharacterSurface owner;
            readonly GameObject stage;
            readonly Camera camera;
            readonly int index;
            readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            GameObject rig;
            RenderTexture texture;
            PlayableGraph graph;
            AnimationClipPlayable animation;
            AnimationClip playing;
            Bounds bounds;
            float portraitHeight;
            bool visible, disposed, dragging;
            Vector2 lastPoint;
            public float Yaw { get; private set; } = 180;
            public double AnimationTime => graph.IsValid() ? animation.GetTime() : 0;
            public int RenderPartCount { get; private set; }
            public bool PlayingCue => playing && playing != owner.idle;
            public Lease(AnimatedCharacterSurface owner)
            {
                this.owner=owner;
                Yaw=owner.initialYaw;
                index=Array.FindIndex(occupied,value=>!value);
                if(index<0)throw new InvalidOperationException("Preview stage quota reached.");
                occupied[index]=true;
                stage=new GameObject("Animated preview stage");
                stage.transform.position=new Vector3(10000+index*100,10000,10000);
                var cameraObject=new GameObject("Preview camera"); cameraObject.transform.SetParent(stage.transform,false);
                camera=cameraObject.AddComponent<Camera>(); camera.enabled=false;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
                camera.orthographic=true; camera.nearClipPlane=.01f; camera.farClipPlane=20;
                camera.cullingMask=1<<31; camera.allowHDR=false; camera.allowMSAA=false;
                var lightObject=new GameObject("Preview light"); lightObject.transform.SetParent(stage.transform,false);
                lightObject.transform.localPosition=new Vector3(-3,4,3);
                var light=lightObject.AddComponent<Light>(); light.type=LightType.Point;
                light.intensity=100; light.range=12; light.cullingMask=1<<31; light.shadows=LightShadows.None;
                try { owner.configureLight?.Invoke(light); owner.configureCamera?.Invoke(camera); Refresh(); stage.SetActive(false); }
                catch { Dispose(); throw; }
            }
            public void Refresh()
            {
                if(disposed)return;
                ReleaseRig();
                if(!owner.source)return;
                var map=new Dictionary<Transform,Transform>();
                Transform Copy(Transform original,Transform parent)
                {
                    var node=new GameObject(original.name); node.layer=31;
                    node.transform.SetParent(parent,false);
                    node.transform.localPosition=original.localPosition;
                    node.transform.localRotation=original.localRotation;
                    node.transform.localScale=original.localScale;
                    map.Add(original,node.transform);
                    foreach(Transform child in original)Copy(child,node.transform);
                    return node.transform;
                }
                rig=Copy(owner.source,stage.transform).gameObject;
                rig.transform.localPosition=Vector3.zero; rig.transform.localRotation=Quaternion.identity;
                foreach(var source in owner.source.GetComponentsInChildren<Renderer>())
                {
                    if(!source.enabled || !source.gameObject.activeInHierarchy)continue;
                    Renderer renderer;
                    if(source is SkinnedMeshRenderer skin && skin.sharedMesh)
                    {
                        var copy=map[skin.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                        copy.sharedMesh=Own(UnityEngine.Object.Instantiate(skin.sharedMesh));
                        var bones=skin.bones;
                        for(int i=0;i<bones.Length;i++)bones[i]=bones[i] && map.TryGetValue(bones[i],out var bone)?bone:null;
                        copy.bones=bones; copy.rootBone=skin.rootBone && map.TryGetValue(skin.rootBone,out var root)?root:null;
                        copy.localBounds=skin.localBounds; copy.updateWhenOffscreen=true;
                        for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)copy.SetBlendShapeWeight(i,skin.GetBlendShapeWeight(i));
                        renderer=copy;
                    }
                    else if(source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
                    {
                        map[source.transform].gameObject.AddComponent<MeshFilter>().sharedMesh=Own(UnityEngine.Object.Instantiate(filter.sharedMesh));
                        renderer=map[source.transform].gameObject.AddComponent<MeshRenderer>();
                    }
                    else continue;
                    var materials=source.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)if(materials[i])materials[i]=Own(UnityEngine.Object.Instantiate(materials[i]));
                    renderer.sharedMaterials=materials; renderer.shadowCastingMode=ShadowCastingMode.Off;
                    renderer.receiveShadows=false;
                }
                var sourceAnimator=owner.source.GetComponentInChildren<Animator>();
                if(!sourceAnimator || !sourceAnimator.avatar)throw new InvalidOperationException("Visual source requires an animation avatar.");
                var animator=map[sourceAnimator.transform].gameObject.AddComponent<Animator>();
                animator.avatar=sourceAnimator.avatar; animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                graph=PlayableGraph.Create("Character surface animation"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output=AnimationPlayableOutput.Create(graph,"Character",animator);
                animation=AnimationClipPlayable.Create(graph,owner.idle); output.SetSourcePlayable(animation);
                playing=owner.idle; graph.Play(); graph.Evaluate(0);
                bool first=true; RenderPartCount=0;
                foreach(var renderer in rig.GetComponentsInChildren<Renderer>())
                { ++RenderPartCount; if(first){bounds=renderer.bounds; first=false;}else bounds.Encapsulate(renderer.bounds); }
                if(first)throw new InvalidOperationException("Visual source has no renderable geometry.");
                if(portraitHeight==0)portraitHeight=Mathf.Max(.5f,bounds.extents.y);
                Frame(); camera.enabled=visible;
            }
            T Own<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
            public void ReplaceOwner()
            {
                dragging=false; Yaw=owner.initialYaw;
                portraitHeight=0;
                Refresh();
                if(texture && !owner.source)ClearTexture();
            }
            void ClearTexture()
            {
                var previous=RenderTexture.active;
                try {RenderTexture.active=texture;GL.Clear(true,true,Color.clear);}
                finally {RenderTexture.active=previous;}
            }
            public void Play(AnimationClip clip)
            {
                if(disposed || !graph.IsValid() || !clip)return;
                var output=(AnimationPlayableOutput)graph.GetOutput(0);
                animation.Destroy(); animation=AnimationClipPlayable.Create(graph,clip);
                output.SetSourcePlayable(animation); playing=clip;
            }
            public void Tick()
            {
                if(disposed || !visible || !graph.IsValid())return;
                if(animation.GetTime()>=playing.length)
                { if(playing!=owner.idle)Play(owner.idle);else animation.SetTime(animation.GetTime()%Math.Max(.001,playing.length)); }
                using (AnimationMarker.Auto()) graph.Evaluate(Time.unscaledDeltaTime);
            }
            public Texture Resize(int width,int height)
            {
                if(disposed)throw new ObjectDisposedException(nameof(Lease));
                // Scene shutdown may destroy the camera before the UI hierarchy.
                if(!camera)return texture ? texture : Texture2D.blackTexture;
                if(texture && texture.width==width && texture.height==height)return texture;
                var next=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32) {name="Character surface",antiAliasing=1};
                if(!next.Create()){Destroy(next);throw new InvalidOperationException("Cannot allocate character surface.");}
                var old=texture; texture=next; camera.targetTexture=next;
                if(old){old.Release();Destroy(old);} ClearTexture(); Frame(); return texture;
            }
            void Frame()
            {
                var center=bounds.center;
                if(owner.portraitFraming)center.y=bounds.min.y+portraitHeight;
                // A portrait is composited into a fixed scene: turn the subject, not its viewpoint or lighting.
                if(owner.portraitFraming && rig)rig.transform.localRotation=Quaternion.Euler(0,owner.initialYaw-Yaw,0);
                var direction=Quaternion.Euler(0,owner.portraitFraming?owner.initialYaw:Yaw,0)*Vector3.forward;
                camera.transform.position=center+direction*5;
                camera.transform.LookAt(center);
                float aspect=texture?(float)texture.width/texture.height:1;
                camera.orthographicSize=owner.portraitFraming
                    ? portraitHeight*1.18f
                    : Mathf.Max(.5f,bounds.extents.y,bounds.extents.magnitude/Mathf.Max(.1f,aspect)) * 1.12f;
            }
            public void SetVisible(bool value)
            {
                if(disposed || !stage || !camera)return;
                visible=value; stage.SetActive(value); camera.enabled=value && rig;
                if(!value)dragging=false;
            }
            public void Pointer(UiPointerPhase phase,int id,Vector2 point,PointerEventData.InputButton button)
            {
                if(disposed || !visible || !owner.source)return;
                if(phase==UiPointerPhase.Down){dragging=true;lastPoint=point;}
                else if(phase==UiPointerPhase.Move && dragging)
                {Yaw=Mathf.Repeat(Yaw+(point.x-lastPoint.x)*.35f,360);lastPoint=point;Frame();}
                else if(phase==UiPointerPhase.Up || phase==UiPointerPhase.Cancel)dragging=false;
            }
            public void Scroll(Vector2 point,Vector2 delta) { }
            void ReleaseRig()
            {
                if(camera)camera.enabled=false;
                if(graph.IsValid())graph.Destroy();
                if(rig){rig.SetActive(false);Destroy(rig);}
                rig=null; playing=null; RenderPartCount=0;
                foreach(var item in owned)Destroy(item);owned.Clear();
            }
            public void Dispose()
            {
                if(disposed)return; disposed=true;
                owner.leases.Remove(this); occupied[index]=false;
                if(camera){camera.enabled=false; camera.targetTexture=null;}
                ReleaseRig(); if(texture){texture.Release();Destroy(texture);}
                Destroy(stage);
            }
            static void Destroy(UnityEngine.Object value)
            {if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        }
    }
}
