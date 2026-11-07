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
    /// empty); one line at a time is chosen from the live state and the wave, with a pulsing outline + a ▼ right above
    /// the thing to press / look at:
    ///   wave 1: OPEN where she waits (go there first) -> read the chip (gold lucky / red unlucky / icon = destination;
    ///           a few seconds or until the player acts) -> CLOSE (head count) -> destination floor -> OPEN (floor number)
    ///   wave 2: "N guests together: wait until all are in, then CLOSE (N = lucky)" -> deliver
    ///   wave 3: two riders, two stops: every door-open judges everyone inside
    ///   after the last wave: closing card (Haunted / prosperous endings, impatient guests from Day 1, practice reset).
    /// Every non-neutral LuckJudged on the tutorial day adds a "why" card (unlucky floor: + stickers can relabel it).
    /// Cards sit in the empty right column under the HUD rule box - never over the building or the panel.
    /// Never blocks clicks (no raycast targets).
    /// </summary>
    sealed class TutorialView
    {
        public enum Step { None = 0, GoTo, Board, Chips, Wait, Close, Floor, OpenThere, NotTheirStop, NextWave, Closing }

        /// <summary>Waves the lesson is written for (the last one ends with the closing card).</summary>
        public const int LessonWaves = 3;
        /// <summary>Doors open, nobody boarded for this long -> CLOSE anyway (e.g. a guest who refuses this head count).</summary>
        public const float CloseFallback = 4f;
        /// <summary>How long the chip explanation stays before CLOSE is prompted (unless the player acts first).</summary>
        public const float ChipsLife = 4.5f;
        const float WhyLife = 7f, MergeWindow = 0.3f, Margin = 24f, Gap = 10f;
        /// <summary>Right column under the HUD's rule box (HudView: rules end 262 px from the top).</summary>
        public const float ColumnTop = 272f, ColumnW = 376f;

        readonly UIContext ctx;
        readonly ElevatorPanelView panel;
        readonly GuestBubblesView bubbles;
        readonly RectTransform canvas, layer, banner, why, highlight, arrow;
        readonly CanvasGroup highlightGroup;
        readonly TextMeshProUGUI stepText, promptText, whyCaption, whyText, whyNote;
        readonly Image bannerEdge, whyEdge;
        readonly InlineIcon promptIcon;
        readonly List<GuestId> waiting = new List<GuestId>();
        readonly LuckProfile[] profiles = new LuckProfile[16];

        // lesson state (reset when a tutorial day starts running)
        int wave, spawned, resolved;
        bool chipsDone, closedThisWave, departedWithRiders;
        float chipsShown, sinceBoarded, whyTimer, whyAge;
        LuckSource whySource;
        LuckVerdict whyVerdict;
        int whyNumber;

        // shown this frame
        Step step;
        string shownText, targetName;
        Rect targetRect;
        bool hasTarget;
        Sprite promptSprite;
        Color promptTint;

        public TutorialView(UIContext ctx, RectTransform canvas, ElevatorPanelView panel, GuestBubblesView bubbles)
        {
            this.ctx = ctx;
            this.canvas = canvas;
            this.panel = panel;
            this.bubbles = bubbles;
            layer = UIBuild.Stretch(UIBuild.Rect("Tutorial", canvas));

            bannerEdge = UIBuild.Framed("Prompt", layer, UIStyle.Card, UIStyle.Highlight, 3f);
            banner = bannerEdge.rectTransform;
            UIBuild.Place(banner, new Vector2(1f, 1f), new Vector2(-Margin, -ColumnTop), new Vector2(ColumnW, 92f));
            Card(banner, out stepText, out promptText, UIStyle.Highlight);
            promptIcon = new InlineIcon(promptText);

            whyEdge = UIBuild.Framed("Why", layer, UIStyle.Card, UIStyle.Lucky, 3f);
            why = whyEdge.rectTransform;
            UIBuild.Place(why, new Vector2(1f, 1f), new Vector2(-Margin, -ColumnTop), new Vector2(ColumnW, 92f));
            Card(why, out whyCaption, out whyText, UIStyle.Lucky);
            whyNote = UIBuild.Text("Note", why, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            whyNote.lineSpacing = 6f;

            highlight = UIBuild.Rect("Highlight", layer);
            highlight.anchorMin = highlight.anchorMax = Vector2.zero;
            highlight.pivot = Vector2.zero;
            UIBuild.Outline("Ring", highlight, UIStyle.Highlight, 4f, 5f);
            highlightGroup = highlight.gameObject.AddComponent<CanvasGroup>();
            highlightGroup.blocksRaycasts = false;
            highlightGroup.interactable = false;

            // ▼ straight above the target on a dark plate: it points DOWN at one button, never sideways across a row
            var plate = UIBuild.Framed("Arrow", layer, UIStyle.Dark, UIStyle.Highlight, 2f);
            arrow = plate.rectTransform;
            arrow.anchorMin = arrow.anchorMax = Vector2.zero;
            arrow.pivot = new Vector2(0.5f, 0f);
            arrow.sizeDelta = new Vector2(ArrowW, ArrowH);
            var glyph = UIBuild.Text("Glyph", arrow, "▼", 24f, UIStyle.Highlight, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(glyph.rectTransform);

            Hide();
        }

        /// <summary>Arrow plate size; its bottom sits ArrowGap..ArrowGap+4 px above the target (clear of the 5 px outline).</summary>
        public const float ArrowW = 40f, ArrowH = 30f, ArrowGap = 8f;

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

        bool TutorialDay => ctx.HasGame && ctx.Day.IsTutorial;
        bool HintsOn => ctx.Settings == null || ctx.Settings.ShowTutorialHints;
        bool Live => TutorialDay && ctx.Phase == DayPhase.Running;

        // ------------------------------------------------------------------ events (UIRoot forwards them)

        public void Reset()
        {
            wave = spawned = resolved = 0;
            chipsDone = closedThisWave = departedWithRiders = false;
            chipsShown = sinceBoarded = whyTimer = 0f;
            whyAge = 99f;
            step = Step.None;
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
            if (!Live || e.Verdict == LuckVerdict.Neutral) return;
            // riders judged together (three grandmas, one head count) are one card
            if (whyTimer > 0f && whyAge <= MergeWindow && whySource == e.Source && whyNumber == e.Number && whyVerdict == e.Verdict) return;
            whyTimer = WhyLife;
            whyAge = 0f;
            whySource = e.Source;
            whyNumber = e.Number;
            whyVerdict = e.Verdict;
            bool lucky = e.Verdict == LuckVerdict.Lucky;
            bool floor = e.Source != LuckSource.Occupancy;
            string number = e.Number >= 0 ? e.Number.ToString(CultureInfo.InvariantCulture)
                : ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) ? ctx.Building.GetLabel(e.Floor).Text : "?";
            var template = lucky ? (floor ? UIStrings.TutWhyLuckyFloor : UIStrings.TutWhyLuckyCount)
                : (floor ? UIStrings.TutWhyUnluckyFloor : UIStrings.TutWhyUnluckyCount);
            whyText.text = ctx.F(template, number);
            var color = lucky ? UIStyle.Lucky : UIStyle.Unlucky;
            whyText.color = whyCaption.color = whyEdge.color = color;
            whyCaption.text = ctx.S(floor ? UIStrings.RuleOpen : UIStrings.RuleClose);
            bool sticker = !lucky && floor;
            whyNote.text = sticker ? ctx.S(UIStrings.TutWhySticker) : "";
            whyNote.gameObject.SetActive(sticker);
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt, float time)
        {
            if (whyTimer > 0f) { whyTimer -= dt; whyAge += dt; }
            if (Live) sinceBoarded += dt;
            if (!(Live && HintsOn) || ctx.Paused) { Hide(); return; }

            hasTarget = false;
            targetName = null;
            promptSprite = null;
            Step s = Prompt(out string text, out bool hold);
            if (text != null) { step = s; shownText = text; }
            else if (!hold) { step = Step.None; shownText = null; }
            // hold: car moving / doors animating / guests walking in - keep the last line, without a target
            if (step == Step.Chips && text != null) chipsShown += dt;
            if (chipsShown >= ChipsLife) chipsDone = true;

            bool showBanner = shownText != null;
            Show(banner, showBanner);
            if (showBanner)
            {
                UIBuild.Set(promptText, shownText);
                if (text != null) promptIcon.Set(promptSprite, promptTint);
                UIBuild.Set(stepText, step == Step.Closing ? ctx.S(UIStrings.TutDoneTitle)
                    : string.Format(ctx.S(UIStrings.TutWave), Mathf.Clamp(wave, 1, LessonWaves), LessonWaves));
            }
            else promptIcon.Set(null, Color.white);
            promptIcon.Place();

            bool showWhy = whyTimer > 0f;
            Show(why, showWhy);
            if (showWhy)
            {
                // right under the prompt (or in its place when there is no prompt)
                float y = -ColumnTop - (showBanner ? banner.rect.height + Gap : 0f);
                if (Mathf.Abs(why.anchoredPosition.y - y) > 0.5f) why.anchoredPosition = new Vector2(-Margin, y);
            }

            Show(highlight, hasTarget);
            Show(arrow, hasTarget);
            if (hasTarget)
            {
                highlight.anchoredPosition = targetRect.min;
                highlight.sizeDelta = targetRect.size;
                highlightGroup.alpha = 0.55f + 0.45f * Mathf.Sin(time * 7f);
                float bob = 2f + 2f * Mathf.Sin(time * 7f); // bobs toward the target, never away from it
                arrow.anchoredPosition = new Vector2(targetRect.center.x, targetRect.yMax + ArrowGap + bob);
                arrow.SetAsLastSibling();
            }
        }

        void Target(RectTransform rt)
        {
            if (rt == null) return;
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
                    text = Closing();
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

        string Closing()
        {
            int lastDay = ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount;
            int prosperous = ctx.Data != null ? ctx.Data.Rules.prosperousEndingLuck : 70;
            return ctx.F(UIStrings.TutRules, lastDay, prosperous) + "\n" + ctx.S(UIStrings.PracticeReset);
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
            return ctx.F(UIStrings.TutChips, Join(luck.Lucky, UIFormat.LuckyHex, false), Join(luck.Unlucky, UIFormat.UnluckyHex, true));
        }

        static string Join(NumberSet numbers, string hex, bool strike)
        {
            if (numbers == null || numbers.Count == 0) return "-";
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=").Append(hex).Append('>');
            if (strike) sb.Append("<s>");
            for (int i = 0; i < numbers.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(numbers[i].ToString(CultureInfo.InvariantCulture));
            }
            if (strike) sb.Append("</s>");
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
            var riders = ctx.Elevator.Riders;
            for (int i = 0; i < riders.Count; i++)
            {
                if (!ctx.Guests.TryGet(riders[i], out var info)) continue;
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
            hasTarget = false;
            targetName = null;
        }

        public void RefreshLanguage() { /* prompts are rebuilt every tick from ctx.S; the why card keeps its event text */ }

        // ------------------------------------------------------------------ test hooks

        public Step CurrentStep => banner.gameObject.activeSelf ? step : Step.None;
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
    }
}
