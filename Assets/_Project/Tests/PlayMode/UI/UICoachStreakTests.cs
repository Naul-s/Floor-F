using System.Collections;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Day 0 coach marks (hold, pause, keyboard), tutorial cards fit the screen, streak readout, day grade, title panel.</summary>
    public class UICoachStreakTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        static IEnumerator BootDay0()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(UITestDriver.UI(), 1);
            yield return UITestDriver.Frames(2);
        }

        [UnityTest]
        public IEnumerator Coach_MenuPause_HidesOverlay_ResumeBringsItBack()
        {
            yield return BootDay0();
            var ui = UITestDriver.UI();
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep);
            Assert.IsTrue(ui.CoachVisible && Day0Driver.Held, "board: held + dimmed");

            ui.ClickPauseButton();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.PauseVisible, "the pause menu works on top of a coach mark");
            Assert.IsFalse(ui.CoachVisible, "paused: overlay hidden");
            Assert.IsNull(ui.TutorialPrompt, "paused: no prompt");
            Assert.IsFalse(Day0Driver.Held, "the menu pause took over the hold");

            ui.ClickResume();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.PauseVisible);
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep, "the same step again");
            Assert.IsTrue(ui.CoachVisible && Day0Driver.Held, "resumed: held + dimmed again (nothing was done)");

            // hints off from the pause menu: no overlay, no hold after RESUME
            ui.ClickPauseButton();
            yield return null;
            ui.ClickHintsToggle();
            ui.ClickResume();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.CoachVisible);
            Assert.IsFalse(Day0Driver.Held, "hints off: never held");
            Assert.IsNull(ui.TutorialPrompt);
        }

        [UnityTest]
        public IEnumerator Coach_KeyboardCommand_ReleasesLikeAClick()
        {
            yield return BootDay0();
            var ui = UITestDriver.UI();
            var e = Day0Driver.Elevator;
            Assert.IsTrue(ui.CoachVisible && Day0Driver.Held);
            // what KeyboardInput's Space does: the elevator command through IElevatorControl
            Assert.IsTrue(GameServices.Get<IElevatorControl>().OpenDoors(), "commands are accepted while held");
            Assert.IsFalse(Day0Driver.Held, "the command released the hold");
            yield return null;
            Assert.IsFalse(ui.CoachVisible, "overlay follows the hold");
            yield return UITestDriver.Until(() => e.Occupancy >= 1);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Chips, ui.TutorialStep);
            Assert.IsTrue(ui.CoachAwaitingContinue);
            // a door command during an explain step continues it too (and closes the doors = the next step anyway)
            Assert.IsTrue(GameServices.Get<IElevatorControl>().CloseDoors());
            yield return UITestDriver.Frames(2);
            Assert.AreNotEqual(TutorialView.Step.Chips, ui.TutorialStep, "chip lesson over");
            yield return UITestDriver.Until(() => e.Doors == DoorState.Closed);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);
            Assert.IsTrue(ui.CoachVisible && Day0Driver.Held, "next step, next coach mark");
        }

        [UnityTest]
        public IEnumerator TutorialCards_FitA720Screen_EnglishAndKorean()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            var size = ui.CanvasSize;
            int checkedCards = 0;
            foreach (bool ko in new[] { false, true })
            {
                settings.UseKorean = ko;
                yield return null;
                foreach (var s in ui.TutorialSampleCards())
                {
                    var r = ui.MeasureTutorialCard(s);
                    string what = (ko ? "ko " : "en ") + s + " -> " + r;
                    Assert.Greater(r.height, 40f, what);
                    Assert.LessOrEqual(r.height, TutorialView.MaxCardHeight + 0.5f, what + ": taller than the column");
                    Assert.GreaterOrEqual(r.xMin, 0f, what);
                    Assert.LessOrEqual(r.xMax, size.x + 0.5f, what);
                    Assert.LessOrEqual(r.yMax, size.y + 0.5f, what);
                    // the canvas is 720 high in a 16:9 window (the batch capture letterboxes it taller): measure from the top
                    Assert.LessOrEqual(size.y - r.yMin, UIStyle.RefHeight - TutorialView.BottomClear + 0.5f, what + ": runs off a 720-high screen");
                    checkedCards++;
                }
            }
            Assert.GreaterOrEqual(checkedCards, 2 * 20, "every lesson line, why card, streak card and closing page");
        }

        [UnityTest]
        public IEnumerator StreakReadout_HiddenAtZero_PopsOnStep_BrokenFlash() => Streak(false);

        [UnityTest]
        public IEnumerator StreakReadout_Korean() => Streak(true);

        static IEnumerator Streak(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.StreakVisible, "hidden at 0");
            var bus = GameServices.Events;
            bus.Publish(new LuckStreakChanged(1, 0, 1.05f));
            yield return null;
            Assert.IsTrue(ui.StreakVisible);
            Assert.AreEqual(ko ? "연속 행운 x1 · 팁 x1.05" : "Lucky streak x1 · tips x1.05", Plain(ui.StreakText));
            bus.Publish(new LuckStreakChanged(3, 1, 1.15f));
            Time.captureDeltaTime = 0.05f;
            yield return UITestDriver.Frames(3);
            Time.captureDeltaTime = 0f;
            Assert.Greater(ui.StreakScale, 1.05f, "pops on a step up");
            Assert.AreEqual(ko ? "연속 행운 x3 · 팁 x1.15" : "Lucky streak x3 · tips x1.15", Plain(ui.StreakText));
            Assert.LessOrEqual(ui.StreakBoxRect.xMax, ui.CanvasSize.x, "the pop stays on screen");
            yield return UITestDriver.Until(() => false, 6);
            Assert.AreEqual(1f, ui.StreakScale, 0.01f, "settles");
            var box = ui.StreakBoxRect;
            var luck = ui.LuckBoxRect;
            Assert.Less(box.yMax, luck.yMin, "under the Luck meter");
            Assert.AreEqual(luck.xMax, box.xMax, 1f, "same right column");
            yield return Day0Driver.Shot("ui_streak_readout" + (ko ? "_ko" : ""), true);

            bus.Publish(new LuckStreakChanged(0, 3, 1f));
            yield return null;
            Assert.IsTrue(ui.StreakVisible, "the break is shown");
            Assert.AreEqual(ko ? "연속 끊김!" : "Streak broken!", Plain(ui.StreakText));
            yield return Day0Driver.Shot("ui_streak_broken" + (ko ? "_ko" : ""), true);
            yield return UITestDriver.Until(() => !ui.StreakVisible, 40);
            Assert.IsFalse(ui.StreakVisible, "then hidden again");
        }

        [UnityTest]
        public IEnumerator Summary_GradeStamp_RealDaysOnly() => Grade(false);

        [UnityTest]
        public IEnumerator Summary_GradeStamp_Korean() => Grade(true);

        static IEnumerator Grade(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Until(() => false, 30);
            UITestDriver.Session().EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen);
            Assert.IsTrue(ui.GradeStampVisible, "grade on a real day");
            Assert.AreEqual(UIFormat.DayGrade(day.LastSummary), ui.GradeStampText);
            StringAssert.Contains(ko ? "최고 연속 행운" : "Best lucky streak", ui.FlowExtra);
            StringAssert.Contains("x" + day.LastSummary.BestStreak, Plain(ui.FlowExtra));
            Assert.IsFalse(ui.GradeStampLanded, "it slams in after a moment");
            yield return UITestDriver.Until(() => false, 12); // 1.2 simulated seconds: delay + slam + bounce
            Assert.IsTrue(ui.GradeStampLanded, "landed");
            var stamp = ui.GradeStampRect;
            var card = ui.FlowCardRect;
            Assert.IsTrue(card.Overlaps(stamp), "stamped on the card");
            Assert.GreaterOrEqual(stamp.center.x, card.center.x + card.width * 0.3f, "in its right part, clear of the centred title");
            var size = ui.CanvasSize;
            Assert.IsTrue(stamp.xMin >= 0f && stamp.xMax <= size.x && stamp.yMin >= 0f && stamp.yMax <= size.y, "on screen: " + stamp);
            yield return Day0Driver.Shot("ui_summary_grade" + (ko ? "_ko" : ""), true);
        }

        [UnityTest]
        public IEnumerator Title_PanelIsCompact_HotelVisibleAbove()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            yield return UITestDriver.Frames(3);
            var ui = UITestDriver.UI();
            var r = ui.TitlePanelRect;
            Assert.Greater(r.height, 100f, "title panel laid out");
            Assert.LessOrEqual(r.yMax, UIStyle.RefHeight * 0.55f, "the panel stays in the lower half: " + r);
            Assert.GreaterOrEqual(r.yMin, 28f, "above the credit strip");
            yield return Day0Driver.Shot("ui_title_compact", true);
        }
    }
}
