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
    /// The debug overlay is installed in the real Main scene (every build) and its cheats stay inert until debug mode is
    /// toggled (typed LUCKY / Ctrl+Shift+D). Input is fed as already-mapped keys through <c>DebugOverlay.Feed</c>, the
    /// same path the Input System keyboard reading takes.
    /// </summary>
    public class DebugModePlayTests
    {
        static readonly DebugKey[] NoKeys = new DebugKey[0];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameplayRoot.SkipTitleForTests = true;
            GameplayRoot.SkipTutorialForTests = true;
            Time.captureDeltaTime = 0f;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            yield return null;
        }

        static DebugOverlay Overlay()
        {
            var overlay = Object.FindFirstObjectByType<DebugOverlay>();
            Assert.IsNotNull(overlay, "DebugOverlay installed on [Gameplay]");
            return overlay;
        }

        static void Press(DebugOverlay o, DebugKey key, bool shift = false, bool ctrl = false)
            => o.Feed(ctrl, shift, false, new[] { key }, null);

        static void Type(DebugOverlay o, string text)
        {
            foreach (var c in text) o.Feed(false, false, false, NoKeys, new List<char> { c });
        }

        [UnityTest]
        public IEnumerator CheatKeys_DoNothing_UntilDebugModeIsToggledOn()
        {
            var o = Overlay();
            var day = GameServices.Get<IDayQuery>();
            var score = GameServices.Get<IScoreQuery>();
            Assert.AreEqual(DayPhase.Intro, day.Phase);
            int startDay = day.DisplayDay, tips = score.Tips, luck = score.Luck;
            int endings = 0, phases = 0;
            using (GameServices.Events.Subscribe<EndingReached>(e => endings++))
            using (GameServices.Events.Subscribe<DayPhaseChanged>(e => phases++))
            {
                Press(o, DebugKey.Digit3);
                Press(o, DebugKey.T, shift: true);
                Press(o, DebugKey.Minus);
                Press(o, DebugKey.G, shift: true);
                Press(o, DebugKey.N);
                yield return null;
                Assert.IsFalse(o.Cheats.Enabled);
                Assert.AreEqual(startDay, day.DisplayDay);
                Assert.AreEqual(tips, score.Tips);
                Assert.AreEqual(luck, score.Luck);
                Assert.AreEqual(0, endings + phases, "debug mode off: no cheat ran");

                Type(o, "LUCKY");
                Assert.IsTrue(o.Cheats.Enabled, "typing LUCKY turns debug mode on");
                Press(o, DebugKey.T, shift: true);
                Assert.AreEqual(tips + DebugCheats.TipsBig, score.Tips);
                Press(o, DebugKey.Digit3);
                yield return null;
                Assert.AreEqual(3, day.DisplayDay);
                Assert.AreEqual(DayPhase.Intro, day.Phase);
                Press(o, DebugKey.G, shift: true);
                yield return null;
                Assert.AreEqual(DayPhase.Ending, day.Phase);
                Assert.AreEqual(EndingKind.Haunted, day.Ending);
                Assert.AreEqual(1, endings);

                Press(o, DebugKey.D, shift: true, ctrl: true);
                Assert.IsFalse(o.Cheats.Enabled, "Ctrl+Shift+D turns it off again");
                Press(o, DebugKey.Digit1);
                yield return null;
                Assert.AreEqual(DayPhase.Ending, day.Phase, "off again: keys inert");
            }
        }
    }
}
