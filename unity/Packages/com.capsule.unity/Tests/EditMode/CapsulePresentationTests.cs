using System;
using System.IO;
using NUnit.Framework;
using Capsule;

namespace Capsule.Tests
{
    public sealed class CapsulePresentationTests
    {
        sealed class Theme : IUiTheme { public bool AllowsStyle(string key)=>true; public bool AllowsAsset(string key)=>true; }
        sealed class Target : IUiCommitTarget { public UiSnapshot Last; public bool TryCommit(UiSnapshot snapshot,out string error){Last=snapshot;error=null;return true;} }
        static byte[] Packet(Action<BinaryWriter> write, byte opcode)
        {
            using var body=new MemoryStream(); using var writer=new BinaryWriter(body); write(writer);
            using var stream=new MemoryStream(); using var output=new BinaryWriter(stream);
            output.Write(0x31495550u);output.Write((ushort)1);output.Write((ushort)0);output.Write((ushort)1);output.Write((ushort)0);output.Write((uint)(20+body.Length));
            output.Write(opcode);output.Write((byte)0);output.Write((ushort)body.Length);output.Write(body.ToArray());return stream.ToArray();
        }
        [Test]
        public void WireCommandsPreserveAndCancelPresentationAndRejectInvalidLimits()
        {
            var session=new UiSession(7,new Theme());var target=new Target();session.ReserveHandle(out var id);
            var created=session.Submit(Packet(w=>{w.Write(id);w.Write(0UL);w.Write((byte)UiNodeKind.Box);w.Write(0);w.Write(0);w.Write(6400);w.Write(6400);w.Write((byte)0);w.Write((byte)0);w.Write((ushort)0);w.Write((ushort)0);},1),target);
            Assert.That(created.Success,Is.True,created.Detail);
            byte[] Anchor(int far)=>Packet(w=>{w.Write(id);w.Write((byte)1);w.Write((byte)1);w.Write((byte)'a');w.Write((byte)1);w.Write((byte)'h');foreach(int v in new[]{0,128,0,0,far,640,32,128})w.Write(v);w.Write((byte)1);},15);
            Assert.That(session.Submit(Anchor(6400),target).Success,Is.True);
            var anchor=target.Last.Nodes[0].WorldAnchor;
            Assert.That(anchor.Entity,Is.EqualTo("a"));Assert.That(anchor.Occlusion,Is.True);
            Assert.That(session.Submit(Anchor(64001),target).Success,Is.False);
            Assert.That(target.Last.Nodes[0].WorldAnchor,Is.SameAs(anchor));
            byte[] Effect(int duration)=>Packet(w=>{w.Write(id);w.Write((byte)1);w.Write(9u);w.Write(duration);w.Write(200);w.Write(640);w.Write(-1280);},16);
            Assert.That(session.Submit(Effect(1000),target).Success,Is.True);
            Assert.That(target.Last.Nodes[0].With(visible:false).Effect.Revision,Is.EqualTo(9));
            Assert.That(target.Last.Nodes[0].WorldAnchor,Is.SameAs(anchor));
            Assert.That(session.Submit(Effect(10001),target).Success,Is.False);
            Assert.That(session.Submit(Packet(w=>{w.Write(id);w.Write((byte)0);},16),target).Success,Is.True);
            Assert.That(target.Last.Nodes[0].Effect,Is.Null);
        }
        [Test]
        public void SnapshotRejectsNestedAnchorsWrongKindsAndExcessiveEffects()
        {
            UiNode Node(ulong id,ulong parent=0,UiNodeKind kind=UiNodeKind.Box,UiWorldAnchor anchor=null,UiPresentationEffect effect=null)=>new UiNode(id,parent,kind,new UiRect(0,0,64,64),"","","",0,true,0,0,worldAnchor:anchor,effect:effect);
            var a=new UiWorldAnchor("actor.1","head");
            Assert.That(UiPresentation.Validate(new UiSnapshot(1,new[]{Node(1,anchor:a),Node(2,1,anchor:a)}),out _),Is.False);
            Assert.That(UiPresentation.Validate(new UiSnapshot(1,new[]{Node(1,kind:UiNodeKind.Text,anchor:a)}),out _),Is.False);
            var nodes=new UiNode[65];for(int i=0;i<nodes.Length;i++)nodes[i]=Node((ulong)i+1,effect:new UiPresentationEffect(1,100));
            Assert.That(UiPresentation.Validate(new UiSnapshot(1,nodes),out _),Is.False);
            Assert.That(new UiWorldAnchor("../actor","head").Valid,Is.False);
            Assert.That(new UiWorldAnchor("actor","head",x:int.MinValue).Valid,Is.False);
        }
    }
}
