using System.Collections;
using System.Collections.Generic;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// "Not default cubes": the hotel's pixel masonry / roof / blade sign, patterned neighbour walls and the star-night
    /// meteor shower on the real Main scene. Pixel alignment in the gameplay and title frames, draw-call budget, shower only
    /// on star night at a steady rate, meteors depth-tested between the rows of buildings. Captures render_facade_detail
    /// (+ _zoom), render_rooftop_zoom, render_title_facade, render_star_night_meteors_1..3; writes render_facade_stats.txt.
    /// </summary>
    public class RenderingFacadeTests
    {
        /// <summary>Day frame with HUD (was ~781-783 before the facade / meteor work): + ~15 at most.</summary>
        const int DayDrawBudget = 800;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var root = RenderingTestDriver.Rendering();
            if (root != null && root.Sky != null) root.Sky.ForceStarNight = null;
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static IWorldLayout Layout => GameServices.Get<IWorldLayout>();

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

        /// <summary>Largest distance (target pixels) of any vertex of the mesh from a whole pixel corner of the camera.</summary>
        static float WorstPixelOffset(Camera cam, MeshFilter mf)
        {
            var m = mf.transform.localToWorldMatrix;
            float worst = 0f;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = cam.WorldToViewportPoint(m.MultiplyPoint3x4(v));
                float x = p.x * RenderingStyle.PixelWidth, y = p.y * RenderingStyle.PixelHeight;
                worst = Mathf.Max(worst, Mathf.Abs(x - Mathf.Round(x)), Mathf.Abs(y - Mathf.Round(y)));
            }
            return worst;
        }

        /// <summary>A patterned wall (as the city builds them) in the active frame: tile quads on whole pixels, 1 texel = 1 px.</summary>
        static void AssertPatternFrontOnPixels(RenderingRoot root, Camera cam, string frame)
        {
            var grid = root.Rig.Current.Grid;
            var b = new PaletteMeshBuilder(root.Palette);
            var blk = CityView.Blocks[1];
            var tile = root.Palette.Tile(CityView.PatternOf(blk), blk.Color);
            b.PatternFront(blk.X0, 0f, blk.X1, 9f, blk.Z, tile, grid);
            var mesh = b.Build("pattern probe");
            var go = new GameObject("Pattern Probe");
            try
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                Assert.Greater(mesh.vertexCount, 40, "many tile quads");
                Assert.Less(WorstPixelOffset(cam, mf), 0.02f, frame + ": tile quads on whole pixels");
                var v = mesh.vertices;
                var uv = mesh.uv;
                for (int i = 0; i + 2 < v.Length; i += 4)
                {
                    var a = cam.WorldToViewportPoint(v[i]);
                    var c = cam.WorldToViewportPoint(v[i + 2]);
                    float pxW = (c.x - a.x) * RenderingStyle.PixelWidth, pxH = (c.y - a.y) * RenderingStyle.PixelHeight;
                    Assert.AreEqual(pxW, (uv[i + 2].x - uv[i].x) * PaletteAtlas.Size, 0.02f, frame + ": texel width = pixel width");
                    Assert.AreEqual(pxH, (uv[i + 2].y - uv[i].y) * PaletteAtlas.Size, 0.02f, frame + ": texel height = pixel height");
                }
            }
            finally
            {
                Object.Destroy(go);
                Object.Destroy(mesh);
            }
        }

        static void AssertPatternsTiled(RenderAssets assets, float ppu, string frame)
        {
            Assert.GreaterOrEqual(assets.PatternMaterials.Count, 3, "brick, stone, dentil");
            foreach (var m in assets.PatternMaterials)
            {
                var tex = m.GetTexture("_BaseMap");
                Assert.IsNotNull(tex, m.name + " has its pattern");
                Assert.AreEqual(FilterMode.Point, tex.filterMode, m.name + " crisp");
                var s = m.GetTextureScale("_BaseMap");
                Assert.AreEqual(ppu / tex.width, s.x, 1e-4f, frame + ": " + m.name + " one texel = one pixel (x)");
                Assert.AreEqual(ppu / tex.height, s.y, 1e-4f, frame + ": " + m.name + " one texel = one pixel (y)");
            }
        }

        [UnityTest]
        public IEnumerator Facade_PixelPatterns_Details_AlignedInBothFrames_AndWithinBudget()
        {
            var report = new List<string>();
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            {
                yield return RenderingTestDriver.LoadMain();
                Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
                var root = RenderingTestDriver.Rendering();
                var cam = root.Pipeline.WorldCamera;
                var facade = root.Facade;
                Assert.IsNotNull(facade.DetailRenderer, "pixel details");
                Assert.AreSame(root.Palette.Material, facade.DetailRenderer.sharedMaterial, "details share the palette");
                Assert.LessOrEqual(facade.Renderers.Count, 16, "facade merged per material");
                Assert.Greater(facade.LitParts, 200, "quoins, bands, cornice, parapet, roof props");
                Assert.GreaterOrEqual(facade.PigeonCount, 4, "pigeons on the parapet");
                Assert.Greater(facade.BladeSignPixels.height, 40, "vertical HOTEL blade sign");
                Assert.GreaterOrEqual(root.Palette.TileCount, 5, "neighbour walls: brick, panels, tiles, stone, far bands");
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 20; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 5; i++) yield return null;
                report.Add("day (HUD) drawCalls=" + draws.LastValue + " facadeRenderers=" + facade.Renderers.Count + " litParts=" + facade.LitParts +
                           " tiles=" + root.Palette.TileCount + " tileRows=" + root.Palette.TileRows + " palette texels=" + root.Palette.Used);
                Assert.LessOrEqual(draws.LastValue, DayDrawBudget, "draw-call budget (day frame with HUD)");

                // gameplay frame: details and the city on whole pixels, lit patterns tiled 1 texel = 1 px
                float ppu = root.Rig.Current.PixelsPerUnit;
                Assert.AreEqual(Mathf.RoundToInt(ppu), facade.DetailPixelsPerUnit);
                Assert.Less(WorstPixelOffset(cam, facade.DetailRenderer.GetComponent<MeshFilter>()), 0.02f, "details on whole pixels (gameplay)");
                AssertPatternFrontOnPixels(root, cam, "gameplay");
                AssertPatternsTiled(root.Assets, ppu, "gameplay");
                RenderingTestDriver.Unfreeze();

                // title frame (zoomed out): its own detail mesh, patterns retiled, still whole pixels
                yield return RenderingTestDriver.LoadTitle();
                root = RenderingTestDriver.Rendering();
                cam = root.Pipeline.WorldCamera;
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 20; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 3; i++) yield return null;
                float tppu = root.Rig.Current.PixelsPerUnit;
                Assert.Less(tppu, ppu, "title is zoomed out");
                Assert.AreEqual(Mathf.RoundToInt(tppu), root.Facade.DetailPixelsPerUnit, "title detail mesh");
                Assert.Less(WorstPixelOffset(cam, root.Facade.DetailRenderer.GetComponent<MeshFilter>()), 0.02f, "details on whole pixels (title)");
                AssertPatternFrontOnPixels(root, cam, "title");
                AssertPatternsTiled(root.Assets, tppu, "title");
                report.Add("title drawCalls=" + draws.LastValue + " ppu " + ppu + " -> " + tppu);
            }
            Directory.CreateDirectory(FrameCapture.OutputDir);
            File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_facade_stats.txt"), string.Join("\n", report) + "\n");
            foreach (var line in report) TestContext.WriteLine(line);
        }

        [UnityTest]
        public IEnumerator MeteorShower_OnlyOnStarNight_SteadyRate_BetweenTheBuildingRows()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var sky = root.Sky;
            Assert.IsTrue(sky.MeteorsInFront, "additive meteor layer");
            var mat = sky.MeteorRenderer.sharedMaterial;
            Assert.AreEqual((float)CompareFunction.LessEqual, mat.GetFloat("_ZTest"), "meteors are depth-tested (hidden by the hotel and the near row)");
            Assert.AreEqual((float)CompareFunction.Always, sky.WishRenderer.sharedMaterial.GetFloat("_ZTest"), "wish stars stay over everything");
            // the layer sits between the rows: behind the hotel and every near block, in front of every far tower
            float nearBack = Layout.Depth * 0.5f + 0.3f, farFront = float.MaxValue;
            foreach (var b in CityView.Blocks)
            {
                if (b.Z > 6f) farFront = Mathf.Min(farFront, b.Z);
                else nearBack = Mathf.Max(nearBack, b.Z + 4.5f);
            }
            Assert.Greater(SkyView.MeteorDepth, nearBack, "behind the near row and the hotel");
            Assert.Less(SkyView.MeteorDepth, farFront, "in front of the far towers");

            // ordinary night: no meteors at all
            Time.captureDeltaTime = 0.1f;
            int before = sky.MeteorsSeen;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                Assert.IsFalse(sky.MeteorActive);
                Assert.IsFalse(sky.MeteorRenderer.enabled, "no meteor draw call on an ordinary night");
            }
            Assert.AreEqual(before, sky.MeteorsSeen);

            // star night: a steady shower (20 s of sky time)
            sky.ForceStarNight = true;
            int seen0 = sky.MeteorsSeen, framesWith = 0, most = 0;
            for (int i = 0; i < 200; i++)
            {
                yield return null;
                if (sky.MeteorActive) framesWith++;
                most = Mathf.Max(most, sky.ActiveMeteors);
                if (sky.MeteorRenderer.enabled) Assert.IsTrue(sky.MeteorActive, "meteor layer drawn only while meteors fly");
            }
            int seen = sky.MeteorsSeen - seen0;
            TestContext.WriteLine("star night: " + seen + " meteors in 20 s, in the sky " + framesWith + "/200 frames, at most " + most + " at once, fireballs so far " + sky.FireballsSeen);
            Assert.That(seen, Is.InRange(14, 34), "about one meteor a second");
            Assert.Greater(framesWith, 90, "the shower keeps falling");
            Assert.LessOrEqual(sky.Root.GetComponentsInChildren<Renderer>(true).Length, 5, "sky = at most 5 renderers");
        }

        [UnityTest]
        public IEnumerator Capture_FacadeDetail_Rooftop_TitleFacade_StarNightMeteors()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 30; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            for (int i = 0; i < 3; i++) yield return null;
            var l = Layout;
            float H = l.FloorCount * l.FloorHeight, z0 = -l.Depth * 0.5f;
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_facade_detail")));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_facade_detail_zoom",
                new Vector3(-13f, 6f, z0), new Vector3(-2f, 17f, z0), 5)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_facade_side_zoom",
                new Vector3(5f, RenderingStyle.RoadY - 0.6f, z0), new Vector3(14f, 7f, z0), 5)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_rooftop_zoom",
                new Vector3(-10f, H - 1.2f, z0), new Vector3(10.5f, H + 3.4f, z0), 4)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_neighbour_walls_zoom",
                new Vector3(-34f, 2f, z0), new Vector3(-14f, 14f, z0), 4)));
            HideOtherCanvases(false);

            // star night, gameplay frame: a few frames of the shower (sky time runs, the simulation too)
            root.Sky.ForceStarNight = true;
            RenderingTestDriver.Unfreeze();
            int shot = 0, brightHeads = 0;
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 1200 && shot < 3; i++)
            {
                yield return null;
                // a frame with a bright meteor in open sky (most of the gameplay sky is hotel / HUD)
                bool open = false;
                foreach (var m in root.Sky.ActiveMeteorPoses)
                    if (m.Level > 0.75f && m.Head.y > 200f && (m.Head.x < 205f || m.Head.x > 445f)) open = true;
                if (!open || (shot == 0 && root.Sky.ActiveMeteors < 2)) continue;
                yield return RenderingTestDriver.Freeze();
                HideOtherCanvases(true);
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_star_night_meteors_" + (shot + 1))));
                var f = root.Rig.Current;
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_star_night_meteors_" + (shot + 1) + "_sky",
                    SkyView.ViewportToWorld(f, new Vector2(0f, 0.45f), z0), SkyView.ViewportToWorld(f, new Vector2(1f, 1f), z0), 2)));
                HideOtherCanvases(false);
                foreach (var m in root.Sky.ActiveMeteorPoses)
                    if (TargetPeak(new Vector2(m.Head.x / 640f, m.Head.y / 360f), 1) > 0.55f) brightHeads++;
                shot++;
                RenderingTestDriver.Unfreeze();
                Time.captureDeltaTime = 0.05f;
                for (int k = 0; k < 9; k++) yield return null;
            }
            Assert.AreEqual(3, shot, "three meteor frames");
            Assert.Greater(brightHeads, 0, "bright meteor heads in the sky");
            root.Sky.ForceStarNight = null;

            // title frame: the whole facade, zoomed out
            yield return RenderingTestDriver.LoadTitle();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_title_facade")));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_title_facade_zoom",
                new Vector3(-13f, H - 6f, z0), new Vector3(13f, H + 4f, z0), 4)));
            HideOtherCanvases(false);
        }

        /// <summary>Brightest luminance in the world target (what the camera drew) within +-r pixels of a viewport point.</summary>
        static float TargetPeak(Vector2 viewport, int r)
        {
            var rt = RenderingTestDriver.Rendering().Pipeline.Target;
            int cx = Mathf.FloorToInt(viewport.x * rt.width), cy = Mathf.FloorToInt(viewport.y * rt.height);
            int x0 = Mathf.Clamp(cx - r, 0, rt.width - 1), y0 = Mathf.Clamp(cy - r, 0, rt.height - 1);
            int w = Mathf.Min(rt.width - x0, 2 * r + 1), h = Mathf.Min(rt.height - y0, 2 * r + 1);
            var prev = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                tex.Apply();
                float best = 0f;
                foreach (var c in tex.GetPixels32()) best = Mathf.Max(best, (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f);
                return best;
            }
            finally
            {
                RenderTexture.active = prev;
                Object.Destroy(tex);
            }
        }
    }
}
