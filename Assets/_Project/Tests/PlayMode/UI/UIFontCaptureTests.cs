using System.Collections;
using System.Collections.Generic;
using System.IO;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// Font choice "A" (Mulmaru body + Galmuri11 Bold headings + Galmuri11 fallback): the screens of the font comparison,
    /// scripted the same way every run (seed 7, same steps), saved as ui_font_A_&lt;screen&gt;.png.
    /// Run: tools/unity-batch.ps1 -Mode playmode -Filter UIFontCapture
    /// </summary>
    public class UIFontCaptureTests
    {
        const string Prefix = "ui_font_A_";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static IGameSettings Settings => GameServices.Get<IGameSettings>();

        /// <summary>Simulated time that barely moves: settles layout without letting animations drift between runs.</summary>
        static IEnumerator Settle(int frames)
        {
            Time.captureDeltaTime = 0.0001f;
            for (int i = 0; i < frames; i++) yield return null;
        }

        static IEnumerator Shot(string screen)
        {
            yield return Settle(2);
            var ui = UITestDriver.UI();
            string report = ui.FontReport();
            Debug.Log("[UIFontCapture] " + screen + ": " + report);
            StringAssert.StartsWith("missing: none", report, screen + ": every visible glyph in a UI font");
            Assert.IsTrue(File.Exists(FrameCapture.Capture(Prefix + screen)));
        }

        static void Boot()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            EndingCollection.StoreOverride = new MemoryEndingStore("1:2,3:1");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FontCapture_Title_Endings_Credits()
        {
            Boot();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return UITestDriver.Until(() => false, 40); // attract demo behind the title
            yield return Shot("title");
            Settings.UseKorean = true;
            yield return Shot("title_ko");

            ui.ClickEndings();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.EndingsVisible);
            yield return Shot("endings_collection_ko");
            ui.ClickEndingsBack();
            yield return null;

            Settings.UseKorean = false;
            ui.ClickCredits();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.CreditsVisible);
            yield return Shot("credits");
            Settings.UseKorean = true;
            yield return Shot("credits_ko");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FontCapture_Korean_Run() => PlayRun(true);

        [UnityTest, Timeout(300000)]
        public IEnumerator FontCapture_English_Run() => PlayRun(false);

        static IEnumerator PlayRun(bool ko)
        {
            string lang = ko ? "_ko" : "_en";
            Boot();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            Settings.UseKorean = ko;
            yield return Settle(10);
            Assert.AreEqual(ko, ui.Korean);
            yield return Shot("intro" + lang);

            // a running day: three riders aboard, a luck popup, a speech bubble with a hanja, the car heading up
            yield return UITestDriver.ClickStart();
            Time.captureDeltaTime = 0.1f;
            var guests = GameServices.Get<IGuestQuery>();
            var elevator = GameServices.Get<IElevatorQuery>();
            var waiting = new List<GuestId>();
            for (int i = 0; i < 100 && guests.GetWaiting(FloorIndex.Ground, waiting) < 3; i++) yield return null;
            Assert.GreaterOrEqual(waiting.Count, 3, "three guests in the lobby");
            var talker = waiting[0];
            ui.ClickOpen();
            for (int i = 0; i < 60 && elevator.Occupancy < 3; i++) yield return null;
            ui.ClickClose();
            for (int i = 0; i < 30 && ui.ActivePopupCount == 0; i++) yield return null;
            ui.ClickFloor(4);
            for (int i = 0; i < 3; i++) yield return null;
            GameServices.Events.Publish(new GuestSpoke(talker, GuestLineTriggers.Impatient,
                ko ? "아이고! 4(사)는 죽을 사(死)야!" : "Aigoo! Four - 'sa' sounds like death!")); // guests.json, with a hanja
            yield return Shot("day" + lang);

            // the summary card, then the shop: real offers, one focused for its details
            var s = UITestDriver.Session();
            s.Score.AddTips(160 - s.Score.Tips, earned: true);
            s.EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen);
            yield return Shot("summary" + lang);
            ui.ClickFlowButton(); // CONTINUE
            yield return Settle(2);
            Assert.AreEqual(DayPhase.Shop, GameServices.Get<IDayQuery>().Phase);
            int focus = 1;
            for (int i = 0; i < ui.ShopOfferCount; i++) if (ui.ShopOfferKind(i) == "CHARM") { focus = i; break; }
            ui.FocusShopOffer(Mathf.Min(focus, ui.ShopOfferCount - 1));
            yield return Shot("shop" + lang);
            if (!ko) yield break;

            // the 4th-floor ending card with its HIDDEN ENDING badge
            UITestDriver.RunToEnding(s);
            yield return UITestDriver.Frames(2);
            var day = new EndingOverrideDay(GameServices.Get<IDayQuery>(), EndingKind.FourthFloor);
            ui.OverrideServicesForTests(day: day);
            GameServices.Events.Publish(new EndingReached(EndingKind.FourthFloor, day.CurrentDay, 75, 120));
            yield return UITestDriver.Frames(2);
            yield return UITestDriver.Until(() => false, 3);
            Assert.IsTrue(ui.EndingHiddenBadgeVisible, "hidden badge");
            yield return Shot("ending" + lang);
        }
    }
}
