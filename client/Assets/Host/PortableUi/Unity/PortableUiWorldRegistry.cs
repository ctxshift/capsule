using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortableUi.Unity
{
    // One registry belongs to one activation. Only trusted game code grants attachments.
    public sealed class PortableUiWorldRegistry : IDisposable
    {
        public sealed class Attachment
        {
            internal Transform Transform;
            internal Func<bool> Permitted;
            readonly PortableUiWorldRegistry registry;
            internal Attachment(PortableUiWorldRegistry registry){this.registry=registry;}
            public bool TryResolve(UiWorldAnchor anchor,out Vector3 point)
            {
                point=default;
                if(registry.disposed || !Transform)return false;
                try { if(!Permitted()){Transform=null;return false;} }
                catch { Transform=null;return false; }
                point=Transform.position+new Vector3(anchor.X,anchor.Y,anchor.Z)/64f;
                return float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
            }
        }
        readonly Dictionary<string, Attachment> grants = new Dictionary<string, Attachment>(StringComparer.Ordinal);
        readonly uint owner, generation;
        bool disposed;
        public Camera Camera { get; set; }
        // True means visible. Null cannot satisfy an occlusion-enabled anchor.
        public Func<Vector3, bool> Visibility { get; set; }
        public PortableUiWorldRegistry(uint owner, uint generation, Camera camera)
        {
            if (owner == 0 || generation == 0 || generation > ushort.MaxValue) throw new ArgumentOutOfRangeException();
            this.owner=owner;this.generation=generation;Camera=camera;
        }
        static string Key(string entity,string attachment)=>entity+"/"+attachment;
        public void GrantAttachment(string entity,string attachment,Transform transform,Func<bool> permitted)
        {
            if(disposed || !transform || permitted==null || !new UiWorldAnchor(entity,attachment).Valid || grants.Count>=64)
                throw new ArgumentException("Invalid or excessive world grant.");
            grants.Add(Key(entity,attachment),new Attachment(this){Transform=transform,Permitted=permitted});
        }
        public void Revoke(string entity,string attachment)
        { var key=Key(entity,attachment);if(grants.TryGetValue(key,out var grant)){grant.Transform=null;grants.Remove(key);} }
        public bool Allows(ulong handle,UiWorldAnchor anchor)
        {
            return !disposed && (uint)(handle>>32)==owner && (ushort)(handle>>16)==generation &&
                grants.TryGetValue(Key(anchor.Entity,anchor.Attachment),out var grant) && grant.Transform;
        }
        // Retained views keep the original lease, so revoke/regrant cannot revive stale UI.
        public Attachment Acquire(ulong handle,UiWorldAnchor anchor)=>Allows(handle,anchor)?grants[Key(anchor.Entity,anchor.Attachment)]:null;
        public void Dispose(){disposed=true;grants.Clear();Camera=null;Visibility=null;}
    }
}
