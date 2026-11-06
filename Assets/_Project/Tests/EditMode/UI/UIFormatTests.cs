using System.Reflection;
using FloorF.Core;
using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.UI
{
    public class UIFormatTests
    {
        [Test]
        public void Numbers_LuckyGold_UnluckyRedStruck()
        {
            var s = UIFormat.Numbers(LuckProfile.Of(new[] { 8, 9 }, new[] { 4 }), hide: false);
            StringAssert.Contains("<color=" + UIFormat.LuckyHex + ">8</color>", s);
            StringAssert.Contains("<color=" + UIFormat.LuckyHex + ">9</color>", s);
            StringAssert.Contains("<color=" + UIFormat.UnluckyHex + "><s>4</s></color>", s);
            Assert.Less(s.IndexOf(">8<"), s.IndexOf(">4<"), "lucky first");
        }

        [Test]
        public void Numbers_Hidden_ShowsQuestionMark_Empty_ShowsDash()
        {
            var hidden = UIFormat.Numbers(LuckProfile.Of(new[] { 4 }, new int[0]), hide: true);
            StringAssert.Contains("?", hidden);
            StringAssert.DoesNotContain("4", hidden.Replace(UIFormat.HiddenHex, ""));
            Assert.AreEqual("-", UIFormat.Numbers(LuckProfile.None, false));
        }

        [TestCase(0f, "0:00")]
        [TestCase(-3f, "0:00")]
        [TestCase(9.2f, "0:10")]
        [TestCase(65f, "1:05")]
        [TestCase(120f, "2:00")]
        public void Clock_RoundsUp(float seconds, string expected) => Assert.AreEqual(expected, UIFormat.Clock(seconds));

        [Test]
        public void JudgedCaption_FloorUsesLabel_OccupancyUsesPeople()
        {
            Assert.AreEqual("floor F", UIFormat.JudgedCaption(LuckSource.FloorLabel, -1, "F", false));
            Assert.AreEqual("floor 4", UIFormat.JudgedCaption(LuckSource.FloorLabel, 4, "4", false));
            Assert.AreEqual("3 people", UIFormat.JudgedCaption(LuckSource.Occupancy, 3, "1", false));
            Assert.AreEqual("1 person", UIFormat.JudgedCaption(LuckSource.Occupancy, 1, "1", false));
            Assert.AreEqual("3명", UIFormat.JudgedCaption(LuckSource.Occupancy, 3, "1", true));
            Assert.AreEqual("", UIFormat.JudgedCaption(LuckSource.Patience, -1, "1", false));
            Assert.AreEqual("8", UIFormat.JudgedNumber(8, "88"));
            Assert.AreEqual("F", UIFormat.JudgedNumber(-1, "F"));
        }

        [Test]
        public void Verdict_Words()
        {
            Assert.AreEqual("LUCKY!", UIFormat.Verdict(LuckVerdict.Lucky, false));
            Assert.AreEqual("UNLUCKY!", UIFormat.Verdict(LuckVerdict.Unlucky, false));
            Assert.AreEqual("", UIFormat.Verdict(LuckVerdict.Neutral, false));
        }

        [Test]
        public void OccupancyPreview_UnluckyWins()
        {
            var grandma = LuckProfile.Of(new[] { 3 }, new[] { 4 });
            var tourist = LuckProfile.Of(new[] { 7 }, new int[0]);
            var couple = LuckProfile.Of(new[] { 7 }, new[] { 4, 9 });
            var riders = new[] { grandma, tourist, couple, null };
            Assert.AreEqual(LuckVerdict.Lucky, UIFormat.OccupancyPreview(3, riders, 2));
            Assert.AreEqual(LuckVerdict.Unlucky, UIFormat.OccupancyPreview(4, riders, 2));
            Assert.AreEqual(LuckVerdict.Neutral, UIFormat.OccupancyPreview(5, riders, 3));
            Assert.AreEqual(LuckVerdict.Lucky, UIFormat.OccupancyPreview(7, riders, 2));
            Assert.AreEqual(LuckVerdict.Neutral, UIFormat.OccupancyPreview(3, riders, 0));
        }

        [Test]
        public void ShortName_Truncates()
        {
            Assert.AreEqual("Sauna", UIFormat.ShortName("Sauna"));
            Assert.AreEqual("Restau.", UIFormat.ShortName("Restaurant"));
            Assert.AreEqual("?", UIFormat.ShortName(""));
        }

        [Test]
        public void StickerHeadline_FromData()
        {
            var four = new StickerDef { id = "four_to_f", from = "4", to = "F" };
            var any = new StickerDef { id = "any_to_8", from = "*", to = "8" };
            Assert.AreEqual("4 → F", UIFormat.StickerHeadline(four, false));
            Assert.AreEqual("any → 8", UIFormat.StickerHeadline(any, false));
        }

        [Test]
        public void ApplyResult_Messages()
        {
            StringAssert.Contains("F", UIFormat.ApplyResult(StickerApplyResult.Applied, "4", "F", false));
            Assert.AreEqual(UIStrings.ResultNotEnough.En, UIFormat.ApplyResult(StickerApplyResult.NotEnoughTips, "4", "4", false));
            Assert.AreEqual(UIStrings.ResultNotApplicable.En, UIFormat.ApplyResult(StickerApplyResult.NotApplicable, "1", "1", false));
        }

        [Test]
        public void Endings_HaveDistinctTitles()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var k in new[] { EndingKind.Prosperous, EndingKind.Modest, EndingKind.Haunted, EndingKind.FourthFloor })
            {
                UIFormat.Ending(k, false, out var title, out var text);
                Assert.IsNotEmpty(title, k.ToString());
                Assert.IsNotEmpty(text, k.ToString());
                Assert.IsTrue(seen.Add(title), "duplicate title " + title);
            }
        }

        [Test]
        public void FourthFloorEnding_SaysTheStickerHidTheRealFloor()
        {
            UIFormat.Ending(EndingKind.FourthFloor, false, out _, out var en);
            UIFormat.Ending(EndingKind.FourthFloor, true, out _, out var ko);
            StringAssert.Contains("covered the 4", en);
            StringAssert.Contains("still there", en);
            StringAssert.Contains("4를 가렸지만", ko);
        }

        [TestCase(10f, 12f)]
        [TestCase(12f, 12f)]
        [TestCase(16f, 12f)]
        [TestCase(18f, 24f)]
        [TestCase(24f, 24f)]
        [TestCase(30f, 36f)]
        [TestCase(34f, 36f)]
        [TestCase(52f, 48f)]
        [TestCase(70f, 72f)]
        public void FontSizes_SnapToGalmuriGrid(float requested, float expected) => Assert.AreEqual(expected, UIFonts.Px(requested));

        [Test]
        public void LedGhost_IsOpaqueAndDim()
        {
            float lit = UIStyle.Led.r * 0.299f + UIStyle.Led.g * 0.587f + UIStyle.Led.b * 0.114f;
            float ghost = UIStyle.LedGhost.r * 0.299f + UIStyle.LedGhost.g * 0.587f + UIStyle.LedGhost.b * 0.114f;
            Assert.AreEqual(1f, UIStyle.LedGhost.a, "opaque - low alpha blends too bright in linear space");
            Assert.That(ghost / lit, Is.InRange(0.11f, 0.16f), "unlit segments ~12-15% of lit");
        }

        [Test]
        public void UIStrings_EveryEntryHasEnglishAndKorean_AndSamePlaceholders()
        {
            int count = 0;
            foreach (var f in typeof(UIStrings).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(Loc)) continue;
                var l = (Loc)f.GetValue(null);
                count++;
                Assert.IsNotEmpty(l.En, f.Name + " en");
                Assert.IsNotEmpty(l.Ko, f.Name + " ko");
                for (int i = 0; i < 3; i++)
                {
                    string ph = "{" + i + "}";
                    Assert.AreEqual(l.En.Contains(ph), l.Ko.Contains(ph), f.Name + " placeholder " + ph);
                }
                Assert.AreEqual(l.Ko, l.Get(true));
                Assert.AreEqual(l.En, l.Get(false));
            }
            Assert.Greater(count, 30);
        }
    }
}
