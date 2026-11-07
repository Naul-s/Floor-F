using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Destination tag (panel floor buttons, guest chips): [icon tinted with the destination colour] + short name on a
    /// dark fill sunk in that colour. Without a sprite it falls back to the Phase-2 text badge (destination-colour fill,
    /// black/white name). The caller places <see cref="Rect"/>.
    /// <para>Name rule: <see cref="NameMode.Truncate"/> keeps the name, shortened until it fits ("Resta.");
    /// <see cref="NameMode.FullOrNone"/> (chips) shows the name only when it fits WHOLE beside the icon, else the icon
    /// alone, centred - a clipped "Resta." adds noise next to an icon that already says it. The text fallback always
    /// shows a (shortened) name.</para>
    /// </summary>
    sealed class DestinationBadge
    {
        public enum NameMode { Truncate, FullOrNone }

        const float Pad = 2f, IconGap = 3f;

        public readonly Image Back;
        public readonly Image Icon;
        public readonly TextMeshProUGUI Name;
        readonly float fontSize;
        readonly NameMode mode;

        public RectTransform Rect => Back.rectTransform;
        public bool ShowsIcon => Icon.gameObject.activeSelf;
        public bool ShowsName => Name.gameObject.activeSelf && !string.IsNullOrEmpty(Name.text);
        public string Text => ShowsName ? Name.text : "";

        public DestinationBadge(Transform parent, float fontSize, NameMode mode)
        {
            this.fontSize = fontSize;
            this.mode = mode;
            Back = UIBuild.Box("Badge", parent, Color.gray);
            Icon = UIBuild.Box("Icon", Back.transform, Color.white);
            Icon.preserveAspect = true;
            var irt = Icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0f, 0.5f);
            Name = UIBuild.Text("Dest", Back.transform, "", fontSize, UIStyle.Dark, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(Name.rectTransform);
            Name.textWrappingMode = TextWrappingModes.NoWrap;
            Name.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>Shows <paramref name="id"/> (call after the rect has its final size).</summary>
        public void Set(UIContext ctx, DestinationId id)
        {
            var color = UIStyle.Destination(ctx.DestinationIndex(id));
            string name = ctx.DestinationName(id);
            var sprite = UIIcons.For(ctx.Destination(id));
            var nrt = Name.rectTransform;
            if (sprite == null)
            {
                Icon.gameObject.SetActive(false);
                Back.color = color;
                Name.gameObject.SetActive(true);
                nrt.offsetMin = new Vector2(Pad, 0f);
                nrt.offsetMax = new Vector2(-Pad, 0f);
                Name.alignment = TextAlignmentOptions.Center;
                Name.color = UIStyle.ContrastInk(color);
                UIBuild.FitName(Name, name, fontSize);
                return;
            }

            Back.color = UIIcons.BadgeFill(color);
            Icon.sprite = sprite;
            Icon.color = color;
            Icon.gameObject.SetActive(true);
            float h = Rect.rect.height;
            float icon = Mathf.Max(8f, h - 2f * Pad);
            var irt = Icon.rectTransform;
            irt.sizeDelta = new Vector2(icon, -2f * Pad);
            Name.color = UIIcons.NameInk(color);
            Name.alignment = TextAlignmentOptions.Center;
            nrt.offsetMin = new Vector2(Pad + icon + IconGap, 0f);
            nrt.offsetMax = new Vector2(-Pad, 0f);

            bool showName = true;
            if (mode == NameMode.FullOrNone)
            {
                Name.fontSize = UIFonts.Px(fontSize);
                if (Name.font != null) Name.font.TryAddCharacters(name);
                showName = Name.GetPreferredValues(name, float.PositiveInfinity, float.PositiveInfinity).x <= nrt.rect.width;
                if (showName) UIBuild.Set(Name, name.Trim());
            }
            else UIBuild.FitName(Name, name, fontSize);

            Name.gameObject.SetActive(showName);
            // icon alone: centred in the badge; with a name: left, name fills the rest
            float x = showName ? Pad : (Rect.rect.width - icon) * 0.5f;
            irt.anchoredPosition = new Vector2(x, 0f);
        }
    }
}
