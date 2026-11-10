using System.Reflection;
using FloorF.Core;
using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.UI
{
    public class UIFormatTests
    {
        [Test]
        public void Numbers_LuckyGold_UnluckyRed_NoStrike()
        {
            var s = UIFormat.Numbers(LuckProfile.Of(new[] { 8, 9 }, new[] { 4 }), hide: false);
            StringAssert.Contains("<color=" + UIFormat.LuckyHex + ">8</color>", s);
            StringAssert.Contains("<color=" + UIFormat.LuckyHex + ">9</color>", s);
            StringAssert.Contains("<color=" + UIFormat.UnluckyHex + ">4</color>", s);
            StringAssert.DoesNotContain("<s>", s, "red alone marks unlucky: no strikethrough");
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
        public void ShortNames_Curated()
        {
            var en = new[] { "Lobby", "Dining", "Gym", "Sing", "Sauna", "Rooms", "Bar", "Casino", "Roof" };
            var ko = new[] { "로비", "식당", "헬스", "노래방", "사우나", "객실", "바", "카지노", "옥상" };
            var ids = new[] { "lobby", "restaurant", "gym", "karaoke", "sauna", "rooms", "bar", "casino", "rooftop" };
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.AreEqual(en[i], UIFormat.DestinationShortName(ids[i], false), ids[i] + " en");
                Assert.AreEqual(ko[i], UIFormat.DestinationShortName(ids[i], true), ids[i] + " ko");
                StringAssert.DoesNotEndWith(".", en[i], "curated, never cut");
            }
            Assert.IsNull(UIFormat.DestinationShortName("observatory", false), "unknown id: no curated name");
            Assert.IsNull(UIFormat.DestinationShortName("", true));
            Assert.AreEqual("Observ.", UIFormat.ShortName("Observatory"), "unknown ids still fall back to truncation");

            // every destination in the shipped data has one
            var hotel = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/hotel");
            Assert.IsNotNull(hotel, "Resources/Data/hotel.json");
            string json = hotel.text;
            int at = json.IndexOf("\"destinations\"", System.StringComparison.Ordinal);
            Assert.Greater(at, 0);
            string list = json.Substring(at, json.IndexOf(']', at) - at);
            var found = System.Text.RegularExpressions.Regex.Matches(list, "\"id\"\\s*:\\s*\"([^\"]+)\"");
            Assert.Greater(found.Count, 5);
            foreach (System.Text.RegularExpressions.Match m in found)
            {
                Assert.IsNotNull(UIFormat.DestinationShortName(m.Groups[1].Value, false), m.Groups[1].Value + " en short name");
                Assert.IsNotNull(UIFormat.DestinationShortName(m.Groups[1].Value, true), m.Groups[1].Value + " ko short name");
            }
        }

        [Test]
        public void AiCreditsLine_MatchesTheDisclosure()
        {
            // Docs/AI_DISCLOSURE.md: code, text, balancing, procedural visuals
            foreach (var word in new[] { "Claude", "Anthropic", "code", "text", "balancing", "procedural visuals" })
                StringAssert.Contains(word, UIStrings.CreditsAi.En, "en AI line: " + word);
            foreach (var word in new[] { "Claude", "Anthropic", "코드", "문구", "밸런싱", "절차적 그래픽" })
                StringAssert.Contains(word, UIStrings.CreditsAi.Ko, "ko AI line: " + word);
        }

        [Test]
        public void StickerHeadline_FromData()
        {
            var four = new StickerDef { id = "four_to_f", from = "4", to = "F" };
            var any = new StickerDef { id = "any_to_8", from = "*", to = "8" };
            Assert.AreEqual("4 → F", UIFormat.StickerHeadline(four, false));
            Assert.AreEqual("any → 8", UIFormat.StickerHeadline(any, false));
            Assert.AreEqual("원하는 층 → 8", UIFormat.StickerHeadline(any, true), "ko: not the awkward '아무 → 8'");
            Assert.AreEqual("4 → F", UIFormat.StickerHeadline(four, true));
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
                    // "{0}" or a Korean particle specifier "{0:은}"
                    var ph = new System.Text.RegularExpressions.Regex(@"\{" + i + @"(:[^}]*)?\}");
                    Assert.AreEqual(ph.IsMatch(l.En), ph.IsMatch(l.Ko), f.Name + " placeholder {" + i + "}");
                }
                foreach (var bad in new[] { "은(는)", "는(은)", "이(가)", "가(이)", "을(를)", "를(을)", "과(와)", "와(과)", "(으)로" })
                    StringAssert.DoesNotContain(bad, l.Ko, f.Name + ": particle placeholder - use {n:은} (KoreanParticles)");
                Assert.AreEqual(l.Ko, l.Get(true));
                Assert.AreEqual(l.En, l.Get(false));
            }
            Assert.Greater(count, 30);
        }

        static DaySummary Day(int delivered, int left, int luckStart, int luckEnd, int unlucky, int bestStreak)
            => new DaySummary(delivered, left, 0, luckStart, luckEnd, 0, unlucky, bestStreak);

        [Test]
        public void DayGrade_Thresholds()
        {
            // perfect day: all delivered (3) + no unlucky (2) + luck +12 (2) + streak 5 (2) = 9
            Assert.AreEqual(9, UIFormat.GradePoints(Day(10, 0, 50, 62, 0, 5)));
            Assert.AreEqual("S", UIFormat.DayGrade(Day(10, 0, 50, 62, 0, 5)));
            // 8 = S: streak 3 instead of 5
            Assert.AreEqual("S", UIFormat.DayGrade(Day(10, 0, 50, 62, 0, 3)));
            // 7 = A: + one unlucky moment
            Assert.AreEqual(7, UIFormat.GradePoints(Day(10, 0, 50, 62, 1, 3)));
            Assert.AreEqual("A", UIFormat.DayGrade(Day(10, 0, 50, 62, 1, 3)));
            // 6 = A: 80% delivered (2) + 1 unlucky (1) + luck +0 (1) + streak 5 (2)
            Assert.AreEqual(6, UIFormat.GradePoints(Day(8, 2, 50, 50, 1, 5)));
            Assert.AreEqual("A", UIFormat.DayGrade(Day(8, 2, 50, 50, 1, 5)));
            // 4 = B: 80% (2) + 3 unlucky (0) + luck +0 (1) + streak 3 (1); one Luck lower = 3 = C
            Assert.AreEqual(4, UIFormat.GradePoints(Day(8, 2, 50, 50, 3, 3)));
            Assert.AreEqual("B", UIFormat.DayGrade(Day(8, 2, 50, 50, 3, 3)));
            Assert.AreEqual("C", UIFormat.DayGrade(Day(8, 2, 50, 49, 3, 3)));
            // C: half delivered, many unlucky, luck down, no streak
            Assert.AreEqual(0, UIFormat.GradePoints(Day(5, 5, 50, 30, 4, 1)));
            Assert.AreEqual("C", UIFormat.DayGrade(Day(5, 5, 50, 30, 4, 1)));
            // nobody delivered is always C (no "0 left, 0 unlucky" free points)
            Assert.AreEqual("C", UIFormat.DayGrade(Day(0, 0, 50, 50, 0, 0)));
            // the 95% edge: 19/20 still gets the top share points
            Assert.AreEqual(3 + 2 + 2 + 2, UIFormat.GradePoints(Day(19, 1, 50, 60, 0, 6)));
        }

        [Test]
        public void Multiplier_UpToTwoDecimals_Invariant()
        {
            Assert.AreEqual("x1.15", UIFormat.Multiplier(1.15f));
            Assert.AreEqual("x1.05", UIFormat.Multiplier(1f + 0.05f));
            Assert.AreEqual("x1.1", UIFormat.Multiplier(1f + 2 * 0.05f));
            Assert.AreEqual("x2.0", UIFormat.Multiplier(2f));
            Assert.AreEqual("x1.0", UIFormat.Multiplier(1f));
        }
    }
}
