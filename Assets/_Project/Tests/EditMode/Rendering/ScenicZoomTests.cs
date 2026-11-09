using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>
    /// Scenic view camera (ScenicViewChanged): the zoom walks whole pixels-per-unit rungs only, every frame on its own
    /// pixel grid, reaches the wide frame, comes back to EXACTLY the gameplay frame, and runs on the camera (unscaled)
    /// clock while the juice clock is stopped (the scenic view pauses the game).
    /// </summary>
    public class ScenicZoomTests
    {
        const int H = 360;
        const float Step = 1f / 60f;

        static CameraFrame Gameplay() => RenderingRoot.FitGameplayFrame(new HotelLayout(new HotelLayoutDef(), 9, 8));

        static void AssertOnGrid(CameraFrame f, string what)
        {
            float ppu = f.PixelsPerUnit;
            Assert.AreEqual(Mathf.Round(ppu), ppu, 1e-3f, what + ": whole pixels per unit");
            Assert.AreEqual(Mathf.Round(f.Position.x * ppu), f.Position.x * ppu, 2e-3f, what + ": centre x on the pixel grid");
            Assert.AreEqual(Mathf.Round(f.Position.y * ppu), f.Position.y * ppu, 2e-3f, what + ": centre y on the pixel grid");
        }

        static void AssertSameFrame(CameraFrame expected, CameraFrame actual, string what)
        {
            Assert.AreEqual(expected.OrthographicSize, actual.OrthographicSize, 1e-6f, what + ": size");
            Assert.AreEqual(0f, Vector3.Distance(expected.Position, actual.Position), 1e-6f, what + ": position");
        }

        [Test]
        public void Ladder_IsEveryWholePpu_BetweenGameplayAndWide()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            var levels = ScenicZoom.Levels(g, rig.WideFrame);
            Assert.AreEqual(11, levels[0], "gameplay frame: 11 ppu");
            Assert.AreEqual(8, levels[levels.Length - 1], "wide (title) frame: 8 ppu");
            for (int i = 1; i < levels.Length; i++) Assert.AreEqual(levels[i - 1] - 1, levels[i], "one ppu per rung");
            AssertSameFrame(g, ScenicZoom.LevelFrame(g, rig.WideFrame, 11, H), "first rung = gameplay frame");
            AssertSameFrame(rig.WideFrame, ScenicZoom.LevelFrame(g, rig.WideFrame, 8, H), "last rung = wide frame");
            float prevSize = 0f, prevTop = float.MinValue;
            foreach (int ppu in levels)
            {
                var f = ScenicZoom.LevelFrame(g, rig.WideFrame, ppu, H);
                AssertOnGrid(f, ppu + " ppu rung");
                Assert.Greater(f.OrthographicSize, prevSize, "each rung wider");
                Assert.GreaterOrEqual(CameraFraming.Top(f), prevTop - 1e-4f, "more sky each rung");
                prevSize = f.OrthographicSize;
                prevTop = CameraFraming.Top(f);
            }
            Assert.AreEqual(11, ScenicZoom.Level(g, rig.WideFrame, 0f, H));
            Assert.AreEqual(8, ScenicZoom.Level(g, rig.WideFrame, 1f, H));
        }

        [Test]
        public void ScenicOn_ZoomsOutOnTheCameraClock_WhilePaused_EveryStepOnTheGrid()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            rig.SetScenic(true, false, false);
            var held = new Dictionary<int, int>();
            int prevLevel = rig.Level, frames = 0;
            while (rig.ZoomProgress < 1f && frames < 600)
            {
                rig.Tick(0f, Step); // paused: juice clock 0, camera clock runs
                frames++;
                AssertOnGrid(rig.Current, "frame " + frames);
                Assert.LessOrEqual(rig.Level, prevLevel, "zoom out never steps back in");
                Assert.GreaterOrEqual(prevLevel - rig.Level, 0);
                Assert.LessOrEqual(prevLevel - rig.Level, 1, "one rung at a time");
                Assert.AreEqual(rig.Level != prevLevel, rig.LevelStepped, "LevelStepped flags exactly the rung changes");
                held[rig.Level] = held.TryGetValue(rig.Level, out var n) ? n + 1 : 1;
                prevLevel = rig.Level;
            }
            Assert.AreEqual(1f, rig.ZoomProgress, "reached the wide frame");
            Assert.AreEqual(CameraRig.ScenicOutSeconds, frames * Step, 2f * Step, "zoom-out duration");
            Assert.AreEqual(8, rig.Level);
            CollectionAssert.AreEquivalent(new[] { 11, 10, 9, 8 }, held.Keys, "every rung shown");
            foreach (var kv in held) Assert.GreaterOrEqual(kv.Value, 6, kv.Key + " ppu rung held long enough to read (>= 0.1 s)");
            // wide frame + whole-pixel pan
            var wide = rig.WideFrame;
            Assert.AreEqual(wide.OrthographicSize, rig.Current.OrthographicSize, 1e-6f);
            Assert.AreEqual(wide.Position.y, rig.Current.Position.y, 1e-5f);
            Assert.AreEqual(rig.OffsetPixels.x / wide.PixelsPerUnit, rig.Current.Position.x - wide.Position.x, 1e-4f, "pan = whole pixels");
        }

        [Test]
        public void ScenicHold_PansSlowly_InWholePixels()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            rig.SetScenic(true, false, false);
            var seen = new HashSet<int>();
            int prev = 0;
            for (int i = 0; i < 60 * 12; i++)
            {
                rig.Tick(0f, Step);
                AssertOnGrid(rig.Current, "pan frame " + i);
                Assert.LessOrEqual(Mathf.Abs(rig.OffsetPixels.x - prev), 1, "gentle: at most one pixel per frame");
                prev = rig.OffsetPixels.x;
                seen.Add(rig.OffsetPixels.x);
            }
            Assert.Greater(seen.Count, 8, "pans");
            Assert.LessOrEqual(Mathf.Abs(rig.OffsetPixels.x), Mathf.CeilToInt(CameraRig.ScenicPanPixels));
        }

        [Test]
        public void ScenicOff_ReturnsExactlyToTheGameplayFrame()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            rig.SetScenic(true, false, false);
            for (int i = 0; i < 60 * 5; i++) rig.Tick(0f, Step);
            rig.SetScenic(false, false, false);
            int frames = 0, prevLevel = rig.Level;
            while (rig.ZoomProgress > 0f && frames < 600)
            {
                rig.Tick(0f, Step);
                frames++;
                AssertOnGrid(rig.Current, "zoom-in frame " + frames);
                Assert.GreaterOrEqual(rig.Level, prevLevel, "zoom in never steps back out");
                prevLevel = rig.Level;
            }
            Assert.AreEqual(CameraRig.ScenicInSeconds, frames * Step, 2f * Step, "zoom-in duration");
            AssertSameFrame(g, rig.Current, "back on the gameplay frame");
            Assert.AreEqual(Vector2Int.zero, rig.OffsetPixels, "no pan left over");
            Assert.IsFalse(rig.InScenicView);
            rig.Tick(0.5f, 0.5f);
            AssertSameFrame(g, rig.Current, "stays there");
        }

        [Test]
        public void Establishing_CutsToWide_HoldsAndPans_ThenZoomsIn()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            rig.SetScenic(true, true, false);
            rig.Tick(0f, Step);
            Assert.AreEqual(8, rig.Level, "starts AT the wide frame");
            Assert.IsTrue(rig.Establishing);
            var start = rig.OffsetPixels.x;
            for (int i = 0; i < 150; i++) rig.Tick(0f, Step); // ~2.5 s hold
            Assert.AreEqual(8, rig.Level, "holds wide");
            Assert.AreNotEqual(start, rig.OffsetPixels.x, "slow pan during the hold");
            rig.SetScenic(false, true, false);
            int frames = 0;
            var levels = new List<int>();
            while (rig.ZoomProgress > 0f && frames < 600)
            {
                rig.Tick(0f, Step);
                frames++;
                if (levels.Count == 0 || levels[levels.Count - 1] != rig.Level) levels.Add(rig.Level);
            }
            CollectionAssert.AreEqual(new[] { 8, 9, 10, 11 }, levels, "zooms into the hotel rung by rung");
            Assert.That(frames * Step, Is.InRange(0.8f, 1.0f), "0.8-1.0 s ease-in-out");
            AssertSameFrame(g, rig.Current, "exactly the gameplay frame");
        }

        static float StarNightLift(CameraFrame plain, CameraFrame star) => star.Position.y - plain.Position.y;

        [Test]
        public void StarNight_WideFrame_IsSkyHeavier_AndKeepsTheStreet()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            var plain = rig.ScenicFrame;
            var star = rig.ScenicStarFrame;
            AssertOnGrid(star, "star-night wide frame");
            Assert.AreEqual(plain.OrthographicSize, star.OrthographicSize, 1e-6f, "same zoom (8 ppu meshes)");
            Assert.AreEqual(CameraRig.StarNightLiftPixels / star.PixelsPerUnit, CameraFraming.Top(star) - CameraFraming.Top(plain), 1e-4f);
            Assert.Greater(StarNightLift(plain, star), 0f, "sky-heavier: the frame rises");
            Assert.LessOrEqual(CameraFraming.Bottom(star), CameraFraming.Bottom(g) + 1e-4f, "at least the gameplay frame's street");
            float nearLane = RenderingStyle.RoadY + star.Shear.y * (RenderingStyle.NearLaneZ - star.ShearDepth);
            Assert.Less(CameraFraming.Bottom(star), nearLane, "near-lane cars stay whole");
            Assert.Greater(CameraFraming.Top(star), CameraFraming.Top(g) + 5f, "lots more sky than the gameplay frame");

            rig.SetScenic(true, false, true);
            for (int i = 0; i < 100; i++) rig.Tick(0f, Step);
            Assert.AreEqual(star.Position.y, rig.WideFrame.Position.y, 1e-6f, "star night heads for the lifted frame");
            Assert.AreEqual(star.Position.y, rig.Current.Position.y, 1e-5f);
        }

        [Test]
        public void JuiceClockAlone_NeverZooms_AndTitleOverridesScenic()
        {
            var g = Gameplay();
            var rig = new CameraRig(null, g, H);
            rig.SetScenic(true, false, false);
            for (int i = 0; i < 30; i++) rig.Tick(Step);
            AssertSameFrame(g, rig.Current, "no camera clock, no zoom");

            for (int i = 0; i < 30; i++) rig.Tick(0f, Step);
            Assert.Greater(rig.ZoomProgress, 0f);
            rig.SetTitle(true);
            Assert.IsFalse(rig.InScenicView, "the title drops the scenic view");
            rig.Tick(0f, Step);
            Assert.IsFalse(rig.LevelStepped, "title cut is not a dissolve step");
            Assert.AreEqual(rig.TitleFrame.OrthographicSize, rig.Current.OrthographicSize, 1e-6f);
            rig.SetScenic(true, false, false);
            Assert.IsFalse(rig.ScenicOn, "ignored on the title");
            rig.SetTitle(false);
            rig.Tick(0f, Step);
            AssertSameFrame(g, rig.Current, "title off: gameplay frame");
            Assert.IsFalse(rig.LevelStepped);
        }

        [Test]
        public void CameraStep_IsCapped_SoAHitchNeverSkipsTheZoom()
        {
            var rig = new CameraRig(null, Gameplay(), H);
            rig.SetScenic(true, false, false);
            rig.Tick(0f, 5f);
            Assert.AreEqual(CameraRig.MaxCameraStep / CameraRig.ScenicOutSeconds, rig.ZoomProgress, 1e-5f);
        }

        [Test]
        public void Dissolve_BayerOrder_CoversEveryPixelOnce()
        {
            var seen = new HashSet<int>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int b = PixelGlow.BayerIndex(x, y);
                    Assert.That(b, Is.InRange(0, 15));
                    Assert.IsTrue(seen.Add(b), "unique threshold per 4x4 cell");
                    Assert.AreEqual(b, PixelGlow.BayerIndex(x + 4, y + 8), "tiles every 4 pixels");
                    Assert.IsFalse(PixelGlow.DissolveShowsNew(x, y, 0f));
                    Assert.IsTrue(PixelGlow.DissolveShowsNew(x, y, 1f));
                }
            int half = 0;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (PixelGlow.DissolveShowsNew(x, y, 0.5f)) half++;
            Assert.AreEqual(8, half, "half the pixels at mix 0.5");
        }
    }
}
