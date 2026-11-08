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
