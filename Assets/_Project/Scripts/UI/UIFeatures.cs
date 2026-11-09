using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// Presentation features that change the flow a test sees (title attract screen, establishing shots, the rule box
    /// collapsing from Day 2). All on in the game. The PlayMode test assembly switches them off for every test
    /// (<c>UIFeatureDefaultsForTests</c> SetUpFixture) and a test that is about one of them switches it back on.
    /// </summary>
    static class UIFeatures
    {
        /// <summary>Title: "press any key" attract state (logo + prompt only) before the menu.</summary>
        internal static bool AttractScreen = true;
        /// <summary>Each real day's Intro: ~2.5 s of city with a caption before the intro card.</summary>
        internal static bool EstablishingShots = true;
        /// <summary>"HOW LUCK WORKS" starts collapsed from Day 2 on (until the player toggles it).</summary>
        internal static bool RulesAutoCollapse = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => SetAll(true);

        internal static void SetAll(bool on)
        {
            AttractScreen = on;
            EstablishingShots = on;
            RulesAutoCollapse = on;
        }
    }
}
