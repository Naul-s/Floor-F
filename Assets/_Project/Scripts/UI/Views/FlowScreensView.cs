using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Modal cards for the day flow (GDD 3): Intro (title, intro text, how-to on tutorial days, START -> IGameFlow.BeginDay),
    /// Summary (DaySummary, CONTINUE -> ContinueFromSummary), Ending (EndingKind text, PLAY AGAIN -> RestartGame).
    /// The Shop is <see cref="ShopView"/> (side card, the panel stays clickable).
    /// </summary>
    sealed class FlowScreensView
    {
        readonly UIContext ctx;
        readonly RectTransform dim, card;
        readonly TextMeshProUGUI title, body, extra;
        readonly UIBuild.ButtonParts action, toTitle;
        DayPhase shown = (DayPhase)(-1);

        public FlowScreensView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            dim = UIBuild.Box("FlowDim", canvas, UIStyle.Dim, raycast: true).rectTransform;
            UIBuild.Stretch(dim);

            card = UIBuild.Box("Card", dim, UIStyle.CardEdge).rectTransform;
            UIBuild.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 300f));
            var fill = UIBuild.Box("Fill", card, UIStyle.Card);
            UIBuild.Stretch(fill.rectTransform, 3f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(36, 36, 28, 28);
            v.spacing = 16f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = UIBuild.Text("Title", card, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            body = UIBuild.Text("Body", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            extra = UIBuild.Text("Extra", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            extra.lineSpacing = 4f;
            var row = UIBuild.Rect("Buttons", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            action = UIBuild.Button("Action", row, "", 24f, UIStyle.Action, OnAction);
            action.Label.color = UIStyle.Ink;
            action.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f;
            toTitle = UIBuild.Button("ToTitle", row, "", 24f, UIStyle.Btn, OnTitle);
            toTitle.Label.color = UIStyle.Ink;
            toTitle.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            toTitle.Rect.gameObject.SetActive(false);

            dim.gameObject.SetActive(false);
        }

        public DayPhase Shown => dim.gameObject.activeSelf ? shown : DayPhase.None;

        /// <summary>Last GuestSpoke with trigger "special" (the mystery guest at the hidden 4th floor). Set by UIRoot.</summary>
        public string LastSpecialLine;

        void OnAction()
        {
            if (ctx.Flow == null || ctx.Day == null) return;
            switch (ctx.Day.Phase)
            {
                case DayPhase.Intro: ctx.Flow.BeginDay(); break;
                case DayPhase.Summary: ctx.Flow.ContinueFromSummary(); break;
                case DayPhase.Ending: ctx.Flow.RestartGame(); break;
            }
        }

        void OnTitle() => ctx.Menu?.ReturnToTitle();

        public void Click() => action.Button.onClick.Invoke();
        public void ClickTitle() => toTitle.Button.onClick.Invoke();
        public bool TitleButtonVisible => dim.gameObject.activeSelf && toTitle.Rect.gameObject.activeSelf;
        public string ActionText => action.Label.text;

        public void Tick()
        {
            var phase = ctx.HasGame ? ctx.Day.Phase : DayPhase.None;
            if (phase == shown) return;
            Show(phase);
        }

        public void Refresh() => Show(shown);

        void Show(DayPhase phase)
        {
            shown = phase;
            bool modal = phase == DayPhase.Intro || phase == DayPhase.Summary || phase == DayPhase.Ending;
            dim.gameObject.SetActive(modal);
            if (!modal) return;
            bool ko = ctx.Korean;
            title.color = UIStyle.Lucky;
            toTitle.Rect.gameObject.SetActive(phase == DayPhase.Ending && ctx.Menu != null);
            toTitle.Label.text = ctx.S(UIStrings.ToTitle);
            switch (phase)
            {
                case DayPhase.Intro: FillIntro(ko); break;
                case DayPhase.Summary: FillSummary(ko); break;
                case DayPhase.Ending: FillEnding(ko); break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        void FillIntro(bool ko)
        {
            string t = "", intro = "";
            bool tutorial = ctx.Day.IsTutorial;
            if (ctx.TryGetDay(out var d))
            {
                t = ko && !string.IsNullOrEmpty(d.titleKo) ? d.titleKo : d.title;
                intro = ko && !string.IsNullOrEmpty(d.introKo) ? d.introKo : d.intro;
                tutorial |= d.tutorial;
            }
            if (string.IsNullOrEmpty(t)) t = string.Format(ctx.S(UIStrings.DayOf), ctx.Day.CurrentDay.Number, ctx.Day.DayCount);
            title.text = t;
            body.text = intro;
            body.gameObject.SetActive(!string.IsNullOrEmpty(intro));
            bool howTo = tutorial || (ctx.Settings != null && ctx.Settings.ShowTutorialHints && ctx.Day.CurrentDay.Number == 1);
            extra.gameObject.SetActive(howTo);
            if (howTo)
                extra.text = "<b><color=#C9A15A>" + ctx.S(UIStrings.HowToTitle) + "</color></b>\n" +
                             string.Format(ctx.S(UIStrings.HowTo), UIFormat.LuckyHex, UIFormat.UnluckyHex);
            action.Label.text = ctx.S(UIStrings.Start);
        }

        void FillSummary(bool ko)
        {
            var s = ctx.Day.LastSummary;
            title.text = string.Format(ctx.S(UIStrings.DayComplete), ctx.Day.CurrentDay.Number);
            body.gameObject.SetActive(false);
            extra.gameObject.SetActive(true);
            string luckColor = s.LuckDelta >= 0 ? "#5BE37D" : UIFormat.UnluckyHex;
            extra.text =
                Row(ctx.S(UIStrings.Delivered), N(s.Delivered)) + "\n" +
                Row(ctx.S(UIStrings.Left), N(s.Left)) + "\n" +
                Row(ctx.S(UIStrings.TipsEarned), "<color=#5BE37D>+$" + N(s.TipsEarned) + "</color>") + "\n" +
                Row(ctx.S(UIStrings.LuckLine), N(s.LuckAtStart) + " → " + N(s.LuckAtEnd) + "  <color=" + luckColor + ">(" + UIFormat.Signed(s.LuckDelta) + ")</color>") + "\n" +
                Row(ctx.S(UIStrings.Judgements), "<color=" + UIFormat.LuckyHex + ">" + N(s.LuckyJudgements) + "</color> / <color=" + UIFormat.UnluckyHex + ">" + N(s.UnluckyJudgements) + "</color>");
            action.Label.text = ctx.S(UIStrings.Continue);
        }

        void FillEnding(bool ko)
        {
            UIFormat.Ending(ctx.Day.Ending, ko, out var t, out var text);
            title.text = t;
            title.color = ctx.Day.Ending == EndingKind.Haunted || ctx.Day.Ending == EndingKind.FourthFloor ? UIStyle.Unlucky : UIStyle.Lucky;
            body.gameObject.SetActive(true);
            body.text = ctx.Day.Ending == EndingKind.FourthFloor && !string.IsNullOrEmpty(LastSpecialLine)
                ? "<color=" + UIFormat.HiddenHex + ">“" + LastSpecialLine + "”</color>\n\n" + text
                : text;
            extra.gameObject.SetActive(true);
            int luck = ctx.Score != null ? ctx.Score.Luck : 0, max = ctx.Score != null ? ctx.Score.MaxLuck : 100;
            int tips = ctx.Score != null ? ctx.Score.Tips : 0;
            float luck01 = max > 0 ? (float)luck / max : 0f;
            string luckHex = luck01 < 0.25f ? UIFormat.UnluckyHex : luck01 >= 0.6f ? UIFormat.LuckyHex : "#F4EEDC";
            extra.text =
                Row(ctx.S(UIStrings.EndDayReached), N(ctx.Day.CurrentDay.Number) + " / " + N(ctx.Day.DayCount)) + "\n" +
                Row(ctx.S(UIStrings.EndFinalLuck), "<color=" + luckHex + ">" + N(luck) + "</color> / " + N(max)) + "\n" +
                Row(ctx.S(UIStrings.EndTips), "<color=#5BE37D>$" + N(tips) + "</color>");
            action.Label.text = ctx.S(UIStrings.PlayAgain);
        }

        static string Row(string caption, string value) => caption + "<pos=62%><b>" + value + "</b>";
        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        public string TitleText => title.text;
        public string BodyText => body.text;
    }
}
