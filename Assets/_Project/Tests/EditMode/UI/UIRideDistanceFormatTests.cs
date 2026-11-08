using FloorF.Core;
using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>Rule 3 (ride distance), the 666 curse and Friday the 13th: pure formatting / verdict helpers.</summary>
    public class UIRideDistanceFormatTests
    {
        static readonly LuckProfile Tourist = LuckProfile.Of(new[] { 7 }, new[] { 13 });
        static readonly LuckProfile Italian = LuckProfile.Of(new[] { 13 }, new[] { 17 });

        [TestCase(0, "↕0")]
        [TestCase(5, "↕5")]
        [TestCase(13, "↕13")]
        [TestCase(17, "↕17")]
        [TestCase(-2, "↕0")]
        public void Distance_Odometer(int floors, string expected) => Assert.AreEqual(expected, UIFormat.Distance(floors));

        [Test]
        public void DistanceVerdict_OwnNumbers()
        {
            Assert.AreEqual(LuckVerdict.Lucky, UIFormat.DistanceVerdict(Tourist, false, 7, false));
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.DistanceVerdict(Tourist, false, 13, false));
            Assert.AreEqual(LuckVerdict.Neutral, UIFormat.DistanceVerdict(Tourist, false, 6, false));
            Assert.AreEqual(LuckVerdict.Lucky, UIFormat.DistanceVerdict(Italian, false, 13, false), "the Italian's 13 is lucky");
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.DistanceVerdict(Italian, false, 17, false));
            Assert.AreEqual(LuckVerdict.Neutral, UIFormat.DistanceVerdict(Tourist, false, 0, false), "nothing travelled yet");
        }

        [Test]
        public void DistanceVerdict_Friday13_ThirteenUnluckyForEveryone()
        {
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.DistanceVerdict(Italian, false, 13, true), "Friday beats their own lucky 13");
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.DistanceVerdict(LuckProfile.None, false, 13, true), "even without numbers");
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.DistanceVerdict(Tourist, true, 13, true), "even a mystery guest");
            Assert.AreEqual(LuckVerdict.Lucky, UIFormat.DistanceVerdict(Tourist, false, 7, true), "other numbers unchanged");
        }

        [Test]
        public void DistanceVerdict_MysteryGuest_Neutral()
        {
            Assert.AreEqual(LuckVerdict.Neutral, UIFormat.DistanceVerdict(LuckProfile.Of(new[] { 4 }, new int[0]), true, 4, false));
        }

        [Test]
        public void RideDistance_PopupNumberAndCaption()
        {
            Assert.AreEqual("↕13", UIFormat.JudgedNumber(LuckSource.RideDistance, 13, "3"));
            Assert.AreEqual("4", UIFormat.JudgedNumber(LuckSource.FloorLabel, 4, "4"), "other sources unchanged");
            Assert.AreEqual("F", UIFormat.JudgedNumber(LuckSource.FloorLabel, -1, "F"));
            Assert.AreEqual("travelled 13 floors", UIFormat.JudgedCaption(LuckSource.RideDistance, 13, "3", false));
            Assert.AreEqual("travelled 1 floor", UIFormat.JudgedCaption(LuckSource.RideDistance, 1, "3", false));
            Assert.AreEqual("13층 이동", UIFormat.JudgedCaption(LuckSource.RideDistance, 13, "3", true));
        }

        [Test]
        public void Curse_OnlyUnluckySixFromDevout()
        {
            Assert.IsTrue(UIFormat.IsCurse(LuckVerdict.Unlucky, 6, devout: true));
            Assert.IsFalse(UIFormat.IsCurse(LuckVerdict.Unlucky, 6, devout: false));
            Assert.IsFalse(UIFormat.IsCurse(LuckVerdict.Lucky, 6, devout: true));
            Assert.IsFalse(UIFormat.IsCurse(LuckVerdict.Unlucky, 13, devout: true));
        }

        [Test]
        public void Friday13Strings_CarryTheMultiplier()
        {
            string m = UIFormat.Multiplier(1.5f);
            Assert.AreEqual("x1.5", m);
            Assert.AreEqual("FRIDAY 13TH · bad luck x1.5", string.Format(UIStrings.Friday13Badge.En, m));
            Assert.AreEqual("13일의 금요일 · 불운 x1.5", string.Format(UIStrings.Friday13Badge.Ko, m));
            StringAssert.Contains("1.5x", KoreanParticles.Format(UIStrings.Friday13Intro.En, "1.5"));
            StringAssert.Contains("1.5배", KoreanParticles.Format(UIStrings.Friday13Intro.Ko, "1.5"));
            StringAssert.Contains("13", UIStrings.RuleFriday13.Ko);
            StringAssert.Contains("움직인 층수", UIStrings.RuleDistance.Ko);
        }
    }
}
