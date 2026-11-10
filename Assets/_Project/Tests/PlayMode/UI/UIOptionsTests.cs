using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// Title OPTIONS (in the old language toggle's slot): the pause card's settings (volume, language, tutorial hints)
    /// + BACK, Esc closes it, Enter does not start a game behind it, the attract reveal still brings the button in. The
    /// language toggle exists only on the OPTIONS card and the pause card - nowhere else, in any phase.
    /// </summary>
    public class UIOptionsTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        [UnityTest]
        public IEnumerator Capture_TitleOptions() => Options(false);

        [UnityTest]
        public IEnumerator Capture_TitleOptions_Korean() => Options(true);

        static IEnumerator Options(bool ko)
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            settings.UseKorean = ko;
            yield return UITestDriver.Frames(2);
            string tag = ko ? "ko" : "en";

            // OPTIONS where the language toggle was, same look as CREDITS
            Assert.IsTrue(ui.TitleButtonsVisible);
            StringAssert.Contains(ko ? "설정" : "OPTIONS", ui.TitleText);
            StringAssert.DoesNotContain("EN | ko", ui.TitleText, "no language toggle on the title menu");
            var options = ui.TitlePartRect(MenuView.TitlePart.Options);
            var credits = ui.TitlePartRect(MenuView.TitlePart.Credits);
            Assert.AreEqual(credits.size.x, options.size.x, 0.5f, "same size as CREDITS");
            Assert.AreEqual(credits.size.y, options.size.y, 0.5f);
            Assert.Less(options.yMax, credits.yMin, "last in the stack");
            Assert.IsTrue(ui.TitleLabelFits(MenuView.TitlePart.Options, out float font), tag + ": label fits");
            Assert.AreEqual(24f, font, tag + ": label at 24px");
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), tag + ": title menu");
            Assert.AreEqual(true, ui.FlowEnterEnabled, "Enter starts a game from the menu");
            yield return Day0Driver.Shot("ui_title_menu_options" + (ko ? "_ko" : ""), true);

            // open
            ui.ClickTitleOptions();
            yield return null;
            Assert.IsTrue(ui.OptionsVisible, "OPTIONS card");
            Assert.IsFalse(ui.TitleMenuVisible, "the logo + buttons step aside");
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Enter does not start a game behind the card");
            string text = ui.OptionsText;
            StringAssert.Contains(ko ? "설정" : "OPTIONS", text);
            StringAssert.Contains(ko ? "en | 한국어" : "EN | ko", text, "language");
            StringAssert.Contains(ko ? "튜토리얼 힌트: 켜짐" : "TUTORIAL HINTS: ON", text, "hints");
            StringAssert.Contains(ko ? "뒤로" : "BACK", text, "back");
            Assert.IsNull(ui.OptionsOverflow(), tag + ": labels fit");
            var card = ui.OptionsCardRect;
            var size = ui.CanvasSize;
            Assert.IsTrue(card.xMin >= 0f && card.yMin >= 0f && card.xMax <= size.x && card.yMax <= size.y, "card on screen " + card);
            Assert.LessOrEqual(card.height, UIStyle.RefHeight - 40f, "fits a 720-high screen");
            Assert.AreEqual(size.x * 0.5f, card.center.x, 1f, "centred");
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), tag + ": options card open");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), tag + ": texts on the grid");
            yield return Day0Driver.Shot("ui_title_options" + (ko ? "_ko" : ""), true);

            // the same settings as the pause card
            ui.SetOptionsVolume(0.4f);
            Assert.AreEqual(0.4f, settings.MasterVolume, 0.001f, "volume -> IGameSettings");
            Assert.AreEqual(0.4f, ui.VolumeSliderValue, 0.001f, "the pause card's slider follows");
            ui.ClickOptionsHints();
            yield return null;
            Assert.IsFalse(settings.ShowTutorialHints, "hints off");
            Assert.AreEqual(ko ? "튜토리얼 힌트: 꺼짐" : "TUTORIAL HINTS: OFF", ui.HintsText, "the pause card's label follows");
            ui.ClickOptionsHints();
            yield return null;
            Assert.IsTrue(settings.ShowTutorialHints);
            ui.ClickOptionsLanguage();
            yield return null;
            Assert.AreEqual(!ko, settings.UseKorean, "language flipped");
            Assert.IsTrue(ui.OptionsVisible, "card stays open");
            StringAssert.Contains(ko ? "OPTIONS" : "설정", ui.OptionsText, "card re-rendered");
            Assert.IsNull(ui.OptionsOverflow(), "other language: labels fit");
            ui.ClickOptionsLanguage();
            yield return null;
            Assert.AreEqual(ko, settings.UseKorean, "and back");

            // BACK / Esc close it; the menu comes back and Enter is the flow key again
            ui.ClickOptionsBack();
            yield return null;
            Assert.IsFalse(ui.OptionsVisible, "BACK closes");
            Assert.IsTrue(ui.TitleMenuVisible);
            Assert.AreEqual(true, ui.FlowEnterEnabled);
            ui.ClickTitleOptions();
            yield return null;
            Assert.IsTrue(ui.OptionsVisible);
            ui.CloseOptionsLikeEsc();
            yield return null;
            Assert.IsFalse(ui.OptionsVisible, "Esc closes");
            Assert.IsTrue(ui.TitleMenuVisible);
            Assert.AreEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase, "still on the title");
        }

        [UnityTest]
        public IEnumerator Attract_RevealBringsOptionsIn_OptionsOpenFromRevealedMenu()
        {
            UIFeatures.AttractScreen = true;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(3);
            Assert.IsTrue(ui.TitleAttract);
            Assert.IsFalse(ui.OptionsVisible);
            ui.InjectPress(enter: true);
            yield return null;
            Assert.IsFalse(ui.TitleAttract, "Enter revealed the menu");
            Assert.IsFalse(ui.OptionsVisible, "the reveal opens nothing");
            yield return Settle(() => !ui.TitleRevealing);
            var options = ui.TitlePartRect(MenuView.TitlePart.Options);
            Assert.AreEqual(MenuView.ButtonSize.x, options.width, 0.5f, "OPTIONS settled at full size");
            Assert.AreEqual(true, ui.FlowEnterEnabled);
            ui.ClickTitleOptions();
            yield return null;
            Assert.IsTrue(ui.OptionsVisible);
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Enter held while the card is open");
            ui.InjectPress(anyKey: true);
            yield return null;
            Assert.IsTrue(ui.OptionsVisible, "a key press does not close / restart anything");
            ui.ClickOptionsBack();
            yield return null;
            Assert.IsFalse(ui.OptionsVisible);
            Assert.AreEqual(true, ui.FlowEnterEnabled);
        }

        /// <summary>Title, OPTIONS, intro, running HUD, pause, summary, shop: a language toggle only on the two cards.</summary>
        [UnityTest]
        public IEnumerator LanguageToggle_OnlyOnOptionsAndPause()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "title");
            ui.ClickTitleStart();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Intro, GameServices.Get<IDayQuery>().Phase);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "intro");
            ui.ClickSkipTutorial();
            yield return UITestDriver.Frames(2);
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "running");
            ui.ClickPauseButton();
            yield return null;
            Assert.IsTrue(ui.PauseVisible);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "paused (the pause card's own toggle is allowed)");
            yield return Day0Driver.Shot("ui_pause_card", true);
            ui.ClickResume();
            yield return null;
            var s = UITestDriver.Session();
            s.EndDayNow();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Summary, GameServices.Get<IDayQuery>().Phase);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "summary");
            ui.ClickFlowButton();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Shop, GameServices.Get<IDayQuery>().Phase);
            Assert.AreEqual("", ui.LanguageTogglesOutsideMenus(), "shop");
            yield return Day0Driver.Shot("ui_shop_no_language", true);
        }
    }
}
