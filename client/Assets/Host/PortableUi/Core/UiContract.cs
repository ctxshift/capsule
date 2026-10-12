using System;
using System.Collections.Generic;

namespace PortableUi
{
    public enum UiError : ushort
    {
        None = 0, InvalidPacket = 1, UnsupportedVersion = 2, QuotaExceeded = 3,
        InvalidCommand = 4, InvalidHandle = 5, StaleHandle = 6,
        ForbiddenThemeKey = 7, CommitRejected = 8, InvalidEvent = 9
    }

    public readonly struct UiResult
    {
        public readonly UiError Error;
        public readonly string Detail;
        public bool Success => Error == UiError.None;
        public UiResult(UiError error, string detail = null)
        {
            Error = error;
            Detail = detail == null ? string.Empty : detail.Length > 160 ? detail.Substring(0, 160) : detail;
        }
        public static UiResult Ok => new UiResult(UiError.None);
    }

    public enum UiNodeKind : byte
    {
        Window = 1, Panel = 2, Text = 3, Image = 4, Portrait = 5,
        Slot = 6, Progress = 7
    }

    [Flags]
    public enum UiEventMask : ushort
    {
        None = 0, Activate = 1, Close = 2, Hover = 4, DragStart = 8,
        Drop = 16, Focus = 32
    }

    public readonly struct UiRect
    {
        // Signed 1/64 design-unit fixed point. Width and height must be positive.
        public readonly int X, Y, Width, Height;
        public UiRect(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    public sealed class UiNode
    {
        public ulong Handle { get; }
        public ulong Parent { get; }
        public UiNodeKind Kind { get; }
        public UiRect Rect { get; }
        public string StyleKey { get; }
        public string AssetKey { get; }
        public string Text { get; }
        public ushort Progress { get; }
        public bool Visible { get; }
        public UiEventMask Events { get; }
        public int Order { get; }

        public UiNode(ulong handle, ulong parent, UiNodeKind kind, UiRect rect,
            string styleKey, string assetKey, string text, ushort progress,
            bool visible, UiEventMask events, int order)
        {
            Handle = handle; Parent = parent; Kind = kind; Rect = rect;
            StyleKey = styleKey; AssetKey = assetKey; Text = text;
            Progress = progress; Visible = visible; Events = events; Order = order;
        }

        public UiNode With(UiRect? rect = null, string style = null, string asset = null,
            string text = null, ushort? progress = null, bool? visible = null,
            UiEventMask? events = null, int? order = null) =>
            new UiNode(Handle, Parent, Kind, rect ?? Rect, style ?? StyleKey,
                asset ?? AssetKey, text ?? Text, progress ?? Progress,
                visible ?? Visible, events ?? Events, order ?? Order);
    }

    public sealed class UiSnapshot
    {
        readonly UiNode[] nodes;
        readonly IReadOnlyList<UiNode> view;
        public uint Generation { get; }
        public IReadOnlyList<UiNode> Nodes => view;
        public UiSnapshot(uint generation, UiNode[] source)
        {
            Generation = generation;
            nodes = (UiNode[])source.Clone();
            view = Array.AsReadOnly(nodes);
        }
    }

    // Host supplies exact art, typography, spacing and palette through this boundary.
    public interface IUiTheme
    {
        bool AllowsStyle(string key);
        bool AllowsAsset(string key);
    }

    // Must stage or roll back native work: false leaves the previously visible tree intact.
    public interface IUiCommitTarget
    {
        bool TryCommit(UiSnapshot proposed, out string error);
    }

    // Only the game adapter may interpret a capability and recheck authority at dispatch.
    public interface IUiActionAdapter
    {
        bool TryRequest(ushort capability, ulong sourceHandle, byte[] copiedPayload);
    }

    // Trusted input adapter routes permitted intents; it never grants gameplay authority.
    public interface IUiInputIntentAdapter
    {
        bool TryRequestInput(ushort capability, byte[] copiedPayload);
    }

    // Game-defined observation topics and payloads stay outside the generic UI schema.
    public interface IUiObservationAdapter
    {
        bool TryDequeue(out ushort topic, out ushort schemaVersion, out byte[] copiedPayload);
    }

    public readonly struct UiEvent
    {
        public readonly ushort SchemaVersion;
        public readonly ulong Handle;
        public readonly UiEventMask Kind;
        readonly byte[] payload;
        public byte[] CopyPayload() => (byte[])payload.Clone();
        public UiEvent(ulong handle, UiEventMask kind, byte[] data)
        { SchemaVersion = 1; Handle = handle; Kind = kind; payload = (byte[])data.Clone(); }
    }
}
