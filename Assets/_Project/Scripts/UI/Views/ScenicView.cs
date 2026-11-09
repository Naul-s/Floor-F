using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Ways to see the city without the UI in front of it.
    /// <para><b>Scenic view</b> (V, the small eye button next to the day box, or SCENIC VIEW in the pause menu;
    /// Running/Intro/Summary/Shop only): pauses through IMenuFlow.SetPaused (the pause card stays hidden), hides every
    /// UI element (UIRoot fades the HUD canvas out) except a tiny "V / click: back" hint that fades after 2 s, and publishes
    /// ScenicViewChanged(true, false). Any key / click returns: ScenicViewChanged(false, false), unpause (or back to the
    /// pause menu when it was opened from there). The exit is applied in LateUpdate, after KeyboardInput saw the same
    /// key while still paused (a Space that ends the view never also opens the doors).</para>
    /// <para><b>Establishing shot</b> (each real day's Intro, not Day 0; <see cref="UIFeatures.EstablishingShots"/>):
    /// the UI is hidden for <see cref="EstablishingTime"/> s with ScenicViewChanged(true, true) and a lower-third caption
    /// "DAY 7 · Night of Falling Stars" between thin letterbox bars, then ScenicViewChanged(false, true) and the intro card
    /// fades in. Any key / click skips. Enter is held back from KeyboardInput meanwhile (it would start the day).</para>
    /// </summary>
    sealed class ScenicView
    {
        public enum Mode { None, Scenic, Establishing }
        public const float HintHold = 1.6f, HintFade = 0.4f;
        public const float EstablishingTime = 2.6f, BarIn = 0.35f, CaptionIn = 0.35f, CaptionFade = 0.4f, BarH = 32f;
        public const float ButtonSize = 40f;
        /// <summary>Eye icon (2 canvas units per pixel).</summary>
        static readonly string[] Eye =
        {
            "....#####....",
            "..##.....##..",
            ".#...###...#.",
            "#...#####...#",
            ".#...###...#.",
            "..##.....##..",
            "....#####....",
        };

        readonly UIContext ctx;
        readonly UIBuild.ButtonParts button;
        readonly RectTransform hint, caption, topBar, bottomBar;
        readonly CanvasGroup hintGroup, captionGroup;
        readonly TextMeshProUGUI hintText, captionText;
        readonly Image[] captionEdges = new Image[2];
        readonly Image captionFill;

        Mode mode;
        bool fromPause, exitPending;
        int enteredFrame = -1, enterHeldUntilFrame = -1;
        float hintAge = -1f, estAge = -1f;
        int lastIntroDay = int.MinValue;

        public ScenicView(UIContext ctx, RectTransform canvas, RectTransform fxCanvas)
        {
            this.ctx = ctx;

            // HUD button: right of the day box, top-left sky corner (Running / Shop)
            button = UIBuild.Button("ScenicButton", canvas, "", 12f, UIStyle.Btn, OnButton);
            UIBuild.Place(button.Rect, new Vector2(0f, 1f), new Vector2(24f + ElevatorPanelView.Width + 8f, -16f), new Vector2(ButtonSize, ButtonSize));
            UIBuild.Outline("Frame", button.Rect, UIStyle.PanelEdge, 1f, 0f);
            var icon = new GameObject("Eye", typeof(RectTransform)).AddComponent<RawImage>();
            icon.gameObject.layer = PresentationContract.UiLayer;
            icon.transform.SetParent(button.Rect, false);
            icon.texture = PixelTexture(Eye, UIStyle.Ink);
            icon.raycastTarget = false;
            UIBuild.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(Eye[0].Length * 2f, Eye.Length * 2f));
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            // the key hint sits in the label, bottom-right
            button.Label.text = "V";
            button.Label.color = UIStyle.InkDim;
            button.Label.alignment = TextAlignmentOptions.BottomRight;
            UIBuild.Stretch(button.Label.rectTransform, 2f);
            button.Rect.gameObject.SetActive(false);

            // letterbox bars + lower-third caption (establishing shot)
            topBar = Bar("LetterboxTop", fxCanvas, 1f);
            bottomBar = Bar("LetterboxBottom", fxCanvas, 0f);
            caption = UIBuild.Rect("EstablishingCaption", fxCanvas);
            caption.anchorMin = caption.anchorMax = new Vector2(0.5f, 0f);
            caption.pivot = new Vector2(0.5f, 0f);
            caption.anchoredPosition = new Vector2(0f, BarH + 28f);
            caption.sizeDelta = new Vector2(480f, 48f);
            captionGroup = caption.gameObject.AddComponent<CanvasGroup>();
            captionGroup.blocksRaycasts = captionGroup.interactable = false;
            captionFill = UIBuild.Box("Fill", caption, new Color(0.03f, 0.03f, 0.06f, 0.78f));
            UIBuild.Stretch(captionFill.rectTransform);
            for (int i = 0; i < 2; i++)
            {
                var e = UIBuild.Box("Edge" + i, caption, UIStyle.CardEdge);
                var rt = e.rectTransform;
                rt.anchorMin = new Vector2(0f, i);
                rt.anchorMax = new Vector2(1f, i);
                rt.pivot = new Vector2(0.5f, i);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, 2f);
                captionEdges[i] = e;
            }
            captionText = UIBuild.Text("Text", caption, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            UIBuild.Stretch(captionText.rectTransform);
            captionText.textWrappingMode = TextWrappingModes.NoWrap;
            caption.gameObject.SetActive(false);

            // scenic hint (bottom centre)
            hint = UIBuild.Rect("ScenicHint", fxCanvas);
            hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0f);
            hint.pivot = new Vector2(0.5f, 0f);
            hint.anchoredPosition = new Vector2(0f, 20f);
            hint.sizeDelta = new Vector2(240f, 32f);
            hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
            hintGroup.blocksRaycasts = hintGroup.interactable = false;
            UIBuild.Stretch(UIBuild.Box("Plate", hint, new Color(0f, 0f, 0.02f, 0.6f)).rectTransform);
            hintText = UIBuild.Text("Text", hint, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            UIBuild.Stretch(hintText.rectTransform);
            hintText.textWrappingMode = TextWrappingModes.NoWrap;
            hint.gameObject.SetActive(false);
        }

        static RectTransform Bar(string name, RectTransform parent, float side)
        {
            var rt = UIBuild.Box(name, parent, Color.black).rectTransform;
            rt.anchorMin = new Vector2(0f, side);
            rt.anchorMax = new Vector2(1f, side);
            rt.pivot = new Vector2(0.5f, side);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, BarH);
            rt.gameObject.SetActive(false);
            return rt;
        }

        /// <summary>Point-filtered texture from a '#' pattern (top row first) in one colour.</summary>
        public static Texture2D PixelTexture(string[] rows, Color color)
        {
            int w = rows[0].Length, h = rows.Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "PixelIcon" };
            var px = new Color32[w * h];
            Color32 on = color, off = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(h - 1 - y) * w + x] = rows[y][x] == '#' ? on : off;
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ------------------------------------------------------------------ state

        public Mode Current => mode;
        /// <summary>The HUD canvas is hidden (scenic view or establishing shot).</summary>
        public bool HidesUi => mode != Mode.None;
        /// <summary>The pause card stays hidden while the scenic view uses the pause.</summary>
        public bool SuppressPauseMenu => mode == Mode.Scenic;
        /// <summary>Enter belongs to the UI (skip) until the frame after the shot ended.</summary>
        public bool HoldsEnter => mode == Mode.Establishing || Time.frameCount <= enterHeldUntilFrame;

        bool OnTitle => ctx.Phase == DayPhase.Title || (ctx.Menu != null && ctx.Menu.IsOnTitle);

        public bool ScenicAllowed
        {
            get
            {
                var p = ctx.Phase;
                return mode == Mode.None && ctx.Menu != null && ctx.HasGame && !OnTitle
                       && (p == DayPhase.Running || p == DayPhase.Intro || p == DayPhase.Summary || p == DayPhase.Shop);
            }
        }

        void OnButton()
        {
            bool ok = TryEnterScenic();
            ctx.Sound(ok ? UiSoundKind.Click : UiSoundKind.Error);
        }

        /// <summary>V / the HUD button / the pause menu button. False when not allowed here (or the pause was refused).</summary>
        public bool TryEnterScenic()
        {
            if (!ScenicAllowed) return false;
            bool wasPaused = ctx.Menu.IsPaused;
            mode = Mode.Scenic; // first: PauseChanged(true) refreshes the menu, which must already keep the pause card hidden
            if (!wasPaused && !ctx.Menu.SetPaused(true)) { mode = Mode.None; return false; }
            fromPause = wasPaused;
            exitPending = false;
            enteredFrame = Time.frameCount;
            hintAge = 0f;
            UIBuild.Set(hintText, ctx.S(UIStrings.ScenicHint));
            float w = hintText.GetPreferredValues(hintText.text, float.PositiveInfinity, float.PositiveInfinity).x;
            hint.sizeDelta = new Vector2(Mathf.Ceil(w) + 32f, 32f);
            hint.gameObject.SetActive(true);
            hintGroup.alpha = 1f;
            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            return true;
        }

        /// <summary>Leaves the scenic view now: event, unpause (unless it was opened from the pause menu).</summary>
        public void ExitScenic()
        {
            if (mode != Mode.Scenic) return;
            mode = Mode.None;
            exitPending = false;
            hint.gameObject.SetActive(false);
            hintAge = -1f;
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            if (!fromPause && ctx.Menu != null && ctx.Menu.IsPaused) ctx.Menu.SetPaused(false);
        }

        /// <summary>PauseChanged: something else unpaused (Esc through KeyboardInput) - the view ends with it.</summary>
        public void OnPauseChanged(PauseChanged e)
        {
            if (mode == Mode.Scenic && !e.Paused) { fromPause = true; ExitScenic(); }
            else if (mode == Mode.Establishing && e.Paused) EndEstablishing(); // Esc during the shot: the pause card over the intro
        }

        void StartEstablishing()
        {
            mode = Mode.Establishing;
            estAge = 0f;
            enteredFrame = Time.frameCount;
            SetCaption();
            caption.gameObject.SetActive(true);
            topBar.gameObject.SetActive(true);
            bottomBar.gameObject.SetActive(true);
            AnimateEstablishing();
            GameServices.Events.Publish(new ScenicViewChanged(true, true));
        }

        public void EndEstablishing()
        {
            if (mode != Mode.Establishing) return;
            mode = Mode.None;
            estAge = -1f;
            enterHeldUntilFrame = Time.frameCount + 1;
            caption.gameObject.SetActive(false);
            topBar.gameObject.SetActive(false);
            bottomBar.gameObject.SetActive(false);
            GameServices.Events.Publish(new ScenicViewChanged(false, true));
        }

        /// <summary>"DAY 7 · Night of Falling Stars" from the day's data title ("Day 7 - Night of Falling Stars").</summary>
        public static string CaptionFor(string dataTitle, string fallbackDay, bool korean)
        {
            string t = string.IsNullOrEmpty(dataTitle) ? fallbackDay : dataTitle.Trim();
            int dash = t.IndexOf(" - ", System.StringComparison.Ordinal);
            if (dash <= 0) return "<color=" + UIFormat.LuckyHex + ">" + t + "</color>";
            string head = t.Substring(0, dash).Trim(), rest = t.Substring(dash + 3).Trim();
            if (!korean) head = head.ToUpperInvariant();
            return "<color=" + UIFormat.LuckyHex + ">" + head + "</color>  ·  " + rest;
        }

        void SetCaption()
        {
            bool ko = ctx.Korean;
            string title = null;
            if (ctx.TryGetDay(out var d)) title = ko && !string.IsNullOrEmpty(d.titleKo) ? d.titleKo : d.title;
            string text = CaptionFor(title, UIFormat.DayLabel(ctx.Day.DisplayDay, ctx.Day.PlayableDayCount, ctx.Day.IsTutorial, ko), ko);
            bool friday = ctx.Day.IsFriday13, star = ctx.Day.IsStarNight && !friday;
            if (star) text = "<color=#DCD4FF>" + UIFonts.Star + "</color>  " + text + "  <color=#DCD4FF>" + UIFonts.Star + "</color>";
            captionText.text = text;
            var edge = friday ? UIStyle.Unlucky : star ? HudView.StarEdge : UIStyle.CardEdge;
            foreach (var e in captionEdges) e.color = edge;
            captionFill.color = friday ? new Color(0.07f, 0.01f, 0.015f, 0.8f) : star ? new Color(0.03f, 0.03f, 0.11f, 0.8f) : new Color(0.03f, 0.03f, 0.06f, 0.78f);
            float w = captionText.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x;
            caption.sizeDelta = new Vector2(Mathf.Max(320f, Mathf.Ceil(w) + 64f), 48f);
        }

        void AnimateEstablishing()
        {
            float t = estAge;
            // bars slide in, hold, slide out over the last BarIn seconds (whole units: hard pixel edges)
            float barK = t < BarIn ? t / BarIn : t > EstablishingTime - BarIn ? (EstablishingTime - t) / BarIn : 1f;
            barK = Mathf.Clamp01(barK);
            barK = 1f - (1f - barK) * (1f - barK);
            float h = Mathf.Round(BarH * barK);
            topBar.sizeDelta = new Vector2(0f, h);
            bottomBar.sizeDelta = new Vector2(0f, h);
            // caption fades in after the bars, out before them
            float inStart = BarIn * 0.5f, outEnd = EstablishingTime - BarIn * 0.5f;
            float a = t < inStart ? 0f : t < inStart + CaptionIn ? (t - inStart) / CaptionIn
                : t > outEnd - CaptionFade ? Mathf.Clamp01((outEnd - t) / CaptionFade) : 1f;
            captionGroup.alpha = a;
            caption.anchoredPosition = new Vector2(0f, BarH + 28f + Mathf.Round((1f - a) * -8f));
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>
        /// Update: the establishing trigger / timer, the scenic hint fade, the HUD button and this frame's presses.
        /// Returns true when the press was used here (skip / exit / V), so nothing else acts on it.
        /// </summary>
        public bool Tick(float unscaledDt, UIPress press)
        {
            bool used = false;
            var phase = ctx.Phase;
            unscaledDt = Mathf.Min(unscaledDt, 0.1f); // a loading hitch must not eat the shot / the hint
            int introDay = ctx.HasGame && phase == DayPhase.Intro && !OnTitle ? ctx.Day.CurrentDay.Number : int.MinValue;
            if (introDay != lastIntroDay)
            {
                lastIntroDay = introDay;
                if (mode == Mode.Establishing) EndEstablishing();
                if (introDay != int.MinValue && UIFeatures.EstablishingShots && mode == Mode.None && !ctx.Day.IsTutorial && !ctx.Paused)
                    StartEstablishing();
            }

            if (mode == Mode.Establishing)
            {
                estAge += unscaledDt;
                if (phase != DayPhase.Intro || estAge >= EstablishingTime) EndEstablishing();
                else if (press.Any && Time.frameCount > enteredFrame) { EndEstablishing(); used = true; }
                else AnimateEstablishing();
            }
            else if (mode == Mode.Scenic)
            {
                bool allowed = ctx.HasGame && !OnTitle && phase != DayPhase.Ending;
                if (!allowed) { fromPause = true; ExitScenic(); }
                else if (press.Any && Time.frameCount > enteredFrame) { exitPending = true; used = true; }
                if (hintAge >= 0f)
                {
                    hintAge += unscaledDt;
                    float a = hintAge < HintHold ? 1f : 1f - (hintAge - HintHold) / HintFade;
                    if (a <= 0f) { hint.gameObject.SetActive(false); hintAge = -1f; }
                    else hintGroup.alpha = a;
                }
            }
            else if (press.V && ScenicAllowed)
            {
                used = TryEnterScenic();
            }

            bool show = mode == Mode.None && ctx.HasGame && !OnTitle && !ctx.Paused && (phase == DayPhase.Running || phase == DayPhase.Shop);
            if (button.Rect.gameObject.activeSelf != show) button.Rect.gameObject.SetActive(show);
            return used;
        }

        /// <summary>LateUpdate: a pending scenic exit (after KeyboardInput handled the same key while still paused).</summary>
        public bool LateTick()
        {
            if (!exitPending) return false;
            ExitScenic();
            return true;
        }

        public void BringButtonToFront() => button.Rect.SetAsLastSibling();

        // ------------------------------------------------------------------ test hooks

        public bool ButtonVisible => button.Rect.gameObject.activeInHierarchy;
        public RectTransform ButtonRect => button.Rect;
        public void ClickButton() => button.Button.onClick.Invoke();
        public bool HintVisible => hint.gameObject.activeInHierarchy && hintGroup.alpha > 0.01f;
        public string HintText => hint.gameObject.activeInHierarchy ? hintText.text : null;
        public RectTransform Hint => hint;
        public bool CaptionVisible => caption.gameObject.activeInHierarchy;
        public string CaptionText => CaptionVisible ? captionText.text : null;
        public RectTransform Caption => caption;
        public float CaptionAlpha => captionGroup.alpha;
        public float EstablishingAge => estAge;
        /// <summary>Test hook: jump the establishing timer (seconds) and redraw.</summary>
        public void SetEstablishingAge(float t)
        {
            if (mode != Mode.Establishing) return;
            estAge = t;
            AnimateEstablishing();
        }
    }
}
