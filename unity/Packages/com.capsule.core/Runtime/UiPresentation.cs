using System;
using System.Collections.Generic;

namespace Capsule
{
    // Coordinates are signed 1/64 units. References are opaque host grants, not scene paths.
    public sealed class UiWorldAnchor
    {
        public readonly string Entity, Attachment;
        public readonly int X, Y, Z, Near, Far, ReferenceDistance, MinScale, MaxScale;
        public readonly bool Occlusion;
        public UiWorldAnchor(string entity, string attachment, int x = 0, int y = 0, int z = 0,
            int near = 0, int far = 6400, int referenceDistance = 0, int minScale = 64, int maxScale = 64, bool occlusion = false)
        { Entity = entity; Attachment = attachment; X = x; Y = y; Z = z; Near = near; Far = far;
          ReferenceDistance = referenceDistance; MinScale = minScale; MaxScale = maxScale; Occlusion = occlusion; }
        public bool Valid => Key(Entity) && Key(Attachment) && Bounded(X,640) && Bounded(Y,640) && Bounded(Z,640) &&
            Near >= 0 && Far > Near && Far <= 64000 && ReferenceDistance >= 0 && ReferenceDistance <= 64000 &&
            MinScale >= 16 && MaxScale >= MinScale && MaxScale <= 256;
        internal static bool Bounded(int value, int max) => value >= -max && value <= max;
        static bool Key(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
            return true;
        }
    }

    public sealed class UiPresentationEffect
    {
        public readonly uint Revision;
        public readonly int DurationMs, FadeMs, X, Y;
        public UiPresentationEffect(uint revision, int durationMs, int fadeMs = 0, int x = 0, int y = 0)
        { Revision = revision; DurationMs = durationMs; FadeMs = fadeMs; X = x; Y = y; }
        public bool Valid => DurationMs >= 1 && DurationMs <= 10000 && FadeMs >= 0 && FadeMs <= DurationMs &&
            UiWorldAnchor.Bounded(X,65536) && UiWorldAnchor.Bounded(Y,65536);
    }

    public static class UiPresentation
    {
        public const int MaxAnchors = 64, MaxEffects = 64;
        public static bool Validate(UiSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null) { error = "Missing snapshot."; return false; }
            int anchors = 0, effects = 0;
            var anchored = new HashSet<ulong>();
            foreach (var node in snapshot.Nodes)
            {
                if (node == null) { error = "Missing node."; return false; }
                bool inherited = anchored.Contains(node.Parent);
                if (node.WorldAnchor != null || node.Effect != null)
                {
                    if (node.Kind != UiNodeKind.Box ||
                        (node.WorldAnchor != null && (!node.WorldAnchor.Valid || inherited || ++anchors > MaxAnchors)) ||
                        (node.Effect != null && (!node.Effect.Valid || ++effects > MaxEffects)))
                    { error = "Presentation options, nesting or quota invalid."; return false; }
                }
                if (inherited || node.WorldAnchor != null) anchored.Add(node.Handle);
            }
            return true;
        }
    }
}
