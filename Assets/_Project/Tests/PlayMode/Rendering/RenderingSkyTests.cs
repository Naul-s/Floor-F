using System.Collections;
using System.Collections.Generic;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Night sky on the real Main scene: moon + three kinds of stars every night, Milky Way + meteor shower only on star
    /// night, wish stars driven by synthetic WishStarAppeared / WishMade / WishStarFaded events (gameplay owns the real
    /// ones). Captures render_sky_title, render_sky_day, render_star_night, render_wish_star; writes render_sky_stats.txt.
    /// </summary>
    public class RenderingSkyTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var root = RenderingTestDriver.Rendering();
            if (root != null && root.Sky != null) root.Sky.ForceStarNight = null;
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static SkyView Sky => RenderingTestDriver.Rendering().Sky;

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

        /// <summary>Brightest luminance in the low-res target within +-<paramref name="r"/> pixels of a viewport point.</summary>
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

        [UnityTest]
        public IEnumerator Sky_MoonAndStars_EveryNight_FewRenderers()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var sky = root.Sky;
            Assert.IsNotNull(sky, "sky built");
            Assert.AreEqual(StarField.SteadyCount, sky.CountOf(StarKind.Steady));
            Assert.AreEqual(StarField.TwinkleCount, sky.CountOf(StarKind.Twinkle));
            Assert.AreEqual(StarField.GlintCount, sky.CountOf(StarKind.Glint));
            Assert.AreSame(root.Palette.Material, sky.StarRenderer.sharedMaterial, "stars + moon share the city palette");
            Assert.AreSame(root.Palette.Material, sky.FxRenderer.sharedMaterial, "glints / meteors / wish stars share it too");
            Assert.IsTrue(sky.StarRenderer.enabled);
            Assert.IsFalse(sky.StarNight, "day 1 is an ordinary night");
            Assert.IsFalse(sky.BandVisible, "no Milky Way on an ordinary night");
            Assert.LessOrEqual(sky.Root.GetComponentsInChildren<Renderer>(true).Length, 4, "sky = at most 4 renderers");

            // twinkles rewrite palette texels, glints come and go, nothing new gets created
            int renderers = sky.Root.GetComponentsInChildren<Renderer>(true).Length;
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.1f;
            bool glint = false;
            int meteorsBefore = sky.MeteorsSeen;
            for (int i = 0; i < 150; i++)
            {
                yield return null;
                if (sky.ActiveGlint >= 0 && sky.FxRenderer.enabled) glint = true;
            }
            Assert.IsTrue(glint, "a glint star flashed within 15 s");
            Assert.AreEqual(meteorsBefore, sky.MeteorsSeen, "no meteors on an ordinary night");
            Assert.AreEqual(renderers, sky.Root.GetComponentsInChildren<Renderer>(true).Length);
        }

        [UnityTest]
        public IEnumerator Sky_StaysOnWholeViewportPixels_WhileTheTitlePans()
        {
            yield return RenderingTestDriver.LoadTitle();
            var root = RenderingTestDriver.Rendering();
            var cam = root.Pipeline.WorldCamera;
            Assert.IsTrue(root.Rig.TitleMode);
            Time.captureDeltaTime = 0.7f;
            var pans = new HashSet<int>();
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                pans.Add(root.Rig.OffsetPixels.x);
                var star = Sky.StarRenderer.transform;
                var o = cam.WorldToViewportPoint(star.position);
                Assert.AreEqual(0f, o.x * 640f, 0.01f, "stars layer origin = viewport left edge");
                Assert.AreEqual(0f, o.y * 360f, 0.01f, "stars layer origin = viewport bottom edge");
                var px = cam.WorldToViewportPoint(star.TransformPoint(new Vector3(200f, 300f, 0f)));
                Assert.AreEqual(200f, px.x * 640f, 0.01f, "one layer unit = one target pixel");
                Assert.AreEqual(300f, px.y * 360f, 0.01f);
                Assert.Less(cam.WorldToViewportPoint(star.position).z, cam.farClipPlane, "inside the far plane");
            }
            Assert.Greater(pans.Count, 3, "the title camera panned meanwhile");
        }

        IEnumerator AssertWishStarAtViewport(string what, string capture)
        {
            var root = RenderingTestDriver.Rendering();
            var cam = root.Pipeline.WorldCamera;
            GameServices.Events.Publish(new WishStarAppeared(71, 0.30f, 0.93f, 0.18f, 0.80f, 6f));
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 12; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            Assert.IsTrue(Sky.TryGetWishHead(71, out var vp), what + ": wish star tracked");
            Assert.That(vp.x, Is.InRange(0.18f, 0.30f));
            // the event's viewport point, through the applied camera frame, lands on the head
            var world = SkyView.ViewportToWorld(root.Rig.Current, vp, SkyView.FxDepth);
            var back = cam.WorldToViewportPoint(world);
            Assert.AreEqual(vp.x, back.x, 0.5f / 640f, what + ": head x through the camera");
            Assert.AreEqual(vp.y, back.y, 0.5f / 360f, what + ": head y through the camera");
            // and the drawn pixels are there: the brightest thing around that spot is the star
            if (capture != null) HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture(capture ?? "test_wish_star")));
            if (capture != null) HideOtherCanvases(false);
            Assert.Greater(TargetPeak(vp, 1), 0.8f, what + ": bright head pixel at the event's viewport point");
            GameServices.Events.Publish(new WishStarFaded(71));
        }

        [UnityTest]
        public IEnumerator WishStar_MatchesEventViewportCoords_InGameplayAndTitleFrames()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Assert.IsFalse(RenderingTestDriver.Rendering().Rig.TitleMode);
            yield return AssertWishStarAtViewport("gameplay frame", null);
            RenderingTestDriver.Unfreeze();

            yield return RenderingTestDriver.LoadTitle();
            Assert.IsTrue(RenderingTestDriver.Rendering().Rig.TitleMode);
            yield return AssertWishStarAtViewport("title frame", null);
        }

        [UnityTest]
        public IEnumerator WishStar_FreezesWhilePausedOrHeld_ThenWishBurstsAndFadeFinishes()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var sky = Sky;
            GameServices.Events.Publish(new WishStarAppeared(5, 0.8f, 0.95f, 0.6f, 0.8f, 5f));
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(sky.TryGetWishHead(5, out var a));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(sky.TryGetWishHead(5, out var b));
            Assert.Greater((b - a).magnitude, 0.01f, "crosses the sky");

            RenderingTestDriver.SetPaused(true);
            yield return null;
            sky.TryGetWishHead(5, out var p0);
            for (int i = 0; i < 10; i++) yield return null;
            sky.TryGetWishHead(5, out var p1);
            Assert.AreEqual(p0, p1, "frozen while the menu pause holds the sim");
            RenderingTestDriver.SetPaused(false);
            for (int i = 0; i < 3; i++) yield return null;
            sky.TryGetWishHead(5, out var p2);
            Assert.AreNotEqual(p1, p2, "moves again after the pause");

            GameServices.Events.Publish(new TutorialHoldChanged(true));
            yield return null;
            sky.TryGetWishHead(5, out var h0);
            for (int i = 0; i < 10; i++) yield return null;
            sky.TryGetWishHead(5, out var h1);
            Assert.AreEqual(h0, h1, "frozen during the coach-mark hold");
            GameServices.Events.Publish(new TutorialHoldChanged(false));
            for (int i = 0; i < 3; i++) yield return null;
            sky.TryGetWishHead(5, out var h2);
            Assert.AreNotEqual(h1, h2);

            // wished: sparkle burst at the star, then it is gone
            int quadsBefore = sky.FxQuadCount;
            GameServices.Events.Publish(new WishMade(5, 3));
            Time.captureDeltaTime = 0.05f;
            yield return null;
            Assert.IsTrue(sky.IsWishEnded(5));
            Assert.Greater(sky.FxQuadCount, quadsBefore, "burst of sparkles");
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 15; i++) yield return null;
            Assert.AreEqual(0, sky.WishCount, "burst finished");

            // faded: simply finishes
            GameServices.Events.Publish(new WishStarAppeared(6, 0.2f, 0.9f, 0.1f, 0.8f, 3f));
            for (int i = 0; i < 5; i++) yield return null;
            GameServices.Events.Publish(new WishStarFaded(6));
            yield return null;
            Assert.IsTrue(sky.IsWishEnded(6));
            for (int i = 0; i < 12; i++) yield return null;
            Assert.AreEqual(0, sky.WishCount, "faded star finished");
        }

        /// <summary>
        /// Day 7 feedback: wish stars were hidden behind the skyline / hotel. Now they are drawn on an additive layer over
        /// the whole world - a star crossing the hotel itself is still the brightest thing at the event's viewport point.
        /// Captures render_wish_front (one star over the hotel, one wished on over the neighbours, HUD hidden).
        /// </summary>
        [UnityTest]
        public IEnumerator WishStar_DrawnInFrontOfTheHotel_OnTheEventCoords()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var sky = Sky;
            var cam = root.Pipeline.WorldCamera;
            Assert.IsTrue(sky.WishInFront, "additive wish shader loaded (Resources/" + RenderAssets.AdditiveShaderPath + ")");
            Assert.AreNotSame(root.Palette.Material, sky.WishRenderer.sharedMaterial, "own additive material");
            Assert.GreaterOrEqual(sky.WishRenderer.sharedMaterial.renderQueue, 3000, "drawn after the opaque world");
            Assert.AreSame(root.Palette.Texture, sky.WishRenderer.sharedMaterial.mainTexture, "same palette texels");

            sky.ForceStarNight = true;
            GameServices.Events.Publish(new WishStarAppeared(41, 0.56f, 0.70f, 0.44f, 0.40f, 6f));
            GameServices.Events.Publish(new WishStarAppeared(42, 0.90f, 0.60f, 0.80f, 0.45f, 5f));
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            GameServices.Events.Publish(new WishMade(42, 3));
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 4; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            Assert.IsTrue(sky.WishRenderer.enabled, "wish layer on while a star crosses");
            Assert.Greater(sky.WishQuadCount, 20, "head, tail, glow and burst on the wish layer");

            Assert.IsTrue(sky.TryGetWishHead(41, out var vp));
            // the head is over the hotel itself (inside its projected bounds) ...
            var hb = BuildingView.FramingBounds(GameServices.Get<IWorldLayout>());
            var lo = cam.WorldToViewportPoint(root.transform.Find(RenderingNames.World).TransformPoint(new Vector3(hb.min.x, hb.min.y, hb.min.z)));
            var hi = cam.WorldToViewportPoint(root.transform.Find(RenderingNames.World).TransformPoint(new Vector3(hb.max.x, hb.max.y, hb.min.z)));
            Assert.That(vp.x, Is.InRange(lo.x, hi.x), "head over the hotel (x)");
            Assert.That(vp.y, Is.InRange(lo.y, hi.y), "head over the hotel (y)");
            // ... on the event's viewport mapping, and still the brightest pixel there
            var back = cam.WorldToViewportPoint(SkyView.ViewportToWorld(root.Rig.Current, vp, SkyView.FxDepth));
            Assert.AreEqual(vp.x, back.x, 0.5f / 640f);
            Assert.AreEqual(vp.y, back.y, 0.5f / 360f);
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_wish_front")));
            HideOtherCanvases(false);
            Assert.Greater(TargetPeak(vp, 1), 0.9f, "white-hot head drawn over the hotel at the event's viewport point");
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), SkyView.FxDepth);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_wish_front_zoom", W(vp.x - 0.08f, vp.y - 0.1f), W(vp.x + 0.12f, vp.y + 0.14f), 5)));

            RenderingTestDriver.Unfreeze();
            GameServices.Events.Publish(new WishStarFaded(41));
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++) yield return null;
            Assert.AreEqual(0, sky.WishCount);
            Assert.IsFalse(sky.WishRenderer.enabled, "wish layer off (no draw call) when no wish star is up");
        }

        [UnityTest]
        public IEnumerator MilkyWayAndMeteors_OnlyOnStarNight()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var sky = Sky;
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsStarNight);
            Time.captureDeltaTime = 0.1f;
            int before = sky.MeteorsSeen;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                Assert.IsFalse(sky.BandVisible);
                Assert.IsFalse(sky.MeteorActive);
            }
            Assert.AreEqual(before, sky.MeteorsSeen);

            sky.ForceStarNight = true;
            yield return null;
            Assert.IsTrue(sky.BandVisible, "Milky Way on star night");
            for (int i = 0; i < 300 && sky.MeteorsSeen < before + 2; i++) yield return null;
            Assert.GreaterOrEqual(sky.MeteorsSeen, before + 2, "meteor shower");
            Assert.LessOrEqual(sky.Root.GetComponentsInChildren<Renderer>(true).Length, 4);

            sky.ForceStarNight = false;
            yield return null;
            Assert.IsFalse(sky.BandVisible, "gone again the next night");
        }

        [UnityTest]
        public IEnumerator ElevatorArrow_ShowsTheTravelDirection()
        {
            yield return RenderingTestDriver.LoadMain();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            var root = RenderingTestDriver.Rendering();
            var control = GameServices.Get<IElevatorControl>();
            var query = GameServices.Get<IElevatorQuery>();
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 40 && !control.AcceptsInput; i++) yield return null;
            if (query.Doors != DoorState.Closed) { control.CloseDoors(); for (int i = 0; i < 20 && query.Doors != DoorState.Closed; i++) yield return null; }
            Assert.AreEqual(0, root.Elevator.ArrowDirection, "idle: no arrow");
            Assert.IsTrue(control.RequestFloor(FloorIndex.FromPhysicalNumber(4)), "send the car up");
            bool up = false;
            for (int i = 0; i < 40 && !up; i++) { yield return null; up = root.Elevator.ArrowDirection == 1; }
            Assert.IsTrue(up, "arrow up while rising");
            var arrow = root.Elevator.Body.Find("Direction Arrow");
            Assert.IsNotNull(arrow);
            Assert.IsTrue(arrow.gameObject.activeInHierarchy);
            for (int i = 0; i < 400 && query.Motion != ElevatorMotion.Idle; i++) yield return null;
            yield return null;
            Assert.AreEqual(0, root.Elevator.ArrowDirection, "arrived: arrow off");
            Assert.IsFalse(arrow.gameObject.activeSelf);
        }

        static string Stats(string label, ProfilerRecorder draws, ProfilerRecorder setPass, ProfilerRecorder tris)
        {
            var sky = Sky;
            return "[RenderStats] " + label + " drawCalls=" + draws.LastValue + " setPass=" + setPass.LastValue + " triangles=" + tris.LastValue +
                   " | sky renderers on=" + CountEnabled(sky.Root) + " fx quads=" + sky.FxQuadCount + " palette texels=" + RenderingTestDriver.Rendering().Palette.Used;
        }

        static int CountEnabled(Transform t)
        {
            int n = 0;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) if (r.enabled) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Capture_SkyTitle_Day_StarNight_WishStar()
        {
            var report = new List<string>();
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            {
                // title with its UI, as players see it
                yield return RenderingTestDriver.LoadTitle();
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 20; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 5; i++) yield return null;
                report.Add(Stats("title", draws, setPass, tris));
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_sky_title")));
                RenderingTestDriver.Unfreeze();

                // ordinary night, gameplay frame: HUD on (stats + capture) and world only
                yield return RenderingTestDriver.LoadMain();
                Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 30; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 5; i++) yield return null;
                report.Add(Stats("day (HUD)", draws, setPass, tris));
                HideOtherCanvases(true);
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_sky_day")));
                HideOtherCanvases(false);
                var sky = Sky;
                Vector3 W(float x, float y) => SkyView.ViewportToWorld(RenderingTestDriver.Rendering().Rig.Current, new Vector2(x, y), SkyView.FxDepth);
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_sky_moon_zoom", W(0.02f, 0.74f), W(0.14f, 0.95f), 6)));

                // star night: Milky Way, meteor in flight, a wish star crossing and another just wished on
                sky.ForceStarNight = true;
                Time.timeScale = 1f;
                Time.captureDeltaTime = 0.05f;
                // wait for a meteor in the open sky left of the hotel (most of the gameplay sky is hotel / HUD)
                for (int i = 0; i < 900 && !(sky.MeteorActive && sky.Meteor.Head.x < 200f && sky.Meteor.Head.y > 250f && sky.Meteor.Level > 0.6f); i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 3; i++) yield return null;
                report.Add(Stats("star night (HUD)", draws, setPass, tris));
                HideOtherCanvases(true);
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_star_night")));
                HideOtherCanvases(false);
                if (sky.MeteorActive)
                {
                    var mh = new Vector2(sky.Meteor.Head.x / 640f, sky.Meteor.Head.y / 360f);
                    Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_meteor_zoom",
                        W(mh.x - 0.06f, mh.y - 0.08f), W(mh.x + 0.08f, Mathf.Min(1f, mh.y + 0.1f)), 5)));
                }

                Time.timeScale = 1f;
                GameServices.Events.Publish(new WishStarAppeared(1, 0.33f, 0.97f, 0.12f, 0.78f, 5f));
                GameServices.Events.Publish(new WishStarAppeared(2, 0.95f, 0.98f, 0.70f, 0.86f, 5f));
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 18; i++) yield return null;
                GameServices.Events.Publish(new WishMade(2, 3));
                Time.captureDeltaTime = 0.05f;
                for (int i = 0; i < 5; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                for (int i = 0; i < 3; i++) yield return null;
                report.Add(Stats("star night + wish stars (HUD)", draws, setPass, tris));
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_wish_star_hud")));
                HideOtherCanvases(true);
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_wish_star")));
                HideOtherCanvases(false);
                Assert.IsTrue(Sky.TryGetWishHead(1, out var head));
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_wish_star_zoom",
                    W(head.x - 0.08f, head.y - 0.1f), W(head.x + 0.12f, Mathf.Min(1f, head.y + 0.12f)), 5)));

                // the wide (title) frame under a forced star night: the whole Milky Way at once (look check only)
                RenderingTestDriver.Unfreeze();
                yield return RenderingTestDriver.LoadTitle();
                Sky.ForceStarNight = true;
                GameServices.Events.Publish(new WishStarAppeared(3, 0.30f, 0.97f, 0.12f, 0.80f, 5f));
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 16; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                HideOtherCanvases(true);
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_star_night_wide")));
                HideOtherCanvases(false);

                Directory.CreateDirectory(FrameCapture.OutputDir);
                File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_sky_stats.txt"), string.Join("\n", report) + "\n");
                foreach (var line in report)
                {
                    Debug.Log(line);
                    TestContext.WriteLine(line);
                }
            }
        }
    }
}
