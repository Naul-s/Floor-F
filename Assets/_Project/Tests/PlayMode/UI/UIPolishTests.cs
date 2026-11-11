using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>The real IDayQuery with another <see cref="IDayQuery.LastSummary"/> (a summary card with a chosen grade).</summary>
    sealed class SummaryOverrideDay : IDayQuery
    {
        readonly IDayQuery inner;
        public DaySummary Forced;
        public SummaryOverrideDay(IDayQuery inner, DaySummary summary) { this.inner = inner; Forced = summary; }
        public DayId CurrentDay => inner.CurrentDay;
        public int DayCount => inner.DayCount;
        public int DisplayDay => inner.DisplayDay;
        public int PlayableDayCount => inner.PlayableDayCount;
        public DayPhase Phase => inner.Phase;
        public bool IsTutorial => inner.IsTutorial;
        public bool IsFriday13 => inner.IsFriday13;
        public bool IsStarNight => inner.IsStarNight;
        public bool IsRainy => inner.IsRainy;
        public float DurationSeconds => inner.DurationSeconds;
        public float ElapsedSeconds => inner.ElapsedSeconds;
        public float RemainingSeconds => inner.RemainingSeconds;
        public EndingKind Ending => inner.Ending;
        public DaySummary LastSummary => Forced;
    }

    /// <summary>
    /// Polish pass: every day's intro card has a coloured "TODAY:" line, the summary card a hint line, the ending card the
    /// run's grade stamps + totals + best record (also on the title), and the riders' chips stay off the lobby and street.
    /// Captures: ui_intro_day0..7, ui_summary_hint, ui_ending_recap, ui_title_best, ui_riders_lobby (+ _ko).
    /// </summary>
    public class UIPolishTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        /// <summary>Summaries that grade B, A, S, A, C, S, A over the seven real days (tips 40..110: $470 in all).</summary>
        static readonly DaySummary[] Week =
        {
            new DaySummary(8, 2, 40, 50, 54, 5, 2, 3),
            new DaySummary(10, 1, 60, 54, 66, 6, 1, 4),
            new DaySummary(12, 0, 90, 66, 80, 9, 0, 6),
            new DaySummary(9, 1, 70, 80, 90, 6, 1, 3),
            new DaySummary(5, 4, 20, 88, 70, 2, 5, 1),
            new DaySummary(14, 0, 110, 70, 84, 10, 0, 7),
            new DaySummary(11, 1, 80, 84, 94, 7, 2, 4),
        };
        static readonly string[] WeekGrades = { "B", "A", "S", "A", "C", "S", "A" };

        [UnityTest, Timeout(400000)]
        public IEnumerator Capture_DayCards_Recap_Best() => Run(false);

        [UnityTest, Timeout(400000)]
        public IEnumerator Capture_DayCards_Recap_Best_Korean() => Run(true);

        static IEnumerator Run(bool ko)
        {
            string suffix = ko ? "_ko" : "";
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            var store = new MemoryEndingStore("2:1,L:40,T:30"); // a modest run before: Luck 40, $30
            EndingCollection.StoreOverride = store;
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.Frames(2);
            var s = UITestDriver.Session();
            var day = GameServices.Get<IDayQuery>();
            var data = GameServices.Get<GameData>();
            for (int i = 0; i < Week.Length; i++) Assert.AreEqual(WeekGrades[i], UIFormat.DayGrade(Week[i]), "week day " + (i + 1));

            var seen = new HashSet<int>();
            for (int guard = 0; guard < 80 && day.Phase != DayPhase.Ending; guard++)
            {
                switch (day.Phase)
                {
                    case DayPhase.Intro:
                        if (seen.Add(day.CurrentDay.Number))
                        {
                            yield return UITestDriver.Frames(2);
                            string what = "day " + day.DisplayDay + " intro";
                            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen, what);
                            string today = Plain(ui.FlowToday);
                            StringAssert.StartsWith(ko ? "오늘: " : "TODAY: ", today, what + ": the TODAY line");
                            yield return Day0Driver.Shot("ui_intro_day" + day.DisplayDay + suffix, true);
                            AssertCardClean(ui, what);
                        }
                        s.BeginDay();
                        break;
                    case DayPhase.Running:
                        s.EndDayNow();
                        yield return null;
                        break;
                    case DayPhase.Summary:
                        if (!day.IsTutorial)
                        {
                            // this week's summaries graded into the journey (they replace the empty days just played)
                            int d = day.DisplayDay;
                            GameServices.Events.Publish(new DayEnded(day.CurrentDay, Week[d - 1]));
                            if (d == 1) yield return SummaryCard(ui, day, suffix);
                        }
                        s.ContinueFromSummary();
                        break;
                    case DayPhase.Shop:
                        s.FinishShopping();
                        break;
                }
            }
            Assert.AreEqual(data.DayCount, seen.Count, "every day's intro card was seen");
            Assert.AreEqual(DayPhase.Ending, day.Phase);

            // ---- ending: seven stamps, the week's totals, the best record beaten
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            Assert.AreEqual("1:B 2:A 3:S 4:A 5:C 6:S 7:A", ui.EndingJourneyText);
            string extra = Plain(ui.FlowExtra);
            StringAssert.Contains("$470", extra, "tips earned over the run");
            StringAssert.Contains("x7", extra, "best streak of the run");
            StringAssert.Contains("69", extra, "guests delivered over the run");
            string best = Plain(ui.EndingBestText);
            int luck = GameServices.Get<IScoreQuery>().Luck;
            Assert.Greater(luck, 40, "this run beats the saved Luck");
            StringAssert.Contains(ko ? "최고 기록 경신!" : "NEW BEST!", best);
            StringAssert.Contains("470", best);
            StringAssert.Contains(",L:" + luck + ",T:470", store.Value, "saved with the endings");
            var row = ui.EndingJourneyRect;
            var card = ui.FlowCardRect;
            Assert.IsTrue(row.xMin >= card.xMin && row.xMax <= card.xMax, "stamps inside the card: " + row + " / " + card);
            AssertCardClean(ui, "ending");
            yield return Day0Driver.Shot("ui_ending_recap" + suffix, true);

            // ---- title: the best plate next to ENDINGS
            ui.ClickEndingTitle();
            yield return UITestDriver.Frames(3);
            Assert.IsTrue(ui.TitleVisible);
            Assert.IsTrue(ui.TitleBestVisible, "best record beside ENDINGS");
            Assert.AreEqual(UIFormat.BestRecord(luck, 470, ko), ui.TitleBestText);
            var plate = ui.TitleBestRect;
            var endings = ui.TitlePartRect(MenuView.TitlePart.Endings);
            var size = ui.CanvasSize;
            Assert.IsTrue(plate.xMin >= endings.xMax && plate.xMax <= size.x - 8f, "right of the button, on screen: " + plate);
            Assert.AreEqual(endings.center.y, plate.center.y, 1f, "level with ENDINGS");
            foreach (var p in new[] { MenuView.TitlePart.Start, MenuView.TitlePart.Credits, MenuView.TitlePart.Options })
                Assert.IsFalse(plate.Overlaps(ui.TitlePartRect(p)), "clear of " + p);
            Assert.IsNull(UIFonts.MissingGlyph(ui.TitleBestText));
            Assert.IsNull(ui.FirstOffGridOrForeignText());
            yield return UITestDriver.Until(() => false, 10);
            yield return Day0Driver.Shot("ui_title_best" + suffix, true);
        }

        /// <summary>Day 1's summary with a chosen B-grade day: the hint names the weakest part, in the stamp's colour.</summary>
        static IEnumerator SummaryCard(UIRoot ui, IDayQuery real, string suffix)
        {
            yield return UITestDriver.Frames(2);
            string hint = Plain(ui.FlowHint);
            Assert.IsFalse(string.IsNullOrEmpty(hint), "real days have a hint line");
            var b = new DaySummary(8, 2, 40, 50, 54, 5, 2, 3);
            ui.OverrideServicesForTests(day: new SummaryOverrideDay(real, b));
            yield return UITestDriver.Until(() => ui.GradeStampLanded, 10);
            Assert.AreEqual("B", ui.GradeStampText);
            Assert.AreEqual(FloorF.UI.SummaryHint.Text(b, ui.Korean), Plain(ui.FlowHint));
            StringAssert.Contains("<color=" + FlowScreensView.GradeHex("B") + ">", ui.FlowHint);
            AssertCardClean(ui, "summary");
            yield return Day0Driver.Shot("ui_summary_hint" + suffix, true);
            ui.OverrideServicesForTests(day: real);
            yield return null;
        }

        static void AssertCardClean(UIRoot ui, string what)
        {
            var card = ui.FlowCardRect;
            Assert.Greater(card.height, 100f, what + ": card shown");
            Assert.LessOrEqual(card.height, UIStyle.RefHeight - 16f, what + ": card " + card + " fits a 720-high screen");
            Assert.IsNull(ui.FlowTextOverflow(), what);
            string all = Plain(ui.FlowTitle) + Plain(ui.FlowBody) + Plain(ui.FlowToday) + Plain(ui.FlowExtra) + Plain(ui.FlowHint) + ui.EndingJourneyText;
            Assert.IsNull(UIFonts.MissingGlyph(all), what + ": every glyph in the font");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), what);
        }

        // ------------------------------------------------------------------ rider chips on the ground floor

        [UnityTest]
        public IEnumerator RiderChips_OnTheGroundFloor_StayOffTheLobbyAndTheSigns()
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var guests = GameServices.Get<IGuestQuery>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var layout = GameServices.Get<IWorldLayout>();
            yield return UITestDriver.ClickStart();
            var waiting = new List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= 3);
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3, 80);
            Assert.GreaterOrEqual(elevator.Occupancy, 3, "three riders");
            Assert.AreEqual(FloorIndex.Ground, elevator.CurrentFloor, "still on the ground floor");
            yield return UITestDriver.Frames(2);

            var slab = ToV3(layout.GetFloorAnchor(new FloorIndex(1)));
            Assert.IsTrue(ui.WorldToCanvas(slab, out var secondFloor));
            Assert.IsTrue(UIMenuTutorialTests.SignColumn(ui, out float sMin, out float sMax), "floor signs rendered");
            var riders = elevator.Riders;
            var riderRects = new List<Rect>();
            foreach (var g in riders)
            {
                var r = ui.ChipRect(g);
                Assert.Greater(r.width, 0f, "rider chip shown");
                Assert.GreaterOrEqual(r.yMin, secondFloor.y - 1.5f, "rider chip above the lobby / street: " + r + " vs 2F " + secondFloor.y);
                Assert.LessOrEqual(r.xMax, sMin + 0.5f, "rider chip left of the floor numbers");
                riderRects.Add(r);
            }
            Assert.IsNull(ui.ChipOverlapX(sMin, sMax), "no chip over the floor signs");
            int n = guests.GetWaiting(FloorIndex.Ground, waiting);
            for (int i = 0; i < n; i++)
            {
                var w = ui.ChipRect(waiting[i]);
                foreach (var r in riderRects) Assert.IsFalse(r.Overlaps(w), "rider chip over the lobby queue");
            }
            ui.ClickClose();
            yield return UITestDriver.Until(() => false, 4);
            yield return Day0Driver.Shot("ui_riders_lobby", true);
        }

        static Vector3 ToV3(WorldPoint p) => new Vector3(p.X, p.Y, p.Z);
    }
}
