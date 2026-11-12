using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Tactile press on every <see cref="UIBuild.Button"/>: while the left button is held on it, its label sits
    /// <see cref="Depth"/> canvas units lower (on top of the colour tint), and pops back on release / leaving / disable.
    /// Only the label moves - the background (the raycast target) stays, so the hover never flickers.
    /// </summary>
    sealed class PressSink : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public const float Depth = 2f;
        public RectTransform Target;
        public Selectable Owner;
        bool down;
        Vector2 baseMin, baseMax;

        public bool Down => down;

        public void OnPointerDown(PointerEventData e)
        {
            if (down || Target == null || e.button != PointerEventData.InputButton.Left) return;
            if (Owner != null && !Owner.IsInteractable()) return;
            down = true;
            baseMin = Target.offsetMin;
            baseMax = Target.offsetMax;
            var d = new Vector2(0f, -Depth);
            Target.offsetMin = baseMin + d;
            Target.offsetMax = baseMax + d;
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) => Release();
        void OnDisable() => Release();

        void Release()
        {
            if (!down) return;
            down = false;
            if (Target == null) return;
            Target.offsetMin = baseMin;
            Target.offsetMax = baseMax;
        }
    }
}
