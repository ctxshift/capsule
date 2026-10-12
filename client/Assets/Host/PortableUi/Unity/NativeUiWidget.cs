using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    public readonly struct NativeUiWidget
    {
        public readonly RectTransform Rect;
        public readonly Image Image;
        public readonly Image Fill;
        public readonly TMP_Text Label;
        public readonly Button CloseButton;
        public NativeUiWidget(RectTransform rect, Image image, Image fill,
            TMP_Text label, Button closeButton = null)
        { Rect = rect; Image = image; Fill = fill; Label = label; CloseButton = closeButton; }
    }

    // Optional game theme adapter; generic layout never hard-codes a game's styles.
    public interface INativeUiStyleAdapter
    {
        void ApplyStyle(NativeUiWidget widget, UiNode node);
    }

    // Host supplies prefab instances and exact allowlisted artwork.
    public interface INativeUiWidgetFactory
    {
        NativeUiWidget Create(UiNodeKind kind, Transform parent);
        bool TryGetSprite(string assetKey, out Sprite sprite);
    }
}
