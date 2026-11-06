using System.Collections;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Visual check of the event-driven visuals without gameplay: fake guests on a few floors, floor 4 stickered to
    /// "F" (flicker), an unlucky red flash on floor 9. Writes captures/rendering_showcase.png (main.png stays clean).
    /// </summary>
    public class RenderingShowcaseTests
    {
        [UnityTest]
        public IEnumerator Showcase_GuestsStickerAndRedFlash_WritesPng()
        {
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            yield return null;
            yield return null;
            var bus = GameServices.Events;
            string[] who = { "korean_grandma", "chinese_businessman", "western_tourist", "japanese_couple", "japanese_couple", "mystery_guest" };
            int[] floor = { 1, 1, 3, 6, 6, 8 };
            int[] slot = { 0, 1, 0, 0, 1, 0 };
            for (int i = 0; i < who.Length; i++)
                bus.Publish(new GuestSpawned(new GuestId(9000 + i), new GuestArchetypeId(who[i]), PartyId.None,
                    FloorIndex.FromPhysicalNumber(floor[i]), new DestinationId("lobby"), FloorIndex.FromPhysicalNumber(2), slot[i]));
            var f4 = FloorIndex.FromPhysicalNumber(4);
            bus.Publish(new FloorLabelChanged(f4, FloorLabel.Original(f4), new FloorLabel("F")));
            for (int i = 0; i < 10; i++) yield return null;
            var f9 = FloorIndex.FromPhysicalNumber(9);
            bus.Publish(new LuckJudged(new GuestId(9000), LuckSource.FloorLabel, 9, LuckVerdict.Unlucky, f9));
            yield return null;
            yield return null;

            var path = FrameCapture.Capture("rendering_showcase");
            Assert.IsTrue(File.Exists(path), path);
        }
    }
}
