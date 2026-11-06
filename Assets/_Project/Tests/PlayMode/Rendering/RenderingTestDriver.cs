using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Drives the REAL gameplay session for rendering tests (no fake gameplay events). Uses the test-visible
    /// GameplayRoot.Session for the two things the public services cannot do on demand: grant tips and end a day early.
    /// </summary>
    static class RenderingTestDriver
    {
        public static RenderingRoot Rendering() => FindRoot<RenderingRoot>(RenderingNames.Root);

        public static GameSession Session()
        {
            var root = FindRoot<GameplayRoot>("[Gameplay]");
            Assert.IsNotNull(root, "[Gameplay] root");
            Assert.IsNotNull(root.Session, "gameplay session (data loaded)");
            return root.Session;
        }

        static T FindRoot<T>(string name) where T : Component
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (go.name == name && go.TryGetComponent<T>(out var c)) return c;
            return null;
        }

        /// <summary>Loads Main and waits until gameplay started the game (Day 1 Intro) and rendering built the world.</summary>
        public static IEnumerator LoadMain()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase, "game started");
            Assert.IsTrue(Rendering().IsBuilt, "rendering built");
        }

        /// <summary>Day 1 Intro -> Running -> (tips granted) -> Summary -> Shop.</summary>
        public static void EnterShopWithTips(int tips)
        {
            var flow = GameServices.Get<IGameFlow>();
            var s = Session();
            Assert.IsTrue(flow.BeginDay(), "BeginDay");
            s.Score.AddTips(tips, earned: true);
            s.EndDayNow();
            Assert.AreEqual(DayPhase.Summary, s.Phase);
            Assert.IsTrue(flow.ContinueFromSummary(), "ContinueFromSummary");
            Assert.AreEqual(DayPhase.Shop, s.Phase, "day 1 offers four_to_f, so the shop opens");
        }

        /// <summary>Freezes the simulation (deltaTime 0 -> GameSession.Tick no-op) and lets rendering reconcile.</summary>
        public static IEnumerator Freeze()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            yield return null;
            yield return null;
        }

        public static void Unfreeze()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
        }
    }
}
