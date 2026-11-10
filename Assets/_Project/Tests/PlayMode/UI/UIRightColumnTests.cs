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
    /// The HUD's right-hand column restacks when the rule box folds into its "?" tab (H / HIDE) and unfolds again: the
    /// tab sits where the box starts on the column's right edge, and every box under it (Friday the 13th / star night
    /// badge, wish explain card, tutorial cards, charms) moves up to hang right under it - no gap, no stale slot.
    /// Day 5 (Friday the 13th), Day 6 (wedding), Day 7 (star night) and the Day 0 lesson, open and folded, en + ko.
    /// </summary>
    public class UIRightColumnTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            yield return null;
        }

        static IEnumerator Boot(bool ko, bool skipTutorial = true)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain(skipTutorial);
            GameServices.Get<IGameSettings>().UseKorean = ko;
            yield return null;
        }

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

        static IEnumerator SetRules(UIRoot ui, bool open)
        {
            if (ui.RulesOpen != open)
            {
                if (open) ui.ClickRulesTab();
                else ui.ClickRulesHide();
            }
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(open, ui.RulesOpen);
            Assert.AreEqual(open, ui.RulesBoxShown, "box shown when open");
            Assert.AreEqual(!open, ui.RulesTabShown, "tab shown when folded");
        }

        /// <summary>Head (box / tab) where the box starts on the right edge; each visible box under it hangs right below
        /// the one above (same right edge, a small even gap), the charms row last.</summary>
        static void AssertColumn(UIRoot ui, string what, int expectBelow)
        {
            var size = ui.CanvasSize;
            float right = size.x - 24f;
            bool open = ui.RulesOpen;
            var head = open ? ui.RulesBoxRect : ui.RulesTabRect;
            Assert.AreEqual(right, head.xMax, 1f, what + ": " + (open ? "box" : "tab") + " on the column's right edge");
            Assert.AreEqual(size.y - HudView.RulesTop, head.yMax, 1f, what + ": head where the box starts");
            if (!open) Assert.LessOrEqual(head.width * head.height, HudView.TabW * HudView.TabH + 1f, what + ": a small tab");

            var below = new List<KeyValuePair<string, Rect>>();
            if (ui.FridayBadgeVisible) below.Add(new KeyValuePair<string, Rect>("friday badge", ui.FridayBadgeRect));
            if (ui.StarBadgeVisible) below.Add(new KeyValuePair<string, Rect>("star badge", ui.StarBadgeRect));
            if (ui.WishCardVisible) below.Add(new KeyValuePair<string, Rect>("wish card", ui.WishCardRect));
            if (ui.TutorialPromptRect.width > 0f) below.Add(new KeyValuePair<string, Rect>("tutorial prompt", ui.TutorialPromptRect));
            if (ui.TutorialWhyRect.width > 0f) below.Add(new KeyValuePair<string, Rect>("tutorial why", ui.TutorialWhyRect));
            if (ui.CharmsVisible) below.Add(new KeyValuePair<string, Rect>("charms", ui.CharmsRect));
            below.Sort((a, b) => b.Value.yMax.CompareTo(a.Value.yMax));
            Assert.AreEqual(expectBelow, below.Count, what + ": boxes under the rule box");

            float prev = head.yMin;
            string prevName = open ? "rule box" : "tab";
            foreach (var kv in below)
            {
                var r = kv.Value;
                Assert.AreEqual(right, r.xMax, 1f, what + ": " + kv.Key + " on the column's right edge");
                float gap = prev - r.yMax;
                Assert.GreaterOrEqual(gap, 6f, what + ": " + kv.Key + " " + r + " clear of the " + prevName);
                Assert.LessOrEqual(gap, 12f, what + ": " + kv.Key + " " + r + " right under the " + prevName + " (gap " + gap + ")");
                prev = r.yMin;
                prevName = kv.Key;
            }
            Assert.GreaterOrEqual(prev, 0f, what + ": column on screen");
            Assert.IsNull(ui.FirstOffGridOrForeignText(), what + ": texts on the grid");
        }

        /// <summary>Open (capture), fold (capture), open again: the column follows each time.</summary>
        static IEnumerator BothStates(UIRoot ui, string shot, int expectBelow)
        {
            yield return SetRules(ui, true);
            yield return Day0Driver.Shot(shot + "_open", true);
            AssertColumn(ui, shot + " open", expectBelow);
            float openBottom = ui.RightColumnBottom;

            ui.InjectPress(h: true); // H folds it
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.RulesOpen, shot + ": H folded the box");
            yield return Day0Driver.Shot(shot + "_folded", true);
            AssertColumn(ui, shot + " folded", expectBelow);
            Assert.Less(ui.RightColumnBottom, openBottom - 40f, shot + ": the column got shorter");

            yield return SetRules(ui, true);
            AssertColumn(ui, shot + " reopened", expectBelow);
            Assert.AreEqual(openBottom, ui.RightColumnBottom, 0.5f, shot + ": back where it was");
        }

        [UnityTest]
        public IEnumerator Capture_Column_Friday13([Values(false, true)] bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            yield return ToIntro(d => d.IsFriday13, "Friday the 13th");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.FridayBadgeVisible, "friday badge");
            yield return BothStates(ui, "ui_column_friday" + (ko ? "_ko" : ""), 1);
        }

        [UnityTest]
        public IEnumerator Capture_Column_Wedding([Values(false, true)] bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            yield return ToIntro(d => d.DisplayDay == 6, "Day 6");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsFalse(ui.FridayBadgeVisible || ui.StarBadgeVisible, "no event badge on the wedding day");
            yield return BothStates(ui, "ui_column_wedding" + (ko ? "_ko" : ""), 0);
        }

        [UnityTest]
        public IEnumerator Capture_Column_StarNight([Values(false, true)] bool ko)
        {
            yield return Boot(ko);
            var ui = UITestDriver.UI();
            yield return ToIntro(d => d.IsStarNight, "star night");
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(2);
            Assert.IsTrue(ui.StarBadgeVisible, "star badge");
            Assert.IsTrue(ui.WishCardVisible, "wish explain card");
            yield return BothStates(ui, "ui_column_starnight" + (ko ? "_ko" : ""), 2);
        }

        [UnityTest]
        public IEnumerator Capture_Column_Tutorial([Values(false, true)] bool ko)
        {
            yield return Boot(ko, skipTutorial: false);
            var ui = UITestDriver.UI();
            Assert.IsTrue(GameServices.Get<IDayQuery>().IsTutorial);
            yield return UITestDriver.ClickStart();
            yield return Day0Driver.WaitForWave(ui, 1);
            Assert.Greater(ui.TutorialPromptRect.width, 0f, "a lesson card");
            yield return BothStates(ui, "ui_column_tutorial" + (ko ? "_ko" : ""), 1);
        }
    }
}
