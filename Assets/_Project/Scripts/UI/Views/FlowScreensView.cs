using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Modal cards for the day flow (GDD 3): Intro (title, intro text, how-to on tutorial days, START -> IGameFlow.BeginDay),
    /// Summary (DaySummary, CONTINUE -> ContinueFromSummary; real days get a stamped S/A/B/C grade, UIFormat.DayGrade),
    /// Ending (EndingKind text, PLAY AGAIN -> RestartGame).
    /// The Shop is <see cref="ShopView"/> (side card, the panel stays clickable).
    /// </summary>
    sealed class FlowScreensView
    {
        readonly UIContext ctx;
        readonly RectTransform dim, card;
        readonly Image cardEdge, cardFill;
        /// <summary>Friday the 13th intro: red edge on near-black, red title, a red-black dim.</summary>
        public static readonly Color FridayFill = new Color(0.04f, 0.01f, 0.012f, 0.97f), FridayDim = new Color(0.08f, 0f, 0f, 0.78f);
        bool fridayCard;
        /// <summary>Star night (Day 7) intro: violet edge on deep blue, starlight title between stars, a blue-violet dim.</summary>
        public static readonly Color StarFill = new Color(0.025f, 0.03f, 0.10f, 0.97f), StarDim = new Color(0.01f, 0.01f, 0.07f, 0.74f);
        bool starCard;
        readonly RectTransform starDeco;
        readonly TextMeshProUGUI[] starGlyphs = new TextMeshProUGUI[6];
        /// <summary>Decorative stars on the card edge: anchor (0..1 of the card), size, phase.</summary>
        static readonly Vector3[] StarSpots =
        {
            new Vector3(0f, 1f, 36f), new Vector3(1f, 1f, 36f), new Vector3(0f, 0f, 24f), new Vector3(1f, 0f, 24f),
            new Vector3(0.18f, 1f, 24f), new Vector3(0.82f, 0f, 24f),
        };
        readonly TextMeshProUGUI title, body, extra;
        readonly UIBuild.ButtonParts action, toTitle, skip;
        DayPhase shown = (DayPhase)(-1);
        int shownDay = -1;
        readonly RectTransform stamp;
        readonly CanvasGroup stampGroup;
        readonly TextMeshProUGUI stampLetter;
        readonly Image[] stampEdges;
        float stampAge = -1f;
        /// <summary>The stamp lands this long after the summary card appears, in <see cref="StampSlam"/> seconds.</summary>
        public const float StampDelay = 0.35f, StampSlam = 0.2f;

        public FlowScreensView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            dim = UIBuild.Box("FlowDim", canvas, UIStyle.Dim, raycast: true).rectTransform;
            UIBuild.Stretch(dim);

            cardEdge = UIBuild.Box("Card", dim, UIStyle.CardEdge);
            card = cardEdge.rectTransform;
            UIBuild.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 300f));
            var fill = cardFill = UIBuild.Box("Fill", card, UIStyle.Card);
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
            // Day 0 intro only: straight to Day 1 (IGameFlow.SkipTutorial)
            skip = UIBuild.Button("SkipTutorial", row, "", 24f, UIStyle.Btn, OnSkip);
            skip.Label.color = UIStyle.Ink;
            skip.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f; // as wide as START: the label stays at 24px
            skip.Rect.gameObject.SetActive(false);

            // grade stamp: top-right corner of the summary card, tilted, slams down (scale 2.4 -> 1)
            stamp = UIBuild.Rect("GradeStamp", card);
            stamp.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            stamp.anchorMin = stamp.anchorMax = new Vector2(1f, 1f);
            stamp.pivot = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = new Vector2(-84f, -70f);
            stamp.sizeDelta = new Vector2(124f, 124f);
            stamp.localRotation = Quaternion.Euler(0f, 0f, -12f);
            stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
            stampGroup.blocksRaycasts = false;
            stampGroup.interactable = false;
            UIBuild.Stretch(UIBuild.Box("Plate", stamp, new Color(0f, 0f, 0f, 0.35f)).rectTransform);
            var ring = UIBuild.Outline("Ring", stamp, UIStyle.Lucky, 6f, 0f);
            stampEdges = ring.GetComponentsInChildren<Image>();
            // just the letter: no "GRADE" caption (it crowded the top of the S)
            stampLetter = UIBuild.Text("Letter", stamp, "", 96f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(stampLetter.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 110f));
            stampLetter.textWrappingMode = TextWrappingModes.NoWrap;
            stamp.gameObject.SetActive(false);

            // star-night stars sitting on the card's edge (outside the layout; twinkle slowly in TickStars)
            starDeco = UIBuild.Stretch(UIBuild.Rect("StarDeco", card));
            starDeco.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            for (int i = 0; i < StarSpots.Length; i++)
            {
                var s = StarSpots[i];
                var g = UIBuild.Text("Star" + i, starDeco, UIFonts.Star, s.z, HudView.StarInk, TextAlignmentOptions.Center);
                UIBuild.Place(g.rectTransform, new Vector2(s.x, s.y), Vector2.zero, new Vector2(s.z + 12f, s.z + 12f));
                g.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                g.textWrappingMode = TextWrappingModes.NoWrap;
                starGlyphs[i] = g;
            }
            starDeco.gameObject.SetActive(false);

            dim.gameObject.SetActive(false);
        }

        public DayPhase Shown => dim.gameObject.activeSelf ? shown : DayPhase.None;

        /// <summary>Last GuestSpoke with trigger "special" (the mystery guest at the hidden 4th floor). Set by UIRoot.</summary>
        public string LastSpecialLine;
        /// <summary>Who said <see cref="LastSpecialLine"/>: the card re-reads the line in the CURRENT language from data.</summary>
        public GuestArchetypeId LastSpecialArchetype;

        string SpecialLine(bool ko)
        {
            if (string.IsNullOrEmpty(LastSpecialLine)) return null;
            if (!LastSpecialArchetype.IsEmpty && ctx.Data != null && ctx.Data.TryGetGuest(LastSpecialArchetype, out var def) && def.lines != null)
                foreach (var l in def.lines)
                    if (l != null && l.trigger == GuestLineTriggers.Special) return l.Get(ko);
            return LastSpecialLine;
        }

        void OnAction()
        {
            if (ctx.Flow == null || ctx.Day == null) { ctx.Sound(UiSoundKind.Error); return; }
            bool ok;
            switch (ctx.Day.Phase)
            {
                case DayPhase.Intro: ok = ctx.Flow.BeginDay(); break;
                case DayPhase.Summary: ok = ctx.Flow.ContinueFromSummary(); break;
                case DayPhase.Ending: ctx.Flow.RestartGame(); ok = true; break;
                default: ok = false; break;
            }
            ctx.Sound(ok ? UiSoundKind.Confirm : UiSoundKind.Error);
        }

        void OnSkip()
        {
            bool ok = ctx.Flow != null && ctx.Flow.SkipTutorial();
            ctx.Sound(ok ? UiSoundKind.Confirm : UiSoundKind.Error);
            if (ok) Show(ctx.Day.Phase); // Intro -> Intro: the next day's card
        }

        void OnTitle()
        {
            ctx.Sound(UiSoundKind.Click);
            ctx.Menu?.ReturnToTitle();
        }

        public void Click() => action.Button.onClick.Invoke();
        public void ClickSkip() => skip.Button.onClick.Invoke();
        public string SkipText => skip.Label.text;
        public float SkipFontSize => skip.Label.fontSize;
        public bool SkipVisible => dim.gameObject.activeSelf && skip.Rect.gameObject.activeSelf;
        public void ClickTitle() => toTitle.Button.onClick.Invoke();
        public bool TitleButtonVisible => dim.gameObject.activeSelf && toTitle.Rect.gameObject.activeSelf;
        public string ActionText => action.Label.text;

        public void Tick()
        {
            TickStamp();
            TickStars();
            var phase = ctx.HasGame ? ctx.Day.Phase : DayPhase.None;
            if (SkipVisible && !(ctx.Paused) && TabPressed()) { OnSkip(); return; }
            int day = ctx.HasGame ? ctx.Day.CurrentDay.Number : -1;
            if (phase == shown && day == shownDay) return; // SkipTutorial is Intro -> Intro on another day
            Show(phase);
        }

        static bool TabPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.tabKey.wasPressedThisFrame;
        }

        public void Refresh() => Show(shown);

        void Show(DayPhase phase)
        {
            shown = phase;
            shownDay = ctx.HasGame ? ctx.Day.CurrentDay.Number : -1;
            card.anchoredPosition = Vector2.zero; // a stamp jolt never carries over
            bool modal = phase == DayPhase.Intro || phase == DayPhase.Summary || phase == DayPhase.Ending;
            dim.gameObject.SetActive(modal);
            bool graded = modal && phase == DayPhase.Summary && !ctx.Day.IsTutorial;
            if (graded != stamp.gameObject.activeSelf)
            {
                stamp.gameObject.SetActive(graded);
                stampAge = graded ? 0f : -1f;
                stampGroup.alpha = 0f;
                stamp.localScale = new Vector3(2.4f, 2.4f, 1f);
            }
            if (!modal) return;
            bool ko = ctx.Korean;
            bool intro = phase == DayPhase.Intro && !ctx.Day.IsTutorial;
            SetCardLook(intro && ctx.Day.IsFriday13, intro && ctx.Day.IsStarNight);
            title.color = fridayCard ? UIStyle.Unlucky : starCard ? StarTitle : UIStyle.Lucky;
            extra.alignment = TextAlignmentOptions.Left; // the Friday-only intro line centres it (FillIntro)
            toTitle.Rect.gameObject.SetActive(phase == DayPhase.Ending && ctx.Menu != null);
            toTitle.Label.text = ctx.S(UIStrings.ToTitle);
            bool canSkip = phase == DayPhase.Intro && ctx.Day.IsTutorial && ctx.Flow != null;
            skip.Rect.gameObject.SetActive(canSkip);
            if (canSkip) skip.Label.text = ctx.S(UIStrings.SkipTutorial);
            switch (phase)
            {
                case DayPhase.Intro: FillIntro(ko); break;
                case DayPhase.Summary: FillSummary(ko); break;
                case DayPhase.Ending: FillEnding(ko); break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            if (skip.Rect.gameObject.activeSelf) UIBuild.Fit(skip.Label, 24f); // after layout: the button has its real width now
        }

        void FillIntro(bool ko)
        {
            string t = "", intro = "";
            bool tutorial = ctx.Day.IsTutorial;
            if (ctx.TryGetDay(out var d))
            {
                t = ko && !string.IsNullOrEmpty(d.titleKo) ? d.titleKo : d.title;
                intro = ko && !string.IsNullOrEmpty(d.introKo) ? d.introKo : d.intro;
            }
            if (string.IsNullOrEmpty(t)) t = UIFormat.DayLabel(ctx.Day.DisplayDay, ctx.Day.PlayableDayCount, tutorial, ko);
            title.text = starCard ? UIFonts.Star + "  " + t + "  " + UIFonts.Star : t;
            body.text = intro;
            body.gameObject.SetActive(!string.IsNullOrEmpty(intro));
            // Day 0 teaches step by step in the game itself; the real Day 1 gets the list as a recap (hints on)
            bool howTo = !tutorial && ctx.Settings != null && ctx.Settings.ShowTutorialHints && ctx.Day.DisplayDay == 1;
            extra.gameObject.SetActive(howTo || fridayCard || starCard);
            extra.text = "";
            if (howTo)
                extra.text = "<b><color=#C9A15A>" + ctx.S(UIStrings.HowToTitle) + "</color></b>\n" +
                             string.Format(ctx.S(UIStrings.HowTo), UIFormat.LuckyHex, UIFormat.UnluckyHex);
            if (fridayCard)
            {
                float m = ctx.Data != null ? ctx.Data.Rules.friday13PenaltyMultiplier : 1.5f;
                extra.text += (howTo ? "\n\n" : "") + "<color=" + UIFormat.UnluckyHex + ">" +
                              ctx.F(UIStrings.Friday13Intro, UIFormat.Multiplier(m).Substring(1)) + "</color>";
                if (!howTo) extra.alignment = TextAlignmentOptions.Center;
            }
            if (starCard)
            {
                int bonus = ctx.Data != null ? ctx.Data.Rules.wishLuckBonus : 4;
                extra.text += (howTo ? "\n\n" : "") + "<color=" + StarInkHex + ">" + ctx.F(UIStrings.StarNightIntro, bonus) + "</color>";
                if (!howTo) extra.alignment = TextAlignmentOptions.Center;
            }
            action.Label.text = ctx.S(UIStrings.Start);
        }

        public static readonly Color StarTitle = UIStyle.Hex("#FFF1B0");
        const string StarInkHex = "#DCD4FF";

        /// <summary>Card colours for the event day (Friday the 13th wins over star night; neither = the normal card).</summary>
        void SetCardLook(bool friday, bool star)
        {
            star &= !friday;
            if (friday == fridayCard && star == starCard) return;
            fridayCard = friday;
            starCard = star;
            cardEdge.color = friday ? UIStyle.Unlucky : star ? HudView.StarEdge : UIStyle.CardEdge;
            cardFill.color = friday ? FridayFill : star ? StarFill : UIStyle.Card;
            dim.GetComponent<Image>().color = friday ? FridayDim : star ? StarDim : UIStyle.Dim;
            starDeco.gameObject.SetActive(star);
        }

        /// <summary>Star-night card: the edge stars twinkle slowly (~0.3-0.9 Hz, alpha 0.44..1 - no flashing).</summary>
        void TickStars()
        {
            if (!starCard || !dim.gameObject.activeSelf) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < starGlyphs.Length; i++)
                starGlyphs[i].alpha = 0.72f + 0.28f * Mathf.Sin(t * (2f + 0.7f * i) + i * 1.9f);
        }

        public bool StarLook => dim.gameObject.activeSelf && starCard;

        public bool FridayLook => dim.gameObject.activeSelf && fridayCard;
        public Color CardEdgeColor => cardEdge.color;

        void FillSummary(bool ko)
        {
            var s = ctx.Day.LastSummary;
            bool tutorial = ctx.Day.IsTutorial;
            title.text = tutorial ? ctx.S(UIStrings.TrainingDone) : string.Format(ctx.S(UIStrings.DayComplete), ctx.Day.DisplayDay);
            // the training shift's wrap-up: what the real days add, and that practice Luck/tips do not carry over
            body.gameObject.SetActive(tutorial);
            if (tutorial) body.text = "<color=" + UIFormat.LuckyHex + ">" + ctx.S(UIStrings.PracticeReset) + "</color>";
            extra.gameObject.SetActive(true);
            string luckColor = s.LuckDelta >= 0 ? "#5BE37D" : UIFormat.UnluckyHex;
            extra.text =
                Row(ctx.S(UIStrings.Delivered), N(s.Delivered)) + "\n" +
                Row(ctx.S(UIStrings.Left), N(s.Left)) + "\n" +
                Row(ctx.S(UIStrings.TipsEarned), "<color=#5BE37D>+$" + N(s.TipsEarned) + "</color>") + "\n" +
                Row(ctx.S(UIStrings.LuckLine), N(s.LuckAtStart) + " → " + N(s.LuckAtEnd) + "  <color=" + luckColor + ">(" + UIFormat.Signed(s.LuckDelta) + ")</color>") + "\n" +
                Row(ctx.S(UIStrings.Judgements), "<color=" + UIFormat.LuckyHex + ">" + N(s.LuckyJudgements) + "</color> / <color=" + UIFormat.UnluckyHex + ">" + N(s.UnluckyJudgements) + "</color>") + "\n" +
                Row(ctx.S(UIStrings.BestStreak), (s.BestStreak > 0 ? "<color=" + UIFormat.LuckyHex + ">x" : "<color=#F4EEDC>x") + N(s.BestStreak) + "</color>");
            string expired = ExpiredNote(ExpiredLabels, ExpiredCharms, ko);
            if (expired != null) extra.text += "\n\n<color=" + UIFormat.ShieldHex + ">" + expired + "</color>";
            if (!tutorial) FillStamp(UIFormat.DayGrade(s));
            if (tutorial)
            {
                int lastDay = ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount;
                int prosperous = ctx.Data != null ? ctx.Data.Rules.prosperousEndingLuck : 70;
                extra.text += "\n\n" + ctx.F(UIStrings.TutRules, lastDay, prosperous);
            }
            action.Label.text = ctx.S(UIStrings.Continue);
        }

        /// <summary>StickersExpired of the day just ended (UIRoot sets it; a new Intro clears it). 0/0 = nothing to say.</summary>
        public int ExpiredLabels, ExpiredCharms;

        public void OnStickersExpired(StickersExpired e)
        {
            ExpiredLabels = e.Labels;
            ExpiredCharms = e.Charms;
            if (Shown == DayPhase.Summary) Refresh(); // it may arrive after the card is up
        }

        public static string ExpiredNote(int labels, int charms, bool ko) =>
            labels > 0 && charms > 0 ? UIStrings.StickersExpired.Get(ko)
            : labels > 0 ? UIStrings.StickersExpiredLabels.Get(ko)
            : charms > 0 ? UIStrings.CharmsExpired.Get(ko)
            : null;

        void FillEnding(bool ko)
        {
            UIFormat.Ending(ctx.Day.Ending, ko, out var t, out var text);
            title.text = t;
            title.color = ctx.Day.Ending == EndingKind.Haunted || ctx.Day.Ending == EndingKind.FourthFloor ? UIStyle.Unlucky : UIStyle.Lucky;
            body.gameObject.SetActive(true);
            string quote = ctx.Day.Ending == EndingKind.FourthFloor ? SpecialLine(ko) : null;
            body.text = !string.IsNullOrEmpty(quote)
                ? "<color=" + UIFormat.HiddenHex + ">“" + quote + "”</color>\n\n" + text
                : text;
            extra.gameObject.SetActive(true);
            int luck = ctx.Score != null ? ctx.Score.Luck : 0, max = ctx.Score != null ? ctx.Score.MaxLuck : 100;
            int tips = ctx.Score != null ? ctx.Score.Tips : 0;
            float luck01 = max > 0 ? (float)luck / max : 0f;
            string luckHex = luck01 < 0.25f ? UIFormat.UnluckyHex : luck01 >= 0.6f ? UIFormat.LuckyHex : "#F4EEDC";
            extra.text =
                Row(ctx.S(UIStrings.EndDayReached), N(ctx.Day.DisplayDay) + " / " + N(ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount)) + "\n" +
                Row(ctx.S(UIStrings.EndFinalLuck), "<color=" + luckHex + ">" + N(luck) + "</color> / " + N(max)) + "\n" +
                Row(ctx.S(UIStrings.EndTips), "<color=#5BE37D>$" + N(tips) + "</color>");
            action.Label.text = ctx.S(UIStrings.PlayAgain);
        }

        void FillStamp(string grade)
        {
            var c = GradeColor(grade);
            stampLetter.text = grade;
            stampLetter.color = c;
            foreach (var e in stampEdges) e.color = c;
        }

        public static Color GradeColor(string grade) =>
            grade == "S" ? UIStyle.Lucky : grade == "A" ? UIStyle.Good : grade == "B" ? UIStyle.Ink : UIStyle.Unlucky;

        void TickStamp()
        {
            if (!stamp.gameObject.activeInHierarchy || stampAge < 0f) return;
            stampAge += Time.deltaTime;
            float t = (stampAge - StampDelay) / StampSlam;
            if (t < 0f) { stampGroup.alpha = 0f; return; }
            float k = Mathf.Clamp01(t);
            stampGroup.alpha = k;
            float scale = Mathf.Lerp(2.4f, 1f, k * k); // accelerates into the card
            if (t > 1f && t < 1.6f) scale = 1f + 0.06f * Mathf.Sin((t - 1f) / 0.6f * Mathf.PI); // a small bounce on impact
            stamp.localScale = new Vector3(scale, scale, 1f);
            // the impact jolts the card down a few pixels, settling in ~0.25 s
            float jolt = t > 1f && t < 1f + 0.25f / StampSlam ? Mathf.Round(-5f * (1f - (t - 1f) * StampSlam / 0.25f)) : 0f;
            card.anchoredPosition = new Vector2(0f, jolt);
        }

        public bool StampVisible => stamp.gameObject.activeInHierarchy;
        public string StampGrade => StampVisible ? stampLetter.text : null;
        /// <summary>Texts on the stamp (1: just the letter, no caption).</summary>
        public int StampTextCount => stamp.GetComponentsInChildren<TMP_Text>(true).Length;
        public bool StampLanded => StampVisible && stampGroup.alpha >= 0.99f && Mathf.Abs(stamp.localScale.x - 1f) < 0.08f;
        public Rect StampRect(RectTransform canvas) => StampVisible ? UIBuild.CanvasRect(canvas, stamp) : default;
        public Rect CardRect(RectTransform canvas) => dim.gameObject.activeSelf ? UIBuild.CanvasRect(canvas, card) : default;

        static string Row(string caption, string value) => caption + "<pos=62%><b>" + value + "</b>";
        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        public string TitleText => title.text;
        public string BodyText => body.text;
        public string ExtraText => extra.gameObject.activeSelf ? extra.text : null;
    }
}
