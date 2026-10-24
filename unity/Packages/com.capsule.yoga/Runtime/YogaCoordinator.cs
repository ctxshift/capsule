using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Capsule.Yoga
{
    public sealed class YogaPresentation
    {
        public uint Owner { get; }
        public long Revision { get; }
        public UiSnapshot Snapshot { get; }
        public string Error { get; }
        public double LayoutMilliseconds { get; }
        public double QueueMilliseconds { get; }
        public double CoordinatorMilliseconds { get; }
        public int Calculations { get; }
        public int Measurements { get; }
        internal YogaPresentation(uint owner, long revision, UiSnapshot snapshot, string error,
            double layout, double queue, int calculations, int measurements, double coordinator)
        { Owner = owner; Revision = revision; Snapshot = snapshot; Error = error; LayoutMilliseconds = layout;
          QueueMilliseconds = queue; Calculations = calculations; Measurements = measurements; CoordinatorMilliseconds = coordinator; }
    }

    // A bounded owner-thread coordinator, independent of Unity and the guest runtime.
    // One outstanding transaction per addon; ready addons never wait for idle addons.
    // Transport/guest execution owns wakeups. This class never polls or starts threads.
    public sealed class YogaCoordinator : IDisposable
    {
        public const int MaxAddons = 8, MaxNodes = UiSession.ExtendedLayoutNodes, MaxReadyPerDrain = 4;
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        readonly Dictionary<uint, Addon> addons = new Dictionary<uint, Addon>();
        readonly Queue<Batch> ready = new Queue<Batch>();
        readonly YogaTextCatalog textCatalog;
        bool disposed;
        public YogaCoordinator(YogaTextCatalog textCatalog = null) { this.textCatalog = textCatalog; }
        sealed class Root : IDisposable
        {
            public NativeYoga Native;
            public YogaStyle[] Styles;
            public YogaRect[] Rects;
            public string[] TextKeys;
            public void Dispose() { Native.Dispose(); }
        }
        sealed class Addon
        {
            public int LayoutNodeLimit;
            public IUiTheme Theme;
            public UiSurfaceAdmission Surfaces;
            public bool Pending, Presented, ValidPresentation;
            public long Revision, OutstandingRevision;
            public Dictionary<ulong, Root> Roots = new Dictionary<ulong, Root>();
        }
        sealed class Batch
        {
            public uint Owner;
            public long Revision, Enqueued;
            public UiSnapshot Snapshot;
            public YogaStyle[] Styles;
        }
        sealed class TextMeasurer : IYogaMeasurer
        {
            public UiSnapshot Snapshot;
            public List<int> Indices;
            public YogaTextCatalog Catalog;
            public YogaTextBudget Budget;
            public void Measure(int node, float width, int widthMode, float height, int heightMode, out float measuredWidth, out float measuredHeight)
            {
                if (node < 0 || node >= Indices.Count) throw new ArgumentException("Text node index.");
                var text = Snapshot.Nodes[Indices[node]];
                YogaTextMeasure.Measure(text.Text, Catalog.Require(text.StyleKey), Budget, width, widthMode, height, heightMode, out measuredWidth, out measuredHeight);
            }
        }
        void Check()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Yoga coordinator owner thread required.");
            if (disposed) throw new ObjectDisposedException(nameof(YogaCoordinator));
        }
        public void Register(uint owner, IUiTheme theme, int layoutNodeLimit = UiSession.DefaultLayoutNodes, UiSurfaceAdmission surfaceAdmission = null)
        {
            Check();
            if (layoutNodeLimit != UiSession.DefaultLayoutNodes && layoutNodeLimit != UiSession.ExtendedLayoutNodes) throw new ArgumentOutOfRangeException(nameof(layoutNodeLimit));
            if (owner == 0 || theme == null || addons.Count >= MaxAddons || addons.ContainsKey(owner)) throw new ArgumentException("Yoga registration quota or identity.");
            addons.Add(owner, new Addon { Theme = theme, LayoutNodeLimit = layoutNodeLimit, Surfaces = surfaceAdmission ?? UiSurfaceAdmission.None });
        }
        public static YogaStyle[] ReadIntent(UiSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Nodes.Count > MaxNodes) throw new ArgumentException("Yoga snapshot quota.");
            var styles = new YogaStyle[snapshot.Nodes.Count];
            for (int i = 0; i < styles.Length; ++i)
            {
                var layout = snapshot.Nodes[i].Layout;
                if (layout == null || !layout.Valid()) throw new ArgumentException("Explicit Yoga layout intent required.");
                var style = YogaStyle.Box(-1, layout.Width < 0 ? -1 : layout.Width / 64f, layout.Height < 0 ? -1 : layout.Height / 64f);
                style.Row = layout.Row; style.Absolute = layout.Absolute; style.Align = layout.Align; style.Justify = layout.Justify;
                style.Left = layout.Left / 64f; style.Top = layout.Top / 64f; style.Grow = layout.Grow / 64f; style.Shrink = layout.Shrink / 64f;
                style.Gap = layout.Gap / 64f; style.PaddingLeft = layout.PaddingLeft / 64f; style.PaddingTop = layout.PaddingTop / 64f;
                style.PaddingRight = layout.PaddingRight / 64f; style.PaddingBottom = layout.PaddingBottom / 64f;
                if (snapshot.Nodes[i].Kind == UiNodeKind.Text) style.IntrinsicWidth = style.IntrinsicHeight = 0;
                styles[i] = style;
            }
            return styles;
        }
        public bool Enqueue(uint owner, long revision, UiSnapshot snapshot, YogaStyle[] styles, out string error)
        {
            Check(); error = null;
            if (!addons.TryGetValue(owner, out var addon) || addon.Pending || revision <= addon.Revision)
            { error = "Yoga registration, revision or backpressure."; return false; }
            if (snapshot == null || snapshot.Nodes.Count > addon.LayoutNodeLimit || styles == null || styles.Length != snapshot.Nodes.Count ||
                !UiSnapshotValidator.Validate(snapshot, owner, addon.Theme, out error, addon.LayoutNodeLimit, addon.Surfaces))
            { error = error ?? "Yoga transaction quota."; return false; }
            addon.Pending = true; addon.Presented = false; addon.OutstandingRevision = revision;
            ready.Enqueue(new Batch { Owner = owner, Revision = revision, Snapshot = snapshot, Styles = (YogaStyle[])styles.Clone(), Enqueued = Stopwatch.GetTimestamp() });
            return true;
        }
        public YogaPresentation[] Drain()
        {
            Check();
            var results = new List<YogaPresentation>();
            for (int count = 0; count < MaxReadyPerDrain && ready.Count != 0; ++count)
            {
                var batch = ready.Dequeue(); var addon = addons[batch.Owner];
                var result = Layout(addon, batch);
                results.Add(result); addon.ValidPresentation = result.Error == null;
                addon.Presented = true;
                // Keep backpressure until this addon alone is acknowledged.
            }
            return results.ToArray();
        }
        YogaPresentation Layout(Addon addon, Batch batch)
        {
            double queue = (Stopwatch.GetTimestamp() - batch.Enqueued) * 1000d / Stopwatch.Frequency;
            long start = Stopwatch.GetTimestamp();
            double elapsed = 0; int calculations = 0, measurements = 0;
            var staged = new Dictionary<ulong, Root>();
            var created = new List<Root>();
            var nodes = new UiNode[batch.Snapshot.Nodes.Count];
            var textBudget = new YogaTextBudget();
            try
            {
                var groups = new Dictionary<ulong, List<int>>();
                var rootOf = new Dictionary<ulong, ulong>();
                for (int i = 0; i < nodes.Length; ++i)
                {
                    var n = batch.Snapshot.Nodes[i];
                    ulong root = n.Parent == 0 ? n.Handle : rootOf[n.Parent]; rootOf.Add(n.Handle, root);
                    if (!groups.TryGetValue(root, out var indices)) groups.Add(root, indices = new List<int>());
                    indices.Add(i);
                }
                foreach (var group in groups)
                {
                    var styles = new YogaStyle[group.Value.Count]; var indexOf = new Dictionary<ulong, int>();
                    var textKeys = new string[styles.Length]; bool hasText = false;
                    for (int i = 0; i < styles.Length; ++i)
                    {
                        int index = group.Value[i]; var n = batch.Snapshot.Nodes[index]; styles[i] = batch.Styles[index];
                        styles[i].Parent = n.Parent == 0 ? -1 : indexOf[n.Parent]; indexOf.Add(n.Handle, i);
                        textKeys[i] = n.Kind == UiNodeKind.Text ? n.StyleKey + "\0" + n.Text : "";
                        hasText |= n.Kind == UiNodeKind.Text && n.Layout != null;
                    }
                    Root root;
                    if (addon.Roots.TryGetValue(group.Key, out var previous) && Same(previous.Styles, styles) && SameText(previous.TextKeys, textKeys)) root = previous;
                    else
                    {
                        if (hasText && textCatalog == null) throw new InvalidOperationException("Yoga text requires captured font metrics.");
                        root = new Root { Native = new NativeYoga(), Styles = styles, TextKeys = textKeys }; created.Add(root);
                        var measurer = hasText ? new TextMeasurer { Snapshot = batch.Snapshot, Indices = group.Value, Catalog = textCatalog, Budget = textBudget } : null;
                        if (!root.Native.TryLayout(styles, out root.Rects, out var metrics, out double ms, out string error, measurer)) throw new InvalidOperationException(error);
                        elapsed += ms; calculations += metrics.Rebuilt; measurements += metrics.Measurements;
                    }
                    staged.Add(group.Key, root);
                    for (int i = 0; i < styles.Length; ++i)
                    {
                        var original = batch.Snapshot.Nodes[group.Value[i]]; var r = root.Rects[i];
                        // Root anchoring stays a host-validated absolute escape hatch.
                        int x = i == 0 ? original.Rect.X : checked((int)Math.Round(r.X * 64));
                        int y = i == 0 ? original.Rect.Y : checked((int)Math.Round(r.Y * 64));
                        nodes[group.Value[i]] = original.With(rect: new UiRect(x, y, checked((int)Math.Round(r.Width * 64)), checked((int)Math.Round(r.Height * 64))));
                    }
                }
                var snapshot = new UiSnapshot(batch.Snapshot.Generation, nodes);
                if (!UiSnapshotValidator.Validate(snapshot, batch.Owner, addon.Theme, out string validation, addon.LayoutNodeLimit, addon.Surfaces)) throw new InvalidOperationException(validation);
                foreach (var pair in addon.Roots) if (!staged.TryGetValue(pair.Key, out var retained) || retained != pair.Value) pair.Value.Dispose();
                addon.Roots = staged; addon.Revision = batch.Revision;
                return new YogaPresentation(batch.Owner, batch.Revision, snapshot, null, elapsed, queue, calculations, measurements,
                    (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
            catch (Exception error)
            {
                foreach (var root in created) root.Dispose();
                return new YogaPresentation(batch.Owner, batch.Revision, null, error.Message, elapsed, queue, calculations, measurements,
                    (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }
        static bool Same(YogaStyle[] a, YogaStyle[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i) if (!a[i].Equals(b[i])) return false;
            return true;
        }
        static bool SameText(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }
        public void Acknowledge(uint owner, long revision, bool accepted)
        {
            Check();
            if (!addons.TryGetValue(owner, out var addon) || !addon.Pending || !addon.Presented || addon.OutstandingRevision != revision)
                throw new InvalidOperationException("No matching pending Yoga presentation.");
            addon.Pending = false;
            // A failed main apply retires this root; the transport must retire its guest.
            if (!accepted && addon.ValidPresentation) { foreach (var root in addon.Roots.Values) root.Dispose(); addon.Roots.Clear(); }
        }
        public void Dispose()
        {
            Check();
            foreach (var addon in addons.Values) foreach (var root in addon.Roots.Values) root.Dispose();
            ready.Clear(); addons.Clear(); disposed = true;
        }
    }
}
