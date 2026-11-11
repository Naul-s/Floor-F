using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;
using Policy = FloorF.Tests.EditMode.Core.PlaythroughBot.Policy;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Automated full playthroughs on the REAL Resources/Data/*.json: Day 0 -> Day 7 -> ending, many fixed seeds per bot
    /// policy (<see cref="PlaythroughBot"/>), every run checking the invariants (no exception, no softlock, luck 0..max,
    /// tips &gt;= 0, one-day stickers/charms, reroll counts/prices, capacity, nobody left behind, the tutorial ends and resets).
    /// Plus mid-run RestartGame / ReturnToTitle / debug day jumps / forced endings, each followed by a full replay.
    /// Each policy test prints its balance row (TestContext output in results.xml).
    /// </summary>
    public class PlaythroughBotTests
    {
        /// <summary>Seeds per policy in the suite (runtime bound: the five policy tests together stay well under ~20 s).</summary>
        const int SeedsPerPolicy = 50;

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // =========================================================================================== full runs

        [Test]
        public void Playthrough_InvariantsHold_AndBalanceReport([Values] Policy policy)
        {
            var watch = Stopwatch.StartNew();
            var runs = new List<PlaythroughBot>();
            for (int seed = 1; seed <= SeedsPerPolicy; seed++)
                runs.Add(new PlaythroughBot(data, policy, seed).Play());
            watch.Stop();

            TestContext.Out.WriteLine(Report(policy, runs, watch.ElapsedMilliseconds));
            var violations = runs.SelectMany(r => r.Violations).ToList();
            Assert.IsEmpty(violations, string.Join("\n", violations.Take(20)));

            var endings = runs.GroupBy(r => r.Ending).ToDictionary(g => g.Key, g => g.Count());
            int Count(EndingKind k) => endings.TryGetValue(k, out int n) ? n : 0;
            switch (policy)
            {
                case Policy.Idle:
                    Assert.AreEqual(runs.Count, Count(EndingKind.Haunted), "doing nothing must haunt the hotel");
                    break;
                case Policy.Random:
                    Assert.AreEqual(0, Count(EndingKind.Prosperous), "random clicking must not prosper");
                    break;
                case Policy.Greedy:
                    Assert.Greater(Count(EndingKind.Prosperous), runs.Count / 2, "playing for luck must usually prosper");
                    Assert.IsTrue(runs.All(r => r.TutorialEnded), "a competent bot finishes Day 0");
                    break;
                case Policy.FourthFloor:
                    Assert.Greater(Count(EndingKind.FourthFloor), runs.Count / 2, "the hidden 4th-floor ending is reachable");
                    break;
            }
        }

        /// <summary>The same bots at real frame rates: invariants hold and the ending mix does not hinge on the frame.</summary>
        [Test]
        public void Playthrough_AtRealFrameRates_Report([Values(Policy.Greedy, Policy.Blind)] Policy policy)
        {
            foreach (var fps in new[] { 10, 30, 60 })
            {
                var watch = Stopwatch.StartNew();
                var runs = new List<PlaythroughBot>();
                for (int seed = 1; seed <= 30; seed++)
                    runs.Add(new PlaythroughBot(data, policy, seed) { FrameSeconds = 1f / fps }.Play());
                TestContext.Out.WriteLine(fps + " fps " + Report(policy, runs, watch.ElapsedMilliseconds));
                var violations = runs.SelectMany(r => r.Violations).ToList();
                Assert.IsEmpty(violations, fps + " fps\n" + string.Join("\n", violations.Take(20)));
            }
        }

        static string Report(Policy policy, List<PlaythroughBot> runs, long ms)
        {
            var sb = new StringBuilder();
            int n = runs.Count;
            string Pct(int k) => k + " (" + (100 * k / Math.Max(1, n)) + "%)";
            var e = runs.GroupBy(r => r.Ending).ToDictionary(g => g.Key, g => g.Count());
            int E(EndingKind k) => e.TryGetValue(k, out int c) ? c : 0;
            sb.AppendLine("=== " + policy + ": " + n + " seeds, " + ms + " ms");
            sb.AppendLine("endings: Prosperous " + Pct(E(EndingKind.Prosperous)) + " | Modest " + Pct(E(EndingKind.Modest)) +
                          " | Haunted " + Pct(E(EndingKind.Haunted)) + " | 4th floor " + Pct(E(EndingKind.FourthFloor)));
            var haunted = runs.Where(r => r.Ending == EndingKind.Haunted).Select(r => r.EndingDisplayDay).ToList();
            if (haunted.Count > 0)
                sb.AppendLine("haunted on day: " + string.Join(" ", haunted.GroupBy(d => d).OrderBy(g => g.Key).Select(g => "d" + g.Key + "x" + g.Count())));
            int days = runs[0].LuckAfterDay.Length;
            sb.Append("avg luck after day:");
            for (int d = 1; d < days; d++)
            {
                var v = runs.Where(r => r.LuckAfterDay[d] >= 0).Select(r => r.LuckAfterDay[d]).ToList();
                sb.Append(" d" + d + "=" + (v.Count > 0 ? v.Average().ToString("0") + (v.Count < n ? "(n" + v.Count + ")" : "") : "-"));
            }
            sb.AppendLine();
            sb.Append("avg tips after day:");
            for (int d = 1; d < days; d++)
            {
                var v = runs.Where(r => r.TipsAfterDay[d] >= 0).Select(r => r.TipsAfterDay[d]).ToList();
                sb.Append(" d" + d + "=" + (v.Count > 0 ? v.Average().ToString("0") : "-"));
            }
            sb.AppendLine();
            sb.AppendLine("avg tips earned/run " + runs.Average(r => r.TipsEarnedTotal).ToString("0") + ", final tips " + runs.Average(r => r.FinalTips).ToString("0") +
                          ", purchases/run " + runs.Average(r => r.Purchases).ToString("0.0") + ", rerolls/run " + runs.Average(r => r.Rerolls).ToString("0.0") +
                          ", delivered/run " + runs.Average(r => r.Delivered).ToString("0.0") + ", walked off/run " + runs.Average(r => r.LeftCount).ToString("0.0"));
            var grades = runs.SelectMany(r => r.Grades).GroupBy(g => g).ToDictionary(g => g.Key, g => g.Count());
            int total = Math.Max(1, grades.Values.Sum());
            sb.AppendLine("grades: " + string.Join(" ", new[] { "S", "A", "B", "C" }.Select(g => g + "=" + (grades.TryGetValue(g, out int c) ? c : 0) * 100 / total + "%")));
            sb.AppendLine("Day 0: played " + runs.Count(r => r.TutorialPlayed) + ", ended " + runs.Count(r => r.TutorialEnded) +
                          (runs.Any(r => r.TutorialEnded) ? ", avg " + runs.Where(r => r.TutorialEnded).Average(r => r.TutorialSeconds).ToString("0") + " s" : "") +
                          (policy == Policy.Random ? ", random clicks alone finished it " + runs.Count(r => r.RandomSoloFinishedTutorial) : ""));
            if (policy == Policy.FourthFloor) sb.AppendLine("4 -> F on the real 4th floor before the last day: " + runs.Count(r => r.FourthStickerBought));
            return sb.ToString();
        }

        // =========================================================================================== fresh state

        /// <summary>Everything a new run starts from (as after StartNewGame on a brand-new session).</summary>
        static void AssertFresh(GameSession s, string ctx)
        {
            var r = s.Data.Rules;
            Assert.AreEqual(DayPhase.Intro, s.Phase, ctx + ": phase");
            Assert.AreEqual(DayId.First, s.CurrentDay, ctx + ": day");
            Assert.AreEqual(EndingKind.None, s.Ending, ctx + ": ending");
            Assert.IsFalse(s.IsPaused || s.TutorialHold || s.IsOnTitle, ctx + ": paused / hold / title");
            Assert.AreEqual(r.startLuck, s.Score.Luck, ctx + ": luck");
            Assert.AreEqual(r.startTips, s.Score.Tips, ctx + ": tips");
            Assert.AreEqual(0, s.Score.TipsEarnedToday, ctx + ": tips today");
            Assert.AreEqual(0, s.Score.LuckStreak, ctx + ": streak");
            Assert.AreEqual(0, s.Score.BestStreakToday, ctx + ": best streak");
            Assert.AreEqual(1f, s.Score.StreakTipMultiplier, ctx + ": streak multiplier");
            for (int f = 0; f < s.Building.FloorCount; f++)
            {
                var floor = new FloorIndex(f);
                Assert.IsFalse(s.Building.IsRelabelled(floor), ctx + ": floor " + (f + 1) + " still stickered");
                Assert.AreEqual(0, s.Guests.WaitingCount(floor), ctx + ": guests waiting on " + (f + 1));
            }
            Assert.IsEmpty(s.ActiveCharms, ctx + ": charms");
            Assert.AreEqual(0, s.ShieldLeft, ctx + ": shield uses");
            Assert.IsFalse(s.IsOpen, ctx + ": shop open");
            Assert.IsEmpty(s.Offers, ctx + ": offers");
            Assert.AreEqual(0, s.RerollsLeft, ctx + ": rerolls");
            Assert.IsEmpty(s.Guests.ActiveGuests, ctx + ": guests");
            var e = s.Elevator;
            Assert.AreEqual(0, e.Occupancy, ctx + ": riders");
            Assert.AreEqual(FloorIndex.Ground, e.CurrentFloor, ctx + ": car floor");
            Assert.AreEqual(0f, e.Position, ctx + ": car position");
            Assert.AreEqual(ElevatorMotion.Idle, e.Motion, ctx + ": motion");
            Assert.AreEqual(DoorState.Closed, e.Doors, ctx + ": doors");
            Assert.IsFalse(e.HasTarget, ctx + ": target");
            Assert.AreEqual(0, s.GetWishStarsInSky(new List<int>()), ctx + ": stars in the sky");
            Assert.IsEmpty(s.PendingWishStarTimes.ToList(), ctx + ": stars to come");
            Assert.AreEqual(0, s.WishesMadeToday, ctx + ": wishes made");
            Assert.AreEqual(0f, s.ElapsedSeconds, ctx + ": clock");
            Assert.AreEqual(default(DaySummary), s.LastSummary, ctx + ": last summary");
        }

        // =========================================================================================== restart / title mid-run

        public enum Stop { Intro, Running, RunningPaused, Summary, ShopAfterBuying, IntroAfterShop }

        /// <summary>Drives <paramref name="bot"/> to display day &gt;= <paramref name="day"/> in the <paramref name="stop"/> situation (or an ending).</summary>
        static void PlayUntil(PlaythroughBot bot, int day, Stop stop, float atSeconds)
        {
            var s = bot.S;
            bool shopped = false;
            bot.Interrupt = b =>
            {
                if (s.Phase == DayPhase.Ending) return true;
                if (s.DisplayDay < day) return false;
                switch (stop)
                {
                    case Stop.Intro: return s.Phase == DayPhase.Intro;
                    case Stop.Running:
                    case Stop.RunningPaused:
                        return s.Phase == DayPhase.Running && (s.ElapsedSeconds >= atSeconds || s.IsTutorial && s.ElapsedSeconds >= 3f);
                    case Stop.Summary: return s.Phase == DayPhase.Summary;
                    case Stop.ShopAfterBuying:
                    case Stop.IntroAfterShop:
                        if (s.Phase != DayPhase.Shop) return stop == Stop.IntroAfterShop && shopped && s.Phase == DayPhase.Intro;
                        if (shopped) return stop == Stop.ShopAfterBuying;
                        // make sure something is bought: stickers + a charm pending for tomorrow
                        s.DebugAddTips(2000);
                        new Content.ShopBot(s.Data).Shop(s);
                        shopped = true;
                        return stop == Stop.ShopAfterBuying;
                }
                return false;
            };
            bot.Play();
            bot.Interrupt = null;
            if (stop == Stop.RunningPaused && s.Phase == DayPhase.Running) Assert.IsTrue(s.SetPaused(true));
        }

        [Test]
        public void RestartGame_AnywhereMidRun_ResetsEverything_ThenReplaysToAnEnding([Values] Stop stop, [Values(false, true)] bool viaTitle)
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = new Random(seed * 31 + (int)stop);
                int day = r.Next(0, data.PlayableDayCount + 1);
                if (stop == Stop.ShopAfterBuying || stop == Stop.IntroAfterShop) day = r.Next(1, data.PlayableDayCount); // days with a shop after them
                if (day == data.PlayableDayCount && r.Next(2) == 0) day = data.PlayableDayCount; // star night (wish stars) often
                float at = 10f + (float)r.NextDouble() * 60f;
                var bot = new PlaythroughBot(data, seed % 2 == 0 ? Policy.Greedy : Policy.FourthFloor, seed);
                PlayUntil(bot, day, stop, at);
                string ctx = "seed " + seed + " " + stop + " day " + bot.S.DisplayDay + " " + bot.S.Phase + (viaTitle ? " via title" : "");
                Assert.IsEmpty(bot.Violations, ctx + "\n" + string.Join("\n", bot.Violations));

                if (viaTitle)
                {
                    bot.S.ReturnToTitle();
                    Assert.AreEqual(DayPhase.Title, bot.S.Phase, ctx);
                    for (int i = 0; i < 300; i++) bot.S.Tick(PlaythroughBot.Dt); // the attract demo plays a while
                    Assert.IsTrue(bot.S.StartGameFromTitle(), ctx);
                }
                else bot.S.RestartGame();
                AssertFresh(bot.S, ctx);

                bot.Play(startNewGame: false);
                Assert.IsEmpty(bot.Violations, ctx + " replay\n" + string.Join("\n", bot.Violations));
                Assert.AreEqual(DayPhase.Ending, bot.S.Phase, ctx + " replay reaches an ending");
            }
        }

        // =========================================================================================== debug cheats

        public enum From { Title, Intro, Running, RunningPaused, Summary, Shop, Ending }

        GameSession SessionIn(From from, int seed, out PlaythroughBot bot)
        {
            bot = new PlaythroughBot(data, Policy.Blind, seed);
            var s = bot.S;
            int day = 1 + seed % (data.PlayableDayCount - 1);
            switch (from)
            {
                case From.Title:
                    s.ReturnToTitle();
                    for (int i = 0; i < 100; i++) s.Tick(PlaythroughBot.Dt);
                    break;
                case From.Intro: PlayUntil(bot, day, Stop.Intro, 0f); break;
                case From.Running: PlayUntil(bot, day, Stop.Running, 20f); break;
                case From.RunningPaused: PlayUntil(bot, day, Stop.RunningPaused, 20f); break;
                case From.Summary: PlayUntil(bot, day, Stop.Summary, 0f); break;
                case From.Shop: PlayUntil(bot, day, Stop.ShopAfterBuying, 0f); break;
                case From.Ending:
                    PlayUntil(bot, day, Stop.Running, 20f);
                    Assert.IsTrue(s.DebugForceEnding(EndingKind.Haunted));
                    break;
            }
            Assert.IsEmpty(bot.Violations, string.Join("\n", bot.Violations));
            return s;
        }

        [Test]
        public void DebugJumpToDay_FromEveryPhase_ThenPlaysToAnEnding([Values] From from)
        {
            for (int n = 0; n <= data.PlayableDayCount; n++)
            {
                var s = SessionIn(from, n + 1, out var bot);
                string ctx = from + " -> day " + n;
                int luck = s.Score.Luck, tips = s.Score.Tips;
                Assert.IsTrue(s.DebugJumpToDay(n), ctx);
                Assert.AreEqual(DayPhase.Intro, s.Phase, ctx);
                Assert.AreEqual(n, s.DisplayDay, ctx);
                Assert.AreEqual(EndingKind.None, s.Ending, ctx);
                Assert.IsFalse(s.IsPaused || s.IsOnTitle || s.IsOpen, ctx);
                Assert.IsEmpty(s.ActiveCharms, ctx);
                Assert.IsEmpty(s.Guests.ActiveGuests, ctx);
                for (int f = 0; f < s.Building.FloorCount; f++) Assert.IsFalse(s.Building.IsRelabelled(new FloorIndex(f)), ctx + " floor " + (f + 1));
                if (from == From.Title) Assert.AreEqual(data.Rules.startLuck, s.Score.Luck, ctx + ": from the title a fresh run");
                else { Assert.AreEqual(luck, s.Score.Luck, ctx + ": luck kept"); Assert.AreEqual(tips, s.Score.Tips, ctx + ": tips kept"); }

                bot.Play(startNewGame: false);
                Assert.IsEmpty(bot.Violations, ctx + "\n" + string.Join("\n", bot.Violations));
                Assert.AreEqual(DayPhase.Ending, s.Phase, ctx + ": reaches an ending");

                s.RestartGame(); // play again
                AssertFresh(s, ctx + " then restart");
            }
        }

        [Test]
        public void DebugForceEnding_FromTheShop_ThenPlayAgain([Values(EndingKind.Prosperous, EndingKind.Modest, EndingKind.Haunted, EndingKind.FourthFloor)] EndingKind kind,
            [Values(false, true)] bool paused)
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var s = SessionIn(From.Shop, seed, out var bot);
                string ctx = kind + " seed " + seed + " day " + s.DisplayDay;
                if (s.Phase != DayPhase.Shop) continue; // the blind bot was haunted before any shop
                Assert.IsTrue(s.ActiveCharms.Count > 0 || Enumerable.Range(0, s.Building.FloorCount).Any(f => s.Building.IsRelabelled(new FloorIndex(f))), ctx + ": bought something");
                if (paused) Assert.IsTrue(s.SetPaused(true));
                int endings = 0;
                using (bot.Bus.Subscribe<EndingReached>(e => { endings++; Assert.AreEqual(kind, e.Ending); }))
                    Assert.IsTrue(s.DebugForceEnding(kind), ctx);
                Assert.AreEqual(1, endings, ctx);
                Assert.AreEqual(DayPhase.Ending, s.Phase, ctx);
                Assert.AreEqual(kind, s.Ending, ctx);
                Assert.IsFalse(s.IsOpen || s.IsPaused, ctx);
                Assert.IsEmpty(s.Offers, ctx);
                Assert.IsEmpty(s.ActiveCharms, ctx + ": charms");
                for (int f = 0; f < s.Building.FloorCount; f++) Assert.IsFalse(s.Building.IsRelabelled(new FloorIndex(f)), ctx + " floor " + (f + 1));
                if (kind == EndingKind.Haunted) Assert.AreEqual(0, s.Score.Luck, ctx);
                if (kind == EndingKind.Prosperous) Assert.GreaterOrEqual(s.Score.Luck, data.Rules.prosperousEndingLuck, ctx);
                if (kind == EndingKind.Modest) Assert.That(s.Score.Luck, Is.InRange(1, data.Rules.prosperousEndingLuck - 1), ctx);
                Assert.IsFalse(s.BeginDay() || s.ContinueFromSummary() || s.FinishShopping(), ctx + ": the ending is terminal");

                s.RestartGame();
                AssertFresh(s, ctx + " then restart");
                bot.Play(startNewGame: false);
                Assert.IsEmpty(bot.Violations, ctx + " replay\n" + string.Join("\n", bot.Violations));
                Assert.AreEqual(DayPhase.Ending, s.Phase, ctx + " replay");
            }
        }
    }
}
