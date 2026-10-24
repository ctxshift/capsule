using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Capsule;

static partial class Program
{
    static int checks;
    static void Check(bool condition, string message)
    { ++checks; if (!condition) throw new Exception(message); }

    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => key == "window" || key == "slot";
        public bool AllowsAsset(string key) => key == "portrait" || key == "sword";
    }

    sealed class Renderer : IUiCommitTarget
    {
        public UiSnapshot Last;
        public bool Reject;
        public bool TryCommit(UiSnapshot proposed, out string error)
        {
            error = Reject ? "staging failed" : null;
            if (Reject) return false;
            Last = proposed;
            return true;
        }
    }

    sealed class Actions : IUiActionAdapter
    {
        public byte[] Seen;
        public bool TryRequest(ushort capability, ulong source, byte[] payload)
        { Seen = payload; return capability == 7; }
    }

    internal static byte[] Packet(params byte[][] commands)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x31495550u); writer.Write((ushort)1); writer.Write((ushort)0);
            writer.Write((ushort)commands.Length); writer.Write((ushort)0);
            writer.Write((uint)(16 + Sum(commands)));
            foreach (var command in commands) writer.Write(command);
            return stream.ToArray();
        }
    }
    static int Sum(byte[][] commands) { int sum = 0; foreach (var c in commands) sum += c.Length; return sum; }
    static byte[] Command(UiOpcode opcode, Action<BinaryWriter> body)
    {
        using (var data = new MemoryStream())
        using (var writer = new BinaryWriter(data))
        {
            body(writer);
            var payload = data.ToArray();
            using (var frame = new MemoryStream())
            using (var output = new BinaryWriter(frame))
            {
                output.Write((byte)opcode); output.Write((byte)0);
                output.Write((ushort)payload.Length); output.Write(payload);
                return frame.ToArray();
            }
        }
    }
    static void Key(BinaryWriter writer, string value)
    { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((byte)bytes.Length); writer.Write(bytes); }
    static void Text(BinaryWriter writer, string value)
    { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    internal static byte[] Create(ulong id, ulong parent, UiNodeKind kind, string style, string asset,
        string text, UiEventMask events = UiEventMask.None) => Command(UiOpcode.Create, w =>
        {
            w.Write(id); w.Write(parent); w.Write((byte)kind);
            w.Write(0); w.Write(0); w.Write(460 * 64); w.Write(260 * 64);
            Key(w, style); Key(w, asset); Text(w, text); w.Write((ushort)events);
        });
    static byte[] SetText(ulong id, string value) => Command(UiOpcode.SetText, w =>
    { w.Write(id); Text(w, value); });
    internal static byte[] Remove(ulong id) => Command(UiOpcode.Remove, w => w.Write(id));
    static byte[] Visible(ulong id, bool value) => Command(UiOpcode.SetVisible, w =>
    { w.Write(id); w.Write((byte)(value ? 1 : 0)); });

    static void Main(string[] args)
    {
        CheckCoreLayoutCapacity();
        if (Array.IndexOf(args, "--layout-capacity") >= 0)
        { Console.WriteLine("PASS layout capacity: " + checks + " checks"); return; }
        checks += HostSessionCases.Run();
        SurfaceCases();
        var renderer = new Renderer();
        var session = new UiSession(0xC0FFEEu, new Theme());
        Check(session.ReserveHandle(out var window).Success, "reserve root");
        Check(session.ReserveHandle(out var image).Success, "reserve child");
        var initial = Packet(Create(window, 0, UiNodeKind.Window, "window", "", "Inventory",
            UiEventMask.Close), Create(image, window, UiNodeKind.Image, "", "portrait", "",
            UiEventMask.Activate));
        Check(session.Submit(initial, renderer).Success, "valid parent-first batch");
        Check(session.NodeCount == 2 && renderer.Last.Nodes.Count == 2, "committed tree");
        Check(renderer.Last.Nodes[0].Handle == window, "parent precedes child");

        var old = renderer.Last;
        renderer.Reject = true;
        var rejected = session.Submit(Packet(SetText(window, "Changed")), renderer);
        Check(rejected.Error == UiError.CommitRejected, "rejected native stage");
        Check(renderer.Last == old && session.NodeCount == 2, "rejection is atomic");
        renderer.Reject = false;
        Check(session.Submit(Packet(SetText(window, "Changed")), renderer).Success, "retry accepted");
        Check(renderer.Last.Nodes[0].Text == "Changed", "text updated");

        Check(session.Submit(Packet(Command(UiOpcode.SetImage, w => { w.Write(image); Key(w, "evil"); })), renderer)
            .Error == UiError.ForbiddenThemeKey, "allowlist enforced");
        Check(session.Submit(Packet(SetText(window, "Partial"),
            Command(UiOpcode.SetImage, w => { w.Write(image); Key(w, "evil"); })), renderer)
            .Error == UiError.ForbiddenThemeKey, "whole batch rejected");
        Check(renderer.Last.Nodes[0].Text == "Changed", "invalid later command did not apply earlier edit");
        Check(session.Submit(Packet(Create(window, 0, UiNodeKind.Window, "window", "", "Duplicate")), renderer)
            .Error == UiError.InvalidHandle, "reused handle rejected");
        var foreign = ((ulong)0xBADu << 32) | (window & 0xffffffffu);
        Check(session.Submit(Packet(Remove(foreign)), renderer).Error == UiError.InvalidHandle,
            "foreign owner rejected");

        var bytes = Packet(SetText(window, "é"));
        bytes[bytes.Length - 2] = 0xff;
        Check(session.Submit(bytes, renderer).Error == UiError.InvalidPacket, "strict UTF8");
        var wrongVersion = Packet(); wrongVersion[4] = 3;
        Check(session.Submit(wrongVersion, renderer).Error == UiError.UnsupportedVersion, "ABI version");
        var truncated = Packet(); Array.Resize(ref truncated, 15);
        Check(session.Submit(truncated, renderer).Error == UiError.InvalidPacket, "truncated header");
        Check(session.Submit(Packet(Command(UiOpcode.SetRect, w =>
            { w.Write(window); w.Write(int.MaxValue); w.Write(0); w.Write(64); w.Write(64); })), renderer)
            .Error == UiError.InvalidCommand, "geometry bound");

        var original = new byte[] { 1, 2 };
        Check(session.QueueEvent(window, UiEventMask.Close, original).Success, "event queued");
        original[0] = 99;
        Check(session.TryPeekEvent(out var eventValue) && eventValue.CopyPayload()[0] == 1, "event copied");
        var tooSmall = new byte[16];
        Check(session.WriteNextEvent(tooSmall, out _).Error == UiError.InvalidPacket,
            "small event buffer rejected without consumption");
        var encoded = new byte[272];
        Check(session.WriteNextEvent(encoded, out var written).Success && written == 18,
            "event encoded and consumed");
        Check(encoded[0] == 1 && encoded[2] == (byte)UiEventMask.Close && encoded[16] == 1,
            "event envelope fields");
        var actions = new Actions();
        Check(session.RequestAction(window, 7, original, actions).Success, "trusted action dispatch");
        Check(!ReferenceEquals(original, actions.Seen), "action payload copied");

        Check(session.QueueEvent(image, UiEventMask.Activate, Array.Empty<byte>()).Success,
            "child event queued while visible");
        Check(session.Submit(Packet(Visible(window, false)), renderer).Success, "hide parent");
        Check(Obsolete(image, UiEventMask.Activate), "hidden ancestor makes presented input obsolete");
        Check(!session.TryDequeueEvent(out _), "queued child event invalidated by hidden parent");
        Check(session.QueueEvent(image, UiEventMask.Activate, Array.Empty<byte>()).Error == UiError.InvalidEvent,
            "hidden ancestor blocks input");
        Check(session.RequestAction(image, 7, Array.Empty<byte>(), actions).Error == UiError.InvalidHandle,
            "hidden ancestor blocks action");
        Check(session.Submit(Packet(Visible(window, true)), renderer).Success, "show parent");
        Check(!Obsolete(image, UiEventMask.Activate), "visible subscribed input stays live");
        Check(Obsolete(image, UiEventMask.Close), "unsubscribed input is obsolete");
        Check(session.RequestAction(image, 7, Array.Empty<byte>(), actions).Success,
            "visible descendant can request action");
        Check(session.ReserveHandle(out var slot).Success, "reserve slot");
        Check(session.Submit(Packet(Create(slot, window, UiNodeKind.Slot, "slot", "sword", "")), renderer).Success,
            "observed slot visual created");
        Check(session.Submit(Packet(Command(UiOpcode.SetImage, w => { w.Write(slot); Key(w, "portrait"); })), renderer)
            .Success, "slot visual may update without changing authority");
        Check(session.Submit(Packet(SetText(slot, "Leather Jerkin")), renderer).Success,
            "retained slot label accepts the equip SetText command");
        UiNode FindSlot() { foreach (var node in renderer.Last.Nodes) if (node.Handle == slot) return node; throw new Exception("Missing slot"); }
        Check(FindSlot().Text == "Leather Jerkin" && FindSlot().AssetKey == "portrait",
            "slot label update preserves image and identity");
        Check(session.Submit(Packet(SetText(slot, "Empty slot"),
            Command(UiOpcode.SetImage, w => { w.Write(slot); Key(w, "evil"); })), renderer)
            .Error == UiError.ForbiddenThemeKey, "invalid slot image rejects entire label/image update");
        Check(FindSlot().Text == "Leather Jerkin" && FindSlot().AssetKey == "portrait",
            "rejected equip packet retains last accepted slot label and image");
        Check(session.Submit(Packet(SetText(image, "Image accessibility title")), renderer).Success,
            "image accessibility text accepts the same updates as initial creation");
        for (int i = 0; i < UiSession.MaxEvents; i++)
            Check(session.QueueEvent(window, UiEventMask.Close, Array.Empty<byte>()).Success, "bounded event enqueue");
        Check(session.QueueEvent(window, UiEventMask.Close, Array.Empty<byte>()).Error == UiError.QuotaExceeded,
            "event queue limit");

        Check(session.Submit(Packet(Remove(window)), renderer).Success, "remove subtree");
        Check(session.NodeCount == 0, "children removed");
        bool Obsolete(ulong handle, UiEventMask kind = UiEventMask.Close) => (bool)typeof(UiSession).GetMethod("IsObsoleteEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(session, new object[] { handle, kind });
        Check(Obsolete(window), "removed presented handle is obsolete input");
        Check(!Obsolete(window ^ (1UL << 32)), "foreign handles are not obsolete input");
        Check(!Obsolete(window + 100), "never reserved handles are not obsolete input");
        session.ReserveHandle(out var unused);
        Check(!Obsolete(unused), "uncommitted reservation is not obsolete input");
        Check(session.QueueEvent(window, UiEventMask.Close, Array.Empty<byte>()).Error == UiError.InvalidHandle,
            "removed callback rejected");
        Check(session.Unload(renderer).Success, "unload commits empty tree");
        Check(session.Submit(Packet(Remove(window)), renderer).Error == UiError.StaleHandle,
            "old generation rejected");
        Check(session.ReserveHandle(out var next).Success && next != window, "new generation handle");
        Check(session.Submit(Packet(Create(next, 0, UiNodeKind.Window, "window", "", "New")), renderer).Success,
            "new generation loads");

        for (int i = 0; i < UiSession.MaxNodes - 1; ++i)
            Check(session.ReserveHandle(out _).Success, "bounded reserve");
        Check(session.ReserveHandle(out _).Error == UiError.QuotaExceeded, "node reservation limit");
        Console.WriteLine("PASS portable UI core: " + checks + " checks");
    }

    static void CheckCoreLayoutCapacity()
    {
        foreach (int limit in new[] { UiSession.DefaultLayoutNodes, UiSession.ExtendedLayoutNodes })
        {
            var session = new UiSession(123, new CapacityTheme(), limit);
            var renderer = new Renderer();
            ulong root = 0;
            for (int i = 0; i < 701; i++)
            {
                Check(session.ReserveHandle(out var id).Success, "core capacity reservation");
                var command = Command(UiOpcode.CreateLayout, w => {
                    w.Write(id); w.Write(root); w.Write((byte)UiNodeKind.Box);
                    w.Write(0); w.Write(0); w.Write(64); w.Write(64);
                    Key(w, "window"); Key(w, ""); Text(w, ""); w.Write((ushort)0);
                    w.Write(new byte[] { 0, 0, 1, 0 });
                    for (int j=0;j<11;j++) w.Write(0);
                });
                var packet = Packet(command); packet[4] = 2;
                var result = session.Submit(packet, renderer);
                if (i == limit) { Check(!result.Success, "community layout ceiling retained"); break; }
                Check(result.Success, "core layout accepted: " + result.Error);
                if (i == 0) root = id;
            }
            Check(session.NodeCount == Math.Min(701, limit), "host-owned layout capacity");
        }
    }
    sealed class CapacityTheme : IUiTheme
    {
        public bool AllowsStyle(string key) => key == "window";
        public bool AllowsAsset(string key) => string.IsNullOrEmpty(key);
    }
}
