using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// Main loads -> services registered -> Title with the attract demo -> StartGameFromTitle -> Day 1 Intro ->
    /// scripted input plays it -> DayEnded. Also: the test boot switch, pause in the real loop, settings persistence.
    /// </summary>
    public class DayLoopPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SettingsStoreOverride = null;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SettingsStoreOverride = null;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        static void AssertServices()
        {
            Assert.IsTrue(GameServices.Has<IElevatorQuery>(), "IElevatorQuery");
            Assert.IsTrue(GameServices.Has<IElevatorControl>(), "IElevatorControl");
            Assert.IsTrue(GameServices.Has<IGuestQuery>(), "IGuestQuery");
            Assert.IsTrue(GameServices.Has<IBuildingQuery>(), "IBuildingQuery");
            Assert.IsTrue(GameServices.Has<IScoreQuery>(), "IScoreQuery");
            Assert.IsTrue(GameServices.Has<IDayQuery>(), "IDayQuery");
            Assert.IsTrue(GameServices.Has<IGameFlow>(), "IGameFlow");
            Assert.IsTrue(GameServices.Has<IShopService>(), "IShopService");
            Assert.IsTrue(GameServices.Has<IActorLocator>(), "IActorLocator");
            Assert.IsTrue(GameServices.Has<IGameSettings>(), "IGameSettings");
            Assert.IsTrue(GameServices.Has<IMenuFlow>(), "IMenuFlow");
        }

        [UnityTest]
        public IEnumerator Main_BootsOnTitle_DemoPlays_StartGameFromTitle_GoesToDay1Intro()
        {
            DayPhase? firstPhase = null;
            int boarded = 0, gameStarted = 0;
            // the bus is replaced only when play mode starts, so subscribing before the load is fine
            using (GameServices.Events.Subscribe<DayPhaseChanged>(e => { if (firstPhase == null) firstPhase = e.Current; }))
            {
                yield return SceneManager.LoadSceneAsync(GameScenes.Main);
                for (int i = 0; i < 3; i++) yield return null;
            }
            AssertServices();
            var menu = GameServices.Get<IMenuFlow>();
            var day = GameServices.Get<IDayQuery>();
            var control = GameServices.Get<IElevatorControl>();
            Assert.AreEqual(DayPhase.Title, firstPhase, "first DayPhaseChanged is -> Title");
            Assert.IsTrue(menu.IsOnTitle);
            Assert.AreEqual(DayPhase.Title, day.Phase);
            Assert.IsFalse(control.AcceptsInput, "player input off during the demo");
            Assert.IsFalse(menu.SetPaused(true), "no pause on the title");

            using (GameServices.Events.Subscribe<GuestBoarded>(e => boarded++))
            using (GameServices.Events.Subscribe<GameStarted>(e => gameStarted++))
            {
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 1500 && boarded == 0; i++) yield return null;
                Time.captureDeltaTime = 0f;
                Assert.That(boarded, Is.GreaterThan(0), "the demo bot boards guests in the real loop");
                Assert.AreEqual(0, gameStarted);
                Assert.AreEqual(DayPhase.Title, day.Phase);

                Assert.IsTrue(menu.StartGameFromTitle());
                Assert.AreEqual(1, gameStarted);
            }
            Assert.AreEqual(DayPhase.Intro, day.Phase);
            Assert.AreEqual(1, day.CurrentDay.Number);
            Assert.AreEqual(0, GameServices.Get<IGuestQuery>().ActiveGuests.Count, "demo guests cleared");
            var score = GameServices.Get<IScoreQuery>();
            var data = GameServices.Get<GameData>();
            Assert.AreEqual(data.Rules.startLuck, score.Luck, "fresh Luck");
            Assert.AreEqual(data.Rules.startTips, score.Tips, "fresh tips");

            menu.ReturnToTitle();
            Assert.AreEqual(DayPhase.Title, day.Phase, "back to the title");
            Assert.IsTrue(menu.StartGameFromTitle());
            Assert.AreEqual(DayPhase.Intro, day.Phase);
        }

        [UnityTest]
        public IEnumerator Main_Day1_ScriptedInput_PublishesDayEnded()
        {
            GameplayRoot.SkipTitleForTests = true; // the documented test boot: straight into Day 1 Intro
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;

            AssertServices();
            Assert.IsFalse(GameServices.Get<IGameSettings>().UseKorean, "English by default");

            var day = GameServices.Get<IDayQuery>();
            var flow = GameServices.Get<IGameFlow>();
            var menu = GameServices.Get<IMenuFlow>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var control = GameServices.Get<IElevatorControl>();
            var building = GameServices.Get<IBuildingQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            Assert.AreEqual(DayPhase.Intro, day.Phase, "game started after the first frame");
            Assert.IsFalse(menu.IsOnTitle);
            Assert.AreEqual(1, day.CurrentDay.Number);

            bool dayEnded = false;
            int delivered = 0, boarded = 0;
            using (GameServices.Events.Subscribe<DayEnded>(e => dayEnded = e.Day.Number == 1))
            using (GameServices.Events.Subscribe<GuestDelivered>(e => delivered++))
            using (GameServices.Events.Subscribe<GuestBoarded>(e => boarded++))
            {
                // Enter (keyboard intent) = Intro -> Running
                Assert.IsTrue(PlayerCommands.Apply(PlayerIntent.Advance, elevator, control, building, day, flow, menu));
                Assert.AreEqual(DayPhase.Running, day.Phase);

                var pilot = new AutoPilot(elevator, control, guests, building);
                Time.captureDeltaTime = 0.1f; // 10x faster than real time, deterministic steps

                // pause in the real loop: the clock does not move for 20 frames
                pilot.Step();
                yield return null;
                Assert.IsTrue(menu.SetPaused(true));
                float frozenAt = day.ElapsedSeconds;
                for (int i = 0; i < 20; i++) yield return null;
                Assert.AreEqual(frozenAt, day.ElapsedSeconds, "paused: day clock frozen");
                Assert.IsTrue(menu.SetPaused(false));

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

        [UnityTest]
        public IEnumerator Main_Settings_PersistAcrossMainReloads_PlayerPrefs()
        {
            // real PlayerPrefs adapter with a private test prefix; keys are deleted afterwards
            var store = new PlayerPrefsSettingsStore("FloorF.Test." + System.Guid.NewGuid().ToString("N") + ".");
            try
            {
                GameplayRoot.SettingsStoreOverride = store;
                GameplayRoot.SkipTitleForTests = true;
                yield return SceneManager.LoadSceneAsync(GameScenes.Main);
                for (int i = 0; i < 3; i++) yield return null;
                var settings = GameServices.Get<IGameSettings>();
                Assert.IsFalse(settings.UseKorean, "nothing stored yet: defaults");
                settings.UseKorean = true;
                settings.MasterVolume = 0.25f;
                settings.ShowTutorialHints = false;
                yield return null; // throttled flush in LateUpdate

                yield return SceneManager.LoadSceneAsync(GameScenes.Main); // "relaunch"
                for (int i = 0; i < 3; i++) yield return null;
                settings = GameServices.Get<IGameSettings>();
                Assert.IsTrue(settings.UseKorean, "UseKorean restored");
                Assert.AreEqual(0.25f, settings.MasterVolume, 1e-4f, "volume restored");
                Assert.IsFalse(settings.ShowTutorialHints, "hints restored");
            }
            finally
            {
                GameplayRoot.SettingsStoreOverride = null;
                store.DeleteAll();
            }
            Assert.IsFalse(PlayerPrefs.HasKey(store.Prefix + SettingsKeys.UseKorean), "test keys removed");

            // leave a default (English) session behind for later tests: batch mode -> fresh in-memory store
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(PlayerPrefs.HasKey(store.Prefix + SettingsKeys.UseKorean), "the old session did not write the keys back");
            if (Application.isBatchMode) Assert.IsFalse(GameServices.Get<IGameSettings>().UseKorean);
        }
    }
}
