using System.Collections;
using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Drives the Day 0 training shift through the UI's own buttons.</summary>
    static class Day0Driver
    {
        public static IElevatorQuery Elevator => GameServices.Get<IElevatorQuery>();
        public static IGuestQuery Guests => GameServices.Get<IGuestQuery>();
        static readonly List<GuestId> waiting = new List<GuestId>();

        public static bool AnyWaiting()
        {
            int n = GameServices.Get<IBuildingQuery>().FloorCount;
            for (int f = 0; f < n; f++) if (Guests.GetWaiting(new FloorIndex(f), waiting) > 0) return true;
            return false;
        }

        public static bool AnyBoarding()
        {
            var active = Guests.ActiveGuests;
            for (int i = 0; i < active.Count; i++)
                if (Guests.TryGet(active[i], out var g) && g.State == GuestState.Boarding) return true;
            return false;
        }

        public static bool IdleAt(FloorIndex f) => Elevator.Motion == ElevatorMotion.Idle && !Elevator.HasTarget && Elevator.CurrentFloor == f;

        /// <summary>Waits until wave <paramref name="n"/> has guests waiting.</summary>
        public static IEnumerator WaitForWave(UIRoot ui, int n)
        {
            yield return UITestDriver.Until(() => ui.TutorialWave >= n && AnyWaiting(), 200);
            Assert.AreEqual(n, ui.TutorialWave, "wave " + n + " arrived");
            Assert.IsTrue(AnyWaiting(), "wave " + n + " waits");
            yield return UITestDriver.Frames(2);
        }

        public static IEnumerator GoTo(UIRoot ui, FloorIndex f)
        {
            if (IdleAt(f)) yield break;
            ui.ClickFloor(f.Value);
            yield return UITestDriver.Until(() => IdleAt(f));
            Assert.IsTrue(IdleAt(f), "arrived at " + f);
            yield return UITestDriver.Frames(2);
        }

        public static IEnumerator Open(UIRoot ui)
        {
            int opened = 0;
            using (GameServices.Events.Subscribe<DoorsOpened>(e => opened++))
            {
                ui.ClickOpen();
                yield return UITestDriver.Until(() => opened > 0);
            }
            Assert.Greater(opened, 0, "doors opened");
            yield return UITestDriver.Frames(2);
        }

        public static IEnumerator Close(UIRoot ui)
        {
            ui.ClickClose();
            yield return UITestDriver.Until(() => Elevator.Doors == DoorState.Closed);
            Assert.AreEqual(DoorState.Closed, Elevator.Doors);
            yield return UITestDriver.Frames(2);
        }

        /// <summary>The ▼ sits right above <paramref name="target"/>, inside its column: it cannot be read as the neighbour.</summary>
        public static void AssertArrowOn(UIRoot ui, Rect target, string what)
        {
            var a = ui.TutorialArrowRect;
            Assert.Greater(a.width, 0f, what + ": arrow visible");
            Assert.GreaterOrEqual(a.xMin, target.xMin - 0.5f, what + ": arrow " + a + " inside the target column " + target);
            Assert.LessOrEqual(a.xMax, target.xMax + 0.5f, what + ": arrow " + a + " inside the target column " + target);
            Assert.GreaterOrEqual(a.yMin, target.yMax - 0.5f, what + ": arrow above the target, not on it");
            Assert.LessOrEqual(a.yMin - target.yMax, 16f, what + ": arrow tight on the target");
            var t = ui.TutorialTargetRect;
            Assert.AreEqual(target.center.x, t.center.x, 1f, what + ": outline on the target");
            Assert.AreEqual(target.center.y, t.center.y, 1f, what + ": outline on the target");
        }

        /// <summary>Prompt / why cards: off the panel and right of the building (its top floors are where they used to sit).</summary>
        public static void AssertCardsClear(UIRoot ui, string what)
        {
            var layout = GameServices.Get<IWorldLayout>();
            float top = (layout.FloorCount - 0.5f) * layout.FloorHeight;
            Assert.IsTrue(ui.WorldToCanvas(new Vector3(layout.ShaftX + layout.FloorWidth * 0.5f, top, 0f), out var right), "project the building");
            var panel = ui.PanelRect;
            var size = ui.CanvasSize;
            foreach (var r in new[] { ui.TutorialPromptRect, ui.TutorialWhyRect })
            {
                if (r.width <= 0f) continue;
                Assert.IsFalse(r.Overlaps(panel), what + ": card " + r + " over the panel " + panel);
                Assert.GreaterOrEqual(r.xMin, right.x, what + ": card " + r + " over the building (right edge " + right.x + ")");
                Assert.GreaterOrEqual(r.yMin, 0f, what + ": card " + r + " runs off the bottom");
                Assert.LessOrEqual(r.xMax, size.x + 0.5f, what + ": card " + r + " runs off the right");
                Assert.LessOrEqual(size.y - r.yMin, UIStyle.RefHeight - TutorialView.BottomClear + 0.5f, what + ": card " + r + " would run off a 720-high screen");
            }
            var p = ui.TutorialPromptRect; var w = ui.TutorialWhyRect;
            if (p.width > 0f && w.width > 0f) Assert.IsFalse(p.Overlaps(w), what + ": prompt and why cards overlap");
        }

        public static IEnumerator Shot(string name, bool capture)
        {
            if (!capture) yield break;
            yield return null;
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture(name)));
        }

        public static bool Held => GameServices.Get<IGameFlow>().TutorialHold;

        /// <summary>Coach mark on an action step: held, dimmed, the cut-out on the target, only the target clickable.</summary>
        public static void AssertActionCoach(UIRoot ui, Rect target, string targetName, string what)
        {
            Assert.IsTrue(Held, what + ": the game is held");
            Assert.IsTrue(ui.CoachVisible, what + ": overlay up");
            Assert.IsFalse(ui.CoachAwaitingContinue, what + ": an action step, not click-to-continue");
            Assert.IsNull(ui.CoachContinueHint, what + ": no continue hint");
            var hole = ui.CoachHoleRect;
            Assert.IsTrue(hole.Contains(target.center), what + ": cut-out " + hole + " on the target " + target);
            Assert.LessOrEqual(hole.width, target.width + 2f * TutorialView.HolePad + 1f, what + ": cut-out no wider than the target");
            Assert.AreEqual(targetName, ui.RaycastAt(target.center), what + ": a click on the target reaches it");
            var size = ui.CanvasSize;
            foreach (var p in new[] { new Vector2(size.x * 0.5f, size.y * 0.5f), new Vector2(target.center.x, target.yMax + 40f), new Vector2(size.x - 60f, 120f) })
            {
                string hit = ui.RaycastAt(p);
                Assert.IsTrue(hit != null && hit.StartsWith("Dim"), what + ": a click at " + p + " is absorbed by the overlay, hit " + hit);
            }
        }

        /// <summary>Coach mark on an explain step: held, dimmed, "click to continue", clicks anywhere land on the overlay.</summary>
        public static void AssertExplainCoach(UIRoot ui, string what)
        {
            Assert.IsTrue(Held, what + ": the game is held");
            Assert.IsTrue(ui.CoachVisible, what + ": overlay up");
            Assert.IsTrue(ui.CoachAwaitingContinue, what + ": waits for a click");
            StringAssert.Contains(ui.Korean ? "클릭해서 계속" : "Click to continue", ui.CoachContinueHint);
            var t = ui.TutorialTargetRect;
            if (t.width > 0f)
            {
                Assert.IsTrue(ui.CoachHoleRect.Contains(t.center), what + ": cut-out on the explained element");
                Assert.AreEqual("Hole", ui.RaycastAt(t.center), what + ": even a click on it just continues");
            }
            string hit = ui.RaycastAt(ui.CanvasSize * 0.5f);
            Assert.IsTrue(hit != null && hit.StartsWith("Dim"), what + ": click anywhere hits the overlay, got " + hit);
        }

        /// <summary>One click on the overlay: the explain step goes away.</summary>
        public static IEnumerator Continue(UIRoot ui, string what)
        {
            Assert.IsTrue(ui.CoachAwaitingContinue, what + ": waiting for a click");
            var before = ui.TutorialStep;
            string text = ui.TutorialWhy ?? ui.TutorialPrompt;
            ui.ClickCoachOverlay();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.TutorialStep == before && (ui.TutorialWhy ?? ui.TutorialPrompt) == text, what + ": the click moved on");
        }

        /// <summary>Clicks through every waiting explain card (why / streak), collecting their texts.</summary>
        public static IEnumerator ContinueAll(UIRoot ui, List<string> seen)
        {
            for (int i = 0; i < 6 && ui.CoachAwaitingContinue && (ui.TutorialStep == TutorialView.Step.Why || ui.TutorialStep == TutorialView.Step.Streak); i++)
            {
                seen?.Add(ui.TutorialStep + ": " + ui.TutorialWhy);
                yield return Continue(ui, "explain " + i);
            }
        }

        /// <summary>Explain steps never time out: the card and the frozen game stay put.</summary>
        public static IEnumerator AssertStaysFrozen(UIRoot ui, string what)
        {
            var step = ui.TutorialStep;
            var floor = Elevator.CurrentFloor;
            var doors = Elevator.Doors;
            int occupancy = Elevator.Occupancy;
            yield return UITestDriver.Until(() => false, 120); // 12 simulated seconds
            Assert.AreEqual(step, ui.TutorialStep, what + ": no timer moved it on");
            Assert.IsTrue(Held, what + ": still held");
            Assert.AreEqual(floor, Elevator.CurrentFloor, what + ": car frozen");
            Assert.AreEqual(doors, Elevator.Doors, what + ": doors frozen");
            Assert.AreEqual(occupancy, Elevator.Occupancy, what + ": guests frozen");
        }
    }

    /// <summary>Day 0 training shift: lesson prompts per wave, arrow, card placement, HUD, intro/summary (+ L2 captures).</summary>
    public class UIDay0CaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_Day0Lesson() => Lesson("", false);

        [UnityTest]
        public IEnumerator Capture_Day0Lesson_Korean() => Lesson("_ko", true);

        static IEnumerator Lesson(string suffix, bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var score = GameServices.Get<IScoreQuery>();
            var data = GameServices.Get<GameData>();
            var e = Day0Driver.Elevator;
            GameServices.Get<IGameSettings>().UseKorean = ko;
            bool en = !ko;
            yield return UITestDriver.Frames(3);

            // ---- intro: Day 0, practice clock, skip button, no how-to list
            Assert.IsTrue(day.IsTutorial, "boots into the training shift");
            Assert.AreEqual(0, day.DisplayDay);
            Assert.AreEqual(ko ? "0일차" : "DAY 0", ui.HudDayText);
            Assert.AreEqual(ko ? "연습" : "PRACTICE", ui.HudClockText);
            Assert.IsTrue(ui.SkipTutorialVisible, "skip button on the Day 0 intro");
            Assert.GreaterOrEqual(ui.SkipTutorialFontSize, 24f, "skip label readable");
            Assert.GreaterOrEqual(ui.HudClockFontSize, 24f, "practice label readable");
            Assert.IsNull(ui.FlowExtra, "no how-to bullet list on Day 0");
            StringAssert.Contains(ko ? "0일차" : "Day 0", ui.FlowTitle);
            yield return Day0Driver.Shot("ui_day0_intro" + suffix, true);
            yield return UITestDriver.ClickStart();

            // ---- wave 1: one grandma -> OPEN, chip, CLOSE, floor 3, OPEN
            yield return Day0Driver.WaitForWave(ui, 1);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep);
            Assert.AreEqual("Open", ui.TutorialTarget);
            if (en) { StringAssert.Contains("OPEN", ui.TutorialPrompt); StringAssert.Contains("floor 1", ui.TutorialPrompt); }
            Day0Driver.AssertArrowOn(ui, ui.OpenButtonRect, "board: OPEN");
            Day0Driver.AssertCardsClear(ui, "board");
            Day0Driver.AssertActionCoach(ui, ui.OpenButtonRect, "Open", "board");
            Assert.AreEqual(ko ? "연습" : "PRACTICE", ui.HudClockText, "no countdown while running");
            yield return Day0Driver.Shot("ui_day0_board" + suffix, true);
            ui.ClickCoachOverlay(); // a click beside the target does nothing
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep, "absorbed click: same step");
            Assert.IsTrue(Day0Driver.Held, "absorbed click: still held");

            ui.ClickOpen();
            yield return null;
            Assert.IsFalse(ui.CoachVisible, "OPEN released the hold: overlay gone");
            yield return UITestDriver.Until(() => e.Occupancy >= 1);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Chips, ui.TutorialStep, "explain the rider chip");
            Assert.AreEqual("Chips", ui.TutorialTarget);
            StringAssert.Contains(">3<", ui.TutorialPrompt, "her lucky number, in gold");
            StringAssert.Contains("4</s>", ui.TutorialPrompt, "her unlucky number, red + struck");
            if (en) { StringAssert.Contains("LUCKY", ui.TutorialPrompt); StringAssert.Contains("UNLUCKY", ui.TutorialPrompt); }
            Day0Driver.AssertArrowOn(ui, ui.TutorialTargetRect, "chips");
            Day0Driver.AssertExplainCoach(ui, "chips");
            Day0Driver.AssertCardsClear(ui, "chips");
            yield return Day0Driver.Shot("ui_day0_chips" + suffix, true);
            yield return Day0Driver.AssertStaysFrozen(ui, "chips");
            yield return Day0Driver.Continue(ui, "chips");

            Assert.AreEqual(TutorialView.Step.Close, ui.TutorialStep, "chip lesson -> CLOSE after the click");
            Assert.AreEqual("Close", ui.TutorialTarget);
            if (en) { StringAssert.Contains("HEAD COUNT", ui.TutorialPrompt); StringAssert.Contains("1 inside", ui.TutorialPrompt); }
            Day0Driver.AssertArrowOn(ui, ui.CloseButtonRect, "CLOSE");
            Day0Driver.AssertActionCoach(ui, ui.CloseButtonRect, "Close", "close");
            yield return Day0Driver.Shot("ui_day0_close" + suffix, true);
            yield return Day0Driver.Close(ui);

            var gym = FloorIndex.FromPhysicalNumber(3);
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);
            Assert.AreEqual("Floor 3", ui.TutorialTarget);
            Assert.IsTrue(ui.TutorialIconVisible, "destination icon inline");
            if (en) StringAssert.Contains("floor 3", ui.TutorialPrompt);
            Day0Driver.AssertArrowOn(ui, ui.FloorButtonRect(gym.Value), "floor 3 (right column)");
            Day0Driver.AssertActionCoach(ui, ui.FloorButtonRect(gym.Value), "Floor 3", "floor 3");
            yield return Day0Driver.Shot("ui_day0_floor" + suffix, true);
            yield return Day0Driver.GoTo(ui, gym);

            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
            Assert.AreEqual("Open", ui.TutorialTarget);
            if (en) StringAssert.Contains("FLOOR", ui.TutorialPrompt);
            yield return Day0Driver.Open(ui);
            Assert.AreEqual(TutorialView.Step.Why, ui.TutorialStep, "lucky floor explained");
            StringAssert.Contains("3", ui.TutorialWhy);
            if (en) StringAssert.Contains("LUCKY", ui.TutorialWhy);
            Assert.AreEqual("LuckBox", ui.TutorialTarget, "points at the Luck meter");
            Assert.IsNull(ui.TutorialPrompt, "one card at a time");
            Day0Driver.AssertExplainCoach(ui, "why lucky");
            Day0Driver.AssertCardsClear(ui, "why (wave 1)");
            yield return Day0Driver.Shot("ui_day0_why_lucky" + suffix, true);
            yield return Day0Driver.Continue(ui, "why lucky");
            Assert.AreEqual(1, score.LuckStreak, "one lucky moment so far");
            Assert.IsTrue(ui.StreakVisible, "streak readout from 1");
            yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.NextWave, 60);
            Assert.AreEqual(TutorialView.Step.NextWave, ui.TutorialStep, "between waves");
            Assert.IsNull(ui.TutorialTarget);
            Assert.IsFalse(ui.CoachVisible, "no freeze between waves: the next one has to come");

            // ---- wave 2: three grandmas -> go down, OPEN, wait for all, CLOSE (3 = lucky), streak, sauna
            yield return Day0Driver.WaitForWave(ui, 2);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.GoTo, ui.TutorialStep, "car is at 3, guests at 1");
            Assert.AreEqual("Floor 1", ui.TutorialTarget);
            Day0Driver.AssertArrowOn(ui, ui.FloorButtonRect(0), "floor 1");
            Day0Driver.AssertActionCoach(ui, ui.FloorButtonRect(0), "Floor 1", "go to floor 1");
            yield return Day0Driver.GoTo(ui, FloorIndex.Ground);
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep);
            if (en) StringAssert.Contains("3 guests", ui.TutorialPrompt);
            ui.ClickOpen();
            yield return UITestDriver.Until(() => e.Occupancy >= 1);
            yield return null;
            Assert.AreEqual(TutorialView.Step.Wait, ui.TutorialStep, "some still walking in: wait");
            Assert.IsNull(ui.TutorialTarget, "no CLOSE arrow while guests get in");
            Assert.IsFalse(ui.CoachVisible, "waiting is not frozen");
            if (en) { StringAssert.Contains("Wait until everyone", ui.TutorialPrompt); StringAssert.Contains("lucky", ui.TutorialPrompt); }
            yield return Day0Driver.Shot("ui_day0_wait" + suffix, true);
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 300 && (e.Occupancy < 3 || Day0Driver.AnyBoarding()); i++)
            {
                yield return null;
                if (e.Occupancy < 3) Assert.AreNotEqual("Close", ui.TutorialTarget, "frame " + i + ": CLOSE before everyone is in");
            }
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(3, e.Occupancy);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Close, ui.TutorialStep);
            if (en) StringAssert.Contains("LUCKY head count", ui.TutorialPrompt);
            yield return Day0Driver.Close(ui);
            Assert.AreEqual(TutorialView.Step.Why, ui.TutorialStep, "3 inside explained");
            if (en) StringAssert.Contains("3 inside", ui.TutorialWhy);
            yield return Day0Driver.Continue(ui, "why 3 inside");

            // three lucky head counts at once: the streak card (once per lesson) points at the readout
            Assert.AreEqual(TutorialView.Step.Streak, ui.TutorialStep, "streak explained after the why card");
            Assert.GreaterOrEqual(score.LuckStreak, 2);
            Assert.AreEqual("StreakBox", ui.TutorialTarget);
            Assert.IsTrue(ui.StreakVisible);
            StringAssert.Contains("x" + score.LuckStreak, ui.TutorialWhy);
            StringAssert.Contains(UIFormat.Multiplier(score.StreakTipMultiplier), ui.TutorialWhy);
            StringAssert.Contains(UIFormat.Multiplier(score.StreakTipMultiplier), ui.StreakText);
            StringAssert.Contains(ko ? "연속 행운" : "Lucky streak", ui.StreakText);
            Day0Driver.AssertExplainCoach(ui, "streak");
            Day0Driver.AssertCardsClear(ui, "streak");
            yield return Day0Driver.Shot("ui_day0_streak" + suffix, true);
            yield return Day0Driver.Continue(ui, "streak");

            var sauna = FloorIndex.FromPhysicalNumber(5);
            Assert.AreEqual("Floor 5", ui.TutorialTarget);
            yield return Day0Driver.GoTo(ui, sauna);
            yield return Day0Driver.Open(ui);
            var seen = new List<string>();
            yield return Day0Driver.ContinueAll(ui, seen);
            yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.NextWave || ui.CoachAwaitingContinue, 80);
            yield return Day0Driver.ContinueAll(ui, seen);
            yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.NextWave, 80);

            // ---- wave 3: two riders, two stops; floor 4 is unlucky (stickers later)
            yield return Day0Driver.WaitForWave(ui, 3);
            yield return Day0Driver.GoTo(ui, FloorIndex.Ground);
            ui.ClickOpen();
            yield return UITestDriver.Until(() => e.Occupancy >= 2 && !Day0Driver.AnyBoarding());
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Close, ui.TutorialStep);
            yield return Day0Driver.Close(ui);
            yield return Day0Driver.ContinueAll(ui, seen);
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);
            if (en) StringAssert.Contains("Two riders", ui.TutorialPrompt);
            yield return Day0Driver.Shot("ui_day0_two_stops" + suffix, true);

            var karaoke = FloorIndex.FromPhysicalNumber(4);
            bool sawUnlucky = false, sawEveryone = false, sawLeftColumn = false;
            for (int stop = 0; stop < 4 && e.Occupancy > 0; stop++)
            {
                Assert.IsTrue(ui.TutorialTarget != null && ui.TutorialTarget.StartsWith("Floor "), "stop " + stop + ": points at a floor, got " + ui.TutorialTarget);
                var dest = new FloorIndex(int.Parse(ui.TutorialTarget.Substring(6)) - 1);
                if (dest == karaoke)
                {
                    Day0Driver.AssertArrowOn(ui, ui.FloorButtonRect(karaoke.Value), "floor 4 (left column)");
                    sawLeftColumn = true;
                    yield return Day0Driver.Shot("ui_day0_floor4" + suffix, true);
                }
                yield return Day0Driver.GoTo(ui, dest);
                Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
                if (e.Occupancy > 1)
                {
                    sawEveryone = true;
                    if (en) StringAssert.Contains("EVERYONE", ui.TutorialPrompt);
                }
                yield return Day0Driver.Open(ui);
                if (dest == karaoke)
                {
                    Assert.AreEqual(TutorialView.Step.Why, ui.TutorialStep, "unlucky 4 explained");
                    StringAssert.Contains("4", ui.TutorialWhy);
                    StringAssert.Contains("F", ui.TutorialWhy, "stickers can relabel it (4 → F)");
                    if (en) { StringAssert.Contains("UNLUCKY", ui.TutorialWhy); StringAssert.Contains("sticker", ui.TutorialWhy); }
                    Day0Driver.AssertCardsClear(ui, "why (unlucky)");
                    Day0Driver.AssertExplainCoach(ui, "why unlucky");
                    sawUnlucky = true;
                    yield return Day0Driver.Shot("ui_day0_why_unlucky" + suffix, true);
                }
                yield return Day0Driver.ContinueAll(ui, seen);
                yield return UITestDriver.Until(() => e.Occupancy == 0 || ui.TutorialStep == TutorialView.Step.Close || ui.CoachAwaitingContinue, 40);
                yield return Day0Driver.ContinueAll(ui, seen);
                if (e.Occupancy > 0)
                {
                    yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.Close, 40);
                    if (en) StringAssert.Contains("take the others on", ui.TutorialPrompt);
                    yield return Day0Driver.Close(ui);
                    yield return Day0Driver.ContinueAll(ui, seen);
                }
            }
            Assert.IsTrue(sawEveryone, "the two-rider stop says everyone judges");
            Assert.IsTrue(sawLeftColumn, "pointed at floor 4");
            Assert.IsTrue(sawUnlucky, "floor 4 was judged unlucky");

            // ---- two closing pages (click each), then the training summary
            yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.Closing || ui.CoachAwaitingContinue, 40);
            yield return Day0Driver.ContinueAll(ui, seen);
            yield return UITestDriver.Until(() => ui.TutorialStep == TutorialView.Step.Closing, 20);
            Assert.AreEqual(TutorialView.Step.Closing, ui.TutorialStep, "closing page after the last wave");
            StringAssert.Contains(data.Rules.prosperousEndingLuck.ToString(), ui.TutorialPrompt);
            if (en) StringAssert.Contains("Haunted", ui.TutorialPrompt);
            Assert.AreEqual("LuckBox", ui.TutorialTarget, "page 1 points at the Luck meter");
            Day0Driver.AssertExplainCoach(ui, "closing 1");
            Day0Driver.AssertCardsClear(ui, "closing 1");
            yield return Day0Driver.Shot("ui_day0_closing" + suffix, true);
            yield return Day0Driver.AssertStaysFrozen(ui, "closing 1");
            Assert.AreEqual(DayPhase.Running, day.Phase, "the shift waits for the player to read");
            yield return Day0Driver.Continue(ui, "closing 1");
            Assert.AreEqual(TutorialView.Step.Closing, ui.TutorialStep, "page 2");
            if (en) { StringAssert.Contains("reset", ui.TutorialPrompt); StringAssert.Contains("leave", ui.TutorialPrompt); }
            Assert.AreEqual("DayBox", ui.TutorialTarget, "page 2 points at the clock");
            Day0Driver.AssertExplainCoach(ui, "closing 2");
            Day0Driver.AssertCardsClear(ui, "closing 2");
            yield return Day0Driver.Shot("ui_day0_closing2" + suffix, true);
            yield return Day0Driver.Continue(ui, "closing 2");
            Assert.IsFalse(Day0Driver.Held, "read: released");

            yield return UITestDriver.Until(() => day.Phase == DayPhase.Summary, 100);
            Assert.AreEqual(DayPhase.Summary, day.Phase, "the training shift ends once the last page is read");
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(ko ? "수습 근무 끝" : "TRAINING SHIFT DONE", ui.FlowTitle);
            if (en) { StringAssert.Contains("reset", ui.FlowBody); StringAssert.Contains("Haunted", ui.FlowExtra); StringAssert.Contains("Best lucky streak", ui.FlowExtra); }
            Assert.IsFalse(ui.GradeStampVisible, "no grade on the training shift");
            Assert.IsFalse(ui.CoachVisible);
            yield return Day0Driver.Shot("ui_day0_summary" + suffix, true);

            // ---- Day 1: real clock, /5, fresh Luck/tips, how-to recap, no lesson prompts
            ui.ClickFlowButton();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Intro, day.Phase, "no shop after the training shift");
            Assert.IsFalse(day.IsTutorial);
            Assert.AreEqual(1, day.DisplayDay);
            Assert.AreEqual(ko ? "1/" + day.PlayableDayCount + "일차" : "DAY 1/" + day.PlayableDayCount, ui.HudDayText);
            StringAssert.Contains(":", ui.HudClockText, "a countdown again");
            Assert.IsFalse(ui.SkipTutorialVisible);
            Assert.AreEqual(data.Rules.startLuck, score.Luck, "practice Luck reset");
            Assert.AreEqual(data.Rules.startTips, score.Tips, "practice tips reset");
            Assert.IsFalse(ui.HudDeltaVisible, "the reset is not shown as a -Luck / -$ moment");
            Assert.IsNotNull(ui.FlowExtra, "how-to recap on Day 1 (hints on)");
            yield return Day0Driver.Shot("ui_day1_intro" + suffix, true);
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Until(() => Day0Driver.AnyWaiting(), 60);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.IsNull(ui.TutorialPrompt, "real days have no lesson prompts");
            }
            GameServices.Get<IGameSettings>().UseKorean = false;
        }
    }

    public class UIDay0Tests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SkipTutorial_ButtonGoesToDay1()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var heard = new List<UiSoundKind>();
            Assert.IsTrue(day.IsTutorial);
            Assert.IsTrue(ui.SkipTutorialVisible);
            StringAssert.Contains("SKIP TUTORIAL", ui.SkipTutorialText);
            using (GameServices.Events.Subscribe<UiSound>(x => heard.Add(x.Kind)))
                ui.ClickSkipTutorial();
            Assert.AreEqual(1, heard.Count, "one UiSound");
            Assert.AreEqual(UiSoundKind.Confirm, heard[0]);
            Assert.AreEqual(DayPhase.Intro, day.Phase);
            Assert.IsFalse(day.IsTutorial, "skipped to the first real day");
            Assert.AreEqual(1, day.DisplayDay);
            yield return null;
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            StringAssert.Contains("Day 1", ui.FlowTitle, "the intro card re-rendered for Day 1");
            Assert.IsFalse(ui.SkipTutorialVisible, "no skip on a real day");
            StringAssert.Contains("HOW TO PLAY", ui.FlowExtra, "Day 1 recap with hints on");
            Assert.AreEqual("DAY 1/" + day.PlayableDayCount, ui.HudDayText);

            // hints off: Day 1 has no recap list
            yield return UITestDriver.LoadMain(skipTutorial: false);
            ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false;
            ui.ClickSkipTutorial();
            yield return null;
            Assert.IsNull(ui.FlowExtra, "hints off: no how-to recap");
        }

        [UnityTest]
        public IEnumerator DisplayDayLabels_HudSummaryEnding()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var settings = GameServices.Get<IGameSettings>();
            Assert.AreEqual("DAY 0", ui.HudDayText);
            Assert.AreEqual("PRACTICE", ui.HudClockText);
            settings.UseKorean = true;
            yield return null;
            Assert.AreEqual("0일차", ui.HudDayText);
            Assert.AreEqual("연습", ui.HudClockText);
            settings.UseKorean = false;
            yield return null;

            ui.ClickSkipTutorial();
            yield return null;
            Assert.AreEqual("DAY 1/" + day.PlayableDayCount, ui.HudDayText);
            StringAssert.Contains(":", ui.HudClockText);
            yield return UITestDriver.ClickStart();
            UITestDriver.Session().EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen);
            Assert.AreEqual("DAY 1 COMPLETE", ui.FlowTitle, "shown day number, not the internal one");

            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return null;
            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            StringAssert.Contains(day.PlayableDayCount + " / " + day.PlayableDayCount, ui.FlowExtra, "days run: 5 / 5");
        }

        [UnityTest]
        public IEnumerator Day0_HintsOff_NoPrompts()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            settings.ShowTutorialHints = false;
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(ui, 1);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.IsNull(ui.TutorialPrompt, "hints off: no prompt");
                Assert.IsNull(ui.TutorialTarget, "hints off: no arrow");
            }
            settings.ShowTutorialHints = true;
            yield return null;
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep, "hints back on: the current step shows");
            StringAssert.Contains("OPEN", ui.TutorialPrompt);
        }

        [UnityTest]
        public IEnumerator Day0_WrongFloor_SaysNotTheirStop()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var e = Day0Driver.Elevator;
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(ui, 1);
            yield return Day0Driver.Open(ui);
            yield return UITestDriver.Until(() => e.Occupancy >= 1);
            yield return Day0Driver.Close(ui); // acting during the chip lesson skips it
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);

            var two = FloorIndex.FromPhysicalNumber(2); // nobody's stop, neutral for grandma (3 / 4)
            yield return Day0Driver.GoTo(ui, two);
            Assert.AreEqual(TutorialView.Step.NotTheirStop, ui.TutorialStep);
            StringAssert.Contains("anyone's stop", ui.TutorialPrompt);
            Assert.AreEqual("Floor 3", ui.TutorialTarget, "points at her floor, not at OPEN");
            yield return Day0Driver.Open(ui);
            yield return Day0Driver.Close(ui);
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep, "doors opened: back to plain 'press floor 3'");
            yield return Day0Driver.GoTo(ui, FloorIndex.FromPhysicalNumber(3));
            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
            StringAssert.Contains("Arrived", ui.TutorialPrompt);
        }

        [UnityTest]
        public IEnumerator Day0_FloorPrompt_ShowsDestinationIcon_Korean()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var e = Day0Driver.Elevator;
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(ui, 1);
            yield return Day0Driver.Open(ui);
            yield return UITestDriver.Until(() => e.Occupancy >= 1);
            yield return Day0Driver.Close(ui);
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);
            Assert.IsTrue(ui.TutorialIconVisible, "icon inline before the destination name: " + ui.TutorialPrompt);
            GameServices.Get<IGameSettings>().UseKorean = true;
            yield return UITestDriver.Frames(3);
            StringAssert.Contains("3층(", ui.TutorialPrompt, "\"3층([icon]헬스장)\"");
            Assert.IsTrue(ui.TutorialIconVisible, "icon in Korean too");
            GameServices.Get<IGameSettings>().UseKorean = false;
        }

        [UnityTest]
        public IEnumerator Bubbles_SameFloor_NeverOverlap()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(); // boots on Day 1 (SkipTutorialForTests)
            var ui = UITestDriver.UI();
            var guests = GameServices.Get<IGuestQuery>();
            var bus = GameServices.Events;
            yield return UITestDriver.ClickStart();
            // the guests walk up one by one and each says hello: check every frame while they arrive
            var waiting = new List<GuestId>();
            var said = new HashSet<int>();
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                int n = guests.GetWaiting(FloorIndex.Ground, waiting);
                for (int k = 0; k < n; k++)
                    if (said.Add(waiting[k].Value)) bus.Publish(new GuestSpoke(waiting[k], GuestLineTriggers.Spawn, k % 2 == 0 ? "Whoa, an elevator operator! So retro." : "Three of us! Lucky, lucky."));
                Assert.IsNull(ui.SpeechSpeechOverlap(), "frame " + i + ": bubbles overlap");
            }
            Time.captureDeltaTime = 0f;
            Assert.GreaterOrEqual(said.Count, 2, "several lobby guests spoke");
        }
    }
}
