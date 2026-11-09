using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FloorF.UI
{
    /// <summary>Pointer enter / exit -> callbacks (shop cards show their full description while hovered).</summary>
    sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Enter, Exit;

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }
}
