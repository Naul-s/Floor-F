using System.Collections;
using System.Text;
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
    /// "Text is tiny until the language is toggled once" (WebGL, fresh page load): a fresh boot - runtime font assets
    /// created from scratch with EMPTY dynamic atlases - must lay out every UI text exactly as it is after a language
    /// round trip (ko -> en). Run: tools/unity-batch.ps1 -Mode playmode -Filter UIFreshBoot
    /// </summary>
    public class UIFreshBootTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameServices.TryGet(out IGameSettings s)) s.UseKorean = false;
            UITestDriver.Reset();
            UIRoot.RecordBootLayout = false;
            yield return null;
        }

        static IGameSettings Settings => GameServices.Get<IGameSettings>();

        static void FreshBoot(bool korean)
        {
            UIFonts.ResetForTests(); // new font assets, nothing in their atlases yet: a fresh page load
            UIRoot.RecordBootLayout = true; // the very first layout, before UIRoot's own relayout
            GameplayRoot.SeedOverride = 7;
            var store = new MemorySettingsStore();
            GameplayRoot.SettingsStoreOverride = store;
            EndingCollection.StoreOverride = new MemoryEndingStore("1:2");
            if (korean) store.SetInt(SettingsKeys.UseKorean, 1); // persisted choice: the UI is BUILT in Korean
        }

        /// <summary>null when both snapshots match, else the first differences ("path | size | text").</summary>
        static string Diff(string[] before, string[] after)
        {
            if (before.Length != after.Length) return "text count " + before.Length + " -> " + after.Length;
            var sb = new StringBuilder();
            int n = 0;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] == after[i]) continue;
                if (n++ < 8) sb.Append("\n  boot: ").Append(before[i]).Append("\n  after: ").Append(after[i]);
            }
            return n == 0 ? null : n + " texts differ:" + sb;
        }

        /// <summary>Snapshot (font sizes + drawn mesh bounds), language there and back, let TMP regenerate, snapshot again.</summary>
        static IEnumerator RoundTrip(UIRoot ui, bool korean, string what)
        {
            Time.captureDeltaTime = 0.0001f; // the clock and animations stand still between the two snapshots
            yield return null;
            yield return null;
            var before = ui.TextSizeSnapshot();
            Settings.UseKorean = !korean; // SettingsChanged -> RefreshLanguage, synchronously
            yield return null;
            Settings.UseKorean = korean;
            Assert.AreEqual(korean, ui.Korean);
            yield return null;
            yield return null;
            Assert.IsNull(Diff(before, ui.TextSizeSnapshot()), what + ": fresh boot vs after a language round trip");
            Assert.GreaterOrEqual(ui.RelayoutCount, 1, "UIRoot laid everything out again after the first rendered frame");
            Assert.IsNotNull(ui.BootFontSizes, "boot layout recorded");
            Assert.IsNull(Diff(ui.BootFontSizes, ui.FontSizeSnapshot()), what + ": the FIRST layout (built from prewarmed fonts) already has the final font sizes");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FreshBoot_Title_SameLayoutAsAfterLanguageToggle([Values(false, true)] bool korean)
        {
            FreshBoot(korean);
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            Assert.AreEqual(korean, ui.Korean);
            Assert.IsTrue(ui.FontsCreated, "fresh runtime font assets");
            foreach (var p in new[] { MenuView.TitlePart.Endings, MenuView.TitlePart.Credits, MenuView.TitlePart.Language })
            {
                Assert.IsTrue(ui.TitleLabelFits(p, out float size), p + " fits");
                Assert.AreEqual(24f, size, p + " label at its full 24px on a fresh boot (not shrunk to 12)");
            }
            Assert.IsTrue(ui.TitleLabelFits(MenuView.TitlePart.Start, out float start));
            Assert.AreEqual(36f, start, "START at 36px");
            yield return RoundTrip(ui, korean, "title");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FreshBoot_DayIntro_SameLayoutAsAfterLanguageToggle([Values(false, true)] bool korean)
        {
            FreshBoot(korean);
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            Assert.AreEqual(korean, ui.Korean);
            yield return RoundTrip(ui, korean, "intro card + HUD + panel");
        }

        /// <summary>Snapshot what the screen shows now, lay everything out again (UIRoot.Relayout = what a language toggle
        /// does), and compare: the first layout of a screen must already be the final one.</summary>
        static IEnumerator SameAfterRelayout(UIRoot ui, string screen)
        {
            Time.captureDeltaTime = 0.0001f;
            yield return null;
            yield return null;
            var before = ui.TextSizeSnapshot();
            ui.Relayout();
            yield return null;
            yield return null;
            Assert.IsNull(Diff(before, ui.TextSizeSnapshot()), screen + ": first layout vs relayout");
        }

        /// <summary>Every screen opened for the first time (layout groups size their children only in the next layout
        /// pass: a text fitted before that saw a 100px-wide default rect) - title, credits, endings, intro, day HUD,
        /// pause, summary, shop, ending - in English and Korean.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator EveryScreen_FirstLayoutIsFinal([Values(false, true)] bool korean)
        {
            FreshBoot(korean);
            yield return UITestDriver.LoadTitle();
            var ui = UITestDriver.UI();
            yield return SameAfterRelayout(ui, "title");
            ui.ClickCredits();
            yield return SameAfterRelayout(ui, "credits");
            ui.ClickCreditsBack();
            ui.ClickEndings();
            yield return SameAfterRelayout(ui, "endings");
            ui.ClickEndingsBack();

            UITestDriver.Reset();
            FreshBoot(korean);
            yield return UITestDriver.LoadMain();
            ui = UITestDriver.UI();
            yield return SameAfterRelayout(ui, "intro");
            yield return UITestDriver.ClickStart();
            yield return SameAfterRelayout(ui, "day");
            ui.ClickPauseButton();
            yield return SameAfterRelayout(ui, "pause");
            ui.ClickResume();
            var s = UITestDriver.Session();
            s.Score.AddTips(160 - s.Score.Tips, earned: true);
            s.EndDayNow();
            yield return SameAfterRelayout(ui, "summary");
            ui.ClickFlowButton();
            yield return SameAfterRelayout(ui, "shop");
            UITestDriver.RunToEnding(s);
            yield return SameAfterRelayout(ui, "ending");
        }
    }
}