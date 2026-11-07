using System.Collections;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Phase 3b: destination icons in the panel / chips, title backdrop, credits screen.</summary>
    public class UIIconCreditsTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UIIcons.ResetStatics();
            UITestDriver.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator UiButtons_PublishUiSound()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var heard = new System.Collections.Generic.List<UiSoundKind>();
            using (GameServices.Events.Subscribe<UiSound>(e => heard.Add(e.Kind)))
            {
                void Expect(System.Action click, UiSoundKind kind, string what)
                {
                    int before = heard.Count;
                    click();
                    Assert.AreEqual(before + 1, heard.Count, what + ": exactly one UiSound per click");
                    Assert.AreEqual(kind, heard[heard.Count - 1], what);
                }

                Expect(ui.ClickFlowButton, UiSoundKind.Confirm, "START");
                Assert.AreEqual(DayPhase.Running, day.Phase);
                Expect(() => ui.ClickFloor(5), UiSoundKind.Click, "floor button (accepted)");
                yield return UITestDriver.Frames(2);
                Expect(ui.ClickLanguage, UiSoundKind.Click, "language toggle");
                Expect(ui.ClickLanguage, UiSoundKind.Click, "language toggle back");
                Expect(ui.ClickPauseButton, UiSoundKind.Click, "pause");
                yield return null;
                Expect(ui.ClickHintsToggle, UiSoundKind.Click, "hints toggle");
                Expect(ui.ClickHintsToggle, UiSoundKind.Click, "hints toggle back");
                Expect(ui.ClickResume, UiSoundKind.Click, "resume");
                yield return null;

                var s = UITestDriver.Session();
                s.Score.AddTips(100, earned: true);
                s.EndDayNow();
                yield return null;
                Expect(ui.ClickFlowButton, UiSoundKind.Confirm, "CONTINUE");
                Assert.AreEqual(DayPhase.Shop, day.Phase);
                yield return null;
                Expect(() => ui.ClickFloor(0), UiSoundKind.Error, "shop floor without a sticker");
                Expect(() => ui.ClickShopOffer(0), UiSoundKind.Confirm, "sticker picked");
                Expect(() => ui.ClickFloor(0), UiSoundKind.Error, "sticker refused on floor 1 (not Applied)");
                Expect(() => ui.ClickFloor(FloorIndex.FromPhysicalNumber(4).Value), UiSoundKind.Confirm, "sticker applied");
                yield return null;
                Expect(ui.ClickShopDone, UiSoundKind.Confirm, "NEXT DAY");
                Assert.AreEqual(DayPhase.Intro, day.Phase);
            }
        }

        [UnityTest]
        public IEnumerator PanelBadges_ShowIcons()
        {
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var building = GameServices.Get<IBuildingQuery>();
            var data = GameServices.Get<GameData>();
            for (int f = 0; f < ui.FloorButtonCount; f++)
            {
                Assert.IsTrue(data.TryGetDestination(building.GetDestination(new FloorIndex(f)), out var d), "floor " + f + " destination");
                // a curated name too wide beside the icon ("Karaoke") takes the whole badge instead of being cut
                bool wideName = ui.BadgeText(f) == UIFormat.DestinationShortName(d.Id.Value, false);
                Assert.IsTrue(ui.BadgeShowsIcon(f) || wideName, "floor " + f + " badge shows an icon (or a whole curated name): " + ui.BadgeText(f));
                if (ui.BadgeShowsIcon(f))
                {
                    Assert.IsNotNull(ui.BadgeSprite(f));
                    StringAssert.StartsWith(d.IconKey, ui.BadgeSprite(f).name, "floor " + f + " sprite = Icons/" + d.IconKey);
                    Assert.AreEqual(ui.DestinationColor(f), ui.BadgeIconColor(f), "icon tinted with the destination colour");
                }
                Assert.GreaterOrEqual(ui.BadgeText(f).Length, 1, "short name next to the icon: " + ui.BadgeText(f));
                Assert.GreaterOrEqual(ui.BadgeFontSize(f), 24f);
            }
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "badge names still Galmuri on the 12px grid");
        }

        static IEnumerator WaitForLobbyChips(UIRoot ui)
        {
            yield return UITestDriver.ClickStart();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new System.Collections.Generic.List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= 3);
            yield return UITestDriver.Frames(2);
        }

        [UnityTest]
        public IEnumerator Chips_ShowIcons_FallbackToText()
        {
            GameplayRoot.SeedOverride = 7;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            yield return WaitForLobbyChips(ui);
            ui.ChipBadgeStats(out int count, out int icons, out int names, out int textOnly);
            Assert.GreaterOrEqual(count, 3, "chips above the lobby guests");
            Assert.AreEqual(count, icons, "every chip shows its destination icon");
            Assert.AreEqual(0, textOnly, "no text-only badges while icons exist");

            // no sprites at all -> the Phase-2 text badge (destination colour + name), one warning per key
            var data = GameServices.Get<GameData>();
            foreach (var d in data.Destinations) UIIcons.ForceMissing.Add(d.IconKey);
            yield return UITestDriver.LoadMain();
            ui = UITestDriver.UI();
            for (int f = 0; f < ui.FloorButtonCount; f++)
            {
                Assert.IsFalse(ui.BadgeShowsIcon(f), "panel " + f + ": no icon");
                Assert.GreaterOrEqual(ui.BadgeText(f).Length, 3, "panel " + f + ": text badge " + ui.BadgeText(f));
            }
            yield return WaitForLobbyChips(ui);
            ui.ChipBadgeStats(out count, out icons, out names, out textOnly);
            Assert.GreaterOrEqual(count, 3);
            Assert.AreEqual(0, icons, "fallback: no icons");
            Assert.AreEqual(count, textOnly, "fallback: every chip names its destination in text");
            GameplayRoot.SeedOverride = null;
        }

        // TutorialFloorPrompt_ShowsDestinationIcon -> UIDay0Tests.Day0_FloorPrompt_ShowsDestinationIcon_Korean

        static float MeanLuminance(string png)
        {
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(System.IO.File.ReadAllBytes(png)), "read " + png);
            var px = tex.GetPixels32();
            double sum = 0;
            for (int i = 0; i < px.Length; i++) sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
            Object.Destroy(tex);
            return (float)(sum / px.Length / 255.0);
        }

        [UnityTest]
        public IEnumerator TitleBackdrop_DimsWorld()
        {
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Until(() => false, 20); // demo running behind
            Assert.IsTrue(ui.TitleVisible);
            Assert.IsTrue(ui.TitleBackdropCovers(out float alpha), "full-screen backdrop behind the title panel");
            Assert.GreaterOrEqual(alpha, 0.5f, "dark enough to push the lit floor signs back");

            float on = MeanLuminance(FrameCapture.Capture("test_title_backdrop_on"));
            ui.SetTitleBackdrop(false);
            yield return null;
            float off = MeanLuminance(FrameCapture.Capture("test_title_backdrop_off"));
            ui.SetTitleBackdrop(true);
            Assert.Greater(off, 0.01f, "the world renders behind the title");
            Assert.Less(on, off * 0.85f, "backdrop darkens the frame (on " + on + " vs off " + off + ")");

            ui.ClickCredits();
            yield return null;
            Assert.IsTrue(ui.TitleBackdropCovers(out _), "credits keep the backdrop");
        }

        [UnityTest]
        public IEnumerator Credits_ListsAllRequiredAttributions()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.CreditsVisible);
            ui.ClickCredits();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.CreditsVisible, "CREDITS -> credits card");
            Assert.IsFalse(ui.TitleMenuVisible, "logo + title buttons step aside");
            Assert.IsTrue(ui.CreditsFitOnScreen, "credits card fits the 1280x720 canvas");

            foreach (bool korean in new[] { false, true })
            {
                GameServices.Get<IGameSettings>().UseKorean = korean;
                yield return UITestDriver.Frames(2);
                Assert.IsTrue(ui.CreditsFitOnScreen, "fits (" + (korean ? "ko" : "en") + ")");
                string text = ui.CreditsText;
                StringAssert.Contains("\"Local Forecast - Elevator\" Kevin MacLeod (incompetech.com)\n" +
                                      "Licensed under Creative Commons: By Attribution 4.0\n" +
                                      "https://creativecommons.org/licenses/by/4.0/", text, "CC BY 4.0 wording, exact");
                foreach (var who in new[] { "Kenney", "CC0", "Galmuri11", "SIL Open Font License 1.1", "Material Symbols", "Apache License 2.0",
                             "Freesound", "LilMati", "PagDev", "LaurenPonder", "egomassive", "danlucaz", "BlondPanda", "Claude", "Anthropic" })
                    StringAssert.Contains(who, text, (korean ? "ko" : "en") + " credits name " + who);
            }
            GameServices.Get<IGameSettings>().UseKorean = false;
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "credits texts on Galmuri's grid");

            ui.ClickCreditsBack();
            yield return null;
            Assert.IsFalse(ui.CreditsVisible, "BACK closes the credits");
            Assert.IsTrue(ui.TitleMenuVisible);

            ui.ClickCredits();
            ui.ClickTitleStart(); // leaving the title closes the credits
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.CreditsVisible);
        }
    }
}
