using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Day 0 coach marks (IGameFlow.SetTutorialHold): a held tutorial day is frozen (Tick advances nothing), elevator
    /// commands stay accepted and the first accepted one releases the hold before it applies; refused off the tutorial's
    /// Running / while paused / on the title; leaving Running, the menu pause, restart and title release it.
    /// </summary>
    public class TutorialHoldTests
    {
        static SpawnDef Sp(float time, string guest, int floor, int dest)
            => new SpawnDef { time = time, guest = guest, floor = floor, destinationFloor = dest };

        /// <summary>Tutorial running with real door/board times so freezing is visible.</summary>
        static Sim TutorialRunning(params SpawnDef[] spawns)
        {
            var sim = new Sim(SimFixture.WithTutorial(spawns, d => { d.Hotel.rules.doorSeconds = 1f; d.Hotel.rules.boardSeconds = 1f; }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.IsTutorial);
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        static void Hold(Sim sim)
        {
            Assert.IsTrue(sim.S.SetTutorialHold(true));
            Assert.IsTrue(sim.S.TutorialHold);
        }

        static void AssertReleasedOnce(Sim sim)
        {
            Assert.IsFalse(sim.S.TutorialHold);
            var e = sim.Events<TutorialHoldChanged>();
            Assert.AreEqual(2, e.Count, "held then released");
            Assert.IsTrue(e[0].Held);
            Assert.IsFalse(e[1].Held);
        }

        // ------------------------------------------------------------------ freeze

        [Test]
        public void Hold_FreezesTick_DoorsElapsedAndWaveClock()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3), Sp(5f, "plain", 1, 6));
            Assert.IsTrue(sim.S.OpenDoors());
            sim.Run(0.5f);
            Assert.AreEqual(DoorState.Opening, sim.S.Elevator.Doors);
            Hold(sim);
            Assert.IsTrue(sim.Events<TutorialHoldChanged>().Single().Held);

            float elapsed = sim.S.ElapsedSeconds, wave = sim.S.WaveClockSeconds;
            int logged = sim.Log.Count;
            sim.Run(30f);
            Assert.AreEqual(DoorState.Opening, sim.S.Elevator.Doors, "doors frozen mid-opening");
            Assert.AreEqual(elapsed, sim.S.ElapsedSeconds, "elapsed frozen");
            Assert.AreEqual(wave, sim.S.WaveClockSeconds, "wave clock frozen");
            Assert.AreEqual(logged, sim.Log.Count, "nothing happens while held");
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
        }

        [Test]
        public void Hold_FreezesMotionAndTransfers()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            sim.Open();
            sim.Close();
            Assert.IsTrue(sim.S.RequestFloor(F(6)));
            sim.Run(0.3f);
            float pos = sim.S.Elevator.Position;
            Hold(sim);
            sim.Run(10f);
            Assert.AreEqual(pos, sim.S.Elevator.Position, "car frozen between floors");
            Assert.AreNotEqual(ElevatorMotion.Idle, sim.S.Elevator.Motion);
            Assert.IsTrue(sim.S.SetTutorialHold(false));
            sim.Run(0.2f);
            Assert.Greater(sim.S.Elevator.Position, pos, "moves again once released");
        }

        [Test]
        public void Hold_FreezesSpawns_OnEmptyHotel()
        {
            var sim = TutorialRunning(Sp(1f, "granny", 1, 3));
            Hold(sim);
            sim.Run(20f);
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count);
            Assert.AreEqual(0f, sim.S.WaveClockSeconds);
            sim.S.SetTutorialHold(false);
            sim.Run(1.05f);
            Assert.AreEqual(1, sim.S.Guests.ActiveGuests.Count);
        }

        // ------------------------------------------------------------------ commands release

        [Test]
        public void Hold_OpenDoors_Accepted_ReleasesThenApplies()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            Assert.IsTrue(sim.S.AcceptsInput, "commands stay accepted while held");
            Assert.IsTrue(sim.S.OpenDoors());
            AssertReleasedOnce(sim);
            Assert.AreEqual(DoorState.Opening, sim.S.Elevator.Doors);
            sim.Run(1.05f);
            Assert.AreEqual(DoorState.Open, sim.S.Elevator.Doors, "the simulation runs again");
        }

        [Test]
        public void Hold_RequestFloor_ReleaseIsPublishedBeforeDeparture()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            Assert.IsTrue(sim.S.RequestFloor(F(5)));
            AssertReleasedOnce(sim);
            int released = sim.IndexOf<TutorialHoldChanged>(e => !e.Held);
            int departed = sim.IndexOf<ElevatorDeparted>();
            Assert.GreaterOrEqual(departed, 0);
            Assert.Less(released, departed, "TutorialHoldChanged(false) then the command's effects");
        }

        [Test]
        public void Hold_CloseDoors_Accepted_Releases()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            sim.Open();
            Hold(sim);
            Assert.IsTrue(sim.S.CloseDoors());
            AssertReleasedOnce(sim);
            sim.Run(1.5f);
            Assert.AreEqual(DoorState.Closed, sim.S.Elevator.Doors);
        }

        [Test]
        public void Hold_RefusedCommand_KeepsHold()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            Assert.IsFalse(sim.S.CloseDoors(), "doors are closed: nothing to close");
            Assert.IsFalse(sim.S.RequestFloor(F(1)), "already here, no pending departure");
            Assert.IsTrue(sim.S.TutorialHold);
            Assert.AreEqual(1, sim.Events<TutorialHoldChanged>().Count);
        }

        // ------------------------------------------------------------------ refusals

        [Test]
        public void Hold_Refused_OnRealDay()
        {
            var sim = Running();
            Assert.IsFalse(sim.S.IsTutorial);
            Assert.IsFalse(sim.S.SetTutorialHold(true));
            Assert.IsFalse(sim.S.TutorialHold);
            Assert.IsTrue(sim.S.SetTutorialHold(false), "already released");
            Assert.IsEmpty(sim.Events<TutorialHoldChanged>());
        }

        [Test]
        public void Hold_Refused_InIntro_WhilePaused_AndOnTitle()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { Sp(0f, "granny", 1, 3) }));
            sim.S.StartNewGame();
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.IsFalse(sim.S.SetTutorialHold(true), "Intro");

            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsTrue(sim.S.SetPaused(true));
            Assert.IsFalse(sim.S.SetTutorialHold(true), "menu-paused");
            Assert.IsTrue(sim.S.SetPaused(false));

            sim.S.ReturnToTitle();
            Assert.AreEqual(DayPhase.Title, sim.S.Phase);
            Assert.IsFalse(sim.S.SetTutorialHold(true), "title");
            Assert.IsFalse(sim.S.TutorialHold);
            Assert.IsEmpty(sim.Events<TutorialHoldChanged>());
        }

        // ------------------------------------------------------------------ release triggers

        [Test]
        public void Hold_ReleasedBy_MenuPause()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            Assert.IsTrue(sim.S.SetPaused(true), "flow commands work as normal while held");
            AssertReleasedOnce(sim);
            Assert.IsTrue(sim.S.SetPaused(false));
            Assert.IsFalse(sim.S.TutorialHold, "unpausing does not bring the hold back");
        }

        [Test]
        public void Hold_ReleasedBy_LeavingRunning()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            sim.S.EndDayNow();
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            AssertReleasedOnce(sim);
            Assert.Less(sim.IndexOf<TutorialHoldChanged>(e => !e.Held), sim.IndexOf<DayPhaseChanged>(e => e.Current == DayPhase.Summary));
        }

        [Test]
        public void Hold_ReleasedBy_Restart()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            sim.S.RestartGame();
            AssertReleasedOnce(sim);
        }

        [Test]
        public void Hold_ReleasedBy_ReturnToTitle()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            sim.S.ReturnToTitle();
            AssertReleasedOnce(sim);
            sim.Run(1f);
            Assert.IsFalse(sim.S.TutorialHold);
        }

        [Test]
        public void Hold_SetFalse_Releases_IdempotentTrue()
        {
            var sim = TutorialRunning(Sp(0f, "granny", 1, 3));
            Hold(sim);
            Assert.IsTrue(sim.S.SetTutorialHold(true), "already held");
            Assert.AreEqual(1, sim.Events<TutorialHoldChanged>().Count, "no duplicate event");
            Assert.IsTrue(sim.S.SetTutorialHold(false));
            AssertReleasedOnce(sim);
        }
    }
}
