using System;
using System.Collections.Generic;

namespace PortableUi
{
    // One session belongs to one guest instance. No CLR/Unity object crosses the guest ABI.
    public sealed class UiSession
    {
        public const int MaxNodes = 1024;
        public const int MaxRoots = 16;
        public const int MaxEvents = 128;
        public const int MaxEventPayloadBytes = 256;
        const ushort AllowedEvents = (ushort)(UiEventMask.Activate | UiEventMask.Close |
            UiEventMask.Hover | UiEventMask.DragStart | UiEventMask.Drop | UiEventMask.Focus);
        readonly uint owner;
        readonly IUiTheme theme;
        readonly Dictionary<ulong, UiNode> nodes = new Dictionary<ulong, UiNode>();
        readonly HashSet<ulong> reserved = new HashSet<ulong>();
        readonly Queue<UiEvent> events = new Queue<UiEvent>();
        ushort generation = 1, nextSequence;
        int nextOrder;

        public uint Owner => owner;
        public uint Generation => generation;
        public int NodeCount => nodes.Count;
        public int PendingEventCount => events.Count;

        public UiSession(uint ownerNonce, IUiTheme injectedTheme)
        {
            if (ownerNonce == 0) throw new ArgumentOutOfRangeException(nameof(ownerNonce));
            owner = ownerNonce;
            theme = injectedTheme ?? throw new ArgumentNullException(nameof(injectedTheme));
        }

        // Called by the trusted WACS import. Reservation has no visible UI effect.
        public UiResult ReserveHandle(out ulong handle)
        {
            handle = 0;
            if (nodes.Count + reserved.Count >= MaxNodes || nextSequence == ushort.MaxValue)
                return new UiResult(UiError.QuotaExceeded, "Handle quota exhausted.");
            handle = ((ulong)owner << 32) | ((ulong)generation << 16) | ++nextSequence;
            reserved.Add(handle);
            return UiResult.Ok;
        }

        public UiResult Submit(byte[] copiedPacket, IUiCommitTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (copiedPacket == null || copiedPacket.Length > UiPacket.MaxBytes)
                return new UiResult(UiError.InvalidPacket, "Packet size invalid.");
            var parse = UiPacket.Parse((byte[])copiedPacket.Clone(), out var commands);
            if (!parse.Success) return parse;
            var proposed = new Dictionary<ulong, UiNode>(nodes);
            int proposedOrder = nextOrder;
            var consumed = new List<ulong>();
            foreach (var command in commands)
            {
                var result = Apply(command, proposed, consumed, ref proposedOrder);
                if (!result.Success) return result;
            }
            var snapshot = Snapshot(proposed);
            try
            {
                if (!target.TryCommit(snapshot, out string failure))
                    return new UiResult(UiError.CommitRejected, failure ?? "Native commit rejected.");
            }
            catch (Exception error)
            {
                return new UiResult(UiError.CommitRejected, error.Message);
            }
            nodes.Clear();
            foreach (var pair in proposed) nodes.Add(pair.Key, pair.Value);
            foreach (var handle in consumed) reserved.Remove(handle);
            nextOrder = proposedOrder;
            return UiResult.Ok;
        }

        UiResult Apply(UiCommand command, Dictionary<ulong, UiNode> next,
            List<ulong> consumed, ref int order)
        {
            var handle = command.Handle;
            if (command.Opcode == UiOpcode.Create)
            {
                var identity = CheckIdentity(handle);
                if (!identity.Success) return identity;
                if (!reserved.Contains(handle) || next.ContainsKey(handle) || consumed.Contains(handle))
                    return new UiResult(UiError.InvalidHandle, "Create requires a fresh reserved handle.");
                if (!Enum.IsDefined(typeof(UiNodeKind), command.Kind))
                    return new UiResult(UiError.InvalidCommand, "Unknown node kind.");
                var geometry = CheckRect(command.Rect);
                if (!geometry.Success) return geometry;
                if (!ValidEvents(command.Events)) return new UiResult(UiError.InvalidCommand, "Unknown event mask.");
                if (command.Parent == 0)
                {
                    if (command.Kind != UiNodeKind.Window && command.Kind != UiNodeKind.Panel)
                        return new UiResult(UiError.InvalidCommand, "Root must be a window or panel.");
                    int roots = 0;
                    foreach (var node in next.Values) if (node.Parent == 0) ++roots;
                    if (roots >= MaxRoots) return new UiResult(UiError.QuotaExceeded, "Root quota exceeded.");
                }
                else
                {
                    identity = CheckLive(command.Parent, next, out _);
                    if (!identity.Success) return identity;
                }
                if (next.Count >= MaxNodes) return new UiResult(UiError.QuotaExceeded, "Node quota exceeded.");
                var style = CheckTheme(command.Style, false);
                if (!style.Success) return style;
                var asset = CheckTheme(command.Asset, true);
                if (!asset.Success) return asset;
                if ((command.Kind == UiNodeKind.Image || command.Kind == UiNodeKind.Portrait) &&
                    string.IsNullOrEmpty(command.Asset))
                    return new UiResult(UiError.InvalidCommand, "Image requires an asset key.");
                next.Add(handle, new UiNode(handle, command.Parent, command.Kind, command.Rect,
                    command.Style, command.Asset, command.Text, 0, true, command.Events, ++order));
                consumed.Add(handle);
                return UiResult.Ok;
            }
            var live = CheckLive(handle, next, out var current);
            if (!live.Success) return live;
            switch (command.Opcode)
            {
                case UiOpcode.SetText:
                    if (current.Kind != UiNodeKind.Text && current.Kind != UiNodeKind.Window)
                        return new UiResult(UiError.InvalidCommand, "Node has no text.");
                    next[handle] = current.With(text: command.Text); break;
                case UiOpcode.SetImage:
                    if (current.Kind != UiNodeKind.Image && current.Kind != UiNodeKind.Portrait &&
                        current.Kind != UiNodeKind.Slot)
                        return new UiResult(UiError.InvalidCommand, "Node has no image.");
                    var asset = CheckTheme(command.Asset, true);
                    if (!asset.Success) return asset;
                    if (string.IsNullOrEmpty(command.Asset))
                        return new UiResult(UiError.InvalidCommand, "Image key required.");
                    next[handle] = current.With(asset: command.Asset); break;
                case UiOpcode.SetProgress:
                    if (current.Kind != UiNodeKind.Progress || command.Value > 10000)
                        return new UiResult(UiError.InvalidCommand, "Progress must be 0..10000 on a progress node.");
                    next[handle] = current.With(progress: command.Value); break;
                case UiOpcode.SetVisible:
                    next[handle] = current.With(visible: command.Visible); break;
                case UiOpcode.SetEvents:
                    if (!ValidEvents(command.Events)) return new UiResult(UiError.InvalidCommand, "Unknown event mask.");
                    next[handle] = current.With(events: command.Events); break;
                case UiOpcode.SetRect:
                    var rect = CheckRect(command.Rect);
                    if (!rect.Success) return rect;
                    next[handle] = current.With(rect: command.Rect); break;
                case UiOpcode.Raise:
                    next[handle] = current.With(order: ++order); break;
                case UiOpcode.Remove:
                    var doomed = new HashSet<ulong> { handle };
                    bool added;
                    do
                    {
                        added = false;
                        foreach (var node in next.Values)
                            if (doomed.Contains(node.Parent) && doomed.Add(node.Handle)) added = true;
                    } while (added);
                    foreach (var dead in doomed) next.Remove(dead);
                    break;
                default: return new UiResult(UiError.InvalidCommand, "Unknown opcode.");
            }
            return UiResult.Ok;
        }

        UiResult CheckTheme(string key, bool asset)
        {
            if (string.IsNullOrEmpty(key)) return UiResult.Ok;
            try
            {
                if (asset ? theme.AllowsAsset(key) : theme.AllowsStyle(key)) return UiResult.Ok;
            }
            catch (Exception error) { return new UiResult(UiError.ForbiddenThemeKey, error.Message); }
            return new UiResult(UiError.ForbiddenThemeKey, "Theme key rejected.");
        }

        static UiResult CheckRect(UiRect rect)
        {
            const long coordinate = 8192L * 64;
            const int size = 4096 * 64;
            if (rect.X < -coordinate || rect.X > coordinate || rect.Y < -coordinate || rect.Y > coordinate ||
                rect.Width <= 0 || rect.Width > size || rect.Height <= 0 || rect.Height > size ||
                (long)rect.X + rect.Width > coordinate || (long)rect.Y + rect.Height > coordinate)
                return new UiResult(UiError.InvalidCommand, "Rect exceeds 1/64-unit bounds.");
            return UiResult.Ok;
        }

        static bool ValidEvents(UiEventMask events) => ((ushort)events & ~AllowedEvents) == 0;

        UiResult CheckIdentity(ulong handle)
        {
            if ((uint)(handle >> 32) != owner || (ushort)handle == 0)
                return new UiResult(UiError.InvalidHandle, "Foreign or zero handle.");
            if ((ushort)(handle >> 16) != generation)
                return new UiResult(UiError.StaleHandle, "Handle generation expired.");
            return UiResult.Ok;
        }

        UiResult CheckLive(ulong handle, Dictionary<ulong, UiNode> set, out UiNode node)
        {
            node = null;
            var identity = CheckIdentity(handle);
            if (!identity.Success) return identity;
            if (!set.TryGetValue(handle, out node))
                return new UiResult(UiError.InvalidHandle, "Handle does not own a live node.");
            return UiResult.Ok;
        }

        UiSnapshot Snapshot(Dictionary<ulong, UiNode> set)
        {
            var children = new Dictionary<ulong, List<UiNode>>();
            foreach (var node in set.Values)
            {
                if (!children.TryGetValue(node.Parent, out var siblings))
                    children.Add(node.Parent, siblings = new List<UiNode>());
                siblings.Add(node);
            }
            foreach (var siblings in children.Values)
                siblings.Sort((a, b) => a.Order.CompareTo(b.Order));
            var all = new List<UiNode>(set.Count);
            var pending = new Stack<UiNode>();
            if (children.TryGetValue(0, out var roots))
                for (int i = roots.Count - 1; i >= 0; --i) pending.Push(roots[i]);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                all.Add(node);
                if (children.TryGetValue(node.Handle, out var direct))
                    for (int i = direct.Count - 1; i >= 0; --i) pending.Push(direct[i]);
            }
            return new UiSnapshot(generation, all.ToArray());
        }

        public UiResult QueueEvent(ulong handle, UiEventMask kind, byte[] copiedPayload)
        {
            if (!ValidEvents(kind) || kind == UiEventMask.None ||
                (((ushort)kind & ((ushort)kind - 1)) != 0))
                return new UiResult(UiError.InvalidEvent, "Event kind must be one known bit.");
            if (copiedPayload == null || copiedPayload.Length > MaxEventPayloadBytes)
                return new UiResult(UiError.QuotaExceeded, "Event payload exceeds 256 bytes.");
            var result = CheckLive(handle, nodes, out var node);
            if (!result.Success) return result;
            if (!EffectivelyVisible(node) || (node.Events & kind) == 0)
                return new UiResult(UiError.InvalidEvent, "Node is hidden or not subscribed.");
            if (events.Count >= MaxEvents) return new UiResult(UiError.QuotaExceeded, "Event queue full.");
            events.Enqueue(new UiEvent(handle, kind, copiedPayload));
            return UiResult.Ok;
        }

        // The WACS adapter pulls events after native commit; never invoke guest code here.
        public bool TryPeekEvent(out UiEvent result)
        {
            while (events.Count > 0)
            {
                result = events.Peek();
                if (nodes.TryGetValue(result.Handle, out var node) && EffectivelyVisible(node) &&
                    (node.Events & result.Kind) != 0) return true;
                events.Dequeue();
            }
            result = default;
            return false;
        }

        public bool TryDequeueEvent(out UiEvent result)
        {
            if (!TryPeekEvent(out result)) return false;
            events.Dequeue();
            return true;
        }

        // Host-side encoding. The WACS adapter copies this bounded buffer into validated guest memory.
        public UiResult WriteNextEvent(byte[] destination, out int written)
        {
            written = 0;
            if (destination == null || destination.Length < 16 + MaxEventPayloadBytes)
                return new UiResult(UiError.InvalidPacket, "Event destination must hold 272 bytes.");
            if (!TryPeekEvent(out var next)) return UiResult.Ok;
            var payload = next.CopyPayload();
            destination[0] = 1; destination[1] = 0;
            destination[2] = (byte)next.Kind; destination[3] = (byte)((ushort)next.Kind >> 8);
            destination[4] = (byte)payload.Length; destination[5] = (byte)(payload.Length >> 8);
            destination[6] = 0; destination[7] = 0;
            for (int i = 0; i < 8; i++) destination[8 + i] = (byte)(next.Handle >> (i * 8));
            Buffer.BlockCopy(payload, 0, destination, 16, payload.Length);
            written = 16 + payload.Length;
            events.Dequeue();
            return UiResult.Ok;
        }

        public UiResult RequestAction(ulong handle, ushort capability, byte[] copiedPayload,
            IUiActionAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (capability == 0 || copiedPayload == null || copiedPayload.Length > MaxEventPayloadBytes)
                return new UiResult(UiError.InvalidCommand, "Invalid action envelope.");
            var live = CheckLive(handle, nodes, out var node);
            if (!live.Success) return live;
            if (!EffectivelyVisible(node)) return new UiResult(UiError.InvalidHandle, "Hidden node cannot request actions.");
            try
            {
                return adapter.TryRequest(capability, handle, (byte[])copiedPayload.Clone())
                    ? UiResult.Ok : new UiResult(UiError.InvalidCommand, "Action rejected by adapter.");
            }
            catch (Exception error) { return new UiResult(UiError.InvalidCommand, error.Message); }
        }

        public UiResult Unload(IUiCommitTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (generation == ushort.MaxValue)
                return new UiResult(UiError.QuotaExceeded, "Generation exhausted; create a new session.");
            try
            {
                if (!target.TryCommit(new UiSnapshot((uint)(generation + 1), Array.Empty<UiNode>()), out var failure))
                    return new UiResult(UiError.CommitRejected, failure);
            }
            catch (Exception error) { return new UiResult(UiError.CommitRejected, error.Message); }
            nodes.Clear(); reserved.Clear(); events.Clear();
            ++generation; nextSequence = 0; nextOrder = 0;
            return UiResult.Ok;
        }

        bool EffectivelyVisible(UiNode node)
        {
            while (true)
            {
                if (!node.Visible) return false;
                if (node.Parent == 0) return true;
                if (!nodes.TryGetValue(node.Parent, out node)) return false;
            }
        }
    }
}
