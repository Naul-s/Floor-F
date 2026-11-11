using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Tests.EditMode.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Gameplay
{
    /// <summary>DebugCheats: the debug-mode gate (Ctrl+Shift+D / typed LUCKY) and the key map, on a fixture session.</summary>
    public class DebugCheatsTests
    {
        static readonly DebugKey[] None = new DebugKey[0];

        static Sim RunningSim()
        {
            var sim = new Sim(SimFixture.Create(d => d.Hotel.rules.startTips = 500));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        static void Press(DebugCheats c, DebugKey key, bool shift = false, bool ctrl = false, bool alt = false)
            => c.Handle(ctrl, shift, alt, new[] { key }, null);

        static void Type(DebugCheats c, string text)
        {
            foreach (var ch in text) c.Handle(false, false, false, None, new List<char> { ch });
        }

        static DebugCheats Enabled(Sim sim, bool overlayWithout = false)
        {
            var c = new DebugCheats(sim.S, overlayWithout);
            Press(c, DebugKey.D, shift: true, ctrl: true);
            Assert.IsTrue(c.Enabled);
            return c;
        }

        [Test]
        public void DebugMode_IsOffByDefault_AndEveryCheatKeyDoesNothing()
        {
            var sim = RunningSim();
            var c = new DebugCheats(sim.S, false);
            sim.Log.Clear();
            foreach (DebugKey key in System.Enum.GetValues(typeof(DebugKey)))
            {
                if (key == DebugKey.D) continue;
                Press(c, key);
                Press(c, key, shift: true);
            }
            Assert.IsFalse(c.Enabled);
            Assert.IsFalse(c.OverlayVisible, "release: F1 needs debug mode");
            Assert.IsEmpty(sim.Log, "no cheat ran");
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
            Assert.AreEqual(500, sim.S.Score.Tips);
        }

        [Test]
        public void CtrlShiftD_TogglesDebugMode_OtherChordsDoNot()
        {
            var sim = RunningSim();
            var c = new DebugCheats(sim.S, false);
            Press(c, DebugKey.D);
            Press(c, DebugKey.D, ctrl: true);
            Press(c, DebugKey.D, shift: true);
            Press(c, DebugKey.D, shift: true, ctrl: true, alt: true);
            Assert.IsFalse(c.Enabled);
            Press(c, DebugKey.D, shift: true, ctrl: true);
            Assert.IsTrue(c.Enabled);
            Assert.AreEqual("debug mode ON", c.LastAction);
            Press(c, DebugKey.D, shift: true, ctrl: true);
            Assert.IsFalse(c.Enabled);
        }

        [Test]
        public void TypingLucky_TogglesDebugMode_OnlyAsAnUnbrokenWord()
        {
            var sim = RunningSim();
            var c = new DebugCheats(sim.S, false);
            Type(c, "LUCXKY");
            Assert.IsFalse(c.Enabled, "interrupted");
            Type(c, "lucky");
            Assert.IsTrue(c.Enabled, "case-insensitive");
            Type(c, "LLUCKY");
            Assert.IsFalse(c.Enabled, "a repeated first letter still matches");
        }

        [Test]
        public void TogglingOff_HidesTheOverlay_InReleaseBuilds()
        {
            var sim = RunningSim();
            var c = Enabled(sim);
            Press(c, DebugKey.F1);
            Assert.IsTrue(c.OverlayVisible);
            Press(c, DebugKey.D, shift: true, ctrl: true);
            Assert.IsFalse(c.OverlayVisible);
            Press(c, DebugKey.F1);
            Assert.IsFalse(c.OverlayVisible);

            var dev = new DebugCheats(sim.S, true);
            Press(dev, DebugKey.F1);
            Assert.IsTrue(dev.OverlayVisible, "Editor/dev builds: F1 works without debug mode");
        }

        [Test]
        public void Digits_JumpToThatDay()
        {
            var sim = RunningSim();
            var c = Enabled(sim);
            Press(c, DebugKey.Digit2);
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(2, sim.S.DisplayDay);
            Assert.AreEqual("jump to day 2", c.LastAction);
            Press(c, DebugKey.Digit7);
            Assert.AreEqual(2, sim.S.DisplayDay, "no day 7 in the fixture: ignored");
        }

        /// <summary>Days 1-10 (no tutorial: display day = day number), 8-10 winter; day 7 schedules the mystery guest.</summary>
        static Sim TenDays()
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                var days = new DayDef[10];
                for (int i = 0; i < 10; i++)
                    days[i] = new DayDef { day = i + 1, durationSeconds = 1000f, extended = i >= 7, shopStickers = new string[0] };
                days[6].spawns = new[] { new SpawnDef { time = 1f, guest = "mystery", floor = 1, destinationFloor = 4 } };
                d.Days.days = days;
            }));
            sim.S.StartNewGame();
            return sim;
        }

        [Test]
        public void Digits8And9_AndShift0_JumpToTheWinterDays()
        {
            var sim = TenDays();
            var c = Enabled(sim);
            Press(c, DebugKey.Digit8);
            Assert.AreEqual(8, sim.S.DisplayDay);
            Assert.IsTrue(sim.S.IsSeasonExtended);
            Press(c, DebugKey.Digit9);
            Assert.AreEqual(9, sim.S.DisplayDay);
            Press(c, DebugKey.Digit0, shift: true);
            Assert.AreEqual(10, sim.S.DisplayDay);
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual("jump to day 10", c.LastAction);
            Press(c, DebugKey.Digit5, shift: true);
            Assert.AreEqual(10, sim.S.DisplayDay, "other Shift+digits do nothing");
            Press(c, DebugKey.Digit0);
            Assert.AreEqual(10, sim.S.DisplayDay, "no day 0 in this fixture: ignored");
            StringAssert.Contains("Shift+0", DebugCheats.HelpText);
            StringAssert.Contains("Shift+X", DebugCheats.HelpText);
        }

        [Test]
        public void ShiftX_InTheIntro_TogglesMysteryTonight()
        {
            var sim = TenDays();
            var c = Enabled(sim);
            Press(c, DebugKey.Digit7);
            Assert.IsFalse(sim.S.MysteryTonight, "fresh record: not on the first run");
            Press(c, DebugKey.X, shift: true);
            Assert.IsTrue(sim.S.MysteryTonight);
            Assert.AreEqual("mystery guest tonight ON", c.LastAction);
            Press(c, DebugKey.X, shift: true);
            Assert.IsFalse(sim.S.MysteryTonight);
            Press(c, DebugKey.X, shift: true);
            Assert.IsTrue(sim.S.BeginDay());
            sim.Run(2f);
            Assert.AreEqual(1, sim.Events<GuestSpawned>().Count, "toggled on: the mystery guest comes");
            Press(c, DebugKey.X, shift: true);
            Assert.IsTrue(sim.S.MysteryTonight, "Shift+X does nothing while the day runs");
        }

        [Test]
        public void ShiftLetters_ForceEndings_PlainLettersDoNot()
        {
            var cases = new[]
            {
                (DebugKey.P, EndingKind.Prosperous), (DebugKey.M, EndingKind.Modest),
                (DebugKey.G, EndingKind.Haunted), (DebugKey.F, EndingKind.FourthFloor),
            };
            foreach (var (key, kind) in cases)
            {
                var sim = RunningSim();
                var c = Enabled(sim);
                Press(c, key);
                Assert.AreEqual(DayPhase.Running, sim.S.Phase, key + " without Shift");
                Press(c, key, shift: true);
                Assert.AreEqual(DayPhase.Ending, sim.S.Phase, key.ToString());
                Assert.AreEqual(kind, sim.S.Ending);
                Assert.AreEqual(1, sim.Events<EndingReached>().Count);
            }
        }

        [Test]
        public void TipsAndLuckKeys()
        {
            var sim = RunningSim();
            var c = Enabled(sim);
            Press(c, DebugKey.T);
            Assert.AreEqual(600, sim.S.Score.Tips);
            Press(c, DebugKey.T, shift: true);
            Assert.AreEqual(1600, sim.S.Score.Tips);
            Press(c, DebugKey.Plus);
            Assert.AreEqual(60, sim.S.Score.Luck);
            Press(c, DebugKey.Minus);
            Press(c, DebugKey.Minus);
            Assert.AreEqual(40, sim.S.Score.Luck);
            Assert.AreEqual("luck -10 = 40", c.LastAction);
            for (int i = 0; i < 4; i++) Press(c, DebugKey.Minus);
            Assert.AreEqual(0, sim.S.Score.Luck);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending, "luck to 0 mid-day haunts as in play");
        }

        [Test]
        public void ExtraKeys_EndDay_Mystery_Rerolls()
        {
            var sim = RunningSim();
            var c = Enabled(sim);
            Press(c, DebugKey.X);
            Assert.AreEqual(1, sim.Events<GuestSpawned>().Count);
            Press(c, DebugKey.N);
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.IsTrue(sim.S.Reroll());
            int left = sim.S.RerollsLeft;
            Press(c, DebugKey.R);
            Assert.AreEqual(left, sim.S.RerollsLeft, "plain R is the overlay's reroll, not a cheat");
            Press(c, DebugKey.R, shift: true);
            Assert.AreEqual(left + 1, sim.S.RerollsLeft);
        }

        [Test]
        public void CtrlOrAlt_Held_IgnoresCheats()
        {
            var sim = RunningSim();
            var c = Enabled(sim);
            sim.Log.Clear();
            Press(c, DebugKey.Digit1, ctrl: true);
            Press(c, DebugKey.T, alt: true);
            Press(c, DebugKey.G, shift: true, ctrl: true);
            Assert.IsEmpty(sim.Log);
        }
    }
}
