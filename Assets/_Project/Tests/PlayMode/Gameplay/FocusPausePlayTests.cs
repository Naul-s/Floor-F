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
    /// WebGL tab / window focus: losing focus (OnApplicationFocus(false) / OnApplicationPause(true)) while a day runs
    /// opens the menu pause (IMenuFlow.SetPaused(true)); regaining focus does not resume. Never on the title, outside
    /// Running or during a Day 0 coach-mark hold. Off in the Editor/batch mode unless <see cref="GameplayRoot.ForceFocusPauseForTests"/>.
    /// The Unity messages are delivered with SendMessage, as the engine would.
    /// </summary>
    public class FocusPausePlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            GameplayRoot.ForceFocusPauseForTests = true;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            GameplayRoot.ForceFocusPauseForTests = false;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        static IEnumerator LoadMain(bool skipTitle, bool skipTutorial)
        {
            GameplayRoot.SkipTitleForTests = skipTitle;
            GameplayRoot.SkipTutorialForTests = skipTutorial;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
        }

        static GameplayRoot Root()
        {
            var root = Object.FindFirstObjectByType<GameplayRoot>();
            Assert.IsNotNull(root, "[Gameplay] root");
            return root;
        }

        [UnityTest]
        public IEnumerator FocusLost_WhileRunning_Pauses_AndStaysPaused()
        {
            yield return LoadMain(true, true);
            var root = Root();
            var day = GameServices.Get<IDayQuery>();
            var flow = GameServices.Get<IGameFlow>();
            var menu = GameServices.Get<IMenuFlow>();
            Assert.AreEqual(DayPhase.Intro, day.Phase);

            root.SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(menu.IsPaused, "Intro already waits on the player: no pause");

            Assert.IsTrue(flow.BeginDay());
            yield return null;
            int changes = 0;
            using (GameServices.Events.Subscribe<PauseChanged>(e => { if (e.Paused) changes++; }))
            {
                root.SendMessage("OnApplicationFocus", false);
                Assert.IsTrue(menu.IsPaused, "tab lost focus mid-day -> pause menu");
                Assert.AreEqual(1, changes, "PauseChanged(true)");
                float elapsed = day.ElapsedSeconds;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(elapsed, day.ElapsedSeconds, "the clock is frozen");

                root.SendMessage("OnApplicationFocus", true);
                yield return null;
                Assert.IsTrue(menu.IsPaused, "focus back: no auto-resume");
                root.SendMessage("OnApplicationFocus", false);
                Assert.AreEqual(1, changes, "already paused: nothing new");
            }

            Assert.IsTrue(menu.SetPaused(false));
            root.SendMessage("OnApplicationPause", true);
            Assert.IsTrue(menu.IsPaused, "OnApplicationPause(true) pauses too");
        }

        [UnityTest]
        public IEnumerator FocusLost_OnTitle_DoesNothing()
        {
            yield return LoadMain(false, false);
            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(menu.IsOnTitle);
            Root().SendMessage("OnApplicationFocus", false);
            Root().SendMessage("OnApplicationPause", true);
            Assert.IsFalse(menu.IsPaused);
            Assert.IsTrue(menu.IsOnTitle, "the demo keeps playing");
        }

        [UnityTest]
        public IEnumerator FocusLost_DuringTutorialHold_KeepsTheHold()
        {
            yield return LoadMain(true, false);
            var day = GameServices.Get<IDayQuery>();
            var flow = GameServices.Get<IGameFlow>();
            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(day.IsTutorial);
            Assert.IsTrue(flow.BeginDay());
            Assert.IsTrue(flow.SetTutorialHold(true));
            Root().SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(menu.IsPaused, "a coach mark already freezes the day");
            Assert.IsTrue(flow.TutorialHold, "and keeps holding");

            Assert.IsTrue(flow.SetTutorialHold(false));
            Root().SendMessage("OnApplicationFocus", false);
            Assert.IsTrue(menu.IsPaused, "the running tutorial pauses like any day");
        }

        [UnityTest]
        public IEnumerator FocusLost_InEditorWithoutTheTestSwitch_DoesNothing()
        {
            GameplayRoot.ForceFocusPauseForTests = false;
            yield return LoadMain(true, true);
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Root().SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(GameServices.Get<IMenuFlow>().IsPaused, "Editor / batch runs never pause on focus");
        }
    }
}
