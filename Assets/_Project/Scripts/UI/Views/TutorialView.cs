using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Day-1 step-by-step prompts (shown while IGameSettings.ShowTutorialHints &amp;&amp; IDayQuery.IsTutorial, Running,
    /// not paused). One line at a time, chosen from the live state, with a pulsing outline + arrow on the panel button
    /// to press:  Board (OPEN where guests wait) -> Close (head count) -> Floor (destination button) -> OpenThere
    /// (floor number) -> Done. A step completes on its game event and never comes back. The first LuckJudged adds a
    /// "why" card explaining what just happened. Never blocks clicks (no raycast targets).
    /// </summary>
    sealed class TutorialView
    {
        public enum Step { Board = 0, Close = 1, Floor = 2, OpenThere = 3, Done = 4, Finished = 5 }

        const int PromptSteps = 4;
        const float DoneLife = 6f, WhyLife = 7f, BannerW = 560f, BannerX = -60f;

        readonly UIContext ctx;
        readonly ElevatorPanelView panel;
        readonly RectTransform canvas, layer, banner, why, highlight, arrow;
        readonly CanvasGroup highlightGroup;
        readonly TextMeshProUGUI stepText, promptText, whyCaption, whyText, whyLegend, arrowText;
        readonly Image bannerEdge, whyEdge;
        readonly System.Collections.Generic.List<GuestId> waiting = new System.Collections.Generic.List<GuestId>();

        Step step;
        float doneTimer, whyTimer;
        bool explained, departedWithRiders;
        string targetName;

        public TutorialView(UIContext ctx, RectTransform canvas, ElevatorPanelView panel)
        {
            this.ctx = ctx;
            this.canvas = canvas;
            this.panel = panel;
            layer = UIBuild.Stretch(UIBuild.Rect("Tutorial", canvas));

            bannerEdge = UIBuild.Framed("Prompt", layer, UIStyle.Card, UIStyle.Highlight, 3f);
            banner = bannerEdge.rectTransform;
            UIBuild.Place(banner, new Vector2(0.5f, 1f), new Vector2(BannerX, -16f), new Vector2(BannerW, 92f));
            Card(banner, out stepText, out promptText, UIStyle.Highlight);

            whyEdge = UIBuild.Framed("Why", layer, UIStyle.Card, UIStyle.Lucky, 3f);
            why = whyEdge.rectTransform;
            UIBuild.Place(why, new Vector2(0.5f, 1f), new Vector2(BannerX, -124f), new Vector2(BannerW, 92f));
            Card(why, out whyCaption, out whyText, UIStyle.Lucky);
            whyLegend = UIBuild.Text("Legend", why, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);

            highlight = UIBuild.Rect("Highlight", layer);
            highlight.anchorMin = highlight.anchorMax = Vector2.zero;
            highlight.pivot = Vector2.zero;
            UIBuild.Outline("Ring", highlight, UIStyle.Highlight, 4f, 5f);
            highlightGroup = highlight.gameObject.AddComponent<CanvasGroup>();
            highlightGroup.blocksRaycasts = false;
            highlightGroup.interactable = false;

            arrowText = UIBuild.Text("Arrow", layer, "▲", 48f, UIStyle.Highlight, TextAlignmentOptions.Center, bold: true);
            arrow = arrowText.rectTransform;
            arrow.anchorMin = arrow.anchorMax = Vector2.zero;
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(56f, 56f);
            arrow.localRotation = Quaternion.Euler(0f, 0f, 90f); // ▲ turned to point left, at the panel

            Hide();
        }

        static void Card(RectTransform card, out TextMeshProUGUI small, out TextMeshProUGUI big, Color smallColor)
        {
            var fill = card.GetChild(0).gameObject;
            fill.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 10, 12);
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

        bool TutorialDay => ctx.HasGame && ctx.Day.IsTutorial;
        bool HintsOn => ctx.Settings == null || ctx.Settings.ShowTutorialHints;
        bool Live => TutorialDay && ctx.Phase == DayPhase.Running;

        // ------------------------------------------------------------------ events (UIRoot forwards them)

        public void Reset()
        {
            step = Step.Board;
            doneTimer = whyTimer = 0f;
            explained = departedWithRiders = false;
            Hide();
        }

        void Complete(Step s)
        {
            if (step > s) return;
            step = s + 1;
            if (step == Step.Done) doneTimer = DoneLife;
        }

        public void OnGuestBoarded(GuestBoarded e) { if (Live) Complete(Step.Board); }

        public void OnDoorsClosed(DoorsClosed e) { if (Live && e.Occupancy > 0) Complete(Step.Close); }

        public void OnDeparted(ElevatorDeparted e)
        {
            if (!Live || ctx.Elevator.Occupancy == 0) return;
            departedWithRiders = true;
            Complete(Step.Floor);
        }

        public void OnDoorsOpened(DoorsOpened e)
        {
            if (Live && e.Occupancy > 0 && departedWithRiders) Complete(Step.OpenThere);
        }

        public void OnLuckJudged(LuckJudged e)
        {
            if (!Live || explained || e.Verdict == LuckVerdict.Neutral) return;
            explained = true;
            whyTimer = WhyLife;
            bool lucky = e.Verdict == LuckVerdict.Lucky;
            string number = e.Number >= 0 ? e.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) ? ctx.Building.GetLabel(e.Floor).Text : "?";
            whyText.text = string.Format(ctx.S(lucky ? UIStrings.TutWhyLucky : UIStrings.TutWhyUnlucky), number);
            whyText.color = lucky ? UIStyle.Lucky : UIStyle.Unlucky;
            whyEdge.color = lucky ? UIStyle.Lucky : UIStyle.Unlucky;
            whyLegend.text = string.Format(ctx.S(UIStrings.TutWhyLegend), UIFormat.LuckyHex, UIFormat.UnluckyHex);
            whyCaption.text = ctx.S(e.Source == LuckSource.Occupancy ? UIStrings.RuleClose : UIStrings.RuleOpen);
            whyCaption.color = whyText.color;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt, float time)
        {
            if (!(Live && HintsOn) || ctx.Paused) { Hide(); return; }

            if (whyTimer > 0f) whyTimer -= dt;
            if (step == Step.Done)
            {
                doneTimer -= dt;
                if (doneTimer <= 0f) step = Step.Finished;
            }

            RectTransform target = null;
            string text = Prompt(ref target);

            Show(banner, text != null);
            if (text != null)
            {
                UIBuild.Set(promptText, text);
                UIBuild.Set(stepText, step < Step.Done
                    ? string.Format(ctx.S(UIStrings.TutStep), (int)step + 1, PromptSteps)
                    : ctx.S(UIStrings.HowToTitle));
            }

            bool showWhy = whyTimer > 0f;
            Show(why, showWhy);
            if (showWhy)
            {
                // keep it right under the banner (or in its place when there is no prompt)
                float y = text != null ? -16f - banner.rect.height - 8f : -16f;
                if (Mathf.Abs(why.anchoredPosition.y - y) > 0.5f) why.anchoredPosition = new Vector2(BannerX, y);
            }

            targetName = target != null ? target.name : null;
            Show(highlight, target != null);
            Show(arrow, target != null);
            if (target != null)
            {
                var r = UIBuild.CanvasRect(canvas, target);
                highlight.anchoredPosition = r.min;
                highlight.sizeDelta = r.size;
                float pulse = 0.55f + 0.45f * Mathf.Sin(time * 7f);
                highlightGroup.alpha = pulse;
                var p = UIBuild.CanvasRect(canvas, panel.Root);
                arrow.anchoredPosition = new Vector2(p.xMax + 30f + Mathf.Sin(time * 7f) * 5f, r.center.y);
            }
        }

        /// <summary>The current step's line when its situation holds (else null), and the button to press.</summary>
        string Prompt(ref RectTransform target)
        {
            var e = ctx.Elevator;
            bool doorsOpen = e.Doors == DoorState.Open || e.Doors == DoorState.Opening;
            bool idle = e.Motion == ElevatorMotion.Idle && !e.HasTarget;
            switch (step)
            {
                case Step.Board:
                {
                    if (e.Occupancy > 0 || ctx.Guests == null) return null;
                    if (!FindWaitingFloor(out var floor)) return null;
                    string label = ctx.Building.GetLabel(floor).Text;
                    if (e.CurrentFloor == floor && idle)
                    {
                        if (doorsOpen) return null; // boarding
                        target = panel.OpenRect;
                        return string.Format(ctx.S(UIStrings.TutBoardHere), label);
                    }
                    target = panel.FloorButtonRect(floor);
                    return string.Format(ctx.S(UIStrings.TutBoardGo), label);
                }
                case Step.Close:
                    if (e.Occupancy == 0 || !doorsOpen) return null;
                    target = panel.CloseRect;
                    return ctx.S(UIStrings.TutClose);
                case Step.Floor:
                {
                    if (e.Occupancy == 0 || e.Doors != DoorState.Closed || !idle) return null;
                    if (!FirstRiderDestination(out var dest, out var destId)) return null;
                    target = panel.FloorButtonRect(dest);
                    return string.Format(ctx.S(UIStrings.TutFloor), ctx.Building.GetLabel(dest).Text, ctx.DestinationName(destId));
                }
                case Step.OpenThere:
                    if (e.Occupancy == 0 || e.Doors != DoorState.Closed || !idle) return null;
                    target = panel.OpenRect;
                    return ctx.S(UIStrings.TutOpenThere);
                case Step.Done:
                    return ctx.S(UIStrings.TutDone);
                default:
                    return null;
            }
        }

        bool FindWaitingFloor(out FloorIndex floor)
        {
            floor = ctx.Elevator.CurrentFloor;
            if (ctx.Guests.GetWaiting(floor, waiting) > 0) return true;
            for (int f = 0; f < ctx.Building.FloorCount; f++)
                if (ctx.Guests.GetWaiting(new FloorIndex(f), waiting) > 0) { floor = new FloorIndex(f); return true; }
            return false;
        }

        bool FirstRiderDestination(out FloorIndex floor, out DestinationId destination)
        {
            floor = default;
            destination = default;
            var riders = ctx.Elevator.Riders;
            for (int i = 0; i < riders.Count; i++)
            {
                if (ctx.Guests == null || !ctx.Guests.TryGet(riders[i], out var info)) continue;
                if (info.DestinationFloor == ctx.Elevator.CurrentFloor) continue;
                floor = info.DestinationFloor;
                destination = info.Destination;
                return true;
            }
            return false;
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
            targetName = null;
        }

        public void RefreshLanguage() { /* texts are rebuilt every tick from ctx.S; the why card keeps its event text */ }

        // ------------------------------------------------------------------ test hooks

        public Step CurrentStep => step;
        public string PromptText => banner.gameObject.activeSelf ? promptText.text : null;
        public string WhyText => why.gameObject.activeSelf ? whyText.text : null;
        public string TargetName => targetName;
    }
}
