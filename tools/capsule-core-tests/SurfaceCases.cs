using System;
using System.Collections.Generic;
using Capsule;

static partial class Program
{
    static void SurfaceCases()
    {
        var keys = new List<string> { "preview.owner", "preview.alternate" };
        var admission = new UiSurfaceAdmission(keys);
        keys.Clear();
        Check(admission.Allows("preview.owner") && !admission.Allows("portrait"), "Surface admission copies host keys");
        var theme = new UiThemeSnapshot(new[] { "window", "slot" }, new[] { "portrait" });
        var renderer = new Renderer();
        var session = new UiSession(7, theme, surfaceAdmission: admission);
        session.ReserveHandle(out var root); session.ReserveHandle(out var surface); session.ReserveHandle(out var extra);
        var initial = Packet(Create(root, 0, UiNodeKind.Window, "window", "", "Preview"),
            Create(surface, root, UiNodeKind.Viewport, "slot", "preview.owner", ""));
        Check(session.Submit(initial, renderer).Success, "Viewport accepts admitted non-sprite source");
        var accepted = renderer.Last;
        Check(UiSnapshotValidator.Validate(accepted, 7, theme, out _, surfaceAdmission: admission), "Main-thread surface admission agrees");
        Check(!UiSnapshotValidator.Validate(accepted, 7, theme, out _), "Main thread rejects missing admission");
        Check(!UiSnapshotValidator.Validate(accepted, 7, theme, out _, surfaceAdmission: new UiSurfaceAdmission(new[] { "another.owner" })), "Cross-guest surface key rejected");
        var tooMany = new List<UiNode>(accepted.Nodes) {
            new UiNode(extra, root, UiNodeKind.Viewport, new UiRect(0,0,64,64), "slot", "preview.owner", "", 0, false, UiEventMask.None, 3)
        };
        Check(!UiSnapshotValidator.Validate(new UiSnapshot(1, tooMany.ToArray()), 7, theme, out _, surfaceAdmission: admission),
            "Snapshot surface quota includes hidden leases");
        Check(session.Submit(Packet(Create(extra, root, UiNodeKind.Viewport, "slot", "preview.owner", "")), renderer).Error == UiError.QuotaExceeded,
            "Surface lease quota independent of node quota");
        Check(ReferenceEquals(accepted, renderer.Last), "Rejected surface preserves visible snapshot");
        byte[] Change(string key) => Packet(Command(UiOpcode.SetImage, writer => { writer.Write(surface); Key(writer, key); }));
        Check(session.Submit(Change("portrait"), renderer).Error == UiError.ForbiddenThemeKey, "Sprite admission cannot authorize surface");
        Check(session.Submit(Change(""), renderer).Error == UiError.ForbiddenThemeKey, "Empty surface rejected");
        Check(session.Submit(Change("preview.alternate"), renderer).Success, "Retained viewport may bind another admitted source");
        var denied = new UiSession(7, theme); denied.ReserveHandle(out _); denied.ReserveHandle(out _);
        Check(denied.Submit(initial, new Renderer()).Error == UiError.ForbiddenThemeKey, "Default session denies all surfaces");
        Check(session.Submit(Packet(Remove(surface), Create(extra, root, UiNodeKind.Viewport, "slot", "preview.owner", "")), renderer).Success,
            "Removing old viewport frees lease quota");
        Check(session.Unload(renderer).Success && session.NodeCount == 0, "Unload retires viewport handles");
        foreach (var bad in new[] { "", "../private", "space key", new string('a', 65) })
        {
            bool failed = false;
            try { _ = new UiSurfaceAdmission(new[] { bad }); } catch (ArgumentException) { failed = true; }
            Check(failed, "Invalid surface key rejected");
        }
        bool duplicate = false;
        try { _ = new UiSurfaceAdmission(new[] { "same", "same" }); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate, "Duplicate surface keys rejected");
        foreach (int quota in new[] { 0, 5 })
        {
            bool failed = false;
            try { _ = new UiSurfaceAdmission(new[] { "preview" }, quota); } catch (ArgumentException) { failed = true; }
            Check(failed, "Surface host quota bounded");
        }
    }
}
