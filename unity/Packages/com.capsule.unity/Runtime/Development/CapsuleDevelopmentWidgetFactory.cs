using System;
using System.Collections.Generic;
using Capsule.Package;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Capsule.Unity.Development
{
    // One factory and its decoded art belong to one admitted revision.
    internal sealed class CapsuleDevelopmentWidgetFactory : INativeUiWidgetFactory, INativeUiStyleAdapter, INativeUiScopeLifetime
    {
        readonly UiControlTheme theme;
        readonly TMP_FontAsset font;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        readonly List<Texture2D> textures = new List<Texture2D>();
        long decodedBytes;
        bool disposed;

        internal CapsuleDevelopmentWidgetFactory(UiControlTheme theme, TMP_FontAsset font)
        { this.theme = theme; this.font = font ? font : throw new ArgumentNullException(nameof(font)); }

        internal void AddImage(UiControlTheme.AssetRecipe recipe, byte[] encoded)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CapsuleDevelopmentWidgetFactory));
            long bytes = (long)recipe.Width * recipe.Height * 4;
            if (recipe.Width < 1 || recipe.Height < 1 || bytes > 16777216 || decodedBytes + bytes > 33554432)
                throw new InvalidOperationException("Development texture quota exceeded.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, encoded, true) || texture.width != recipe.Width || texture.height != recipe.Height ||
                    recipe.X < 0 || recipe.Y < 0 || recipe.RectWidth < 1 || recipe.RectHeight < 1 ||
                    recipe.X + recipe.RectWidth > recipe.Width || recipe.Y + recipe.RectHeight > recipe.Height ||
                    recipe.Border < 0 || 2 * recipe.Border > Math.Min(recipe.RectWidth, recipe.RectHeight))
                    throw new InvalidOperationException("Theme image dimensions or region invalid.");
                var rect = new Rect(recipe.X, recipe.Height - recipe.Y - recipe.RectHeight, recipe.RectWidth, recipe.RectHeight);
                var border = new Vector4(recipe.Border, recipe.Border, recipe.Border, recipe.Border);
                var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border);
                sprites.Add(recipe.Key, sprite); textures.Add(texture); decodedBytes += bytes;
            }
            catch { Release(texture); throw; }
        }

        public bool TryGetSprite(string assetKey, out Sprite sprite) => sprites.TryGetValue(assetKey, out sprite) && sprite;

        public NativeUiWidget Create(UiNodeKind kind, Transform parent)
        {
            if (kind == UiNodeKind.Text)
            {
                var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                obj.transform.SetParent(parent, false);
                var text = obj.GetComponent<TextMeshProUGUI>(); text.font = font; text.richText = false; text.raycastTarget = false;
                return new NativeUiWidget((RectTransform)obj.transform, null, null, text);
            }
            if (kind == UiNodeKind.Image)
            {
                var obj = new GameObject("Image", typeof(RectTransform), typeof(Image));
                obj.transform.SetParent(parent, false);
                return new NativeUiWidget((RectTransform)obj.transform, obj.GetComponent<Image>(), null, null);
            }
            throw new NotSupportedException("Development host does not render node kind " + kind + ".");
        }

        public void ApplyStyle(NativeUiWidget widget, UiNode node)
        {
            if (node.Kind == UiNodeKind.Box || node.Kind == UiNodeKind.ClipBox || node.Kind == UiNodeKind.Button)
            {
                if (theme.TrySurfaceColor(node.StyleKey, out var surface)) widget.Image.color = Rgba(surface);
                else if (node.Kind == UiNodeKind.Button) widget.Image.color = Rgba(theme.Color("button", "background"));
                else widget.Image.color = Color.clear;
            }
            else if (node.Kind == UiNodeKind.Text)
            {
                string key = string.IsNullOrEmpty(node.StyleKey) ? theme.String("components", "buttonLabel") : node.StyleKey;
                var label = widget.Label;
                label.fontSize = theme.TextNumber(key, "size");
                label.color = Rgba(theme.TextColor(key));
                label.fontStyle = (theme.TextBold(key) ? FontStyles.Bold : FontStyles.Normal) |
                    (theme.TextItalic(key) ? FontStyles.Italic : FontStyles.Normal);
                label.textWrappingMode = theme.TextWrap(key) ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                label.alignment = TextAlignmentOptions.TopLeft;
            }
            else if (node.Kind == UiNodeKind.Image) widget.Image.color = Color.white;
            else throw new NotSupportedException("Development host does not render node kind " + node.Kind + ".");
        }

        static Color32 Rgba(byte[] c) => new Color32(c[0], c[1], c[2], c[3]);
        static void Release(UnityEngine.Object value)
        { if (!value) return; if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var sprite in sprites.Values) Release(sprite);
            foreach (var texture in textures) Release(texture);
            sprites.Clear(); textures.Clear();
        }
    }
}
