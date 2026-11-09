using System.Collections;
using System.Collections.Generic;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Scenic view on the real Main scene, driven by synthetic ScenicViewChanged events (UI owns the real ones): the
    /// camera zooms out over the whole city while the game is paused (unscaled camera clock), every rung a whole-ppu
    /// frame with city / facade / signs rebuilt for it, and returns exactly to the gameplay frame. Captures
    /// render_scenic_wide, render_scenic_starnight and render_zoom_steps (six frames of the zoom into the hotel).
    /// </summary>
    public class RenderingScenicTests
    {
        const float Step = 1f / 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var root = RenderingTestDriver.Rendering();
            if (root != null)
            {
                root.CameraStepOverride = null;
                if (root.Sky != null) root.Sky.ForceStarNight = null;
                if (root.Rig != null && root.Rig.InScenicView) GameServices.Events.Publish(new ScenicViewChanged(false, false));
            }
            if (GameServices.TryGet<IMenuFlow>(out var m) && m.IsPaused) m.SetPaused(false);
            HideOtherCanvases(false);
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

        static void AssertCameraOnGrid(RenderingRoot root, string what)
        {
            var cam = root.Pipeline.WorldCamera;
            float ppu = RenderingStyle.PixelHeight / (2f * cam.orthographicSize);
            Assert.AreEqual(Mathf.Round(ppu), ppu, 1e-3f, what + ": whole pixels per unit");
            var p = cam.transform.position;
            Assert.AreEqual(Mathf.Round(p.x * ppu), p.x * ppu, 3e-3f, what + ": camera x on the pixel grid");
            Assert.AreEqual(Mathf.Round(p.y * ppu), p.y * ppu, 3e-3f, what + ": camera y on the pixel grid");
            Assert.AreEqual(Mathf.RoundToInt(ppu), root.City.ActivePixelsPerUnit, what + ": city mesh of this rung");
            Assert.AreEqual(Mathf.RoundToInt(ppu), root.Facade.DetailPixelsPerUnit, what + ": facade details of this rung");
            Assert.AreEqual(ppu, root.Building.Grid.PixelsPerUnit, 1e-3f, what + ": sign grid of this rung");
        }

        /// <summary>Day 1 running, then paused like the UI's scenic mode does (IMenuFlow) with the sim frozen.</summary>
        static IEnumerator RunningThenPaused(RenderingRoot root)
        {
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            RenderingTestDriver.SetPaused(true);
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            root.CameraStepOverride = Step;
            yield return null;
            Assert.IsTrue(root.IsPaused);
        }

        [UnityTest]
        public IEnumerator ScenicView_WhilePaused_ZoomsOutOnWholePixels_AndReturnsExactly()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var rig = root.Rig;
            var cam = root.Pipeline.WorldCamera;
            yield return RunningThenPaused(root);
            float juice = root.JuiceTime, ambient = root.AmbientTime;
            int dissolves = root.Pipeline.Glow.DissolvesStarted;

            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            Assert.IsTrue(rig.ScenicOn);
            var levels = new List<int>();
            for (int i = 0; i < 200 && (rig.ZoomProgress < 1f || levels.Count == 0); i++)
            {
                yield return null;
                AssertCameraOnGrid(root, "zoom-out frame " + i);
                if (levels.Count == 0 || levels[levels.Count - 1] != rig.Level) levels.Add(rig.Level);
            }
            Assert.AreEqual(1f, rig.ZoomProgress, "reached the wide frame while paused");
            CollectionAssert.AreEqual(new[] { 11, 10, 9, 8 }, levels, "whole-ppu rungs, in order");
            Assert.AreEqual(rig.WideFrame.OrthographicSize, cam.orthographicSize, 1e-5f, "wide frame");
            Assert.AreEqual(juice, root.JuiceTime, "game juice stays paused");
            Assert.Greater(root.AmbientTime, ambient + 0.5f, "the city keeps living while the player looks");
            if (root.Pipeline.Glow.Available)
                Assert.AreEqual(dissolves + 3, root.Pipeline.Glow.DissolvesStarted, "one pixel dissolve per rung change");

            // hold + slow pan (whole pixels)
            var pans = new HashSet<int>();
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                AssertCameraOnGrid(root, "hold frame " + i);
                pans.Add(rig.OffsetPixels.x);
            }
            Assert.Greater(pans.Count, 3, "slow pan");
            Assert.IsFalse(root.Pipeline.Glow.Dissolving, "dissolves finished during the hold");
            Assert.AreSame(root.Pipeline.Glow.LitTexture, root.Pipeline.Screen.Texture, "screen back on the lit target");

            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            for (int i = 0; i < 200 && rig.ZoomProgress > 0f; i++)
            {
                yield return null;
                AssertCameraOnGrid(root, "zoom-in frame " + i);
            }
            yield return null;
            Assert.AreEqual(0f, rig.ZoomProgress);
            Assert.AreEqual(rig.BaseFrame.OrthographicSize, cam.orthographicSize, 1e-6f, "gameplay size");
            Assert.AreEqual(0f, Vector3.Distance(rig.BaseFrame.Position, cam.transform.position), 1e-5f, "exactly the gameplay frame");
            AssertCameraOnGrid(root, "gameplay frame");
            Assert.AreEqual(11, root.City.ActivePixelsPerUnit);
            Assert.AreEqual(juice, root.JuiceTime, "still paused");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(root.Pipeline.Glow.Dissolving);
            Assert.AreSame(root.Pipeline.Glow.LitTexture, root.Pipeline.Screen.Texture);
        }

        [UnityTest]
        public IEnumerator EstablishingShot_StartsWide_ThenZoomsIntoTheHotel()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var rig = root.Rig;
            root.CameraStepOverride = Step;
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase);
            GameServices.Events.Publish(new ScenicViewChanged(true, true));
            yield return null;
            Assert.AreEqual(8, rig.Level, "starts AT the wide frame");
            AssertCameraOnGrid(root, "establishing");
            for (int i = 0; i < 150; i++) yield return null; // ~2.5 s hold
            Assert.AreEqual(8, rig.Level);
            GameServices.Events.Publish(new ScenicViewChanged(false, true));
            int frames = 0;
            while (rig.ZoomProgress > 0f && frames < 200) { yield return null; frames++; AssertCameraOnGrid(root, "zoom-in " + frames); }
            Assert.That(frames * Step, Is.InRange(0.8f, 1.0f + 2f * Step), "~0.9 s zoom into the hotel");
            Assert.AreEqual(0f, Vector3.Distance(rig.BaseFrame.Position, root.Pipeline.WorldCamera.transform.position), 1e-5f);
            Assert.AreEqual(rig.BaseFrame.OrthographicSize, root.Pipeline.WorldCamera.orthographicSize, 1e-6f);
        }

        // ------------------------------------------------------------------ captures

        static IEnumerator WaitWide(RenderingRoot root, int extraFrames)
        {
            for (int i = 0; i < 300 && root.Rig.ZoomProgress < 1f; i++) yield return null;
            for (int i = 0; i < extraFrames; i++) yield return null;
        }

        static IEnumerator WaitGameplay(RenderingRoot root)
        {
            for (int i = 0; i < 300 && root.Rig.ZoomProgress > 0f; i++) yield return null;
            for (int i = 0; i < 12; i++) yield return null;
        }

        /// <summary>The image on the pixel screen (640x360) as it was rendered last frame.</summary>
        static Color32[] ReadScreen(RenderingRoot root, out int w, out int h)
        {
            var rt = root.Pipeline.Screen.Texture as RenderTexture ?? root.Pipeline.Target;
            w = rt.width; h = rt.height;
            var prev = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                Object.Destroy(tex);
            }
        }

        [UnityTest]
        public IEnumerator Capture_ScenicWide_StarNight_ZoomSteps()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var rig = root.Rig;
            yield return RunningThenPaused(root);

            // ordinary night, scenic: UI hidden (the UI does that itself in the game)
            HideOtherCanvases(true);
            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            yield return WaitWide(root, 30);
            Assert.AreEqual(8, rig.Level);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_scenic_wide")));

            // zoom into the hotel: six frames of the transition (rung holds + mid-dissolves), 3x2 sheet
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            var frames = new List<Color32[]>();
            var info = new List<string>();
            int w = 0, h = 0;
            bool[] dissolveTaken = new bool[12];
            bool heldTaken = false;
            yield return null; // the render of the event frame
            frames.Add(ReadScreen(root, out w, out h));
            info.Add("wide " + rig.Level + " ppu");
            for (int i = 0; i < 200 && rig.ZoomProgress > 0f; i++)
            {
                yield return null;
                // the screen holds last frame's render, made from the rig / dissolve state still current here
                int level = rig.Level;
                bool dissolving = root.Pipeline.Glow.Dissolving;
                float mix = root.Pipeline.Glow.DissolveMix;
                if (dissolving && level < dissolveTaken.Length && !dissolveTaken[level] && mix >= 0.4f)
                {
                    dissolveTaken[level] = true;
                    frames.Add(ReadScreen(root, out w, out h));
                    info.Add("dissolve -> " + level + " ppu (mix " + mix.ToString("0.00") + ")");
                }
                else if (!dissolving && level == 10 && !heldTaken)
                {
                    heldTaken = true;
                    frames.Add(ReadScreen(root, out w, out h));
                    info.Add("held 10 ppu");
                }
            }
            yield return null;
            yield return null;
            frames.Add(ReadScreen(root, out w, out h));
            info.Add("gameplay " + rig.Level + " ppu");
            Assert.AreEqual(0f, Vector3.Distance(rig.BaseFrame.Position, root.Pipeline.WorldCamera.transform.position), 1e-5f);
            Assert.GreaterOrEqual(frames.Count, 5, "wide, dissolves, held rung, gameplay: " + string.Join(", ", info));
            const int cols = 3, rows = 2, gap = 4;
            var sheet = new Texture2D(cols * w + (cols - 1) * gap, rows * h + (rows - 1) * gap, TextureFormat.RGBA32, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 24, 255);
            sheet.SetPixels32(fill);
            for (int k = 0; k < Mathf.Min(frames.Count, cols * rows); k++)
            {
                int cx = (k % cols) * (w + gap), cy = (rows - 1 - k / cols) * (h + gap);
                sheet.SetPixels32(cx, cy, w, h, frames[k]);
            }
            sheet.Apply();
            Directory.CreateDirectory(FrameCapture.OutputDir);
            var path = Path.Combine(FrameCapture.OutputDir, "render_zoom_steps.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.Destroy(sheet);
            File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_zoom_steps.txt"), string.Join("\n", info) + "\n");
            Debug.Log("[FrameCapture] " + path + " :: " + string.Join(" | ", info));
            Assert.IsTrue(File.Exists(path));

            // star night (Day 7): the lifted, sky-heavy wide frame; the shower keeps falling while paused
            root.Sky.ForceStarNight = true;
            yield return WaitGameplay(root);
            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            yield return WaitWide(root, 10);
            Assert.AreEqual(rig.ScenicStarFrame.Position.y, root.Pipeline.WorldCamera.transform.position.y, 1e-4f, "lifted star-night frame");
            root.CameraStepOverride = 0.05f;
            var sky = root.Sky;
            for (int i = 0; i < 900 && !(sky.MeteorActive && sky.Meteor.Level > 0.7f && sky.Meteor.Head.y > 220f); i++) yield return null;
            root.CameraStepOverride = 0f; // hold the frame for the capture
            yield return null;
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_scenic_starnight")));
            HideOtherCanvases(false);
        }
    }
}
