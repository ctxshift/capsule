using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PortableUi.Unity
{
    // Main-thread edge sampling does not schedule guest work while idle.
    [DefaultExecutionOrder(-100)]
    public sealed class PortableUiKeyboardInput : MonoBehaviour
    {
        PortableUiNativeTarget target;
        public void Bind(PortableUiNativeTarget owner) => target = owner;
        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || target == null) return;
            byte modifiers = (byte)((keyboard.shiftKey.isPressed ? 1 : 0) |
                (keyboard.ctrlKey.isPressed ? 2 : 0) | (keyboard.altKey.isPressed ? 4 : 0) |
                (keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed ? 8 : 0));
            if(!target.ExternalEscapeRouting)Send(keyboard.escapeKey, UiInputKey.Escape, modifiers);
            Send(keyboard.tabKey, UiInputKey.Tab, modifiers);
            Send(keyboard.enterKey, UiInputKey.Enter, modifiers);
            Send(keyboard.spaceKey, UiInputKey.Space, modifiers);
            Send(keyboard.leftArrowKey, UiInputKey.Left, modifiers);
            Send(keyboard.rightArrowKey, UiInputKey.Right, modifiers);
            Send(keyboard.upArrowKey, UiInputKey.Up, modifiers);
            Send(keyboard.downArrowKey, UiInputKey.Down, modifiers);
            Send(keyboard.homeKey, UiInputKey.Home, modifiers);
            Send(keyboard.endKey, UiInputKey.End, modifiers);
            Send(keyboard.pageUpKey, UiInputKey.PageUp, modifiers);
            Send(keyboard.pageDownKey, UiInputKey.PageDown, modifiers);
        }
        void Send(KeyControl control, UiInputKey key, byte modifiers)
        { if (control.wasPressedThisFrame) target.RouteKey(key, modifiers); }
    }
}
