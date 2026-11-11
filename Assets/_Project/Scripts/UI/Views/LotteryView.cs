using System.Collections.Generic;
using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Lottery night draw (LotteryDrawn): a plate at the top centre where a lottery ball rolls in, its number ticking
    /// through the numbers on the floor buttons (slowing down), stops on the winning number with a little bounce, then
    /// "WINNING NUMBER 7" pulses gently (1.5 Hz scale, no flashing) and the plate fades. The HUD badge keeps the number
    /// afterwards; the panel rings the matching button in gold. Never a raycast target, frozen while paused.
    /// </summary>
    sealed class LotteryView
    {
        public const float RollTime = 1.6f, RevealTime = 2.4f, FadeTime = 0.4f, Life = RollTime + RevealTime + FadeTime;
        const float PlateW = 340f, PlateH = 176f, Ball = 84f, RollIn = 120f;
        public static readonly Color Plate = new Color(0.07f, 0.05f, 0.01f, 0.94f), BallInk = UIStyle.Hex("#1B1508");

        readonly UIContext ctx;
        readonly RectTransform root, ball;
        readonly CanvasGroup group;
        readonly TextMeshProUGUI caption, number, result;
        readonly List<int> pool = new List<int>();
        float age = -1f, nextTick;
        int winning, shownIndex;

        public LotteryView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            root = UIBuild.Framed("LotteryDraw", canvas, Plate, UIStyle.Lucky, 3f).rectTransform;
            UIBuild.Place(root, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(PlateW, PlateH));
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            caption = UIBuild.Text("Caption", root, "", 24f, UIStyle.CardEdge, TextAlignmentOptions.Center);
            UIBuild.Place(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(PlateW - 16f, 28f));
            caption.textWrappingMode = TextWrappingModes.NoWrap;

            // the ball: gold ring, white face, dark number (a point-filtered disc: chunky pixel edge like the game)
            var holder = UIBuild.Rect("BallHolder", root);
            UIBuild.Place(holder, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(Ball, Ball));
            ball = UIBuild.Rect("Ball", holder);
            UIBuild.Stretch(ball);
            var ring = UIBuild.Box("Ring", ball, UIStyle.Lucky);
            ring.sprite = Disc;
            UIBuild.Stretch(ring.rectTransform);
            var face = UIBuild.Box("Face", ball, new Color(0.98f, 0.96f, 0.9f, 1f));
            face.sprite = Disc;
            UIBuild.Stretch(face.rectTransform, 6f);
            var shine = UIBuild.Box("Shine", ball, new Color(1f, 1f, 1f, 0.9f));
            shine.sprite = Disc;
            UIBuild.Place(shine.rectTransform, new Vector2(0.28f, 0.74f), Vector2.zero, new Vector2(14f, 14f));
            shine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            number = UIBuild.Heading(UIBuild.Text("Number", ball, "", 36f, BallInk, TextAlignmentOptions.Center, bold: true));
            UIBuild.Stretch(number.rectTransform);
            number.textWrappingMode = TextWrappingModes.NoWrap;

            result = UIBuild.Text("Result", root, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(result.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(PlateW - 16f, 30f));
            result.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            result.rectTransform.anchoredPosition = new Vector2(0f, 25f);
            result.textWrappingMode = TextWrappingModes.NoWrap;
            root.gameObject.SetActive(false);
        }

        static Sprite disc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => disc = null;

        /// <summary>A 21x21 point-filtered disc (no assets).</summary>
        static Sprite Disc
        {
            get
            {
                if (disc != null) return disc;
                const int N = 21;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "UI Disc", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color[N * N];
                float r = N * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        px[y * N + x] = dx * dx + dy * dy <= r * r ? Color.white : Color.clear;
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                disc = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                disc.name = "UI Disc";
                disc.hideFlags = HideFlags.HideAndDontSave;
                return disc;
            }
        }

        public void OnDrawn(LotteryDrawn e)
        {
            winning = e.Number;
            pool.Clear();
            if (ctx.Building != null)
                for (int f = 0; f < ctx.Building.FloorCount; f++)
                    if (int.TryParse(ctx.Building.GetLabel(new FloorIndex(f)).Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && !pool.Contains(n)) pool.Add(n);
            if (pool.Count < 2) { pool.Clear(); for (int n = 1; n <= 9; n++) pool.Add(n); }
            caption.text = ctx.F(UIStrings.LotteryDraw, e.DrawIndex + 1, Mathf.Max(1, e.DrawCount));
            result.text = ctx.F(UIStrings.WinningNumber, N(e.Number));
            shownIndex = Mathf.Abs(e.Number * 7 + e.DrawIndex * 3) % pool.Count;
            number.text = N(pool[shownIndex]);
            age = 0f;
            nextTick = 0.07f;
            root.gameObject.SetActive(true);
            Animate();
        }

        public void Stop()
        {
            age = -1f;
            root.gameObject.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (age < 0f) return;
            if (ctx.Phase != DayPhase.Running) { Stop(); return; }
            if (!ctx.Paused) age += dt;
            if (age >= Life) { Stop(); return; }
            // the number ticks through the panel's numbers, slower and slower (<= 14 changes/s, a small digit: no flash)
            while (age < RollTime && age >= nextTick)
            {
                shownIndex = (shownIndex + 1) % pool.Count;
                if (pool[shownIndex] == winning && pool.Count > 1) shownIndex = (shownIndex + 1) % pool.Count; // the winner comes last
                nextTick += Mathf.Lerp(0.07f, 0.3f, Mathf.Clamp01(age / RollTime));
            }
            Animate();
        }

        void Animate()
        {
            bool rolling = age < RollTime;
            if (!rolling) UIBuild.Set(number, N(winning));
            else UIBuild.Set(number, N(pool[shownIndex]));
            // rolls in from the left, turning as it goes; stops with a small hop
            float k = Mathf.Clamp01(age / RollTime);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float x = Mathf.Lerp(-RollIn, 0f, ease);
            float hop = !rolling && age < RollTime + 0.3f ? 8f * Mathf.Sin((age - RollTime) / 0.3f * Mathf.PI) : 0f;
            ball.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(hop));
            ball.localRotation = Quaternion.Euler(0f, 0f, rolling ? -x / (Ball * 0.5f) * Mathf.Rad2Deg * 0.35f : 0f);
            result.gameObject.SetActive(!rolling);
            if (!rolling)
            {
                float pulse = 1f + 0.05f * Mathf.Sin((age - RollTime) * Mathf.PI * 2f * 1.5f); // 1.5 Hz, scale only
                result.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }
            group.alpha = age > RollTime + RevealTime ? Mathf.Clamp01((Life - age) / FadeTime) : Mathf.Clamp01(age / 0.2f);
        }

        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ test hooks
        public bool Visible => root.gameObject.activeInHierarchy;
        /// <summary>The ball is still rolling (the HUD badge waits for it).</summary>
        public bool Rolling => Visible && age < RollTime;
        public bool Revealed => Visible && age >= RollTime;
        public string NumberText => number.text;
        public string ResultText => Revealed ? result.text : null;
        public string CaptionText => Visible ? caption.text : null;
        public RectTransform Rect => root;
        public void SetAge(float t) { if (age >= 0f) { age = t; Animate(); } }
    }
}
