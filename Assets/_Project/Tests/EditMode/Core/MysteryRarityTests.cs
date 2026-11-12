using System;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// The hidden 4th-floor ending is rare: ISeasonQuery.MysteryTonight (never on the first run, rules.mysteryChance,
    /// pity after rules.mysteryPityRuns eligible nights), the counters (IRunHistory, RunHistoryChanged) and
    /// RunHistoryPersistence. Fixture: real day 1 schedules the mystery guest at 1 s (and a plain guest at 2 s); day 2 is plain.
    /// </summary>
    public class MysteryRarityTests
    {
        static GameData MysteryData(float chance, int pity, bool onDay2 = false, Action<SimFixture.Defs> tweak = null) => SimFixture.Create(d =>
        {
            d.Hotel.rules.mysteryChance = chance;
            d.Hotel.rules.mysteryPityRuns = pity;
            var spawns = new[]
            {
                new SpawnDef { time = 1f, guest = "mystery", floor = 1, destinationFloor = 4 },
                new SpawnDef { time = 2f, guest = "plain", floor = 1, destinationFloor = 3 },
            };
            d.Days.days[onDay2 ? 1 : 0].spawns = spawns;
            d.Days.days[0].shopStickers = new string[0];
            tweak?.Invoke(d);
        });

        static Sim New(GameData data, int runs, int misses, int seed = 1)
        {
            var sim = new Sim(data, seed);
            sim.S.LoadRunHistory(runs, misses);
            return sim;
        }

        static int MysterySpawns(Sim sim) => sim.Events<GuestSpawned>().Count(e => e.Archetype.Value == "mystery");

        /// <summary>Begins the day and runs past both spawn times.</summary>
        static void BeginAndRun(Sim sim)
        {
            Assert.IsTrue(sim.S.BeginDay());
            sim.Run(3f);
        }

        [Test]
        public void FirstRun_NeverTheMysteryGuest_EvenAtChanceOne()
        {
            var sim = New(MysteryData(1f, 1), 0, 5);
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.MysteryTonight);
            BeginAndRun(sim);
            Assert.AreEqual(0, MysterySpawns(sim), "skipped");
            Assert.AreEqual(1, sim.Events<GuestSpawned>().Count, "the rest of the schedule still comes");
            Assert.AreEqual(5, sim.S.MysteryMisses, "the first run does not count");
            Assert.IsEmpty(sim.Events<RunHistoryChanged>());
        }

        [Test]
        public void LaterRun_ChanceOne_Comes_ResetsMisses()
        {
            var sim = New(MysteryData(1f, 0), 1, 2);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.MysteryTonight);
            BeginAndRun(sim);
            Assert.AreEqual(1, MysterySpawns(sim));
            Assert.AreEqual(0, sim.S.MysteryMisses);
            var e = sim.Events<RunHistoryChanged>().Single();
            Assert.AreEqual(1, e.FinishedRuns);
            Assert.AreEqual(0, e.MysteryMisses);
            sim.AssertOrder(typeof(DayStarted), typeof(RunHistoryChanged), typeof(GuestSpawned));
        }

        [Test]
        public void Pity_GuaranteedAfterMysteryPityRunsMisses()
        {
            var data = MysteryData(0f, 3);
            var sim = New(data, 1, 0);
            sim.S.StartNewGame();
            for (int miss = 1; miss <= 3; miss++)
            {
                Assert.IsFalse(sim.S.MysteryTonight, "chance 0, misses " + (miss - 1));
                BeginAndRun(sim);
                Assert.AreEqual(0, MysterySpawns(sim));
                Assert.AreEqual(miss, sim.S.MysteryMisses);
                Assert.AreEqual(miss, sim.Events<RunHistoryChanged>().Last().MysteryMisses);
                sim.S.RestartGame();
            }
            Assert.IsTrue(sim.S.MysteryTonight, "3 misses: guaranteed");
            BeginAndRun(sim);
            Assert.AreEqual(1, MysterySpawns(sim));
            Assert.AreEqual(0, sim.S.MysteryMisses);
        }

        /// <summary>
        /// Bug (polish pass, found auditing the run history): the pity counter was reset at the mystery night's BeginDay,
        /// so a guaranteed night lost before the guest's spawn time (Haunted early, a quit to the title) used up the pity
        /// although the player never saw it. Now it resets only when the guest really appears.
        /// </summary>
        [Test]
        public void MysteryNight_EndsBeforeItArrives_KeepsThePity()
        {
            var sim = New(MysteryData(0f, 2), 1, 2);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.MysteryTonight, "2 misses: guaranteed");
            Assert.IsTrue(sim.S.BeginDay());
            sim.Run(0.5f); // its spawn time is 1 s
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Haunted));
            Assert.AreEqual(0, MysterySpawns(sim));
            Assert.AreEqual(2, sim.S.MysteryMisses, "never appeared: the pity is kept");
            Assert.IsFalse(sim.Events<RunHistoryChanged>().Any(e => e.MysteryMisses != 2));

            sim.S.ReturnToTitle(); // and a quit to the title in the middle of the night keeps it too
            Assert.IsTrue(sim.S.StartGameFromTitle());
            Assert.IsTrue(sim.S.MysteryTonight, "still guaranteed on the next run");
            Assert.IsTrue(sim.S.BeginDay());
            sim.Run(0.5f);
            sim.S.ReturnToTitle();
            Assert.AreEqual(2, sim.S.MysteryMisses);

            Assert.IsTrue(sim.S.StartGameFromTitle());
            Assert.IsTrue(sim.S.MysteryTonight);
            BeginAndRun(sim);
            Assert.AreEqual(1, MysterySpawns(sim), "this time it comes");
            Assert.AreEqual(0, sim.S.MysteryMisses);
            Assert.AreEqual(0, sim.Events<RunHistoryChanged>().Last().MysteryMisses);
        }

        [Test]
        public void PityZero_ChanceZero_Never()
        {
            var sim = New(MysteryData(0f, 0), 10, 100);
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.MysteryTonight);
        }

        [Test]
        public void Chance_IsTheShareOfEligibleNights([Values(0.3f, 0.5f)] float chance)
        {
            var data = MysteryData(chance, 0);
            int came = 0;
            const int N = 2000;
            for (int seed = 1; seed <= N; seed++)
            {
                var sim = New(data, 1, 0, seed);
                sim.S.StartNewGame();
                if (sim.S.MysteryTonight) came++;
            }
            Assert.AreEqual(chance, came / (double)N, 0.04, came + " of " + N);
        }

        [Test]
        public void DecidedBeforeTheIntroIsShown_OnTheNextDay()
        {
            var sim = New(MysteryData(1f, 0, onDay2: true), 1, 0);
            bool? atIntro = null;
            sim.Bus.Subscribe<DayPhaseChanged>(e => { if (e.Current == DayPhase.Intro && e.Day.Number == 2) atIntro = sim.S.MysteryTonight; });
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.MysteryTonight, "day 1 has no mystery guest");
            BeginAndRun(sim);
            Assert.IsEmpty(sim.Events<RunHistoryChanged>(), "day 1 is not an eligible night");
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            if (sim.S.Phase == DayPhase.Shop) Assert.IsTrue(sim.S.FinishShopping());
            Assert.AreEqual(true, atIntro, "decided when day 2's Intro is entered");
        }

        [Test]
        public void SkipTutorial_AndDebugJump_DecideToo()
        {
            var sim = new Sim(SimFixture.WithTutorial(new SpawnDef[0], d =>
            {
                d.Hotel.rules.mysteryChance = 1f;
                d.Days.days[1].spawns = new[] { new SpawnDef { time = 1f, guest = "mystery", floor = 1, destinationFloor = 4 } };
            }));
            sim.S.LoadRunHistory(1, 0);
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.MysteryTonight, "Day 0");
            Assert.IsTrue(sim.S.SkipTutorial());
            Assert.IsTrue(sim.S.MysteryTonight, "skipped to day 1");
            Assert.IsTrue(sim.S.DebugJumpToDay(2));
            Assert.IsFalse(sim.S.MysteryTonight, "day 2 has none");
            Assert.IsTrue(sim.S.DebugJumpToDay(1));
            Assert.IsTrue(sim.S.MysteryTonight, "debug jump decides");
        }

        [Test]
        public void DebugToggle_InTheIntro_FlipsTonight_SpawnsFollow()
        {
            var sim = New(MysteryData(0f, 0), 0, 0);
            sim.S.StartNewGame();
            Assert.IsFalse(sim.S.MysteryTonight);
            Assert.IsTrue(sim.S.DebugToggleMysteryTonight());
            Assert.IsTrue(sim.S.MysteryTonight);
            BeginAndRun(sim);
            Assert.AreEqual(1, MysterySpawns(sim), "toggled on: it comes (even on a first run)");
            Assert.IsFalse(sim.S.DebugToggleMysteryTonight(), "not while the day runs");

            var plain = new Sim(SimFixture.Create());
            plain.S.StartNewGame();
            Assert.IsFalse(plain.S.DebugToggleMysteryTonight(), "no mystery guest scheduled today");
        }

        [Test]
        public void DebugSpawnMystery_StillWorks_WhenNotTonight()
        {
            var sim = New(MysteryData(0f, 0), 0, 0);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsFalse(sim.S.DebugSpawnMystery().IsNone);
        }

        [Test]
        public void TitleDemo_NeverCountsOrDecides()
        {
            var sim = New(MysteryData(1f, 1, tweak: d => d.Days.days[0].durationSeconds = 5f), 3, 4);
            sim.S.ReturnToTitle();
            for (int i = 0; i < 400; i++) sim.S.Tick(0.1f); // several demo loops
            Assert.IsFalse(sim.S.MysteryTonight);
            Assert.AreEqual(0, MysterySpawns(sim), "the demo skips the mystery guest");
            Assert.AreEqual(3, sim.S.FinishedRuns);
            Assert.AreEqual(4, sim.S.MysteryMisses);
            Assert.IsEmpty(sim.Events<RunHistoryChanged>());
        }

        // ================================================================================== persistence

        [Test]
        public void RunHistoryPersistence_LoadsSilently_SavesEveryChange_SurvivesARelaunch()
        {
            var store = new MemorySettingsStore();
            var data = MysteryData(0f, 3);
            var first = new Sim(data);
            using (var p = new RunHistoryPersistence(first.S, store, first.Bus))
            {
                p.Load();
                Assert.AreEqual(0, first.S.FinishedRuns, "nothing stored: a new player");
                first.S.StartNewGame();
                Assert.IsTrue(first.S.DebugForceEnding(EndingKind.Haunted));
                Assert.AreEqual(1, store.SaveCount, "flushed at once");
                first.S.RestartGame();
                BeginAndRun(first);
                Assert.AreEqual(2, store.SaveCount);
            }
            Assert.IsTrue(store.TryGetInt(ProgressKeys.FinishedRuns, out int runs) && runs == 1);
            Assert.IsTrue(store.TryGetInt(ProgressKeys.MysteryMisses, out int misses) && misses == 1);

            var relaunch = new Sim(data);
            using (var p = new RunHistoryPersistence(relaunch.S, store, relaunch.Bus))
            {
                p.Load();
                Assert.AreEqual(1, relaunch.S.FinishedRuns);
                Assert.AreEqual(1, relaunch.S.MysteryMisses);
                Assert.IsEmpty(relaunch.Log, "no event on load");
            }
        }
    }
}
