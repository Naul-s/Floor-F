using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Shop economy on the REAL Resources/Data/*.json (content-designer): one-day stickers + charms, random offers, rerolls.
    /// A bot earns the tips; at every shop the greedy <see cref="ShopBot"/> buys whatever helps tomorrow and rerolls while
    /// it can. Targets: every visit it can afford something, a good player buys 2-3 things, and money (not stock) is the
    /// limit - the shop stays a choice. Intent + before/after numbers: Docs/Content/balance.md ("경제").
    /// </summary>
    public class EconomyTests
    {
        static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

        const string FourToF = "four_to_f";

        /// <summary>Upper bound for the greedy shopper's average purchases per visit (it rerolls too).</summary>
        const double MaxAveragePurchases = 6.0;

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ runner

        sealed class Day
        {
            public int Number, TipsEarned;
            public ShopBot.Visit Visit;
            public override string ToString() => "earned " + TipsEarned + ", " + (Visit != null ? Visit.ToString() : "no shop");
        }

        /// <summary>Plays days 1..(last-1) with the given bot; <paramref name="buy"/> = shop with ShopBot (else skip the shop).</summary>
        List<Day> Run(int seed, AutoPilotStrategy strategy, bool buy)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new Random(seed));
            var days = new List<Day>();
            var shopper = new ShopBot(data);
            bus.Subscribe<DayEnded>(e => days.Add(new Day { Number = e.Day.Number, TipsEarned = e.Summary.TipsEarned }));

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
                        if (s.IsTutorial) { Assert.IsTrue(s.SkipTutorial()); break; } // the bot never plays Day 0 (no shop after it anyway)
                        if (s.CurrentDay.Number >= data.DayCount) return days; // the last day has no shop after it
                        pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building, strategy) { Data = data };
                        s.BeginDay();
                        break;
                    case DayPhase.Summary:
                        if (s.CurrentDay.Number >= data.DayCount) return days;
                        s.ContinueFromSummary();
                        break;
                    case DayPhase.Shop:
                        if (buy) days[days.Count - 1].Visit = shopper.Shop(s);
                        s.FinishShopping();
                        break;
                    case DayPhase.Running:
                        if (wait <= 0f) { pilot.Step(); wait = (float)reaction.NextDouble() * 0.5f; }
                        wait -= dt;
                        s.Tick(dt);
                        break;
                }
            }
            Assert.Fail("seed " + seed + ": ended before the last day (" + s.Phase + ")");
            return days;
        }

        // ------------------------------------------------------------ tests

        /// <summary>
        /// LuckAware play, greedy shopper: every shop sells it something; on average 2+ things per
        /// visit; money (an item or a reroll it still wanted) is the limit at every visit.
        /// </summary>
        [Test]
        public void Economy_ShopIsAChoice()
        {
            var all = new List<ShopBot.Visit>();
            foreach (var seed in Seeds)
            {
                var days = Run(seed, AutoPilotStrategy.LuckAware, buy: true);
                foreach (var d in days) TestContext.WriteLine("seed " + seed + " day " + d.Number + ": " + d);
                var visits = days.Select(d => d.Visit).ToList();
                Assert.AreEqual(data.PlayableDayCount - 1, visits.Count(v => v != null), "seed " + seed + ": one shop after every real day but the last");

                foreach (var v in visits)
                {
                    Assert.IsNotEmpty(v.Bought, "seed " + seed + ": could not afford anything - " + v);
                    Assert.IsTrue(v.MoneyWasTheLimit, "seed " + seed + ": bought everything it wanted - " + v);
                }
                all.AddRange(visits);
            }
            double avg = all.Average(v => v.Bought.Count);
            double rerolls = all.Average(v => v.Rerolls);
            TestContext.WriteLine("average purchases per visit " + avg.ToString("F2") + ", rerolls " + rerolls.ToString("F2"));
            Assert.GreaterOrEqual(avg, 2.0, "a good player should buy 2-3 things per visit");
            Assert.LessOrEqual(avg, MaxAveragePurchases, "the shop hands out too much");
        }

        /// <summary>A careless (LuckBlind) player can still afford something useful at every shop.</summary>
        [Test]
        public void Economy_LuckBlind_AffordsSomethingEveryVisit()
        {
            foreach (var seed in Seeds)
            {
                var days = Run(seed, AutoPilotStrategy.LuckBlind, buy: true);
                foreach (var d in days) TestContext.WriteLine("seed " + seed + " day " + d.Number + ": " + d);
                foreach (var v in days.Select(d => d.Visit).Where(v => v != null))
                    Assert.IsNotEmpty(v.Bought, "seed " + seed + ": " + v);
            }
        }

        /// <summary>Report only (no purchases): what each bot earns per day. Numbers for balance.md.</summary>
        [Test]
        public void Economy_TipsPerDay_Report([Values] AutoPilotStrategy strategy)
        {
            foreach (var seed in Seeds)
            {
                var days = Run(seed, strategy, buy: false);
                TestContext.WriteLine(strategy + " seed " + seed + ": earned by day " + string.Join(", ", days.Select(d => d.TipsEarned))
                                      + " (total " + days.Sum(d => d.TipsEarned) + ")");
                Assert.AreEqual(data.PlayableDayCount - 1, days.Count);
            }
        }

        /// <summary>
        /// The 4th-floor ending (choice A) needs floor 4 relabelled when the mystery guest arrives; stickers last one day,
        /// so the shop right before the last day must sell 4 -> F (and the session always offers that staple).
        /// </summary>
        [Test]
        public void Day4Shop_OffersFourToF()
        {
            var beforeLast = data.GetDay(new DayId(data.DayCount - 1));
            CollectionAssert.Contains(beforeLast.shopStickers, FourToF, "the shop before the last day must offer 4 -> F");
            Assert.IsEmpty(data.GetDay(new DayId(data.AllDayCount)).shopStickers, "no shop after the last day of the season");
            if (data.HasExtendedDays)
                for (int n = data.DayCount; n < data.AllDayCount; n++) // the winter-season shops keep the staple too
                    CollectionAssert.Contains(data.GetDay(new DayId(n)).shopStickers, FourToF, "season shop after day " + n);
            var s = data.Stickers.Single(x => x.id == FourToF);
            Assert.AreEqual("4", s.from.Trim());
            Assert.IsTrue(s.CanApplyTo(data.GetFloor(StoryRules.RealFourthFloor).InitialLabel), "4 -> F must fit the real 4th floor");
        }
    }
}
