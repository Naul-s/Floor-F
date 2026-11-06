using System.Linq;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>Phase 3a: title + attract demo, pause, settings persistence (GameSession as IMenuFlow).</summary>
    public class MenuFlowTests
    {
        static readonly FloorIndex Ground = FloorIndex.FromPhysicalNumber(1);

        /// <summary>Day 1 with two scheduled guests, so the demo bot has work to do and the day ends early.</summary>
        static void WithSpawns(SimFixture.Defs d)
        {
            d.Days.days[0].spawns = new[]
            {
                new SpawnDef { time = 0f, guest = "plain", floor = 3, destinationFloor = 6 },
                new SpawnDef { time = 1f, guest = "tourist", floor = 1, destinationFloor = 5 },
            };
        }

        static Sim TitleSim()
        {
            var sim = new Sim(SimFixture.Create(WithSpawns));
            sim.S.ReturnToTitle();
            return sim;
        }

        static void Tick(Sim sim, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.05f) sim.S.Tick(0.05f);
        }

        [Test]
        public void Boot_StartsOnTitle_WithDemoRunning()
        {
            var sim = TitleSim();
            var s = sim.S;
            Assert.IsTrue(s.HasStarted);
            Assert.IsTrue(s.IsOnTitle);
            Assert.AreEqual(DayPhase.Title, s.Phase);
            Assert.IsFalse(s.AcceptsInput, "player input is off during the demo");
            var phases = sim.Events<DayPhaseChanged>();
            Assert.AreEqual(1, phases.Count, "only DayPhaseChanged(-> Title)");
            Assert.AreEqual(DayPhase.None, phases[0].Previous);
            Assert.AreEqual(DayPhase.Title, phases[0].Current);
            Assert.AreEqual(1, s.Guests.ActiveGuests.Count, "the demo day really started (t=0 spawn)");

            Tick(sim, 60f);
            Assert.That(sim.Events<GuestBoarded>().Count, Is.GreaterThan(0), "demo bot boards guests");
            Assert.That(sim.Events<GuestDelivered>().Count, Is.GreaterThan(2), "demo bot delivers, and the demo loops after the day ends");
            Assert.That(sim.Events<DoorsOpened>().Count, Is.GreaterThan(0));
            Assert.AreEqual(DayPhase.Title, s.Phase, "still on the title after the demo day looped");
            Assert.IsEmpty(sim.Events<GameStarted>(), "no flow events from the demo");
            Assert.IsEmpty(sim.Events<DayStarted>());
            Assert.IsEmpty(sim.Events<DayEnded>());
            Assert.IsEmpty(sim.Events<EndingReached>());
            Assert.AreEqual(1, sim.Events<DayPhaseChanged>().Count);
            Assert.AreEqual(AutoPilotStrategy.LuckAware, s.DemoPilot.Strategy);
        }

        [Test]
        public void Title_RefusesPlayerCommands()
        {
            var s = TitleSim().S;
            Assert.IsFalse(s.RequestFloor(FloorIndex.FromPhysicalNumber(5)));
            Assert.IsFalse(s.OpenDoors());
            Assert.IsFalse(s.CloseDoors());
            Assert.IsFalse(s.BeginDay());
            Assert.IsFalse(s.ContinueFromSummary());
            Assert.IsFalse(s.FinishShopping());
            Assert.AreEqual(DayPhase.Title, s.Phase);
        }

        [Test]
        public void StartGameFromTitle_ResetsToDay1Intro()
        {
            var sim = TitleSim();
            var s = sim.S;
            Tick(sim, 7.3f);
            Assert.That(s.Score.Luck != 50 || s.Score.Tips != 0 || s.Guests.ActiveGuests.Count > 0, "demo changed something");
            sim.Log.Clear();

            Assert.IsTrue(s.StartGameFromTitle());
            Assert.IsFalse(s.IsOnTitle);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(1, s.CurrentDay.Number);
            Assert.AreEqual(50, s.Score.Luck, "start Luck");
            Assert.AreEqual(0, s.Score.Tips, "start tips");
            Assert.AreEqual(0, s.Guests.ActiveGuests.Count, "demo guests gone");
            Assert.AreEqual(Ground, s.Elevator.CurrentFloor);
            Assert.AreEqual(0, s.Elevator.Occupancy);
            Assert.AreEqual(DoorState.Closed, s.Elevator.Doors);
            Assert.AreEqual(0f, s.ElapsedSeconds);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Title, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);
            sim.AssertOrder(typeof(GameStarted), typeof(DayPhaseChanged));

            Tick(sim, 5f);
            Assert.AreEqual(0, s.Guests.ActiveGuests.Count, "the demo pilot is off: nothing runs in Intro");
            Assert.IsFalse(s.StartGameFromTitle(), "only from the title");
            Assert.IsTrue(s.BeginDay());
            Assert.IsTrue(s.AcceptsInput, "the player has the controls");
            Assert.AreEqual(1, sim.Events<DayStarted>().Count);
            Tick(sim, 3f);
            Assert.AreEqual(Ground, s.Elevator.CurrentFloor, "no bot moves the car in the real game");
        }

        [Test]
        public void RestartGame_FromTitle_LeavesTitle()
        {
            var sim = TitleSim();
            sim.S.RestartGame();
            Assert.IsFalse(sim.S.IsOnTitle);
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
        }

        [Test]
        public void ReturnToTitle_RestartsDemo()
        {
            var sim = TitleSim();
            var s = sim.S;
            Assert.IsTrue(s.StartGameFromTitle());
            Assert.IsTrue(s.BeginDay());
            Tick(sim, 0.5f);
            Assert.IsTrue(s.RequestFloor(FloorIndex.FromPhysicalNumber(9)));
            Tick(sim, 2f);
            sim.Log.Clear();

            s.ReturnToTitle();
            Assert.IsTrue(s.IsOnTitle);
            Assert.AreEqual(DayPhase.Title, s.Phase);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Running, phase.Previous);
            Assert.AreEqual(DayPhase.Title, phase.Current);
            Assert.That(sim.Events<GuestLeft>().Count, Is.GreaterThan(0), "the abandoned run's guests are dropped");
            Assert.AreEqual(Ground, s.Elevator.CurrentFloor, "car back at the ground floor");
            Assert.AreEqual(0f, s.ElapsedSeconds, 0.001f, "demo day restarted from t=0");
            Assert.AreEqual(1, s.Guests.ActiveGuests.Count, "demo t=0 spawn");

            Tick(sim, 30f);
            Assert.That(sim.Events<GuestDelivered>().Count, Is.GreaterThan(0), "demo plays again");
            Assert.IsEmpty(sim.Events<GameStarted>());

            sim.Log.Clear();
            s.ReturnToTitle();
            Assert.IsEmpty(sim.Events<DayPhaseChanged>(), "already on the title: demo restarts silently");
            Assert.AreEqual(0f, s.ElapsedSeconds, 0.001f);
        }

        [Test]
        public void Pause_FreezesClockAndPatience()
        {
            var sim = Sim.Running();
            var s = sim.S;
            var guest = sim.Spawn("impatient", 3, 6);
            Assert.IsTrue(s.RequestFloor(FloorIndex.FromPhysicalNumber(5)));
            sim.Run(1.5f);
            float elapsed = s.ElapsedSeconds, position = s.Elevator.Position;
            Assert.IsTrue(s.Guests.TryGet(guest, out var before));

            Assert.IsTrue(s.SetPaused(true));
            Assert.IsTrue(s.IsPaused);
            Assert.AreEqual(true, sim.Events<PauseChanged>().Single().Paused);
            Assert.AreEqual(DayPhase.Running, s.Phase, "pause is not a phase");
            Assert.IsTrue(s.SetPaused(true), "already paused: accepted, no second event");
            Assert.AreEqual(1, sim.Events<PauseChanged>().Count);

            sim.Run(30f); // the impatient guest (5 s) would have left
            Assert.AreEqual(elapsed, s.ElapsedSeconds, "day clock frozen");
            Assert.AreEqual(position, s.Elevator.Position, "car frozen");
            Assert.IsTrue(s.Guests.TryGet(guest, out var during), "guest still there");
            Assert.AreEqual(before.Patience01, during.Patience01, "patience frozen");
            Assert.AreEqual(GuestState.Waiting, during.State);
            Assert.IsEmpty(sim.Events<GuestLeft>());

            Assert.IsTrue(s.SetPaused(false));
            Assert.IsFalse(s.IsPaused);
            Assert.AreEqual(false, sim.Events<PauseChanged>().Last().Paused);
            sim.Run(0.5f);
            Assert.Greater(s.ElapsedSeconds, elapsed, "clock runs again");
            Assert.Greater(s.Elevator.Position, position);
        }

        [Test]
        public void Pause_AllowedInIntroSummaryShop_BlocksFlowCommands()
        {
            var sim = new Sim(SimFixture.Create());
            var s = sim.S;
            Assert.IsFalse(s.SetPaused(true), "not before the game started");
            s.StartNewGame();
            Assert.IsTrue(s.SetPaused(true), "Intro");
            Assert.IsFalse(s.BeginDay(), "flow refused while paused");
            Assert.IsTrue(s.SetPaused(false));
            Assert.IsTrue(s.BeginDay());
            s.EndDayNow();
            Assert.IsTrue(s.SetPaused(true), "Summary");
            Assert.IsFalse(s.ContinueFromSummary());
            s.SetPaused(false);
            Assert.IsTrue(s.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, s.Phase);
            Assert.IsTrue(s.SetPaused(true), "Shop");
            Assert.AreEqual(StickerApplyResult.ShopClosed, s.TryBuyAndApply(new StickerId("any_to_8"), FloorIndex.FromPhysicalNumber(2)));
            Assert.IsFalse(s.FinishShopping());

            s.RestartGame();
            Assert.IsFalse(s.IsPaused, "restart unpauses");
            Assert.AreEqual(false, sim.Events<PauseChanged>().Last().Paused);
        }

        [Test]
        public void Pause_RejectedOnTitleAndEnding()
        {
            var sim = TitleSim();
            var s = sim.S;
            Assert.IsFalse(s.SetPaused(true), "Title");
            Assert.IsFalse(s.IsPaused);

            Assert.IsTrue(s.StartGameFromTitle());
            Assert.IsTrue(s.SetPaused(true));
            s.ReturnToTitle();
            Assert.IsFalse(s.IsPaused, "ReturnToTitle unpauses");

            Assert.IsTrue(s.StartGameFromTitle());
            for (int d = 0; d < 2; d++)
            {
                Assert.IsTrue(s.BeginDay());
                s.EndDayNow();
                Assert.IsTrue(s.ContinueFromSummary());
                if (s.Phase == DayPhase.Shop) Assert.IsTrue(s.FinishShopping());
            }
            Assert.AreEqual(DayPhase.Ending, s.Phase);
            sim.Log.Clear();
            Assert.IsFalse(s.SetPaused(true), "Ending");
            Assert.IsFalse(s.IsPaused);
            Assert.IsEmpty(sim.Events<PauseChanged>());
            Assert.IsTrue(s.SetPaused(false), "unpausing is always fine");
        }

        [Test]
        public void Paused_RejectsElevatorCommands()
        {
            var sim = Sim.Running();
            var s = sim.S;
            Assert.IsTrue(s.AcceptsInput);
            Assert.IsTrue(s.SetPaused(true));
            Assert.IsFalse(s.AcceptsInput);
            Assert.IsFalse(s.RequestFloor(FloorIndex.FromPhysicalNumber(4)));
            Assert.IsFalse(s.OpenDoors());
            Assert.IsFalse(s.CloseDoors());
            Assert.IsFalse(s.Elevator.HasTarget);
            Assert.AreEqual(DoorState.Closed, s.Elevator.Doors);
            s.SetPaused(false);
            Assert.IsTrue(s.OpenDoors());
        }

        [Test]
        public void Settings_PersistAcrossSessions()
        {
            var store = new MemorySettingsStore();

            // first launch: defaults, player changes everything
            var busA = new EventBus();
            var a = new GameSession(SimFixture.Create(), busA, new System.Random(1));
            var pa = new SettingsPersistence(a.Settings, store, busA);
            pa.Load();
            Assert.IsFalse(a.Settings.UseKorean);
            Assert.AreEqual(1f, a.Settings.MasterVolume);
            Assert.IsTrue(a.Settings.ShowTutorialHints);
            Assert.IsFalse(pa.IsDirty);
            a.Settings.UseKorean = true;
            a.Settings.MasterVolume = 0.3f;
            a.Settings.ShowTutorialHints = false;
            Assert.IsTrue(pa.IsDirty);
            Assert.AreEqual(0, store.SaveCount, "disk flush is deferred");
            Assert.IsTrue(pa.Flush());
            Assert.IsFalse(pa.Flush(), "nothing new");
            Assert.AreEqual(1, store.SaveCount);
            pa.Dispose();

            // relaunch: same store, new session + bus
            var busB = new EventBus();
            int published = 0;
            busB.Subscribe<SettingsChanged>(e => published++);
            var b = new GameSession(SimFixture.Create(), busB, new System.Random(2));
            var pb = new SettingsPersistence(b.Settings, store, busB);
            pb.Load();
            Assert.IsTrue(b.Settings.UseKorean);
            Assert.AreEqual(0.3f, b.Settings.MasterVolume, 1e-6f);
            Assert.IsFalse(b.Settings.ShowTutorialHints);
            Assert.IsTrue(b.UseKorean, "session text follows the loaded setting");
            Assert.AreEqual(0, published, "loading is silent");
            Assert.IsFalse(pb.IsDirty);

            // a corrupt stored volume is clamped
            store.SetFloat(SettingsKeys.MasterVolume, 7f);
            pb.Load();
            Assert.AreEqual(1f, b.Settings.MasterVolume);
        }

        [Test]
        public void Settings_DisposedPersistence_StopsWriting()
        {
            var store = new MemorySettingsStore();
            var bus = new EventBus();
            var s = new GameSession(SimFixture.Create(), bus, new System.Random(1));
            var p = new SettingsPersistence(s.Settings, store, bus);
            s.Settings.UseKorean = true;
            p.Dispose();
            Assert.AreEqual(1, store.SaveCount, "Dispose flushes");
            s.Settings.UseKorean = false;
            Assert.IsTrue(store.TryGetInt(SettingsKeys.UseKorean, out int k));
            Assert.AreEqual(1, k, "no writes after Dispose");
        }
    }
}
