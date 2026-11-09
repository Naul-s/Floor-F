using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Golden click effect on the FX canvas (above every UI card, no GraphicRaycaster, every Image raycastTarget off - it
    /// can never block a click): a dotted pixel ring expanding for <see cref="RingLife"/> s plus a few sparkle pixels
    /// flying out. Hard square pixels (no sprite, no blur) snapped to a <see cref="Pixel"/>-unit grid around the
    /// whole-unit click point. Unscaled time: it plays while paused. <see cref="Spawn"/> big = the title reveal burst.
    /// One ring per click, it only moves and fades (no flashing).
    /// </summary>
    sealed class ClickFxView
    {
        public const float RingLife = 0.25f, SparkLife = 0.38f, Pixel = 3f;
        public const float BigRingLife = 0.5f, BigSparkLife = 0.7f, BigPixel = 6f;
        public const int RingDots = 16, Sparks = 5, BigRingDots = 28, BigSparks = 14, MaxActive = 8;
        static readonly Color RingA = UIStyle.Hex("#FFE066"), RingB = UIStyle.Hex("#F0962A");
        static readonly Color SparkA = UIStyle.Hex("#FFF6C8"), SparkB = UIStyle.Lucky;

        sealed class Fx
        {
            public RectTransform Root;
            public Image[] Ring, Spark;
            public Vector2[] Dir;
            public float[] Speed;
            public float Age;
            public bool Big;
        }

        readonly RectTransform layer;
        readonly List<Fx> pool = new List<Fx>(), active = new List<Fx>();
        uint seed = 0x2545F491u;

        public ClickFxView(RectTransform fxCanvas)
        {
            layer = UIBuild.Stretch(UIBuild.Rect("ClickFx", fxCanvas));
        }

        float Rand()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Starts one effect at canvas-local <paramref name="pos"/> (bottom-left origin, reference units).</summary>
        public void Spawn(Vector2 pos, bool big = false)
        {
            Fx fx = null;
            for (int i = 0; i < pool.Count; i++) if (!pool[i].Root.gameObject.activeSelf) { fx = pool[i]; break; }
            if (fx == null && pool.Count < MaxActive) { fx = Create(); pool.Add(fx); }
            if (fx == null) { fx = active[0]; active.RemoveAt(0); } // the oldest makes room
            fx.Big = big;
            fx.Age = 0f;
            int dots = big ? BigRingDots : RingDots, sparks = big ? BigSparks : Sparks;
            for (int i = 0; i < fx.Ring.Length; i++) fx.Ring[i].gameObject.SetActive(i < dots);
            for (int i = 0; i < fx.Spark.Length; i++)
            {
                bool on = i < sparks;
                fx.Spark[i].gameObject.SetActive(on);
                if (!on) continue;
                float a = (i + 0.2f + 0.6f * Rand()) / sparks * Mathf.PI * 2f;
                fx.Dir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                fx.Speed[i] = big ? Mathf.Lerp(150f, 260f, Rand()) : Mathf.Lerp(70f, 120f, Rand());
                float px = big && i % 3 == 0 ? BigPixel : big ? Pixel * 1.5f : Pixel;
                fx.Spark[i].rectTransform.sizeDelta = new Vector2(px, px);
            }
            float rp = big ? BigPixel : Pixel;
            for (int i = 0; i < dots; i++) fx.Ring[i].rectTransform.sizeDelta = new Vector2(rp, rp);
            fx.Root.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
            fx.Root.gameObject.SetActive(true);
            fx.Root.SetAsLastSibling();
            active.Add(fx);
            Animate(fx);
        }

        Fx Create()
        {
            var fx = new Fx { Root = UIBuild.Rect("Fx", layer) };
            fx.Root.anchorMin = fx.Root.anchorMax = Vector2.zero;
            fx.Root.pivot = new Vector2(0.5f, 0.5f);
            fx.Root.sizeDelta = Vector2.zero;
            fx.Ring = new Image[BigRingDots];
            for (int i = 0; i < fx.Ring.Length; i++) fx.Ring[i] = Dot("Ring" + i, fx.Root);
            fx.Spark = new Image[BigSparks];
            fx.Dir = new Vector2[BigSparks];
            fx.Speed = new float[BigSparks];
            for (int i = 0; i < fx.Spark.Length; i++) fx.Spark[i] = Dot("Spark" + i, fx.Root);
            fx.Root.gameObject.SetActive(false);
            return fx;
        }

        static Image Dot(string name, RectTransform parent)
        {
            var img = UIBuild.Box(name, parent, RingA);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        public void Tick(float unscaledDt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var fx = active[i];
                fx.Age += unscaledDt;
                if (fx.Age >= (fx.Big ? BigSparkLife : SparkLife)) { fx.Root.gameObject.SetActive(false); active.RemoveAt(i); continue; }
                Animate(fx);
            }
        }

        static float Snap(float v, float grid) => Mathf.Round(v / grid) * grid;

        void Animate(Fx fx)
        {
            bool big = fx.Big;
            float ringLife = big ? BigRingLife : RingLife, sparkLife = big ? BigSparkLife : SparkLife;
            float grid = big ? BigPixel : Pixel;
            int dots = big ? BigRingDots : RingDots, sparks = big ? BigSparks : Sparks;

            // ring: radius eases out, fades with t^2, gold -> amber
            float t = Mathf.Clamp01(fx.Age / ringLife);
            bool ringOn = fx.Age < ringLife;
            float r = (big ? 10f : 4f) + (big ? 78f : 20f) * (1f - (1f - t) * (1f - t));
            var ringColor = Color.Lerp(RingA, RingB, t);
            ringColor.a = 1f - t * t;
            for (int i = 0; i < dots; i++)
            {
                var img = fx.Ring[i];
                if (img.enabled != ringOn) img.enabled = ringOn;
                if (!ringOn) continue;
                float a = (i + 0.5f) / dots * Mathf.PI * 2f;
                img.rectTransform.anchoredPosition = new Vector2(Snap(Mathf.Cos(a) * r, grid), Snap(Mathf.Sin(a) * r, grid));
                img.color = ringColor;
            }

            // sparks: decelerate outwards, a little gravity, fade out over the last 40%
            float u = Mathf.Clamp01(fx.Age / sparkLife);
            float travel = (1f - (1f - u) * (1f - u)) * 0.5f * sparkLife;
            var sparkColor = Color.Lerp(SparkA, SparkB, u);
            sparkColor.a = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
            for (int i = 0; i < sparks; i++)
            {
                var d = fx.Dir[i] * (fx.Speed[i] * travel) + new Vector2(0f, -(big ? 60f : 30f) * u * u);
                d += fx.Dir[i] * (big ? 12f : 5f); // start just outside the click point
                var img = fx.Spark[i];
                img.rectTransform.anchoredPosition = new Vector2(Snap(d.x, Pixel), Snap(d.y, Pixel));
                img.color = sparkColor;
            }
        }

        public void Clear()
        {
            foreach (var fx in active) fx.Root.gameObject.SetActive(false);
            active.Clear();
        }

        // ------------------------------------------------------------------ test hooks

        public int ActiveCount => active.Count;
        public bool LastIsBig => active.Count > 0 && active[active.Count - 1].Big;
        public Vector2 LastPosition => active.Count > 0 ? active[active.Count - 1].Root.anchoredPosition : default;
        /// <summary>Any Image under the effect layer that takes raycasts (must stay false).</summary>
        public bool AnyRaycastTarget
        {
            get
            {
                foreach (var g in layer.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) return true;
                return false;
            }
        }
        /// <summary>Ring dot positions of the newest effect are whole multiples of the pixel grid.</summary>
        public bool LastOnGrid
        {
            get
            {
                if (active.Count == 0) return false;
                var fx = active[active.Count - 1];
                float grid = fx.Big ? BigPixel : Pixel;
                foreach (var img in fx.Ring)
                {
                    if (!img.gameObject.activeSelf || !img.enabled) continue;
                    var p = img.rectTransform.anchoredPosition;
                    if (Mathf.Abs(p.x / grid - Mathf.Round(p.x / grid)) > 0.01f || Mathf.Abs(p.y / grid - Mathf.Round(p.y / grid)) > 0.01f) return false;
                }
                return true;
            }
        }
    }
}
