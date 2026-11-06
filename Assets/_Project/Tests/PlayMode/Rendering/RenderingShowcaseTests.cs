using System.Collections;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Visual check on the real session: floor 4 stickered to "F" in the day-1 shop, day 2 running with real guests
    /// (proxies), plus an unlucky red flash on floor 9. Writes captures/rendering_showcase.png (main.png stays the
    /// untouched boot frame).
    /// </summary>
    public class RenderingShowcaseTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Showcase_StickerGuestsAndRedFlash_WritesPng()
        {
            yield return RenderingTestDriver.LoadMain();
            var flow = GameServices.Get<IGameFlow>();
            var guests = GameServices.Get<IGuestQuery>();
            RenderingTestDriver.EnterShopWithTips(100);
            Assert.AreEqual(StickerApplyResult.Applied,
                GameServices.Get<IShopService>().TryBuyAndApply(new StickerId("four_to_f"), FloorIndex.FromPhysicalNumber(4)));
            Assert.IsTrue(flow.FinishShopping());
            Assert.IsTrue(flow.BeginDay(), "day 2 starts");

            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 200 && guests.ActiveGuests.Count < 3; i++) yield return null;
            Time.captureDeltaTime = 0f;
            GameServices.Events.Publish(new LuckJudged(new GuestId(1), LuckSource.FloorLabel, 9, LuckVerdict.Unlucky, FloorIndex.FromPhysicalNumber(9)));
            yield return RenderingTestDriver.Freeze();
            Assert.AreEqual(guests.ActiveGuests.Count, RenderingTestDriver.Rendering().Guests.Count, "every active guest has a proxy");

            var path = FrameCapture.Capture("rendering_showcase");
            Assert.IsTrue(File.Exists(path), path);
        }
    }
}
