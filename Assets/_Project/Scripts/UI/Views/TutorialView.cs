using System.Collections.Generic;
using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// "Day 0" training-shift lesson (shown while IGameSettings.ShowTutorialHints &amp;&amp; IDayQuery.IsTutorial, Running,
    /// not paused; real days never show prompts). Guests come in waves (a wave = guests spawning while the hotel was
    /// empty); one line at a time is chosen from the live state and the wave, with a pulsing outline + an arrow at
    /// the thing to press / look at:
    ///   wave 1: OPEN where she waits (go there first) -> read the chip (gold lucky / red unlucky / icon = destination)
    ///           -> CLOSE (head count) -> destination floor -> OPEN (floor number)
    ///   wave 2: "N guests together: wait until all are in, then CLOSE (N = lucky)" -> deliver
    ///   wave 3: two riders, two stops: every door-open judges everyone inside
    ///   after the last wave: three closing pages (rule 3: floors travelled, at the rule box; Luck: Haunted / prosperous
    ///   endings; clock: impatient guests, reset).
    /// Every non-neutral LuckJudged adds a "why" card (unlucky floor: + stickers can relabel it), the first streak of 2+
    /// a streak card.
    ///
    /// COACH MARKS: each step freezes the game (IGameFlow.SetTutorialHold) under a dark overlay with a cut-out around
    /// its target; only the cards, the outline and the arrow sit above it.
    ///   action steps (OPEN / CLOSE / a floor): only the target is clickable through the cut-out, other clicks are
    ///     absorbed. The player's accepted command releases the hold in Core (TutorialHoldChanged) -> overlay gone, the
    ///     game runs until the next step. Keyboard commands work the same way.
    ///   explain steps (chips, why cards, streak, closing pages): "Click to continue" - a click anywhere or Enter/Space.
    /// The menu pause releases the hold (overlay hidden); it comes back after RESUME. If the hold is refused the
    /// prompts show without the freeze and explain steps advance after a few seconds (the old timed lesson).
    /// Cards sit in the empty right column under the HUD rule box - never over the building or the panel - one at a
    /// time, short enough for a 720-high canvas (<see cref="MaxCardHeight"/>).
    /// </summary>
    sealed class TutorialView
    {
        public enum Step { None = 0, GoTo, Board, Chips, Wait, Close, Floor, OpenThere, NotTheirStop, NextWave, Closing, Why, Streak }

        /// <summary>Waves the lesson is written for (the last one ends with the closing pages).</summary>
        public const int LessonWaves = 3;
        public const int ClosingPages = 3;
        /// <summary>Doors open, nobody boarded for this long (game time) -> CLOSE anyway (e.g. a guest who refuses this head count).</summary>
        public const float CloseFallback = 4f;
        /// <summary>Only when the hold is refused: how long an explain step stays before the lesson moves on.</summary>
        public const float ChipsLife = 4.5f, WhyLife = 7f, PageLife = 6f;
        const float Margin = 24f, FadeSpeed = 8f;
        /// <summary>Right column under the HUD's open rule box; while the day runs the cards follow
        /// <see cref="HudView.ColumnCardTop"/> (they move up under the folded "?" tab).</summary>
        public const float ColumnTop = HudView.RulesBottom + HudView.CardGap, ColumnW = 376f;
        /// <summary>Cards end this far above the bottom of a 720-high canvas (clear of the pause button).</summary>
        public const float BottomClear = 60f;
        public const float MaxCardHeight = UIStyle.RefHeight - ColumnTop - BottomClear;
        /// <summary>Cut-out margin around the target (smaller than half the panel's button gap: neighbours stay covered).</summary>
        public const float HolePad = 3f;
        // linear colour space: 0.66 alpha read as only ~40% darker in the WebGL build; 0.88 gives a clear coach-mark dim
        public static readonly Color DimColor = new Color(0.03f, 0.03f, 0.04f, 0.88f);

        struct Explain
        {
            public int Id;
            public Step Kind; // Why / Streak
            public LuckSource Source;
            public LuckVerdict Verdict;
            public int Number;
            public string Label;
        }

        readonly UIContext ctx;
        readonly ElevatorPanelView panel;
        readonly GuestBubblesView bubbles;
        readonly HudView hud;
        readonly RectTransform canvas, layer, dim, banner, why, highlight, arrow;
        readonly CanvasGroup highlightGroup, dimGroup;
        readonly Image[] dimPieces = new Image[4];
        readonly Image hole;
        readonly TextMeshProUGUI stepText, promptText, bannerHint, whyCaption, whyText, whyNote, whyHint, arrowGlyph;
        readonly Image bannerEdge, whyEdge;
        readonly InlineIcon promptIcon;
        readonly List<GuestId> waiting = new List<GuestId>();
        readonly LuckProfile[] profiles = new LuckProfile[16];
        readonly List<Explain> queue = new List<Explain>();

        // lesson state (reset when a tutorial day starts running)
        int wave, spawned, resolved, closingPage, nextId, streakValue;
        float streakMultiplier = 1f;
        bool chipsDone, closedThisWave, departedWithRiders, streakExplained;
        float sinceBoarded;

        // coach marks
        string holdKey, actedKey, refusedKey, explainKey, shownKey;
        bool holdExplain, shownExplain, coaching;
        float explainAge, dimAlpha, nudge;
        int lastAdvanceFrame = -1;

        // composed this frame
        Step step, whyStep;
        string shownText, targetName, markKey;
        bool markExplain, markWhy, hasTarget;
        float markLife;
        Rect targetRect;
        Sprite promptSprite;
        Color promptTint;

        public TutorialView(UIContext ctx, RectTransform canvas, ElevatorPanelView panel, GuestBubblesView bubbles, HudView hud)
        {
            this.ctx = ctx;
            this.canvas = canvas;
            this.panel = panel;
            this.bubbles = bubbles;
            this.hud = hud;
            layer = UIBuild.Stretch(UIBuild.Rect("Tutorial", canvas));

            // overlay: four dark rects around the cut-out (+ a clear catcher in the hole on explain steps)
            dim = UIBuild.Stretch(UIBuild.Rect("CoachDim", layer));
            dimGroup = dim.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < 4; i++) dimPieces[i] = Catcher("Dim" + i, DimColor);
            hole = Catcher("Hole", Color.clear);

            bannerEdge = UIBuild.Framed("Prompt", layer, UIStyle.Card, UIStyle.Highlight, 3f);
            banner = bannerEdge.rectTransform;
            UIBuild.Place(banner, new Vector2(1f, 1f), new Vector2(-Margin, -ColumnTop), new Vector2(ColumnW, 92f));
            Card(banner, out stepText, out promptText, UIStyle.Highlight);
            promptIcon = new InlineIcon(promptText);
            bannerHint = Hint(banner);

            whyEdge = UIBuild.Framed("Why", layer, UIStyle.Card, UIStyle.Lucky, 3f);
            why = whyEdge.rectTransform;
            UIBuild.Place(why, new Vector2(1f, 1f), new Vector2(-Margin, -ColumnTop), new Vector2(ColumnW, 92f));
            Card(why, out whyCaption, out whyText, UIStyle.Lucky);
            whyNote = UIBuild.Text("Note", why, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            whyNote.lineSpacing = 6f;
            whyHint = Hint(why);

            highlight = UIBuild.Rect("Highlight", layer);
            highlight.anchorMin = highlight.anchorMax = Vector2.zero;
            highlight.pivot = Vector2.zero;
            UIBuild.Outline("Ring", highlight, UIStyle.Highlight, 4f, 5f);
            highlightGroup = highlight.gameObject.AddComponent<CanvasGroup>();
            highlightGroup.blocksRaycasts = false;
            highlightGroup.interactable = false;

            // ▼ straight above the target on a dark plate: it points DOWN at one button, never sideways across a row
            // (HUD targets at the top of the screen get a ▶ / ◀ beside them instead)
            var plate = UIBuild.Framed("Arrow", layer, UIStyle.Dark, UIStyle.Highlight, 2f);
            arrow = plate.rectTransform;
            arrow.anchorMin = arrow.anchorMax = Vector2.zero;
            arrow.pivot = new Vector2(0.5f, 0f);
            arrow.sizeDelta = new Vector2(ArrowW, ArrowH);
            arrowGlyph = UIBuild.Text("Glyph", arrow, "▼", 24f, UIStyle.Highlight, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(arrowGlyph.rectTransform);

            Hide();
        }

        /// <summary>Arrow plate size; its bottom sits ArrowGap..ArrowGap+4 px above the target (clear of the 5 px outline).</summary>
        public const float ArrowW = 40f, ArrowH = 30f, ArrowGap = 8f;

        Image Catcher(string name, Color color)
        {
            var img = UIBuild.Box(name, dim, color, raycast: true);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            b.onClick.AddListener(OnDimClick);
            return img;
        }

        static void Card(RectTransform card, out TextMeshProUGUI small, out TextMeshProUGUI big, Color smallColor)
        {
            var fill = card.GetChild(0).gameObject;
            fill.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 10, 12);
            v.spacing = 4f;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            small = UIBuild.Text("Step", card, "", 12f, smallColor, TextAlignmentOptions.Left, bold: true);
            big = UIBuild.Text("Text", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            big.lineSpacing = 6f;
        }

        static TextMeshProUGUI Hint(RectTransform card)
        {
            var t = UIBuild.Text("Continue", card, "", 24f, UIStyle.Highlight, TextAlignmentOptions.Right, bold: true);
            t.gameObject.SetActive(false);
            return t;
        }

        bool TutorialDay => ctx.HasGame && ctx.Day.IsTutorial;
        bool HintsOn => ctx.Settings == null || ctx.Settings.ShowTutorialHints;
        bool Live => TutorialDay && ctx.Phase == DayPhase.Running;
        bool CoreHeld => ctx.Flow != null && ctx.Flow.TutorialHold;

        // ------------------------------------------------------------------ events (UIRoot forwards them)

        public void Reset()
        {
            wave = spawned = resolved = closingPage = 0;
            streakValue = 0;
            streakMultiplier = 1f;
            chipsDone = closedThisWave = departedWithRiders = streakExplained = false;
            sinceBoarded = 0f;
            queue.Clear();
            holdKey = actedKey = refusedKey = explainKey = shownKey = null; // Core releases its hold on every phase change
            holdExplain = shownExplain = coaching = false;
            explainAge = 0f;
            step = whyStep = Step.None;
            shownText = null;
            Hide();
        }

        public void OnPhaseChanged(DayPhaseChanged e)
        {
            // every tutorial day starts its lesson from wave 1 (a new run, a restart, the attract demo's leftovers)
            if (e.Current == DayPhase.Running && e.Previous != DayPhase.Running && TutorialDay) Reset();
            else if (e.Current == DayPhase.Title) Reset();
        }

        public void OnGuestSpawned(GuestSpawned e)
        {
            if (!Live) return;
            if (spawned == resolved) // the hotel was empty: a new wave
            {
                wave++;
                closedThisWave = departedWithRiders = false;
            }
            spawned++;
        }

        public void OnGuestDelivered(GuestDelivered e) { if (Live) resolved++; }
        public void OnGuestLeft(GuestLeft e) { if (Live) resolved++; }

        public void OnGuestBoarded(GuestBoarded e)
        {
            if (!Live) return;
            sinceBoarded = 0f;
        }

        public void OnDoorsClosed(DoorsClosed e)
        {
            if (!Live || e.Occupancy <= 0) return;
            closedThisWave = true;
            chipsDone = true; // the player acted: the chip lesson is over
        }

        public void OnDeparted(ElevatorDeparted e)
        {
            if (!Live || ctx.Elevator.Occupancy == 0) return;
            departedWithRiders = true;
            chipsDone = true;
        }

        public void OnDoorsOpened(DoorsOpened e)
        {
            if (Live) departedWithRiders = false;
        }

        public void OnLuckJudged(LuckJudged e)
        {
            if (!Live || !HintsOn || e.Verdict == LuckVerdict.Neutral) return;
            // riders judged together (three grandmas, one head count) are one card
            // (the streak card can sit between their verdicts: LuckJudged, LuckStreakChanged, LuckJudged, ...)
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var q = queue[i];
                if (q.Kind == Step.Why && q.Source == e.Source && q.Number == e.Number && q.Verdict == e.Verdict) return;
            }
            string label = e.Number >= 0 ? e.Number.ToString(CultureInfo.InvariantCulture)
                : ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) ? ctx.Building.GetLabel(e.Floor).Text : "?";
            queue.Add(new Explain { Id = ++nextId, Kind = Step.Why, Source = e.Source, Verdict = e.Verdict, Number = e.Number, Label = label });
        }

        public void OnLuckStreakChanged(LuckStreakChanged e)
        {
            streakValue = e.Streak;
            streakMultiplier = e.TipMultiplier;
            if (!Live || !HintsOn || streakExplained || e.Streak < 2) return;
            streakExplained = true;
            queue.Add(new Explain { Id = ++nextId, Kind = Step.Streak, Number = e.Streak });
        }

        /// <summary>Follows Core's hold at once (a command released it: no stale overlay for a frame).</summary>
        public void OnTutorialHoldChanged(TutorialHoldChanged e)
        {
            if (e.Held) return;
            coaching = false;
            dimGroup.blocksRaycasts = false;
            hole.raycastTarget = false;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt, float time)
        {
            bool held = CoreHeld;
            // our hold went away without us: the player's command went through (or the menu pause / day end took over)
            if (holdKey != null && !held)
            {
                string k = holdKey;
                bool explain = holdExplain;
                holdKey = null;
                if (Live && !ctx.Paused)
                {
                    if (explain) Advance(k);
                    else { actedKey = k; step = Step.None; shownText = null; } // done: no stale "press X" line
                }
            }
            if (Live && !held) sinceBoarded += dt; // game time: frozen while held
            if (!(Live && HintsOn) || ctx.Paused) { ReleaseHold(); Hide(); return; }

            Compose();
            PlaceColumn();

            // one hold per coach mark: switch marks without letting the game run, release when there is none
            string want = markKey;
            if (actedKey != null && want != actedKey) actedKey = null;
            if (refusedKey != null && want != refusedKey) refusedKey = null;
            if (holdKey != null)
            {
                if (want == null) ReleaseHold();
                else { holdKey = want; holdExplain = markExplain; }
            }
            else if (want != null && want != actedKey && want != refusedKey && ctx.Flow != null)
            {
                if (ctx.Flow.SetTutorialHold(true)) { holdKey = want; holdExplain = markExplain; }
                else refusedKey = want; // not a tutorial day / paused / stubbed: the prompt shows without the freeze
            }
            coaching = holdKey != null && CoreHeld;

            if (markExplain)
            {
                if (explainKey != markKey) { explainKey = markKey; explainAge = 0f; }
                explainAge += dt;
                if (ContinuePressed()) Advance(markKey);
                else if (!coaching && explainAge >= markLife) Advance(markKey); // hold refused: the timed lesson
            }
            else explainKey = null;
            shownKey = markKey;
            shownExplain = markExplain;
            Render(dt, time);
        }

        bool ContinuePressed()
        {
            if (Time.frameCount == lastAdvanceFrame) return false; // a click / command already moved on this frame
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
        }

        void ReleaseHold()
        {
            if (holdKey == null) return;
            holdKey = null;
            coaching = false;
            if (ctx.Flow != null && ctx.Flow.TutorialHold) ctx.Flow.SetTutorialHold(false);
        }

        /// <summary>Dismisses the explain step <paramref name="key"/> (no-op when it is not the current one any more).</summary>
        void Advance(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool done = false;
            if (key[0] == 'q')
            {
                int id = int.Parse(key.Substring(1), CultureInfo.InvariantCulture);
                if (queue.Count > 0 && queue[0].Id == id) { queue.RemoveAt(0); done = true; }
            }
            else if (key.StartsWith("chips", System.StringComparison.Ordinal)) { done = !chipsDone; chipsDone = true; }
            else if (key.StartsWith("closing", System.StringComparison.Ordinal))
            {
                int page = int.Parse(key.Substring(7), CultureInfo.InvariantCulture);
                if (page == closingPage) { closingPage++; done = true; }
            }
            if (!done) return;
            if (key[0] != 'q') { step = Step.None; shownText = null; } // read: no stale line while the game moves on
            lastAdvanceFrame = Time.frameCount;
            explainKey = null;
            explainAge = 0f;
        }

        /// <summary>Any overlay click: continues an explain step; on an action step it is absorbed (the arrow nudges).</summary>
        void OnDimClick()
        {
            if (!coaching) return;
            if (shownExplain)
            {
                if (Time.frameCount == lastAdvanceFrame) return;
                ctx.Sound(UiSoundKind.Click);
                Advance(shownKey);
            }
            else
            {
                ctx.Sound(UiSoundKind.Error);
                nudge = 0.5f;
            }
        }

        /// <summary>The mark for this frame: queued explain card first, else the live lesson line.</summary>
        void Compose()
        {
            hasTarget = false;
            targetName = null;
            promptSprite = null;
            markKey = null;
            markExplain = markWhy = false;
            markLife = 0f;
            if (queue.Count > 0)
            {
                var q = queue[0];
                markKey = "q" + q.Id.ToString(CultureInfo.InvariantCulture);
                markExplain = markWhy = true;
                markLife = WhyLife;
                whyStep = q.Kind;
                Target(q.Kind == Step.Streak ? hud.StreakBox : hud.LuckBox);
                FillWhy(q);
                return;
            }
            Step s = Prompt(out string text, out bool keep);
            if (text != null) { step = s; shownText = text; }
            else if (!keep) { step = Step.None; shownText = null; }
            if (text == null) return; // a transition (car moving, doors animating, guests walking): keep the line, no mark
            switch (s)
            {
                case Step.Chips:
                    markKey = "chips" + wave.ToString(CultureInfo.InvariantCulture);
                    markExplain = true;
                    markLife = ChipsLife;
                    break;
                case Step.Closing:
                    markKey = "closing" + closingPage.ToString(CultureInfo.InvariantCulture);
                    markExplain = true;
                    markLife = PageLife;
                    break;
                case Step.GoTo: case Step.Board: case Step.Close: case Step.Floor: case Step.OpenThere: case Step.NotTheirStop:
                    if (hasTarget) markKey = s + "|" + targetName + "|" + wave.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        void FillWhy(Explain q)
        {
            Color color;
            if (q.Kind == Step.Streak)
            {
                int n = streakValue > 0 ? streakValue : q.Number;
                UIBuild.Set(whyText, ctx.F(UIStrings.TutStreak, n, UIFormat.Multiplier(streakMultiplier)));
                UIBuild.Set(whyCaption, ctx.S(UIStrings.Tips));
                color = UIStyle.Lucky;
                SetNote(null);
            }
            else
            {
                bool lucky = q.Verdict == LuckVerdict.Lucky;
                WhyTemplate(q.Source, lucky, out var template, out var caption, out var note);
                UIBuild.Set(whyText, ctx.F(template, q.Label));
                UIBuild.Set(whyCaption, ctx.S(caption));
                color = lucky ? UIStyle.Lucky : UIStyle.Unlucky;
                SetNote(note.HasValue ? ctx.S(note.Value) : null);
            }
            whyText.color = whyCaption.color = whyEdge.color = color;
        }

        /// <summary>Why-card text per rule: 1 floor number (unlucky: + stickers), 2 head count, 3 floors travelled (+ the ↕ chip).</summary>
        static void WhyTemplate(LuckSource source, bool lucky, out Loc template, out Loc caption, out Loc? note)
        {
            note = null;
            switch (source)
            {
                case LuckSource.Occupancy:
                    template = lucky ? UIStrings.TutWhyLuckyCount : UIStrings.TutWhyUnluckyCount;
                    caption = UIStrings.RuleClose;
                    break;
                case LuckSource.RideDistance:
                    template = lucky ? UIStrings.TutWhyLuckyDistance : UIStrings.TutWhyUnluckyDistance;
                    caption = UIStrings.RuleDistance;
                    note = UIStrings.TutWhyDistanceNote;
                    break;
                default:
                    template = lucky ? UIStrings.TutWhyLuckyFloor : UIStrings.TutWhyUnluckyFloor;
                    caption = UIStrings.RuleOpen;
                    if (!lucky) note = UIStrings.TutWhySticker;
                    break;
            }
        }

        void SetNote(string note)
        {
            bool on = !string.IsNullOrEmpty(note);
            if (on) UIBuild.Set(whyNote, note);
            if (whyNote.gameObject.activeSelf != on) whyNote.gameObject.SetActive(on);
        }

        void Render(float dt, float time)
        {
            bool showBanner = !markWhy && shownText != null;
            Show(banner, showBanner);
            if (showBanner)
            {
                UIBuild.Set(promptText, shownText);
                promptIcon.Set(promptSprite, promptTint);
                UIBuild.Set(stepText, step == Step.Closing
                    ? ctx.S(UIStrings.TutDoneTitle) + "  " + Mathf.Min(closingPage + 1, ClosingPages) + "/" + ClosingPages
                    : string.Format(ctx.S(UIStrings.TutWave), Mathf.Clamp(wave, 1, LessonWaves), LessonWaves));
            }
            else promptIcon.Set(null, Color.white);
            promptIcon.Place();
            Show(why, markWhy);

            bool hint = coaching && markExplain;
            SetHint(bannerHint, hint && showBanner);
            SetHint(whyHint, hint && markWhy);
            if (hint)
            {
                var h = markWhy ? whyHint : bannerHint;
                UIBuild.Set(h, ctx.S(UIStrings.TutContinue));
                h.alpha = 0.6f + 0.4f * Mathf.Sin(time * 5f);
            }

            // overlay
            dimAlpha = Mathf.MoveTowards(dimAlpha, coaching ? 1f : 0f, FadeSpeed * dt);
            if (coaching && dimAlpha <= 0f) dimAlpha = 0.01f;
            Show(dim, dimAlpha > 0f);
            dimGroup.alpha = dimAlpha;
            dimGroup.blocksRaycasts = coaching;
            hole.raycastTarget = coaching && markExplain;
            if (dimAlpha > 0f) LayoutDim();

            Show(highlight, hasTarget);
            Show(arrow, hasTarget);
            if (nudge > 0f) nudge -= dt;
            if (hasTarget)
            {
                highlight.anchoredPosition = targetRect.min;
                highlight.sizeDelta = targetRect.size;
                highlightGroup.alpha = nudge > 0f ? 1f : 0.55f + 0.45f * Mathf.Sin(time * 7f);
                float bob = 2f + 2f * Mathf.Sin(time * 7f) + (nudge > 0f ? 6f * Mathf.Abs(Mathf.Sin(nudge * 40f)) : 0f); // toward the target, never away
                PlaceArrow(bob);
                arrow.SetAsLastSibling();
            }
        }

        static void SetHint(TextMeshProUGUI t, bool on)
        {
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        /// <summary>▼ above the target when there is room; HUD targets in the right column / at the top get ▶ or ◀ beside them.</summary>
        void PlaceArrow(float bob)
        {
            var size = canvas.rect.size;
            // the right-hand HUD boxes (Luck, tips, streak - the streak box pops wider out of its right edge)
            bool rightColumn = targetRect.xMax >= size.x - Margin - 8f && targetRect.yMin >= size.y - ColumnTop;
            bool roomAbove = targetRect.yMax + ArrowGap + 4f + ArrowH <= size.y;
            if (roomAbove && !rightColumn)
            {
                UIBuild.Set(arrowGlyph, "▼");
                arrow.pivot = new Vector2(0.5f, 0f);
                arrow.sizeDelta = new Vector2(ArrowW, ArrowH);
                arrow.anchoredPosition = new Vector2(targetRect.center.x, targetRect.yMax + ArrowGap + bob);
            }
            else if (targetRect.xMin - ArrowGap - 4f - ArrowW >= 0f)
            {
                UIBuild.Set(arrowGlyph, "▶");
                arrow.pivot = new Vector2(1f, 0.5f);
                arrow.sizeDelta = new Vector2(ArrowH + 4f, ArrowW - 6f);
                arrow.anchoredPosition = new Vector2(targetRect.xMin - ArrowGap - bob, targetRect.center.y);
            }
            else
            {
                UIBuild.Set(arrowGlyph, "◀");
                arrow.pivot = new Vector2(0f, 0.5f);
                arrow.sizeDelta = new Vector2(ArrowH + 4f, ArrowW - 6f);
                arrow.anchoredPosition = new Vector2(targetRect.xMax + ArrowGap + bob, targetRect.center.y);
            }
        }

        /// <summary>Four rects around the cut-out (the target + <see cref="HolePad"/>), the clear catcher inside it.</summary>
        void LayoutDim()
        {
            var size = canvas.rect.size;
            Rect h = hasTarget ? CoachHole : new Rect(0f, 0f, 0f, 0f);
            Piece(dimPieces[0], 0f, 0f, size.x, h.yMin);           // below
            Piece(dimPieces[1], 0f, h.yMax, size.x, size.y);       // above
            Piece(dimPieces[2], 0f, h.yMin, h.xMin, h.yMax);       // left
            Piece(dimPieces[3], h.xMax, h.yMin, size.x, h.yMax);   // right
            Piece(hole, h.xMin, h.yMin, h.xMax, h.yMax);
        }

        Rect CoachHole
        {
            get
            {
                var size = canvas.rect.size;
                return Rect.MinMaxRect(Mathf.Clamp(targetRect.xMin - HolePad, 0f, size.x), Mathf.Clamp(targetRect.yMin - HolePad, 0f, size.y),
                    Mathf.Clamp(targetRect.xMax + HolePad, 0f, size.x), Mathf.Clamp(targetRect.yMax + HolePad, 0f, size.y));
            }
        }

        static void Piece(Image img, float x0, float y0, float x1, float y1)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x0, y0);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }

        void Target(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return;
            targetRect = UIBuild.CanvasRect(canvas, rt);
            targetName = rt.name;
            hasTarget = true;
        }

        /// <summary>
        /// The line for the live situation (null = nothing to say; <paramref name="hold"/> = a transition, keep the
        /// last line) and its target.
        /// </summary>
        Step Prompt(out string text, out bool hold)
        {
            text = null;
            hold = false;
            if (wave == 0 || ctx.Guests == null) return Step.None; // before the first guest
            if (spawned == resolved)
            {
                if (wave >= LessonWaves || AllSpawned())
                {
                    if (closingPage >= ClosingPages) return Step.None; // read: the summary comes
                    text = ClosingPage(closingPage);
                    if (closingPage == 0) hud.EnsureRulesOpen(); // the page about the box: unfold it even if the player folded it
                    Target(closingPage == 0 ? hud.RulesBox : closingPage == 1 ? hud.LuckBox : hud.DayBox);
                    return Step.Closing;
                }
                text = ctx.S(UIStrings.TutNextWave);
                return Step.NextWave;
            }

            var e = ctx.Elevator;
            bool doorsOpen = e.Doors == DoorState.Open || e.Doors == DoorState.Opening;
            bool idle = e.Motion == ElevatorMotion.Idle && !e.HasTarget;
            hold = true;

            if (e.Occupancy == 0)
            {
                if (!FindWaitingFloor(out var floor, out int count)) return Step.None; // walking in / out
                string label = ctx.Building.GetLabel(floor).Text;
                if (e.CurrentFloor == floor && idle)
                {
                    if (doorsOpen) return Step.None; // boarding now
                    Target(panel.OpenRect);
                    text = count > 1 ? ctx.F(UIStrings.TutBoardHereMany, label, count) : ctx.F(UIStrings.TutBoardHereOne, label, count);
                    return Step.Board;
                }
                if (!idle) return Step.None; // already on the way
                Target(panel.FloorButtonRect(floor));
                text = ctx.F(UIStrings.TutBoardGo, label);
                return Step.GoTo;
            }

            if (doorsOpen)
            {
                if (e.Doors == DoorState.Opening) return Step.None;
                if (RiderGetsOffAt(e.CurrentFloor)) return Step.None; // someone is still stepping out here
                if (wave == 1 && !chipsDone && !closedThisWave)
                {
                    if (!bubbles.RiderChipsRect(out var chips)) return Step.None;
                    targetRect = chips;
                    targetName = "Chips";
                    hasTarget = true;
                    text = ChipsLine();
                    return Step.Chips;
                }
                int group = GroupSize(out var groupVerdict);
                if (!e.IsFull && sinceBoarded < CloseFallback && SomeoneCanStillBoard())
                {
                    if (wave < 2 || group < 2) return Step.None;
                    text = ctx.F(groupVerdict == LuckVerdict.Lucky ? UIStrings.TutWaitAllLucky : UIStrings.TutWaitAll, group);
                    return Step.Wait;
                }
                Target(panel.CloseRect);
                var v = Preview(e.Occupancy);
                if (closedThisWave) text = ctx.S(UIStrings.TutCloseOn);
                else if (v == LuckVerdict.Lucky) text = ctx.F(UIStrings.TutCloseLucky, e.Occupancy);
                else if (v == LuckVerdict.Unlucky) text = ctx.F(UIStrings.TutCloseUnlucky, e.Occupancy);
                else text = ctx.F(UIStrings.TutCloseNeutral, e.Occupancy);
                return Step.Close;
            }

            if (e.Doors != DoorState.Closed || !idle) return Step.None; // closing / moving
            if (RiderGetsOffAt(e.CurrentFloor))
            {
                Target(panel.OpenRect);
                text = e.Occupancy > 1 && SomeoneStaysOn(e.CurrentFloor)
                    ? ctx.F(UIStrings.TutOpenAll, ctx.Building.GetLabel(e.CurrentFloor).Text)
                    : ctx.S(UIStrings.TutOpenThere);
                return Step.OpenThere;
            }
            if (!FirstRiderDestination(out var dest, out var destId)) return Step.None;
            Target(panel.FloorButtonRect(dest));
            string place = InlineIcon.Markup(ctx, destId, out promptSprite, out promptTint);
            string destLabel = ctx.Building.GetLabel(dest).Text;
            if (departedWithRiders)
            {
                text = ctx.F(UIStrings.TutNotTheirStop, destLabel, place);
                return Step.NotTheirStop;
            }
            text = ctx.F(DistinctStops() > 1 ? UIStrings.TutFloorTwo : UIStrings.TutFloor, destLabel, place);
            return Step.Floor;
        }

        /// <summary>Closing page <paramref name="page"/>: 0 = rule 3 (floors travelled), 1 = Luck (endings), 2 = the clock + the practice reset.</summary>
        string ClosingPage(int page)
        {
            if (page == 0) return ctx.S(UIStrings.TutEndDistance);
            if (page == 1)
            {
                int lastDay = ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount;
                int prosperous = ctx.Data != null ? ctx.Data.Rules.prosperousEndingLuck : 70;
                return ctx.F(UIStrings.TutEndLuck, lastDay, prosperous);
            }
            return ctx.S(UIStrings.TutEndClock) + "\n" + ctx.S(UIStrings.PracticeReset);
        }

        bool AllSpawned()
        {
            if (!ctx.TryGetDay(out var d) || d.spawns == null || d.spawns.Length == 0) return false;
            return spawned >= d.spawns.Length;
        }

        string ChipsLine()
        {
            var riders = ctx.Elevator.Riders;
            LuckProfile luck = null;
            for (int i = 0; i < riders.Count && luck == null; i++)
                if (ctx.Guests.TryGet(riders[i], out var g) && !g.HideNumbers) luck = g.Luck;
            luck = luck ?? LuckProfile.None;
            return ctx.F(UIStrings.TutChips, Join(luck.Lucky, UIFormat.LuckyHex), Join(luck.Unlucky, UIFormat.UnluckyHex));
        }

        static string Join(NumberSet numbers, string hex)
        {
            if (numbers == null || numbers.Count == 0) return "-";
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=").Append(hex).Append('>');
            for (int i = 0; i < numbers.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(numbers[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.Append("</color>").ToString();
        }

        /// <summary>What the riders would think of <paramref name="count"/> if the doors closed now.</summary>
        LuckVerdict Preview(int count)
        {
            int n = 0;
            var riders = ctx.Elevator.Riders;
            for (int i = 0; i < riders.Count && n < profiles.Length; i++)
                if (ctx.Guests.TryGet(riders[i], out var g) && !g.HideNumbers) profiles[n++] = g.Luck;
            var v = UIFormat.OccupancyPreview(count, profiles, n);
            for (int i = 0; i < n; i++) profiles[i] = null;
            return v;
        }

        /// <summary>Riders + guests still getting in here, and what that full group would think of its own size.</summary>
        int GroupSize(out LuckVerdict verdict)
        {
            var e = ctx.Elevator;
            int n = 0, count = e.Occupancy;
            var riders = e.Riders;
            for (int i = 0; i < riders.Count && n < profiles.Length; i++)
                if (ctx.Guests.TryGet(riders[i], out var g) && !g.HideNumbers) profiles[n++] = g.Luck;
            var active = ctx.Guests.ActiveGuests;
            for (int i = 0; i < active.Count; i++)
            {
                if (!ctx.Guests.TryGet(active[i], out var g)) continue;
                bool boarding = g.State == GuestState.Boarding;
                bool waitingHere = g.State == GuestState.Waiting && g.Floor == e.CurrentFloor && g.DestinationFloor != e.CurrentFloor;
                if (!boarding && !waitingHere) continue;
                count++;
                if (n < profiles.Length && !g.HideNumbers) profiles[n++] = g.Luck;
            }
            count = Mathf.Min(count, e.Capacity);
            verdict = UIFormat.OccupancyPreview(count, profiles, n);
            for (int i = 0; i < n; i++) profiles[i] = null;
            return count;
        }

        bool FindWaitingFloor(out FloorIndex floor, out int count)
        {
            floor = ctx.Elevator.CurrentFloor;
            count = ctx.Guests.GetWaiting(floor, waiting);
            if (count > 0) return true;
            for (int f = 0; f < ctx.Building.FloorCount; f++)
            {
                count = ctx.Guests.GetWaiting(new FloorIndex(f), waiting);
                if (count > 0) { floor = new FloorIndex(f); return true; }
            }
            return false;
        }

        /// <summary>A guest on this floor (doors open here) who would still get in: Waiting, not already there, car not full.</summary>
        bool SomeoneCanStillBoard()
        {
            var e = ctx.Elevator;
            if (ctx.Guests == null || e.IsFull) return false;
            var active = ctx.Guests.ActiveGuests;
            for (int i = 0; i < active.Count; i++)
                if (ctx.Guests.TryGet(active[i], out var g) && g.State == GuestState.Boarding) return true; // walking in now
            ctx.Guests.GetWaiting(e.CurrentFloor, waiting);
            for (int i = 0; i < waiting.Count; i++)
                if (ctx.Guests.TryGet(waiting[i], out var g) && g.State == GuestState.Waiting && g.DestinationFloor != e.CurrentFloor) return true;
            return false;
        }

        bool RiderGetsOffAt(FloorIndex floor)
        {
            var riders = ctx.Elevator.Riders;
            for (int i = 0; i < riders.Count; i++)
                if (ctx.Guests.TryGet(riders[i], out var info) && info.DestinationFloor == floor) return true;
            return false;
        }

        bool SomeoneStaysOn(FloorIndex floor)
        {
            var riders = ctx.Elevator.Riders;
            for (int i = 0; i < riders.Count; i++)
                if (ctx.Guests.TryGet(riders[i], out var info) && info.DestinationFloor != floor) return true;
            return false;
        }

        int DistinctStops()
        {
            var riders = ctx.Elevator.Riders;
            int a = -1, n = 0;
            for (int i = 0; i < riders.Count; i++)
            {
                if (!ctx.Guests.TryGet(riders[i], out var info)) continue;
                int d = info.DestinationFloor.Value;
                if (n == 0) { a = d; n = 1; }
                else if (d != a) return 2;
            }
            return n;
        }

        bool FirstRiderDestination(out FloorIndex floor, out DestinationId destination)
        {
            floor = default;
            destination = default;
            // nearest stop first (Day 0 wave 3: floor 4 before 7, so grandma's 3-floor ride shows a lucky rule-3 verdict)
            var riders = ctx.Elevator.Riders;
            int here = ctx.Elevator.CurrentFloor.Value, best = int.MaxValue;
            for (int i = 0; i < riders.Count; i++)
            {
                if (!ctx.Guests.TryGet(riders[i], out var info)) continue;
                if (info.DestinationFloor == ctx.Elevator.CurrentFloor) continue;
                int d = System.Math.Abs(info.DestinationFloor.Value - here);
                if (d >= best) continue;
                best = d;
                floor = info.DestinationFloor;
                destination = info.Destination;
            }
            return best != int.MaxValue;
        }

        static void Show(RectTransform rt, bool on)
        {
            if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
        }

        void Hide()
        {
            Show(banner, false);
            Show(why, false);
            Show(highlight, false);
            Show(arrow, false);
            Show(dim, false);
            dimAlpha = 0f;
            dimGroup.blocksRaycasts = false;
            coaching = false;
            hasTarget = false;
            targetName = null;
            shownKey = null;
            shownExplain = false;
        }

        public void RefreshLanguage() { /* every card is rebuilt each tick from ctx.S / ctx.F */ }

        /// <summary>Prompt / why cards hang right under the HUD's right column (rule box or its tab).</summary>
        void PlaceColumn()
        {
            var p = new Vector2(-Margin, -hud.ColumnCardTop);
            if (banner.anchoredPosition != p) banner.anchoredPosition = p;
            if (why.anchoredPosition != p) why.anchoredPosition = p;
        }

        // ------------------------------------------------------------------ test hooks

        public Step CurrentStep => why.gameObject.activeSelf ? whyStep : banner.gameObject.activeSelf ? step : Step.None;
        public int Wave => wave;
        public string PromptText => banner.gameObject.activeSelf ? promptText.text : null;
        public string WhyText => why.gameObject.activeSelf ? whyText.text + (whyNote.gameObject.activeSelf ? "\n" + whyNote.text : "") : null;
        public string TargetName => highlight.gameObject.activeSelf ? targetName : null;
        public bool PromptIconVisible => banner.gameObject.activeSelf && promptIcon.Visible;
        public Sprite PromptIconSprite => promptIcon.Sprite;

        /// <summary>Canvas rects of the visible cards (prompt, why) - empty rects when hidden.</summary>
        public Rect PromptRect => banner.gameObject.activeSelf ? UIBuild.CanvasRect(canvas, banner) : default;
        public Rect WhyRect => why.gameObject.activeSelf ? UIBuild.CanvasRect(canvas, why) : default;
        public Rect TargetRect => highlight.gameObject.activeSelf ? targetRect : default;
        public Rect ArrowRect => arrow.gameObject.activeSelf ? UIBuild.CanvasRect(canvas, arrow) : default;

        /// <summary>The coach overlay is up and absorbing clicks (the game is held).</summary>
        public bool CoachVisible => dim.gameObject.activeSelf && coaching && dimGroup.blocksRaycasts;
        public Rect CoachHoleRect => CoachVisible && hasTarget ? CoachHole : default;
        /// <summary>An explain step waits for "click to continue" (hint shown).</summary>
        public bool AwaitingContinue => CoachVisible && shownExplain;
        public string ContinueHint => bannerHint.gameObject.activeInHierarchy ? bannerHint.text : whyHint.gameObject.activeInHierarchy ? whyHint.text : null;
        /// <summary>A click on the overlay (outside any card), as the player would.</summary>
        public void ClickOverlay() => dimPieces[1].GetComponent<Button>().onClick.Invoke();
        public int QueuedExplains => queue.Count;

        /// <summary>One sample card per lesson line (worst-case numbers/names), for the fits-the-screen test.</summary>
        public sealed class CardSample
        {
            public bool Why;
            public string Caption, Body, Note;
            public override string ToString() => (Why ? "why: " : "prompt: ") + Body;
        }

        public List<CardSample> SampleCards()
        {
            var list = new List<CardSample>();
            string label = "88";
            int count = 8;
            string place = "?";
            if (ctx.Data != null)
            {
                string longest = null;
                foreach (var d in ctx.Data.Destinations)
                {
                    string name = ctx.DestinationName(d.Id);
                    if (longest == null || name.Length > longest.Length) { longest = name; place = InlineIcon.Markup(ctx, d.Id, out _, out _); }
                }
            }
            string waveCaption = string.Format(ctx.S(UIStrings.TutWave), LessonWaves, LessonWaves);
            void P(string body) => list.Add(new CardSample { Caption = waveCaption, Body = body });
            P(ctx.F(UIStrings.TutBoardHereOne, label, 1));
            P(ctx.F(UIStrings.TutBoardHereMany, label, count));
            P(ctx.F(UIStrings.TutBoardGo, label));
            P(ctx.F(UIStrings.TutChips, Join(NumberSet.Of(7, 8, 9), UIFormat.LuckyHex), Join(NumberSet.Of(4, 13), UIFormat.UnluckyHex)));
            P(ctx.F(UIStrings.TutCloseNeutral, count));
            P(ctx.F(UIStrings.TutCloseLucky, count));
            P(ctx.F(UIStrings.TutCloseUnlucky, count));
            P(ctx.S(UIStrings.TutCloseOn));
            P(ctx.F(UIStrings.TutWaitAll, count));
            P(ctx.F(UIStrings.TutWaitAllLucky, count));
            P(ctx.F(UIStrings.TutFloor, label, place));
            P(ctx.F(UIStrings.TutFloorTwo, label, place));
            P(ctx.S(UIStrings.TutOpenThere));
            P(ctx.F(UIStrings.TutOpenAll, label));
            P(ctx.F(UIStrings.TutNotTheirStop, label, place));
            P(ctx.S(UIStrings.TutNextWave));
            string done = ctx.S(UIStrings.TutDoneTitle) + "  " + ClosingPages + "/" + ClosingPages;
            for (int p = 0; p < ClosingPages; p++) list.Add(new CardSample { Caption = done, Body = ClosingPage(p) });
            foreach (var source in new[] { LuckSource.FloorLabel, LuckSource.Occupancy, LuckSource.RideDistance })
                foreach (bool lucky in new[] { true, false })
                {
                    WhyTemplate(source, lucky, out var t, out var caption, out var note);
                    list.Add(new CardSample
                    {
                        Why = true, Caption = ctx.S(caption), Body = ctx.F(t, source == LuckSource.RideDistance ? "17" : label),
                        Note = note.HasValue ? ctx.S(note.Value) : null,
                    });
                }
            list.Add(new CardSample { Why = true, Caption = ctx.S(UIStrings.Tips), Body = ctx.F(UIStrings.TutStreak, 88, UIFormat.Multiplier(1.15f)) });
            return list;
        }

        /// <summary>Lays <paramref name="s"/> out in its card (with the continue hint) and returns the card's canvas rect.</summary>
        public Rect MeasureCard(CardSample s)
        {
            var card = s.Why ? why : banner;
            Show(card, true);
            if (s.Why) { whyCaption.text = s.Caption; whyText.text = s.Body; SetNote(s.Note); }
            else { stepText.text = s.Caption; promptText.text = s.Body; }
            var hint = s.Why ? whyHint : bannerHint;
            hint.text = ctx.S(UIStrings.TutContinue);
            SetHint(hint, true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            var r = UIBuild.CanvasRect(canvas, card);
            Show(card, false);
            SetHint(hint, false);
            return r;
        }
    }
}
