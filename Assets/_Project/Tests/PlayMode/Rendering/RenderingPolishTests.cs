using System.Collections;
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
    /// Player-feedback polish on the real Main scene: Day 5 rain + lightning, original-floor marks on sticker signs.
    /// Captures render_rain_day, render_lightning, render_original_floor_marks.
    /// </summary>
    public class RenderingPolishTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var root = RenderingTestDriver.Rendering();
            if (root != null && root.Weather != null) root.Weather.ForceRain = null;
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [UnityTest]
        public IEnumerator Rain_OnlyOnTheRainyDay_OvercastSky_AndCheap()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var w = root.Weather;
            var cam = root.Pipeline.WorldCamera;
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsRainy, "day 1 is dry");
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.IsFalse(w.Raining);
                Assert.IsFalse(w.RainRenderer.enabled || w.PuddleRenderer.enabled || w.BoltRenderer.enabled, "no weather renderers on a dry night");
            }
            Assert.AreEqual(RenderingStyle.Background, cam.backgroundColor);
            Assert.IsTrue(root.Sky.StarRenderer.enabled, "stars on a clear night");

            w.ForceRain = true; // what IDayQuery.IsRainy does on Day 5
            yield return null;
            Assert.IsTrue(w.Raining);
            Assert.IsTrue(w.RainRenderer.enabled && w.PuddleRenderer.enabled, "rain + wet street");
            Assert.IsFalse(root.Sky.StarRenderer.enabled, "overcast: no moon and stars");
            Assert.Greater(w.RainQuadCount, WeatherSchedule.DropCount, "streaks + splashes");
            Assert.Less(w.RainQuadCount, 1300, "cheap: under ~1k quads");
            Assert.LessOrEqual(w.Root.GetComponentsInChildren<Renderer>(true).Length, 3);
            Assert.GreaterOrEqual(w.RainRenderer.sharedMaterial.renderQueue, 3000, "rain added over the world");
            Assert.That(w.HotelPixels.width, Is.InRange(100f, 640f), "hotel rectangle known (rain is lighter there)");

            // rain moves with the juice clock, freezes with the pause
            yield return RenderingTestDriver.Freeze();
            int q = w.RainQuadCount;
            w.ForceRain = false;
            yield return null;
            Assert.IsFalse(w.Raining);
            Assert.IsFalse(w.RainRenderer.enabled || w.PuddleRenderer.enabled);
            Assert.IsTrue(root.Sky.StarRenderer.enabled, "clear again");
            Assert.AreEqual(RenderingStyle.Background, cam.backgroundColor, "sky colour restored");
            Assert.Greater(q, 0);
        }

        [UnityTest]
        public IEnumerator Lightning_FlashesTheSkyOnly_Moderately_WithABolt_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var w = root.Weather;
            var cam = root.Pipeline.WorldCamera;
            w.ForceRain = true;
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_rain_day")));
            HideOtherCanvases(false);
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), SkyView.FxDepth);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_rain_street_zoom", W(0.0f, 0.0f), W(0.45f, 0.3f), 3)));
            RenderingTestDriver.Unfreeze();

            // wait for a strike at its peak (one comes every 8-20 s of juice time)
            Time.captureDeltaTime = 0.03f;
            int strikes = w.StrikesSeen;
            float maxLevel = 0f;
            for (int i = 0; i < 900 && !(w.LightningLevel > 0.95f); i++)
            {
                yield return null;
                maxLevel = Mathf.Max(maxLevel, w.LightningLevel);
            }
            Assert.Greater(w.LightningLevel, 0.95f, "a strike within 27 s");
            Assert.Greater(w.StrikesSeen, strikes);
            yield return RenderingTestDriver.Freeze();
            Assert.IsTrue(w.BoltRenderer.enabled, "bolt drawn during the flash");
            Assert.Greater(w.BoltQuadCount, 20);
            Assert.Greater(Luminance(cam.backgroundColor), Luminance(WeatherView.RainSky) + 0.15f, "the sky brightens");
            Assert.Less(Luminance(cam.backgroundColor), 0.45f, "moderately (not white)");
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_lightning")));
            HideOtherCanvases(false);
            RenderingTestDriver.Unfreeze();

            // the flash dies away within a second; the bolt goes with it
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(0f, w.LightningLevel, "single flash, over");
            Assert.IsFalse(w.BoltRenderer.enabled);
            Assert.AreEqual(WeatherView.RainSky, cam.backgroundColor);
        }

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

        [UnityTest]
        public IEnumerator OriginalFloorMarks_AppearWithStickers_AndGoWhenTheLabelReverts()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var building = GameServices.Get<IBuildingQuery>();
            var shop = GameServices.Get<IShopService>();
            var floor4 = FloorIndex.FromPhysicalNumber(4);
            for (int i = 0; i < building.FloorCount; i++)
                Assert.IsNull(root.Building.GetSignOriginalMark(new FloorIndex(i)), "no marks before any sticker");
            float plainWidth = root.Building.GetSign(floor4).GetComponentInChildren<MeshRenderer>().bounds.size.x;

            // real sticker through the shop: "4" -> "F", the old 4 struck out beside it
            RenderingTestDriver.EnterShopWithTips(100);
            Assert.AreEqual(StickerApplyResult.Applied, shop.TryBuyAndApply(new StickerId("four_to_f"), floor4));
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 12; i++) yield return null; // sticker slap animation settles
            Assert.IsFalse(root.Building.IsSignAnimating(floor4));
            Assert.AreEqual("F", root.Building.GetSignText(floor4));
            Assert.AreEqual("4", root.Building.GetSignOriginalMark(floor4), "original 4 marked beside the F sticker");
            float markedWidth = root.Building.GetSign(floor4).GetComponentInChildren<MeshRenderer>().bounds.size.x;
            Assert.Greater(markedWidth, plainWidth, "the mark strip widens the sign");
            Assert.Less(markedWidth, plainWidth * 1.8f, "but stays small");

            // the label goes back to the original (one-day stickers expire): FloorLabelChanged -> plain sign again
            GameServices.Events.Publish(new FloorLabelChanged(floor4, new FloorLabel("F"), building.GetOriginalLabel(floor4)));
            yield return null;
            Assert.AreEqual("4", root.Building.GetSignText(floor4));
            Assert.IsNull(root.Building.GetSignOriginalMark(floor4), "mark gone with the sticker");
            Assert.AreEqual(plainWidth, root.Building.GetSign(floor4).GetComponentInChildren<MeshRenderer>().bounds.size.x, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Neighbours_HaveShopsSignsAndRooftops_InTheOneCityMesh_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var d = root.City.Dressing;
            Assert.IsNotNull(d);
            Assert.GreaterOrEqual(d.SignCount, 10, "neon signs on the shops and the billboard");
            Assert.Greater(d.NeonTexelCount, 30);
            Assert.AreEqual(1, root.City.Root.GetComponentsInChildren<Renderer>().Length, "still one city draw call");
            Assert.Less(root.Palette.Used, PaletteAtlas.Size * PaletteAtlas.Size / 2, "palette has room left");

            // the neon hums through palette texels: uploads, no rebuilds
            int uploads = root.Palette.Uploads;
            var mesh = root.City.Renderer.GetComponent<MeshFilter>().sharedMesh;
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            Assert.Greater(root.Palette.Uploads, uploads);
            Assert.AreSame(mesh, root.City.Renderer.GetComponent<MeshFilter>().sharedMesh);

            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_neighbours")));
            HideOtherCanvases(false);
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), 0f);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_neighbours_left", W(0f, 0f), W(0.3f, 0.75f), 3)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_neighbours_right", W(0.68f, 0f), W(1f, 0.75f), 3)));
            RenderingTestDriver.Unfreeze();

            // the wide title frame shows the whole street
            yield return RenderingTestDriver.LoadTitle();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 10; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_neighbours_title")));
            HideOtherCanvases(false);
        }

        static float ImageBrightness(string path)
        {
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)));
                double sum = 0;
                foreach (var c in tex.GetPixels32()) sum += c.r + c.g + c.b;
                return (float)(sum / (tex.width * tex.height * 765.0));
            }
            finally { Object.Destroy(tex); }
        }

        // ------------------------------------------------------------------ banded pixel lighting

        /// <summary>Banded light alone (PixelGlow pass LightOnly) as band indices per target pixel (0 = no light).</summary>
        static int[] LightBands(PixelGlow glow, out int width, out int height, out int[] levels)
        {
            width = RenderingStyle.PixelWidth;
            height = RenderingStyle.PixelHeight;
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
            });
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            try
            {
                glow.RenderLightOnly(rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                var px = tex.GetPixels32();
                // band brightness = max channel (hue * level): cluster the distinct values (1 LSB of rounding)
                var values = new System.Collections.Generic.SortedSet<int>();
                foreach (var c in px) { int m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); if (m > 0) values.Add(m); }
                var clusters = new System.Collections.Generic.List<int>();
                foreach (int v in values) if (clusters.Count == 0 || v - clusters[clusters.Count - 1] > 2) clusters.Add(v);
                levels = clusters.ToArray();
                var bands = new int[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    int m = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                    if (m == 0) continue;
                    int k = 0;
                    while (k + 1 < clusters.Count && m >= clusters[k + 1] - 2) k++;
                    bands[i] = k + 1;
                }
                return bands;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tex);
            }
        }

        static Color32[] ReadTarget(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                return tex.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                Object.Destroy(tex);
            }
        }

        /// <summary>2x2 checkerboards (a b / b a, a != b) and pixels whose 4 neighbours all differ from them.</summary>
        static void Speckle(int[] bands, int w, int h, out int checkers, out int isolated, out int lit)
        {
            checkers = isolated = lit = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = bands[y * w + x];
                    if (a != 0) lit++;
                    if (x + 1 < w && y + 1 < h)
                    {
                        int b = bands[y * w + x + 1], c = bands[(y + 1) * w + x], d = bands[(y + 1) * w + x + 1];
                        if (a == d && b == c && a != b) checkers++;
                    }
                    if (a != 0 && x > 0 && y > 0 && x + 1 < w && y + 1 < h &&
                        bands[y * w + x - 1] != a && bands[y * w + x + 1] != a && bands[(y - 1) * w + x] != a && bands[(y + 1) * w + x] != a)
                        isolated++;
                }
        }

        [UnityTest]
        public IEnumerator Lighting_IsFlatBands_NoDither_StableUnderPan_TogglesOff_AndCheap_ThenCaptures()
        {
            var report = new System.Collections.Generic.List<string>();
            using (var draws = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count"))
            {
                yield return RenderingTestDriver.LoadMain();
                Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
                var root = RenderingTestDriver.Rendering();
                var glow = root.Pipeline.Glow;
                Assert.IsNotNull(glow);
                Assert.IsTrue(glow.Available, "light shader in Resources/Rendering/Shaders");
                Assert.AreEqual(PixelGlow.DefaultOn, glow.Enabled, "PixelGlow.DefaultOn decides");
                glow.Enabled = true;
                Assert.AreSame(glow.LitTexture, root.Pipeline.Screen.Texture, "pixel screen shows the lit target");
                Assert.AreEqual(RenderingStyle.PixelWidth / PixelGlow.Downsample + 1, glow.GlowTexture.width, "quarter resolution (+1 for the grid offset)");
                Assert.GreaterOrEqual(glow.Pools.Count, CityView.LampXs.Length + 1, "authored pools: lamps, shops, HOTEL roof");
                Assert.Greater(glow.VisiblePools, 0, "some pools on screen");
                Time.captureDeltaTime = 0.1f;
                int passes = glow.Passes;
                for (int i = 0; i < 10; i++) yield return null;
                Assert.Greater(glow.Passes, passes, "runs every frame while on");

                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 3; i++) yield return null;
                report.Add("lighting on  drawCalls=" + draws.LastValue + " pools=" + glow.Pools.Count + " visible=" + glow.VisiblePools);
                HideOtherCanvases(true);
                string on = FrameCapture.Capture("render_day_lighting");
                Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), 0f);
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_light_bands", W(0f, 0f), W(0.42f, 0.5f), 4)));
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_light_bands_hotel", W(0.3f, 0.62f), W(0.72f, 1f), 4)));

                // the light is flat bands: at most BandCount levels, no checkerboard / dither speckle
                var bands = LightBands(glow, out int w, out int h, out var levels);
                Speckle(bands, w, h, out int checkers, out int isolated, out int lit);
                report.Add("bands levels=[" + string.Join(",", levels) + "] lit=" + lit + " checkers=" + checkers + " isolated=" + isolated);
                Assert.That(levels.Length, Is.InRange(2, PixelGlow.BandCount), "2..4 flat band levels");
                Assert.Greater(lit, w * h / 100, "halos and pools visible");
                Assert.Less(lit, w * h / 2, "subtle: most of the frame unlit");
                Assert.LessOrEqual(checkers, 2 + lit / 2000, "no checkerboard dither");
                Assert.LessOrEqual(isolated, 4 + lit / 1000, "no speckle");

                // a halo around the HOTEL sign: stepped rings
                var hotel = root.Pipeline.WorldCamera.WorldToViewportPoint(root.Building.HotelSign.position);
                int hx = Mathf.RoundToInt(hotel.x * w), hy = Mathf.RoundToInt(hotel.y * h);
                var ring = new System.Collections.Generic.HashSet<int>();
                for (int y = hy - 30; y <= hy + 30; y++)
                    for (int x = hx - 60; x <= hx + 60; x++)
                        if (x >= 0 && y >= 0 && x < w && y < h) ring.Add(bands[y * w + x]);
                Assert.GreaterOrEqual(ring.Count, 3, "HOTEL halo: unlit + at least two bands");

                // stable: moving the camera by whole pixels moves the bands with the world, unchanged
                var baseFrame = root.Rig.Current;
                var raw = ReadTarget(root.Pipeline.Target);
                foreach (var shift in new[] { new Vector2Int(1, 2), new Vector2Int(-3, 1), new Vector2Int(6, -5) })
                {
                    var moved = baseFrame.WithPosition(baseFrame.Position + new Vector3(shift.x, shift.y, 0f) / baseFrame.PixelsPerUnit, RenderingStyle.PixelHeight);
                    root.Pipeline.ApplyFrame(moved);
                    FrameCapture.Capture("test_light_shift");
                    var raw2 = ReadTarget(root.Pipeline.Target);
                    // the lit 3D interiors re-round their gradients by a step here and there: compare the light only where
                    // the world image itself is unchanged within the light's reach (8x8 cells, dilated 4 cells = 32 px)
                    const int cell = 8, reach = 4;
                    int cw = w / cell, ch = h / cell, rawDiff = 0;
                    var dirty = new bool[cw * ch];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int sx = x + shift.x, sy = y + shift.y;
                            if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                            Color32 p = raw2[y * w + x], q = raw[sy * w + sx];
                            if (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) <= 3) continue;
                            rawDiff++;
                            dirty[(y / cell) * cw + x / cell] = true;
                        }
                    var b2 = LightBands(glow, out _, out _, out _);
                    int diff = 0, n = 0;
                    var lo = new Vector2Int(int.MaxValue, int.MaxValue);
                    var hi = new Vector2Int(int.MinValue, int.MinValue);
                    for (int y = 40; y < h - 40; y++)
                        for (int x = 40; x < w - 40; x++)
                        {
                            bool near = false;
                            for (int cy = Mathf.Max(0, y / cell - reach); cy <= Mathf.Min(ch - 1, y / cell + reach) && !near; cy++)
                                for (int cx = Mathf.Max(0, x / cell - reach); cx <= Mathf.Min(cw - 1, x / cell + reach); cx++)
                                    if (dirty[cy * cw + cx]) { near = true; break; }
                            if (near) continue;
                            n++;
                            if (b2[y * w + x] == bands[(y + shift.y) * w + x + shift.x]) continue;
                            diff++;
                            lo = Vector2Int.Min(lo, new Vector2Int(x, y));
                            hi = Vector2Int.Max(hi, new Vector2Int(x, y));
                        }
                    report.Add("shift " + shift + " world changed=" + rawDiff + " px");                    report.Add("shift " + shift + " changed=" + diff + "/" + n + " in " + lo + ".." + hi);
                    Assert.Greater(n, (w - 80) * (h - 80) / 3, "most of the frame compared");
                    Assert.LessOrEqual(diff, n / 1000, "bands move with the world (shift " + shift + "): " + string.Join(" ; ", report));
                }
                root.Pipeline.ApplyFrame(baseFrame);

                // off switch: raw target, no passes
                glow.Enabled = false;
                Assert.AreSame(root.Pipeline.Target, root.Pipeline.Screen.Texture, "raw target when off");
                for (int i = 0; i < 3; i++) yield return null;
                passes = glow.Passes;
                for (int i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(passes, glow.Passes, "no lighting passes while off");
                report.Add("lighting off drawCalls=" + draws.LastValue);
                string off = FrameCapture.Capture("render_lighting_off");
                HideOtherCanvases(false);

                float bOn = ImageBrightness(on), bOff = ImageBrightness(off);
                report.Add("brightness on=" + bOn.ToString("0.0000") + " off=" + bOff.ToString("0.0000"));
                Assert.Greater(bOn, bOff + 0.002f, "emissive things light their surroundings");
                Assert.Less(bOn, bOff * 1.3f, "lighting, not a wash-out");
                glow.Enabled = PixelGlow.DefaultOn;
            }
            Directory.CreateDirectory(FrameCapture.OutputDir);
            File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_lighting_stats.txt"), string.Join("\n", report) + "\n");
            foreach (var line in report) TestContext.WriteLine(line);
        }

        // ------------------------------------------------------------------ crisp signs in both frames

        /// <summary>Neon quads of the active city mesh whose corners are not on whole pixels of <paramref name="f"/>.</summary>
        static int MisalignedNeonQuads(CityView city, CameraFrame f, out int checkedQuads)
        {
            var neon = new System.Collections.Generic.HashSet<int>(city.Dressing.NeonTexels);
            var mesh = city.ActiveMesh;
            var v = mesh.vertices;
            var uv = mesh.uv;
            var tr = city.Renderer.transform;
            float ppu = f.PixelsPerUnit;
            int bad = 0;
            checkedQuads = 0;
            for (int i = 0; i + 3 < v.Length; i += 4)
            {
                int texel = Mathf.FloorToInt(uv[i].x * PaletteAtlas.Size) + Mathf.FloorToInt(uv[i].y * PaletteAtlas.Size) * PaletteAtlas.Size;
                if (!neon.Contains(texel)) continue;
                checkedQuads++;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                bool ok = true;
                for (int k = 0; k < 4; k++)
                {
                    var s = f.ToScreenPlane(tr.TransformPoint(v[i + k])) * ppu;
                    min = Vector2.Min(min, s);
                    max = Vector2.Max(max, s);
                    if (Mathf.Abs(s.x - Mathf.Round(s.x)) > 0.02f || Mathf.Abs(s.y - Mathf.Round(s.y)) > 0.02f) ok = false;
                }
                if (max.x - min.x < 0.98f || max.y - min.y < 0.98f) ok = false;
                if (!ok) bad++;
            }
            return bad;
        }

        [UnityTest]
        public IEnumerator NeonSigns_AreWholePixels_InTheTitleAndGameplayFrames_ThenCaptures()
        {
            var report = new System.Collections.Generic.List<string>();
            yield return RenderingTestDriver.LoadTitle();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 6; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            var root = RenderingTestDriver.Rendering();
            Assert.IsTrue(root.Rig.TitleMode);
            var title = root.Rig.Current;
            var game = root.Rig.BaseFrame;
            Assert.AreNotEqual(Mathf.RoundToInt(game.PixelsPerUnit), Mathf.RoundToInt(title.PixelsPerUnit), "title zooms out");
            Assert.AreEqual(Mathf.RoundToInt(title.PixelsPerUnit), root.City.ActivePixelsPerUnit, "city mesh built for the title zoom");
            Assert.AreEqual(Mathf.Round(title.Position.x * title.PixelsPerUnit), title.Position.x * title.PixelsPerUnit, 1e-3f, "camera on a whole pixel");
            int bad = MisalignedNeonQuads(root.City, title, out int n);
            report.Add("title ppu=" + title.PixelsPerUnit + " neon quads=" + n + " misaligned=" + bad);
            Assert.Greater(n, 100, "neon letters found");
            Assert.AreEqual(0, bad, "every neon texel = whole target pixels in the title frame");

            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_title_signs")));
            HideOtherCanvases(false);
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), 0f);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_title_signs_zoom", W(0f, 0.02f), W(0.5f, 0.42f), 4)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_title_signs_zoom_right", W(0.5f, 0.02f), W(1f, 0.42f), 4)));
            RenderingTestDriver.Unfreeze();

            // gameplay frame: the first build, same texels (the rebuild allocated nothing)
            yield return RenderingTestDriver.LoadMain();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 3; i++) yield return null;
            root = RenderingTestDriver.Rendering();
            Assert.IsFalse(root.Rig.TitleMode);
            Assert.AreEqual(Mathf.RoundToInt(root.Rig.Current.PixelsPerUnit), root.City.ActivePixelsPerUnit, "city mesh for the gameplay zoom");
            bad = MisalignedNeonQuads(root.City, root.Rig.Current, out n);
            report.Add("gameplay ppu=" + root.Rig.Current.PixelsPerUnit + " neon quads=" + n + " misaligned=" + bad + " palette=" + root.Palette.Used);
            Assert.AreEqual(0, bad, "every neon texel = whole target pixels in the gameplay frame");
            int used = root.Palette.Used, lights = root.LivingLights.Count;
            root.City.ApplyPixelGrid(title.Grid);
            // every scenic zoom rung (11..8 ppu) is prewarmed at build: one mesh each, whole-pixel neon in each
            var rungs = ScenicZoom.Levels(root.Rig.BaseFrame, root.Rig.WideFrame);
            Assert.AreEqual(rungs.Length, root.City.MeshVariants, "one city mesh per zoom rung (title = widest rung)");
            Assert.AreEqual(used, root.Palette.Used, "zoom rebuild reuses the palette texels");
            Assert.AreEqual(lights, root.LivingLights.Count, "and the lights");
            foreach (int ppu in rungs)
            {
                var rung = ScenicZoom.LevelFrame(root.Rig.BaseFrame, root.Rig.WideFrame, ppu, RenderingStyle.PixelHeight);
                root.City.ApplyPixelGrid(rung.Grid);
                bad = MisalignedNeonQuads(root.City, rung, out n);
                report.Add("scenic rung ppu=" + ppu + " neon quads=" + n + " misaligned=" + bad);
                Assert.AreEqual(0, bad, "every neon texel = whole target pixels on the " + ppu + " ppu rung");
            }
            Assert.AreEqual(rungs.Length, root.City.MeshVariants, "no extra meshes");
            root.City.ApplyPixelGrid(root.Rig.Current.Grid);
            foreach (var line in report) TestContext.WriteLine(line);
            RenderingTestDriver.Unfreeze();
        }

        [UnityTest]
        public IEnumerator Capture_OriginalFloorMarks()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            // many floors relabelled "8" (the any_to_8 sticker) + one 4 -> F: presentation only (BuildingView directly)
            foreach (int n in new[] { 3, 5, 6, 9 })
                root.Building.SetLabel(FloorIndex.FromPhysicalNumber(n), new FloorLabel("8"), true, n.ToString());
            root.Building.SetLabel(FloorIndex.FromPhysicalNumber(4), new FloorLabel("F"), true, "4");
            Assert.AreEqual("6", root.Building.GetSignOriginalMark(FloorIndex.FromPhysicalNumber(6)));
            Assert.IsNull(root.Building.GetSignOriginalMark(FloorIndex.FromPhysicalNumber(8)), "the real 8 is unmarked");
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 10; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_original_floor_marks")));
            HideOtherCanvases(false);
            var layout = GameServices.Get<IWorldLayout>();
            var a = root.Building.GetSign(FloorIndex.FromPhysicalNumber(3)).transform.position + new Vector3(-2.2f, -0.6f, 0f);
            var b = root.Building.GetSign(FloorIndex.FromPhysicalNumber(9)).transform.position + new Vector3(2.4f, 0.8f, 0f);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_original_floor_marks_zoom", a, b, 4)));
            Assert.IsNotNull(layout);
        }
    }
}
