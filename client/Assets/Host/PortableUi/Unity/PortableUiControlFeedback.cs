using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PortableUi.Unity
{
    // Reusable native feedback; theme supplies the graphics and palette.
    public sealed class PortableUiControlFeedback : MonoBehaviour, IPointerEnterHandler,
        IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public static bool KeyboardFocus { get; set; }
        Image tint, texture;
        GameObject focus;
        Color hover;
        Color normalLabel=Color.white;
        bool over, down, selected;
        public void Configure(Image highlight, Image detail, GameObject focusOutline, Color hoverColor, Color? labelColor=null)
        { tint = highlight; texture = detail; focus = focusOutline; hover = hoverColor; normalLabel=labelColor??Color.white;Paint(); }
        public void OnPointerEnter(PointerEventData data) { over=true; Paint(); }
        public void OnPointerExit(PointerEventData data) { over=false; Paint(); }
        public void OnPointerDown(PointerEventData data) { KeyboardFocus=false;if(data.button==PointerEventData.InputButton.Left)down=true; Paint(); }
        public void OnPointerUp(PointerEventData data) { down=false; Paint(); }
        public void OnSelect(BaseEventData data) { selected=true; Paint(); }
        public void OnDeselect(BaseEventData data) { selected=false; Paint(); }
        void Paint()
        {
            if(tint)tint.color = over ? hover : Color.clear;
            if(texture)texture.color = new Color(1,1,1,over?.12f:0);
            if(focus)focus.SetActive(selected&&KeyboardFocus);
            foreach(var label in GetComponentsInChildren<TMP_Text>())label.color = down ? Color.white : normalLabel;
        }
        void OnDisable() { over=down=selected=false; Paint(); }
    }
}
