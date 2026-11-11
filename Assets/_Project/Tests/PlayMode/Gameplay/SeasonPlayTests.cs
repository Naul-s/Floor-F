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
    /// Real Main scene: ISeasonQuery / ISeasonFlow / IRunHistory are registered, the run history is loaded from the
    /// settings store before the first run and saved on every change (survives a Main reload = relaunch), and the
    /// Ending screen's "keep the hotel open" goes on into the winter season (debug keys drive the flow).
    /// </summary>
    public class SeasonPlayTests
    {
        MemorySettingsStore store;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            store = new MemorySettingsStore();
            store.SetInt(ProgressKeys.FinishedRuns, 3);
            store.SetInt(ProgressKeys.MysteryMisses, 2);
            GameplayRoot.SettingsStoreOverride = store;
            GameplayRoot.SkipTitleForTests = true;
            GameplayRoot.SkipTutorialForTests = true;
            Time.captureDeltaTime = 0f;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SettingsStoreOverride = null;
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        static void Press(DebugOverlay o, DebugKey key, bool shift = false)
            => o.Feed(false, shift, false, new[] { key }, null);

        [UnityTest]
        public IEnumerator Services_Registered_RunHistoryLoadedBeforeTheFirstRun_SavedAndRestored()
        {
            Assert.IsTrue(GameServices.Has<ISeasonQuery>(), "ISeasonQuery");
            Assert.IsTrue(GameServices.Has<ISeasonFlow>(), "ISeasonFlow");
            Assert.IsTrue(GameServices.Has<IRunHistory>(), "IRunHistory");
            var history = GameServices.Get<IRunHistory>();
            Assert.AreEqual(3, history.FinishedRuns, "loaded at boot");
            Assert.AreEqual(2, history.MysteryMisses);

            var o = Object.FindFirstObjectByType<DebugOverlay>();
            o.Cheats.Toggle();
            int saves = store.SaveCount;
            Press(o, DebugKey.G, shift: true); // forced Haunted = a finished run
            Assert.AreEqual(4, history.FinishedRuns);
            Assert.Greater(store.SaveCount, saves, "saved at once");
            Assert.IsTrue(store.TryGetInt(ProgressKeys.FinishedRuns, out int runs) && runs == 4);

            yield return SceneManager.LoadSceneAsync(GameScenes.Main); // "relaunch" on the same store
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(4, GameServices.Get<IRunHistory>().FinishedRuns, "restored after a relaunch");
        }

        [UnityTest]
        public IEnumerator EndingScreen_ExtendSeason_GoesOnIntoTheWinter_DebugDayKeys()
        {
            var o = Object.FindFirstObjectByType<DebugOverlay>();
            o.Cheats.Toggle();
            var day = GameServices.Get<IDayQuery>();
            var season = GameServices.Get<ISeasonQuery>();
            var flow = GameServices.Get<ISeasonFlow>();
            var data = GameServices.Get<GameData>();
            Assume.That(data.HasExtendedDays, "no winter days in the data");

            Press(o, DebugKey.P, shift: true);
            Assert.AreEqual(DayPhase.Ending, day.Phase);
            Assert.IsTrue(flow.CanExtendSeason);
            int extended = 0;
            using (GameServices.Events.Subscribe<SeasonExtended>(e => extended++))
                Assert.IsTrue(flow.ExtendSeason());
            Assert.AreEqual(1, extended);
            Assert.IsTrue(season.IsSeasonExtended);
            Assert.AreEqual(data.SeasonPlayableDayCount, day.PlayableDayCount);
            Assert.That(day.Phase, Is.EqualTo(DayPhase.Shop).Or.EqualTo(DayPhase.Intro));
            yield return null;

            Press(o, DebugKey.Digit9);
            Assert.AreEqual(9, day.DisplayDay);
            Assert.AreEqual(DayPhase.Intro, day.Phase);
            Assert.IsTrue(season.IsExtendedDay);
            Assert.AreEqual(data.GetDay(day.CurrentDay).lottery, season.IsLotteryNight);
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            if (season.IsLotteryNight) Assert.Greater(season.LotteryNumber, 0, "first winning number at the start");
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 30; i++) yield return null;
            Time.captureDeltaTime = 0f;

            Press(o, DebugKey.Digit0, shift: true);
            Assert.AreEqual(10, day.DisplayDay);
            Assert.AreEqual(data.GetDay(day.CurrentDay).luckyForAll, season.LuckyForAll);

            GameServices.Get<IGameFlow>().RestartGame();
            Assert.IsFalse(season.IsSeasonExtended, "a new run ends the season");
            Assert.AreEqual(data.PlayableDayCount, day.PlayableDayCount);
        }
    }
}
