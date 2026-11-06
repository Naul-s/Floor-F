using System.Collections;
using System.Linq;
using FloorF.Core;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>Main scene: pixel pipeline wiring, greybox, and the event hooks (labels, unlucky flash).</summary>
    public class PixelPipelineTests
    {
        static RenderingRoot FindRoot()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (go.name == RenderingNames.Root && go.TryGetComponent<RenderingRoot>(out var r)) return r;
            return null;
        }

        IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldCamera_RendersInto640x360PointTexture_ShownByScreenSpaceCameraCanvas()
        {
            yield return LoadMain();
            var root = FindRoot();
            Assert.IsNotNull(root, "[Rendering] root");
            var p = root.Pipeline;
            Assert.IsNotNull(p.WorldCamera);
            Assert.AreSame(p.Target, p.WorldCamera.targetTexture, "world camera renders into the pixel target");
            Assert.AreEqual(640, p.Target.width);
            Assert.AreEqual(360, p.Target.height);
            Assert.AreEqual(FilterMode.Point, p.Target.filterMode);
            Assert.AreEqual(1, p.Target.antiAliasing, "no MSAA");
            Assert.IsTrue(p.WorldCamera.orthographic);
            Assert.AreEqual("MainCamera", p.WorldCamera.tag, "the scene's Main Camera is reused as the world camera");

            Assert.IsNotNull(p.UiCamera);
            Assert.AreEqual(RenderingNames.UiCamera, p.UiCamera.name);
            Assert.IsNull(p.UiCamera.targetTexture, "UI camera draws to the screen");
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, p.ScreenCanvas.renderMode, "Overlay canvases are invisible to FrameCapture");
            Assert.AreSame(p.UiCamera, p.ScreenCanvas.worldCamera);
            Assert.AreSame(p.Target, p.Screen.Texture);
            Assert.Greater(p.Screen.DisplayRect.width, 0f);
            Assert.AreEqual(640f / 360f, p.Screen.DisplayRect.width / p.Screen.DisplayRect.height, 0.01f, "aspect kept");
            Assert.IsFalse(Object.FindObjectsByType<Canvas>().Any(c => c.renderMode == RenderMode.ScreenSpaceOverlay),
                "no Overlay canvases from rendering");
        }

        [UnityTest]
        public IEnumerator Greybox_HasOneSignPerFloor_WithCurrentLabels_AndCar()
        {
            yield return LoadMain();
            var root = FindRoot();
            Assert.IsTrue(root.IsBuilt);
            var b = root.Building;
            Assert.AreEqual(9, b.FloorCount);
            for (int i = 0; i < 9; i++)
            {
                var f = new FloorIndex(i);
                Assert.IsNotNull(b.GetSign(f), "sign for floor " + f.PhysicalNumber);
                Assert.AreEqual(FloorLabel.Original(f).Text, b.GetSignText(f), "initial label = physical number");
            }
            Assert.IsNotNull(root.Elevator.Root);
            Assert.AreEqual(0f, root.Elevator.FloorPosition, 0.01f, "car parked at floor 1 without elevator service");
        }

        [UnityTest]
        public IEnumerator FloorLabelChanged_UpdatesSign_AndFFloorFlickers()
        {
            yield return LoadMain();
            var root = FindRoot();
            var floor4 = FloorIndex.FromPhysicalNumber(4);
            GameServices.Events.Publish(new FloorLabelChanged(floor4, FloorLabel.Original(floor4), new FloorLabel("F")));
            yield return null;
            Assert.AreEqual("F", root.Building.GetSignText(floor4));
            Assert.IsTrue(root.Building.GetSignSticker(floor4), "relabelled floors get the sticker plate");
            Assert.IsTrue(root.Lighting.IsFlickering(floor4), "F floor tube flickers");

            GameServices.Events.Publish(new FloorLabelChanged(floor4, new FloorLabel("F"), FloorLabel.Original(floor4)));
            yield return null;
            Assert.AreEqual("4", root.Building.GetSignText(floor4));
            Assert.IsFalse(root.Lighting.IsFlickering(floor4));
        }

        [UnityTest]
        public IEnumerator UnluckyJudgement_FlashesThatFloorRed()
        {
            yield return LoadMain();
            var root = FindRoot();
            var f = FloorIndex.FromPhysicalNumber(9);
            GameServices.Events.Publish(new LuckJudged(new GuestId(1), LuckSource.FloorLabel, 9, LuckVerdict.Unlucky, f));
            yield return null;
            Assert.IsTrue(root.Lighting.IsFlashing(f));
            var c = root.Lighting.GetCurrentColor(f);
            Assert.Greater(c.r, c.g + 0.3f, "reddish");
        }

        [UnityTest]
        public IEnumerator GuestSpawned_CreatesProxy_GuestLeft_RemovesIt()
        {
            yield return LoadMain();
            var root = FindRoot();
            var id = new GuestId(4242);
            GameServices.Events.Publish(new GuestSpawned(id, new GuestArchetypeId("korean_grandma"), PartyId.None,
                FloorIndex.FromPhysicalNumber(3), new DestinationId("sauna"), FloorIndex.FromPhysicalNumber(5), 0));
            yield return null;
            if (GameServices.Has<IGuestQuery>()) Assert.Ignore("IGuestQuery registered: proxies follow gameplay, fake events are reconciled away");
            Assert.IsTrue(root.Guests.Contains(id));
            var layout = GameServices.Get<IWorldLayout>();
            Assert.AreEqual(layout.GetWaitingSlot(FloorIndex.FromPhysicalNumber(3), 0).Y, root.Guests.Get(id).localPosition.y, 0.01f);
            GameServices.Events.Publish(new GuestLeft(id, FloorIndex.FromPhysicalNumber(3), GuestLeaveReason.PatienceRanOut));
            yield return null;
            Assert.IsFalse(root.Guests.Contains(id));
        }
    }
}
