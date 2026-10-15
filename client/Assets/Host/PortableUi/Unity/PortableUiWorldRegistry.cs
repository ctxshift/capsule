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
        public string AvoidOverlapAttachment { get; set; }
        readonly Dictionary<ulong, Rect> placed = new Dictionary<ulong, Rect>();
        int placementFrame = -1;
        public bool TryPlace(UiWorldAnchor anchor, ulong handle, Vector2 position, Vector2 size, Vector2 viewport, out Vector2 result)
        {
            result = position;
            if (anchor == null || anchor.Attachment != AvoidOverlapAttachment) return true;
            if (placementFrame != Time.frameCount) { placed.Clear(); placementFrame = Time.frameCount; }
            placed.Remove(handle);
            if (placed.Count >= 64 || size.x <= 0 || size.y <= 0 || viewport.x < size.x || viewport.y < size.y) return false;
            float x = Mathf.Clamp(position.x, 0, viewport.x - size.x);
            float y = Mathf.Clamp(position.y, -viewport.y + size.y, 0);
            for (int lane = 0; lane < 64; lane++)
            {
                int shift = lane == 0 ? 0 : (lane + 1) / 2 * (lane % 2 == 1 ? 1 : -1);
                float top = y + shift * (size.y + 4);
                if (top > 0 || top - size.y < -viewport.y) continue;
                var bounds = new Rect(x, top - size.y, size.x, size.y);
                bool overlaps = false;
                foreach (var occupied in placed.Values) if (occupied.Overlaps(bounds)) { overlaps = true; break; }
                if (overlaps) continue;
                placed.Add(handle, bounds); result = new Vector2(x, top); return true;
            }
            return false;
        }
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
        public void Dispose(){disposed=true;grants.Clear();placed.Clear();Camera=null;Visibility=null;}
    }
}
