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
    /// <para>Name rule: the full name when it fits, else the curated short name (UIFormat.DestinationShortName:
    /// "Guest Rooms" -> "Rooms", "Restaurant" -> "Dining"); truncation ("Obser.") only for ids without one.
    /// <see cref="NameMode.Truncate"/> (panel) always shows the icon AND names the venue - a name too wide beside the
    /// icon at the badge size drops to the next 12px-grid size ("Karaoke" 24 -> 12) before anything is cut;
    /// <see cref="NameMode.FullOrNone"/> (chips) shows the name only when it fits WHOLE beside the icon, else the icon
    /// alone, centred - a clipped "Resta." adds noise next to an icon that already says it. The text fallback always
    /// shows a (shortened) name.</para>
    /// </summary>
    sealed class DestinationBadge
    {
        public enum NameMode { Truncate, FullOrNone }

        const float Pad = 2f, IconGap = 1f;

        public readonly Image Back;
        public readonly Image Icon;
        public readonly TextMeshProUGUI Name;
        readonly float fontSize;
        readonly NameMode mode;

        public RectTransform Rect => Back.rectTransform;
        public bool ShowsIcon => Icon.gameObject.activeSelf;
        public bool ShowsName => Name.gameObject.activeSelf && !string.IsNullOrEmpty(Name.text);
        public string Text => ShowsName ? Name.text : "";

        string lastName, lastShort;

        /// <summary>Test hook: the name is below the badge size only because neither the full nor the curated name fits
        /// beside the icon at that size.</summary>
        public bool ShrunkOnlyWhenNeeded()
        {
            float full = UIFonts.Px(fontSize);
            if (!ShowsName || Name.fontSize >= full) return true;
            float was = Name.fontSize;
            Name.fontSize = full;
            bool fits = UIBuild.FirstThatFits(Name, Name.rectTransform.rect.width, lastName, lastShort) != null;
            Name.fontSize = was;
            return !fits;
        }

        /// <summary>Test hook: null when the shown name fits its rect on one line and sits clear of the icon (and both
        /// inside the badge), else what is wrong.</summary>
        public string LayoutProblem()
        {
            if (!ShowsName) return null;
            var nr = Name.rectTransform.rect;
            float need = Name.GetPreferredValues(Name.text, float.PositiveInfinity, float.PositiveInfinity).x;
            if (need > nr.width + 0.5f) return "name '" + Name.text + "' " + need + "px in " + nr.width + "px at " + Name.fontSize;
            if (Name.rectTransform.offsetMin.x < 0f || Name.rectTransform.offsetMax.x > 0f) return "name outside the badge";
            if (ShowsIcon)
            {
                var ir = Icon.rectTransform;
                float iconRight = ir.anchoredPosition.x + ir.rect.width;
                if (Name.rectTransform.offsetMin.x < iconRight - 0.5f) return "name over the icon (" + Name.rectTransform.offsetMin.x + " < " + iconRight + ")";
                if (iconRight > Rect.rect.width + 0.5f) return "icon outside the badge";
            }
            return null;
        }

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
            string shortName = ctx.DestinationShortName(id);
            lastName = name;
            lastShort = shortName;
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
                UIBuild.FitName(Name, name, fontSize, shortName);
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
            nrt.offsetMax = new Vector2(-1f, 0f);

            Name.enableAutoSizing = false;
            Name.fontSize = UIFonts.Px(fontSize);
            string whole = UIBuild.FirstThatFits(Name, nrt.rect.width, name, shortName);
            bool showName = whole != null;
            if (showName) UIBuild.Set(Name, whole);
            else if (mode == NameMode.Truncate)
            {
                // the panel always shows icon AND name: a smaller grid size before any cut ("Karaoke": 96px at 24 in
                // Mulmaru, 85px beside the icon -> 12px), truncation only when even 12px is too wide
                showName = true;
                for (float s = UIFonts.Px(fontSize) - UIFonts.Grid; s >= UIFonts.Grid && whole == null; s -= UIFonts.Grid)
                {
                    Name.fontSize = s;
                    whole = UIBuild.FirstThatFits(Name, nrt.rect.width, name, shortName);
                }
                if (whole != null) UIBuild.Set(Name, whole);
                else UIBuild.FitName(Name, name, UIFonts.Grid, shortName);
            }

            Name.gameObject.SetActive(showName);
            // icon alone: centred in the badge; with a name: left, name fills the rest
            float x = showName ? Pad : (Rect.rect.width - icon) * 0.5f;
            irt.anchoredPosition = new Vector2(x, 0f);
        }
    }
}
