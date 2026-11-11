using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Settable ISeasonQuery (Gameplay's real one is built in parallel; the UI only reads it).</summary>
    sealed class FakeSeason : ISeasonQuery
    {
        public bool IsSeasonExtended { get; set; }
        public bool IsExtendedDay { get; set; }
        public bool MysteryTonight { get; set; }
        public bool IsLotteryNight { get; set; }
        public int LotteryNumber { get; set; }
        public bool IsChristmas { get; set; }
        public int LuckyForAll { get; set; }
        public float SnowAmount { get; set; }
    }

    sealed class FakeSeasonFlow : ISeasonFlow
    {
        public bool CanExtendSeason { get; set; }
        public int Extends;
        public bool ExtendSeason() { if (!CanExtendSeason) return false; Extends++; return true; }
    }

    /// <summary>The real IDayQuery shown on another (winter) day: data day, display number, "/10", optionally phase / ending.</summary>
    sealed class WinterDay : IDayQuery
    {
        readonly IDayQuery inner;
        public DayId Day;
        public int Display, Playable = 10;
        public DayPhase? ForcedPhase;
        public EndingKind? ForcedEnding;
        public WinterDay(IDayQuery inner, int dataDay, int displayDay) { this.inner = inner; Day = new DayId(dataDay); Display = displayDay; }
        public DayId CurrentDay => Day;
        public int DayCount => inner.DayCount;
        public int DisplayDay => Display;
        public int PlayableDayCount => Playable;
        public DayPhase Phase => ForcedPhase ?? inner.Phase;
        public bool IsTutorial => false;
        public bool IsFriday13 => false;
        public bool IsStarNight => false;
        public bool IsRainy => false;
        public float DurationSeconds => inner.DurationSeconds;
        public float ElapsedSeconds => inner.ElapsedSeconds;
        public float RemainingSeconds => inner.RemainingSeconds;
        public EndingKind Ending => ForcedEnding ?? inner.Ending;
        public DaySummary LastSummary => inner.LastSummary;
    }

    /// <summary>
    /// Winter season UI: the ending card's "keep the hotel open" offer (Enter owns it), the winter / Christmas cards,
    /// lottery night (draw reveal, gold jackpot button, HUD badge, JACKPOT popup), Christmas Eve's rule line and badge,
    /// the winter endings with a 10-day journey, the rare mystery intro line. Services are fakes on the contract.
    /// </summary>
    public class UIWinterTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            var ui = UITestDriver.UI();
            if (ui != null) ui.OverrideSeasonForTests(null);
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        static IEnumerator Boot(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            EndingCollection.StoreOverride = new MemoryEndingStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return null;
        }

        static GameData Data => GameServices.Get<GameData>();

        static DayDef Day(System.Func<DayDef, bool> pick)
        {
            foreach (var d in Data.Days) if (d != null && pick(d)) return d;
            Assert.Fail("no such day in the data");
            return null;
        }

        static void AssertCardFits(UIRoot ui, string what)
        {
            var card = ui.FlowCardRect;
            var size = ui.CanvasSize;
            Assert.Greater(card.height, 100f, what + ": card shown");
            Assert.LessOrEqual(card.height, UIStyle.RefHeight - 16f, what + ": card " + card + " taller than a 720-high screen");
            Assert.IsTrue(card.xMin >= 0f && card.yMin >= 0f && card.xMax <= size.x && card.yMax <= size.y, what + ": " + card);
            Assert.IsNull(ui.FlowTextOverflow(), what);
            Assert.IsNull(ui.FirstOffGridOrForeignText(), what);
        }

        // ------------------------------------------------------------------ ending card: keep the hotel open

        [UnityTest]
        public IEnumerator Capture_EndingKeepOpen() => KeepOpen(false);

        [UnityTest]
        public IEnumerator Capture_EndingKeepOpen_Korean() => KeepOpen(true);

        static IEnumerator KeepOpen(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            var real = GameServices.Get<IDayQuery>().Ending;
            var flowFake = new FakeSeasonFlow { CanExtendSeason = true };
            ui.OverrideSeasonForTests(new FakeSeason(), flowFake);
            yield return UITestDriver.Frames(2);

            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            Assert.AreEqual(ko ? "계속 영업하기  [Enter]" : "KEEP THE HOTEL OPEN  [Enter]", ui.FlowActionText, "the main button");
            Assert.IsTrue(ui.PlayAgainVisible, "PLAY AGAIN stays");
            Assert.AreEqual(ko ? "다시 하기" : "PLAY AGAIN", ui.PlayAgainText, "no [Enter] on it: Enter keeps the hotel open");
            Assert.IsTrue(ui.EndingTitleButtonVisible, "TITLE stays");
            int extra = Data.ExtendedDayCount;
            string offer = Plain(ui.SeasonOfferText);
            StringAssert.Contains(ko ? "겨울 시즌: " + extra + "일 더" : "Winter season: " + extra + " more nights", offer);
            StringAssert.Contains(ko ? "솔로의 날, 복권 추첨, 그리고 크리스마스 이브" : "Singles' Day, a lottery, and Christmas Eve", offer);
            Assert.IsTrue(ui.FlowSnowfall, "snow drifts down the card");
            Assert.IsTrue(ui.FlowCornerFlakes, "snowflakes on the corners");
            Assert.IsTrue(ui.FlowOwnsEnter);
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Gameplay's Enter (PLAY AGAIN) is held back");
            AssertCardFits(ui, (ko ? "ko" : "en") + " offer");
            Assert.IsNull(UIFonts.MissingGlyph(Plain(ui.SeasonOfferText) + ui.FlowActionText + ui.PlayAgainText));
            yield return Day0Driver.Shot("ui_winter_keep_open" + (ko ? "_ko" : ""), true);

            // Enter = keep the hotel open (once) - only after the offer has been on screen a moment
            yield return new UnityEngine.WaitForSecondsRealtime(UIRoot.OfferEnterDelay + 0.1f);
            ui.InjectPress(enter: true);
            yield return null;
            yield return null;
            Assert.AreEqual(1, flowFake.Extends, "Enter -> ISeasonFlow.ExtendSeason");
            Assert.AreEqual(DayPhase.Ending, GameServices.Get<IDayQuery>().Phase, "Gameplay did not restart on the same Enter");

            // no offer: the card is the plain ending again
            flowFake.CanExtendSeason = false;
            yield return null;
            ui.OverrideSeasonForTests(new FakeSeason(), flowFake);
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.PlayAgainVisible);
            StringAssert.Contains(ko ? "다시 하기" : "PLAY AGAIN", ui.FlowActionText);
            Assert.IsNull(ui.SeasonOfferText);
            Assert.IsFalse(ui.FlowSnowfall);
            Assert.AreEqual(true, ui.FlowEnterEnabled, "Enter belongs to Gameplay again");

            // PLAY AGAIN button restarts
            flowFake.CanExtendSeason = true;
            ui.OverrideSeasonForTests(new FakeSeason(), flowFake);
            yield return UITestDriver.Frames(2);
            ui.ClickPlayAgain();
            yield return UITestDriver.Frames(2);
            Assert.AreNotEqual(DayPhase.Ending, GameServices.Get<IDayQuery>().Phase, "PLAY AGAIN -> a new run (" + real + ")");
        }

        // ------------------------------------------------------------------ ending: scene first, card later

        /// <summary>Steps <paramref name="seconds"/> of 0.1 s frames (UIClock follows captureDeltaTime).</summary>
        static IEnumerator Step(float seconds)
        {
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < Mathf.RoundToInt(seconds / 0.1f); i++) yield return null;
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator Capture_EndingBeat_SceneFirst_ThenCard_KeySkips()
        {
            yield return Boot(false);
            UIFeatures.EndingBeat = true;
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            UITestDriver.RunToEnding(UITestDriver.Session());
            // the run ends as a Christmas miracle (Rendering flies Santa's sleigh past the moon)
            var xmas = Day(x => x.christmas);
            var day = new WinterDay(GameServices.Get<IDayQuery>(), xmas.day, Data.DisplayDayNumber(new DayId(xmas.day))) { ForcedEnding = EndingKind.WinterMiracle };
            var flowFake = new FakeSeasonFlow { CanExtendSeason = true }; // a pressed key must not take the main button
            ui.OverrideServicesForTests(day: day);
            ui.OverrideSeasonForTests(new FakeSeason { IsSeasonExtended = true, IsExtendedDay = true, IsChristmas = true, LuckyForAll = 12, SnowAmount = 1f }, flowFake);
            GameServices.Events.Publish(new EndingReached(EndingKind.WinterMiracle, day.CurrentDay, 92, 300));
            yield return null;

            Assert.IsTrue(ui.EndingBeatActive, "the ending scene plays alone first");
            Assert.IsFalse(ui.FlowCardUp);
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Gameplay's Enter waits too");
            yield return Step(0.5f);
            Assert.IsTrue(ui.EndingBeatActive, "t = 0.5 s: still the scene");
            Assert.IsFalse(ui.FlowCardUp, "t = 0.5 s: no card");
            Assert.Less(ui.HudAlpha, 0.05f, "no HUD over the scene");
            yield return Step(3.0f); // the sleigh's first flight (1.5 s in, 7 s long) is crossing the moon now
            Assert.IsTrue(ui.EndingBeatActive, "the miracle waits " + FlowScreensView.MiracleBeat + " s");
            yield return Day0Driver.Shot("ui_ending_beat_miracle_sleigh", true);
            yield return Step(FlowScreensView.MiracleBeat - 3.5f - 0.3f);
            Assert.IsTrue(ui.EndingBeatActive, "just before the end of the beat");
            yield return Step(0.3f + FlowScreensView.CardFadeIn + 0.2f);
            Assert.IsFalse(ui.EndingBeatActive);
            Assert.IsTrue(ui.FlowCardUp, "after the beat the card is up");
            StringAssert.Contains("A CHRISTMAS MIRACLE", ui.FlowTitle);
            Assert.AreEqual(0, flowFake.Extends);

            // the next ending: a key during the beat only brings the card (no button, no restart)
            day.ForcedPhase = DayPhase.Summary;
            yield return null;
            day.ForcedPhase = null;
            day.ForcedEnding = EndingKind.SilentNight;
            yield return null;
            Assert.IsTrue(ui.EndingBeatActive, "a new ending: a new beat");
            Assert.AreEqual(FlowScreensView.EndingBeat, FlowScreensView.BeatFor(EndingKind.SilentNight));
            yield return Step(0.3f);
            ui.InjectPress(enter: true);
            yield return null;
            Assert.IsFalse(ui.EndingBeatActive, "the key skipped the beat");
            Assert.AreEqual(0, flowFake.Extends, "...and did not keep the hotel open");
            Assert.AreEqual(DayPhase.Ending, GameServices.Get<IDayQuery>().Phase, "...nor restart");
            ui.InjectPress(pointer: true, canvasPoint: ui.CanvasSize * 0.5f);
            yield return Step(FlowScreensView.CardFadeIn + 0.2f);
            Assert.IsTrue(ui.FlowCardUp);
            Assert.AreEqual(0, flowFake.Extends, "a click during the fade-in hits no button either");
            Assert.IsTrue(ui.FlowOwnsEnter, "now the offer takes Enter (after its arm delay)");
        }

        // ------------------------------------------------------------------ winter / Christmas intro cards, HUD

        [UnityTest]
        public IEnumerator Capture_ChristmasEve() => Christmas(false);

        [UnityTest]
        public IEnumerator Capture_ChristmasEve_Korean() => Christmas(true);

        static IEnumerator Christmas(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            string sfx = ko ? "_ko" : "";
            var xmas = Day(d => d.christmas);
            var day = new WinterDay(GameServices.Get<IDayQuery>(), xmas.day, Data.DisplayDayNumber(new DayId(xmas.day)));
            var season = new FakeSeason { IsSeasonExtended = true, IsExtendedDay = true, IsChristmas = true, LuckyForAll = xmas.luckyForAll, SnowAmount = xmas.snow };
            ui.OverrideServicesForTests(day: day);
            ui.OverrideSeasonForTests(season);
            yield return UITestDriver.Frames(3);

            Assert.AreEqual(DayPhase.Intro, ui.VisibleFlowScreen);
            Assert.IsTrue(ui.FlowChristmasLook, "red + green card");
            Assert.IsTrue(ui.FlowHolly, "holly sprigs");
            Assert.IsTrue(ui.FlowCornerFlakes);
            Assert.IsTrue(ui.WinterRibbonVisible);
            Assert.AreEqual(ko ? "겨울 시즌" : "WINTER SEASON", ui.WinterRibbonText);
            var ribbon = ui.WinterRibbonRect;
            Assert.AreEqual(ui.FlowCardRect.yMax, ribbon.center.y, 1f, "the ribbon straddles the top edge");
            StringAssert.Contains(ko ? "크리스마스 이브" : "Christmas Eve", ui.FlowTitle);
            StringAssert.Contains(ko ? "12는 누구에게나 행운" : "12 is LUCKY for everyone", Plain(ui.FlowToday));
            Assert.AreEqual(ko ? "10/10일차" : "DAY 10/10", ui.HudDayText, "the season total");
            AssertCardFits(ui, "christmas intro");
            yield return Day0Driver.Shot("ui_winter_christmas_intro" + sfx, true);

            // the day runs: rule line + badge, snow on the HUD boxes
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(3);
            StringAssert.Contains(ko ? "크리스마스 이브: 12 = 모두 행운" : "Christmas Eve: 12 = lucky for ALL", ui.RulesText);
            Assert.IsNull(ui.RulesOverflow());
            Assert.IsTrue(ui.SeasonBadgeVisible, "Christmas badge");
            StringAssert.Contains(ko ? "크리스마스 이브" : "CHRISTMAS EVE", ui.SeasonBadgeText);
            StringAssert.Contains("12", ui.SeasonBadgeText);
            Assert.IsTrue(ui.SeasonBadgeFits, "badge text fits");
            Assert.Greater(ui.SeasonBadgeRect.yMax, 0f);
            Assert.LessOrEqual(ui.SeasonBadgeRect.yMax, ui.RulesBoxRect.yMin + 0.5f, "under the rule box");
            Assert.IsTrue(ui.SnowCapVisible, "snow piled on the day box");
            Assert.IsNull(UIFonts.MissingGlyph(Plain(ui.SeasonBadgeText) + Plain(ui.RulesText)));
            yield return UITestDriver.Until(() => false, 30); // guests arrive
            yield return Day0Driver.Shot("ui_winter_christmas_hud" + sfx, true);

            // the day's summary: Merry Christmas!
            day.ForcedPhase = DayPhase.Summary;
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen);
            StringAssert.Contains(ko ? "메리 크리스마스!" : "Merry Christmas!", ui.FlowBody);
            Assert.IsTrue(ui.FlowChristmasLook);
            AssertCardFits(ui, "christmas summary");
            yield return UITestDriver.Until(() => ui.GradeStampLanded, 20);
            yield return Day0Driver.Shot("ui_winter_christmas_summary" + sfx, true);
            day.ForcedPhase = null;
        }

        [UnityTest]
        public IEnumerator Capture_SinglesDayIntro()
        {
            yield return Boot(false);
            var ui = UITestDriver.UI();
            var singles = Day(d => d.extended && !d.lottery && d.luckyForAll == 0);
            var day = new WinterDay(GameServices.Get<IDayQuery>(), singles.day, Data.DisplayDayNumber(new DayId(singles.day)));
            ui.OverrideServicesForTests(day: day);
            ui.OverrideSeasonForTests(new FakeSeason { IsSeasonExtended = true, IsExtendedDay = true });
            yield return UITestDriver.Frames(3);
            Assert.IsTrue(ui.FlowWinterLook, "ice-blue winter card");
            Assert.IsFalse(ui.FlowChristmasLook);
            Assert.IsTrue(ui.WinterRibbonVisible);
            Assert.IsTrue(ui.FlowCornerFlakes);
            StringAssert.Contains("Solo Traveller loves 1 and 11", Plain(ui.FlowToday));
            Assert.AreEqual("DAY 8/10", ui.HudDayText);
            AssertCardFits(ui, "singles intro");
            yield return Day0Driver.Shot("ui_winter_singles_intro", true);
        }

        // ------------------------------------------------------------------ the rare mystery line

        [UnityTest]
        public IEnumerator MysteryIntro_OnlyWhenTheGuestComesTonight()
        {
            yield return Boot(false);
            var ui = UITestDriver.UI();
            var night = Day(d => !string.IsNullOrEmpty(d.mysteryIntro));
            var day = new WinterDay(GameServices.Get<IDayQuery>(), night.day, Data.DisplayDayNumber(new DayId(night.day))) { Playable = Data.PlayableDayCount };
            var season = new FakeSeason();
            ui.OverrideServicesForTests(day: day);
            ui.OverrideSeasonForTests(season);
            yield return UITestDriver.Frames(2);
            StringAssert.DoesNotContain(night.mysteryIntro, ui.FlowBody, "not tonight (always so on a first run)");
            season.MysteryTonight = true;
            ui.OverrideSeasonForTests(season);
            yield return UITestDriver.Frames(2);
            StringAssert.Contains(night.mysteryIntro, Plain(ui.FlowBody), "the eerie extra sentence");
            StringAssert.Contains(UIFormat.HiddenHex, ui.FlowBody, "in the hidden ending's violet");
            GameServices.Get<IGameSettings>().UseKorean = true;
            yield return UITestDriver.Frames(2);
            StringAssert.Contains(night.mysteryIntroKo, Plain(ui.FlowBody));
            Assert.IsNull(ui.FlowTextOverflow());
        }

        // ------------------------------------------------------------------ lottery night

        [UnityTest]
        public IEnumerator Capture_LotteryNight() => Lottery(false);

        [UnityTest]
        public IEnumerator Capture_LotteryNight_Korean() => Lottery(true);

        static IEnumerator Lottery(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            string sfx = ko ? "_ko" : "";
            var lottery = Day(d => d.lottery);
            var day = new WinterDay(GameServices.Get<IDayQuery>(), lottery.day, Data.DisplayDayNumber(new DayId(lottery.day)));
            var season = new FakeSeason { IsSeasonExtended = true, IsExtendedDay = true, IsLotteryNight = true, SnowAmount = lottery.snow };
            ui.OverrideServicesForTests(day: day);
            ui.OverrideSeasonForTests(season);
            yield return UITestDriver.Frames(2);
            StringAssert.Contains(ko ? "당첨 번호를 2번 뽑아요" : "a winning number is drawn 2x", Plain(ui.FlowToday));
            Assert.IsTrue(ui.FlowWinterLook);
            AssertCardFits(ui, "lottery intro");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.SeasonBadgeVisible, "lottery badge");
            StringAssert.Contains(ko ? "곧 추첨합니다" : "drawing soon", Plain(ui.SeasonBadgeText));
            StringAssert.Contains(ko ? "복권: 당첨 번호 층에서 내리면 = 잭팟" : "Lottery: step out on the WINNING number = JACKPOT", ui.RulesText);
            Assert.IsNull(ui.RulesOverflow());

            // draw 1: the floor labelled 7 wins
            int floor = -1;
            for (int i = 0; i < ui.FloorButtonCount; i++) if (ui.FloorButtonText(i) == "7") floor = i;
            Assert.GreaterOrEqual(floor, 0, "a floor shows 7");
            season.LotteryNumber = 7;
            GameServices.Events.Publish(new LotteryDrawn(7, 0, 2));
            yield return null;
            Assert.IsTrue(ui.LotteryVisible, "the draw plate");
            Assert.IsTrue(ui.LotteryRolling, "the ball rolls first");
            Assert.AreEqual(ko ? "복권 추첨 1/2" : "LOTTERY DRAW 1/2", ui.LotteryCaptionText);
            StringAssert.Contains(ko ? "곧 추첨합니다" : "drawing soon", Plain(ui.SeasonBadgeText), "the badge waits for the ball");
            var plate = ui.LotteryRect;
            Assert.IsFalse(plate.Overlaps(ui.PanelRect), "clear of the panel");
            Assert.IsFalse(plate.Overlaps(ui.LuckBoxRect), "clear of the luck meter");
            Assert.IsTrue(plate.yMax <= ui.CanvasSize.y && Mathf.Abs(plate.center.x - ui.CanvasSize.x * 0.5f) < 1f, "top centre: " + plate);
            Assert.LessOrEqual(plate.height, 64f, "a slim strip");
            Assert.GreaterOrEqual(plate.yMin, ui.CanvasSize.y - 80f, "stays in the sky above the building (not over the top floors / HOTEL sign): " + plate);
            yield return null;
            Assert.IsFalse(ui.IsJackpotButton(floor), "no spoiler while the ball rolls");
            ui.SetLotteryAge(0.8f);
            yield return Day0Driver.Shot("ui_lottery_rolling" + sfx, true);
            yield return UITestDriver.Until(() => !ui.LotteryRolling, 40);
            yield return null; // the HUD reads the stopped ball next frame
            Assert.AreEqual("7", ui.LotteryBallText, "stops on the winning number");
            Assert.AreEqual(ko ? "당첨 번호 7" : "WINNING NUMBER 7", ui.LotteryResultText);
            Assert.IsTrue(ui.IsJackpotButton(floor), "the panel rings the 7 in gold");
            for (int i = 0; i < ui.FloorButtonCount; i++) if (i != floor) Assert.IsFalse(ui.IsJackpotButton(i), "only that one");
            StringAssert.Contains(ko ? "당첨 번호 7" : "WINNING NUMBER 7", Plain(ui.SeasonBadgeText));
            Assert.IsTrue(ui.SeasonBadgeFits);
            yield return Day0Driver.Shot("ui_lottery_reveal" + sfx, true);

            // a party of two steps out on it: one merged gold popup, the badge counts the jackpots
            GameServices.Events.Publish(new LotteryWon(new GuestId(9101), new FloorIndex(floor), 7));
            GameServices.Events.Publish(new LotteryWon(new GuestId(9102), new FloorIndex(floor), 7));
            yield return null;
            Assert.IsTrue(ui.LastPopupIsJackpot);
            string mult = UIFormat.Factor(Data.Rules.lotteryTipMultiplier);
            StringAssert.Contains(ko ? "잭팟 x" + mult : "JACKPOT x" + mult, ui.LastPopupText);
            StringAssert.Contains(ko ? "운 +" + 2 * Data.Rules.lotteryLuckBonus : "Luck +" + 2 * Data.Rules.lotteryLuckBonus, ui.LastPopupText, "merged: Luck adds up");
            StringAssert.Contains(UIFonts.Star + "2", Plain(ui.SeasonBadgeText), "2 jackpots tonight");
            Assert.IsNull(UIFonts.MissingGlyph(Plain(ui.LastPopupText) + Plain(ui.SeasonBadgeText) + ui.LotteryCaptionText));
            yield return UITestDriver.Frames(3);
            yield return Day0Driver.Shot("ui_lottery_jackpot" + sfx, true);

            // the plate fades away; the number stays on the badge and the button
            yield return UITestDriver.Until(() => !ui.LotteryVisible, 60);
            Assert.IsFalse(ui.LotteryVisible);
            Assert.IsTrue(ui.IsJackpotButton(floor));
            // the second draw moves the gold ring
            int other = -1;
            for (int i = 0; i < ui.FloorButtonCount; i++) if (ui.FloorButtonText(i) == "3") other = i;
            if (other >= 0)
            {
                season.LotteryNumber = 3;
                GameServices.Events.Publish(new LotteryDrawn(3, 1, 2));
                yield return UITestDriver.Until(() => !ui.LotteryRolling, 40);
                yield return null;
                Assert.AreEqual(ko ? "복권 추첨 2/2" : "LOTTERY DRAW 2/2", ui.LotteryCaptionText);
                Assert.IsTrue(ui.IsJackpotButton(other));
                Assert.IsFalse(ui.IsJackpotButton(floor));
            }
        }

        // ------------------------------------------------------------------ winter endings + 10-day journey

        [UnityTest]
        public IEnumerator Capture_WinterEndings() => WinterEndings(false);

        [UnityTest]
        public IEnumerator Capture_WinterEndings_Korean() => WinterEndings(true);

        static IEnumerator WinterEndings(bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            string sfx = ko ? "_ko" : "";
            yield return UITestDriver.ClickStart();
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            // the run went on: three winter days graded, then Christmas Eve ended it
            var summary = GameServices.Get<IDayQuery>().LastSummary;
            int baseDays = Data.PlayableDayCount;
            for (int d = 1; d <= Data.ExtendedDayCount; d++) ui.Journey.Add(baseDays + d, summary);
            var xmas = Day(x => x.christmas);
            var day = new WinterDay(GameServices.Get<IDayQuery>(), xmas.day, Data.DisplayDayNumber(new DayId(xmas.day))) { ForcedEnding = EndingKind.WinterMiracle };
            ui.OverrideServicesForTests(day: day);
            // the season is over: no "keep the hotel open" any more
            ui.OverrideSeasonForTests(new FakeSeason { IsSeasonExtended = true, IsExtendedDay = true, IsChristmas = true, LuckyForAll = 12, SnowAmount = 1f },
                new FakeSeasonFlow { CanExtendSeason = false });
            GameServices.Events.Publish(new EndingReached(EndingKind.WinterMiracle, day.CurrentDay, 80, 400));
            yield return UITestDriver.Frames(2);

            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            StringAssert.Contains(ko ? "크리스마스의 기적" : "A CHRISTMAS MIRACLE", ui.FlowTitle);
            StringAssert.Contains(ko ? "북극" : "North Pole", ui.FlowBody);
            Assert.IsTrue(ui.FlowChristmasLook);
            Assert.IsTrue(ui.FlowSnowfall);
            Assert.IsFalse(ui.WinterRibbonVisible, "the ribbon is for the season's days");
            Assert.IsFalse(ui.EndingHiddenBadgeVisible);
            Assert.IsNull(ui.SeasonOfferText, "no offer after the season");
            string journey = ui.EndingJourneyText;
            StringAssert.Contains("10:", journey, "ten stamps: " + journey);
            Assert.AreEqual(10, journey.Split(' ').Length, journey);
            StringAssert.Contains("10 / 10", Plain(ui.FlowExtra));
            Assert.AreEqual(1, ui.Endings.Record.Times(EndingKind.WinterMiracle), "recorded");
            StringAssert.Contains(ko ? "/6 해금" : "/6 unlocked", Plain(ui.EndingUnlockText));
            AssertCardFits(ui, "miracle");
            var j = ui.EndingJourneyRect;
            Assert.IsTrue(j.xMin >= ui.FlowCardRect.xMin && j.xMax <= ui.FlowCardRect.xMax, "10 stamps fit the card: " + j);
            yield return Day0Driver.Shot("ui_winter_ending_miracle" + sfx, true);

            // the eye button (V) hides the card to watch the ending scene; any key brings it back (Enter does nothing else)
            Assert.IsTrue(ui.EndingViewButtonVisible, "eye button on the ending card");
            var eye = ui.EndingViewButtonRect;
            var cardRect = ui.FlowCardRect;
            Assert.IsTrue(eye.xMin >= cardRect.xMin && eye.xMax <= cardRect.xMax && eye.yMax <= cardRect.yMax && eye.yMin >= cardRect.yMin, "inside the card: " + eye);
            ui.ClickEndingView();
            yield return UITestDriver.Frames(3);
            Assert.AreEqual(ScenicView.Mode.Scenic, ui.ScenicMode, "scenic view over the ending");
            Assert.IsTrue(ui.HudHidden);
            Assert.AreEqual(false, ui.FlowEnterEnabled, "Enter only brings the card back");
            ui.InjectPress(enter: true);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(ScenicView.Mode.None, ui.ScenicMode);
            Assert.AreEqual(DayPhase.Ending, GameServices.Get<IDayQuery>().Phase, "still the ending: no restart");
            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            yield return UITestDriver.Frames(2);

            day.ForcedEnding = EndingKind.SilentNight;
            GameServices.Events.Publish(new EndingReached(EndingKind.SilentNight, day.CurrentDay, 40, 200));
            yield return UITestDriver.Frames(2);
            StringAssert.Contains(ko ? "고요한 밤" : "SILENT NIGHT", ui.FlowTitle);
            Assert.IsTrue(ui.FlowWinterLook, "ice-blue card");
            AssertCardFits(ui, "silent night");
            if (!ko) yield return Day0Driver.Shot("ui_winter_ending_silent", true);
        }
    }
}
