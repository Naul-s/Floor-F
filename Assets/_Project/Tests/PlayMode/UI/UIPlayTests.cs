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

        /// <summary>
        /// Boots Main straight into Day 1 Intro (no title / attract demo). By default skips the "Day 0" training shift at
        /// boot (GameplayRoot.SkipTutorialForTests) so tests run on the first real day; tutorial tests pass
        /// <paramref name="skipTutorial"/> false.
        /// </summary>
        public static IEnumerator LoadMain(bool skipTutorial = true)
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = true;
            GameplayRoot.SkipTutorialForTests = skipTutorial;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 4; i++) yield return null;
            var ui = UI();
            Assert.IsNotNull(ui, "[UI] root");
            Assert.IsTrue(ui.IsBuilt, "UI built");
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase, "game started");
            if (skipTutorial) Assert.IsFalse(GameServices.Get<IDayQuery>().IsTutorial, "booted on the first real day");
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

        /// <summary>Boots Main the real way: the title screen with the attract demo behind it.</summary>
        public static IEnumerator LoadTitle()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = false;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 4; i++) yield return null;
            var ui = UI();
            Assert.IsNotNull(ui, "[UI] root");
            Assert.IsTrue(ui.IsBuilt, "UI built");
            Assert.AreEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase, "boots into the title");
            Assert.IsTrue(GameServices.Get<IMenuFlow>().IsOnTitle);
        }

        /// <summary>Waits (simulated 0.1 s frames) until <paramref name="done"/> or <paramref name="frames"/> run out.</summary>
        public static IEnumerator Until(System.Func<bool> done, int frames = 150)
        {
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < frames && !done(); i++) yield return null;
            Time.captureDeltaTime = 0f;
        }

        /// <summary>Plays every remaining day through the session (BeginDay -> EndDayNow -> ...) up to the ending card.</summary>
        public static void RunToEnding(GameSession s)
        {
            var day = GameServices.Get<IDayQuery>();
            for (int guard = 0; guard < 40 && day.Phase != DayPhase.Ending; guard++)
            {
                switch (day.Phase)
                {
                    case DayPhase.Intro: s.BeginDay(); break;
                    case DayPhase.Running: s.EndDayNow(); break;
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop: s.FinishShopping(); break;
                }
            }
            Assert.AreEqual(DayPhase.Ending, day.Phase, "reached an ending");
        }

        public static void Reset()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            GameplayRoot.SettingsStoreOverride = null;
            GameplayRoot.SeedOverride = null;
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
            Assert.AreEqual(2, day.DisplayDay, "Day 2 (internal day 3 after the Day 0 training shift)");
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
        [UnityTest]
        public IEnumerator Font_IsGalmuri_ForAllTexts()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            Assert.IsTrue(ui.FontIsGalmuri, "runtime Galmuri11 font asset created (no fallback)");
            Assert.Greater(ui.TextCount, 20, "UI has texts");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "every text: Galmuri, 12px-grid size, no auto-size");
            yield return UITestDriver.ClickStart();
            ui.ClickLanguage();
            yield return UITestDriver.Frames(2);
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "still Galmuri after the Korean toggle");
            GameServices.Get<IGameSettings>().UseKorean = false;
        }

        [UnityTest]
        public IEnumerator KoreanToggle_UsesSettingsService()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            Assert.IsTrue(ui.SettingsServiceBound, "UI reads the registered IGameSettings");
            Assert.IsFalse(settings.UseKorean);
            int changed = 0;
            using (GameServices.Events.Subscribe<SettingsChanged>(e => changed++))
            {
                ui.ClickLanguage();
                yield return null;
                Assert.IsTrue(settings.UseKorean, "toggle writes IGameSettings.UseKorean");
                Assert.AreEqual(1, changed, "one SettingsChanged");
                Assert.IsTrue(ui.Korean);
                StringAssert.Contains("일차", ui.FlowTitle);

                settings.UseKorean = false; // changed from outside the UI -> UI follows via SettingsChanged
                yield return null;
                Assert.IsFalse(ui.Korean);
                StringAssert.Contains("Day 1", ui.FlowTitle);
            }
        }

        static GuestSpoke Line(int guest, string text) => new GuestSpoke(new GuestId(9000 + guest), GuestLineTriggers.Spawn, text);

        [UnityTest]
        public IEnumerator SpeechBubbles_MaxThree()
        {
            yield return UITestDriver.LoadMain(); // Intro: no gameplay speech, only ours
            var ui = UITestDriver.UI();
            var bus = GameServices.Events;
            for (int i = 0; i < 5; i++) bus.Publish(Line(i, "line " + i));
            Assert.AreEqual(3, ui.ActiveSpeechCount, "max three bubbles");
            StringAssert.DoesNotContain("line 0", ui.SpeechTexts, "oldest dropped first");
            StringAssert.DoesNotContain("line 1", ui.SpeechTexts);
            StringAssert.Contains("line 4", ui.SpeechTexts);

            bus.Publish(Line(7, "same words"));
            bus.Publish(Line(8, "same words"));
            Assert.AreEqual(3, ui.ActiveSpeechCount);
            Assert.AreEqual(ui.SpeechTexts.IndexOf("same words"), ui.SpeechTexts.LastIndexOf("same words"), "identical text merged: " + ui.SpeechTexts);

            Time.captureDeltaTime = 0.1f;
            yield return UITestDriver.Frames(20); // 2.0 s
            Assert.Greater(ui.ActiveSpeechCount, 0, "still visible at 2 s");
            yield return UITestDriver.Frames(8); // 2.8 s > 2.5 s life
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(0, ui.ActiveSpeechCount, "short life (~2.5 s)");
        }

        [UnityTest]
        public IEnumerator Bubbles_AvoidShaftColumn()
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var guests = GameServices.Get<IGuestQuery>();
            var bus = GameServices.Events;

            // bubbles with no known guest fall back to the screen centre - right on the shaft
            bus.Publish(Line(1, "Where is everybody?"));
            yield return null;
            Assert.IsNull(ui.SpeechShaftOverlap(), "fallback bubble pushed out of the shaft column");

            yield return UITestDriver.ClickStart();
            Time.captureDeltaTime = 0.1f;
            var waiting = new System.Collections.Generic.List<GuestId>();
            for (int i = 0; i < 100 && guests.GetWaiting(FloorIndex.Ground, waiting) < 3; i++) yield return null;
            Time.captureDeltaTime = 0f;
            int n = guests.GetWaiting(FloorIndex.Ground, waiting);
            Assert.GreaterOrEqual(n, 3);
            for (int i = 0; i < n; i++) bus.Publish(new GuestSpoke(waiting[i], GuestLineTriggers.Impatient, "I have been waiting for ages, number " + i));
            // luck popups take priority over speech
            bus.Publish(new LuckJudged(waiting[0], LuckSource.FloorLabel, 1, LuckVerdict.Lucky, FloorIndex.Ground));
            bus.Publish(new LuckJudged(waiting[1], LuckSource.Occupancy, 4, LuckVerdict.Unlucky, FloorIndex.Ground));
            for (int f = 0; f < 4; f++)
            {
                yield return null;
                Assert.LessOrEqual(ui.ActiveSpeechCount, 3);
                Assert.IsNull(ui.SpeechShaftOverlap(), "frame " + f + ": bubble over the shaft");
                Assert.IsNull(ui.SpeechPopupOverlap(), "frame " + f + ": bubble under a luck popup");
            }
            GameplayRoot.SeedOverride = null;
        }
    }

    /// <summary>Phase 3a: day-1 tutorial prompts, title, pause/settings, sign-column avoidance, badges, ending card.</summary>
    public class UIMenuTutorialTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        static IEnumerator BoardLobbyGuests(UIRoot ui)
        {
            var guests = GameServices.Get<IGuestQuery>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var waiting = new System.Collections.Generic.List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= 3);
            Assert.AreEqual(3, guests.GetWaiting(FloorIndex.Ground, waiting), "day 1: three guests wait in the lobby");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TutorialPrompts_FollowDay1Flow()
        {
            Assert.Ignore("ui-dev rewrites for Day 0"); // the Day 0 training shift replaced the old day-1 tutorial flow
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            Assert.IsTrue(GameServices.Get<IGameSettings>().ShowTutorialHints, "hints on by default");
            Assert.IsTrue(GameServices.Get<IDayQuery>().IsTutorial, "day 1 is the tutorial");
            Assert.IsNull(ui.TutorialPrompt, "no prompt on the intro card");
            yield return UITestDriver.ClickStart();
            yield return BoardLobbyGuests(ui);

            // 1. guests waiting where the car is -> OPEN
            Assert.AreEqual(TutorialView.Step.Board, ui.TutorialStep);
            StringAssert.Contains("OPEN", ui.TutorialPrompt);
            StringAssert.Contains("floor 1", ui.TutorialPrompt);
            Assert.AreEqual("Open", ui.TutorialTarget, "arrow + outline on the OPEN button");
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3);
            Assert.AreEqual(3, elevator.Occupancy);
            yield return null;

            // 2. riders inside, doors open -> CLOSE (head count)
            Assert.AreEqual(TutorialView.Step.Close, ui.TutorialStep);
            StringAssert.Contains("CLOSE", ui.TutorialPrompt);
            StringAssert.Contains("HEAD COUNT", ui.TutorialPrompt);
            Assert.AreEqual("Close", ui.TutorialTarget);
            ui.ClickClose();
            yield return UITestDriver.Until(() => elevator.Doors == DoorState.Closed);
            yield return null;

            // first LuckJudged (grandma: 3 people is lucky) -> the "why" card
            Assert.IsNotNull(ui.TutorialWhy, "first judgement explained");
            StringAssert.Contains("LUCKY", ui.TutorialWhy);
            StringAssert.Contains("3", ui.TutorialWhy);

            // 3. doors closed with riders -> press the destination floor
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);
            Assert.IsTrue(guests.TryGet(elevator.Riders[0], out var first));
            var dest = first.DestinationFloor;
            string label = GameServices.Get<IBuildingQuery>().GetLabel(dest).Text;
            StringAssert.Contains("floor " + label, ui.TutorialPrompt);
            Assert.AreEqual("Floor " + dest.PhysicalNumber, ui.TutorialTarget, "arrow on the first rider's floor button");
            ui.ClickFloor(dest.Value);
            yield return UITestDriver.Until(() => elevator.Motion == ElevatorMotion.Idle && elevator.CurrentFloor == dest && !elevator.HasTarget);
            Assert.AreEqual(dest, elevator.CurrentFloor, "arrived");
            yield return null;

            // 4. arrived, doors closed -> OPEN (floor number)
            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
            StringAssert.Contains("FLOOR", ui.TutorialPrompt);
            Assert.AreEqual("Open", ui.TutorialTarget);
            int opened = 0;
            using (GameServices.Events.Subscribe<DoorsOpened>(e => opened++))
            {
                ui.ClickOpen();
                yield return UITestDriver.Until(() => opened > 0);
            }
            yield return null;
            Assert.AreEqual(TutorialView.Step.Done, ui.TutorialStep);
            StringAssert.Contains("tips", ui.TutorialPrompt);
            Assert.IsNull(ui.TutorialTarget, "wrap-up line has no arrow");

            // passed steps never come back, the wrap-up fades
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 80; i++)
            {
                yield return null;
                var p = ui.TutorialPrompt;
                if (p != null) StringAssert.DoesNotContain("waiting", p, "frame " + i);
                if (p != null) StringAssert.DoesNotContain("CLOSE", p, "frame " + i);
            }
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(TutorialView.Step.Finished, ui.TutorialStep);
            Assert.IsNull(ui.TutorialPrompt);
        }

        [UnityTest]
        public IEnumerator TutorialHints_Off_HidesPrompts()
        {
            Assert.Ignore("ui-dev rewrites for Day 0"); // the Day 0 training shift replaced the old day-1 tutorial flow
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            settings.ShowTutorialHints = false;
            yield return UITestDriver.ClickStart();
            yield return BoardLobbyGuests(ui);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.IsNull(ui.TutorialPrompt, "hints off: no prompt");
                Assert.IsNull(ui.TutorialTarget, "hints off: no arrow");
            }
            settings.ShowTutorialHints = true;
            yield return null;
            StringAssert.Contains("OPEN", ui.TutorialPrompt, "hints back on: the current step shows");
        }

        /// <summary>Canvas x-range covered by Rendering's floor-sign plates ("Sign n" objects).</summary>
        static bool SignColumn(UIRoot ui, out float minX, out float maxX)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            int n = GameServices.Get<IBuildingQuery>().FloorCount, found = 0;
            for (int f = 1; f <= n; f++)
            {
                var sign = GameObject.Find("Sign " + f);
                if (sign == null) continue;
                foreach (var r in sign.GetComponentsInChildren<Renderer>())
                {
                    var b = r.bounds;
                    if (!ui.WorldToCanvas(new Vector3(b.min.x, b.center.y, b.min.z), out var a)) continue;
                    if (!ui.WorldToCanvas(new Vector3(b.max.x, b.center.y, b.min.z), out var c)) continue;
                    minX = Mathf.Min(minX, a.x, c.x);
                    maxX = Mathf.Max(maxX, a.x, c.x);
                    found++;
                }
            }
            return found > 0;
        }

        [UnityTest]
        public IEnumerator Bubbles_AvoidSignColumn()
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var bus = GameServices.Events;
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            yield return UITestDriver.ClickStart();
            yield return BoardLobbyGuests(ui);
            Assert.IsTrue(SignColumn(ui, out float sMin, out float sMax), "floor signs rendered");
            Assert.Greater(sMax - sMin, 4f, "sign column has width");

            // waiting guests talk: their chips + bubbles stay right of the signs
            var waiting = new System.Collections.Generic.List<GuestId>();
            int n = guests.GetWaiting(FloorIndex.Ground, waiting);
            for (int i = 0; i < n; i++) bus.Publish(new GuestSpoke(waiting[i], GuestLineTriggers.Impatient, "Waiting here for a long time, number " + i));
            for (int f = 0; f < 3; f++)
            {
                yield return null;
                Assert.IsNull(ui.SpeechOverlapX(sMin, sMax), "waiting: bubble over the floor signs");
                Assert.IsNull(ui.ChipOverlapX(sMin, sMax), "waiting: chip over the floor signs");
                Assert.IsNull(ui.SpeechShaftOverlap(), "waiting: bubble over the shaft");
            }

            // riders talk while the car moves: their grid + bubbles stay left of the signs
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3);
            ui.ClickFloor(6);
            yield return UITestDriver.Until(() => elevator.Motion != ElevatorMotion.Idle, 40);
            var riders = elevator.Riders;
            Assert.Greater(riders.Count, 0);
            for (int i = 0; i < riders.Count; i++) bus.Publish(new GuestSpoke(riders[i], GuestLineTriggers.Board, "Riding up now, me number " + i));
            Time.captureDeltaTime = 0.05f;
            for (int f = 0; f < 6; f++)
            {
                yield return null;
                Assert.IsTrue(SignColumn(ui, out sMin, out sMax));
                Assert.IsNull(ui.SpeechOverlapX(sMin, sMax), "frame " + f + ": rider bubble over the floor signs");
                Assert.IsNull(ui.ChipOverlapX(sMin, sMax), "frame " + f + ": rider chip over the floor signs");
                Assert.IsNull(ui.SpeechShaftOverlap(), "frame " + f + ": rider bubble over the shaft");
            }
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator Badges_AreOneGridStepBigger()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            for (int f = 0; f < ui.FloorButtonCount; f++)
            {
                Assert.GreaterOrEqual(ui.BadgeFontSize(f), 24f, "panel badge " + f + " at 24px");
                Assert.GreaterOrEqual(ui.BadgeText(f).Length, 3, "badge " + f + " readable: " + ui.BadgeText(f));
            }
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "still on the 12px grid");
        }

        [UnityTest]
        public IEnumerator Title_StartButton_StartsDay1()
        {
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.TitleVisible, "title card");
            StringAssert.Contains("FLOOR F", ui.TitleText);
            StringAssert.Contains("4th floor", ui.TitleText);
            Assert.AreEqual(DayPhase.None, ui.VisibleFlowScreen, "no day card over the title");
            Assert.IsFalse(ui.PauseButtonVisible, "no pause on the title");

            // attract demo runs behind it without popping HUD feedback on top
            yield return UITestDriver.Until(() => false, 30);
            Assert.AreEqual(0, ui.VisibleBubbleCount, "no number chips over the demo");
            Assert.IsTrue(ui.TitleVisible);

            ui.ClickTitleLanguage();
            yield return null;
            Assert.IsTrue(ui.Korean, "title language toggle");
            StringAssert.Contains("4층", ui.TitleText);
            ui.ClickTitleLanguage();
            yield return null;
            Assert.IsFalse(ui.Korean);

            ui.ClickTitleStart();
            Assert.AreEqual(DayPhase.Intro, day.Phase, "START -> Day 0 Intro");
            Assert.AreEqual(1, day.CurrentDay.Number);
            Assert.IsTrue(day.IsTutorial, "a new game starts on the Day 0 training shift");
            yield return null;
            Assert.IsFalse(ui.TitleVisible);
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            StringAssert.Contains("Day 0", ui.FlowTitle);
            yield return UITestDriver.ClickStart();
            // (tutorial step assertion removed: ui-dev rewrites TutorialView for the Day 0 lessons)
        }

        [UnityTest]
        public IEnumerator Pause_Menu_ResumesAndReturnsToTitle()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var menu = GameServices.Get<IMenuFlow>();
            var day = GameServices.Get<IDayQuery>();
            yield return UITestDriver.ClickStart();
            yield return null;
            Assert.IsTrue(ui.PauseButtonVisible, "HUD pause button while running");

            ui.ClickPauseButton();
            Assert.IsTrue(menu.IsPaused, "pause button -> SetPaused(true)");
            yield return null;
            Assert.IsTrue(ui.PauseVisible, "pause card on PauseChanged");
            Assert.IsFalse(ui.PauseButtonVisible);
            float t = day.ElapsedSeconds;
            yield return UITestDriver.Until(() => false, 10);
            Assert.AreEqual(t, day.ElapsedSeconds, 1e-4f, "sim clock frozen");
            Assert.IsNull(ui.TutorialPrompt, "no tutorial prompt over the pause card");

            ui.ClickResume();
            Assert.IsFalse(menu.IsPaused, "RESUME");
            yield return null;
            Assert.IsFalse(ui.PauseVisible);
            yield return UITestDriver.Until(() => false, 5);
            Assert.Greater(day.ElapsedSeconds, t, "clock runs again");

            menu.SetPaused(true); // e.g. Esc (KeyboardInput) - the UI follows PauseChanged
            yield return null;
            Assert.IsTrue(ui.PauseVisible);
            ui.ClickQuitToTitle();
            Assert.IsTrue(menu.IsOnTitle, "QUIT TO TITLE -> ReturnToTitle");
            Assert.IsFalse(menu.IsPaused);
            yield return null;
            Assert.IsFalse(ui.PauseVisible);
            Assert.IsTrue(ui.TitleVisible);
            Assert.AreEqual(DayPhase.Title, day.Phase);
        }

        [UnityTest]
        public IEnumerator VolumeSlider_WritesSettings()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            yield return UITestDriver.ClickStart();
            ui.ClickPauseButton();
            yield return null;
            Assert.IsTrue(ui.PauseVisible);

            ui.SetVolumeSlider(0.25f);
            Assert.AreEqual(0.25f, settings.MasterVolume, 1e-4f, "slider -> IGameSettings.MasterVolume");
            settings.MasterVolume = 0.8f; // changed elsewhere -> slider follows
            yield return null;
            Assert.AreEqual(0.8f, ui.VolumeSliderValue, 1e-4f, "slider follows SettingsChanged");

            Assert.IsTrue(settings.ShowTutorialHints);
            ui.ClickHintsToggle();
            Assert.IsFalse(settings.ShowTutorialHints, "hints toggle -> IGameSettings.ShowTutorialHints");
            StringAssert.Contains("OFF", ui.HintsText);
            ui.ClickHintsToggle();
            Assert.IsTrue(settings.ShowTutorialHints);
            settings.MasterVolume = 1f;
            ui.ClickResume();
        }

        [UnityTest]
        public IEnumerator Ending_ShowsStats_PlayAgainAndTitle()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return null;
            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen, "ending card");
            Assert.IsFalse(string.IsNullOrEmpty(ui.FlowTitle));
            Assert.IsTrue(ui.EndingTitleButtonVisible, "TITLE button next to PLAY AGAIN");
            StringAssert.Contains("PLAY AGAIN", ui.FlowActionText);
            ui.ClickEndingTitle();
            Assert.AreEqual(DayPhase.Title, day.Phase, "TITLE -> ReturnToTitle");
            yield return null;
            Assert.IsTrue(ui.TitleVisible);
        }
    }

    /// <summary>L2 captures of the title, tutorial, pause and ending (run with -Filter Capture).</summary>
    public class UIMenuCaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_MenuAndTutorial() => Flow("", false);

        [UnityTest]
        public IEnumerator Capture_MenuAndTutorial_Korean() => Flow("_ko", true);

        static IEnumerator Flow(string suffix, bool korean)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = korean;
            yield return UITestDriver.Until(() => false, 40); // demo plays behind the title
            yield return null;
            Assert.IsTrue(ui.TitleVisible);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_title" + suffix)));

            ui.ClickCredits();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.CreditsVisible);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_credits" + suffix)));
            ui.ClickCreditsBack();
            yield return null;

            ui.ClickTitleStart();
            yield return null;
            yield return UITestDriver.ClickStart();
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new System.Collections.Generic.List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= 3);
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3);
            ui.ClickClose();
            yield return UITestDriver.Until(() => elevator.Doors == DoorState.Closed);
            yield return UITestDriver.Frames(2);
            Assert.IsNotNull(ui.TutorialPrompt, "floor prompt");
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_tutorial" + suffix)));

            ui.ClickPauseButton();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.PauseVisible);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_pause" + suffix)));
            ui.ClickResume();
            yield return null;

            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_ending" + suffix)));
            GameServices.Get<IGameSettings>().UseKorean = false;
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
        public IEnumerator Capture_UiStates() => CaptureFlow("", korean: false, fontCompare: true);

        [UnityTest]
        public IEnumerator Capture_UiStates_Korean() => CaptureFlow("_ko", korean: true, fontCompare: false);

        static IEnumerator CaptureFlow(string suffix, bool korean, bool fontCompare)
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            settings.UseKorean = korean;
            yield return UITestDriver.Frames(10);
            Assert.AreEqual(korean, ui.Korean);
            if (fontCompare)
            {
                ui.ShowFontCompare(true);
                yield return UITestDriver.Frames(2);
                Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_font_compare")));
                ui.ShowFontCompare(false);
                yield return null;
            }
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_intro" + suffix)));

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
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_running" + suffix)));

            var s = UITestDriver.Session();
            s.Score.AddTips(60, earned: true);
            s.EndDayNow();
            yield return null;
            ui.ClickFlowButton();
            yield return null;
            ui.ClickShopOffer(0);
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_shop" + suffix)));
            settings.UseKorean = false;
            GameplayRoot.SeedOverride = null;
        }
    }
}
