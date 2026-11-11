using System.Collections;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>The real IDayQuery with another <see cref="IDayQuery.Ending"/> (the 4th-floor ending is hard to play into).</summary>
    sealed class EndingOverrideDay : IDayQuery
    {
        readonly IDayQuery inner;
        public EndingKind ForcedEnding;
        public EndingOverrideDay(IDayQuery inner, EndingKind ending) { this.inner = inner; ForcedEnding = ending; }
        public DayId CurrentDay => inner.CurrentDay;
        public int DayCount => inner.DayCount;
        public int DisplayDay => inner.DisplayDay;
        public int PlayableDayCount => inner.PlayableDayCount;
        public DayPhase Phase => inner.Phase;
        public bool IsTutorial => inner.IsTutorial;
        public bool IsFriday13 => inner.IsFriday13;
        public bool IsStarNight => inner.IsStarNight;
        public bool IsRainy => inner.IsRainy;
        public float DurationSeconds => inner.DurationSeconds;
        public float ElapsedSeconds => inner.ElapsedSeconds;
        public float RemainingSeconds => inner.RemainingSeconds;
        public EndingKind Ending => ForcedEnding;
        public DaySummary LastSummary => inner.LastSummary;
    }

    /// <summary>Hidden-ending badge on the ending card, the title's ENDINGS button and collection card, the saved record.</summary>
    public class UIEndingsTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        [UnityTest]
        public IEnumerator EndingCard_HiddenBadge_OnlyForTheFourthFloor() => HiddenBadge(false);

        [UnityTest]
        public IEnumerator EndingCard_HiddenBadge_OnlyForTheFourthFloor_Korean() => HiddenBadge(true);

        static IEnumerator HiddenBadge(bool ko)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            var store = new MemoryEndingStore();
            EndingCollection.StoreOverride = store;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.ClickStart();
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            var real = GameServices.Get<IDayQuery>().Ending;
            Assert.AreNotEqual(EndingKind.FourthFloor, real, "skipped days never meet the mystery guest");
            Assert.AreEqual(DayPhase.Ending, ui.VisibleFlowScreen);
            Assert.IsFalse(ui.EndingHiddenBadgeVisible, real + ": no hidden badge");
            Assert.IsFalse(ui.FlowHiddenLook);
            Assert.AreEqual(1, ui.Endings.Unlocked, "the real ending was recorded");
            Assert.AreEqual(1, ui.Endings.Record.Times(real));
            Assert.AreEqual(((int)real) + ":1,L:50,T:0", store.Value, "saved, with the best run (Luck 50, nothing earned)");
            string unlock = Plain(ui.EndingUnlockText);
            Assert.AreEqual(ko ? "엔딩 1/6 해금   새 엔딩!" : "Endings 1/6 unlocked   NEW!", unlock);

            // the same card for the 4th-floor ending: badge on the top edge, eerie violet look, 2/4
            var day = new EndingOverrideDay(GameServices.Get<IDayQuery>(), EndingKind.FourthFloor);
            ui.OverrideServicesForTests(day: day);
            GameServices.Events.Publish(new EndingReached(EndingKind.FourthFloor, day.CurrentDay, 75, 120));
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.EndingHiddenBadgeVisible, "hidden badge on the 4th-floor ending");
            Assert.AreEqual(ko ? "히든 엔딩" : "HIDDEN ENDING", ui.EndingHiddenBadgeText);
            Assert.IsTrue(ui.EndingHiddenBadgeFits, "badge text on one line inside its plate");
            Assert.IsTrue(ui.FlowHiddenLook, "violet card");
            StringAssert.Contains(ko ? "4층" : "THE FOURTH FLOOR", ui.FlowTitle);
            var badge = ui.EndingHiddenBadgeRect;
            var card = ui.FlowCardRect;
            var size = ui.CanvasSize;
            Assert.AreEqual(card.center.x, badge.center.x, 1f, "centred on the card");
            Assert.AreEqual(card.yMax, badge.center.y, 1f, "straddles the card's top edge");
            Assert.IsTrue(badge.xMin >= 0f && badge.yMax <= size.y && badge.yMin >= 0f, "on screen: " + badge);
            Assert.GreaterOrEqual(badge.yMin, ui.FlowTitleRect.yMax - 0.5f, "clear of the title");
            Assert.AreEqual(ko ? "엔딩 2/6 해금   새 엔딩!" : "Endings 2/6 unlocked   NEW!", Plain(ui.EndingUnlockText));
            Assert.IsNull(UIFonts.MissingGlyph(ui.EndingHiddenBadgeText + ui.EndingUnlockText), "every glyph in the font");
            Assert.IsNull(ui.FirstOffGridOrForeignText());
            yield return UITestDriver.Until(() => false, 3); // the badge breathes, never vanishes
            Assert.IsTrue(ui.EndingHiddenBadgeVisible);
            yield return Day0Driver.Shot("ui_ending_hidden" + (ko ? "_ko" : ""), true);

            // back to a Luck ending: the badge goes away again (no NEW: seen before)
            day.ForcedEnding = real;
            ui.Endings.LastNew = EndingKind.None;
            ui.OverrideServicesForTests(day: day);
            yield return null;
            Assert.IsFalse(ui.EndingHiddenBadgeVisible);
            Assert.IsFalse(ui.FlowHiddenLook);
            Assert.AreEqual(ko ? "엔딩 2/6 해금" : "Endings 2/6 unlocked", Plain(ui.EndingUnlockText));
        }

        [UnityTest]
        public IEnumerator RealEnding_IsCounted_TitleShowsIt_DemoNeverCounts()
        {
            GameplayRoot.SeedOverride = 3;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            var store = new MemoryEndingStore();
            EndingCollection.StoreOverride = store;
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            var real = GameServices.Get<IDayQuery>().Ending;
            Assert.IsTrue(ui.EndingTitleButtonVisible);
            ui.ClickEndingTitle();
            yield return UITestDriver.Frames(3);
            Assert.IsTrue(ui.TitleVisible);
            Assert.AreEqual("ENDINGS 1/6", ui.EndingsButtonText);
            // the attract demo plays behind the title for a while: nothing more is counted
            yield return UITestDriver.Until(() => false, 60);
            Assert.AreEqual(1, ui.Endings.Unlocked);
            StringAssert.StartsWith(((int)real) + ":1,", store.Value);
            // a second run to the same ending counts it twice, unlocks nothing new
            ui.ClickTitleStart();
            yield return UITestDriver.Frames(2);
            UITestDriver.RunToEnding(UITestDriver.Session());
            yield return UITestDriver.Frames(2);
            var second = GameServices.Get<IDayQuery>().Ending;
            var rec = ui.Endings.Record;
            int total = 0;
            foreach (var k in EndingRecord.All) total += rec.Times(k);
            Assert.AreEqual(2, total, "two endings reached");
            if (second == real)
            {
                Assert.AreEqual(1, rec.UnlockedCount);
                Assert.AreEqual(2, rec.Times(real));
                Assert.AreEqual("Endings 1/6 unlocked", Plain(ui.EndingUnlockText), "no NEW for a repeat");
            }
            else
            {
                Assert.AreEqual(2, rec.UnlockedCount);
                Assert.AreEqual("Endings 2/6 unlocked   NEW!", Plain(ui.EndingUnlockText));
            }
        }

        [UnityTest]
        public IEnumerator Title_EndingsButton_CollectionCard() => Collection(false);

        [UnityTest]
        public IEnumerator Title_EndingsButton_CollectionCard_Korean() => Collection(true);

        static IEnumerator Collection(bool ko)
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            EndingCollection.StoreOverride = new MemoryEndingStore("1:2,3:1"); // Prosperous x2, Haunted x1
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return UITestDriver.Frames(3);
            string sfx = ko ? "_ko" : "";

            Assert.AreEqual(ko ? "엔딩 2/6" : "ENDINGS 2/6", ui.EndingsButtonText);
            StringAssert.Contains(ui.EndingsButtonText, ui.TitleText);
            var start = ui.TitlePartRect(MenuView.TitlePart.Start);
            var endings = ui.TitlePartRect(MenuView.TitlePart.Endings);
            var credits = ui.TitlePartRect(MenuView.TitlePart.Credits);
            Assert.Greater(start.yMin, endings.yMax, "ENDINGS under START");
            Assert.Greater(endings.yMin, credits.yMax, "above CREDITS");
            Assert.AreEqual(credits.size, endings.size, "same size as CREDITS");
            Assert.IsTrue(ui.TitleLabelFits(MenuView.TitlePart.Endings, out float font), "label on one line");
            Assert.GreaterOrEqual(font, 24f, "not shrunk");
            if (!ko) yield return Day0Driver.Shot("ui_title_endings_button", true);

            ui.ClickEndings();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.EndingsVisible, "ENDINGS -> collection card");
            Assert.IsFalse(ui.TitleMenuVisible, "the title buttons step aside");
            Assert.AreEqual(6, ui.EndingsSlotCount);
            CollectionAssert.AreEqual(EndingRecord.All, new[] { ui.EndingsSlotKind(0), ui.EndingsSlotKind(1), ui.EndingsSlotKind(2), ui.EndingsSlotKind(3), ui.EndingsSlotKind(4), ui.EndingsSlotKind(5) });
            StringAssert.Contains(ko ? "6개 중 2개 발견" : "2 of 6 found", ui.EndingsCardText);

            var data = GameServices.Get<GameData>();
            var dayQ = GameServices.Get<IDayQuery>();
            int lastDay = dayQ.PlayableDayCount > 0 ? dayQ.PlayableDayCount : dayQ.DayCount;
            int luck = data.Rules.prosperousEndingLuck;

            string prosperous = Plain(ui.EndingsSlotText(0));
            Assert.IsTrue(ui.EndingsSlotUnlocked(0));
            StringAssert.Contains(ko ? "번창" : "PROSPEROUS", prosperous);
            StringAssert.Contains(ko ? "동네에서 가장 운 좋은 호텔" : "THE LUCKIEST HOTEL IN TOWN", prosperous);
            StringAssert.Contains(ko ? "2회 달성" : "Seen x2", prosperous);

            string modest = Plain(ui.EndingsSlotText(1));
            Assert.IsFalse(ui.EndingsSlotUnlocked(1));
            StringAssert.Contains("???", modest);
            StringAssert.Contains(ko ? lastDay + "일차" : "Day " + lastDay, modest, "the rule, from the data");
            StringAssert.Contains(luck.ToString(), modest);
            StringAssert.DoesNotContain(ko ? "평범한 영업" : "BUSINESS AS USUAL", modest, "locked: no spoiler");

            string haunted = Plain(ui.EndingsSlotText(2));
            Assert.IsTrue(ui.EndingsSlotUnlocked(2));
            StringAssert.Contains(ko ? "1회 달성" : "Seen x1", haunted);

            // the winter endings: locked, their rule names Christmas Eve (the miracle also the Luck)
            string miracle = Plain(ui.EndingsSlotText(3));
            Assert.AreEqual(EndingKind.WinterMiracle, ui.EndingsSlotKind(3));
            Assert.IsFalse(ui.EndingsSlotUnlocked(3));
            StringAssert.Contains(ko ? "크리스마스 이브를 운 " + data.Rules.WinterMiracleLuck : "through Christmas Eve with Luck " + data.Rules.WinterMiracleLuck, miracle);
            StringAssert.DoesNotContain(ko ? "크리스마스의 기적" : "A CHRISTMAS MIRACLE", miracle, "locked: no spoiler");
            StringAssert.Contains(ko ? "크리스마스 이브까지" : "through Christmas Eve.", Plain(ui.EndingsSlotText(4)));

            string fourth = Plain(ui.EndingsSlotText(5));
            Assert.AreEqual(EndingKind.FourthFloor, ui.EndingsSlotKind(5), "the hidden one comes last");
            Assert.IsFalse(ui.EndingsSlotUnlocked(5));
            Assert.IsTrue(ui.EndingsSlotBadgeVisible(5), "the hidden slot wears its badge");
            Assert.AreEqual(ko ? "히든 엔딩" : "HIDDEN ENDING", ui.EndingsSlotBadgeText(5));
            for (int i = 0; i < 5; i++) Assert.IsFalse(ui.EndingsSlotBadgeVisible(i), "only the hidden slot has a badge");
            StringAssert.Contains(ko ? "어떤 밤엔 누군가 없는 층을 찾습니다…" : "Some nights, someone asks for a floor that isn't there…", fourth);
            StringAssert.DoesNotContain(ko ? "첫 방문" : "first stay", fourth, "hidden: it only whispers, never explains");
            StringAssert.DoesNotContain(ko ? "4층" : "4TH FLOOR", fourth, "locked: the name stays hidden too");
            StringAssert.DoesNotContain(ko ? "운 " : "Luck", fourth, "no rule for the hidden one");

            AssertCardFits(ui, ko ? "ko 2/6" : "en 2/6");
            yield return Day0Driver.Shot("ui_endings_collection" + sfx, true);

            // nothing found / everything found: still fits, the hidden slot reveals itself
            ui.UseEndingStoreForTests(new MemoryEndingStore());
            yield return null;
            StringAssert.Contains(ko ? "6개 중 0개 발견" : "0 of 6 found", ui.EndingsCardText);
            for (int i = 0; i < 6; i++) Assert.IsFalse(ui.EndingsSlotUnlocked(i));
            AssertCardFits(ui, (ko ? "ko" : "en") + " 0/6");
            if (!ko) yield return Day0Driver.Shot("ui_endings_collection_0", true);

            ui.UseEndingStoreForTests(new MemoryEndingStore("1:3,2:1,3:12,4:1,5:1,6:2"));
            yield return null;
            StringAssert.Contains(ko ? "6개 중 6개 발견" : "6 of 6 found", ui.EndingsCardText);
            string found = Plain(ui.EndingsSlotText(5));
            StringAssert.Contains(ko ? "4층" : "4TH FLOOR", found);
            StringAssert.Contains(ko ? "4를 가렸지만" : "You covered the 4.", found);
            Assert.IsTrue(ui.EndingsSlotBadgeVisible(5));
            StringAssert.Contains(ko ? "12회 달성" : "Seen x12", Plain(ui.EndingsSlotText(2)));
            StringAssert.Contains(ko ? "크리스마스의 기적" : "A CHRISTMAS MIRACLE", Plain(ui.EndingsSlotText(3)));
            StringAssert.Contains(ko ? "2회 달성" : "Seen x2", Plain(ui.EndingsSlotText(4)));
            AssertCardFits(ui, (ko ? "ko" : "en") + " 6/6");
            yield return Day0Driver.Shot("ui_endings_collection_4" + sfx, true);

            ui.ClickEndingsBack();
            yield return null;
            Assert.IsFalse(ui.EndingsVisible, "BACK closes the card");
            Assert.IsTrue(ui.TitleMenuVisible);
            Assert.AreEqual(ko ? "엔딩 6/6" : "ENDINGS 6/6", ui.EndingsButtonText, "the button follows the record");
            ui.ClickEndings();
            yield return null;
            Assert.IsTrue(ui.EndingsVisible);
            ui.CloseEndingsLikeEsc();
            yield return null;
            Assert.IsFalse(ui.EndingsVisible, "Esc closes it too");
            Assert.IsTrue(ui.TitleMenuVisible);
            // credits and endings never stack
            ui.ClickEndings();
            yield return null;
            ui.ClickTitleStart();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.EndingsVisible, "leaving the title closes the card");
        }

        static void AssertCardFits(UIRoot ui, string what)
        {
            Assert.IsTrue(ui.EndingsFitOnScreen, what + ": card fits the canvas " + ui.EndingsCardRect + " in " + ui.CanvasSize);
            var r = ui.EndingsCardRect;
            Assert.LessOrEqual(r.height, UIStyle.RefHeight - 16f, what + ": fits a 720-high screen");
            Assert.IsNull(ui.EndingsSlotOverflow(), what);
            for (int i = 0; i < ui.EndingsSlotCount; i++)
                Assert.IsNull(UIFonts.MissingGlyph(Plain(ui.EndingsSlotText(i)) + ui.EndingsSlotBadgeText(i)), what + " slot " + i + " glyphs");
            Assert.IsNull(UIFonts.MissingGlyph(ui.EndingsCardText), what);
            Assert.IsNull(ui.FirstOffGridOrForeignText(), what);
        }
    }
}
