using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Sticker economy on the REAL Resources/Data/*.json (content-designer): the shop must be a choice, not a formality.
    /// A LuckAware <see cref="AutoPilot"/> earns the tips; at every shop a greedy buyer spends everything it can.
    /// Target: every visit it can afford a sticker, but never everything on offer (money runs out before stock/floors do).
    /// Intent + before/after numbers: Docs/Content/balance.md ("경제").
    /// </summary>
    public class EconomyTests
    {
        static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

        /// <summary>Where the greedy buyer puts an "any floor" sticker, best first (never 7/8: already lucky).</summary>
        static readonly int[] AnyStickerFloors = { 9, 5, 2, 6, 3, 1, 4, 7 };

        const string FourToF = "four_to_f";

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ runner

        sealed class ShopVisit
        {
            public int Day;
            public int TipsEarnedToday;
            public int TipsBefore;
            public int TipsAfter;
            public readonly List<string> Bought = new List<string>();
            /// <summary>Something offered, in stock and applicable, but too expensive (money was the limit).</summary>
            public bool MoneyWasTheLimit;
            /// <summary>Cheapest applicable offer at the start of the visit (int.MaxValue = none).</summary>
            public int CheapestApplicable = int.MaxValue;

            public override string ToString() =>
                "day " + Day + ": earned " + TipsEarnedToday + ", had " + TipsBefore + " -> bought [" + string.Join(", ", Bought) + "] -> left "
                + TipsAfter + (MoneyWasTheLimit ? " (money ran out)" : " (bought everything)");
        }

        static IEnumerable<FloorIndex> Targets(StickerDef s)
        {
            if (!s.MatchesAnyLabel) return new[] { StoryRules.RealFourthFloor };
            return AnyStickerFloors.Select(FloorIndex.FromPhysicalNumber);
        }

        /// <summary>Greedy: buy the first affordable applicable sticker (offer order, then floor order) until nothing more can be bought.</summary>
        static void BuyGreedy(GameSession s, ShopVisit v, bool buy)
        {
            v.TipsBefore = s.Score.Tips;
            foreach (var o in s.Offers)
                foreach (var f in Targets(o.Sticker))
                {
                    var r = s.CanBuyAndApply(o.Id, f);
                    if (r == StickerApplyResult.Applied || r == StickerApplyResult.NotEnoughTips) v.CheapestApplicable = Math.Min(v.CheapestApplicable, o.Price);
                }

            bool bought = buy;
            while (bought)
            {
                bought = false;
                foreach (var o in s.Offers)
                {
                    foreach (var f in Targets(o.Sticker))
                        if (s.TryBuyAndApply(o.Id, f) == StickerApplyResult.Applied) { v.Bought.Add(o.Id.Value + "@" + f.PhysicalNumber); bought = true; break; }
                    if (bought) break;
                }
            }

            foreach (var o in s.Offers)
                foreach (var f in Targets(o.Sticker))
                    if (s.CanBuyAndApply(o.Id, f) == StickerApplyResult.NotEnoughTips) v.MoneyWasTheLimit = true;
            v.TipsAfter = s.Score.Tips;
        }

        /// <summary>Plays days 1..(last-1) with the given bot and records every shop visit.</summary>
        List<ShopVisit> Run(int seed, AutoPilotStrategy strategy, bool buy)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new Random(seed));
            var visits = new List<ShopVisit>();
            int earned = 0;
            bus.Subscribe<DayEnded>(e => earned = e.Summary.TipsEarned);

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
                        if (s.CurrentDay.Number >= data.DayCount) return visits; // the last day has no shop after it
                        pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building, strategy) { Data = data };
                        s.BeginDay();
                        break;
                    case DayPhase.Summary:
                        if (s.CurrentDay.Number >= data.DayCount) return visits;
                        s.ContinueFromSummary();
                        break;
                    case DayPhase.Shop:
                        var v = new ShopVisit { Day = s.CurrentDay.Number, TipsEarnedToday = earned };
                        BuyGreedy(s, v, buy);
                        visits.Add(v);
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
            return visits;
        }

        // ------------------------------------------------------------ tests

        /// <summary>
        /// LuckAware play, greedy buyer: day 1 pays for 4 -> F; every shop sells it at least one sticker;
        /// no shop lets it buy everything (it always runs out of tips first).
        /// </summary>
        [Test]
        public void Economy_StickerIsAChoice()
        {
            var fourToF = data.Stickers.Single(x => x.id == FourToF);
            foreach (var seed in Seeds)
            {
                var visits = Run(seed, AutoPilotStrategy.LuckAware, buy: true);
                foreach (var v in visits) TestContext.WriteLine("seed " + seed + " " + v);
                Assert.AreEqual(data.PlayableDayCount - 1, visits.Count, "seed " + seed + ": one shop after every real day but the last");

                var day1 = visits[0];
                Assert.GreaterOrEqual(day1.TipsBefore, fourToF.price, "seed " + seed + ": day 1 must pay for 4 -> F");
                Assert.IsTrue(day1.Bought.Any(b => b.StartsWith(FourToF)), "seed " + seed + ": 4 -> F not bought after day 1");

                foreach (var v in visits)
                {
                    Assert.IsNotEmpty(v.Bought, "seed " + seed + " day " + v.Day + ": could not afford any sticker - " + v);
                    Assert.IsTrue(v.MoneyWasTheLimit, "seed " + seed + " day " + v.Day + ": bought everything on offer - " + v);
                    Assert.LessOrEqual(v.Bought.Count, 2, "seed " + seed + " day " + v.Day + ": more than 2 stickers in one visit - " + v);
                }
            }
        }

        /// <summary>Report only (no purchases): what each bot earns per day. Numbers for balance.md.</summary>
        [Test]
        public void Economy_TipsPerDay_Report([Values] AutoPilotStrategy strategy)
        {
            foreach (var seed in Seeds)
            {
                var visits = Run(seed, strategy, buy: false);
                TestContext.WriteLine(strategy + " seed " + seed + ": earned by day " + string.Join(", ", visits.Select(v => v.TipsEarnedToday))
                                      + " (total before day " + data.DayCount + ": " + visits.Last().TipsBefore + ")");
                Assert.AreEqual(data.PlayableDayCount - 1, visits.Count);
            }
        }

        /// <summary>
        /// The 4th-floor ending (choice A) needs floor 4 to be relabelled when the mystery guest arrives, so the shop right
        /// before the last day must still sell 4 -> F (a player who skipped it can still choose the ending).
        /// </summary>
        [Test]
        public void Day4Shop_OffersFourToF()
        {
            var beforeLast = data.GetDay(new DayId(data.DayCount - 1));
            CollectionAssert.Contains(beforeLast.shopStickers, FourToF, "the shop before the last day must offer 4 -> F");
            Assert.IsEmpty(data.GetDay(new DayId(data.DayCount)).shopStickers, "no shop after the last day");
            var s = data.Stickers.Single(x => x.id == FourToF);
            Assert.AreEqual("4", s.from.Trim());
            Assert.IsTrue(s.CanApplyTo(data.GetFloor(StoryRules.RealFourthFloor).InitialLabel), "4 -> F must fit the real 4th floor");
        }
    }
}
