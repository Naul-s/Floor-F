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

        /// <summary>Of the 6 seeds, how many luck-blind runs with the greedy shopper may still end Prosperous ("mostly Modest").</summary>
        const int MaxProsperousGreedyBlind = 1;

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ bot runner

        /// <summary>
        /// None: never buys. HideFour: every visit only the 4 -> F staple onto the real 4th floor (one-day stickers: so the
        /// last day's floor 4 is hidden for the mystery guest). Greedy: <see cref="ShopBot"/> spends everything it can.
        /// </summary>
        public enum ShopPolicy { None, HideFour, Greedy }

        static void HideFour(GameSession s)
        {
            var four = StoryRules.RealFourthFloor;
            foreach (var o in s.Offers)
                if (!o.Sticker.IsCharm && !o.Sticker.MatchesAnyLabel && s.CanBuyAndApply(o.Id, four) == StickerApplyResult.Applied)
                    s.TryBuyAndApply(o.Id, four);
        }

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
        RunResult Run(int seed, ShopPolicy shop, int lastDay = int.MaxValue, AutoPilotStrategy strategy = AutoPilotStrategy.LuckBlind,
            SortedDictionary<string, int> ledger = null, Action<EventBus> onBus = null, Func<StickerDef, bool> allow = null, bool knowsTomorrow = false,
            bool mysteryDue = false)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new Random(seed));
            // the mystery guest never comes on a fresh record; a played record at the pity limit guarantees it tonight
            if (mysteryDue) s.LoadRunHistory(1, data.Rules.mysteryPityRuns);
            onBus?.Invoke(bus);
            if (ledger != null)
                bus.Subscribe<LuckChanged>(e =>
                {
                    if (s.Phase != DayPhase.Running) return;
                    var key = "day " + s.DisplayDay + " " + e.Source + (e.Delta < 0 ? " -" : " +");
                    ledger.TryGetValue(key, out int sum);
                    ledger[key] = sum + e.Delta;
                });
            if (ledger != null)
                bus.Subscribe<LuckJudged>(e =>
                {
                    if (s.Phase != DayPhase.Running || !s.Guests.TryGet(e.Guest, out var g)) return;
                    var key = "day " + s.DisplayDay + "   " + e.Verdict + " " + e.Source + " " + e.Number + " " + g.Archetype.Value;
                    ledger.TryGetValue(key, out int n);
                    ledger[key] = n + 1;
                });
            var luckAfterDay = new List<int>();
            var ending = EndingKind.None;
            int endingDay = 0, finalLuck = -1;
            bus.Subscribe<DayEnded>(e => luckAfterDay.Add(e.Summary.LuckAtEnd));
            bus.Subscribe<EndingReached>(e => { ending = e.Ending; endingDay = e.Day.Number; finalLuck = e.FinalLuck; });

            var reaction = new Random(seed * 7919 + 13);
            var shopper = new ShopBot(data) { Allow = allow, KnowsTomorrow = knowsTomorrow };
            AutoPilot pilot = null;
            float wait = 0f;
            const float dt = 1f / 30f;
            s.StartNewGame();
            for (int frame = 0; frame < 300000 && s.Phase != DayPhase.Ending; frame++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Intro:
                        if (s.IsTutorial) { Assert.IsTrue(s.SkipTutorial()); break; } // the bot never plays Day 0
                        if (s.CurrentDay.Number > lastDay) return new RunResult(ending, s.CurrentDay.Number - 1, s.Score.Luck, luckAfterDay.ToArray());
                        pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building, strategy) { Data = data };
                        s.BeginDay();
                        break;
                    case DayPhase.Summary:
                        if (s.CurrentDay.Number >= lastDay) return new RunResult(ending, s.CurrentDay.Number, s.Score.Luck, luckAfterDay.ToArray());
                        s.ContinueFromSummary();
                        break;
                    case DayPhase.Shop:
                        if (shop == ShopPolicy.HideFour) HideFour(s);
                        else if (shop == ShopPolicy.Greedy) shopper.Shop(s);
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

        /// <summary>Report only: what each kind of purchase is worth to each bot (greedy shopper restricted to one kind).</summary>
        [Test]
        public void ShopValue_Report([Values] AutoPilotStrategy strategy)
        {
            var kinds = new (string name, Func<StickerDef, bool> allow)[]
            {
                ("all", null), ("labels", x => !x.IsCharm), ("charms", x => x.IsCharm),
                ("no speed", x => x.effect != StickerEffects.Speed), ("no patience", x => x.effect != StickerEffects.Patience),
                ("no shield", x => x.effect != StickerEffects.Shield), ("no tips", x => x.effect != StickerEffects.Tips),
                ("4F only", x => x.id == "four_to_f"),
            };
            var cheat = Seeds.Select(seed => Run(seed, ShopPolicy.Greedy, strategy: strategy, knowsTomorrow: true)).ToArray();
            TestContext.WriteLine(strategy + " greedy[all, knows tomorrow]: avg " + cheat.Average(r => r.FinalLuck).ToString("F1") + " | "
                                  + string.Join(" | ", cheat.Select(r => r.Ending + " " + r.FinalLuck)));
            foreach (var (name, allow) in kinds)
            {
                var rs = Seeds.Select(seed => Run(seed, ShopPolicy.Greedy, strategy: strategy, allow: allow)).ToArray();
                TestContext.WriteLine(strategy + " greedy[" + name + "]: avg " + rs.Average(r => r.FinalLuck).ToString("F1") + " | "
                                      + string.Join(" | ", rs.Select(r => r.Ending + " " + r.FinalLuck + " (" + string.Join(",", r.LuckAfterDay) + ")")));
            }
        }

        /// <summary>Report only: where each bot gains and loses Luck, per day and source (numbers for balance.md).</summary>
        [Test]
        public void LuckLedger_Report([Values] AutoPilotStrategy strategy, [Values(ShopPolicy.None, ShopPolicy.Greedy)] ShopPolicy shop)
        {
            foreach (var seed in new[] { 1, 2, 5 })
            {
                var ledger = new SortedDictionary<string, int>();
                var r = Run(seed, shop, strategy: strategy, ledger: ledger);
                TestContext.WriteLine(strategy + " shop " + shop + " seed " + seed + ": " + r);
                foreach (var kv in ledger) TestContext.WriteLine("  " + kv.Key + " " + kv.Value);
            }
        }

        [Test]
        public void LuckBlindBot_DoesNotProsper([Values] ShopPolicy shop)
        {
            int prosperous = data.Rules.prosperousEndingLuck;
            var results = Seeds.Select(seed => (seed, r: Run(seed, shop))).ToArray();
            int lucky = 0;
            foreach (var (seed, r) in results)
            {
                TestContext.WriteLine("seed " + seed + " shop " + shop + ": " + r);
                bool notProsperous = r.Ending == EndingKind.Haunted || r.Ending == EndingKind.Modest || r.FinalLuck < prosperous;
                if (notProsperous) continue;
                lucky++;
                // a careless driver who shops greedily may get lucky once in a while; without the shop's help never
                Assert.AreEqual(ShopPolicy.Greedy, shop, "seed " + seed + ": ignoring luck still ends prosperous - " + r);
            }
            Assert.LessOrEqual(lucky, MaxProsperousGreedyBlind, "careless driving + greedy shopping prospers too often");
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

        /// <summary>7 days: careless play ends Modest (or the 4th floor), never in the Haunted hotel - on any day.</summary>
        [Test]
        public void LuckBlindBot_NeverHaunted([Values] ShopPolicy shop)
        {
            foreach (var seed in Seeds)
            {
                var r = Run(seed, shop);
                TestContext.WriteLine("seed " + seed + " shop " + shop + ": " + r);
                Assert.AreNotEqual(EndingKind.Haunted, r.Ending, "seed " + seed + ": " + r);
                Assert.IsTrue(r.LuckAfterDay.All(l => l > 0), "seed " + seed + ": " + r);
            }
        }

        /// <summary>The star night's wishes: the luck player wishes on every star of the last day, the careless one on none.</summary>
        [Test]
        public void StarNight_Wishes_LuckAwareTakesThemAll()
        {
            var last = data.GetDay(new DayId(data.DayCount));
            Assert.IsTrue(last.starNight);
            foreach (var strategy in new[] { AutoPilotStrategy.LuckAware, AutoPilotStrategy.LuckBlind })
            {
                int wishes = 0, faded = 0, appeared = 0;
                var r = Run(1, ShopPolicy.None, strategy: strategy, onBus: bus =>
                {
                    bus.Subscribe<WishStarAppeared>(e => appeared++);
                    bus.Subscribe<WishMade>(e => wishes++);
                    bus.Subscribe<WishStarFaded>(e => faded++);
                });
                TestContext.WriteLine(strategy + ": " + r + ", wishes " + wishes + "/" + appeared);
                Assert.AreEqual(last.wishStars, appeared, strategy + ": every star of the night shows up");
                Assert.AreEqual(strategy == AutoPilotStrategy.LuckAware ? last.wishStars : 0, wishes, strategy.ToString());
                Assert.AreEqual(appeared, wishes + faded, "each star ends once");
            }
        }

        /// <summary>
        /// 4th-floor ending, choice A: the mystery guest only ends the game at the real 4th floor while a sticker hides it.
        /// Without stickers the luck player finishes normally; with 4 -> F on floor 4 it reaches FourthFloor on the last day.
        /// Both runs use a played record whose pity counter guarantees the (otherwise rare) mystery guest on the star night.
        /// </summary>
        [Test]
        public void FourthFloorEnding_OnlyWhenFloor4IsHidden()
        {
            foreach (var seed in Seeds)
            {
                var plain = Run(seed, ShopPolicy.None, strategy: AutoPilotStrategy.LuckAware, mysteryDue: true);
                TestContext.WriteLine("seed " + seed + " no stickers: " + plain);
                Assert.AreNotEqual(EndingKind.FourthFloor, plain.Ending, "seed " + seed + ": honest 4th floor still gave the 4th-floor ending");
                Assert.AreEqual(data.DayCount, plain.EndingDay, "seed " + seed + ": " + plain);

                var trace = new List<string>();
                var hidden = Run(seed, ShopPolicy.HideFour, strategy: AutoPilotStrategy.LuckAware, mysteryDue: true, onBus: bus =>
                {
                    bus.Subscribe<GuestSpawned>(e => { if (e.Archetype.Value == "mystery_guest") trace.Add("mystery spawned " + e.Guest); });
                    bus.Subscribe<GuestBoarded>(e => trace.Add("boarded " + e.Guest));
                    bus.Subscribe<GuestDelivered>(e => trace.Add("delivered " + e.Guest + " @" + e.Floor.PhysicalNumber));
                    bus.Subscribe<FloorLabelChanged>(e => { if (e.Floor == StoryRules.RealFourthFloor) trace.Add("floor 4 -> " + e.Current); });
                    bus.Subscribe<DayStarted>(e => trace.Add("day " + e.Day.Number));
                    bus.Subscribe<GuestLeft>(e => trace.Add("left " + e.Guest + " " + e.Reason));
                    bus.Subscribe<DayEnded>(e => trace.Add("day ended"));
                });
                TestContext.WriteLine("seed " + seed + " floor 4 hidden: " + hidden);
                if (hidden.Ending != EndingKind.FourthFloor) TestContext.WriteLine(string.Join(" | ", trace.Skip(trace.LastIndexOf("day " + data.DayCount))));
                Assert.AreEqual(EndingKind.FourthFloor, hidden.Ending, "seed " + seed + ": hidden 4th floor + mystery guest should end there");
                Assert.AreEqual(data.DayCount, hidden.EndingDay, "seed " + seed + ": " + hidden);
            }
        }

        /// <summary>Day 1 = the first real day (the internal day after the Day 0 training shift, which the bot skips).</summary>
        [Test]
        public void LuckBlindBot_SurvivesDay1()
        {
            var r0 = data.Rules;
            var firstReal = new DayId(data.TutorialDayCount + 1);
            foreach (var seed in Seeds)
            {
                var r = Run(seed, ShopPolicy.None, lastDay: firstReal.Number);
                TestContext.WriteLine("seed " + seed + ": " + r);
                Assert.AreEqual(EndingKind.None, r.Ending, "seed " + seed + ": the tutorial ended the game - " + r);
                Assert.AreEqual(1, r.LuckAfterDay.Length, "seed " + seed + ": day 1 did not finish");
                Assert.Greater(r.LuckAfterDay[0], 0, "seed " + seed + ": haunted in the tutorial");
            }

            // Worst case on paper: every tutorial guest suffers every unlucky label once, every reachable unlucky
            // headcount once and an unlucky ride distance (one a straight ride can reach) - or walks out. The hotel must still stand.
            var day1 = data.GetDay(firstReal);
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
                // a ride of n floors is possible when n >= the straight distance and has its parity (detours add 2k floors)
                int dest = sp.HasDestinationFloor ? sp.destinationFloor
                    : data.TryFindDestinationFloor(new DestinationId(sp.destination), out var df) ? df.PhysicalNumber : -1;
                int straight = dest > 0 ? Math.Abs(dest - sp.floor) : 1;
                bool distance = g.unlucky.Any(n => n >= 1 && (dest < 0 || (n >= straight && (n - straight) % 2 == 0)));
                int ride = labels * r0.unluckyFloorLuckPenalty + heads * r0.unluckyOccupancyLuckPenalty
                           + (distance ? r0.unluckyDistanceLuckPenalty : 0);
                worst += g.partySize * Math.Max(ride, r0.patienceOutLuckPenalty);
            }
            Assert.Less(worst, r0.startLuck, "day 1 worst case (" + worst + ") could haunt the hotel");
        }

        [Test]
        public void Tutorial_Day0_IsScripted()
        {
            var day0 = data.GetDay(DayId.First);
            Assert.IsTrue(day0.tutorial, "internal day 1 is the Day 0 training shift");
            Assert.AreEqual(1, data.TutorialDayCount);
            Assert.IsTrue(day0.spawns.All(sp => !string.IsNullOrWhiteSpace(sp.destination) || sp.HasDestinationFloor),
                "tutorial destinations are scripted, not random");
            Assert.IsTrue(day0.spawns.All(sp => data.GetGuest(sp.Guest).partySize == 1), "one person per entry: head-counts stay readable");
        }
    }
}
