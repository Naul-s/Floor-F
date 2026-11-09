using System.Collections;
using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// Title attract screen ("press any key" -> reveal), scenic view (V / HUD eye / pause menu), the establishing shot at
    /// each real day's Intro, the rule box folding into a "?" tab, and the golden click effect. Each test switches on the
    /// UIFeatures it is about (they are off for every other test).
    /// </summary>
    public class UICinematicsTests
    {
        readonly List<UiSoundKind> sounds = new List<UiSoundKind>();
        readonly List<ScenicViewChanged> scenicEvents = new List<ScenicViewChanged>();
        System.IDisposable soundSub, scenicSub;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            sounds.Clear();
            scenicEvents.Clear();
            soundSub = GameServices.Events.Subscribe<UiSound>(e => sounds.Add(e.Kind));
            scenicSub = GameServices.Events.Subscribe<ScenicViewChanged>(e => scenicEvents.Add(e));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            soundSub?.Dispose();
            scenicSub?.Dispose();
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static IEnumerator Settle(System.Func<bool> done, int frames = 60)
        {
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < frames && !done(); i++) yield return null;
            Time.captureDeltaTime = 0f;
        }

        /// <summary>Plays days through the session until the Intro that matches <paramref name="want"/>.</summary>
        static IEnumerator ToIntro(System.Func<IDayQuery, bool> want, string what)
        {
            var day = GameServices.Get<IDayQuery>();
            var s = UITestDriver.Session();
            for (int guard = 0; guard < 60 && !(day.Phase == DayPhase.Intro && want(day)); guard++)
            {
                switch (day.Phase)
                {
                    case DayPhase.Intro: s.BeginDay(); break;
                    case DayPhase.Running: s.EndDayNow(); break;
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop: s.FinishShopping(); break;
                    default: Assert.Fail("unexpected phase " + day.Phase + " before " + what); break;
                }
                yield return null;
            }
            Assert.IsTrue(day.Phase == DayPhase.Intro && want(day), "reached the " + what + " intro");
            yield return UITestDriver.Frames(2);
        }

        // ------------------------------------------------------------------ title attract screen

        [UnityTest]
        public IEnumerator Capture_TitleAttract_KeyReveals() => Attract(false, key: true);

        [UnityTest]
        public IEnumerator Capture_TitleAttract_Korean_ClickReveals() => Attract(true, key: false);

        IEnumerator Attract(bool ko, bool key)
        {
            UIFeatures.AttractScreen = true;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.Frames(3);
            var size = ui.CanvasSize;

            Assert.IsTrue(ui.TitleVisible && ui.TitleAttract, "boots into the attract state");
            Assert.IsFalse(ui.TitleButtonsVisible, "no menu buttons yet");
            Assert.IsFalse(ui.TitleCreditStripVisible, "no credit strip");
            Assert.AreEqual(0f, ui.TitleBackdropAlpha, 0.001f, "no dim: the whole city is visible");
            Assert.Greater(ui.TitlePartRect(MenuView.TitlePart.Logo).width, 100f, "the logo shows");
            Assert.IsTrue(ui.TitlePromptVisible);
            Assert.AreEqual(ko ? "아무 키나 누르거나 클릭하세요" : "PRESS ANY KEY OR CLICK", ui.TitlePromptText);
            var prompt = ui.TitlePromptRect;
            Assert.AreEqual(size.x * 0.5f, prompt.center.x, 1f, "prompt centred");
            Assert.Less(prompt.yMax, size.y * 0.25f, "near the bottom: " + prompt);
            Assert.Greater(prompt.yMin, 24f, "above the bottom edge");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "texts on the grid");
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Enter belongs to the reveal, not 'new game'");

            // blink: a smooth breath, <= 1.5 Hz, never fully off (photosensitivity: far below 3 flashes/s)
            int dips = 0;
            float prev = MenuView.PromptAlpha(0f), prevD = 0f, min = 1f;
            for (float t = 0.01f; t <= 10f; t += 0.01f)
            {
                float a = MenuView.PromptAlpha(t), d = a - prev;
                if (prevD < 0f && d >= 0f) dips++;
                min = Mathf.Min(min, a);
                prev = a; prevD = d;
            }
            Assert.LessOrEqual(dips, 15, "<= 1.5 blinks per second");
            Assert.Greater(dips, 3, "it does blink");
            Assert.GreaterOrEqual(min, 0.3f, "readable at its dimmest");
            yield return Day0Driver.Shot("ui_title_attract" + (ko ? "_ko" : ""), true);

            // any key / click -> reveal
            int fxBefore = ui.ClickFxCount;
            if (key) ui.InjectPress(enter: true);
            else ui.InjectPress(pointer: true, canvasPoint: new Vector2(size.x * 0.3f, size.y * 0.5f));
            yield return null;
            Assert.IsFalse(ui.TitleAttract, "revealed");
            Assert.IsTrue(ui.TitleRevealing, "the reveal animation plays");
            Assert.AreEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase, "Enter / click did not start a game");
            CollectionAssert.Contains(sounds, UiSoundKind.Reveal);
            CollectionAssert.DoesNotContain(sounds, UiSoundKind.Tap, "the reveal click has its own sound");
            Assert.AreEqual(fxBefore + 1, ui.ClickFxCount, "one effect: the burst (no extra click ring)");
            Assert.IsTrue(ui.ClickFxLastBig, "the big golden burst");
            Assert.AreEqual(prompt.center.x, ui.ClickFxLastPosition.x, 2f, "bursting out of the prompt");
            Assert.IsTrue(ui.TitleButtonsVisible, "buttons come in");
            Assert.AreEqual(false, ui.FlowEnterEnabled, "still the UI's while revealing");

            yield return Settle(() => !ui.TitleRevealing);
            Assert.IsFalse(ui.TitleRevealing);
            Assert.AreEqual(MenuView.Backdrop.a, ui.TitleBackdropAlpha, 0.001f, "the usual dim");
            Assert.IsTrue(ui.TitleCreditStripVisible);
            Assert.IsFalse(ui.TitlePromptVisible, "prompt gone");
            Assert.AreEqual(MenuView.StartSize.x, ui.TitlePartRect(MenuView.TitlePart.Start).width, 0.5f, "START settled at full size");
            Assert.Greater(ui.TitlePartRect(MenuView.TitlePart.Start).yMin, ui.TitlePartRect(MenuView.TitlePart.Endings).yMax, "stack order kept");
            Assert.AreEqual(true, ui.FlowEnterEnabled, "Enter starts a game again from the menu");
            yield return UITestDriver.Frames(2);
            yield return Day0Driver.Shot("ui_title_revealed" + (ko ? "_ko" : ""), !ko);

            // leaving and coming back: the attract state again
            ui.ClickTitleStart();
            yield return UITestDriver.Frames(2);
            Assert.AreNotEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase);
            GameServices.Get<IMenuFlow>().ReturnToTitle();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.TitleAttract, "return to title -> attract");
            Assert.IsFalse(ui.TitleButtonsVisible);
        }

        [UnityTest]
        public IEnumerator Attract_Off_ShowsMenuAtOnce()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.TitleAttract, "tests see the menu by default");
            Assert.IsTrue(ui.TitleButtonsVisible);
            Assert.AreEqual(true, ui.FlowEnterEnabled);
            ui.InjectPress(v: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode, "no scenic view on the title");
        }

        // ------------------------------------------------------------------ scenic view

        [UnityTest]
        public IEnumerator Capture_Scenic_VKey_PausesHidesAndReturns()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            var menu = GameServices.Get<IMenuFlow>();
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.ScenicButtonVisible, "eye button while running");
            var eye = ui.ScenicButtonRect;
            var size = ui.CanvasSize;
            Assert.GreaterOrEqual(eye.yMax, size.y - 60f, "top area");
            Assert.Less(eye.xMax, ui.LuckBoxRect.xMin, "clear of the luck meter");
            Assert.Greater(eye.xMin, ui.PanelRect.xMax - 1f, "right of the day box / panel column");
            Assert.LessOrEqual(eye.width, 44f, "small");
            Assert.AreEqual("ScenicButton", ui.RaycastAt(eye.center), "clickable");

            ui.InjectPress(v: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode);
            Assert.IsTrue(menu.IsPaused, "the game is paused");
            Assert.IsFalse(ui.PauseVisible, "but no pause card");
            Assert.IsTrue(ui.HudHidden);
            Assert.IsFalse(ui.HudBlocksRaycasts, "no hidden button takes clicks");
            Assert.IsTrue(ui.ScenicHintVisible);
            Assert.AreEqual("V / click: back", ui.ScenicHintText);
            Assert.AreEqual(1, scenicEvents.Count);
            Assert.IsTrue(scenicEvents[0].On && !scenicEvents[0].Establishing);
            yield return Settle(() => ui.HudAlpha < 0.01f, 20);
            Assert.Less(ui.HudAlpha, 0.01f, "the whole UI faded out");
            var hint = ui.ScenicHintRect;
            Assert.AreEqual(size.x * 0.5f, hint.center.x, 1f);
            Assert.Less(hint.yMax, 80f, "tiny, at the bottom");
            yield return Day0Driver.Shot("ui_scenic_hint", true);

            // the hint fades after ~2 s, the view stays
            yield return Settle(() => !ui.ScenicHintVisible, 80);
            Assert.IsFalse(ui.ScenicHintVisible, "hint faded");
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode, "still scenic");

            // any key returns (applied in LateUpdate)
            ui.InjectPress(anyKey: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.IsFalse(menu.IsPaused, "unpaused");
            Assert.IsFalse(ui.PauseVisible);
            Assert.IsFalse(ui.HudHidden);
            Assert.AreEqual(2, scenicEvents.Count);
            Assert.IsTrue(!scenicEvents[1].On && !scenicEvents[1].Establishing);
            yield return Settle(() => ui.HudAlpha > 0.99f, 20);
            Assert.Greater(ui.HudAlpha, 0.99f, "UI back");

            // the HUD button, then a click returns (with the click effect + Tap)
            ui.ClickScenicButton();
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode);
            CollectionAssert.Contains(sounds, UiSoundKind.Click);
            yield return null;
            sounds.Clear();
            ui.InjectPress(pointer: true, canvasPoint: size * 0.5f);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode, "click returns");
            Assert.IsFalse(menu.IsPaused);
            Assert.AreEqual(4, scenicEvents.Count);
            CollectionAssert.Contains(sounds, UiSoundKind.Tap);

            // something else unpauses (Esc through KeyboardInput): the view ends with it
            ui.InjectPress(v: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode);
            menu.SetPaused(false);
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.AreEqual(6, scenicEvents.Count);
            Assert.IsFalse(scenicEvents[5].On);
        }

        [UnityTest]
        public IEnumerator Scenic_FromPauseMenu_ReturnsToPauseMenu_NotOnEnding()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            var menu = GameServices.Get<IMenuFlow>();
            yield return UITestDriver.ClickStart();
            yield return null;
            ui.ClickPauseButton();
            yield return null;
            Assert.IsTrue(ui.PauseVisible);
            Assert.IsTrue(ui.PauseScenicVisible, "SCENIC VIEW in the pause menu");
            Assert.AreEqual("SCENIC VIEW  [V]", ui.PauseScenicText);
            Assert.IsNull(ui.FirstOffGridOrForeignText());
            ui.ClickPauseScenic();
            yield return null;
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode);
            Assert.IsTrue(menu.IsPaused);
            Assert.IsFalse(ui.PauseVisible, "pause card hidden while scenic");
            ui.InjectPress(anyKey: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.IsTrue(menu.IsPaused, "back to the pause menu it came from");
            Assert.IsTrue(ui.PauseVisible);
            ui.ClickResume();
            yield return null;

            // Intro / Summary / Shop: V works; Ending: not
            UITestDriver.Session().EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, GameServices.Get<IDayQuery>().Phase);
            ui.InjectPress(v: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode, "summary: scenic works");
            ui.InjectPress(anyKey: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            ui.InjectPress(v: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode, "never on the ending");
            Assert.IsFalse(ui.ScenicButtonVisible);
        }

        // ------------------------------------------------------------------ establishing shot

        [UnityTest]
        public IEnumerator Establishing_Off_InTestsByDefault()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode, "no shot unless a test asks for it");
            Assert.AreEqual(0, scenicEvents.Count);
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
        }

        [UnityTest]
        public IEnumerator Capture_Establishing_Day1SkipAndDay7()
        {
            UIFeatures.EstablishingShots = true;
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            Assert.AreEqual(ScenicView.Mode.Establishing, ui.ScenicMode, "Day 1 intro opens on the city");
            Assert.IsTrue(ui.HudHidden);
            Assert.AreEqual(1, scenicEvents.Count);
            Assert.IsTrue(scenicEvents[0].On && scenicEvents[0].Establishing);
            Assert.IsTrue(ui.CaptionVisible);
            string plain = System.Text.RegularExpressions.Regex.Replace(ui.CaptionText, "<[^>]+>", "");
            Assert.AreEqual("DAY 1  ·  First Shift", plain);
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen, "the card is waiting under it");
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Enter skips the shot, it does not start the day");

            // any key skips
            ui.InjectPress(enter: true);
            yield return null;
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.AreEqual(DayPhase.Intro, day.Phase, "still the intro");
            Assert.AreEqual(2, scenicEvents.Count);
            Assert.IsTrue(!scenicEvents[1].On && scenicEvents[1].Establishing);
            Assert.IsFalse(ui.HudHidden);
            Assert.IsFalse(ui.CaptionVisible);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(true, ui.FlowEnterEnabled, "Enter is the flow key again");

            // every later day gets one; Day 7's
            yield return ToIntro(d => d.IsStarNight, "star night");
            Assert.AreEqual(ScenicView.Mode.Establishing, ui.ScenicMode, "day 7 too");
            plain = System.Text.RegularExpressions.Regex.Replace(ui.CaptionText, "<[^>]+>", "");
            StringAssert.Contains("DAY 7  ·  Night of Falling Stars", plain);
            StringAssert.Contains("★", plain);
            ui.SetEstablishingAge(1.3f);
            yield return null;
            Assert.AreEqual(1f, ui.CaptionAlpha, 0.01f, "caption fully in mid-shot");
            var cap = ui.CaptionRect;
            var size = ui.CanvasSize;
            Assert.AreEqual(size.x * 0.5f, cap.center.x, 1f, "bottom-centre");
            Assert.Less(cap.yMax, size.y * 0.2f, "a lower third, the sky stays free: " + cap);
            Assert.IsTrue(cap.xMin >= 0f && cap.xMax <= size.x, "on screen");
            yield return Day0Driver.Shot("ui_establishing_day7", true);

            // it ends by itself: the intro card comes back
            int before = scenicEvents.Count;
            ui.SetEstablishingAge(ScenicView.EstablishingTime - 0.001f);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.AreEqual(before + 1, scenicEvents.Count);
            Assert.IsFalse(scenicEvents[scenicEvents.Count - 1].On);
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            yield return Settle(() => ui.HudAlpha > 0.99f, 20);
            Assert.Greater(ui.HudAlpha, 0.99f);
        }

        [UnityTest]
        public IEnumerator Establishing_SkippedOnDay0()
        {
            UIFeatures.EstablishingShots = true;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            Assert.IsTrue(GameServices.Get<IDayQuery>().IsTutorial);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode, "Day 0: straight to playing");
            ui.ClickSkipTutorial();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsTutorial);
            Assert.AreEqual(ScenicView.Mode.Establishing, ui.ScenicMode, "the first real day gets its shot");
        }

        // ------------------------------------------------------------------ rule box

        [UnityTest]
        public IEnumerator Capture_RuleBox_CollapsesFromDay2_RemembersChoice()
        {
            UIFeatures.RulesAutoCollapse = true;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            var s = UITestDriver.Session();
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.RulesOpen && ui.RulesBoxShown && !ui.RulesTabShown, "Day 1: open");
            Assert.AreEqual("HIDE [H]", ui.RulesHideText);
            Assert.IsNull(ui.RulesOverflow());

            // Day 2: folded into the tab
            yield return ToIntro(d => d.DisplayDay == 2, "Day 2");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.RulesOpen, "Day 2: collapsed");
            Assert.IsFalse(ui.RulesBoxShown);
            Assert.IsTrue(ui.RulesTabShown);
            var tab = ui.RulesTabRect;
            var size = ui.CanvasSize;
            Assert.AreEqual(size.x - 24f, tab.xMax, 1f, "right column edge");
            Assert.AreEqual(size.y - HudView.RulesTop, tab.yMax, 1f, "where the box starts");
            Assert.LessOrEqual(tab.width * tab.height, 48f * 36f + 1f, "small");
            Assert.AreEqual("RulesTab", ui.RaycastAt(tab.center), "clickable");
            Assert.AreEqual(HudView.RulesTop + HudView.TabH, ui.RightColumnBottom, 0.5f, "charms move up under the tab");
            Assert.IsNull(ui.FirstOffGridOrForeignText());
            yield return Day0Driver.Shot("ui_rulebox_collapsed", true);

            // H opens, HIDE closes, the tab opens
            ui.InjectPress(h: true);
            yield return null;
            Assert.IsTrue(ui.RulesOpen && ui.RulesBoxShown && !ui.RulesTabShown, "H opens");
            ui.ClickRulesHide();
            Assert.IsTrue(!ui.RulesOpen && !ui.RulesBoxShown && ui.RulesTabShown, "HIDE folds");
            ui.ClickRulesTab();
            Assert.IsTrue(ui.RulesOpen && ui.RulesBoxShown, "the tab unfolds");

            // the choice sticks on later days
            yield return ToIntro(d => d.DisplayDay == 3, "Day 3");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.RulesOpen && ui.RulesBoxShown, "remembered: open on Day 3");
        }

        // ------------------------------------------------------------------ click effect

        [UnityTest]
        public IEnumerator Capture_ClickFx_SpawnsOnTop_NeverBlocks()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = false; // no explain cards over the HUD
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.FxCanvasAbove, "FX canvas drawn above the HUD on the same UI camera");
            Assert.IsFalse(ui.ClickFxRaycastable, "no raycaster, no raycast target");
            Assert.IsTrue(ui.AllOnUiLayer);

            // a click on the sky: ring + Tap
            var size = ui.CanvasSize;
            var sky = new Vector2(size.x * 0.5f, size.y * 0.72f);
            string skyHit = ui.RaycastAt(sky);
            sounds.Clear();
            ui.InjectPress(pointer: true, canvasPoint: sky);
            yield return null;
            Assert.AreEqual(1, ui.ClickFxCount, "one effect");
            Assert.IsFalse(ui.ClickFxLastBig);
            Assert.AreEqual(sky.x, ui.ClickFxLastPosition.x, 1.5f);
            Assert.AreEqual(sky.y, ui.ClickFxLastPosition.y, 1.5f);
            Assert.IsTrue(ui.ClickFxOnGrid, "hard pixels on the grid");
            if (skyHit == null) CollectionAssert.AreEqual(new[] { UiSoundKind.Tap }, sounds, "a click on the world taps once");
            Assert.AreEqual(skyHit, ui.RaycastAt(sky), "the effect does not take the click point");

            // a click on a floor button: ring, the button still on top, no Tap (the button sounds itself)
            var fb = ui.FloorButtonRect(3);
            string fbHit = ui.RaycastAt(fb.center);
            Assert.IsNotNull(fbHit);
            sounds.Clear();
            ui.InjectPress(pointer: true, canvasPoint: fb.center);
            yield return null;
            Assert.AreEqual(2, ui.ClickFxCount);
            Assert.AreEqual(fbHit, ui.RaycastAt(fb.center), "the button stays clickable under the effect");
            CollectionAssert.DoesNotContain(sounds, UiSoundKind.Tap);

            var open = ui.OpenButtonRect;
            ui.InjectPress(pointer: true, canvasPoint: open.center);
            Time.captureDeltaTime = 0.03f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(3, ui.ClickFxCount);
            yield return Day0Driver.Shot("ui_click_fx", true);

            yield return Settle(() => ui.ClickFxCount == 0, 30);
            Assert.AreEqual(0, ui.ClickFxCount, "gone in well under a second");
        }
    }
}
