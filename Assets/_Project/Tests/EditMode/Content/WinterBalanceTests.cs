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
    /// Winter-season balance on the REAL data (content-designer), with the full-run <see cref="PlaythroughBot"/>
    /// (ExtendSeason whenever offered). Target: careful play (Greedy) usually earns the Christmas miracle, careless play
    /// (Blind) splits between WinterMiracle and SilentNight, and the winter rarely haunts the hotel.
    /// </summary>
    public class WinterBalanceTests
    {
        const int Seeds = 50;

        GameData data;
        readonly Dictionary<Policy, List<PlaythroughBot>> runs = new Dictionary<Policy, List<PlaythroughBot>>();
        readonly Dictionary<Policy, SortedDictionary<string, int>> ledgers = new Dictionary<Policy, SortedDictionary<string, int>>();

        [OneTimeSetUp]
        public void Load()
        {
            data = GameDataResources.Load();
            foreach (var policy in new[] { Policy.Greedy, Policy.Blind })
            {
                runs[policy] = Play(policy, 0, out var ledger);
                ledgers[policy] = ledger;
            }
        }

        /// <summary>
        /// <paramref name="seasonLuckCap"/> &gt; 0: balance what-if (Luck capped at that value when the season is extended, done with
        /// the debug luck command - not a game rule). Tried 60/70/80: it only hurts the careless player, so no such rule.
        /// </summary>
        List<PlaythroughBot> Play(Policy policy, int seasonLuckCap, out SortedDictionary<string, int> ledger)
        {
            {
                var list = new List<PlaythroughBot>();
                var book = ledger = new SortedDictionary<string, int>();
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    var bot = new PlaythroughBot(data, policy, seed) { ExtendSeason = true };
                    var s = bot.S;
                    if (seasonLuckCap > 0)
                        bot.Bus.Subscribe<SeasonExtended>(e => { if (s.Score.Luck > seasonLuckCap) s.DebugAddLuck(seasonLuckCap - s.Score.Luck); });
                    bot.Bus.Subscribe<LuckChanged>(e =>
                    {
                        if (s.Phase != DayPhase.Running || !s.IsExtendedDay) return;
                        Add(book, "day " + s.DisplayDay + " luck " + e.Source + (e.Delta < 0 ? " -" : " +"), e.Delta);
                    });
                    bot.Bus.Subscribe<LuckJudged>(e =>
                    {
                        if (s.Phase != DayPhase.Running || !s.IsExtendedDay || !s.Guests.TryGet(e.Guest, out var g)) return;
                        Add(book, "day " + s.DisplayDay + "   " + e.Verdict + " " + e.Source + " " + g.Archetype.Value, 1);
                    });
                    bot.Bus.Subscribe<GuestLeft>(e =>
                    {
                        if (s.IsExtendedDay && e.Reason != GuestLeaveReason.DayEnded) Add(book, "day " + s.DisplayDay + "   walked off", 1);
                    });
                    list.Add(bot.Play());
                }
                return list;
            }
        }

        static void Add(SortedDictionary<string, int> d, string key, int v)
        {
            d.TryGetValue(key, out int n);
            d[key] = n + v;
        }

        [Test]
        public void WinterLedger_Report([Values(Policy.Greedy, Policy.Blind)] Policy policy)
        {
            var sb = new StringBuilder();
            var list = runs[policy];
            var ext = list.Where(r => r.Extended).ToList();
            sb.AppendLine(Summary(policy.ToString(), list));
            sb.AppendLine(policy + ": extended " + ext.Count + "/" + list.Count + ", WinterMiracle " + ext.Count(r => r.Ending == EndingKind.WinterMiracle) +
                          ", SilentNight " + ext.Count(r => r.Ending == EndingKind.SilentNight) + ", Haunted " + ext.Count(r => r.Ending == EndingKind.Haunted));
            for (int d = data.PlayableDayCount; d < data.SeasonPlayableDayCount + 1; d++)
            {
                var v = ext.Where(r => r.LuckAfterDay[d] >= 0).Select(r => r.LuckAfterDay[d]).ToList();
                if (v.Count > 0) sb.AppendLine("  luck after day " + d + ": avg " + v.Average().ToString("0") + " min " + v.Min() + " max " + v.Max());
            }
            var finals = ext.Where(r => r.Ending != EndingKind.Haunted).Select(r => r.LuckAfterDay[data.SeasonPlayableDayCount]).ToList();
            sb.AppendLine("  final luck >= 60/70/80/85/90: " + string.Join(" / ", new[] { 60, 70, 80, 85, 90 }.Select(x => ext.Count(r => r.LuckAfterDay[data.SeasonPlayableDayCount] >= x) * 100 / Math.Max(1, ext.Count) + "%")));
            foreach (var kv in ledgers[policy]) sb.AppendLine("  " + kv.Key + " " + (kv.Value / (double)Math.Max(1, ext.Count)).ToString("0.00") + "/run");
            TestContext.Out.WriteLine(sb.ToString());
        }

        string Summary(string name, List<PlaythroughBot> list)
        {
            var ext = list.Where(r => r.Extended).ToList();
            int last = data.SeasonPlayableDayCount, before = data.PlayableDayCount;
            var net = ext.Where(r => r.LuckAfterDay[last] > 0).Select(r => r.LuckAfterDay[last] - r.LuckAfterDay[before]).ToList();
            return name + ": extended " + ext.Count + ", WinterMiracle " + ext.Count(r => r.Ending == EndingKind.WinterMiracle) +
                   ", SilentNight " + ext.Count(r => r.Ending == EndingKind.SilentNight) + ", Haunted " + ext.Count(r => r.Ending == EndingKind.Haunted) +
                   ", final luck avg " + (ext.Count > 0 ? ext.Average(r => Math.Max(0, r.LuckAfterDay[last])).ToString("0") : "-") +
                   ", winter net (survivors) " + (net.Count > 0 ? net.Average().ToString("+0;-0") : "-") +
                   ", haunted on day " + string.Join(" ", ext.Where(r => r.Ending == EndingKind.Haunted).Select(r => r.EndingDisplayDay));
        }

        /// <summary>Loose pin of the winter-ending spread, so the season does not drift back to a guaranteed miracle.</summary>
        [Test]
        public void WinterEndings_Spread()
        {
            if (!data.HasExtendedDays) Assert.Ignore("no winter season in the data");
            double Share(Policy p, EndingKind k)
            {
                var ext = runs[p].Where(r => r.Extended).ToList();
                Assert.GreaterOrEqual(ext.Count, Seeds / 3, p + ": too few runs reach the winter");
                return ext.Count(r => r.Ending == k) / (double)ext.Count;
            }
            double greedy = Share(Policy.Greedy, EndingKind.WinterMiracle), blind = Share(Policy.Blind, EndingKind.WinterMiracle);
            TestContext.Out.WriteLine("WinterMiracle: Greedy " + greedy.ToString("P0") + ", Blind " + blind.ToString("P0") +
                                      "; SilentNight Blind " + Share(Policy.Blind, EndingKind.SilentNight).ToString("P0"));
            // careful play enters the winter at the top of the meter (~99 vs ~85), so even with its own higher line
            // (rules.winterMiracleLuck) it nearly always ends in the miracle; the careless player splits
            Assert.GreaterOrEqual(greedy, 0.6, "careful play should usually earn the Christmas miracle");
            Assert.That(blind, Is.InRange(0.25, 0.75), "careless play should split between WinterMiracle and SilentNight");
            Assert.GreaterOrEqual(greedy - blind, 0.2, "playing for luck must pay in the winter too");
            Assert.Greater(Share(Policy.Blind, EndingKind.SilentNight), 0.2, "Silent Night must be a real outcome");
            foreach (var p in new[] { Policy.Greedy, Policy.Blind })
                Assert.Less(Share(p, EndingKind.Haunted), 0.1, p + ": the winter rarely haunts the hotel");
        }
    }
}
