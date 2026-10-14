using System;
using PortableUi.Package;
using UnityEngine;

namespace PortableUi.Unity
{
    // Unity adapter for a package-owned decoded image. Never exposes Texture2D to Wasm.
    public sealed class UnityUiImageDecoder : IUiImageDecoder
    {
        public IUiDecodedImage Decode(byte[] encoded, UiPackageImageDescriptor expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!ImageConversion.LoadImage(texture, encoded, true) ||
                    texture.width != expected.Width || texture.height != expected.Height)
                    throw new InvalidOperationException("Image decoder rejected data or changed dimensions.");
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                return new UnityUiDecodedImage(texture, sprite,
                    (long)texture.width * texture.height * 4);
            }
            catch
            {
                if (sprite) UnityEngine.Object.Destroy(sprite);
                if (texture) UnityEngine.Object.Destroy(texture);
                throw;
            }
        }
    }

    public sealed class UnityUiDecodedImage : IUiDecodedImage
    {
        Texture2D texture;
        Sprite sprite;
        readonly bool ownsResources;
        public Sprite Sprite => sprite;
        public int Width => texture ? texture.width : 0;
        public int Height => texture ? texture.height : 0;
        public long ResidentBytes { get; }

        internal UnityUiDecodedImage(Texture2D ownedTexture, Sprite ownedSprite, long residentBytes, bool ownsResources = true)
        { texture = ownedTexture; sprite = ownedSprite; ResidentBytes = residentBytes; this.ownsResources = ownsResources; }

        // The caller retains resource ownership and must keep both assets alive for
        // this wrapper's lifetime. Disposing the wrapper never destroys either asset.
        public static UnityUiDecodedImage Borrow(Sprite sprite, UiPackageImageDescriptor expected)
        {
            if (!sprite || !sprite.texture) throw new ArgumentException("A live sprite and texture are required.", nameof(sprite));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            var texture = sprite.texture;
            if (texture.width != expected.Width || texture.height != expected.Height)
                throw new InvalidOperationException("Borrowed image dimensions differ from package source.");
            return new UnityUiDecodedImage(texture, sprite, expected.DecodedBytes, false);
        }

        public void Dispose()
        {
            if (sprite && ownsResources) UnityEngine.Object.Destroy(sprite);
            if (texture && ownsResources) UnityEngine.Object.Destroy(texture);
            sprite = null; texture = null;
        }
    }
}
