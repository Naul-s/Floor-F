using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Rule 3 (ride distance odometer on rider chips, distance popups / why card), the 666 curse, Friday the 13th
    /// (intro card, HUD badge, rule-box line), the rule box's third rule and every day's intro fitting the screen.
    /// </summary>
    public class UIRuleThreeTests
    {
        // data patched by a test (no Friday day / devout guest in the data yet): restored on tear-down
        static DayDef patchedDay;
        static GuestDef patchedGuest;
        static string[] patchedTraits;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (patchedDay != null) { patchedDay.friday13 = false; patchedDay = null; }
            if (patchedGuest != null) { patchedGuest.traits = patchedTraits; patchedGuest = null; patchedTraits = null; }
            GuestBubblesView.DistanceOverride.Clear();
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        static IEnumerator StartDay1(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
        }

        static IEnumerator WaitLobby(int n)
        {
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= n, 120);
            Assert.GreaterOrEqual(guests.GetWaiting(FloorIndex.Ground, waiting), n, n + " guests wait in the lobby");
            yield return UITestDriver.Frames(2);
        }

        static List<GuestId> Waiting(FloorIndex f)
        {
            var list = new List<GuestId>();
            GameServices.Get<IGuestQuery>().GetWaiting(f, list);
            return list;
        }

        // ------------------------------------------------------------------ rider odometer

        [UnityTest]
        public IEnumerator Capture_RiderOdometer() => Odometer(false);

        [UnityTest]
        public IEnumerator Capture_RiderOdometer_Korean() => Odometer(true);

        static IEnumerator Odometer(bool ko)
        {
            yield return StartDay1(ko);
            var ui = UITestDriver.UI();
            var e = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            var day = GameServices.Get<IDayQuery>();
            yield return WaitLobby(3);
            foreach (var g in Waiting(FloorIndex.Ground))
                Assert.AreEqual("-", ui.ChipOdometer(g), "waiting guests show no distance");

            ui.ClickOpen();
            yield return UITestDriver.Until(() => e.Occupancy >= 3 && !Day0Driver.AnyBoarding(), 120);
            Assert.GreaterOrEqual(e.Occupancy, 3, "three riders");
            yield return Day0Driver.Close(ui);
            yield return UITestDriver.Frames(2);
            foreach (var g in e.Riders)
            {
                Assert.IsTrue(guests.TryGet(g, out var info));
                Assert.AreEqual(UIFormat.Distance(info.FloorsTravelled), Plain(ui.ChipOdometer(g)).Split(':')[0], "odometer = GuestInfo.FloorsTravelled");
            }

            // live: one floor up, the odometer follows GuestInfo (rule 3 counts every floor moved with them inside)
            var second = new FloorIndex(1);
            yield return Day0Driver.GoTo(ui, second);
            foreach (var g in e.Riders)
            {
                if (!guests.TryGet(g, out var info) || info.State != GuestState.Riding) continue;
                string expected = UIFormat.Distance(info.FloorsTravelled);
                var v = UIFormat.DistanceVerdict(info.Luck, info.HideNumbers, info.FloorsTravelled, day.IsFriday13);
                if (v != LuckVerdict.Neutral) expected += ":" + v;
                Assert.AreEqual(expected, ui.ChipOdometer(g), "live odometer after a floor");
            }

            // wide numbers + colours: the first rider's lucky number, 13 and 17 (any of the riders)
            var riders = new List<GuestId>(e.Riders);
            Assert.IsTrue(guests.TryGet(riders[0], out var first));
            int lucky = first.Luck.Lucky.Count > 0 ? first.Luck.Lucky[0] : 7;
            int unlucky = first.Luck.Unlucky.Count > 0 ? first.Luck.Unlucky[0] : 13;
            GuestBubblesView.DistanceOverride[riders[0].Value] = lucky;
            if (riders.Count > 1) GuestBubblesView.DistanceOverride[riders[1].Value] = 13;
            if (riders.Count > 2) GuestBubblesView.DistanceOverride[riders[2].Value] = 17;
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(UIFormat.Distance(lucky) + ":Lucky", ui.ChipOdometer(riders[0]), "gold when stepping out now would be lucky");
            Assert.AreEqual(UIStyle.Lucky, ui.ChipOdometerColor(riders[0]));
            Assert.IsNull(ui.ChipOdometerOverflow(), "two-digit distances fit the plate");
            var size = ui.CanvasSize;
            var chip = ui.ChipRect(riders[0]);
            Assert.AreEqual(GuestBubblesView.RiderChipH, chip.height, 0.5f, "rider chips carry the odometer row");
            foreach (var g in riders)
            {
                var r = ui.ChipRect(g);
                Assert.IsTrue(r.xMin >= 0f && r.yMin >= 0f && r.xMax <= size.x && r.yMax <= size.y, "chip on screen: " + r);
            }
            yield return Day0Driver.Shot("ui_chips_distance" + (ko ? "_ko" : ""), true);

            GuestBubblesView.DistanceOverride[riders[0].Value] = unlucky;
            yield return UITestDriver.Frames(2);
            if (first.Luck.Unlucky.Count > 0)
            {
                Assert.AreEqual(UIFormat.Distance(unlucky) + ":Unlucky", ui.ChipOdometer(riders[0]), "red when it would be unlucky");
                Assert.AreEqual(UIStyle.Unlucky, ui.ChipOdometerColor(riders[0]));
            }
        }

        // ------------------------------------------------------------------ distance popup + why card

        [UnityTest]
        public IEnumerator Capture_DistancePopup() => DistancePopup(false);

        [UnityTest]
        public IEnumerator Capture_DistancePopup_Korean() => DistancePopup(true);

        static IEnumerator DistancePopup(bool ko)
        {
            yield return StartDay1(ko);
            var ui = UITestDriver.UI();
            yield return WaitLobby(1);
            var g = Waiting(FloorIndex.Ground)[0];
            GameServices.Events.Publish(new LuckJudged(g, LuckSource.RideDistance, 13, LuckVerdict.Unlucky, FloorIndex.Ground));
            yield return UITestDriver.Frames(2);
            Assert.Greater(ui.ActivePopupCount, 0, "RideDistance -> popup");
            string text = ui.LastPopupText;
            StringAssert.Contains("↕13", text);
            StringAssert.Contains(ko ? "불길!" : "UNLUCKY!", text);
            StringAssert.Contains(ko ? "13층 이동" : "travelled 13 floors", text);
            Time.captureDeltaTime = 0.05f;
            yield return UITestDriver.Frames(4);
            Time.captureDeltaTime = 0f;
            yield return Day0Driver.Shot("ui_distance_popup" + (ko ? "_ko" : ""), true);
        }

        [UnityTest]
        public IEnumerator Capture_Day0WhyDistance() => WhyDistance(false);

        [UnityTest]
        public IEnumerator Capture_Day0WhyDistance_Korean() => WhyDistance(true);

        static IEnumerator WhyDistance(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(ui, 1);
            var g = Waiting(FloorIndex.Ground)[0];
            GameServices.Events.Publish(new LuckJudged(g, LuckSource.RideDistance, 4, LuckVerdict.Unlucky, FloorIndex.Ground));
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(TutorialView.Step.Why, ui.TutorialStep, "rule 3 verdict explained");
            string why = Plain(ui.TutorialWhy);
            StringAssert.Contains(ko ? "4층을 움직였어요" : "travelled 4 floors", why);
            StringAssert.Contains(UIFormat.DistanceGlyph, why, "points at the chip odometer");
            Day0Driver.AssertExplainCoach(ui, "why distance");
            Day0Driver.AssertCardsClear(ui, "why distance");
            yield return Day0Driver.Shot("ui_day0_why_distance" + (ko ? "_ko" : ""), true);
        }

        // ------------------------------------------------------------------ 666

        [UnityTest]
        public IEnumerator Capture_Curse666() => Curse(false);

        [UnityTest]
        public IEnumerator Capture_Curse666_Korean() => Curse(true);

        static IEnumerator Curse(bool ko)
        {
            yield return StartDay1(ko);
            var ui = UITestDriver.UI();
            var data = GameServices.Get<GameData>();
            var guests = GameServices.Get<IGuestQuery>();
            yield return WaitLobby(1);
            var g = Waiting(FloorIndex.Ground)[0];
            Assert.IsTrue(guests.TryGet(g, out var info));
            Assert.IsTrue(data.TryGetGuest(info.Archetype, out var def));
            var bus = GameServices.Events;
            if (!def.HasTrait(GuestTraits.Devout))
            {
                bus.Publish(new LuckJudged(g, LuckSource.FloorLabel, 6, LuckVerdict.Unlucky, FloorIndex.Ground));
                yield return null;
                Assert.IsFalse(ui.CurseVisible, "an unlucky 6 from an ordinary guest is no curse");
                // the devout guest is not in the data yet / not spawned here: lend the trait to this one
                patchedGuest = def;
                patchedTraits = def.traits;
                var traits = new List<string>(def.traits ?? new string[0]) { GuestTraits.Devout };
                def.traits = traits.ToArray();
            }
            bus.Publish(new LuckJudged(g, LuckSource.Occupancy, 6, LuckVerdict.Lucky, FloorIndex.Ground));
            yield return null;
            Assert.IsFalse(ui.CurseVisible, "a lucky 6 is no curse");

            bus.Publish(new LuckJudged(g, LuckSource.FloorLabel, 6, LuckVerdict.Unlucky, FloorIndex.Ground));
            yield return null;
            Assert.IsTrue(ui.CurseVisible, "unlucky 6 from a devout guest -> 666");
            StringAssert.Contains(UIStrings.CurseNumber, ui.CurseText);
            StringAssert.Contains(ko ? "두 배" : "DOUBLE", ui.CurseText);
            Assert.AreEqual(DayPhase.Running, GameServices.Get<IDayQuery>().Phase);
            Time.captureDeltaTime = 0.05f;
            yield return UITestDriver.Frames(6); // 0.3 s: punched in, mid-pulse
            Time.captureDeltaTime = 0f;
            Assert.Greater(ui.CurseVignetteAlpha, 0.25f, "red vignette pulse");
            Assert.GreaterOrEqual(ui.CurseCaptionFontSize, 24f, "caption readable (not shrunk)");
            var r = ui.CurseTextRect;
            var size = ui.CanvasSize;
            Assert.IsTrue(r.xMin >= 0f && r.xMax <= size.x && r.yMin >= 0f && r.yMax <= size.y, "666 on screen: " + r);
            Assert.AreNotEqual("Number", ui.RaycastAt(r.center), "never blocks clicks");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "curse texts on Galmuri's grid");
            yield return Day0Driver.Shot("ui_curse_666" + (ko ? "_ko" : ""), true);
            yield return UITestDriver.Until(() => !ui.CurseVisible, 20);
            Assert.IsFalse(ui.CurseVisible, "gone after ~1.5 s");
        }

        // ------------------------------------------------------------------ Friday the 13th + rule box

        /// <summary>From the current Intro, plays days through the session until the Friday the 13th Intro.</summary>
        static IEnumerator ToFridayIntro()
        {
            var data = GameServices.Get<GameData>();
            var day = GameServices.Get<IDayQuery>();
            DayDef friday = null;
            foreach (var d in data.Days) if (d.friday13) friday = d;
            if (friday == null)
            {
                // no Friday day in the data yet: the last real day is it (contract: DayDef.friday13)
                friday = data.Days[data.Days.Count - 1];
                friday.friday13 = true;
                patchedDay = friday;
            }
            var s = UITestDriver.Session();
            for (int guard = 0; guard < 40 && !(day.Phase == DayPhase.Intro && day.IsFriday13); guard++)
            {
                switch (day.Phase)
                {
                    case DayPhase.Intro: s.BeginDay(); break;
                    case DayPhase.Running: s.EndDayNow(); break;
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop: s.FinishShopping(); break;
                    default: Assert.Fail("unexpected phase " + day.Phase); break;
                }
                yield return null;
            }
            Assert.IsTrue(day.Phase == DayPhase.Intro && day.IsFriday13, "reached the Friday the 13th intro");
            yield return UITestDriver.Frames(2);
        }

        [UnityTest]
        public IEnumerator Capture_Friday13() => Friday(false);

        [UnityTest]
        public IEnumerator Capture_Friday13_Korean() => Friday(true);

        static IEnumerator Friday(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return ToFridayIntro();
            Assert.IsTrue(GameServices.Get<GameData>().TryGetDay(GameServices.Get<IDayQuery>().CurrentDay, out var def));

            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            Assert.IsTrue(ui.FlowFridayLook, "red/black Friday card");
            Assert.AreEqual(ko && !string.IsNullOrEmpty(def.titleKo) ? def.titleKo : def.title, ui.FlowTitle, "headline from the data title");
            StringAssert.Contains(ko ? "1.5배" : "1.5x", Plain(ui.FlowExtra), "the penalty multiplier");
            StringAssert.Contains("13", Plain(ui.FlowExtra));
            AssertCardFits(ui, "friday intro");
            yield return Day0Driver.Shot("ui_friday13_intro" + (ko ? "_ko" : ""), true);

            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.FridayBadgeVisible, "HUD badge on Friday the 13th");
            Assert.AreEqual(ko ? "13일의 금요일 · 불운 x1.5" : "FRIDAY 13TH · bad luck x1.5", ui.FridayBadgeText);
            Assert.GreaterOrEqual(ui.FridayBadgeFontSize, 24f, "badge readable (not shrunk)");
            StringAssert.Contains(ko ? "이동 13 = 모두 불길" : "travelled 13 = unlucky for ALL", ui.RulesText);
            StringAssert.Contains(ko ? "움직인 층수" : "FLOORS TRAVELLED", ui.RulesText);
            Assert.IsNull(ui.RulesOverflow(), "rule lines fit the box");
            var rules = ui.RulesBoxRect;
            var badge = ui.FridayBadgeRect;
            Assert.Less(badge.yMax, rules.yMin, "badge under the rule box");
            Assert.AreEqual(rules.xMax, badge.xMax, 1f, "same right column");
            Assert.GreaterOrEqual(badge.yMin, ui.CanvasSize.y - UIStyle.RefHeight + 60f, "on a 720-high screen");
            yield return WaitLobby(1);
            yield return Day0Driver.Shot("ui_friday13_hud" + (ko ? "_ko" : ""), true);
        }

        [UnityTest]
        public IEnumerator Capture_RuleBox() => RuleBox(false);

        [UnityTest]
        public IEnumerator Capture_RuleBox_Korean() => RuleBox(true);

        static IEnumerator RuleBox(bool ko)
        {
            yield return StartDay1(ko);
            var ui = UITestDriver.UI();
            string rules = ui.RulesText;
            Assert.IsNotNull(rules, "rule box while running");
            StringAssert.Contains(ko ? "문이 열리면" : "Doors OPEN", rules);
            StringAssert.Contains(ko ? "문이 닫히면" : "Doors CLOSE", rules);
            StringAssert.Contains(ko ? "내릴 때" : "Step OUT", rules, "rule 3");
            StringAssert.DoesNotContain(ko ? "13일의 금요일" : "Friday 13th", rules, "no Friday line on an ordinary day");
            Assert.IsFalse(ui.FridayBadgeVisible);
            Assert.IsNull(ui.RulesOverflow(), "rule lines fit the box");
            var r = ui.RulesBoxRect;
            Assert.AreEqual(HudView.RulesH, r.height, 0.5f);
            Assert.Less(ui.CanvasSize.y - r.yMin, TutorialView.ColumnTop, "tutorial column starts under it");
            yield return WaitLobby(1);
            yield return Day0Driver.Shot("ui_rulebox" + (ko ? "_ko" : ""), true);
        }

        // ------------------------------------------------------------------ every intro fits

        static void AssertCardFits(UIRoot ui, string what)
        {
            var card = ui.FlowCardRect;
            var size = ui.CanvasSize;
            Assert.Greater(card.height, 100f, what + ": card shown");
            Assert.LessOrEqual(card.height, UIStyle.RefHeight - 16f, what + ": card " + card + " taller than a 720-high screen");
            Assert.GreaterOrEqual(card.yMin, 0f, what + ": " + card);
            Assert.LessOrEqual(card.yMax, size.y, what + ": " + card);
            Assert.GreaterOrEqual(card.xMin, 0f, what + ": " + card);
            Assert.LessOrEqual(card.xMax, size.x, what + ": " + card);
        }

        [UnityTest]
        public IEnumerator DayIntros_FitTheScreen_EnglishAndKorean()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial: false);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var settings = GameServices.Get<IGameSettings>();
            var s = UITestDriver.Session();
            int intros = 0;
            for (int guard = 0; guard < 60 && day.Phase != DayPhase.Ending; guard++)
            {
                if (day.Phase == DayPhase.Intro)
                {
                    foreach (bool ko in new[] { false, true })
                    {
                        settings.UseKorean = ko;
                        yield return UITestDriver.Frames(2);
                        AssertCardFits(ui, (ko ? "ko" : "en") + " day " + day.DisplayDay + " intro");
                    }
                    settings.UseKorean = false;
                    intros++;
                    if (day.IsTutorial) { s.SkipTutorial(); yield return null; continue; }
                    s.BeginDay();
                }
                else if (day.Phase == DayPhase.Running) s.EndDayNow();
                else if (day.Phase == DayPhase.Summary) s.ContinueFromSummary();
                else if (day.Phase == DayPhase.Shop) s.FinishShopping();
                yield return null;
            }
            Assert.AreEqual(day.DayCount, intros, "every day's intro checked");
        }
    }
}
