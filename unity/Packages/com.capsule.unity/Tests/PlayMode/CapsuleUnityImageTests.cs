using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Capsule.Package;
using Capsule.Unity;
using UnityEngine;
using UnityEngine.TestTools;

namespace Capsule.Tests
{
    public sealed class CapsuleUnityImageTests
    {
        static byte[] Png(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try { return texture.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        static UiPackageImageDescriptor Descriptor(byte[] bytes)
        {
            Assert.That(UiPackageImageDescriptor.TryInspect(bytes, "fixture.png", out var descriptor), Is.True);
            return descriptor;
        }

        [UnityTest]
        public IEnumerator DecodedResourcesAreOwnedAndDisposedOnce()
        {
            byte[] bytes = Png(2, 3);
            var image = (UnityUiDecodedImage)new UnityUiImageDecoder().Decode(bytes, Descriptor(bytes));
            var sprite = image.Sprite;
            var texture = sprite.texture;
            Assert.That(image.Width, Is.EqualTo(2));
            Assert.That(image.Height, Is.EqualTo(3));
            Assert.That(image.ResidentBytes, Is.EqualTo(24));
            image.Dispose(); image.Dispose();
            Assert.That(image.Sprite, Is.Null);
            yield return null;
            Assert.That(sprite == null && texture == null, Is.True, "Owned resources retire after Unity's destruction boundary.");
        }

        [Test]
        public void BorrowedResourcesOutliveIndependentWrappersAndRejectMismatch()
        {
            var texture = new Texture2D(2, 3, TextureFormat.RGBA32, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 3), Vector2.zero);
            try
            {
                var expected = Descriptor(Png(2, 3));
                using var second = UnityUiDecodedImage.Borrow(sprite, expected);
                var first = UnityUiDecodedImage.Borrow(sprite, expected);
                first.Dispose(); first.Dispose();
                Assert.That(sprite && texture, Is.True);
                Assert.That(second.Sprite, Is.SameAs(sprite));
                Assert.That(second.ResidentBytes, Is.EqualTo(24));
                Assert.Throws<InvalidOperationException>(() => UnityUiDecodedImage.Borrow(sprite, Descriptor(Png(1, 1))));
                Assert.Throws<ArgumentNullException>(() => UnityUiDecodedImage.Borrow(sprite, null));
                Assert.Throws<ArgumentException>(() => UnityUiDecodedImage.Borrow(null, expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [UnityTest]
        public IEnumerator FailedDecodeRetiresItsCandidateTexture()
        {
            byte[] bytes = Png(2, 3);
            var wrong = Descriptor(Png(1, 1));
            var before = Resources.FindObjectsOfTypeAll<Texture2D>().Select(value => value.GetEntityId()).ToHashSet();
            Assert.Throws<InvalidOperationException>(() => new UnityUiImageDecoder().Decode(bytes, wrong));
            yield return null;
            var leaked = Resources.FindObjectsOfTypeAll<Texture2D>().Where(value => !before.Contains(value.GetEntityId()) && value.width == 2 && value.height == 3).ToArray();
            Assert.That(leaked, Is.Empty, "Rejected decoded candidates must release their textures.");
        }
    }
}
