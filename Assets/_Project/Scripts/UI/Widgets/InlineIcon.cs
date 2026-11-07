using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// A destination icon inside a TMP sentence ("Press floor 5 ([icon]Sauna)") without a TMP sprite asset (no extra
    /// shader in the WebGL build): <see cref="Markup"/> reserves a gap with a linked space + &lt;space=1em&gt;, and
    /// <see cref="Place"/> lays a uGUI Image over that gap from TMP's textInfo every tick. No sprite -> plain coloured
    /// name, no gap (text fallback).
    /// </summary>
    sealed class InlineIcon
    {
        public const string LinkId = "dest-icon";

        readonly TextMeshProUGUI text;
        readonly Image icon;

        public InlineIcon(TextMeshProUGUI text)
        {
            this.text = text;
            icon = UIBuild.Box("InlineIcon", text.transform, Color.white);
            icon.preserveAspect = true;
            var rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = text.rectTransform.pivot; // TMP geometry is relative to the pivot
            rt.pivot = new Vector2(0.5f, 0.5f);
            icon.gameObject.SetActive(false);
        }

        public bool Visible => icon.gameObject.activeSelf;
        public Sprite Sprite => icon.sprite;
        public Color Color => icon.color;

        /// <summary>
        /// Rich text for destination <paramref name="id"/>: gap + name in the destination colour (no line break inside).
        /// <paramref name="sprite"/> is null when there is no icon (then the markup is just the coloured name).
        /// </summary>
        public static string Markup(UIContext ctx, DestinationId id, out Sprite sprite, out Color color)
        {
            color = UIStyle.Destination(ctx.DestinationIndex(id));
            sprite = UIIcons.For(ctx.Destination(id));
            string name = "<color=" + UIIcons.Hex(UIIcons.NameInk(color)) + ">" + ctx.DestinationName(id) + "</color>";
            return sprite == null ? name : "<nobr><link=\"" + LinkId + "\"> </link><space=1em>" + name + "</nobr>";
        }

        /// <summary>Which sprite/tint the next <see cref="Place"/> draws (null = none).</summary>
        public void Set(Sprite sprite, Color color)
        {
            icon.sprite = sprite;
            icon.color = color;
        }

        /// <summary>Puts the icon over the link gap of the text as last laid out; hides it when there is none.</summary>
        public void Place()
        {
            Vector2 center = default;
            float size = 0f;
            bool show = icon.sprite != null && text.gameObject.activeInHierarchy && Find(out center, out size);
            if (icon.gameObject.activeSelf != show) icon.gameObject.SetActive(show);
            if (!show) return;
            icon.rectTransform.anchoredPosition = center;
            icon.rectTransform.sizeDelta = new Vector2(size, size);
        }

        bool Find(out Vector2 center, out float size)
        {
            center = default;
            size = 0f;
            var ti = text.textInfo;
            if (ti == null || ti.linkCount == 0) return false;
            for (int l = 0; l < ti.linkCount && l < ti.linkInfo.Length; l++)
            {
                var link = ti.linkInfo[l];
                if (link.GetLinkID() != LinkId) continue;
                int i = link.linkTextfirstCharacterIndex;
                if (i < 0 || i >= ti.characterCount) return false;
                var ci = ti.characterInfo[i];
                float x0 = ci.origin, x1 = ci.xAdvance + text.fontSize, cy = ci.baseLine + text.fontSize * 0.35f;
                if (i + 1 < ti.characterCount && ti.characterInfo[i + 1].lineNumber == ci.lineNumber)
                {
                    var next = ti.characterInfo[i + 1];
                    x1 = next.origin;
                    if (next.isVisible) cy = (next.topLeft.y + next.bottomLeft.y) * 0.5f;
                }
                size = Mathf.Min(text.fontSize, Mathf.Max(0f, x1 - x0));
                center = new Vector2((x0 + x1) * 0.5f, cy);
                return size > 2f;
            }
            return false;
        }
    }
}
