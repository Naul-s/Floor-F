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
    /// Player-feedback polish on the real Main scene: original-floor marks on sticker signs.
    /// Captures render_original_floor_marks.
    /// </summary>
    public class RenderingPolishTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
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
