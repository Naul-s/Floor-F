using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Always-on HUD: day + clock (IDayQuery), Luck meter (animated on LuckChanged, ghost bar on loss, +/- float),
    /// tips (TipsChanged), the two-line rule reminder, and the language toggle (IGameSettings.UseKorean).
    /// </summary>
    sealed class HudView
    {
        const float MeterW = 344f;
        readonly UIContext ctx;
        readonly TextMeshProUGUI dayText, clockText, luckCaption, luckValue, luckDelta, tipsCaption, tipsValue, tipsDelta;
        readonly TextMeshProUGUI rulesTitle, ruleOpen, ruleClose, ruleLegend;
        readonly RectTransform luckFill, luckGhost, rulesBox, dayBox, luckBox, tipsBox;
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
            dayText = UIBuild.Text("Day", dayBox, "", 20f, UIStyle.Ink, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(dayText.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(110f, 40f));
            clockText = UIBuild.Text("Clock", dayBox, "", 34f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(clockText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(100f, 48f));

            // luck meter (top-right)
            luckBox = UIBuild.Framed("LuckBox", canvas, UIStyle.Box, UIStyle.CardEdge, 2f).rectTransform;
            UIBuild.Place(luckBox, new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(MeterW + 32f, 76f));
            luckCaption = UIBuild.Text("Caption", luckBox, "", 20f, UIStyle.Lucky, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(luckCaption.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -6f), new Vector2(120f, 28f));
            luckValue = UIBuild.Text("Value", luckBox, "", 24f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(luckValue.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -4f), new Vector2(140f, 30f));
            luckDelta = UIBuild.Text("Delta", luckBox, "", 22f, UIStyle.Good, TextAlignmentOptions.Center, bold: true);
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
            tipsCaption = UIBuild.Text("Caption", tipsBox, "", 20f, UIStyle.Good, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(tipsCaption.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(120f, 32f));
            tipsValue = UIBuild.Text("Value", tipsBox, "", 26f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(tipsValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(160f, 34f));
            tipsDelta = UIBuild.Text("Delta", tipsBox, "", 22f, UIStyle.Good, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(tipsDelta.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(100f, 32f));

            // rule reminder (right, under tips)
            rulesBox = UIBuild.Framed("RulesBox", canvas, UIStyle.Box, new Color(1f, 1f, 1f, 0.15f), 2f).rectTransform;
            UIBuild.Place(rulesBox, new Vector2(1f, 1f), new Vector2(-24f, -150f), new Vector2(MeterW + 32f, 112f));
            rulesTitle = UIBuild.Text("Title", rulesBox, "", 15f, UIStyle.CardEdge, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(rulesTitle.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -6f), new Vector2(MeterW, 22f));
            ruleOpen = UIBuild.Text("Open", rulesBox, "", 15f, UIStyle.Ink, TextAlignmentOptions.Left);
            UIBuild.Place(ruleOpen.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -30f), new Vector2(MeterW + 10f, 22f));
            ruleClose = UIBuild.Text("Close", rulesBox, "", 15f, UIStyle.Ink, TextAlignmentOptions.Left);
            UIBuild.Place(ruleClose.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -54f), new Vector2(MeterW + 10f, 22f));
            ruleLegend = UIBuild.Text("Legend", rulesBox, "", 16f, UIStyle.Ink, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(ruleLegend.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -80f), new Vector2(MeterW, 24f));

            // language toggle (bottom-right)
            language = UIBuild.Button("Language", canvas, "", 16f, UIStyle.Btn, () => onLanguage?.Invoke());
            UIBuild.Place(language.Rect, new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(130f, 34f));
            language.Label.color = UIStyle.Ink;

            RefreshLanguage();
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
            UIBuild.Set(ruleLegend, string.Format(ctx.S(UIStrings.RuleLegend), UIFormat.LuckyHex, UIFormat.UnluckyHex));
            UIBuild.Set(language.Label, ctx.S(UIStrings.Language));
        }

        public void OnLuckChanged(LuckChanged e)
        {
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
            tipsDelta.text = (e.Delta > 0 ? "+$" : "-$") + Mathf.Abs(e.Delta).ToString(CultureInfo.InvariantCulture);
            tipsDelta.color = e.Delta > 0 ? UIStyle.Good : UIStyle.Unlucky;
            tipsDeltaTimer = 1.4f;
        }

        public void OnGameStarted()
        {
            shownLuck = -1f;
            ghostLuck = -1f;
        }

        public void Tick(float dt)
        {
            if (!ctx.HasGame) return;
            var phase = ctx.Day.Phase;

            UIBuild.Set(dayText, string.Format(ctx.S(UIStrings.DayOf), ctx.Day.CurrentDay.Number, ctx.Day.DayCount));
            float remaining = phase == DayPhase.Running ? ctx.Day.RemainingSeconds : phase == DayPhase.Intro ? ctx.Day.DurationSeconds : 0f;
            UIBuild.Set(clockText, UIFormat.Clock(remaining));
            clockText.color = phase == DayPhase.Running && remaining <= 10f && Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f ? UIStyle.Unlucky : UIStyle.Ink;

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
            rulesBox.gameObject.SetActive(phase == DayPhase.Running);
        }

        static void Fade(TextMeshProUGUI t, ref float timer, float dt)
        {
            if (timer <= 0f) { if (t.alpha != 0f) t.alpha = 0f; return; }
            timer -= dt;
            t.alpha = Mathf.Clamp01(timer / 0.5f);
        }

        public void ClickLanguage() => language.Button.onClick.Invoke();

        /// <summary>Keep the toggle clickable above the modal flow cards.</summary>
        public void BringLanguageToFront() => language.Rect.SetAsLastSibling();
    }
}
