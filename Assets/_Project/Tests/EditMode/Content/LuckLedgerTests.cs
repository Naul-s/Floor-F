using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Tests.EditMode.Core;
using NUnit.Framework;
using Policy = FloorF.Tests.EditMode.Core.PlaythroughBot.Policy;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Where Luck is GAINED and LOST, per display day and source, on the REAL data (content-designer). Greedy / Blind / Idle /
    /// Random are the full-run <see cref="PlaythroughBot"/> (season extended when offered); "Middle" is a player who plays for
    /// luck half the time (each decision: LuckAware with p = 0.5, else LuckBlind) with the same greedy shopper.
    /// Report + the base-run ending spread pins (the feel target: a careful player trends up, a careless one drifts down).
    /// </summary>
    public class LuckLedgerTests
    {
        const int Seeds = 50;

        static readonly LuckSource[] Sources =
        {
            LuckSource.FloorLabel, LuckSource.Occupancy, LuckSource.RideDistance, LuckSource.Patience, LuckSource.Sticker,
            LuckSource.Story, LuckSource.Lottery, LuckSource.Delivery,
        };

        GameData data;

        sealed class Run
        {
            public EndingKind BaseEnding = EndingKind.None, FinalEnding = EndingKind.None;
            public int BaseEndingDay = -1, FinalEndingDay = -1;
            public bool Extended;
            /// <summary>[display day][source] = luck gained (>0) / lost (&lt;0) that day (actual, clamped deltas).</summary>
            public readonly Dictionary<int, Dictionary<LuckSource, (int gain, int loss)>> Days = new Dictionary<int, Dictionary<LuckSource, (int, int)>>();
            public readonly Dictionary<int, int> LuckAfter = new Dictionary<int, int>();

            public void Add(int day, LuckSource source, int delta)
            {
                if (!Days.TryGetValue(day, out var d)) Days[day] = d = new Dictionary<LuckSource, (int, int)>();
                d.TryGetValue(source, out var v);
                d[source] = delta > 0 ? (v.gain + delta, v.loss) : (v.gain, v.loss + delta);
            }
        }

        readonly Dictionary<string, List<Run>> runs = new Dictionary<string, List<Run>>();

        [OneTimeSetUp]
        public void Load()
        {
            data = GameDataResources.Load();
            foreach (var p in new[] { Policy.Greedy, Policy.Blind, Policy.Idle, Policy.Random })
                runs[p.ToString()] = Enumerable.Range(1, Seeds).Select(seed => PlayBot(p, seed)).ToList();
            runs["Middle"] = Enumerable.Range(1, Seeds).Select(PlayMiddle).ToList();
        }

        static void Track(GameSession s, EventBus bus, GameData data, Run run)
        {
            bus.Subscribe<LuckChanged>(e =>
            {
                if (s.Phase != DayPhase.Running || s.IsTutorial) return;
                run.Add(s.DisplayDay, e.Source, e.Delta);
            });
            bus.Subscribe<DayEnded>(e => { if (!data.GetDay(e.Day).tutorial) run.LuckAfter[data.DisplayDayNumber(e.Day)] = e.Summary.LuckAtEnd; });
            bus.Subscribe<EndingReached>(e =>
            {
                int day = data.DisplayDayNumber(e.Day);
                if (run.BaseEnding == EndingKind.None) { run.BaseEnding = e.Ending; run.BaseEndingDay = day; }
                run.FinalEnding = e.Ending;
                run.FinalEndingDay = day;
            });
        }

        Run PlayBot(Policy policy, int seed)
        {
            var bot = new PlaythroughBot(data, policy, seed) { ExtendSeason = true };
            var run = new Run();
            Track(bot.S, bot.Bus, data, run);
            bot.Play();
            Assert.IsEmpty(bot.Violations, string.Join("\n", bot.Violations.Take(5)));
            run.Extended = bot.Extended;
            return run;
        }

        /// <summary>Half-careful player: LuckAware or LuckBlind per decision (p = 0.5), greedy shopper, Day 0 skipped.</summary>
        Run PlayMiddle(int seed)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new Random(seed));
            var run = new Run();
            Track(s, bus, data, run);
            var rng = new Random(seed * 7919 + 101);
            var shopper = new ShopBot(data);
            AutoPilot aware = null, blind = null;
            float wait = 0f;
            const float dt = 0.1f;
            s.StartNewGame();
            for (int guard = 0; guard < 400000; guard++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Ending:
                        if (!s.CanExtendSeason) return run;
                        run.Extended = true;
                        Assert.IsTrue(s.ExtendSeason());
                        break;
                    case DayPhase.Intro:
                        if (s.IsTutorial) { Assert.IsTrue(s.SkipTutorial()); break; }
                        aware = new AutoPilot(s.Elevator, s, s.Guests, s.Building, AutoPilotStrategy.LuckAware) { Data = data };
                        blind = new AutoPilot(s.Elevator, s, s.Guests, s.Building, AutoPilotStrategy.LuckBlind) { Data = data };
                        Assert.IsTrue(s.BeginDay());
                        break;
                    case DayPhase.Summary: Assert.IsTrue(s.ContinueFromSummary()); break;
                    case DayPhase.Shop: shopper.Shop(s); Assert.IsTrue(s.FinishShopping()); break;
                    case DayPhase.Running:
                        if (wait <= 0f) { (rng.NextDouble() < 0.5 ? aware : blind).Step(); wait = (float)rng.NextDouble() * 0.4f; }
                        wait -= dt;
                        s.Tick(dt);
                        break;
                }
            }
            Assert.Fail("middle seed " + seed + ": no ending");
            return run;
        }

        static string Pct(int k, int n) => (100 * k / Math.Max(1, n)) + "%";

        string Spread(string name)
        {
            var list = runs[name];
            int n = list.Count;
            int B(EndingKind k) => list.Count(r => r.BaseEnding == k);
            var ext = list.Where(r => r.Extended).ToList();
            int W(EndingKind k) => ext.Count(r => r.FinalEnding == k);
            var haunted = list.Where(r => r.BaseEnding == EndingKind.Haunted).Select(r => r.BaseEndingDay).OrderBy(d => d).ToList();
            return name + ": base Prosperous " + Pct(B(EndingKind.Prosperous), n) + " / Modest " + Pct(B(EndingKind.Modest), n) +
                   " / Haunted " + Pct(B(EndingKind.Haunted), n) + (haunted.Count > 0 ? " (day " + string.Join(",", haunted) + ")" : "") +
                   " | winter (" + ext.Count + "): Miracle " + Pct(W(EndingKind.WinterMiracle), ext.Count) + " / SilentNight " +
                   Pct(W(EndingKind.SilentNight), ext.Count) + " / Haunted " + Pct(W(EndingKind.Haunted), ext.Count) +
                   " | day-" + data.PlayableDayCount + " luck >= 70/75/80/85/90: " + string.Join(" ", new[] { 70, 75, 80, 85, 90 }.Select(x =>
                       Pct(list.Count(r => r.LuckAfter.TryGetValue(data.PlayableDayCount, out int l) && l >= x), n))) +
                   " | final >= 80/85/90: " + string.Join(" ", new[] { 80, 85, 90 }.Select(x =>
                       Pct(ext.Count(r => r.LuckAfter.TryGetValue(data.SeasonPlayableDayCount, out int l) && l >= x), ext.Count)));
        }

        [Test]
        public void LuckLedger_GainedVsLost_Report([Values("Greedy", "Middle", "Blind")] string name)
        {
            var list = runs[name];
            var sb = new StringBuilder();
            sb.AppendLine(Spread(name));
            sb.AppendLine("per day, avg per run that played it: +gained / -lost by source (lbl head dist pat honest wish lotto deliv) | net | luck after");
            int totalGain = 0, totalLoss = 0;
            for (int d = 1; d <= data.SeasonPlayableDayCount; d++)
            {
                var played = list.Where(r => r.LuckAfter.ContainsKey(d)).ToList();
                if (played.Count == 0) continue;
                double G(LuckSource src) => played.Average(r => r.Days.TryGetValue(d, out var x) && x.TryGetValue(src, out var v) ? v.gain : 0);
                double L(LuckSource src) => played.Average(r => r.Days.TryGetValue(d, out var x) && x.TryGetValue(src, out var v) ? v.loss : 0);
                double gain = Sources.Sum(G), loss = Sources.Sum(L);
                if (d <= data.PlayableDayCount) { totalGain += (int)Math.Round(gain); totalLoss += (int)Math.Round(loss); }
                sb.AppendLine("  d" + d + " (n" + played.Count + "): +" + string.Join(" ", Sources.Select(x => G(x).ToString("0.#"))) +
                              "  /  " + string.Join(" ", Sources.Select(x => L(x).ToString("0.#"))) +
                              "  | +" + gain.ToString("0") + " " + loss.ToString("0") + " = " + (gain + loss).ToString("+0;-0") +
                              " | luck " + played.Average(r => r.LuckAfter[d]).ToString("0"));
            }
            sb.AppendLine("  base run days 1-" + data.PlayableDayCount + ": gained " + totalGain + ", lost " + totalLoss);
            TestContext.Out.WriteLine(sb.ToString());
        }

        [Test]
        public void IdleAndRandom_HauntedEarly_Report()
        {
            foreach (var name in new[] { "Idle", "Random" })
            {
                TestContext.Out.WriteLine(Spread(name));
                var days = runs[name].Where(r => r.BaseEnding == EndingKind.Haunted).Select(r => r.BaseEndingDay).ToList();
                if (days.Count > 0) TestContext.Out.WriteLine("  haunted by day: avg " + days.Average().ToString("0.0") + ", latest " + days.Max());
            }
        }

        /// <summary>
        /// Feel targets for the base run (loose): careful play nearly always prospers, careless play splits Prosperous/Modest
        /// and is sometimes haunted, doing nothing haunts the hotel within the first few days.
        /// </summary>
        [Test]
        public void BaseRun_EndingSpread()
        {
            double Share(string name, EndingKind k) => runs[name].Count(r => r.BaseEnding == k) / (double)runs[name].Count;
            TestContext.Out.WriteLine(string.Join("\n", new[] { "Greedy", "Middle", "Blind", "Idle", "Random" }.Select(Spread)));
            Assert.GreaterOrEqual(Share("Greedy", EndingKind.Prosperous), 0.85, "careful play should nearly always prosper");
            Assert.That(Share("Blind", EndingKind.Prosperous), Is.InRange(0.3, 0.75), "careless play: Prosperous sometimes");
            Assert.That(Share("Blind", EndingKind.Modest), Is.InRange(0.2, 0.6), "careless play: Modest often");
            Assert.LessOrEqual(Share("Blind", EndingKind.Haunted), 0.15, "careless play is rarely haunted");
            Assert.GreaterOrEqual(Share("Middle", EndingKind.Prosperous), Share("Blind", EndingKind.Prosperous), "half-careful beats careless");
            Assert.AreEqual(1.0, Share("Idle", EndingKind.Haunted), "doing nothing haunts the hotel");
            Assert.LessOrEqual(runs["Idle"].Max(r => r.BaseEndingDay), 3, "idle: haunted by day 3");
        }
    }
}
