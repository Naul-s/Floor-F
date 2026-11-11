using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// "HIDDEN ENDING" plate for the 4th-floor ending (ending card ribbon, collection slot): violet ink and edge on a
    /// near-black violet plate, outside any layout. <see cref="Tick"/> breathes it like a dying bulb (slow, 0.7..1 alpha,
    /// with a brief dip now and then - never a flash).
    /// </summary>
    sealed class HiddenBadge
    {
        public static readonly Color Ink = UIStyle.Hex(UIFormat.HiddenHex);
        public static readonly Color Plate = new Color(0.05f, 0.025f, 0.08f, 0.98f);
        public readonly RectTransform Root;
        public readonly TextMeshProUGUI Label;
        readonly CanvasGroup group;
        readonly float padX, height;

        readonly Image edge, fill;

        /// <param name="ink">Edge + text colour (default violet <see cref="Ink"/>); the winter ribbon is ice blue.</param>
        public HiddenBadge(string name, Transform parent, float fontSize, float padX, float height, Color? ink = null, Color? plate = null)
        {
            this.padX = padX;
            this.height = height;
            edge = UIBuild.Framed(name, parent, plate ?? Plate, ink ?? Ink, 2f);
            fill = edge.transform.GetChild(0).GetComponent<Image>();
            Root = edge.rectTransform;
            Root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            group = Root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Label = UIBuild.Text("Text", Root, "", fontSize, ink ?? Ink, TextAlignmentOptions.Center);
            UIBuild.Stretch(Label.rectTransform);
            Label.textWrappingMode = TextWrappingModes.NoWrap;
            Label.characterSpacing = 100f / UIFonts.Grid; // one font pixel at 12px: stays on the grid
        }

        /// <summary>Sets the text and sizes the plate around it (keeps anchors / pivot / position).</summary>
        public void SetText(string text)
        {
            UIBuild.Set(Label, text);
            float w = Label.GetPreferredValues(Label.text, float.PositiveInfinity, float.PositiveInfinity).x;
            Root.sizeDelta = new Vector2(Mathf.Ceil(w) + 2f * padX, height);
        }

        /// <summary>Recolours edge, text and plate (one ribbon serves several card looks).</summary>
        public void SetColors(Color ink, Color plate)
        {
            edge.color = ink;
            Label.color = ink;
            fill.color = plate;
        }

        public bool Visible => Root.gameObject.activeInHierarchy;
        public void Show(bool on) { if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on); }
        public float Alpha { get => group.alpha; set => group.alpha = value; }

        /// <summary>Slow breathing + a short sag every ~4.7 s (unscaled time).</summary>
        public void Tick(float t)
        {
            if (!Visible) return;
            float breathe = 0.85f + 0.15f * Mathf.Sin(t * 1.3f);
            float cycle = Mathf.Repeat(t, 4.7f);
            float sag = cycle < 0.35f ? 1f - 0.25f * Mathf.Sin(cycle / 0.35f * Mathf.PI) : 1f;
            group.alpha = Mathf.Clamp(breathe * sag, 0.6f, 1f);
        }

        public bool TextFits => Label.GetPreferredValues(Label.text, float.PositiveInfinity, float.PositiveInfinity).x <= Label.rectTransform.rect.width + 0.5f;
    }
}
