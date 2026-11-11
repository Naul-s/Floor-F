using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Winter-season decoration built from plain Images (no assets): snowflakes (three crossed bars + a centre gem),
    /// holly (two leaves + three berries) and <see cref="SnowCap"/> - little drifts of snow piled on a box's top edge.
    /// Nothing here is a raycast target.
    /// </summary>
    static class WinterDeco
    {
        /// <summary>Ice blue: winter-season accents (card edge, ribbon, flakes).</summary>
        public static readonly Color Ice = UIStyle.Hex("#BFE6FF");
        public static readonly Color IceEdge = UIStyle.Hex("#7FB8E6");
        public static readonly Color IceFill = new Color(0.025f, 0.045f, 0.085f, 0.97f), IceDim = new Color(0.0f, 0.02f, 0.05f, 0.72f);
        public static readonly Color Snow = new Color(0.94f, 0.97f, 1f, 1f);
        /// <summary>Christmas: holly green, berry red, the card's deep green.</summary>
        public static readonly Color Holly = UIStyle.Hex("#3FA34D"), Berry = UIStyle.Hex("#E0413A");
        public static readonly Color ChristmasEdge = UIStyle.Hex("#D2453A");
        public static readonly Color ChristmasFill = new Color(0.02f, 0.06f, 0.035f, 0.97f), ChristmasDim = new Color(0.01f, 0.03f, 0.02f, 0.74f);
        public const string IceHex = "#BFE6FF", HollyHex = "#9BE89B", BerryHex = "#FF6A5E";

        /// <summary>Six-armed flake, <paramref name="size"/> across, centred on <paramref name="pos"/> (from the parent's centre).</summary>
        public static RectTransform Snowflake(string name, Transform parent, Vector2 pos, float size, Color color)
        {
            var root = UIBuild.Rect(name, parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = pos;
            root.sizeDelta = new Vector2(size, size);
            float w = Mathf.Max(2f, Mathf.Round(size / 10f));
            for (int i = 0; i < 3; i++)
            {
                var bar = UIBuild.Box("Arm" + i, root, color).rectTransform;
                bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = Vector2.zero;
                bar.sizeDelta = new Vector2(size, w);
                bar.localRotation = Quaternion.Euler(0f, 0f, 90f + i * 60f);
                // little side twigs near each tip
                for (int end = -1; end <= 1; end += 2)
                {
                    var twig = UIBuild.Box("Twig", bar, color).rectTransform;
                    twig.anchorMin = twig.anchorMax = twig.pivot = new Vector2(0.5f, 0.5f);
                    twig.anchoredPosition = new Vector2(end * size * 0.3f, 0f);
                    twig.sizeDelta = new Vector2(w, size * 0.28f);
                }
            }
            UIBuild.Diamond("Core", root, Vector2.zero, w * 2f, color);
            return root;
        }

        /// <summary>Hand-authored holly sprig (23x11): two spiky leaves, three berries, a 1-px dark outline.</summary>
        static readonly string[] HollyPixels =
        {
            "..........ooo..........",
            ".........ohrro.........",
            ".........orrro.........",
            ".......ooorrrooo.......",
            "....o.oohrrohrroo.o....",
            "..oogogorrrorrrogogoo..",
            ".ogggggorrrorrrogggggo.",
            "ogglglglooooooolglglggo",
            ".oogggggggo.ogggggggoo.",
            "...ogggggo...ogggggo...",
            "....ooooo.....ooooo....",
        };
        public const int HollyW = 23, HollyH = 11;
        static Sprite holly;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => holly = null;

        static Sprite HollySprite => holly != null ? holly : holly = PixelSprite("UI Holly", HollyPixels, c =>
            c == 'o' ? UIStyle.Hex("#10200F") : c == 'g' ? UIStyle.Hex("#2F8A3E") : c == 'l' ? UIStyle.Hex("#6CC86A")
            : c == 'r' ? UIStyle.Hex("#D8352E") : c == 'h' ? UIStyle.Hex("#FFB3A3") : Color.clear);

        /// <summary>Pixel grid (row 0 = top) -> point-filtered sprite, one texel per character.</summary>
        static Sprite PixelSprite(string name, string[] rows, System.Func<char, Color> palette)
        {
            int h = rows.Length, w = rows[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[(h - 1 - y) * w + x] = palette(rows[y][x]);
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.name = name;
            sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }

        /// <summary>Pixel holly at an integer <paramref name="scale"/> (2 = 46x22), centred on <paramref name="pos"/>.</summary>
        public static RectTransform HollySprig(string name, Transform parent, Vector2 pos, int scale)
        {
            var img = UIBuild.Box(name, parent, Color.white);
            img.sprite = HollySprite;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(HollyW * scale, HollyH * scale);
            return rt;
        }
    }

    /// <summary>
    /// Snow piled on the top edge of a box: a row of white bumps of uneven height (seeded), sitting ON the edge and
    /// scaled by <see cref="SetAmount"/> (ISeasonQuery.SnowAmount: 0 = gone). Outside any layout, never a raycast target.
    /// </summary>
    sealed class SnowCap
    {
        readonly RectTransform root;
        readonly RectTransform[] bumps;
        readonly float[] heights;
        float shown = -1f;

        public SnowCap(Transform box, int seed, float maxHeight = 7f)
        {
            root = UIBuild.Rect("SnowCap", box);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, -2f); // sits over the top edge line
            root.sizeDelta = new Vector2(0f, maxHeight);
            var le = root.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            const int N = 14;
            bumps = new RectTransform[N];
            heights = new float[N];
            var rng = new System.Random(seed);
            float x = 0f;
            for (int i = 0; i < N; i++)
            {
                float w = 1f / N;
                var b = UIBuild.Box("Drift" + i, root, WinterDeco.Snow).rectTransform;
                b.anchorMin = new Vector2(x, 0f);
                b.anchorMax = new Vector2(x + w, 0f);
                b.pivot = new Vector2(0.5f, 0f);
                b.anchoredPosition = Vector2.zero;
                heights[i] = Mathf.Round(Mathf.Lerp(2f, maxHeight, (float)rng.NextDouble())) + (i == 0 || i == N - 1 ? -2f : 0f);
                b.sizeDelta = new Vector2(0f, heights[i]);
                // a soft blue shadow under the drift keeps it readable on the gold edges
                var shade = UIBuild.Box("Shade", b, new Color(0.55f, 0.7f, 0.9f, 0.9f)).rectTransform;
                shade.anchorMin = Vector2.zero;
                shade.anchorMax = new Vector2(1f, 0f);
                shade.pivot = new Vector2(0.5f, 0f);
                shade.anchoredPosition = Vector2.zero;
                shade.sizeDelta = new Vector2(0f, 2f);
                bumps[i] = b;
                x += w;
            }
            root.gameObject.SetActive(false);
        }

        public bool Visible => root.gameObject.activeInHierarchy;
        public RectTransform Root => root;

        /// <summary>0..1: 0 hides it, light snow = thin dusting, 1 = full drifts.</summary>
        public void SetAmount(float amount)
        {
            amount = Mathf.Clamp01(amount);
            float q = Mathf.Round(amount * 10f) / 10f; // no per-frame resize
            if (Mathf.Approximately(q, shown)) return;
            shown = q;
            bool on = q > 0f;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            float k = Mathf.Lerp(0.45f, 1f, q);
            for (int i = 0; i < bumps.Length; i++) bumps[i].sizeDelta = new Vector2(0f, Mathf.Max(2f, Mathf.Round(heights[i] * k)));
        }
    }
}
