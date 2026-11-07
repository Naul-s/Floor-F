using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// "Day 0" training shift (IDayQuery.IsTutorial): no clock, wave clock spawns, no patience loss / Haunted,
    /// Luck and tips reset + no shop after its summary, SkipTutorial, display numbering, demo/bot never play it.
    /// Fixture: <see cref="SimFixture.WithTutorial"/> (tutorial = internal day 1, real days 2 and 3).
    /// </summary>
    public class TutorialShiftTests
    {
        static SpawnDef Sp(float time, string guest, int floor, int dest)
            => new SpawnDef { time = time, guest = guest, floor = floor, destinationFloor = dest };

        static Sim TutorialRunning(SpawnDef[] spawns, System.Action<SimFixture.Defs> tweak = null)
        {
            var sim = new Sim(SimFixture.WithTutorial(spawns, tweak));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.IsTutorial);
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        // ------------------------------------------------------------------ no clock

        [Test]
        public void Tutorial_NeverTimesOut_RemainingStaysAtDuration()
        {
            var sim = TutorialRunning(new[] { Sp(0f, "plain", 3, 6) });
            float duration = sim.S.DurationSeconds;
            Assert.AreEqual(30f, duration);
            sim.Run(duration * 4f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "no time-out on the training shift");
            Assert.AreEqual(duration, sim.S.RemainingSeconds, "the clock stays full (UI hides it)");
            Assert.Greater(sim.S.ElapsedSeconds, duration);
            Assert.IsEmpty(sim.Events<DayEnded>());
            Assert.AreEqual(1, sim.S.Guests.ActiveGuests.Count);
        }

        // ------------------------------------------------------------------ wave clock

        [Test]
        public void Tutorial_WaveClock_NextWaveWaitsForEmptyHotel_EqualTimesSpawnTogether()
        {
            var sim = TutorialRunning(new[]
            {
                Sp(1f, "granny", 1, 3),
                Sp(3f, "tourist", 1, 7),
                Sp(3f, "tourist", 1, 7),
            });
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count);
            sim.Run(1.05f);
            Assert.AreEqual(1, sim.S.Guests.ActiveGuests.Count, "wave 1 at wave-clock t=1");

            sim.Run(20f);
            Assert.AreEqual(1, sim.S.Guests.ActiveGuests.Count, "wave 2 waits while someone is in the hotel");
            Assert.AreEqual(1f, sim.S.WaveClockSeconds, 0.06f, "the wave clock is frozen while a guest is active");

            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count, "granny delivered");
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "more waves to come: no early end yet");

            sim.Run(1.7f);
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count, "~2 s pause after the hotel empties");
            sim.Run(0.4f);
            Assert.AreEqual(2, sim.S.Guests.ActiveGuests.Count, "both t=3 entries spawn together");
            Assert.AreEqual(3, sim.Events<GuestSpawned>().Count);
        }

        [Test]
        public void Tutorial_EndsEarly_AfterLastWaveIsDelivered()
        {
            var sim = TutorialRunning(new[] { Sp(0f, "granny", 1, 3) }, d => d.Hotel.rules.earlyEndDelaySeconds = 2f);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
            sim.Run(2.1f);
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase, "early end after the last wave");
            Assert.AreEqual(1, sim.S.LastSummary.Delivered);
        }

        // ------------------------------------------------------------------ forgiving

        [Test]
        public void Tutorial_NoPatienceLoss_NoHaunted()
        {
            var sim = TutorialRunning(new[] { Sp(0f, "granny", 1, 6), Sp(0f, "impatient", 9, 8) },
                d => d.Hotel.rules.startLuck = 5);
            Assert.AreEqual(2, sim.S.Guests.ActiveGuests.Count);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            sim.Open(); // granny sees her unlucky 4: 5 - 10 -> 0
            Assert.AreEqual(0, sim.S.Score.Luck);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "Luck 0 does not haunt the training hotel");
            Assert.AreEqual(EndingKind.None, sim.S.Ending);
            Assert.IsEmpty(sim.Events<EndingReached>());

            sim.Close();
            sim.Run(60f); // impatient guest (5 s patience) keeps waiting on 9
            Assert.IsEmpty(sim.Events<GuestLeft>(), "nobody runs out of patience on the training shift");
            var impatient = sim.S.Guests.ActiveGuests.Select(id => { sim.S.Guests.TryGet(id, out var i); return i; })
                .Single(i => i.Archetype.Value == "impatient");
            Assert.AreEqual(1f, impatient.Patience01, 1e-4f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
        }

        // ------------------------------------------------------------------ after the summary

        [Test]
        public void Tutorial_LeavingSummary_ResetsLuckAndTips_NoShop()
        {
            var sim = TutorialRunning(new[] { Sp(0f, "granny", 1, 3) }, d => d.Hotel.rules.earlyEndDelaySeconds = 1f);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open(); // lucky 3: +2 luck, tip x2, delivery +1
            sim.Run(1.1f);
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.AreEqual(53, sim.S.Score.Luck);
            Assert.AreEqual(20, sim.S.Score.Tips);
            Assert.AreEqual(20, sim.S.LastSummary.TipsEarned, "the summary shows what the shift earned");

            sim.Log.Clear();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase, "no shop after the training shift");
            Assert.IsFalse(sim.S.IsOpen);
            Assert.AreEqual(2, sim.S.CurrentDay.Number);
            Assert.AreEqual(1, sim.S.DisplayDay);
            Assert.IsFalse(sim.S.IsTutorial);
            Assert.AreEqual(50, sim.S.Score.Luck, "back to start Luck");
            Assert.AreEqual(0, sim.S.Score.Tips, "back to start tips");
            var luck = sim.Events<LuckChanged>().Single();
            Assert.AreEqual(53, luck.Previous);
            Assert.AreEqual(50, luck.Current);
            var tips = sim.Events<TipsChanged>().Single();
            Assert.AreEqual(20, tips.Previous);
            Assert.AreEqual(0, tips.Current);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Summary, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);

            // the first real day still has its shop
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, sim.S.Phase);
        }

        // ------------------------------------------------------------------ skip

        [Test]
        public void SkipTutorial_FromTutorialIntro_GoesToFirstRealDayIntro()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { Sp(0f, "granny", 1, 3) }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.IsTutorial);
            sim.Log.Clear();

            Assert.IsTrue(sim.S.SkipTutorial());
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(2, sim.S.CurrentDay.Number);
            Assert.AreEqual(1, sim.S.DisplayDay);
            Assert.IsFalse(sim.S.IsTutorial);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Intro, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);
            Assert.IsEmpty(sim.Events<LuckChanged>(), "Luck untouched");
            Assert.IsEmpty(sim.Events<TipsChanged>(), "tips untouched");
            Assert.IsEmpty(sim.Events<DayStarted>());

            Assert.IsFalse(sim.S.SkipTutorial(), "only on a tutorial day");
            Assert.IsTrue(sim.S.BeginDay());
            Assert.AreEqual(2, sim.Events<DayStarted>().Single().Day.Number);
        }

        [Test]
        public void SkipTutorial_RefusedOutsideTutorialIntro()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { Sp(0f, "granny", 1, 3) }));
            Assert.IsFalse(sim.S.SkipTutorial(), "before the game starts");
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.SetPaused(true));
            Assert.IsFalse(sim.S.SkipTutorial(), "paused");
            sim.S.SetPaused(false);
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsFalse(sim.S.SkipTutorial(), "the shift already runs");
            Assert.AreEqual(1, sim.S.CurrentDay.Number);

            var plain = new Sim(SimFixture.Create());
            plain.S.StartNewGame();
            Assert.IsFalse(plain.S.SkipTutorial(), "no tutorial in the data");

            var only = new Sim(SimFixture.WithTutorial(new SpawnDef[0], d => d.Days.days = new[] { d.Days.days[0] }));
            only.S.StartNewGame();
            Assert.IsFalse(only.S.SkipTutorial(), "no real day to skip to");
            Assert.AreEqual(1, only.S.CurrentDay.Number);
        }

        // ------------------------------------------------------------------ numbering

        [Test]
        public void DisplayDay_And_PlayableDayCount()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { Sp(0f, "granny", 1, 3) }));
            sim.S.StartNewGame();
            Assert.AreEqual(3, sim.S.DayCount, "all days");
            Assert.AreEqual(2, sim.S.PlayableDayCount, "tutorial excluded");
            Assert.AreEqual(1, sim.S.CurrentDay.Number);
            Assert.AreEqual(0, sim.S.DisplayDay, "Day 0");

            Assert.IsTrue(sim.S.BeginDay());
            var started = sim.Events<DayStarted>().Single();
            Assert.AreEqual(3, started.DayCount, "DayStarted keeps the full DayCount");
            Assert.IsTrue(started.IsTutorial);
            sim.S.EndDayNow();
            sim.S.ContinueFromSummary();
            Assert.AreEqual(1, sim.S.DisplayDay);

            sim.S.BeginDay();
            sim.S.EndDayNow();
            sim.S.ContinueFromSummary();
            sim.S.FinishShopping();
            Assert.AreEqual(3, sim.S.CurrentDay.Number);
            Assert.AreEqual(2, sim.S.DisplayDay, "last day reads 2/2");
            Assert.IsTrue(sim.S.Data.IsLastDay(sim.S.CurrentDay));
            sim.S.BeginDay();
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(EndingKind.Modest, sim.S.Ending);

            var plain = new Sim(SimFixture.Create());
            plain.S.StartNewGame();
            Assert.AreEqual(1, plain.S.DisplayDay, "no tutorial: day 1 is Day 1");
            Assert.AreEqual(plain.S.DayCount, plain.S.PlayableDayCount);
        }

        // ------------------------------------------------------------------ demo / bot

        [Test]
        public void TitleDemo_NeverPlaysTheTutorialDay()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { Sp(0f, "granny", 1, 3) }, d =>
            {
                d.Days.days[1].durationSeconds = 8f;
                d.Days.days[1].spawns = new[] { Sp(0f, "plain", 3, 6) };
            }));
            sim.S.ReturnToTitle();
            Assert.AreEqual(DayPhase.Title, sim.S.Phase);
            Assert.AreEqual(2, sim.S.CurrentDay.Number, "the demo plays the first real day");
            Assert.IsFalse(sim.S.IsTutorial);
            for (int i = 0; i < 600; i++)
            {
                sim.S.Tick(0.05f);
                Assert.AreEqual(2, sim.S.CurrentDay.Number, "the demo loops on the real day");
            }
            Assert.That(sim.Events<GuestSpawned>().Count, Is.GreaterThan(1), "the demo looped (8 s day, 30 s played)");
            Assert.IsFalse(sim.Events<GuestSpawned>().Any(e => e.Archetype.Value == "granny"), "no tutorial guests in the demo");

            Assert.IsTrue(sim.S.StartGameFromTitle());
            Assert.AreEqual(1, sim.S.CurrentDay.Number, "a new game starts on the training shift");
            Assert.IsTrue(sim.S.IsTutorial);
        }

        [Test]
        public void AutoPilot_DoesNotPlayTheTutorialDay()
        {
            var sim = TutorialRunning(new[] { Sp(0f, "granny", 3, 6) });
            var pilot = new AutoPilot(sim.S.Elevator, sim.S, sim.S.Guests, sim.S.Building);
            for (int i = 0; i < 200; i++) { pilot.Step(); sim.S.Tick(0.05f); }
            Assert.AreEqual(F(1), sim.S.Elevator.CurrentFloor, "the bot leaves the training shift to people");
            Assert.IsEmpty(sim.Events<ElevatorDeparted>());

            var real = Running(d => d.Days.days[0].spawns = new[] { Sp(0f, "granny", 3, 6) });
            var bot = new AutoPilot(real.S.Elevator, real.S, real.S.Guests, real.S.Building);
            for (int i = 0; i < 200; i++) { bot.Step(); real.S.Tick(0.05f); }
            Assert.IsNotEmpty(real.Events<ElevatorDeparted>(), "on a real day the same bot drives");
        }
    }
}
