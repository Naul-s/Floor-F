using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Main scene with the real gameplay session: pixel pipeline wiring, greybox mirrors IBuildingQuery/IElevatorQuery,
    /// a real sticker purchase relabels the sign, proxies mirror IGuestQuery.ActiveGuests.
    /// </summary>
    public class PixelPipelineTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static void AssertSignsMatchBuilding(RenderingRoot root, IBuildingQuery building)
        {
            Assert.AreEqual(building.FloorCount, root.Building.FloorCount);
            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                Assert.IsNotNull(root.Building.GetSign(f), "sign for floor " + f.PhysicalNumber);
                Assert.AreEqual(building.GetLabel(f).Text, root.Building.GetSignText(f), "sign text = current label, floor " + f.PhysicalNumber);
                Assert.AreEqual(building.IsRelabelled(f), root.Building.GetSignSticker(f), "sticker plate = IsRelabelled, floor " + f.PhysicalNumber);
                Assert.AreEqual(building.GetLabel(f).Text == "F", root.Lighting.IsFlickering(f), "F floors flicker, floor " + f.PhysicalNumber);
            }
        }

        [UnityTest]
        public IEnumerator WorldCamera_RendersInto640x360PointTexture_ShownByScreenSpaceCameraCanvas()
        {
            yield return RenderingTestDriver.LoadMain();
            var p = RenderingTestDriver.Rendering().Pipeline;
            Assert.IsNotNull(p.WorldCamera);
            Assert.AreSame(p.Target, p.WorldCamera.targetTexture, "world camera renders into the pixel target");
            Assert.AreEqual(PresentationContract.PixelWidth, p.Target.width);
            Assert.AreEqual(PresentationContract.PixelHeight, p.Target.height);
            Assert.AreEqual(FilterMode.Point, p.Target.filterMode);
            Assert.AreEqual(1, p.Target.antiAliasing, "no MSAA");
            Assert.IsTrue(p.WorldCamera.orthographic);
            Assert.AreEqual("MainCamera", p.WorldCamera.tag, "the scene's Main Camera is reused as the world camera");
            Assert.AreEqual(0, p.WorldCamera.cullingMask & (1 << PresentationContract.UiLayer), "world camera skips the UI layer");

            var ui = GameObject.Find(PresentationContract.UiCameraName);
            Assert.IsNotNull(ui, "UI camera findable by PresentationContract.UiCameraName");
            Assert.AreSame(p.UiCamera, ui.GetComponent<Camera>());
            Assert.IsNull(p.UiCamera.targetTexture, "UI camera draws to the screen");
            Assert.AreEqual(1 << PresentationContract.UiLayer, p.UiCamera.cullingMask);
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, p.ScreenCanvas.renderMode, "Overlay canvases are invisible to FrameCapture");
            Assert.AreSame(p.UiCamera, p.ScreenCanvas.worldCamera);
            Assert.AreEqual(PresentationContract.PixelScreenSortingOrder, p.ScreenCanvas.sortingOrder);
            Assert.AreSame(p.Target, p.Screen.Texture);
            Assert.Greater(p.Screen.DisplayRect.width, 0f);
            Assert.AreEqual(640f / 360f, p.Screen.DisplayRect.width / p.Screen.DisplayRect.height, 0.01f, "aspect kept");
            Assert.IsFalse(Object.FindObjectsByType<Canvas>().Any(c => c.renderMode == RenderMode.ScreenSpaceOverlay),
                "no Overlay canvases");
        }

        [UnityTest]
        public IEnumerator Greybox_MirrorsBuildingQuery_AndCarFollowsElevator()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            AssertSignsMatchBuilding(root, GameServices.Get<IBuildingQuery>());
            var elevator = GameServices.Get<IElevatorQuery>();
            var locator = GameServices.Get<IActorLocator>();
            Assert.AreEqual(elevator.Position, root.Elevator.FloorPosition, 0.01f, "car at the elevator's position");
            Assert.AreEqual(locator.CarPosition.Y, root.Elevator.Root.position.y, 0.01f, "car at IActorLocator.CarPosition");
        }

        [UnityTest]
        public IEnumerator RealSticker_FourToF_RelabelsSign_WithStickerPlate_AndFlicker()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var building = GameServices.Get<IBuildingQuery>();
            var shop = GameServices.Get<IShopService>();
            var floor4 = FloorIndex.FromPhysicalNumber(4);

            RenderingTestDriver.EnterShopWithTips(100);
            Assert.AreEqual(StickerApplyResult.Applied, shop.TryBuyAndApply(new StickerId("four_to_f"), floor4));
            yield return null;

            Assert.AreEqual("F", building.GetLabel(floor4).Text, "gameplay relabelled the floor");
            Assert.IsTrue(building.IsRelabelled(floor4));
            Assert.AreEqual("F", root.Building.GetSignText(floor4));
            Assert.IsTrue(root.Building.GetSignSticker(floor4), "relabelled floors get the sticker plate");
            Assert.IsTrue(root.Lighting.IsFlickering(floor4), "F floor tube flickers");
            AssertSignsMatchBuilding(root, building);
        }

        [UnityTest]
        public IEnumerator UnluckyJudgement_FlashesThatFloorRed()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var f = FloorIndex.FromPhysicalNumber(9);
            // presentation-only reaction: the event is what gameplay publishes on an unlucky door-open
            GameServices.Events.Publish(new LuckJudged(new GuestId(1), LuckSource.FloorLabel, 9, LuckVerdict.Unlucky, f));
            yield return null;
            Assert.IsTrue(root.Lighting.IsFlashing(f));
            var c = root.Lighting.GetCurrentColor(f);
            Assert.Greater(c.r, c.g + 0.3f, "reddish");
        }

        static void AssertProxiesMatch(RenderingRoot root, IGuestQuery guests, IActorLocator locator)
        {
            var expected = new HashSet<GuestId>(guests.ActiveGuests);
            var actual = new HashSet<GuestId>(root.Guests.Ids);
            Assert.IsTrue(expected.SetEquals(actual),
                "proxies [" + string.Join(",", actual) + "] = ActiveGuests [" + string.Join(",", expected) + "]");
            foreach (var id in expected)
                if (locator.TryGetGuestPosition(id, out var wp))
                    Assert.Less(Vector3.Distance(new Vector3(wp.X, wp.Y, wp.Z), root.Guests.Get(id).localPosition), 1.0f, id + " near its gameplay position");
        }

        [UnityTest]
        public IEnumerator GuestProxies_MirrorActiveGuests_ThroughRealSpawnsAndDelivery()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var guests = GameServices.Get<IGuestQuery>();
            var locator = GameServices.Get<IActorLocator>();
            var flow = GameServices.Get<IGameFlow>();
            Assert.AreEqual(0, root.Guests.Count, "no proxies before the day starts");

            int spawned = 0, finished = 0;
            using (GameServices.Events.Subscribe<GuestSpawned>(e => spawned++))
            using (GameServices.Events.Subscribe<GuestDelivered>(e => finished++))
            using (GameServices.Events.Subscribe<GuestLeft>(e => finished++))
            {
                Assert.IsTrue(flow.BeginDay());
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 100 && guests.ActiveGuests.Count < 2; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                Assert.Greater(spawned, 0, "day 1 spawned guests");
                Assert.Greater(guests.ActiveGuests.Count, 0);
                AssertProxiesMatch(root, guests, locator);
                RenderingTestDriver.Unfreeze();

                // play until someone is delivered (or gives up): their proxy must disappear with them
                var pilot = new AutoPilot(GameServices.Get<IElevatorQuery>(), GameServices.Get<IElevatorControl>(), guests, GameServices.Get<IBuildingQuery>());
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 900 && finished == 0; i++)
                {
                    pilot.Step();
                    yield return null;
                }
                yield return RenderingTestDriver.Freeze();
                Assert.Greater(finished, 0, "a guest was delivered or left");
                AssertProxiesMatch(root, guests, locator);
            }
        }
    }
}
