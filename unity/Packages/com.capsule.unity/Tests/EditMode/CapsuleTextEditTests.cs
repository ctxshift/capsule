using System;
using System.IO;
using NUnit.Framework;
using Capsule;
using UnityEngine;

namespace Capsule.Tests
{
    public sealed class CapsuleTextEditTests
    {
        [Serializable] sealed class Vectors { public Vector[] vectors; }
        [Serializable] sealed class Vector
        {
            public string phase, text, composition, hex;
            public ushort anchor, focus;
            public bool composing, userInput;
        }
        static byte[] Hex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
        [Test] public void SharedVectorsPreserveUnicodeSelectionAndEditOrigin()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.capsule.unity/Tests/Fixtures/text-edit-vectors.json"));
            var vectors = JsonUtility.FromJson<Vectors>(File.ReadAllText(path));
            Assert.That(vectors.vectors.Length, Is.EqualTo(4));
            foreach (var vector in vectors.vectors)
            {
                var phase = (UiTextEditPhase)Enum.Parse(typeof(UiTextEditPhase), vector.phase, true);
                var edit = new UiTextEdit(phase, vector.text, vector.composition, vector.anchor, vector.focus, vector.composing, vector.userInput);
                Assert.That(edit.Encode(), Is.EqualTo(Hex(vector.hex)));
                Assert.That(UiTextEdit.TryDecode(Hex(vector.hex), out var decoded), Is.True);
                Assert.That(decoded.Text, Is.EqualTo(vector.text));
                Assert.That(decoded.Composition, Is.EqualTo(vector.composition));
                Assert.That(decoded.Anchor, Is.EqualTo(vector.anchor));
                Assert.That(decoded.Focus, Is.EqualTo(vector.focus));
                Assert.That(decoded.UserInput, Is.EqualTo(vector.userInput));
            }
        }
        [Test] public void RejectsMalformedRecordsAndCompositionSubmission()
        {
            var bytes = Hex("01020300010003000700030041f09f9880c3a9e697a5");
            foreach (var pair in new[] { (0, 2), (1, 7), (2, 4), (3, 1), (4, 2), (8, 255), (13, 255) })
            {
                var bad = (byte[])bytes.Clone(); bad[pair.Item1] = (byte)pair.Item2;
                Assert.That(UiTextEdit.TryDecode(bad, out _), Is.False);
            }
            foreach (var phase in new[] { UiTextEditPhase.Submit, UiTextEditPhase.Cancel, UiTextEditPhase.Blur })
                Assert.Throws<ArgumentException>(() => new UiTextEdit(phase, "", "x", 0, 0, true, true).Encode());
            Assert.Throws<System.Text.EncoderFallbackException>(() => new UiTextEdit(UiTextEditPhase.Change, "\ud800", "", 0, 0, false, true).Encode());
        }
        [Test] public void TextQuotaDoesNotExpandOpaqueActionOrInputPayloads()
        {
            var text = new string('x', 512);
            var bytes = new UiTextEdit(UiTextEditPhase.Change, text, "", 512, 512, false, true).Encode();
            Assert.That(UiInputPayload.Valid(UiEventMask.TextEdit, bytes), Is.True);
            Assert.That(UiInputPayload.Valid(UiEventMask.Activate, bytes), Is.False);
            Assert.That(UiSession.MaxEventPayloadBytes, Is.EqualTo(256));
            Assert.Throws<ArgumentException>(() => new UiTextEdit(UiTextEditPhase.Change, text + "x", "", 0, 0, false, true).Encode());
        }
    }
}
