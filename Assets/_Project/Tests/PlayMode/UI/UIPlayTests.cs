using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Drives the real game through the UI's own buttons (onClick.Invoke via UIRoot test hooks).</summary>
    static class UITestDriver
    {
        public static T FindRoot<T>(string name) where T : Component
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (go.name == name && go.TryGetComponent<T>(out var c)) return c;
            return null;
        }

        public static UIRoot UI() => FindRoot<UIRoot>("[UI]");

        public static GameSession Session()
        {
            var root = FindRoot<GameplayRoot>("[Gameplay]");
            Assert.IsNotNull(root, "[Gameplay] root");
            Assert.IsNotNull(root.Session, "gameplay session");
            return root.Session;
        }

        public static IEnumerator LoadMain()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 4; i++) yield return null;
            var ui = UI();
            Assert.IsNotNull(ui, "[UI] root");
            Assert.IsTrue(ui.IsBuilt, "UI built");
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase, "game started");
        }

        public static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Intro -> Running through the START button.</summary>
        public static IEnumerator ClickStart()
        {
            var ui = UI();
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen, "intro card visible");
            ui.ClickFlowButton();
            Assert.AreEqual(DayPhase.Running, GameServices.Get<IDayQuery>().Phase, "START -> Running");
            yield return null;
        }

        public static void Reset()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
        }
    }

    public class UIPlayTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Main_ShowsIntro_OnUiCamera_StartButtonBeginsDay()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            Assert.IsTrue(ui.UsesUiCamera, "Screen Space - Camera on the Rendering UI camera, planeDistance < 10, sortingOrder >= 0");
            Assert.IsTrue(ui.AllOnUiLayer, "every UI object on layer " + PresentationContract.UiLayer);
            Assert.IsNotNull(UnityEngine.EventSystems.EventSystem.current, "EventSystem exists");
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            StringAssert.Contains("Day 1", ui.FlowTitle);

            yield return UITestDriver.ClickStart();
            yield return null;
            Assert.AreEqual(DayPhase.None, ui.VisibleFlowScreen, "no modal while running");
        }

        [UnityTest]
        public IEnumerator Panel_ButtonsShowCurrentLabels_ClickRequestsFloor()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var building = GameServices.Get<IBuildingQuery>();
            Assert.AreEqual(building.FloorCount, ui.FloorButtonCount);
            Assert.AreEqual(9, ui.FloorButtonCount);
            for (int f = 0; f < building.FloorCount; f++)
                Assert.AreEqual(building.GetLabel(new FloorIndex(f)).Text, ui.FloorButtonText(f), "button " + f);

            yield return UITestDriver.ClickStart();
            var elevator = GameServices.Get<IElevatorQuery>();
            ui.ClickFloor(5);
            Assert.IsTrue(elevator.HasTarget, "panel click -> RequestFloor");
            Assert.AreEqual(5, elevator.TargetFloor.Value);
            yield return UITestDriver.Frames(3);
            Assert.AreEqual(ElevatorMotion.MovingUp, elevator.Motion);
            Assert.AreEqual(building.GetLabel(elevator.CurrentFloor).Text, ui.LedText, "LED shows the current label");
        }

        [UnityTest]
        public IEnumerator Shop_StickerOnHighlightedFloor_RelabelsButton()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            yield return UITestDriver.ClickStart();

            var s = UITestDriver.Session();
            s.Score.AddTips(100, earned: true);
            s.EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, day.Phase);
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen, "summary card");

            ui.ClickFlowButton(); // CONTINUE
            Assert.AreEqual(DayPhase.Shop, day.Phase);
            yield return null;
            Assert.AreEqual(DayPhase.Shop, ui.VisibleFlowScreen, "shop card");
            Assert.GreaterOrEqual(ui.ShopOfferCount, 1, "day 1 sells four_to_f");

            ui.ClickShopOffer(0);
            yield return null;
            var fourth = FloorIndex.FromPhysicalNumber(4);
            Assert.IsTrue(ui.IsFloorApplicable(fourth.Value), "floor labelled 4 glows for 4 -> F");
            Assert.IsFalse(ui.IsFloorApplicable(0), "floor 1 does not");

            ui.ClickFloor(fourth.Value);
            Assert.AreEqual("F", GameServices.Get<IBuildingQuery>().GetLabel(fourth).Text, "sticker applied via TryBuyAndApply");
            Assert.AreEqual("F", ui.FloorButtonText(fourth.Value), "button relabelled on FloorLabelChanged");
            StringAssert.Contains("F", ui.ShopResult);

            ui.ClickShopDone();
            Assert.AreEqual(DayPhase.Intro, day.Phase);
            Assert.AreEqual(2, day.CurrentDay.Number);
            yield return null;
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            Assert.AreEqual("F", ui.FloorButtonText(fourth.Value), "label persists into day 2");
        }

        [UnityTest]
        public IEnumerator RealLuckJudgement_ShowsPopup_AndBubbles()
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var guests = GameServices.Get<IGuestQuery>();
            var elevator = GameServices.Get<IElevatorQuery>();
            int judged = 0, spoke = 0;
            LuckJudged last = default;
            using (GameServices.Events.Subscribe<LuckJudged>(e => { judged++; last = e; }))
            using (GameServices.Events.Subscribe<GuestSpoke>(e => spoke++))
            {
                yield return UITestDriver.ClickStart();
                Time.captureDeltaTime = 0.1f;
                var waiting = new System.Collections.Generic.List<GuestId>();
                for (int i = 0; i < 100 && guests.GetWaiting(FloorIndex.Ground, waiting) < 3; i++) yield return null;
                Assert.AreEqual(3, guests.GetWaiting(FloorIndex.Ground, waiting), "day 1: three guests wait in the lobby");
                yield return null;
                Assert.GreaterOrEqual(ui.VisibleBubbleCount, 3, "number chips above waiting guests");
                if (spoke > 0) Assert.Greater(ui.ActiveSpeechCount, 0, "GuestSpoke -> speech bubble");

                ui.ClickOpen();
                for (int i = 0; i < 100 && elevator.Occupancy < 3; i++) yield return null;
                Assert.AreEqual(3, elevator.Occupancy, "all three boarded");
                ui.ClickClose();
                for (int i = 0; i < 100 && judged == 0; i++) yield return null;
                Time.captureDeltaTime = 0f;
            }
            GameplayRoot.SeedOverride = null;

            Assert.Greater(judged, 0, "closing with 3 inside: grandma (lucky 3) judges the head count");
            Assert.AreEqual(LuckSource.Occupancy, last.Source);
            Assert.Greater(ui.ActivePopupCount, 0, "LuckJudged -> popup");
            StringAssert.Contains("LUCKY", ui.LastPopupText);
            StringAssert.Contains("3 people", ui.LastPopupText);
        }

        [UnityTest]
        public IEnumerator LanguageToggle_FlipsAndBack()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            Assert.IsFalse(ui.Korean, "English by default");
            ui.ClickLanguage();
            yield return null;
            Assert.IsTrue(ui.Korean);
            StringAssert.Contains("일차", ui.FlowTitle, "intro re-rendered from titleKo");
            ui.ClickLanguage();
            yield return null;
            Assert.IsFalse(ui.Korean);
            StringAssert.Contains("Day 1", ui.FlowTitle);
        }
    }

    /// <summary>L2 captures of the HUD in each flow state (run with -Filter Capture). Agents open the PNGs with Read.</summary>
    public class UICaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_UiStates()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(10);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_intro")));

            yield return UITestDriver.ClickStart();
            Time.captureDeltaTime = 0.1f;
            var guests = GameServices.Get<IGuestQuery>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var waiting = new System.Collections.Generic.List<GuestId>();
            for (int i = 0; i < 100 && guests.GetWaiting(FloorIndex.Ground, waiting) < 3; i++) yield return null;
            ui.ClickOpen();
            for (int i = 0; i < 60 && elevator.Occupancy < 3; i++) yield return null;
            ui.ClickClose();
            for (int i = 0; i < 30 && ui.ActivePopupCount == 0; i++) yield return null;
            ui.ClickFloor(4); // head up, so the riders' chips sit beside a moving car
            for (int i = 0; i < 3; i++) yield return null;
            Time.captureDeltaTime = 0f;
            yield return null;
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_running")));

            var s = UITestDriver.Session();
            s.Score.AddTips(60, earned: true);
            s.EndDayNow();
            yield return null;
            ui.ClickFlowButton();
            yield return null;
            ui.ClickShopOffer(0);
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_shop")));
        }
    }
}
