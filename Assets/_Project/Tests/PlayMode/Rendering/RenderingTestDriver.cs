using System.Collections;
using System.IO;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
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

        /// <summary>
        /// Loads Main straight into Day 1 Intro (GameplayRoot.SkipTitleForTests, cleared again by Unfreeze) and waits
        /// until rendering built the world. Should the title still show, presses Start like the player does.
        /// Skips the "Day 0" training shift (no clock, waves, no shop) so tests run on the first real day.
        /// </summary>
        public static IEnumerator LoadMain()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = true;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            if (GameServices.Get<IDayQuery>().Phase == DayPhase.Title && GameServices.TryGet<IMenuFlow>(out var menu))
            {
                Assert.IsTrue(menu.StartGameFromTitle(), "IMenuFlow.StartGameFromTitle");
                for (int i = 0; i < 2; i++) yield return null;
            }
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase, "game started");
            if (GameServices.Get<IDayQuery>().IsTutorial)
                Assert.IsTrue(GameServices.Get<IGameFlow>().SkipTutorial(), "skip the Day 0 training shift");
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsTutorial, "on the first real day");
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

        /// <summary>Loads Main the way the game boots: title screen + attract demo (DayPhase.Title).</summary>
        public static IEnumerator LoadTitle()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = false;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase, "boots to the title");
            Assert.IsTrue(Rendering().IsBuilt, "rendering built");
        }

        public static void Unfreeze()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = false;
        }

        /// <summary>Pause through the real IMenuFlow when gameplay provides it, else announce PauseChanged like it would.</summary>
        public static void SetPaused(bool paused)
        {
            if (GameServices.TryGet<IMenuFlow>(out var menu))
                Assert.IsTrue(menu.SetPaused(paused), "IMenuFlow.SetPaused(" + paused + ")");
            else
                GameServices.Events.Publish(new PauseChanged(paused));
        }

        /// <summary>
        /// Writes the low-res world target region covering world points <paramref name="a"/>..<paramref name="b"/>,
        /// nearest-upscaled by <paramref name="scale"/> (true pixel readability check). Call right after FrameCapture.Capture
        /// (which rendered the world camera into the target).
        /// </summary>
        public static string CaptureTargetCrop(string name, Vector3 a, Vector3 b, int scale)
        {
            var p = Rendering().Pipeline;
            var rt = p.Target;
            var va = p.WorldCamera.WorldToViewportPoint(a);
            var vb = p.WorldCamera.WorldToViewportPoint(b);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(va.x, vb.x) * rt.width), 0, rt.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(va.x, vb.x) * rt.width), x0 + 1, rt.width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(va.y, vb.y) * rt.height), 0, rt.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(va.y, vb.y) * rt.height), y0 + 1, rt.height);
            int w = x1 - x0, h = y1 - y0;
            var prev = RenderTexture.active;
            var src = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var big = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                src.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                src.Apply();
                var px = src.GetPixels32();
                var outPx = new Color32[w * scale * h * scale];
                for (int y = 0; y < h * scale; y++)
                    for (int x = 0; x < w * scale; x++)
                        outPx[y * w * scale + x] = px[(y / scale) * w + x / scale];
                big.SetPixels32(outPx);
                big.Apply();
                Directory.CreateDirectory(FrameCapture.OutputDir);
                var path = Path.Combine(FrameCapture.OutputDir, name + ".png");
                File.WriteAllBytes(path, big.EncodeToPNG());
                Debug.Log("[FrameCapture] " + path);
                return path;
            }
            finally
            {
                RenderTexture.active = prev;
                Object.Destroy(src);
                Object.Destroy(big);
            }
        }
    }
}
