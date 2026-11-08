using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// Pixel title logo + pause mini logo, Day 6 / Day 7 intros, star night (mystical intro card, HUD badge, rule line,
    /// explain card, day banner) and wish stars (click target follows the real star, freezes on pause, panel stays
    /// clickable, WishMade float-up; a fade shows nothing).
    /// </summary>
    public class UIStarNightTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        static IEnumerator Boot(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return null;
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

        static void AssertCardFits(UIRoot ui, string what)
        {
            var card = ui.FlowCardRect;
            var size = ui.CanvasSize;
            Assert.Greater(card.height, 100f, what + ": card shown");
            Assert.LessOrEqual(card.height, UIStyle.RefHeight - 16f, what + ": card " + card + " taller than a 720-high screen");
            Assert.IsTrue(card.xMin >= 0f && card.yMin >= 0f && card.xMax <= size.x && card.yMax <= size.y, what + ": " + card);
        }

        // ------------------------------------------------------------------ title logo

        [UnityTest]
        public IEnumerator Capture_TitleLogo() => TitleLogo(false);

        [UnityTest]
        public IEnumerator Capture_TitleLogo_Korean() => TitleLogo(true);

        static IEnumerator TitleLogo(bool ko)
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.Frames(3);
            var size = ui.CanvasSize;
            var logo = ui.TitlePartRect(MenuView.TitlePart.Logo);
            var tag = ui.TitlePartRect(MenuView.TitlePart.Tagline);
            Assert.AreEqual(MenuView.LogoPixel * ui.TitleLogo.W, logo.width, 0.5f, "whole logo pixels");
            Assert.Greater(ui.TitleLogo.FacePixelCount, 300, "chunky letters");
            Assert.Less(tag.yMax, logo.yMin, "tagline under the logo");
            Assert.AreEqual(size.x * 0.5f, tag.center.x, 1f, "tagline centred");
            // the logo block ends in the top ~22% of a 720-high screen (measured from the top)
            Assert.LessOrEqual(size.y - tag.yMin, UIStyle.RefHeight * 0.23f, "logo + tagline in the top 22%: " + tag);
            Assert.AreEqual(ko ? "행운의 숫자 호텔" : "A LUCKY NUMBER HOTEL", ui.TaglineText);
            Assert.IsTrue(ui.TaglineFits, "tagline on one line in its plate");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "title texts on the grid");

            // the LED flicker is two short dips now and then (photosensitivity: <= 3 flashes in any second)
            var l = ui.TitleLogo;
            l.RestartAnimation();
            var dips = new List<float>();
            bool was = true;
            for (float t = 0f; t < 40f; t += 0.01f)
            {
                l.Tick(t);
                if (was && !l.LedLit) dips.Add(t);
                was = l.LedLit;
            }
            Assert.Greater(dips.Count, 0, "flickers now and then");
            Assert.LessOrEqual(dips.Count, 2 * Mathf.CeilToInt(40f / PixelLogo.FlickerMin) + 2, "rarely");
            for (int i = 0; i + 3 < dips.Count; i++)
                Assert.Greater(dips[i + 3] - dips[i], 1f, "at most 3 flashes in any second");
            l.ForceLed(true);
            yield return Day0Driver.Shot("ui_title_logo" + (ko ? "_ko" : ""), true);
        }

        [UnityTest]
        public IEnumerator Capture_PauseLogo()
        {
            yield return Boot(false);
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            ui.ClickPauseButton();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.PauseVisible);
            var logo = ui.PauseLogoRect;
            var card = ui.PauseCardRect;
            Assert.Greater(logo.width, 150f, "mini logo shown");
            Assert.Greater(logo.yMin, card.yMax, "on top of the card");
            Assert.AreEqual(card.center.x, logo.center.x, 1f, "centred on it");
            Assert.LessOrEqual(logo.yMax, ui.CanvasSize.y - 4f, "on screen");
            ui.PauseLogo.ForceLed(true);
            yield return Day0Driver.Shot("ui_pause_logo", true);
            ui.ClickResume();
            yield return null;
        }

        // ------------------------------------------------------------------ Day 6

        [UnityTest]
        public IEnumerator Capture_Day6Intro() => Day6(false);

        [UnityTest]
        public IEnumerator Capture_Day6Intro_Korean() => Day6(true);

        static IEnumerator Day6(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            yield return ToIntro(d => d.DisplayDay == 6, "Day 6");
            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            Assert.IsFalse(ui.FlowStarLook || ui.FlowFridayLook, "an ordinary card");
            AssertCardFits(ui, "day 6");
            yield return Day0Driver.Shot("ui_day6_intro" + (ko ? "_ko" : ""), true);
            yield return UITestDriver.ClickStart();
            yield return null;
            Assert.IsTrue(ui.DayBannerVisible, "day banner slides in");
            StringAssert.Contains("6", ui.DayBannerText);
            Assert.AreEqual(ko ? "6/" + day.PlayableDayCount + "일차" : "DAY 6/" + day.PlayableDayCount, ui.HudDayText);
        }

        // ------------------------------------------------------------------ star night

        [UnityTest]
        public IEnumerator Capture_StarNight() => Star(false);

        [UnityTest]
        public IEnumerator Capture_StarNight_Korean() => Star(true);

        static IEnumerator Star(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            var day = GameServices.Get<IDayQuery>();
            var data = GameServices.Get<GameData>();
            yield return ToIntro(d => d.IsStarNight, "star night");
            Assert.AreEqual(day.PlayableDayCount, day.DisplayDay, "star night is the last day");
            Assert.IsTrue(data.TryGetDay(day.CurrentDay, out var def));

            // intro: mystical card
            Assert.IsTrue(ui.FlowStarLook, "deep blue / violet star card");
            StringAssert.Contains(ko && !string.IsNullOrEmpty(def.titleKo) ? def.titleKo : def.title, ui.FlowTitle);
            StringAssert.Contains("★", ui.FlowTitle, "between stars");
            StringAssert.Contains("+" + data.Rules.wishLuckBonus, Plain(ui.FlowExtra), "the wish bonus");
            AssertCardFits(ui, "star night intro");
            yield return Day0Driver.Shot("ui_starnight_intro" + (ko ? "_ko" : ""), true);

            // HUD
            yield return UITestDriver.ClickStart();
            yield return null;
            Assert.IsTrue(ui.DayBannerVisible);
            StringAssert.Contains(day.DisplayDay.ToString(), ui.DayBannerText);
            StringAssert.Contains(ko ? "마지막 날" : "FINAL DAY", ui.DayBannerText);
            var banner = ui.DayBannerRect;
            Assert.IsTrue(banner.xMin >= -UIStyle.RefWidth && banner.yMax <= ui.CanvasSize.y, "banner on the canvas");
            Assert.IsTrue(ui.StarBadgeVisible, "star night badge");
            Assert.IsFalse(ui.FridayBadgeVisible);
            Assert.AreEqual(ko ? "별이 쏟아지는 밤\n7 = 모두 행운" : "STAR NIGHT\n7 lucky for all", ui.StarBadgeText);
            Assert.IsTrue(ui.StarBadgeFits, "both badge lines fit: " + ui.StarBadgeMeasure);
            Assert.GreaterOrEqual(ui.StarBadgeFontSize, 24f, "badge readable (not shrunk)");
            StringAssert.Contains(ko ? "7 = 모두 행운" : "7 = lucky for ALL", ui.RulesText);
            Assert.IsNull(ui.RulesOverflow(), "rule lines fit");
            var rules = ui.RulesBoxRect;
            var badge = ui.StarBadgeRect;
            Assert.Less(badge.yMax, rules.yMin, "badge under the rule box");
            Assert.IsTrue(ui.WishCardVisible, "one-time explain card (hints on)");
            var card = ui.WishCardRect;
            Assert.Less(card.yMax, badge.yMin, "card under the badge");
            Assert.LessOrEqual(ui.CanvasSize.y - card.yMin, UIStyle.RefHeight - 60f, "card fits a 720-high screen: " + card);
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "texts on the grid");
            yield return UITestDriver.Frames(3);
            yield return Day0Driver.Shot("ui_starnight_hud" + (ko ? "_ko" : ""), true);

            // a real wish star
            var appeared = new List<WishStarAppeared>();
            var made = new List<WishMade>();
            using (GameServices.Events.Subscribe<WishStarAppeared>(e => appeared.Add(e)))
            using (GameServices.Events.Subscribe<WishMade>(e => made.Add(e)))
            {
                yield return UITestDriver.Until(() => appeared.Count > 0, 600);
                Assert.Greater(appeared.Count, 0, "a wish star crossed the sky");
                var star = appeared[0];
                yield return null;
                Assert.AreEqual(1, ui.WishTargetCount, "one click target");
                var t = ui.WishTargetRect(star.Id);
                var head = ui.ViewportToCanvas(star.FromX, star.FromY);
                Assert.GreaterOrEqual(t.width, WishView.TargetSize - 0.5f, "generous target");
                Assert.LessOrEqual(Vector2.Distance(t.center, head), 24f, "target on the star: " + t.center + " vs " + head);
                Assert.AreEqual("WishStar", ui.RaycastAt(t.center), "a click there hits the star");
                Assert.IsTrue(ui.WishHintVisible, "hint the first time");
                Assert.AreEqual(ko ? "소원을 빌어요! [E]" : "Make a wish! [E]", ui.WishHintText);
                var hint = ui.WishHintRect;
                var size = ui.CanvasSize;
                Assert.IsTrue(hint.xMin >= 0f && hint.xMax <= size.x && hint.yMin >= 0f && hint.yMax <= size.y, "hint on screen " + hint);
                // panel buttons stay clickable
                var fb = ui.FloorButtonRect(1);
                Assert.AreNotEqual("WishStar", ui.RaycastAt(fb.center), "floor button still on top");

                // frozen while paused
                ui.ClickPauseButton();
                yield return null;
                var before = ui.WishTargetRect(star.Id).center;
                yield return UITestDriver.Until(() => false, 8);
                Assert.AreEqual(before.x, ui.WishTargetRect(star.Id).center.x, 0.01f, "the star waits for you");
                ui.ClickResume();
                yield return null;
                yield return Day0Driver.Shot("ui_wish_star" + (ko ? "_ko" : ""), true);

                int luck = GameServices.Get<IScoreQuery>().Luck;
                ui.ClickWish(star.Id);
                Assert.AreEqual(1, made.Count, "the click made a wish");
                yield return null;
                Assert.AreEqual(0, ui.WishTargetCount, "target gone");
                Assert.GreaterOrEqual(GameServices.Get<IScoreQuery>().Luck, luck, "the wish adds Luck");
                Assert.AreEqual(1, ui.WishGrantCount, "float-up");
                Assert.AreEqual(ko ? "소원 성취! +" + made[0].LuckGain + " 운" : "Wish granted! +" + made[0].LuckGain + " Luck", ui.WishGrantText);
                Assert.IsFalse(ui.WishHintVisible, "hint gone after the first wish");
                Time.captureDeltaTime = 0.05f;
                yield return UITestDriver.Frames(4);
                Time.captureDeltaTime = 0f;
                var g = ui.WishGrantRect;
                Assert.IsTrue(g.xMin >= 0f && g.xMax <= size.x && g.yMax <= size.y, "float-up on screen " + g);
                yield return Day0Driver.Shot("ui_wish_granted" + (ko ? "_ko" : ""), true);
                yield return UITestDriver.Until(() => ui.WishGrantCount == 0, 40);
                Assert.AreEqual(0, ui.WishGrantCount, "fades away");

            }
        }

        [UnityTest]
        public IEnumerator WishTarget_SyntheticStar_FollowsAndFades()
        {
            yield return Boot(false);
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            var bus = GameServices.Events;
            bus.Publish(new WishStarAppeared(9001, 0.7f, 0.9f, 0.4f, 0.7f, 3f));
            yield return null;
            Assert.AreEqual(1, ui.WishTargetCount);
            var a = ui.WishTargetRect(9001).center;
            yield return UITestDriver.Until(() => false, 10); // 1 s of day clock
            var b = ui.WishTargetRect(9001).center;
            Assert.Less(b.x, a.x - 20f, "moves along its path");
            var mid = ui.ViewportToCanvas(Mathf.Lerp(0.7f, 0.4f, 1f / 3f), Mathf.Lerp(0.9f, 0.7f, 1f / 3f));
            Assert.LessOrEqual(Vector2.Distance(b, mid), 30f, "linear in the day clock: " + b + " vs " + mid);
            bus.Publish(new WishStarFaded(9001));
            yield return null;
            Assert.AreEqual(0, ui.WishTargetCount, "fade removes it");
            Assert.AreEqual(0, ui.WishGrantCount, "and shows nothing");
            Assert.IsFalse(ui.WishHintVisible);
            // a synthetic WishMade (no star on screen) still celebrates
            bus.Publish(new WishMade(9002, 4));
            yield return null;
            Assert.AreEqual(1, ui.WishGrantCount);
        }
    }
}
