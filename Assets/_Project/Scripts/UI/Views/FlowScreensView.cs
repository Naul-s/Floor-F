using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Modal cards for the day flow (GDD 3): Intro (title, flavour text, the coloured "TODAY:" twist line (DayTwist), how-to
    /// recap on Day 1, START -> IGameFlow.BeginDay), Summary (DaySummary, CONTINUE -> ContinueFromSummary; real days get a
    /// stamped S/A/B/C grade, UIFormat.DayGrade, and a hint line explaining it, SummaryHint), Ending (EndingKind text, the
    /// run's grade stamps (RunJourney) and totals, the best record, PLAY AGAIN -> RestartGame).
    /// The Shop is <see cref="ShopView"/> (side card, the panel stays clickable).
    /// </summary>
    sealed class FlowScreensView
    {
        readonly UIContext ctx;
        readonly RectTransform dim, card;
        /// <summary>On the dim: hides the whole card during the ending beat and fades it in after.</summary>
        readonly CanvasGroup dimGroup;

        // ------------------------------------------------------------------ ending: scene first, card later
        /// <summary>
        /// Seconds the ending scene plays alone before the card fades in (any key / click skips). The Christmas miracle
        /// waits longer: Santa's sleigh (first flight 1.5 s in, 7 s long, past the moon top-left) should be mostly seen.
        /// </summary>
        public const float EndingBeat = 3.5f, MiracleBeat = 6f, CardFadeIn = 0.4f;
        public static float BeatFor(EndingKind k) => k == EndingKind.WinterMiracle ? MiracleBeat : EndingBeat;
        float beatAge = -1f, fadeAge = -1f;
        int enterHeldUntilFrame = -1;
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
        readonly TextMeshProUGUI title, body, today, extra, hint, unlockLine;
        /// <summary>Ending card: one small grade stamp per real day (RunJourney), "-" for a day not reached.</summary>
        readonly RectTransform journey;
        readonly JourneyStamp[] journeyStamps = new JourneyStamp[MaxJourneyDays];
        public const int MaxJourneyDays = 10;
        public const float JourneyCell = 48f, JourneyGap = 20f, JourneyDayH = 24f;

        sealed class JourneyStamp
        {
            public RectTransform Root;
            public TextMeshProUGUI Letter, Day;
            public Image[] Ring;
        }
        /// <summary>4th-floor ending: "HIDDEN ENDING" ribbon straddling the card's top edge.</summary>
        readonly HiddenBadge hiddenBadge;
        /// <summary>4th-floor ending card: violet edge on near-black violet, a violet-black dim (the quote's colour).</summary>
        public static readonly Color HiddenFill = new Color(0.03f, 0.015f, 0.055f, 0.97f), HiddenDim = new Color(0.02f, 0f, 0.05f, 0.76f);
        bool hiddenCard;
        readonly UIBuild.ButtonParts action, again, toTitle, skip, view;
        /// <summary>Set by UIRoot: the scenic view (V) - the ending card's eye button hides the card to watch the scene.</summary>
        public System.Func<bool> OnView;
        readonly VerticalLayoutGroup layout;
        /// <summary>Ending card, ISeasonFlow.CanExtendSeason: "Winter season: 3 more nights" + the teaser, over the buttons.</summary>
        readonly TextMeshProUGUI offer;
        /// <summary>Winter-season intro / summary: "WINTER SEASON" ribbon on the top edge (the hidden badge's slot).</summary>
        readonly HiddenBadge winterRibbon;
        bool winterCard, christmasCard, offerShown, snowShown;
        /// <summary>KEEP THE HOTEL OPEN: winter blue, a slow glow (0.5 Hz).</summary>
        public static readonly Color KeepOpenBtn = UIStyle.Hex("#2C6496"), KeepOpenGlow = UIStyle.Hex("#3F86C4");
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
            dimGroup = dim.gameObject.AddComponent<CanvasGroup>();

            cardEdge = UIBuild.Box("Card", dim, UIStyle.CardEdge);
            card = cardEdge.rectTransform;
            UIBuild.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 300f));
            var fill = cardFill = UIBuild.Box("Fill", card, UIStyle.Card);
            UIBuild.Stretch(fill.rectTransform, 3f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(36, 36, 28, 28);
            v.spacing = 16f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = UIBuild.Heading(UIBuild.Text("Title", card, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true));
            body = UIBuild.Text("Body", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            // intro: "TODAY: <the day's twist>" in the card's accent colour (+ the quiet rain line)
            today = UIBuild.Text("Today", card, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center);
            today.gameObject.SetActive(false);
            journey = BuildJourney();
            extra = UIBuild.Text("Extra", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            extra.lineSpacing = 4f;
            // summary: the grade's hint line; ending: the best record
            hint = UIBuild.Text("Hint", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            hint.gameObject.SetActive(false);
            // ending card only: "Endings 3/4 unlocked" (+ NEW!) above the buttons (24px: at 12px Mulmaru the NEW! was lost)
            unlockLine = UIBuild.Text("Unlocks", card, "", 24f, UIStyle.InkDim, TextAlignmentOptions.Center);
            unlockLine.gameObject.SetActive(false);
            offer = UIBuild.Text("SeasonOffer", card, "", 24f, WinterDeco.Ice, TextAlignmentOptions.Center);
            offer.lineSpacing = 2f;
            offer.gameObject.SetActive(false);
            var row = UIBuild.Rect("Buttons", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            action = UIBuild.Button("Action", row, "", 24f, UIStyle.Action, OnAction);
            action.Label.color = UIStyle.Ink;
            action.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f;
            // ending card with the winter season on offer: PLAY AGAIN steps aside next to TITLE (Enter = keep open)
            again = UIBuild.Button("PlayAgain", row, "", 24f, UIStyle.Btn, OnAgain);
            again.Label.color = UIStyle.Ink;
            again.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            again.Rect.gameObject.SetActive(false);
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
            stampLetter = UIBuild.Heading(UIBuild.Text("Letter", stamp, "", 96f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true));
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

            BuildDecoration();

            // ending card: a small eye button in the top-right corner (V) - hide the card and watch the ending scene
            view = UIBuild.Button("ViewScene", card, "V", 12f, UIStyle.Btn, () => ctx.Sound(OnView != null && OnView()));
            view.Rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIBuild.Place(view.Rect, new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(ViewSize, ViewSize));
            UIBuild.Outline("Frame", view.Rect, UIStyle.PanelEdge, 1f, 0f);
            var eye = new GameObject("Eye", typeof(RectTransform)).AddComponent<RawImage>();
            eye.raycastTarget = false;
            eye.transform.SetParent(view.Rect, false);
            eye.texture = ScenicView.PixelTexture(ScenicView.Eye, UIStyle.Ink);
            UIBuild.Place(eye.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(ScenicView.Eye[0].Length * 2f, ScenicView.Eye.Length * 2f));
            view.Label.color = UIStyle.InkDim;
            view.Label.alignment = TextAlignmentOptions.BottomRight;
            UIBuild.Stretch(view.Label.rectTransform, 2f);
            view.Rect.gameObject.SetActive(false);

            hiddenBadge = new HiddenBadge("HiddenBadge", card, 24f, 18f, BadgeHeight);
            hiddenBadge.Root.anchorMin = hiddenBadge.Root.anchorMax = new Vector2(0.5f, 1f);
            hiddenBadge.Root.pivot = new Vector2(0.5f, 0.5f);
            hiddenBadge.Root.anchoredPosition = Vector2.zero; // centred on the top edge
            hiddenBadge.Show(false);
            winterRibbon = new HiddenBadge("WinterRibbon", card, 24f, 18f, BadgeHeight, WinterDeco.Ice, RibbonPlate);
            winterRibbon.Root.anchorMin = winterRibbon.Root.anchorMax = new Vector2(0.5f, 1f);
            winterRibbon.Root.pivot = new Vector2(0.5f, 0.5f);
            winterRibbon.Root.anchoredPosition = Vector2.zero;
            winterRibbon.Show(false);
            BuildWinter();

            dim.gameObject.SetActive(false);
        }

        /// <summary>Row of small tilted stamps (letter in a ring) with the day number under each.</summary>
        RectTransform BuildJourney()
        {
            var row = UIBuild.Rect("Journey", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = JourneyCell + 4f + JourneyDayH;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = JourneyGap;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = false;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            for (int i = 0; i < MaxJourneyDays; i++)
            {
                var cell = UIBuild.Rect("Day" + (i + 1), row);
                cell.sizeDelta = new Vector2(JourneyCell, JourneyCell + 4f + JourneyDayH);
                var face = UIBuild.Rect("Stamp", cell);
                UIBuild.Place(face, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(JourneyCell, JourneyCell));
                face.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -5f : 4f); // a stamped look; the gap keeps tilted corners apart
                UIBuild.Stretch(UIBuild.Box("Plate", face, new Color(0f, 0f, 0f, 0.35f)).rectTransform);
                var ring = UIBuild.Outline("Ring", face, UIStyle.Lucky, 3f, 0f);
                var letter = UIBuild.Heading(UIBuild.Text("Letter", face, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true));
                UIBuild.Stretch(letter.rectTransform);
                letter.textWrappingMode = TextWrappingModes.NoWrap;
                var day = UIBuild.Text("Number", cell, "", 24f, UIStyle.InkDim, TextAlignmentOptions.Center);
                UIBuild.Place(day.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(JourneyCell + JourneyGap, JourneyDayH));
                day.textWrappingMode = TextWrappingModes.NoWrap;
                journeyStamps[i] = new JourneyStamp { Root = cell, Letter = letter, Day = day, Ring = ring.GetComponentsInChildren<Image>() };
            }
            row.gameObject.SetActive(false);
            return row;
        }

        // ------------------------------------------------------------------ decoration (outside the layout, no raycasts)

        /// <summary>Wedding day accents (Day 6): rose on the gold card.</summary>
        public static readonly Color WeddingInk = UIStyle.Hex("#FF8FB8");
        const float InnerInset = 8f, StudSize = 6f, RuleW = 150f, RuleGap = 26f;

        Image[] innerFrame, studs, rules;
        Image ruleGem;
        /// <summary>The ornamental rule sits in the middle of the title-to-body gap (the gap shrinks on the offer card).</summary>
        RectTransform ruleRect;
        RectTransform ruleRings, weddingDeco, starDots;
        bool weddingCard;

        /// <summary>
        /// Second, thin frame inside the card edge with a stud on each corner, an ornamental rule under the title
        /// (line - gem - line; twin rings on the wedding day), and the day themes: confetti (wedding day), faint star
        /// dust (star night); Friday the 13th is just the red frame / rule (no corner marks). Everything stays inside the card's padding / title band.
        /// </summary>
        void BuildDecoration()
        {
            var frame = UIBuild.Outline("InnerFrame", card, UIStyle.CardEdge, 2f, -InnerInset);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.SetSiblingIndex(1); // right above the fill: under every text
            innerFrame = frame.GetComponentsInChildren<Image>();
            studs = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                // centred on the inner frame's corners
                var corner = new Vector2(i % 2, i / 2);
                var s = UIBuild.Box("Stud" + i, frame, UIStyle.CardEdge);
                UIBuild.Place(s.rectTransform, corner, new Vector2(corner.x > 0.5f ? StudSize * 0.5f - 1f : 1f - StudSize * 0.5f, corner.y > 0.5f ? StudSize * 0.5f - 1f : 1f - StudSize * 0.5f), new Vector2(StudSize, StudSize));
                studs[i] = s;
            }

            // rule under the title, in the gap before the next line (child of the title: it follows a wrapped title down)
            var rule = ruleRect = UIBuild.Rect("Rule", title.transform);
            rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 0f);
            rule.pivot = new Vector2(0.5f, 0.5f);
            rule.anchoredPosition = new Vector2(0f, -8f);
            rule.sizeDelta = new Vector2(2f * RuleW + 2f * RuleGap, 12f);
            rules = new Image[4];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var line = UIBuild.Box("Line" + i, rule, UIStyle.CardEdge);
                var lrt = line.rectTransform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(side * (RuleGap + RuleW * 0.5f), 0f);
                lrt.sizeDelta = new Vector2(RuleW, 2f);
                rules[i] = line;
                var dot = UIBuild.Box("Dot" + i, rule, UIStyle.CardEdge);
                var drt = dot.rectTransform;
                drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
                drt.anchoredPosition = new Vector2(side * (RuleGap - 8f), 0f);
                drt.sizeDelta = new Vector2(4f, 4f);
                rules[2 + i] = dot;
            }
            ruleGem = UIBuild.Diamond("Gem", rule, Vector2.zero, 8f, UIStyle.CardEdge);
            // wedding day: two interlocked rings instead of the gem
            ruleRings = UIBuild.Rect("Rings", rule);
            ruleRings.anchorMin = ruleRings.anchorMax = ruleRings.pivot = new Vector2(0.5f, 0.5f);
            ruleRings.anchoredPosition = Vector2.zero;
            ruleRings.sizeDelta = new Vector2(24f, 12f);
            for (int i = 0; i < 2; i++)
            {
                var ring = UIBuild.Rect("Ring" + i, ruleRings);
                ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
                ring.anchoredPosition = new Vector2(i == 0 ? -4f : 4f, 0f);
                ring.sizeDelta = new Vector2(10f, 10f);
                ring.localRotation = Quaternion.Euler(0f, 0f, 45f);
                UIBuild.Outline("Band", ring, i == 0 ? WeddingInk : UIStyle.Lucky, 2f, 0f);
            }
            ruleRings.gameObject.SetActive(false);

            // wedding day: confetti in the top corners; star night: star dust there
            weddingDeco = Layer("WeddingDeco");
            starDots = Layer("StarDust");
            for (int side = 0; side < 2; side++)
            {
                var wc = Corner(weddingDeco, side == 1, 130f, 64f);
                var sc = Corner(starDots, side == 1, 130f, 64f);
                for (int i = 0; i < Sprinkle.Length; i++)
                {
                    var p = Sprinkle[i];
                    var at = new Vector2((side == 0 ? p.x : 130f - p.x) - 65f, p.y - 32f);
                    var confetti = UIBuild.Diamond("Confetti", wc, at, p.z + 2f, i % 3 == 0 ? UIStyle.Lucky : i % 3 == 1 ? WeddingInk : UIStyle.Ink);
                    if (i % 2 == 0) confetti.rectTransform.localRotation = Quaternion.identity;
                    var dust = UIBuild.Box("Dust", sc, i % 2 == 0 ? HudView.StarInk : StarTitle);
                    var drt = dust.rectTransform;
                    drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
                    drt.anchoredPosition = at;
                    drt.sizeDelta = new Vector2(p.z, p.z);
                }
            }
            weddingDeco.gameObject.SetActive(false);
            starDots.gameObject.SetActive(false);
            SetDecoration(UIStyle.CardEdge, false, false, false);
        }

        /// <summary>x, y inside a 130x64 corner box (from its outer bottom corner), size.</summary>
        static readonly Vector3[] Sprinkle =
        {
            new Vector3(22f, 46f, 4f), new Vector3(44f, 30f, 2f), new Vector3(62f, 50f, 4f), new Vector3(84f, 22f, 2f),
            new Vector3(100f, 44f, 4f), new Vector3(30f, 14f, 2f), new Vector3(116f, 28f, 2f),
        };

        RectTransform Layer(string name)
        {
            var rt = UIBuild.Stretch(UIBuild.Rect(name, card));
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.SetSiblingIndex(1); // right above the fill: under every text
            return rt;
        }

        /// <summary>Box in a top corner of the card, just inside the inner frame.</summary>
        static RectTransform Corner(RectTransform layer, bool right, float w, float h)
        {
            var rt = UIBuild.Rect(right ? "TopRight" : "TopLeft", layer);
            UIBuild.Place(rt, new Vector2(right ? 1f : 0f, 1f), new Vector2(right ? -14f : 14f, -14f), new Vector2(w, h));
            return rt;
        }

        // ------------------------------------------------------------------ winter season: flakes, holly, falling snow

        static readonly Color RibbonPlate = new Color(0.03f, 0.06f, 0.11f, 0.98f), RibbonPlateXmas = new Color(0.26f, 0.04f, 0.04f, 0.98f);
        public const int SnowfallFlakes = 16;
        RectTransform winterDeco, hollyDeco, snowfall;
        readonly RectTransform[] cornerFlakes = new RectTransform[4];
        readonly RectTransform[] fallFlakes = new RectTransform[SnowfallFlakes];
        readonly Vector4[] fallSeeds = new Vector4[SnowfallFlakes];

        /// <summary>Snowflakes on the four corners, holly sprigs (Christmas), a few flakes drifting down behind the text.</summary>
        void BuildWinter()
        {
            winterDeco = UIBuild.Stretch(UIBuild.Rect("WinterDeco", card));
            winterDeco.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var anchor = UIBuild.Rect("Corner" + i, winterDeco);
                UIBuild.Place(anchor, corner, Vector2.zero, Vector2.zero);
                anchor.pivot = new Vector2(0.5f, 0.5f);
                cornerFlakes[i] = WinterDeco.Snowflake("Flake", anchor, Vector2.zero, i < 2 ? 26f : 34f, WinterDeco.Ice);
            }
            winterDeco.gameObject.SetActive(false);

            hollyDeco = UIBuild.Stretch(UIBuild.Rect("HollyDeco", card));
            hollyDeco.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var spots = new[] { new Vector2(0.14f, 1f), new Vector2(0.86f, 0f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var anchor = UIBuild.Rect("Spot" + i, hollyDeco);
                UIBuild.Place(anchor, spots[i], Vector2.zero, Vector2.zero);
                anchor.pivot = new Vector2(0.5f, 0.5f);
                WinterDeco.HollySprig("Holly", anchor, Vector2.zero, 3);
            }
            hollyDeco.gameObject.SetActive(false);

            snowfall = Layer("Snowfall");
            var rng = new System.Random(1225);
            for (int i = 0; i < SnowfallFlakes; i++)
            {
                float size = i % 3 == 0 ? 4f : 2f;
                var f = UIBuild.Box("Snow" + i, snowfall, new Color(1f, 1f, 1f, i % 3 == 0 ? 0.75f : 0.5f)).rectTransform;
                f.anchorMin = f.anchorMax = new Vector2(0f, 1f);
                f.pivot = new Vector2(0.5f, 0.5f);
                f.sizeDelta = new Vector2(size, size);
                fallFlakes[i] = f;
                // x 0..1, fall speed px/s, sway phase, start offset 0..1
                fallSeeds[i] = new Vector4((float)rng.NextDouble(), 18f + 22f * (float)rng.NextDouble(), (float)rng.NextDouble() * 6.28f, (float)rng.NextDouble());
            }
            snowfall.gameObject.SetActive(false);
        }

        /// <summary>The corner flakes stay still; the falling snow drifts down and sways (no flashing).</summary>
        void TickWinter()
        {
            if (!dim.gameObject.activeSelf) return;
            float t = Time.unscaledTime;
            if (snowfall.gameObject.activeSelf)
            {
                var size = card.rect.size;
                if (size.y < 10f) return;
                for (int i = 0; i < fallFlakes.Length; i++)
                {
                    var seed = fallSeeds[i];
                    float y = Mathf.Repeat(seed.w * size.y + t * seed.y, size.y + 8f);
                    float x = seed.x * size.x + 10f * Mathf.Sin(t * 0.8f + seed.z);
                    fallFlakes[i].anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(x, 6f, size.x - 6f)), -Mathf.Round(y));
                }
            }
            if (offerShown)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(t * 3.1f); // ~0.5 Hz
                action.Background.color = Color.Lerp(KeepOpenBtn, KeepOpenGlow, k * 0.8f);
            }
        }

        /// <summary>Recolours the frame / studs / rule for the card look and shows the day theme's accents.</summary>
        void SetDecoration(Color edge, bool friday, bool star, bool wedding)
        {
            var soft = new Color(edge.r, edge.g, edge.b, 0.55f);
            foreach (var e in innerFrame) e.color = soft;
            foreach (var s in studs) { s.color = edge; s.gameObject.SetActive(!star && !(winterDeco != null && winterDeco.gameObject.activeSelf)); } // star night / winter: stars / flakes sit there
            var ruleInk = wedding ? WeddingInk : edge;
            foreach (var r in rules) r.color = new Color(ruleInk.r, ruleInk.g, ruleInk.b, 0.8f);
            ruleGem.color = ruleInk;
            ruleGem.gameObject.SetActive(!wedding);
            ruleRings.gameObject.SetActive(wedding);
            weddingDeco.gameObject.SetActive(wedding);
            starDots.gameObject.SetActive(star);
        }

        /// <summary>The wedding day has no flag in DayDef: it is the day wedding guests check in (ids "..wedding..").</summary>
        static bool IsWeddingDay(DayDef d)
        {
            if (d == null || d.spawns == null) return false;
            foreach (var s in d.spawns)
                if (s != null && s.guest != null && s.guest.IndexOf("wedding", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public bool WeddingLook => dim.gameObject.activeSelf && weddingCard;

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
                case DayPhase.Ending:
                    // the winter season on offer: the main button keeps the hotel open (PLAY AGAIN is its own button then)
                    if (ctx.CanExtendSeason) { ok = ctx.SeasonFlow.ExtendSeason(); break; }
                    ctx.Flow.RestartGame(); ok = true; break;
                default: ok = false; break;
            }
            ctx.Sound(ok ? UiSoundKind.Confirm : UiSoundKind.Error);
        }

        void OnAgain()
        {
            if (ctx.Flow == null) { ctx.Sound(UiSoundKind.Error); return; }
            ctx.Flow.RestartGame();
            ctx.Sound(UiSoundKind.Confirm);
        }

        /// <summary>Ending card with the winter season on offer: Enter = KEEP THE HOTEL OPEN (UIRoot owns Enter then).</summary>
        public bool OwnsEnter => Shown == DayPhase.Ending && offerShown && ctx.CanExtendSeason && CardUp;

        /// <summary>The card is fully shown and clickable (not in the ending beat / its fade-in).</summary>
        public bool CardUp => dim.gameObject.activeSelf && beatAge < 0f && fadeAge < 0f;
        /// <summary>The ending scene plays alone (the card is hidden; any key / click skips to it).</summary>
        public bool InEndingBeat => beatAge >= 0f && dim.gameObject.activeSelf;
        public float EndingBeatAge => beatAge;
        /// <summary>Enter belongs to the UI during the beat, the card's fade-in and the frame after a skip: it never also
        /// restarts / keeps the hotel open through Gameplay's Enter shortcut.</summary>
        public bool HoldsEnter => InEndingBeat || fadeAge >= 0f || Time.frameCount <= enterHeldUntilFrame;

        void StartBeat()
        {
            beatAge = 0f;
            fadeAge = -1f;
            dimGroup.alpha = 0f;
            dimGroup.interactable = false;
            dimGroup.blocksRaycasts = true; // a click during the beat lands on the (invisible) dim, never on a button
        }

        void StopBeat()
        {
            beatAge = fadeAge = -1f;
            dimGroup.alpha = 1f;
            dimGroup.interactable = true;
            dimGroup.blocksRaycasts = true;
        }

        /// <summary>The beat is over (time, or a key / click): the card fades in; that press does nothing else.</summary>
        public void SkipEndingBeat()
        {
            if (beatAge < 0f) return;
            beatAge = -1f;
            fadeAge = 0f;
            enterHeldUntilFrame = Time.frameCount + 1;
        }

        void TickEndingBeat()
        {
            if (beatAge >= 0f)
            {
                if (!dim.gameObject.activeSelf || shown != DayPhase.Ending) { StopBeat(); return; }
                if (!ctx.Paused) beatAge += UIClock.UnscaledDt;
                if (beatAge >= BeatFor(ctx.Day != null ? ctx.Day.Ending : EndingKind.None)) SkipEndingBeat();
                else return;
            }
            if (fadeAge < 0f) return;
            fadeAge += UIClock.UnscaledDt;
            float k = Mathf.Clamp01(fadeAge / CardFadeIn);
            dimGroup.alpha = k * k * (3f - 2f * k);
            card.anchoredPosition = new Vector2(0f, Mathf.Round(-24f * (1f - k) * (1f - k))); // rises into place
            if (k >= 1f) StopBeat();
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
        public void ClickAgain() => again.Button.onClick.Invoke();
        public void ClickView() => view.Button.onClick.Invoke();
        public bool ViewButtonVisible => dim.gameObject.activeSelf && view.Rect.gameObject.activeSelf;
        public Rect ViewButtonRect(RectTransform canvas) => UIBuild.CanvasRect(canvas, view.Rect);
        public bool AgainVisible => dim.gameObject.activeSelf && again.Rect.gameObject.activeSelf;
        public string AgainText => again.Label.text;
        public string OfferText => dim.gameObject.activeSelf && offer.gameObject.activeSelf ? offer.text : null;
        public bool WinterLook => dim.gameObject.activeSelf && winterCard;
        public bool ChristmasLook => dim.gameObject.activeSelf && christmasCard;
        public bool WinterRibbonVisible => dim.gameObject.activeSelf && winterRibbon.Visible;
        public string WinterRibbonText => winterRibbon.Label.text;
        public bool SnowfallVisible => dim.gameObject.activeSelf && snowfall.gameObject.activeSelf;
        public bool CornerFlakesVisible => dim.gameObject.activeSelf && winterDeco.gameObject.activeSelf;
        public bool HollyVisible => dim.gameObject.activeSelf && hollyDeco.gameObject.activeSelf;
        public Rect WinterRibbonRect(RectTransform canvas) => WinterRibbonVisible ? UIBuild.CanvasRect(canvas, winterRibbon.Root) : default;
        public bool TitleButtonVisible => dim.gameObject.activeSelf && toTitle.Rect.gameObject.activeSelf;
        public string ActionText => action.Label.text;

        public void Tick()
        {
            TickEndingBeat();
            TickStamp();
            TickStars();
            TickWinter();
            if (hiddenCard && dim.gameObject.activeSelf) hiddenBadge.Tick(Time.unscaledTime);
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
            bool enteringEnding = phase == DayPhase.Ending && shown != DayPhase.Ending;
            if (phase != DayPhase.Ending) StopBeat();
            else if (enteringEnding && UIFeatures.EndingBeat) StartBeat();
            shown = phase;
            shownDay = ctx.HasGame ? ctx.Day.CurrentDay.Number : -1;
            if (beatAge < 0f && fadeAge < 0f) card.anchoredPosition = Vector2.zero; // a stamp jolt never carries over
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
            bool hidden = phase == DayPhase.Ending && EndingRecord.IsHidden(ctx.Day.Ending);
            // winter season: extended days' intro / summary, the two winter endings; Christmas Eve (and the miracle) in red + green
            bool daily = (phase == DayPhase.Intro || phase == DayPhase.Summary) && !ctx.Day.IsTutorial;
            var ending = phase == DayPhase.Ending ? ctx.Day.Ending : EndingKind.None;
            bool canExtend = phase == DayPhase.Ending && ctx.CanExtendSeason;
            bool christmas = (daily && ctx.IsChristmas) || ending == EndingKind.WinterMiracle;
            bool winter = !christmas && ((daily && ctx.IsExtendedDay) || ending == EndingKind.SilentNight);
            SetCardLook(intro && ctx.Day.IsFriday13, intro && ctx.Day.IsStarNight, hidden, intro && ctx.TryGetDay(out var todayDef) && IsWeddingDay(todayDef), winter, christmas);
            hiddenBadge.Show(hidden);
            if (hidden) { hiddenBadge.SetText(ctx.S(UIStrings.HiddenEnding)); hiddenBadge.Alpha = 1f; }
            bool ribbon = daily && (ctx.IsExtendedDay || ctx.IsChristmas) && !hidden;
            winterRibbon.Show(ribbon);
            if (ribbon)
            {
                winterRibbon.SetColors(christmasCard ? ChristmasRibbonInk : WinterDeco.Ice, christmasCard ? RibbonPlateXmas : RibbonPlate);
                winterRibbon.SetText(ctx.S(UIStrings.WinterSeason));
            }
            // corner flakes on every winter look and on the "keep the hotel open" offer; falling snow on the offer,
            // the winter endings and Christmas Eve's intro
            bool flakes = winterCard || christmasCard || canExtend;
            if (winterDeco.gameObject.activeSelf != flakes) { winterDeco.gameObject.SetActive(flakes); SetDecoration(cardEdge.color, fridayCard, starCard, weddingCard); }
            hollyDeco.gameObject.SetActive(christmasCard);
            snowShown = canExtend || UIFormat.IsWinterEnding(ending) || (christmasCard && phase == DayPhase.Intro);
            snowfall.gameObject.SetActive(snowShown);
            offerShown = canExtend;
            offer.gameObject.SetActive(canExtend);
            again.Rect.gameObject.SetActive(canExtend);
            layout.spacing = canExtend ? 10f : 16f;
            ruleRect.anchoredPosition = new Vector2(0f, -Mathf.Round(layout.spacing * 0.5f));
            if (!canExtend) action.Background.color = UIStyle.Action;
            unlockLine.gameObject.SetActive(phase == DayPhase.Ending && ctx.Endings != null);
            today.gameObject.SetActive(false);
            hint.gameObject.SetActive(false);
            journey.gameObject.SetActive(false);
            title.color = fridayCard ? UIStyle.Unlucky : starCard ? StarTitle : winterCard ? WinterDeco.Ice : UIStyle.Lucky;
            extra.alignment = TextAlignmentOptions.Left; // the Friday-only intro line centres it (FillIntro)
            toTitle.Rect.gameObject.SetActive(phase == DayPhase.Ending && ctx.Menu != null);
            view.Rect.gameObject.SetActive(phase == DayPhase.Ending && OnView != null && ctx.Menu != null);
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
            ctx.TryGetDay(out var d);
            if (d != null)
            {
                t = ko && !string.IsNullOrEmpty(d.titleKo) ? d.titleKo : d.title;
                intro = ko && !string.IsNullOrEmpty(d.introKo) ? d.introKo : d.intro;
                // the rare mystery guest's night (never on a first run): one more, eerie sentence
                string mystery = ko && !string.IsNullOrEmpty(d.mysteryIntroKo) ? d.mysteryIntroKo : d.mysteryIntro;
                if (!string.IsNullOrEmpty(mystery) && ctx.Season != null && ctx.Season.MysteryTonight)
                    intro = (string.IsNullOrEmpty(intro) ? "" : intro + " ") + "<color=" + UIFormat.HiddenHex + ">" + mystery + "</color>";
            }
            if (string.IsNullOrEmpty(t)) t = UIFormat.DayLabel(ctx.Day.DisplayDay, ctx.Day.PlayableDayCount, tutorial, ko);
            title.text = starCard ? UIFonts.Star + "  " + t + "  " + UIFonts.Star : t;
            body.text = intro;
            body.gameObject.SetActive(!string.IsNullOrEmpty(intro));
            // the day's twist, plainly, in the card's accent colour (every day has one); the rain line under it
            string line = DayTwist.Line(d, ctx.Data, ko) ?? "";
            if (!tutorial && ctx.Day.IsRainy)
                line += (line.Length > 0 ? "\n" : "") + "<color=" + RainHex + ">" + ctx.S(UIStrings.RainIntro) + "</color>"; // presentation only
            today.text = line;
            today.gameObject.SetActive(line.Length > 0);
            // Day 0 teaches step by step in the game itself; the real Day 1 gets the list as a recap (hints on)
            bool howTo = !tutorial && ctx.Settings != null && ctx.Settings.ShowTutorialHints && ctx.Day.DisplayDay == 1;
            extra.gameObject.SetActive(howTo);
            extra.text = howTo
                ? "<b><color=#C9A15A>" + ctx.S(UIStrings.HowToTitle) + "</color></b>\n" + string.Format(ctx.S(UIStrings.HowTo), UIFormat.LuckyHex, UIFormat.UnluckyHex)
                : "";
            action.Label.text = ctx.S(UIStrings.Start);
        }

        public const string RainHex = "#8FB8E8";

        public static readonly Color StarTitle = UIStyle.Hex("#FFF1B0");

        /// <summary>Card colours for the event day (Friday the 13th wins over star night; neither = the normal card),
        /// or the hidden 4th-floor ending.</summary>
        void SetCardLook(bool friday, bool star, bool hidden = false, bool wedding = false, bool winter = false, bool christmas = false)
        {
            star &= !friday;
            hidden &= !friday && !star;
            christmas &= !friday && !star && !hidden;
            winter &= !friday && !star && !hidden && !christmas;
            wedding &= !friday && !star && !hidden && !winter && !christmas;
            if (friday == fridayCard && star == starCard && hidden == hiddenCard && wedding == weddingCard && winter == winterCard && christmas == christmasCard) return;
            fridayCard = friday;
            starCard = star;
            hiddenCard = hidden;
            weddingCard = wedding;
            winterCard = winter;
            christmasCard = christmas;
            cardEdge.color = friday ? UIStyle.Unlucky : star ? HudView.StarEdge : hidden ? HiddenBadge.Ink
                : christmas ? WinterDeco.ChristmasEdge : winter ? WinterDeco.IceEdge : UIStyle.CardEdge;
            cardFill.color = friday ? FridayFill : star ? StarFill : hidden ? HiddenFill
                : christmas ? WinterDeco.ChristmasFill : winter ? WinterDeco.IceFill : UIStyle.Card;
            dim.GetComponent<Image>().color = friday ? FridayDim : star ? StarDim : hidden ? HiddenDim
                : christmas ? WinterDeco.ChristmasDim : winter ? WinterDeco.IceDim : UIStyle.Dim;
            starDeco.gameObject.SetActive(star);
            SetDecoration(cardEdge.color, friday, star, wedding);
        }

        public const float BadgeHeight = 36f, ViewSize = 36f;
        static readonly Color ChristmasRibbonInk = UIStyle.Hex("#FFE3C2");

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
            bool merry = !tutorial && ctx.IsChristmas;
            body.gameObject.SetActive(tutorial || merry);
            if (tutorial) body.text = "<color=" + UIFormat.LuckyHex + ">" + ctx.S(UIStrings.PracticeReset) + "</color>";
            else if (merry) body.text = "<color=" + WinterDeco.HollyHex + ">" + ctx.S(UIStrings.MerryChristmas) + "</color>";
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
            if (!tutorial)
            {
                string grade = UIFormat.DayGrade(s);
                FillStamp(grade);
                // one line under the numbers: what earned / cost the grade
                hint.text = "<color=" + GradeHex(grade) + ">" + SummaryHint.Text(s, ko) + "</color>";
                hint.gameObject.SetActive(true);
            }
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
            title.color = ctx.Day.Ending == EndingKind.Haunted || ctx.Day.Ending == EndingKind.FourthFloor ? UIStyle.Unlucky
                : ctx.Day.Ending == EndingKind.SilentNight ? WinterDeco.Ice : UIStyle.Lucky;
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
            var j = ctx.Journey;
            bool recap = j != null && j.Count > 0;
            extra.text =
                Row(ctx.S(UIStrings.EndDayReached), N(ctx.Day.DisplayDay) + " / " + N(ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount)) + "\n" +
                Row(ctx.S(UIStrings.EndFinalLuck), "<color=" + luckHex + ">" + N(luck) + "</color> / " + N(max)) + "\n" +
                (recap
                    ? Row(ctx.S(UIStrings.TipsEarned), "<color=#5BE37D>$" + N(j.TipsEarned) + "</color>") + "\n" +
                      Row(ctx.S(UIStrings.Delivered), N(j.Delivered)) + "\n" +
                      Row(ctx.S(UIStrings.BestStreak), (j.BestStreak > 0 ? "<color=" + UIFormat.LuckyHex + ">x" : "<color=#F4EEDC>x") + N(j.BestStreak) + "</color>")
                    : Row(ctx.S(UIStrings.EndTips), "<color=#5BE37D>$" + N(tips) + "</color>"));
            FillJourney();
            FillBest();
            if (ctx.Endings != null)
            {
                string line = ctx.F(UIStrings.EndingsUnlocked, ctx.Endings.Unlocked, EndingCollection.Total);
                if (ctx.Endings.LastNew != EndingKind.None && ctx.Endings.LastNew == ctx.Day.Ending)
                    line += "   <color=" + UIFormat.LuckyHex + ">" + ctx.S(UIStrings.EndingNew) + "</color>";
                unlockLine.text = line;
            }
            if (offerShown)
            {
                int extra = ctx.Data != null && ctx.Data.ExtendedDayCount > 0 ? ctx.Data.ExtendedDayCount : 3;
                offer.text = ctx.F(UIStrings.SeasonOffer, extra) + "\n<color=#F4EEDCAA>" + ctx.S(UIStrings.SeasonTeaser) + "</color>";
                action.Label.text = ctx.S(UIStrings.KeepOpen);
                again.Label.text = ctx.S(UIStrings.PlayAgainShort);
            }
            else action.Label.text = ctx.S(UIStrings.PlayAgain);
        }

        /// <summary>One stamp per real day: its grade in the summary stamp's colour, "-" (dim) for a day not reached.</summary>
        void FillJourney()
        {
            int days = Mathf.Min(MaxJourneyDays, ctx.Day.PlayableDayCount);
            var j = ctx.Journey;
            bool show = days > 0 && j != null && j.Count > 0;
            journey.gameObject.SetActive(show);
            if (!show) return;
            int baseDays = ctx.Data != null && ctx.Data.PlayableDayCount > 0 ? ctx.Data.PlayableDayCount : days;
            for (int i = 0; i < MaxJourneyDays; i++)
            {
                var st = journeyStamps[i];
                st.Root.gameObject.SetActive(i < days);
                if (i >= days) continue;
                string g = j.GradeOf(i + 1);
                var c = g != null ? GradeColor(g) : UIStyle.InkDim;
                st.Letter.text = g ?? "-";
                st.Letter.color = c;
                foreach (var e in st.Ring) e.color = g != null ? c : new Color(c.r, c.g, c.b, 0.3f);
                st.Day.text = N(i + 1);
                st.Day.color = i + 1 > baseDays ? WinterDeco.Ice : UIStyle.InkDim; // the winter season's nights
            }
        }

        /// <summary>Best run so far (saved with the endings): small, with NEW BEST! when this run beat it.</summary>
        void FillBest()
        {
            var e = ctx.Endings;
            bool show = e != null && e.Record.HasBest;
            hint.gameObject.SetActive(show);
            if (!show) return;
            string line = "<color=#F4EEDC99>" + ctx.F(UIStrings.EndBest, N(Mathf.Max(0, e.Record.BestLuck)), N(Mathf.Max(0, e.Record.BestTips))) + "</color>";
            if (e.NewBestLuck || e.NewBestTips) line += "   <color=" + UIFormat.LuckyHex + ">" + ctx.S(UIStrings.NewBest) + "</color>";
            hint.text = line;
        }

        void FillStamp(string grade)
        {
            var c = GradeColor(grade);
            stampLetter.text = grade;
            stampLetter.color = c;
            foreach (var e in stampEdges) e.color = c;
        }

        public static string GradeHex(string grade) =>
            grade == "S" ? UIFormat.LuckyHex : grade == "A" ? "#5BE37D" : grade == "B" ? "#F4EEDC" : UIFormat.UnluckyHex;

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

        public bool HiddenBadgeVisible => dim.gameObject.activeSelf && hiddenBadge.Visible;
        public string HiddenBadgeText => hiddenBadge.Label.text;
        public bool HiddenBadgeFits => hiddenBadge.TextFits;
        public Rect HiddenBadgeRect(RectTransform canvas) => HiddenBadgeVisible ? UIBuild.CanvasRect(canvas, hiddenBadge.Root) : default;
        public Rect TitleRect(RectTransform canvas) => UIBuild.CanvasRect(canvas, title.rectTransform);
        public bool HiddenLook => dim.gameObject.activeSelf && hiddenCard;
        public string UnlockText => unlockLine.gameObject.activeInHierarchy ? unlockLine.text : null;

        public string TitleText => title.text;
        public string BodyText => body.text;
        public string ExtraText => extra.gameObject.activeSelf ? extra.text : null;
        public string TodayText => dim.gameObject.activeSelf && today.gameObject.activeSelf ? today.text : null;
        public string HintText => dim.gameObject.activeSelf && hint.gameObject.activeSelf ? hint.text : null;
        /// <summary>Ending card stamps as "1:S 2:A 3:- ..." (null while hidden).</summary>
        public string JourneyText
        {
            get
            {
                if (!dim.gameObject.activeSelf || !journey.gameObject.activeSelf) return null;
                var sb = new System.Text.StringBuilder();
                foreach (var st in journeyStamps)
                {
                    if (!st.Root.gameObject.activeSelf) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(st.Day.text).Append(':').Append(st.Letter.text);
                }
                return sb.ToString();
            }
        }
        public string BestText => HintText;
        public Rect JourneyRect(RectTransform canvas) => journey.gameObject.activeInHierarchy ? UIBuild.CanvasRect(canvas, journey) : default;

        /// <summary>First visible card text that overflows (or a one-line text wider than its rect), or the card leaving
        /// the canvas; null when everything fits.</summary>
        public string Overflow()
        {
            if (!dim.gameObject.activeSelf) return null;
            foreach (var t in card.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!t.isActiveAndEnabled || t.transform.IsChildOf(stamp)) continue; // the big grade letter overhangs its plate by design
                t.ForceMeshUpdate();
                if (t.isTextOverflowing) return t.name + " overflows: " + t.text;
                if (t.textWrappingMode == TextWrappingModes.NoWrap && t.preferredWidth > t.rectTransform.rect.width + 1f && t.transform.parent != card)
                    return t.name + " too wide: " + t.preferredWidth.ToString("0") + " > " + t.rectTransform.rect.width.ToString("0") + " (" + t.text + ")";
            }
            var canvas = (RectTransform)dim.parent;
            var c = UIBuild.CanvasRect(canvas, card);
            var size = canvas.rect.size;
            if (c.yMin < 0f || c.yMax > size.y || c.xMin < 0f || c.xMax > size.x) return "card " + c + " leaves the canvas " + size;
            return null;
        }
    }
}
