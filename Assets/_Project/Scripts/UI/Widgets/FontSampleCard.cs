using TMPro;
using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// L2 capture aid (ui_font_compare.png): the same EN/KO lines in Galmuri11 SDF (left) and Raster+Point (right) at
    /// 12/24/36/48 so the render mode can be chosen by eye. Only built by the capture test hook; never in normal play.
    /// </summary>
    static class FontSampleCard
    {
        const string Sample = "Floor 4 → F  LUCKY! 3 people\n손님 머리 위 숫자 4층 행운!";

        public static GameObject Build(RectTransform canvas)
        {
            var root = UIBuild.Box("[UI] FontCompare", canvas, new Color(0.05f, 0.055f, 0.08f, 1f), raycast: false).rectTransform;
            UIBuild.Stretch(root);
            var sdf = UIFonts.Create(UIFonts.Mode.Sdf);
            var raster = UIFonts.Create(UIFonts.Mode.Raster);
            Column(root, sdf, "SDF (48pt sample, bilinear)", 0f);
            Column(root, raster, "RASTER_HINTED (12pt, point)", 0.5f);
            root.SetAsLastSibling();
            return root.gameObject;
        }

        static void Column(RectTransform root, TMP_FontAsset fa, string caption, float x01)
        {
            float y = -16f;
            var head = UIBuild.Text("Caption", root, fa != null ? caption : caption + " - unavailable", 24f, UIStyle.CardEdge, TextAlignmentOptions.TopLeft);
            Place(head, x01, y, 40f);
            if (fa != null) head.font = fa;
            y -= 44f;
            foreach (var size in new[] { 12f, 24f, 36f, 48f })
            {
                var t = UIBuild.Text("S" + size, root, Sample, size, UIStyle.Ink, TextAlignmentOptions.TopLeft);
                if (fa != null) t.font = fa;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                float h = size * 1.4f * 2f + 8f;
                Place(t, x01, y, h);
                y -= h;
            }
        }

        static void Place(TextMeshProUGUI t, float x01, float y, float h)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(x01, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, y);
            rt.sizeDelta = new Vector2(620f, h);
        }
    }
}
