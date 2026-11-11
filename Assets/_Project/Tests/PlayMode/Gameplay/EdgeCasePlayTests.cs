using System.Collections;
using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// Gameplay MonoBehaviour edge cases in the real Main scene (every module installed, so a UI / Audio / Rendering
    /// handler that throws or logs an error fails the test): Esc / P pause toggles in every phase (the day's establishing
    /// shot plays at the Intro), focus loss (OnApplicationFocus / OnApplicationPause / WebPause) in every phase, and the
    /// debug cheats - day jumps 0..7 from every phase, forced endings from the shop - each followed by playing again.
    /// Keys are fed already mapped (KeyboardInput.Press / DebugOverlay.Feed): the test assembly has no Input System reference.
    /// </summary>
    public class EdgeCasePlayTests
    {
        static readonly DebugKey[] NoKeys = new DebugKey[0];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            GameplayRoot.ForceFocusPauseForTests = true;
            GameplayRoot.SeedOverride = 7;
            Time.captureDeltaTime = 0f;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.ForceFocusPauseForTests = false;
            GameplayRoot.SeedOverride = null;
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        // ------------------------------------------------------------------ helpers

        static GameplayRoot Root() => Object.FindFirstObjectByType<GameplayRoot>();
        static GameSession S() => Root().Session;
        static KeyboardInput Keys() => Root().GetComponent<KeyboardInput>();
        static DebugOverlay Overlay() => Root().GetComponent<DebugOverlay>();

        static void Cheat(DebugKey key, bool shift = false) => Overlay().Feed(false, shift, false, new[] { key }, null);

        static void DebugModeOn()
        {
            var o = Overlay();
            if (o.Cheats.Enabled) return;
            foreach (var c in "LUCKY") o.Feed(false, false, false, NoKeys, new List<char> { c });
            Assert.IsTrue(o.Cheats.Enabled);
        }

        static IEnumerator Frames(int n, float dt = 0.1f)
        {
            Time.captureDeltaTime = dt;
            for (int i = 0; i < n; i++) yield return null;
            Time.captureDeltaTime = 0f;
        }

        public enum From { Title, Intro, Running, RunningPaused, Summary, Shop, Ending }

        /// <summary>Puts the running game into <paramref name="from"/> (debug cheats on), with a few rendered frames.</summary>
        static IEnumerator Into(From from)
        {
            var s = S();
            DebugModeOn();
            if (from == From.Title)
            {
                s.ReturnToTitle();
                yield return Frames(5);
                Assert.AreEqual(DayPhase.Title, s.Phase);
                yield break;
            }
            Cheat(DebugKey.Digit2);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            yield return Frames(2);
            if (from == From.Intro) yield break;
            Assert.IsTrue(s.BeginDay());
            yield return Frames(15);
            Assert.AreEqual(DayPhase.Running, s.Phase);
            if (from == From.Running) yield break;
            if (from == From.RunningPaused) { Assert.IsTrue(s.SetPaused(true)); yield return Frames(2); yield break; }
            if (from == From.Ending) { Cheat(DebugKey.G, shift: true); yield return Frames(2); Assert.AreEqual(DayPhase.Ending, s.Phase); yield break; }
            Cheat(DebugKey.N);
            Assert.AreEqual(DayPhase.Summary, s.Phase);
            yield return Frames(2);
            if (from == From.Summary) yield break;
            Assert.IsTrue(s.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, s.Phase);
            // something bought for tomorrow: a label on the real 4th floor and a charm
            Cheat(DebugKey.T, shift: true);
            foreach (var o in s.Offers)
            {
                if (o.Sticker.IsCharm) s.TryBuy(o.Id);
                else if (s.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor) != StickerApplyResult.Applied)
                    for (int f = 0; f < s.Building.FloorCount; f++) if (s.TryBuyAndApply(o.Id, new FloorIndex(f)) == StickerApplyResult.Applied) break;
            }
            yield return Frames(2);
        }

        static void AssertCleanIntro(GameSession s, int day, string ctx)
        {
            Assert.AreEqual(DayPhase.Intro, s.Phase, ctx);
            Assert.AreEqual(day, s.DisplayDay, ctx);
            Assert.AreEqual(EndingKind.None, s.Ending, ctx);
            Assert.IsFalse(s.IsPaused || s.IsOnTitle || s.IsOpen || s.TutorialHold, ctx + ": paused / title / shop / hold");
            Assert.IsEmpty(s.ActiveCharms, ctx + ": charms");
            Assert.IsEmpty(s.Guests.ActiveGuests, ctx + ": guests");
            for (int f = 0; f < s.Building.FloorCount; f++) Assert.IsFalse(s.Building.IsRelabelled(new FloorIndex(f)), ctx + ": floor " + (f + 1));
        }

        /// <summary>The game goes on from an Intro: the day starts, runs a while, ends to the summary.</summary>
        static IEnumerator PlayOn(GameSession s, string ctx)
        {
            Assert.IsTrue(s.BeginDay(), ctx + ": BeginDay");
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 20; i++)
            {
                if (s.TutorialHold) s.SetTutorialHold(false); // Day 0 coach marks wait for the player
                yield return null;
            }
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(DayPhase.Running, s.Phase, ctx);
            Assert.Greater(s.ElapsedSeconds, 0f, ctx + ": the clock runs");
            Cheat(DebugKey.N);
            Assert.AreEqual(DayPhase.Summary, s.Phase, ctx);
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ pause keys

        [Test]
        public void PauseKey_EscOrPlainP_NotShiftCtrlAlt()
        {
            Assert.IsTrue(KeyboardInput.IsPauseKey(true, false, false, false, false), "Esc");
            Assert.IsTrue(KeyboardInput.IsPauseKey(true, false, true, false, false), "Shift+Esc still Esc");
            Assert.IsTrue(KeyboardInput.IsPauseKey(false, true, false, false, false), "P");
            Assert.IsFalse(KeyboardInput.IsPauseKey(false, true, true, false, false), "Shift+P = debug Prosperous ending");
            Assert.IsFalse(KeyboardInput.IsPauseKey(false, true, false, true, false), "Ctrl+P = print");
            Assert.IsFalse(KeyboardInput.IsPauseKey(false, true, false, false, true), "Alt+P");
            Assert.IsFalse(KeyboardInput.IsPauseKey(false, false, false, false, false));
        }

        [UnityTest]
        public IEnumerator EscToggle_InEveryPhase_FreezesFlowAndClock_AndResumes()
        {
            var s = S();
            var keys = Keys();
            Assert.AreEqual(DayPhase.Title, s.Phase);
            Assert.IsFalse(keys.Press(KeyPress.Escape), "no pause on the title");
            Assert.IsFalse(s.IsPaused);

            Assert.IsTrue(s.StartGameFromTitle());
            yield return Frames(2); // the Day 0 establishing shot plays at the Intro
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.IsTrue(keys.Press(KeyPress.Escape), "Esc during the establishing shot pauses");
            Assert.IsTrue(s.IsPaused);
            yield return Frames(10);
            Assert.IsFalse(s.BeginDay(), "paused: the day does not start");
            Assert.IsFalse(keys.Press(KeyPress.Enter), "paused: Enter does not start it either");
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.IsTrue(keys.Press(KeyPress.Escape));
            Assert.IsFalse(s.IsPaused, "Esc again resumes");
            yield return Frames(2);

            Assert.IsTrue(s.SkipTutorial());
            Assert.IsTrue(s.BeginDay());
            yield return Frames(10);
            Assert.IsTrue(keys.Press(KeyPress.Escape), "pause mid-day");
            float t = s.ElapsedSeconds;
            yield return Frames(10);
            Assert.AreEqual(t, s.ElapsedSeconds, "the clock is frozen");
            Assert.IsFalse(keys.Press(KeyPress.W) || keys.Press(KeyPress.Space), "elevator keys refused while paused");
            Assert.IsTrue(keys.Press(KeyPress.Escape));
            yield return Frames(5);
            Assert.Greater(s.ElapsedSeconds, t, "resumed");

            DebugModeOn();
            Cheat(DebugKey.N);
            Assert.AreEqual(DayPhase.Summary, s.Phase);
            Assert.IsTrue(keys.Press(KeyPress.Escape), "pause on the summary");
            Assert.IsFalse(s.ContinueFromSummary());
            Assert.IsTrue(keys.Press(KeyPress.Escape));
            Assert.IsTrue(keys.Press(KeyPress.Enter), "Enter: summary -> shop");
            Assert.AreEqual(DayPhase.Shop, s.Phase);
            yield return Frames(2);
            Assert.IsTrue(keys.Press(KeyPress.Escape), "pause in the shop");
            Assert.IsFalse(s.FinishShopping() || s.Reroll(), "shop frozen while paused");
            yield return Frames(3);
            Assert.IsTrue(keys.Press(KeyPress.Escape));
            Assert.IsTrue(keys.Press(KeyPress.Enter), "Enter: shop -> next intro");
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(2, s.DisplayDay);

            Cheat(DebugKey.G, shift: true);
            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.IsFalse(keys.Press(KeyPress.Escape), "no pause on an ending");
            Assert.IsTrue(keys.Press(KeyPress.Enter), "Enter on the ending: play again");
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(0, s.DisplayDay);
            yield return Frames(3);
        }

        // ------------------------------------------------------------------ focus loss

        [UnityTest]
        public IEnumerator FocusLoss_AndWebPause_PauseOnlyARunningDay([Values] From from)
        {
            yield return Into(from);
            var s = S();
            var root = Root();
            bool wasPaused = s.IsPaused;
            var phase = s.Phase;
            int pauses = 0;
            using (GameServices.Events.Subscribe<PauseChanged>(e => { if (e.Paused) pauses++; }))
            {
                root.SendMessage("OnApplicationFocus", false);
                root.SendMessage("OnApplicationPause", true);
                root.WebPause();
                yield return Frames(3);
            }
            Assert.AreEqual(phase, s.Phase, "focus loss never changes the phase");
            bool shouldPause = from == From.Running;
            Assert.AreEqual(shouldPause || wasPaused, s.IsPaused, from + ": paused");
            Assert.AreEqual(shouldPause ? 1 : 0, pauses, from + ": PauseChanged(true) count");
            root.SendMessage("OnApplicationFocus", true);
            yield return Frames(2);
            Assert.AreEqual(shouldPause || wasPaused, s.IsPaused, "focus back: no auto-resume");
            if (s.IsPaused) Assert.IsTrue(s.SetPaused(false));
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ debug cheats

        [UnityTest]
        public IEnumerator DebugJump_ToEveryDay_FromEveryPhase_ThenPlaysOn([Values] From from)
        {
            for (int day = 0; day <= 7; day++)
            {
                yield return Into(from);
                var s = S();
                string ctx = from + " -> day " + day;
                Cheat(DebugKey.Digit0 + day);
                AssertCleanIntro(s, day, ctx);
                yield return Frames(3);
                yield return PlayOn(s, ctx);
            }
        }

        [UnityTest]
        public IEnumerator DebugForcedEnding_FromTheShop_ThenPlayAgain([Values("P", "M", "G", "F")] string letter)
        {
            var key = letter == "P" ? DebugKey.P : letter == "M" ? DebugKey.M : letter == "G" ? DebugKey.G : DebugKey.F;
            yield return Into(From.Shop);
            var s = S();
            Assert.IsTrue(s.IsOpen);
            Assert.IsTrue(s.SetPaused(true), "the cheat works from the pause menu too");
            Cheat(key, shift: true);
            var expected = key == DebugKey.P ? EndingKind.Prosperous : key == DebugKey.M ? EndingKind.Modest : key == DebugKey.G ? EndingKind.Haunted : EndingKind.FourthFloor;
            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.AreEqual(expected, s.Ending);
            Assert.IsFalse(s.IsPaused || s.IsOpen);
            Assert.IsEmpty(s.ActiveCharms, "tomorrow's charms are gone with the run");
            for (int f = 0; f < s.Building.FloorCount; f++) Assert.IsFalse(s.Building.IsRelabelled(new FloorIndex(f)), "floor " + (f + 1));
            yield return Frames(10); // the ending screen

            Assert.IsTrue(Keys().Press(KeyPress.Enter), "Enter on the ending: play again");
            AssertCleanIntro(s, 0, "after " + expected);
            Assert.AreEqual(s.Data.Rules.startLuck, s.Score.Luck);
            Assert.AreEqual(s.Data.Rules.startTips, s.Score.Tips);
            yield return Frames(3);
            Assert.IsTrue(s.SkipTutorial());
            yield return PlayOn(s, "replay after " + expected);
            Assert.IsTrue(s.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, s.Phase, "the next shop opens");
            Assert.AreEqual(s.Data.Rules.maxRerolls, s.RerollsLeft, "fresh rerolls");
            yield return Frames(2);
        }
    }
}
