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
    /// Every ending as the player first sees it: the UI turns the WIDE scenic framing on for the whole Ending
    /// (ScenicViewChanged(true, false), no pause) and the ending scene plays out over the whole city. Captures each one
    /// (render_ending_wide_&lt;kind&gt;.png + the render_endings_wide.png sheet), then zooms back in and restarts: nothing of
    /// any ending may survive into the next run.
    /// </summary>
    public class RenderingEndingWideTests
    {
        static readonly FloorIndex Floor4 = FloorIndex.FromPhysicalNumber(4);
        const float Step = 0.05f;

        static readonly EndingKind[] Kinds =
        {
            EndingKind.Prosperous, EndingKind.Modest, EndingKind.Haunted,
            EndingKind.FourthFloor, EndingKind.WinterMiracle, EndingKind.SilentNight,
        };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var root = RenderingTestDriver.Rendering();
            if (root != null)
            {
                root.CameraStepOverride = null;
                if (root.Rig != null && root.Rig.InScenicView) GameServices.Events.Publish(new ScenicViewChanged(false, false));
            }
            HideOtherCanvases(false);
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

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

        static void WritePng(string name, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(FrameCapture.OutputDir);
            var path = Path.Combine(FrameCapture.OutputDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log("[FrameCapture] " + path);
        }

        /// <summary>Share of the frame's pixels that are pure black (an empty border would show up here).</summary>
        static float BlackShare(Color32[] px)
        {
            int n = 0;
            foreach (var c in px) if (c.r < 3 && c.g < 3 && c.b < 3) n++;
            return n / (float)px.Length;
        }

        /// <summary>Darkest column / row average along the frame edges: no empty strip at the city's edges.</summary>
        static float EdgeLuma(Color32[] px, int w, int h, bool column, int index)
        {
            float sum = 0f;
            int n = column ? h : w;
            for (int i = 0; i < n; i++)
            {
                var c = column ? px[i * w + index] : px[index * w + i];
                sum += c.r + c.g + c.b;
            }
            return sum / n;
        }

        [UnityTest]
        public IEnumerator EveryEnding_PlaysInTheWideFrame_ThenLeavesNothingBehind()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var rig = root.Rig;
            // a sticker on the real 4th floor, so the 4F ending has its F to drop
            RenderingTestDriver.EnterShopWithTips(100);
            Assert.AreEqual(StickerApplyResult.Applied, GameServices.Get<IShopService>().TryBuyAndApply(new StickerId("four_to_f"), Floor4));
            var day = GameServices.Get<IDayQuery>();
            int n = root.Lighting.FloorCount;
            Time.captureDeltaTime = Step;
            root.CameraStepOverride = Step;
            for (int i = 0; i < 4; i++) yield return null;
            HideOtherCanvases(true);

            var frames = new List<Color32[]>();
            int w = 0, h = 0;
            foreach (var kind in Kinds)
            {
                // what the flow does on an ending: EndingReached, the UI's wide framing on (the camera may still be wide
                // from the last ending: the scenic view only turns off between them)
                GameServices.Events.Publish(new EndingReached(kind, day.CurrentDay, 50, 500));
                GameServices.Events.Publish(new ScenicViewChanged(true, false));
                yield return null;
                Assert.AreEqual(kind, root.Ending.Active);
                for (int i = 0; i < 300 && rig.ZoomProgress < 1f; i++) yield return null;
                Assert.AreEqual(1f, rig.ZoomProgress, kind + ": wide");
                // the ending's beat in the wide frame (the UI holds 5 s / 8.5 s before the card)
                float until = kind == EndingKind.SilentNight ? 5f : kind == EndingKind.WinterMiracle ? 5.2f : 3.2f;
                for (int i = 0; i < 400 && root.Ending.Elapsed < until; i++) yield return null;
                if (kind == EndingKind.SilentNight)
                {
                    // the UI's 5 s beat: the whole lights-out has happened before the card covers the scene
                    Assert.IsTrue(root.Lighting.IsLit(new FloorIndex(0)), "the lobby (and its tree) stays lit");
                    for (int i = 1; i < n; i++) Assert.Less(root.Lighting.GetLevel(new FloorIndex(i)), 0.2f, "floor " + (i + 1) + " dark within the beat");
                }
                if (kind == EndingKind.WinterMiracle)
                    for (int i = 0; i < 300 && !root.Winter.SleighVisible; i++) yield return null;
                if (kind == EndingKind.FourthFloor)
                {
                    Assert.IsTrue(root.Building.IsStickerFallen(Floor4), "the F fell off in the wide frame");
                    Assert.AreEqual(Color.white, root.Building.GetSignTint(Floor4), "the 4th floor's number stays bright");
                    for (int i = 0; i < n; i++)
                        if (i != Floor4.Value) Assert.Less(root.Building.GetSignTint(new FloorIndex(i)).r, 0.5f, "power out: sign " + (i + 1) + " dimmed");
                    for (int i = 0; i < 60 && root.Lighting.GetLightIntensity(Floor4) < RenderingStyle.FloorLightIntensity * 0.8f; i++) yield return null;
                }
                yield return null; // the screen holds last frame's render
                var px = ReadScreen(root, out w, out h);
                frames.Add(px);
                WritePng("render_ending_wide_" + kind.ToString().ToLowerInvariant(), px, w, h);
                Assert.Less(BlackShare(px), 0.02f, kind + ": no black borders / empty sky");
                Assert.Greater(EdgeLuma(px, w, h, true, 0), 15f, kind + ": left edge has city");
                Assert.Greater(EdgeLuma(px, w, h, true, w - 1), 15f, kind + ": right edge has city");
                Assert.Greater(EdgeLuma(px, w, h, false, 0), 15f, kind + ": street along the bottom edge");
            }

            // contact sheet 3x2
            const int cols = 3, rows = 2, gap = 4;
            var sheet = new Color32[(cols * w + (cols - 1) * gap) * (rows * h + (rows - 1) * gap)];
            int sw = cols * w + (cols - 1) * gap, sh = rows * h + (rows - 1) * gap;
            for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(20, 20, 24, 255);
            for (int k = 0; k < frames.Count; k++)
            {
                int cx = (k % cols) * (w + gap), cy = (rows - 1 - k / cols) * (h + gap);
                for (int y = 0; y < h; y++)
                    System.Array.Copy(frames[k], y * w, sheet, (cy + y) * sw + cx, w);
            }
            WritePng("render_endings_wide", sheet, sw, sh);

            // the reveal plate follows the zoom back into the gameplay frame (whole texels at 11 ppu)
            GameServices.Events.Publish(new EndingReached(EndingKind.FourthFloor, day.CurrentDay, 50, 500));
            for (int i = 0; i < 200 && !root.Building.IsStickerFallen(Floor4); i++) yield return null;
            Assert.AreEqual(8, rig.Level, "revealed in the wide frame");
            var wide = root.Building.GetRevealSize(Floor4);
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            for (int i = 0; i < 300 && rig.ZoomProgress > 0f; i++) yield return null;
            yield return null;
            Assert.AreEqual(11, rig.Level);
            var near = root.Building.GetRevealSize(Floor4);
            Assert.AreEqual(wide.x * 8f / 11f, near.x, 1e-4f, "reveal plate keeps its texels in pixels (8 -> 11 ppu)");
            float texelPx = near.x * 11f / RenderingStyle.SignPixelsPerTexel;
            Assert.AreEqual(Mathf.Round(texelPx), texelPx, 1e-3f, "whole texels");

            // chimney smoke on the roof (gameplay frame): a stream of puffs, frozen with the clock
            Assert.Greater(root.Smoke.QuadCount, 4, "smoke puffs");
            var mouth = root.Facade.ChimneyTop;
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_chimney_smoke", mouth + new Vector3(-4f, -1.5f, 0f), mouth + new Vector3(3f, 3.5f, 0f), 6)));
            yield return RenderingTestDriver.Freeze();
            int rebuilds = root.Smoke.Rebuilds;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(rebuilds, root.Smoke.Rebuilds, "no smoke rebuilds while the clock is frozen");
            RenderingTestDriver.Unfreeze();
            Time.captureDeltaTime = Step;

            // restart: back to a normal night - no snow, decor, sleigh, reveal, ending lighting
            RenderingTestDriver.Session().RestartGame();
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(EndingKind.None, root.Ending.Active);
            Assert.IsFalse(root.Building.IsRevealed(Floor4));
            Assert.IsFalse(root.Winter.Snowing, "no ending snow");
            Assert.IsFalse(root.Winter.SleighVisible);
            Assert.IsFalse(root.Christmas.Shown, "no Christmas dressing");
            Assert.IsFalse(root.Winter.HeavyCover || root.Winter.DustCover, "no snow cover");
            Assert.AreEqual(Color.white, root.Building.HotelSignTint);
            for (int i = 0; i < n; i++) Assert.AreEqual(Color.white, root.Building.GetSignTint(new FloorIndex(i)), "sign " + (i + 1) + " back on");
            Assert.AreEqual(1f, root.Elevator.LightLevel, 1e-4f);
            // the cold grading eases out on the unscaled clock (real time in batch runs)
            for (int i = 0; i < 4000 && root.Winter.Mood > 0f; i++) yield return null;
            Assert.AreEqual(0f, root.Winter.Mood, 1e-3f, "winter mood gone");
            for (int i = 0; i < n; i++) Assert.IsTrue(root.Lighting.IsLit(new FloorIndex(i)), "relit " + (i + 1));
        }
    }
}
