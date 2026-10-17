using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Capsule.Unity
{
    public sealed class CapsuleNodeInput : MonoBehaviour, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, ISelectHandler, IDeselectHandler, ISubmitHandler, ICancelHandler, IScrollHandler
    {
        CapsuleNativeTarget target;
        ulong handle;
        UiEventMask mask;
        bool captured;
        int pointerId;
        Vector2 lastPoint;
        byte button;
        public void Bind(CapsuleNativeTarget owner, ulong id, UiEventMask events)
        {
            // A new snapshot may already have removed the old subscription or
            // handle from the guest. Retire local capture without stale input.
            if (captured && (target != owner || handle != id || (events & UiEventMask.Pointer) == 0)) captured = false;
            target = owner; handle = id; mask = events;
            enabled = events != UiEventMask.None;
        }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left &&
                (mask & UiEventMask.Activate) != 0) target?.Queue(handle, UiEventMask.Activate);
        }
        public void OnScroll(PointerEventData data)
        {
            if (!float.IsFinite(data.scrollDelta.x) || !float.IsFinite(data.scrollDelta.y)) return;
            target?.QueueWheelPath(handle, UiInputPayload.Wheel(
                Mathf.RoundToInt(Mathf.Clamp(data.scrollDelta.x * 64, -8192, 8192) * 64),
                Mathf.RoundToInt(Mathf.Clamp(-data.scrollDelta.y * 64, -8192, 8192) * 64), Modifiers()));
        }
        public void OnPointerEnter(PointerEventData data)
        { if ((mask & UiEventMask.Hover) != 0) target?.Queue(handle, UiEventMask.Hover); }
        public void OnPointerExit(PointerEventData data)
        { if ((mask & UiEventMask.Hover) != 0) target?.Queue(handle, UiEventMask.Hover, new byte[] { UiInputPayload.Version, 0 }); }
        public void OnPointerDown(PointerEventData data)
        {
            target?.QueueFocusPath(handle);
            if ((mask & UiEventMask.Pointer) == 0 || captured || !Point(data)) return;
            pointerId = data.pointerId; button = data.button==PointerEventData.InputButton.Right?(byte)2:data.button==PointerEventData.InputButton.Middle?(byte)1:(byte)0;
            captured = Emit(UiPointerPhase.Down);
        }
        public void OnSelect(BaseEventData data)
        { if ((mask & UiEventMask.Focus) != 0) target?.Queue(handle, UiEventMask.Focus); }
        public void OnDeselect(BaseEventData data)
        { if ((mask & UiEventMask.Focus) != 0) target?.Queue(handle, UiEventMask.Focus, new byte[] { UiInputPayload.Version, 0 }); }
        public void OnSubmit(BaseEventData data)
        { if ((mask & UiEventMask.Activate) != 0) target?.Queue(handle, UiEventMask.Activate); }
        public void OnBeginDrag(PointerEventData data) { }
        public void OnDrag(PointerEventData data)
        {
            if (!captured || data.pointerId != pointerId || !Point(data)) return;
            if (!Emit(UiPointerPhase.Move)) CancelCapture();
        }
        public void OnPointerUp(PointerEventData data) => Release(data);
        public void OnEndDrag(PointerEventData data) => Release(data);
        void Release(PointerEventData data)
        {
            if (!captured || data.pointerId != pointerId) return;
            Point(data); Emit(UiPointerPhase.Up); captured = false;
        }
        bool Point(PointerEventData data)
        {
            if (target == null || !target.TryDesignPoint(data.position, data.pressEventCamera, out var point)) return false;
            lastPoint = point; return true;
        }
        static byte Modifiers()
        {
            var keys = Keyboard.current;
            if (keys == null) return 0;
            return (byte)((keys.shiftKey.isPressed ? 1 : 0) | (keys.ctrlKey.isPressed ? 2 : 0) |
                (keys.altKey.isPressed ? 4 : 0) | (keys.leftMetaKey.isPressed || keys.rightMetaKey.isPressed ? 8 : 0));
        }
        bool Emit(UiPointerPhase phase) => target != null && target.Queue(handle, UiEventMask.Pointer,
            UiInputPayload.Pointer(phase, pointerId,
                Mathf.RoundToInt(Mathf.Clamp(lastPoint.x, -8192, 8192) * 64),
                Mathf.RoundToInt(Mathf.Clamp(lastPoint.y, -8192, 8192) * 64), button, Modifiers()));
        void CancelCapture()
        {
            if (!captured) return;
            captured = false; Emit(UiPointerPhase.Cancel);
        }
        public void OnCancel(BaseEventData data)=>CancelCapture();
        void OnDisable() { captured=false; }
        void OnApplicationFocus(bool focus) { if(!focus)CancelCapture(); }
        void OnApplicationPause(bool paused) { if(paused)CancelCapture(); }
    }
}
