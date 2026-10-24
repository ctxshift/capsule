using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Capsule;

namespace Capsule.Tests
{
    public sealed class CapsuleCoreTests
    {
        sealed class Theme : IUiTheme
        {
            public bool AllowsStyle(string key) => key == "window";
            public bool AllowsAsset(string key) => key == "portrait";
        }

        sealed class Commit : IUiCommitTarget
        {
            public UiSnapshot Last;
            public bool TryCommit(UiSnapshot proposed, out string error)
            { Last = proposed; error = null; return true; }
        }

        static byte[] WindowPacket(ulong handle)
        {
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body))
            {
                writer.Write(handle); writer.Write(0UL); writer.Write((byte)UiNodeKind.Window);
                writer.Write(0); writer.Write(0); writer.Write(460 * 64); writer.Write(260 * 64);
                writer.Write((byte)6); writer.Write(new byte[] { 119, 105, 110, 100, 111, 119 });
                writer.Write((byte)0); writer.Write((ushort)9);
                writer.Write(new byte[] { 73, 110, 118, 101, 110, 116, 111, 114, 121 });
                writer.Write((ushort)UiEventMask.Close);
                var command = body.ToArray();
                using (var packet = new MemoryStream())
                using (var output = new BinaryWriter(packet))
                {
                    output.Write(0x31495550u); output.Write((ushort)1); output.Write((ushort)0);
                    output.Write((ushort)1); output.Write((ushort)0);
                    output.Write((uint)(16 + 4 + command.Length));
                    output.Write((byte)UiOpcode.Create); output.Write((byte)0);
                    output.Write((ushort)command.Length); output.Write(command);
                    return packet.ToArray();
                }
            }
        }

        [TestCase(UiNodeKind.Box)]
        [TestCase(UiNodeKind.Button)]
        [TestCase(UiNodeKind.Text)]
        public void ReconciledTitlesCanChangeAndClearWithoutReplacingNodes(UiNodeKind kind)
        {
            var host = new UiSession(0x1234u, new Theme()); var target = new Commit();
            host.ReserveHandle(out var root);
            Assert.That(host.Submit(WindowPacket(root), target).Success, Is.True);
            host.ReserveHandle(out var handle);
            var create = WindowPacket(handle);
            create[36] = (byte)kind;
            BitConverter.GetBytes(root).CopyTo(create, 28);
            Assert.That(host.Submit(create, target).Success, Is.True);
            foreach (var title in new[] { "Face", "Facial Hair", "" })
            {
                var text = System.Text.Encoding.UTF8.GetBytes(title);
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                writer.Write(0x31495550u); writer.Write((ushort)1); writer.Write((ushort)0);
                writer.Write((ushort)1); writer.Write((ushort)0); writer.Write((uint)(30 + text.Length));
                writer.Write((byte)UiOpcode.SetText); writer.Write((byte)0);
                writer.Write((ushort)(10 + text.Length)); writer.Write(handle);
                writer.Write((ushort)text.Length); writer.Write(text);
                var result = host.Submit(stream.ToArray(), target);
                Assert.That(result.Success, Is.True, result.Detail);
                Assert.That(target.Last.Nodes.Single(node => node.Handle == handle).Text, Is.EqualTo(title));
                Assert.That(host.NodeCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void ButtonStateAcceptsOnlyEnabledOrDisabledValues()
        {
            var host=new UiSession(0x1234u,new Theme());var target=new Commit();
            Assert.That(host.ReserveHandle(out var root).Success,Is.True);
            Assert.That(host.Submit(WindowPacket(root),target).Success,Is.True);
            Assert.That(host.ReserveHandle(out var handle).Success,Is.True);
            var create=WindowPacket(handle);create[36]=(byte)UiNodeKind.Button;
            BitConverter.GetBytes(root).CopyTo(create,28);
            create[create.Length-2]=(byte)UiEventMask.Activate;
            var created=host.Submit(create,target);
            Assert.That(created.Success,Is.True,created.Detail);
            byte[] State(ushort value)
            {
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
                writer.Write(0x31495550u);writer.Write((ushort)1);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)0);writer.Write(30u);
                writer.Write((byte)UiOpcode.SetProgress);writer.Write((byte)0);writer.Write((ushort)10);writer.Write(handle);writer.Write(value);return stream.ToArray();
            }
            Assert.That(host.Submit(State(1),target).Success,Is.True);Assert.That(target.Last.Nodes.Single(node=>node.Kind==UiNodeKind.Button).Progress,Is.EqualTo(1));
            Assert.That(host.Submit(State(2),target).Error,Is.EqualTo(UiError.InvalidCommand));
            Assert.That(host.Submit(State(0),target).Success,Is.True);
        }

        [Test]
        public void InputRecordsKeepSecondaryButtonAndBoundedBoundaryAndLetterKeys()
        {
            Assert.That(UiInputPayload.Pointer(UiPointerPhase.Down, 1, 64, 128, 2)[2],Is.EqualTo(2));
            foreach(var key in new[]{UiInputKey.E,UiInputKey.I,UiInputKey.C,UiInputKey.J,UiInputKey.K}) Assert.That(UiInputPayload.Valid(UiEventMask.Key,UiInputPayload.Key(key,true)),Is.True);
            Assert.That(UiInputPayload.Key(UiInputKey.J,true),Is.EqualTo(new byte[]{1,16,1,0,0}));
            Assert.That(UiInputPayload.Key(UiInputKey.K,true),Is.EqualTo(new byte[]{1,17,1,0,0}));
            Assert.That(UiInputPayload.Valid(UiEventMask.Key,new byte[]{1,18,1,0,0}),Is.False);
            foreach(var kind in new[]{UiEventMask.Hover,UiEventMask.Focus})
            {
                Assert.That(UiInputPayload.Valid(kind,UiInputPayload.Boundary(true)),Is.True);
                Assert.That(UiInputPayload.Valid(kind,UiInputPayload.Boundary(false)),Is.True);
                Assert.That(UiInputPayload.Valid(kind,Array.Empty<byte>()),Is.True);
                Assert.That(UiInputPayload.Valid(kind,new byte[]{1,2}),Is.False);
            }
            Assert.That(UiInputPayload.AllowedEvents,Is.EqualTo(2047));
        }

        [Test]
        public void TextEventsPreserveQueueOnShortBufferAndRetireWithGeneration()
        {
            var host = new UiSession(0x1234u, new Theme()); var target = new Commit();
            host.ReserveHandle(out var handle);
            var packet = WindowPacket(handle);
            packet[packet.Length - 2] = 0; packet[packet.Length - 1] = 4;
            Assert.That(host.Submit(packet, target).Success, Is.True);
            var payload = new UiTextEdit(UiTextEditPhase.Change, new string('x', 512), "", 512, 512, false, true).Encode();
            Assert.That(host.QueueEvent(handle, UiEventMask.TextEdit, payload).Success, Is.True);
            Assert.That(host.WriteNextEvent(new byte[272], out _).Success, Is.False);
            Assert.That(host.PendingEventCount, Is.EqualTo(1));
            var output = new byte[UiHostBridgeContract.TextEventCapacity];
            Assert.That(host.WriteNextEvent(output, out int written).Success, Is.True);
            Assert.That(written, Is.EqualTo(16 + payload.Length));
            Assert.That(host.QueueEvent(handle, UiEventMask.TextEdit, payload).Success, Is.True);
            Assert.That(host.Unload(target).Success, Is.True);
            Assert.That(host.TryDequeueEvent(out _), Is.False);
            Assert.That(host.QueueEvent(handle, UiEventMask.TextEdit, payload).Success, Is.False);
        }

        [Test]
        public void TextInputOptionsAreValidatedTransactionallyAndKeepRevision()
        {
            var host = new UiSession(0x1234u, new Theme()); var target = new Commit();
            host.ReserveHandle(out var root); Assert.That(host.Submit(WindowPacket(root), target).Success, Is.True);
            host.ReserveHandle(out var handle); var create = WindowPacket(handle);
            create[36] = (byte)UiNodeKind.TextInput; BitConverter.GetBytes(root).CopyTo(create,28);
            Assert.That(host.Submit(create,target).Success,Is.True);
            byte[] Options(ushort maxLength, ushort maxBytes, byte flags, uint revision)
            {
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
                writer.Write(0x31495550u);writer.Write((ushort)1);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)0);writer.Write(38u);
                writer.Write((byte)UiOpcode.SetTextInput);writer.Write((byte)0);writer.Write((ushort)18);
                writer.Write(handle);writer.Write(maxLength);writer.Write(maxBytes);writer.Write(flags);writer.Write((byte)0);writer.Write(revision);return stream.ToArray();
            }
            foreach(var packet in new[]{Options(0,512,0,0),Options(8,512,0,0),Options(512,8,0,0),Options(512,512,2,0),Options(512,512,4,0)})
            {
                var before=target.Last;Assert.That(host.Submit(packet,target).Success,Is.False);Assert.That(target.Last,Is.SameAs(before));
            }
            Assert.That(host.Submit(Options(20,20,1,7),target).Success,Is.True);
            var node=target.Last.Nodes.Single(n=>n.Handle==handle);
            Assert.That(node.TextInput.ReadOnly,Is.True);Assert.That(node.TextInput.Revision,Is.EqualTo(7));
            Assert.That(node.With(rect:new UiRect(0,0,6400,6400)).TextInput,Is.SameAs(node.TextInput));
        }

        [Test]
        public void CoreAssemblyHasNoGameOrEngineReferences()
        {
            var names = typeof(UiSession).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name).ToArray();
            Assert.That(names, Has.None.StartsWith("UnityEngine"));
            Assert.That(names, Has.None.StartsWith("Host"));
            Assert.That(names, Has.None.StartsWith("MoonSharp"));
        }

        [Test]
        public void VersionedPacketCreatesOnlyOwnedNativeSnapshot()
        {
            var host = new UiSession(0x1234u, new Theme());
            var target = new Commit();
            Assert.That(host.ReserveHandle(out var owned).Success, Is.True);
            Assert.That(host.Submit(WindowPacket(owned), target).Success, Is.True);
            Assert.That(target.Last.Nodes.Single().Text, Is.EqualTo("Inventory"));
            var foreign = ((ulong)0x4321 << 32) | (owned & 0xffffffffUL);
            Assert.That(host.Submit(WindowPacket(foreign), target).Error, Is.EqualTo(UiError.InvalidHandle));
            Assert.That(host.NodeCount, Is.EqualTo(1));
            Assert.That(host.Unload(target).Success, Is.True);
            Assert.That(host.Submit(WindowPacket(owned), target).Error, Is.EqualTo(UiError.StaleHandle));
        }
    }
}
