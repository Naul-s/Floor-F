using UnityEngine;

namespace FloorF.UI
{
    /// <summary>Colours and sizes (reference resolution 1280x720, CanvasScaler Expand).</summary>
    static class UIStyle
    {
        public const float RefWidth = 1280f, RefHeight = 720f;
        public const int SortingOrder = 10;
        public const float PlaneDistance = 1f;

        public static readonly Color Lucky = Hex("#FFC83D");
        public static readonly Color Unlucky = Hex("#FF4B4B");
        public static readonly Color Good = Hex("#5BE37D");
        public static readonly Color Ink = Hex("#F4EEDC");
        public static readonly Color InkDim = new Color(0.96f, 0.93f, 0.86f, 0.55f);
        public static readonly Color Dark = Hex("#101119");

        public static readonly Color Card = new Color(0.07f, 0.075f, 0.11f, 0.94f);
        public static readonly Color CardEdge = Hex("#C9A15A");
        public static readonly Color Box = new Color(0.05f, 0.055f, 0.08f, 0.82f);
        public static readonly Color Dim = new Color(0f, 0f, 0.02f, 0.62f);

        public static readonly Color PanelPlate = Hex("#2B2D33");
        public static readonly Color PanelEdge = Hex("#8C8F98");
        public static readonly Color Btn = Hex("#3B3F4A");
        public static readonly Color BtnTarget = Hex("#FFB21E");
        public static readonly Color BtnApplicable = Hex("#FFD447");
        public static readonly Color BtnBlocked = new Color(0.23f, 0.24f, 0.28f, 0.55f);
        public static readonly Color Led = Hex("#FF5A36");
        /// <summary>
        /// Unlit segments: OPAQUE dark red at ~13% of the lit LED's brightness. (A low alpha looked far brighter than its
        /// value because blending happens in linear space - a "1" read as an "8".)
        /// </summary>
        public static readonly Color LedGhost = new Color(Led.r * GhostLevel, Led.g * GhostLevel, Led.b * GhostLevel, 1f);
        public const float GhostLevel = 0.13f;
        public static readonly Color Action = Hex("#2E7D5B");
        public static readonly Color ActionHot = Hex("#3FAE7E");

        /// <summary>Destination badge colours (index = position in hotel.json destinations) - the icon placeholder.</summary>
        static readonly Color[] destination =
        {
            Hex("#9AA3B5"), Hex("#E8875A"), Hex("#58B368"), Hex("#C86DD7"), Hex("#E0C04A"),
            Hex("#5A9BE8"), Hex("#B07A4E"), Hex("#D94F6A"), Hex("#58C9C2"), Hex("#8C7AE6"),
        };
        public static readonly Color DestinationUnknown = Hex("#3A2A4A");

        public static Color Destination(int index) => index < 0 ? DestinationUnknown : destination[index % destination.Length];

        public static Color Patience(float p01) =>
            p01 > 0.6f ? Good : p01 > 0.3f ? Lucky : Unlucky;

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>Black or white ink for text on <paramref name="bg"/>.</summary>
        public static Color InkOn(Color bg) => (0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b) > 0.55f ? Dark : Ink;

        /// <summary>Pure black or white, whichever has the higher WCAG contrast on <paramref name="bg"/> (small labels).</summary>
        public static Color ContrastInk(Color bg)
        {
            var l = bg.linear;
            float lum = 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
            return lum > 0.179f ? Color.black : Color.white;
        }

        public static readonly Color Title = Hex("#FFD447");
        public static readonly Color TitlePanel = new Color(0.04f, 0.045f, 0.07f, 0.78f);
        public static readonly Color Highlight = Hex("#FFE14D");
    }
}
