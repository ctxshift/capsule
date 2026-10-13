using System;
using System.Collections.Generic;

namespace PortableUi
{
    // Main-thread transport validation before touching Unity. Packet validation still
    // runs in the worker session; this independently bounds the immutable handoff.
    public static class UiSnapshotValidator
    {
        public static bool Validate(UiSnapshot snapshot, uint owner, IUiTheme theme, out string error)
        {
            error = null;
            if (snapshot == null || snapshot.Nodes.Count > UiSession.MaxNodes || owner == 0)
            { error = "Snapshot quota or owner invalid."; return false; }
            var seen = new HashSet<ulong>();
            var depths = new Dictionary<ulong, int>();
            bool? layoutMode = null;
            int roots = 0;
            foreach (var node in snapshot.Nodes)
            {
                if (node == null || (uint)(node.Handle >> 32) != owner || (ushort)node.Handle == 0 ||
                    (ushort)(node.Handle >> 16) != snapshot.Generation || !seen.Add(node.Handle) ||
                    node.Parent == node.Handle || (node.Parent != 0 && !seen.Contains(node.Parent)))
                { error = "Snapshot handle or parent invalid."; return false; }
                if (node.Parent == 0 && ++roots > UiSession.MaxRoots)
                { error = "Snapshot root quota exceeded."; return false; }
                if (node.Parent == 0 && node.Kind != UiNodeKind.Window && node.Kind != UiNodeKind.Panel &&
                    node.Kind != UiNodeKind.Box && node.Kind != UiNodeKind.ClipBox)
                { error = "Snapshot root kind invalid."; return false; }
                bool layout = node.Layout != null;
                if (layoutMode.HasValue && layoutMode.Value != layout)
                { error = "Mixed explicit and computed layout is ambiguous."; return false; }
                layoutMode = layout;
                int depth = node.Parent == 0 ? 1 : depths[node.Parent] + 1;
                depths.Add(node.Handle, depth);
                if (layout && (!node.Layout.Valid() || depth > 24 || snapshot.Nodes.Count > 128))
                { error = "Layout intent quota or value invalid."; return false; }
                var r = node.Rect;
                if (r.X < -8192 * 64 || r.X > 8192 * 64 || r.Y < -8192 * 64 || r.Y > 8192 * 64 ||
                    r.Width < 1 || r.Width > 4096 * 64 || r.Height < 1 || r.Height > 4096 * 64 ||
                    (long)r.X + r.Width > 8192 * 64 || (long)r.Y + r.Height > 8192 * 64 ||
                    node.Kind < UiNodeKind.Window || node.Kind > UiNodeKind.Select ||
                    node.Progress > 10000 || node.Brightness < 8000 || node.Brightness > 12000 ||
                    ((ushort)node.Events & ~UiInputPayload.AllowedEvents) != 0 ||
                    System.Text.Encoding.UTF8.GetByteCount(node.Text ?? "") > 512 ||
                    !theme.AllowsStyle(node.StyleKey) || !theme.AllowsAsset(node.AssetKey))
                { error = "Snapshot property or theme invalid."; return false; }
            }
            return true;
        }
    }
}
