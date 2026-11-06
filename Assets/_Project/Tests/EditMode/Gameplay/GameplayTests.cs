using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Tests.EditMode.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Gameplay
{
    public class GameplayTests
    {
        static void Apply(GameSession s, PlayerIntent i) => PlayerCommands.Apply(i, s.Elevator, s, s.Building, s, s);

        [Test]
        public void Keys_WSpaceEnter_DriveTheSession()
        {
            var s = new GameSession(SimFixture.Create(), new EventBus(), new System.Random(1));
            s.StartNewGame();
            Assert.IsFalse(PlayerCommands.Apply(PlayerIntent.Up, s.Elevator, s, s.Building, s, s), "no elevator input during Intro");

            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Running, s.Phase, "Enter: Intro -> Running");

            Apply(s, PlayerIntent.Up);
            Apply(s, PlayerIntent.Up);
            Assert.AreEqual(2, s.Elevator.TargetFloor.Value, "W twice: two floors up");
            s.Tick(5f);
            Assert.AreEqual(2, s.Elevator.CurrentFloor.Value);
            Apply(s, PlayerIntent.Down);
            s.Tick(5f);
            Assert.AreEqual(1, s.Elevator.CurrentFloor.Value, "S: one floor down");

            Apply(s, PlayerIntent.ToggleDoors);
            s.Tick(0.1f);
            Assert.AreEqual(DoorState.Open, s.Elevator.Doors);
            Apply(s, PlayerIntent.ToggleDoors);
            s.Tick(0.1f);
            Assert.AreEqual(DoorState.Closed, s.Elevator.Doors);

            s.EndDayNow();
            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Shop, s.Phase);
            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(2, s.CurrentDay.Number);
        }

        [Test]
        public void RealData_AutoPilot_PlaysEveryDay_ReachesAnEnding()
        {
            var data = GameDataResources.Load();
            var bus = new EventBus();
            var s = new GameSession(data, bus, new System.Random(7));
            var ended = new List<int>();
            int delivered = 0;
            bus.Subscribe<DayEnded>(e => ended.Add(e.Day.Number));
            bus.Subscribe<GuestDelivered>(e => delivered++);
            var pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building);

            s.StartNewGame();
            for (int frame = 0; frame < 200000 && s.Phase != DayPhase.Ending; frame++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Intro: s.BeginDay(); break;
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop:
                        foreach (var o in s.Offers) s.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor);
                        s.FinishShopping();
                        break;
                    case DayPhase.Running:
                        pilot.Step();
                        s.Tick(1f / 30f);
                        break;
                }
            }

            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.AreNotEqual(EndingKind.None, s.Ending);
            Assert.That(ended.Count, Is.GreaterThanOrEqualTo(1));
            CollectionAssert.AreEqual(ended, new SortedSet<int>(ended), "days end in order");
            Assert.That(delivered, Is.GreaterThan(0), "the bot delivers someone");
            TestContext.WriteLine($"ending {s.Ending} after day {s.CurrentDay.Number}: luck {s.Score.Luck}, tips {s.Score.Tips}, delivered {delivered}");
        }
    }
}
