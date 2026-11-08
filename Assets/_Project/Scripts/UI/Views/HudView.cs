using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Always-on HUD: day + clock (IDayQuery), Luck meter (animated on LuckChanged, ghost bar on loss, +/- float),
    /// tips (TipsChanged), the lucky-streak readout under the tips (LuckStreakChanged: hidden at 0, pops on each step,
    /// "Streak broken!" for a moment), the two-line rule reminder, and the language toggle (IGameSettings.UseKorean).
    /// </summary>
    sealed class HudView
    {
        const float MeterW = 344f, ClockW = 118f, PracticeW = 150f;
        /// <summary>Streak slot under the tips box; the rule box sits under it (ends <see cref="RulesBottom"/> px from the top).</summary>
        public const float StreakTop = 148f, StreakH = 38f, RulesTop = 192f, RulesH = 132f, RulesBottom = RulesTop + RulesH;
        /// <summary>Friday the 13th: the rule box gets one more (red) line and the badge sits under it.</summary>
        public const float Friday13LineH = 22f, BadgeGap = 8f, BadgeH = 40f;
        /// <summary>Star night badge: two 24px lines ("STAR NIGHT" / "7 lucky for all") - the Korean line is too long for one.</summary>
        public const float StarBadgeH = 72f;
        const float BrokenLife = 1.6f, PopLife = 0.35f;
        readonly UIContext ctx;
        readonly TextMeshProUGUI dayText, clockText, luckCaption, luckValue, luckDelta, tipsCaption, tipsValue, tipsDelta;
        readonly TextMeshProUGUI rulesTitle, ruleOpen, ruleClose, ruleDistance, ruleFriday, ruleStar, ruleLegend, fridayText, starText;
        readonly RectTransform luckFill, luckGhost, rulesBox, dayBox, luckBox, tipsBox, streakBox, fridayBadge, starBadge;
        readonly Image rulesEdge, rulesFill, fridayEdge, starEdge;
        bool friday, starNight;
        /// <summary>Star night: pale starlight ink, violet edge, deep-blue box.</summary>
        public static readonly Color StarInk = UIStyle.Hex("#DCD4FF"), StarEdge = UIStyle.Hex("#8C6CFF"), StarBoxFill = new Color(0.03f, 0.03f, 0.11f, 0.95f);
        /// <summary>Friday line ink: a lighter red that reads on the dark box (UIStyle.Unlucky is the edge).</summary>
        public static readonly Color FridayInk = UIStyle.Hex("#FF7A7A");
        static readonly Color FridayBoxFill = new Color(0.07f, 0.015f, 0.02f, 0.97f);
        readonly TextMeshProUGUI streakText;
        readonly Image streakEdge;
        int streak;
        float streakMultiplier = 1f, brokenTimer, popTimer;
        readonly Image luckFillImage;
        readonly UIBuild.ButtonParts language;

        float shownLuck = -1f, ghostLuck = -1f, ghostHold;
        float luckDeltaTimer, tipsDeltaTimer;
        int shownTips = int.MinValue;

        public HudView(UIContext ctx, RectTransform canvas, System.Action onLanguage)
        {
            this.ctx = ctx;

            // day + clock (top-left, above the panel)
            dayBox = UIBuild.Framed("DayBox", canvas, UIStyle.Box, UIStyle.CardEdge, 2f).rectTransform;
            UIBuild.Place(dayBox, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(ElevatorPanelView.Width, 64f));
            dayText = UIBuild.Text("Day", dayBox, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(dayText.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(104f, 40f));
            clockText = UIBuild.Text("Clock", dayBox, "", 36f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(clockText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(ClockW, 48f));

            // luck meter (top-right)
            luckBox = UIBuild.Framed("LuckBox", canvas, UIStyle.Box, UIStyle.CardEdge, 2f).rectTransform;
            UIBuild.Place(luckBox, new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(MeterW + 32f, 76f));
            luckCaption = UIBuild.Text("Caption", luckBox, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(luckCaption.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -6f), new Vector2(120f, 28f));
            luckValue = UIBuild.Text("Value", luckBox, "", 24f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(luckValue.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -4f), new Vector2(140f, 30f));
            luckDelta = UIBuild.Text("Delta", luckBox, "", 24f, UIStyle.Good, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(luckDelta.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(100f, 30f));
            var track = UIBuild.Box("Track", luckBox, new Color(0f, 0f, 0f, 0.8f));
            UIBuild.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(MeterW, 24f));
            luckGhost = UIBuild.Box("Ghost", track.transform, new Color(1f, 0.92f, 0.9f, 0.85f)).rectTransform;
            Bar(luckGhost);
            luckFillImage = UIBuild.Box("Fill", track.transform, UIStyle.Lucky);
            luckFill = luckFillImage.rectTransform;
            Bar(luckFill);

            // tips (under the luck meter)
            tipsBox = UIBuild.Framed("TipsBox", canvas, UIStyle.Box, UIStyle.CardEdge, 2f).rectTransform;
            UIBuild.Place(tipsBox, new Vector2(1f, 1f), new Vector2(-24f, -98f), new Vector2(MeterW + 32f, 44f));
            tipsCaption = UIBuild.Text("Caption", tipsBox, "", 24f, UIStyle.Good, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(tipsCaption.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(120f, 32f));
            tipsValue = UIBuild.Text("Value", tipsBox, "", 24f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(tipsValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(160f, 34f));
            tipsDelta = UIBuild.Text("Delta", tipsBox, "", 24f, UIStyle.Good, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(tipsDelta.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(100f, 32f));

            // lucky streak (right, under tips; hidden at 0)
            streakEdge = UIBuild.Framed("StreakBox", canvas, UIStyle.Box, UIStyle.Lucky, 2f);
            streakBox = streakEdge.rectTransform;
            UIBuild.Place(streakBox, new Vector2(1f, 1f), new Vector2(-24f, -StreakTop), new Vector2(MeterW + 32f, StreakH));
            streakBox.pivot = new Vector2(1f, 0.5f); // pops out of its right edge: never past the screen edge
            streakBox.anchoredPosition = new Vector2(-24f, -StreakTop - StreakH * 0.5f);
            streakText = UIBuild.Text("Text", streakBox, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(streakText.rectTransform, 6f);
            streakText.textWrappingMode = TextWrappingModes.NoWrap;
            streakBox.gameObject.SetActive(false);

            // rule reminder (right, under the streak slot)
            rulesEdge = UIBuild.Framed("RulesBox", canvas, UIStyle.Box, RulesEdgeColor, 2f);
            rulesBox = rulesEdge.rectTransform;
            rulesFill = rulesBox.GetChild(0).GetComponent<Image>();
            UIBuild.Place(rulesBox, new Vector2(1f, 1f), new Vector2(-24f, -RulesTop), new Vector2(MeterW + 32f, RulesH));
            rulesTitle = UIBuild.Text("Title", rulesBox, "", 12f, UIStyle.CardEdge, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(rulesTitle.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -6f), new Vector2(MeterW, 22f));
            ruleOpen = UIBuild.Text("Open", rulesBox, "", 12f, UIStyle.Ink, TextAlignmentOptions.Left);
            UIBuild.Place(ruleOpen.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -30f), new Vector2(MeterW + 10f, 22f));
            ruleClose = UIBuild.Text("Close", rulesBox, "", 12f, UIStyle.Ink, TextAlignmentOptions.Left);
            UIBuild.Place(ruleClose.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -54f), new Vector2(MeterW + 10f, 22f));
            ruleDistance = UIBuild.Text("Distance", rulesBox, "", 12f, UIStyle.Ink, TextAlignmentOptions.Left);
            UIBuild.Place(ruleDistance.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -78f), new Vector2(MeterW + 10f, 22f));
            ruleFriday = UIBuild.Text("Friday13", rulesBox, "", 12f, FridayInk, TextAlignmentOptions.Left);
            UIBuild.Place(ruleFriday.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -102f), new Vector2(MeterW + 10f, 22f));
            ruleFriday.gameObject.SetActive(false);
            ruleStar = UIBuild.Text("StarNight", rulesBox, "", 12f, StarInk, TextAlignmentOptions.Left);
            UIBuild.Place(ruleStar.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -102f), new Vector2(MeterW + 10f, 22f));
            ruleStar.gameObject.SetActive(false);
            ruleLegend = UIBuild.Text("Legend", rulesBox, "", 12f, UIStyle.Ink, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(ruleLegend.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -102f), new Vector2(MeterW, 24f));

            // Friday the 13th badge (right column, under the rule box; only on that day)
            fridayEdge = UIBuild.Framed("Friday13Badge", canvas, new Color(0.05f, 0.01f, 0.01f, 0.94f), UIStyle.Unlucky, 3f);
            fridayBadge = fridayEdge.rectTransform;
            UIBuild.Place(fridayBadge, new Vector2(1f, 1f), new Vector2(-24f, -(RulesBottom + Friday13LineH + BadgeGap)), new Vector2(MeterW + 32f, BadgeH));
            fridayText = UIBuild.Text("Text", fridayBadge, "", 24f, UIStyle.Unlucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(fridayText.rectTransform, 6f);
            fridayText.textWrappingMode = TextWrappingModes.NoWrap;
            fridayBadge.gameObject.SetActive(false);

            // star night badge (same slot; deep blue with a violet edge, a star at each end)
            starEdge = UIBuild.Framed("StarNightBadge", canvas, StarBoxFill, StarEdge, 3f);
            starBadge = starEdge.rectTransform;
            UIBuild.Place(starBadge, new Vector2(1f, 1f), new Vector2(-24f, -(RulesBottom + Friday13LineH + BadgeGap)), new Vector2(MeterW + 32f, StarBadgeH));
            starText = UIBuild.Text("Text", starBadge, "", 24f, StarInk, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(starText.rectTransform, 3f);
            starText.textWrappingMode = TextWrappingModes.NoWrap;
            starBadge.gameObject.SetActive(false);

            // language toggle (bottom-right)
            language = UIBuild.Button("Language", canvas, "", 24f, UIStyle.Btn, () => onLanguage?.Invoke());
            UIBuild.Place(language.Rect, new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(170f, 36f));
            language.Label.color = UIStyle.Ink;

            RefreshLanguage();
        }

        static readonly Color RulesEdgeColor = new Color(1f, 1f, 1f, 0.15f);

        float Friday13Multiplier => ctx.Data != null ? ctx.Data.Rules.friday13PenaltyMultiplier : 1.5f;

        /// <summary>
        /// Event-day look: Friday the 13th = red rule-box edge + its extra line, star night = violet edge + its line;
        /// the day's badge sits under the box (Running only). The two never share a day (Friday wins if data says both).
        /// </summary>
        void SetEventDay(bool fri, bool star, bool running)
        {
            if (fri) star = false;
            if (fri != friday || star != starNight)
            {
                friday = fri;
                starNight = star;
                bool extra = fri || star;
                ruleFriday.gameObject.SetActive(fri);
                ruleStar.gameObject.SetActive(star);
                ruleLegend.rectTransform.anchoredPosition = new Vector2(14f, extra ? -102f - Friday13LineH : -102f);
                rulesBox.sizeDelta = new Vector2(MeterW + 32f, extra ? RulesH + Friday13LineH : RulesH);
                rulesEdge.color = fri ? UIStyle.Unlucky : star ? StarEdge : RulesEdgeColor;
                rulesFill.color = fri ? FridayBoxFill : star ? StarBoxFill : UIStyle.Box; // the edge sits behind the fill: no wash
            }
            bool badge = fri && running;
            if (fridayBadge.gameObject.activeSelf != badge) fridayBadge.gameObject.SetActive(badge);
            if (badge)
            {
                // a slow ominous pulse of the edge
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                fridayEdge.color = Color.Lerp(UIStyle.Unlucky, new Color(0.45f, 0.05f, 0.05f, 1f), k);
            }
            bool starOn = star && running;
            if (starBadge.gameObject.activeSelf != starOn) starBadge.gameObject.SetActive(starOn);
            if (starOn)
            {
                // a slow twinkle of the edge (0.4 Hz, no flashing)
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.5f);
                starEdge.color = Color.Lerp(StarEdge, StarInk, k * 0.6f);
            }
        }

        static void Bar(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);
        }

        static void SetBar(RectTransform rt, float t01)
        {
            rt.anchorMax = new Vector2(Mathf.Clamp01(t01), 1f);
            rt.offsetMax = Vector2.zero;
        }

        public void RefreshLanguage()
        {
            UIBuild.Set(luckCaption, ctx.S(UIStrings.Luck));
            UIBuild.Set(tipsCaption, ctx.S(UIStrings.Tips));
            UIBuild.Set(rulesTitle, ctx.S(UIStrings.RulesTitle));
            UIBuild.Set(ruleOpen, ctx.S(UIStrings.RuleOpen));
            UIBuild.Set(ruleClose, ctx.S(UIStrings.RuleClose));
            UIBuild.Set(ruleDistance, ctx.S(UIStrings.RuleDistance));
            UIBuild.Set(ruleFriday, ctx.S(UIStrings.RuleFriday13));
            UIBuild.Set(ruleLegend, string.Format(ctx.S(UIStrings.RuleLegend), UIFormat.LuckyHex, UIFormat.UnluckyHex));
            fridayText.text = ctx.F(UIStrings.Friday13Badge, UIFormat.Multiplier(Friday13Multiplier));
            UIBuild.Fit(fridayText, 24f);
            UIBuild.Set(ruleStar, ctx.S(UIStrings.RuleStarNight));
            starText.text = StarBadgeLines(ctx.S(UIStrings.StarNightBadge));
            starText.fontSize = 24f;
            if (!string.Equals(language.Label.text, ctx.S(UIStrings.Language), System.StringComparison.Ordinal))
            {
                language.Label.text = ctx.S(UIStrings.Language);
                UIBuild.Fit(language.Label, 24f);
            }
            ShowStreakText();
        }

        // ------------------------------------------------------------------ lucky streak

        /// <summary>LuckStreakChanged (Running, not the title demo): pop on every step up, "broken" flash on a reset.</summary>
        public void OnLuckStreakChanged(LuckStreakChanged e)
        {
            streak = Mathf.Max(0, e.Streak);
            streakMultiplier = e.TipMultiplier;
            brokenTimer = e.Broken ? BrokenLife : 0f;
            if (e.Streak > e.Previous && e.Streak > 0) popTimer = PopLife;
            ShowStreakText();
        }

        /// <summary>A new day / run starts at 0 silently (no "broken" flash).</summary>
        public void ResetStreak()
        {
            streak = 0;
            streakMultiplier = 1f;
            brokenTimer = popTimer = 0f;
            ShowStreakText();
        }

        void ShowStreakText()
        {
            if (streakText == null) return;
            if (streak > 0)
            {
                streakText.text = string.Format(ctx.S(UIStrings.Streak),
                    "<color=" + UIFormat.LuckyHex + ">" + streak.ToString(CultureInfo.InvariantCulture) + "</color>",
                    "<color=#5BE37D>" + UIFormat.Multiplier(streakMultiplier) + "</color>");
                streakText.color = UIStyle.Ink;
                streakEdge.color = UIStyle.Lucky;
            }
            else
            {
                streakText.text = ctx.S(UIStrings.StreakBroken);
                streakText.color = UIStyle.Unlucky;
                streakEdge.color = UIStyle.Unlucky;
            }
            UIBuild.Fit(streakText, 24f);
        }

        void TickStreak(float dt, bool running)
        {
            if (brokenTimer > 0f) brokenTimer -= dt;
            bool show = running && (streak > 0 || brokenTimer > 0f);
            if (streakBox.gameObject.activeSelf != show) streakBox.gameObject.SetActive(show);
            if (!show) { popTimer = 0f; streakBox.localScale = Vector3.one; return; }
            float scale = 1f;
            if (popTimer > 0f)
            {
                popTimer -= dt;
                float t = 1f - Mathf.Clamp01(popTimer / PopLife); // 0 -> 1
                scale = 1f + 0.22f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
            }
            else if (streak == 0) scale = 1f + 0.04f * Mathf.Sin(brokenTimer * 30f); // a short shake while "broken"
            streakBox.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// Luck/tips set back between days (the Day 0 practice reset happens while leaving its summary): not a +/- moment,
        /// the meter just snaps. Only Running (judgements, tips) and Shop (purchases) show deltas.
        /// </summary>
        bool SilentChange => ctx.Phase != DayPhase.Running && ctx.Phase != DayPhase.Shop;

        public void OnLuckChanged(LuckChanged e)
        {
            if (SilentChange)
            {
                shownLuck = e.Current;
                ghostLuck = -1f;
                ghostHold = luckDeltaTimer = 0f;
                return;
            }
            if (shownLuck < 0f) shownLuck = e.Previous;
            if (e.Delta < 0) { ghostLuck = Mathf.Max(ghostLuck, shownLuck); ghostHold = 0.45f; }
            if (e.Delta != 0)
            {
                luckDelta.text = UIFormat.Signed(e.Delta);
                luckDelta.color = e.Delta > 0 ? UIStyle.Good : UIStyle.Unlucky;
                luckDeltaTimer = 1.4f;
            }
        }

        public void OnTipsChanged(TipsChanged e)
        {
            if (e.Delta == 0) return;
            if (SilentChange) { tipsDeltaTimer = 0f; return; }
            tipsDelta.text = (e.Delta > 0 ? "+$" : "-$") + Mathf.Abs(e.Delta).ToString(CultureInfo.InvariantCulture);
            tipsDelta.color = e.Delta > 0 ? UIStyle.Good : UIStyle.Unlucky;
            tipsDeltaTimer = 1.4f;
        }

        public void OnGameStarted()
        {
            shownLuck = -1f;
            ghostLuck = -1f;
            luckDeltaTimer = tipsDeltaTimer = 0f; // the new run's reset is not a "+/-" moment
            ghostHold = 0f;
            ResetStreak();
        }

        public void Tick(float dt)
        {
            if (!ctx.HasGame) return;
            var phase = ctx.Day.Phase;

            UIBuild.Set(dayText, UIFormat.DayLabel(ctx.Day.DisplayDay, ctx.Day.PlayableDayCount, ctx.Day.IsTutorial, ctx.Korean));
            if (ctx.Day.IsTutorial)
            {
                // the training shift has no clock: "PRACTICE" instead of a countdown
                string practice = ctx.S(UIStrings.Practice);
                if (!string.Equals(clockText.text, practice, System.StringComparison.Ordinal))
                {
                    clockText.text = practice;
                    clockText.rectTransform.sizeDelta = new Vector2(PracticeW, 48f); // "DAY 0" is short: room for 24px
                    UIBuild.Fit(clockText, 36f);
                }
                clockText.color = UIStyle.Good;
            }
            else
            {
                float remaining = phase == DayPhase.Running ? ctx.Day.RemainingSeconds : phase == DayPhase.Intro ? ctx.Day.DurationSeconds : 0f;
                string clock = UIFormat.Clock(remaining);
                if (!string.Equals(clockText.text, clock, System.StringComparison.Ordinal)) { clockText.text = clock; clockText.fontSize = 36f; clockText.rectTransform.sizeDelta = new Vector2(ClockW, 48f); }
                clockText.color = phase == DayPhase.Running && remaining <= 10f && Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.6f /* <= 3 flashes/s */ ? UIStyle.Unlucky : UIStyle.Ink;
            }

            if (ctx.Score != null)
            {
                int luck = ctx.Score.Luck, max = Mathf.Max(1, ctx.Score.MaxLuck);
                if (shownLuck < 0f) shownLuck = luck;
                shownLuck = Mathf.MoveTowards(shownLuck, luck, Mathf.Max(20f, Mathf.Abs(luck - shownLuck) * 5f) * dt);
                if (ghostHold > 0f) ghostHold -= dt;
                else if (ghostLuck >= 0f) ghostLuck = Mathf.MoveTowards(ghostLuck, shownLuck, 40f * dt);
                if (ghostLuck >= 0f && ghostLuck <= shownLuck) ghostLuck = -1f;
                SetBar(luckFill, shownLuck / max);
                SetBar(luckGhost, ghostLuck < 0f ? 0f : ghostLuck / max);
                float t = shownLuck / max;
                luckFillImage.color = t < 0.25f ? UIStyle.Unlucky : Color.Lerp(new Color(1f, 0.55f, 0.2f), UIStyle.Lucky, Mathf.InverseLerp(0.25f, 0.6f, t));
                UIBuild.Set(luckValue, Mathf.RoundToInt(shownLuck).ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture));

                int tips = ctx.Score.Tips;
                if (tips != shownTips)
                {
                    shownTips = tips;
                    tipsValue.text = "$" + tips.ToString(CultureInfo.InvariantCulture);
                }
            }

            Fade(luckDelta, ref luckDeltaTimer, dt);
            Fade(tipsDelta, ref tipsDeltaTimer, dt);
            bool rules = phase == DayPhase.Running;
            if (rulesBox.gameObject.activeSelf != rules) rulesBox.gameObject.SetActive(rules);
            SetEventDay(ctx.Day.IsFriday13 && !ctx.Day.IsTutorial, ctx.Day.IsStarNight && !ctx.Day.IsTutorial, rules);
            TickStreak(dt, rules);
        }

        static void Fade(TextMeshProUGUI t, ref float timer, float dt)
        {
            if (timer <= 0f) { if (t.alpha != 0f) t.alpha = 0f; return; }
            timer -= dt;
            t.alpha = Mathf.Clamp01(timer / 0.5f);
        }

        public void ClickLanguage() => language.Button.onClick.Invoke();
        public string DayText => dayText.text;
        public string ClockText => clockText.text;
        public float ClockFontSize => clockText.fontSize;
        public bool DeltaVisible => luckDelta.alpha > 0.01f || tipsDelta.alpha > 0.01f;

        /// <summary>Title screen: the demo plays behind the title card without HUD boxes (the title has its own toggle).</summary>
        public void SetVisible(bool visible)
        {
            if (dayBox.gameObject.activeSelf == visible) return;
            dayBox.gameObject.SetActive(visible);
            luckBox.gameObject.SetActive(visible);
            tipsBox.gameObject.SetActive(visible);
            language.Rect.gameObject.SetActive(visible);
            if (!visible) { rulesBox.gameObject.SetActive(false); streakBox.gameObject.SetActive(false); fridayBadge.gameObject.SetActive(false); starBadge.gameObject.SetActive(false); }
        }

        // coach-mark targets + test hooks
        public RectTransform RulesBox => rulesBox;
        public RectTransform FridayBadge => fridayBadge;
        public bool FridayBadgeVisible => fridayBadge.gameObject.activeInHierarchy;
        public string FridayBadgeText => FridayBadgeVisible ? fridayText.text : null;
        public float FridayBadgeFontSize => fridayText.fontSize;
        /// <summary>"A · B" -> two lines.</summary>
        public static string StarBadgeLines(string s) => s.Replace(" · ", "\n");
        public RectTransform StarBadge => starBadge;
        public bool StarBadgeVisible => starBadge.gameObject.activeInHierarchy;
        public string StarBadgeText => StarBadgeVisible ? starText.text : null;
        public float StarBadgeFontSize => starText.fontSize;
        public string StarBadgeMeasure => starText.GetPreferredValues(starText.text, float.PositiveInfinity, float.PositiveInfinity) + " in " + starText.rectTransform.rect.size;
        public bool StarBadgeFits
        {
            get
            {
                var p = starText.GetPreferredValues(starText.text, float.PositiveInfinity, float.PositiveInfinity);
                return p.x <= starText.rectTransform.rect.width + 0.5f && p.y <= starText.rectTransform.rect.height + 0.5f;
            }
        }
        /// <summary>The rule box's visible lines, top to bottom (rich text), "\n"-joined.</summary>
        public string RulesText
        {
            get
            {
                if (!rulesBox.gameObject.activeInHierarchy) return null;
                var sb = new System.Text.StringBuilder();
                foreach (var t in new[] { rulesTitle, ruleOpen, ruleClose, ruleDistance, ruleFriday, ruleStar, ruleLegend })
                    if (t.gameObject.activeSelf) { if (sb.Length > 0) sb.Append('\n'); sb.Append(t.text); }
                return sb.ToString();
            }
        }
        /// <summary>First rule-box line wider than the box (null = all fit).</summary>
        public string RulesOverflow()
        {
            float inner = rulesBox.rect.width - 14f - 4f;
            foreach (var t in new[] { rulesTitle, ruleOpen, ruleClose, ruleDistance, ruleFriday, ruleStar, ruleLegend })
            {
                if (!t.gameObject.activeSelf) continue;
                float w = t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity).x;
                if (w > inner) return "'" + t.text + "' " + w.ToString("0") + " > " + inner.ToString("0");
            }
            return null;
        }
        public RectTransform LuckBox => luckBox;
        public RectTransform DayBox => dayBox;
        public RectTransform StreakBox => streakBox;
        public bool StreakVisible => streakBox.gameObject.activeInHierarchy;
        public string StreakText => StreakVisible ? streakText.text : null;
        public float StreakScale => streakBox.localScale.x;

        /// <summary>Keep the toggle clickable above the modal flow cards.</summary>
        public void BringLanguageToFront() => language.Rect.SetAsLastSibling();
    }
}
