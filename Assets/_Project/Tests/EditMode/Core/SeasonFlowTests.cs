using System;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Winter season (ISeasonFlow.ExtendSeason, ISeasonQuery, extended days, WinterMiracle / SilentNight) and the run
    /// counter (IRunHistory.FinishedRuns). Fixture: base days 1-2 (day 2 = last base day), winter days 3-4 (extended).
    /// </summary>
    public class SeasonFlowTests
    {
        const int Threshold = 70;

        static GameData SeasonData(Action<SimFixture.Defs> tweak = null) => SimFixture.Create(d =>
        {
            d.Days.days = new[]
            {
                new DayDef { day = 1, durationSeconds = 1000f, shopStickers = new[] { "four_to_f" } },
                new DayDef { day = 2, durationSeconds = 1000f, shopStickers = new[] { "four_to_f", "any_to_8" } },
                new DayDef { day = 3, durationSeconds = 1000f, extended = true, shopStickers = new[] { "any_to_8" } },
                new DayDef { day = 4, durationSeconds = 1000f, extended = true, shopStickers = new string[0] },
            };
            tweak?.Invoke(d);
        });

        /// <summary>New game on the season fixture, played to the base Ending with <paramref name="luck"/> on the last day.</summary>
        static Sim AtBaseEnding(int luck, Action<SimFixture.Defs> tweak = null, int tips = 0)
        {
            var sim = new Sim(SeasonData(tweak));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.Score.SetLuck(luck, LuckSource.Other);
            if (tips > 0) sim.S.Score.AddTips(tips, earned: false);
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            return sim;
        }

        /// <summary>From the base Ending through the extension to <paramref name="displayDay"/>'s Intro.</summary>
        static void ExtendTo(Sim sim, int displayDay)
        {
            Assert.IsTrue(sim.S.ExtendSeason());
            if (sim.S.Phase == DayPhase.Shop) Assert.IsTrue(sim.S.FinishShopping());
            while (sim.S.DisplayDay < displayDay)
            {
                Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
                Assert.IsTrue(sim.S.BeginDay());
                sim.S.EndDayNow();
                Assert.IsTrue(sim.S.ContinueFromSummary());
                if (sim.S.Phase == DayPhase.Shop) Assert.IsTrue(sim.S.FinishShopping());
            }
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
        }

        // ================================================================================== base run unchanged

        [Test]
        public void GameData_ExtendedDays_AreATrailingBlock_NotInTheBaseCount()
        {
            var data = SeasonData();
            Assert.AreEqual(2, data.DayCount);
            Assert.AreEqual(4, data.AllDayCount);
            Assert.AreEqual(2, data.ExtendedDayCount);
            Assert.IsTrue(data.IsLastDay(new DayId(2)));
            Assert.IsTrue(data.IsLastSeasonDay(new DayId(4)));
            Assert.IsTrue(data.IsExtendedDay(new DayId(3)));
        }

        [Test]
        public void BaseLastDay_EndsProsperousOrModest_AsBefore([Values(80, 40)] int luck)
        {
            var sim = AtBaseEnding(luck);
            var expected = luck >= Threshold ? EndingKind.Prosperous : EndingKind.Modest;
            Assert.AreEqual(expected, sim.S.Ending);
            Assert.AreEqual(expected, sim.Events<EndingReached>().Single().Ending);
            Assert.AreEqual(new DayId(2), sim.S.CurrentDay);
            Assert.IsFalse(sim.S.IsSeasonExtended);
            Assert.AreEqual(2, sim.S.PlayableDayCount, "the base run still reads /2");
            Assert.IsTrue(sim.S.CanExtendSeason);
        }

        [Test]
        public void CanExtendSeason_OnlyAfterABaseProsperousOrModestEnding()
        {
            var sim = new Sim(SeasonData());
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.CanExtendSeason, "intro");
            Assert.IsFalse(sim.S.ExtendSeason());
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsFalse(sim.S.CanExtendSeason, "running");
            sim.S.Score.SetLuck(1, LuckSource.Other);
            sim.Spawn("impatient", 1, 3);
            sim.Run(6f);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.IsFalse(sim.S.CanExtendSeason, "Haunted is final");
            Assert.IsFalse(sim.S.ExtendSeason());

            var fourth = new Sim(SeasonData());
            fourth.S.StartNewGame();
            Assert.IsTrue(fourth.S.DebugForceEnding(EndingKind.FourthFloor));
            Assert.IsFalse(fourth.S.CanExtendSeason, "the 4th floor is final");
        }

        [Test]
        public void CanExtendSeason_False_WithoutExtendedDays()
        {
            var sim = new Sim(SimFixture.Create());
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Prosperous));
            Assert.IsFalse(sim.S.CanExtendSeason);
            Assert.IsFalse(sim.S.ExtendSeason());
        }

        // ================================================================================== extension

        [Test]
        public void ExtendSeason_PublishesSeasonExtended_ThenOpensTheLastBaseDaysShop_KeepsLuckAndTips()
        {
            var sim = AtBaseEnding(80, tips: 500);
            int luck = sim.S.Score.Luck, tips = sim.S.Score.Tips;
            sim.Log.Clear();
            Assert.IsTrue(sim.S.ExtendSeason());
            sim.AssertOrder(typeof(SeasonExtended), typeof(DayPhaseChanged));
            var ext = sim.Events<SeasonExtended>().Single();
            Assert.AreEqual(new DayId(3), ext.FirstDay);
            Assert.AreEqual(2, ext.ExtraDays);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Ending, phase.Previous);
            Assert.AreEqual(DayPhase.Shop, phase.Current);
            Assert.AreEqual(EndingKind.None, sim.S.Ending);
            Assert.IsTrue(sim.S.IsSeasonExtended);
            Assert.IsFalse(sim.S.IsExtendedDay, "still the base last day's shop");
            Assert.IsFalse(sim.S.CanExtendSeason, "once");
            CollectionAssert.AreEquivalent(new[] { "four_to_f", "any_to_8" }, sim.S.Offers.Select(o => o.Id.Value), "day 2's shopStickers");
            Assert.AreEqual(luck, sim.S.Score.Luck);
            Assert.AreEqual(tips, sim.S.Score.Tips);
            Assert.AreEqual(4, sim.S.PlayableDayCount, "/4 once extended");
            Assert.AreEqual(4, sim.S.DayCount);
            Assert.IsEmpty(sim.Events<EndingReached>(), "no ending event for the extension");

            Assert.IsTrue(sim.S.FinishShopping());
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(new DayId(3), sim.S.CurrentDay);
            Assert.AreEqual(3, sim.S.DisplayDay);
            Assert.IsTrue(sim.S.IsExtendedDay);
            Assert.IsTrue(sim.S.BeginDay());
            var started = sim.Events<DayStarted>().Single();
            Assert.AreEqual(4, started.DayCount, "DayStarted carries the season total");
        }

        [Test]
        public void ExtendSeason_EmptyShop_GoesStraightToTheFirstWinterIntro()
        {
            var sim = AtBaseEnding(50, d => d.Days.days[1].shopStickers = new string[0]);
            sim.Log.Clear();
            Assert.IsTrue(sim.S.ExtendSeason());
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(new DayId(3), sim.S.CurrentDay);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Ending, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);
        }

        [Test]
        public void WinterDay_NotLast_GoesToTheShop()
        {
            var sim = AtBaseEnding(80);
            ExtendTo(sim, 3);
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, sim.S.Phase, "day 3 has a shop");
            Assert.AreEqual(EndingKind.None, sim.S.Ending);
        }

        [Test]
        public void LastWinterDay_EndsWinterMiracle_OrSilentNight([Values(70, 100, 69, 1)] int luck)
        {
            var sim = AtBaseEnding(50);
            ExtendTo(sim, 4);
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.Score.SetLuck(luck, LuckSource.Other);
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            var expected = luck >= Threshold ? EndingKind.WinterMiracle : EndingKind.SilentNight;
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(expected, sim.S.Ending);
            var endings = sim.Events<EndingReached>();
            Assert.AreEqual(2, endings.Count, "base ending, then the winter ending");
            Assert.AreEqual(expected, endings[1].Ending);
            Assert.AreEqual(new DayId(4), endings[1].Day);
            Assert.IsFalse(sim.S.CanExtendSeason);
            Assert.AreEqual(4, sim.S.PlayableDayCount, "the winter ending reads /4");
            Assert.IsFalse(sim.S.BeginDay() || sim.S.ContinueFromSummary() || sim.S.FinishShopping(), "terminal");
        }

        [Test]
        public void WinterDay_LuckZero_Haunted()
        {
            var sim = AtBaseEnding(50);
            ExtendTo(sim, 3);
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.Score.SetLuck(5, LuckSource.Other);
            sim.Spawn("impatient", 1, 3);
            sim.Run(6f);
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.AreEqual(new DayId(3), sim.Events<EndingReached>().Last().Day);
        }

        [Test]
        public void RestartGame_OrTitle_EndsTheSeason([Values(false, true)] bool viaTitle)
        {
            var sim = AtBaseEnding(80);
            ExtendTo(sim, 3);
            Assert.IsTrue(sim.S.IsSeasonExtended);
            if (viaTitle)
            {
                sim.S.ReturnToTitle();
                Assert.IsFalse(sim.S.IsSeasonExtended, "title");
                Assert.IsFalse(sim.S.IsExtendedDay, "title");
                Assert.IsTrue(sim.S.StartGameFromTitle());
            }
            else sim.S.RestartGame();
            Assert.IsFalse(sim.S.IsSeasonExtended);
            Assert.AreEqual(2, sim.S.PlayableDayCount);
            Assert.AreEqual(DayId.First, sim.S.CurrentDay);
            Assert.AreEqual(sim.S.Data.Rules.startLuck, sim.S.Score.Luck);
        }

        [Test]
        public void DebugJumpToDay_WinterDay_TurnsTheSeasonOn_BaseDayOff()
        {
            var sim = new Sim(SeasonData());
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.DebugJumpToDay(4));
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.IsTrue(sim.S.IsSeasonExtended);
            Assert.IsTrue(sim.S.IsExtendedDay);
            Assert.AreEqual(4, sim.S.PlayableDayCount);
            Assert.AreEqual(4, sim.Events<GameStarted>().Last().DayCount);
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.Score.SetLuck(90, LuckSource.Other);
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(EndingKind.WinterMiracle, sim.S.Ending);

            Assert.IsTrue(sim.S.DebugJumpToDay(1));
            Assert.IsFalse(sim.S.IsSeasonExtended);
            Assert.AreEqual(2, sim.S.PlayableDayCount);
            Assert.IsFalse(sim.S.DebugJumpToDay(5), "no day 5");
        }

        [Test]
        public void DebugForceProsperous_DuringWinter_BackToTheBaseEnding_CanExtendAgain()
        {
            var sim = AtBaseEnding(80);
            ExtendTo(sim, 3);
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Prosperous));
            Assert.AreEqual(new DayId(2), sim.S.CurrentDay);
            Assert.IsFalse(sim.S.IsSeasonExtended);
            Assert.IsTrue(sim.S.CanExtendSeason);
        }

        // ================================================================================== finished runs

        [Test]
        public void FinishedRuns_CountedAtTheRunsFirstEnding_NotAgainAfterTheExtension()
        {
            var sim = AtBaseEnding(80);
            Assert.AreEqual(1, sim.S.FinishedRuns);
            sim.AssertOrder(typeof(EndingReached), typeof(RunHistoryChanged));
            var changed = sim.Events<RunHistoryChanged>().Single();
            Assert.AreEqual(1, changed.FinishedRuns);
            Assert.AreEqual(0, changed.MysteryMisses);

            ExtendTo(sim, 4);
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(EndingKind.WinterMiracle, sim.S.Ending);
            Assert.AreEqual(1, sim.S.FinishedRuns, "the winter ending is the same run");
            Assert.AreEqual(1, sim.Events<RunHistoryChanged>().Count);

            sim.S.RestartGame();
            Assert.AreEqual(1, sim.S.FinishedRuns, "a restart is not a finished run");
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Haunted));
            Assert.AreEqual(2, sim.S.FinishedRuns, "the next run's ending counts");
            Assert.AreEqual(2, sim.Events<RunHistoryChanged>().Last().FinishedRuns);
        }

        [Test]
        public void FinishedRuns_RestartMidRun_DoesNotCount()
        {
            var sim = new Sim(SeasonData());
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.RestartGame();
            sim.S.ReturnToTitle();
            for (int i = 0; i < 100; i++) sim.S.Tick(0.5f);
            Assert.AreEqual(0, sim.S.FinishedRuns);
            Assert.IsEmpty(sim.Events<RunHistoryChanged>());
        }

        [Test]
        public void LoadRunHistory_RestoresSilently_ClampsNegatives()
        {
            var sim = new Sim(SeasonData());
            sim.S.LoadRunHistory(5, 2);
            Assert.AreEqual(5, sim.S.FinishedRuns);
            Assert.AreEqual(2, sim.S.MysteryMisses);
            sim.S.LoadRunHistory(-3, -1);
            Assert.AreEqual(0, sim.S.FinishedRuns);
            Assert.AreEqual(0, sim.S.MysteryMisses);
            Assert.IsEmpty(sim.Log);
        }

        [Test]
        public void SeasonQuery_ChristmasAndSnow_FromDayDef()
        {
            var sim = new Sim(SeasonData(d =>
            {
                d.Days.days[0].christmas = true;
                d.Days.days[0].snow = 0.3f;
                d.Days.days[0].luckyForAll = 12;
            }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.IsChristmas);
            Assert.AreEqual(0.3f, sim.S.SnowAmount, 1e-6f);
            Assert.AreEqual(12, sim.S.LuckyForAll);
            Assert.IsFalse(sim.S.IsLotteryNight);
            Assert.AreEqual(0, sim.S.LotteryNumber);
            Assert.IsTrue(sim.S.DebugJumpToDay(2));
            Assert.IsFalse(sim.S.IsChristmas);
            Assert.AreEqual(0f, sim.S.SnowAmount);
            Assert.AreEqual(0, sim.S.LuckyForAll);
        }
    }
}
