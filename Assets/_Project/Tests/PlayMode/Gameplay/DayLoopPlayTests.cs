using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Gameplay
{
    /// <summary>Main loads -> services registered -> Enter-equivalent starts Day 1 -> scripted input plays it -> DayEnded.</summary>
    public class DayLoopPlayTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Main_Day1_ScriptedInput_PublishesDayEnded()
        {
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;

            Assert.IsTrue(GameServices.Has<IElevatorQuery>(), "IElevatorQuery");
            Assert.IsTrue(GameServices.Has<IElevatorControl>(), "IElevatorControl");
            Assert.IsTrue(GameServices.Has<IGuestQuery>(), "IGuestQuery");
            Assert.IsTrue(GameServices.Has<IBuildingQuery>(), "IBuildingQuery");
            Assert.IsTrue(GameServices.Has<IScoreQuery>(), "IScoreQuery");
            Assert.IsTrue(GameServices.Has<IDayQuery>(), "IDayQuery");
            Assert.IsTrue(GameServices.Has<IGameFlow>(), "IGameFlow");
            Assert.IsTrue(GameServices.Has<IShopService>(), "IShopService");
            Assert.IsTrue(GameServices.Has<IActorLocator>(), "IActorLocator");

            var day = GameServices.Get<IDayQuery>();
            var flow = GameServices.Get<IGameFlow>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var control = GameServices.Get<IElevatorControl>();
            var building = GameServices.Get<IBuildingQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            Assert.AreEqual(DayPhase.Intro, day.Phase, "game started after the first frame");
            Assert.AreEqual(1, day.CurrentDay.Number);

            bool dayEnded = false;
            int delivered = 0, boarded = 0;
            using (GameServices.Events.Subscribe<DayEnded>(e => dayEnded = e.Day.Number == 1))
            using (GameServices.Events.Subscribe<GuestDelivered>(e => delivered++))
            using (GameServices.Events.Subscribe<GuestBoarded>(e => boarded++))
            {
                // Enter (keyboard intent) = Intro -> Running
                Assert.IsTrue(PlayerCommands.Apply(PlayerIntent.Advance, elevator, control, building, day, flow));
                Assert.AreEqual(DayPhase.Running, day.Phase);

                var pilot = new AutoPilot(elevator, control, guests, building);
                Time.captureDeltaTime = 0.1f; // 10x faster than real time, deterministic steps
                int frames = Mathf.CeilToInt(day.DurationSeconds / 0.1f) + 200;
                for (int i = 0; i < frames && !dayEnded; i++)
                {
                    pilot.Step();
                    yield return null;
                }
                Time.captureDeltaTime = 0f;
            }

            Assert.IsTrue(dayEnded, "DayEnded(1) published");
            Assert.That(day.Phase, Is.EqualTo(DayPhase.Summary).Or.EqualTo(DayPhase.Ending));
            Debug.Log($"[DayLoopPlayTests] day 1: boarded {boarded}, delivered {delivered}, summary delivered {day.LastSummary.Delivered}");
            Assert.That(boarded, Is.GreaterThan(0), "scripted input boarded someone");
        }
    }
}
