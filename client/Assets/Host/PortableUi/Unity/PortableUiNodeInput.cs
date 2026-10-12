using UnityEngine;
using UnityEngine.EventSystems;

namespace PortableUi.Unity
{
    public sealed class PortableUiNodeInput : MonoBehaviour, IPointerClickHandler,
        IPointerEnterHandler, IPointerDownHandler
    {
        PortableUiNativeTarget target;
        ulong handle;
        UiEventMask mask;
        public void Bind(PortableUiNativeTarget owner, ulong id, UiEventMask events)
        { target = owner; handle = id; mask = events; }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left &&
                (mask & UiEventMask.Activate) != 0) target?.Queue(handle, UiEventMask.Activate);
        }
        public void OnPointerEnter(PointerEventData data)
        { if ((mask & UiEventMask.Hover) != 0) target?.Queue(handle, UiEventMask.Hover); }
        public void OnPointerDown(PointerEventData data)
        { if ((mask & UiEventMask.Focus) != 0) target?.Queue(handle, UiEventMask.Focus); }
    }
}
