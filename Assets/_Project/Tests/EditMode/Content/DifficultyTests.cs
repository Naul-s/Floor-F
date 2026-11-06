using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Difficulty on the REAL Resources/Data/*.json (content-designer): ignoring luck must not be enough to prosper,
    /// playing for luck must be, and the tutorial must forgive mistakes. Drives the real <see cref="GameSession"/> with
    /// <see cref="AutoPilot"/> (LuckBlind / LuckAware, public API only). Intent + before/after numbers: Docs/Content/balance.md ("난이도").
    /// </summary>
    public class DifficultyTests
    {
        /// <summary>Seeds vary GameSession's destination picks and the bot's reaction delay.</summary>
        static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

        /// <summary>The luck-blind run should land well below the Prosperous line, not just under it.</summary>
        const int AverageMargin = 10;

        /// <summary>Playing for luck must beat ignoring it by a clear gap (on average).</summary>
        const int SkillGap = 20;

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ bot runner

        public enum ShopPolicy { None, BuyEverythingOnFour }

        readonly struct RunResult
        {
            public readonly EndingKind Ending;
            public readonly int EndingDay;
            public readonly int FinalLuck;
            public readonly int[] LuckAfterDay;
            public RunResult(EndingKind ending, int endingDay, int finalLuck, int[] luckAfterDay)
            { Ending = ending; EndingDay = endingDay; FinalLuck = finalLuck; LuckAfterDay = luckAfterDay; }
            public override string ToString() =>
                Ending + " on day " + EndingDay + ", luck " + FinalLuck + " (by day: " + string.Join(", ", LuckAfterDay) + ")";
        }

        /// <summary>
        /// Plays with the luck-blind AutoPilot until an ending (or <paramref name="lastDay"/> ends).
        /// A fresh pilot each day (a human starts each day fresh too); reaction delay 0-0.5 s from the seed.
        /// </summary>
        RunResult Run(int seed, ShopPolicy shop, int lastDay = int.MaxValue, AutoPilotStrategy strategy = AutoPilotStrategy.LuckBlind)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new Random(seed));
            var luckAfterDay = new List<int>();
            var ending = EndingKind.None;
            int endingDay = 0, finalLuck = -1;
            bus.Subscribe<DayEnded>(e => luckAfterDay.Add(e.Summary.LuckAtEnd));
            bus.Subscribe<EndingReached>(e => { ending = e.Ending; endingDay = e.Day.Number; finalLuck = e.FinalLuck; });

            var reaction = new Random(seed * 7919 + 13);
            AutoPilot pilot = null;
            float wait = 0f;
            const float dt = 1f / 30f;
            s.StartNewGame();
            for (int frame = 0; frame < 300000 && s.Phase != DayPhase.Ending; frame++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Intro:
                        if (s.CurrentDay.Number > lastDay) return new RunResult(ending, s.CurrentDay.Number - 1, s.Score.Luck, luckAfterDay.ToArray());
                        pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building, strategy) { Data = data };
                        s.BeginDay();
                        break;
                    case DayPhase.Summary:
                        if (s.CurrentDay.Number >= lastDay) return new RunResult(ending, s.CurrentDay.Number, s.Score.Luck, luckAfterDay.ToArray());
                        s.ContinueFromSummary();
                        break;
                    case DayPhase.Shop:
                        if (shop == ShopPolicy.BuyEverythingOnFour)
                            foreach (var o in s.Offers) s.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor);
                        s.FinishShopping();
                        break;
                    case DayPhase.Running:
                        if (wait <= 0f) { pilot.Step(); wait = (float)reaction.NextDouble() * 0.5f; }
                        wait -= dt;
                        s.Tick(dt);
                        break;
                }
            }
            Assert.AreEqual(DayPhase.Ending, s.Phase, "seed " + seed + ": the run never reached an ending");
            return new RunResult(ending, endingDay, finalLuck, luckAfterDay.ToArray());
        }

        // ------------------------------------------------------------ tests

        [Test]
        public void LuckBlindBot_DoesNotProsper([Values] ShopPolicy shop)
        {
            int prosperous = data.Rules.prosperousEndingLuck;
            var results = Seeds.Select(seed => (seed, r: Run(seed, shop))).ToArray();
            foreach (var (seed, r) in results)
            {
                TestContext.WriteLine("seed " + seed + " shop " + shop + ": " + r);
                Assert.AreNotEqual(EndingKind.Prosperous, r.Ending, "seed " + seed + ": " + r);
                bool notProsperous = r.Ending == EndingKind.Haunted || r.Ending == EndingKind.Modest || r.FinalLuck < prosperous;
                Assert.IsTrue(notProsperous, "seed " + seed + ": ignoring luck still ends prosperous - " + r);
            }
            double avg = results.Average(x => x.r.FinalLuck);
            TestContext.WriteLine("average final luck " + avg.ToString("F1") + " (prosperous " + prosperous + ")");
            Assert.LessOrEqual(avg, prosperous - AverageMargin, "luck-blind play lands too close to Prosperous");
        }

        /// <summary>The other half of the difficulty target: the greedy luck player reaches the Prosperous line.</summary>
        [Test]
        public void LuckAwareBot_Prospers_AndBeatsLuckBlind([Values] ShopPolicy shop)
        {
            int prosperous = data.Rules.prosperousEndingLuck;
            var aware = Seeds.Select(seed => (seed, r: Run(seed, shop, strategy: AutoPilotStrategy.LuckAware))).ToArray();
            foreach (var (seed, r) in aware)
            {
                TestContext.WriteLine("LuckAware seed " + seed + " shop " + shop + ": " + r);
                Assert.AreNotEqual(EndingKind.Haunted, r.Ending, "seed " + seed + ": " + r);
                Assert.GreaterOrEqual(r.FinalLuck, prosperous, "seed " + seed + ": playing for luck should reach Prosperous - " + r);
            }
            double awareAvg = aware.Average(x => x.r.FinalLuck);
            double blindAvg = Seeds.Average(seed => Run(seed, shop).FinalLuck);
            TestContext.WriteLine("average final luck: LuckAware " + awareAvg.ToString("F1") + ", LuckBlind " + blindAvg.ToString("F1"));
            Assert.GreaterOrEqual(awareAvg - blindAvg, SkillGap, "luck play barely beats ignoring luck");
        }

        /// <summary>Over-tuning guard: careless but diligent play is punished, not killed before the last day.</summary>
        [Test]
        public void LuckBlindBot_ReachesLastDay()
        {
            foreach (var seed in Seeds)
            {
                var r = Run(seed, ShopPolicy.None);
                TestContext.WriteLine("seed " + seed + ": " + r);
                Assert.AreEqual(data.DayCount, r.EndingDay, "seed " + seed + ": luck-blind play should still see the last day - " + r);
            }
        }

        /// <summary>
        /// 4th-floor ending, choice A: the mystery guest only ends the game at the real 4th floor while a sticker hides it.
        /// Without stickers the luck player finishes normally; with 4 -> F on floor 4 it reaches FourthFloor on the last day.
        /// </summary>
        [Test]
        public void FourthFloorEnding_OnlyWhenFloor4IsHidden()
        {
            foreach (var seed in Seeds)
            {
                var plain = Run(seed, ShopPolicy.None, strategy: AutoPilotStrategy.LuckAware);
                TestContext.WriteLine("seed " + seed + " no stickers: " + plain);
                Assert.AreNotEqual(EndingKind.FourthFloor, plain.Ending, "seed " + seed + ": honest 4th floor still gave the 4th-floor ending");
                Assert.AreEqual(data.DayCount, plain.EndingDay, "seed " + seed + ": " + plain);

                var hidden = Run(seed, ShopPolicy.BuyEverythingOnFour, strategy: AutoPilotStrategy.LuckAware);
                TestContext.WriteLine("seed " + seed + " floor 4 hidden: " + hidden);
                Assert.AreEqual(EndingKind.FourthFloor, hidden.Ending, "seed " + seed + ": hidden 4th floor + mystery guest should end there");
                Assert.AreEqual(data.DayCount, hidden.EndingDay, "seed " + seed + ": " + hidden);
            }
        }

        [Test]
        public void LuckBlindBot_SurvivesDay1()
        {
            var r0 = data.Rules;
            foreach (var seed in Seeds)
            {
                var r = Run(seed, ShopPolicy.None, lastDay: 1);
                TestContext.WriteLine("seed " + seed + ": " + r);
                Assert.AreEqual(EndingKind.None, r.Ending, "seed " + seed + ": the tutorial ended the game - " + r);
                Assert.AreEqual(1, r.LuckAfterDay.Length, "seed " + seed + ": day 1 did not finish");
                Assert.Greater(r.LuckAfterDay[0], 0, "seed " + seed + ": haunted in the tutorial");
            }

            // Worst case on paper: every tutorial guest suffers every unlucky label once and every reachable unlucky
            // headcount once (or walks out) - the hotel must still stand.
            var day1 = data.GetDay(DayId.First);
            int people = day1.spawns.Sum(sp => data.GetGuest(sp.Guest).partySize);
            int worst = 0;
            foreach (var sp in day1.spawns)
            {
                var g = data.GetGuest(sp.Guest);
                var p = data.GetLuckProfile(sp.Guest);
                int labels = 0;
                for (int f = 0; f < data.FloorCount; f++)
                    if (data.GetFloor(new FloorIndex(f)).InitialLabel.TryGetNumber(out var n) && p.Judge(n) == LuckVerdict.Unlucky) labels++;
                int heads = Enumerable.Range(1, people).Count(n => p.Judge(n) == LuckVerdict.Unlucky);
                int ride = labels * r0.unluckyFloorLuckPenalty + heads * r0.unluckyOccupancyLuckPenalty;
                worst += g.partySize * Math.Max(ride, r0.patienceOutLuckPenalty);
            }
            Assert.Less(worst, r0.startLuck, "day 1 worst case (" + worst + ") could haunt the hotel");
        }

        [Test]
        public void Tutorial_Day1_StillThreeGuests()
        {
            var day1 = data.GetDay(DayId.First);
            Assert.IsTrue(day1.tutorial, "day 1 is the tutorial");
            Assert.AreEqual(3, day1.spawns.Length, "GDD 3: 3-guest tutorial");
            Assert.AreEqual(3, day1.spawns.Sum(sp => data.GetGuest(sp.Guest).partySize), "3 people, not 3 parties");
            Assert.IsTrue(day1.spawns.All(sp => !string.IsNullOrWhiteSpace(sp.destination) || sp.HasDestinationFloor),
                "tutorial destinations are scripted, not random");
        }
    }
}
