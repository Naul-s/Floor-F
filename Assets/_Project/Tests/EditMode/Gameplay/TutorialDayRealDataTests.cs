using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Gameplay
{
    /// <summary>
    /// The REAL "Day 0" training shift played the way the lessons intend (scripted player, real timings): wave 1 gives a
    /// lucky door-open floor, wave 2 a lucky head-count, wave 3 an unlucky 4 (and a lucky 7); then the day ends early,
    /// and leaving its summary resets Luck/tips and goes straight to Day 1.
    /// </summary>
    public class TutorialDayRealDataTests
    {
        GameData data;
        GameSession s;
        readonly List<LuckJudged> judged = new List<LuckJudged>();

        [SetUp]
        public void SetUp()
        {
            data = GameDataResources.Load();
            var bus = new EventBus();
            s = new GameSession(data, bus, new Random(1));
            judged.Clear();
            bus.Subscribe<LuckJudged>(e => judged.Add(e));
        }

        void RunUntil(Func<bool> done, string what, float max = 120f)
        {
            for (float t = 0f; !done(); t += 1f / 30f)
            {
                Assert.Less(t, max, what);
                s.Tick(1f / 30f);
            }
        }

        static FloorIndex F(int physical) => FloorIndex.FromPhysicalNumber(physical);

        void GoTo(int physical)
        {
            if (s.Elevator.CurrentFloor == F(physical)) return;
            Assert.IsTrue(s.RequestFloor(F(physical)), "RequestFloor " + physical);
            RunUntil(() => s.Elevator.Motion == ElevatorMotion.Idle && !s.Elevator.HasTarget && s.Elevator.Doors == DoorState.Closed
                           && s.Elevator.CurrentFloor == F(physical), "reach " + physical);
        }

        void OpenAndWait(Func<bool> until, string what)
        {
            Assert.IsTrue(s.OpenDoors(), "OpenDoors");
            RunUntil(() => s.Elevator.Doors == DoorState.Open && until(), what);
        }

        void Close()
        {
            Assert.IsTrue(s.CloseDoors(), "CloseDoors");
            RunUntil(() => s.Elevator.Doors == DoorState.Closed, "doors closed");
        }

        int Active => s.Guests.ActiveGuests.Count;

        [Test]
        public void RealDay0_ScriptedPlay_TeachesEachLesson_ThenResets()
        {
            int startLuck = data.Rules.startLuck, startTips = data.Rules.startTips;
            s.StartNewGame();
            Assert.IsTrue(s.IsTutorial);
            Assert.AreEqual(0, s.DisplayDay);
            Assert.IsTrue(s.BeginDay());

            // wave 1: grandma -> gym (3): lucky door-open floor
            RunUntil(() => Active == 1, "wave 1");
            OpenAndWait(() => s.Elevator.Occupancy == 1 && !s.IsTransferring, "grandma boards");
            Close();
            GoTo(3);
            OpenAndWait(() => Active == 0, "grandma gets off at 3");
            Assert.IsTrue(judged.Any(j => j.Source == LuckSource.FloorLabel && j.Number == 3 && j.Verdict == LuckVerdict.Lucky), "lesson 1: lucky 3");
            Close();

            // wave 2: three grandmas -> sauna (5): lucky head-count 3
            judged.Clear();
            RunUntil(() => Active == 3, "wave 2 after the hotel emptied");
            GoTo(1);
            OpenAndWait(() => s.Elevator.Occupancy == 3 && !s.IsTransferring, "three board");
            Close();
            Assert.AreEqual(3, judged.Count(j => j.Source == LuckSource.Occupancy && j.Number == 3 && j.Verdict == LuckVerdict.Lucky), "lesson 2: three inside");
            GoTo(5);
            OpenAndWait(() => Active == 0, "they get off at 5");
            Close();

            // wave 3: tourist -> bar (7), grandma -> karaoke (4): unlucky 4, lucky 7
            judged.Clear();
            RunUntil(() => Active == 2, "wave 3");
            GoTo(1);
            OpenAndWait(() => s.Elevator.Occupancy == 2 && !s.IsTransferring, "both board");
            Close();
            GoTo(4);
            OpenAndWait(() => Active == 1, "grandma gets off at 4");
            Assert.IsTrue(judged.Any(j => j.Source == LuckSource.FloorLabel && j.Number == 4 && j.Verdict == LuckVerdict.Unlucky), "lesson 3: unlucky 4");
            Close();
            GoTo(7);
            OpenAndWait(() => Active == 0 || s.Phase != DayPhase.Running, "tourist gets off at 7");
            Assert.IsTrue(judged.Any(j => j.Source == LuckSource.FloorLabel && j.Number == 7 && j.Verdict == LuckVerdict.Lucky), "lucky 7 for the tourist");

            RunUntil(() => s.Phase != DayPhase.Running, "early end after the last wave", 10f);
            Assert.AreEqual(DayPhase.Summary, s.Phase);
            Assert.AreEqual(6, s.LastSummary.Delivered);
            Assert.AreEqual(0, s.LastSummary.Left);
            Assert.AreEqual(s.DurationSeconds, s.RemainingSeconds, "no clock on the training shift");
            Assert.Greater(s.Score.Tips, startTips, "tips earned during the shift");

            Assert.IsTrue(s.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Intro, s.Phase, "no shop after Day 0");
            Assert.AreEqual(1, s.DisplayDay);
            Assert.IsFalse(s.IsTutorial);
            Assert.AreEqual(startLuck, s.Score.Luck);
            Assert.AreEqual(startTips, s.Score.Tips);
        }
    }
}
