using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>Phase 3b: destination icons, Korean particles by number reading, credit wording.</summary>
    public class UIIconAndParticleTests
    {
        [SetUp]
        public void SetUp() => UIIcons.ResetStatics();

        [TearDown]
        public void TearDown() => UIIcons.ResetStatics();

        [Test]
        public void Icons_LoadForAllDestinations()
        {
            var data = GameDataResources.Load();
            Assert.GreaterOrEqual(data.Destinations.Count, 9, "hotel.json destinations");
            foreach (var d in data.Destinations)
            {
                var s = UIIcons.For(d);
                Assert.IsNotNull(s, "Resources/" + UIIcons.ResourceFolder + d.IconKey + " (" + d.id + ")");
                Assert.GreaterOrEqual(s.rect.width, 32f, d.id + " icon size");
                Assert.AreEqual(s.rect.width, s.rect.height, 1f, d.id + " square");
            }
        }

        [Test]
        public void Icons_Missing_ReturnsNull_WarnsOnce()
        {
            UIIcons.ForceMissing.Add("sauna");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("icon 'Icons/sauna' not found"));
            Assert.IsNull(UIIcons.For("sauna"));
            Assert.IsNull(UIIcons.For("sauna"), "second miss: no second warning (LogAssert fails on unexpected logs)");
            Assert.IsNotNull(UIIcons.For("gym"), "other icons still load");
        }

        // 0 영, 1 일, 2 이, 3 삼, 4 사, 5 오, 6 육, 7 칠, 8 팔, 9 구; 10 십, 100 백, 1000 천
        [TestCase("0", "은", "이", "을", "으로")]
        [TestCase("1", "은", "이", "을", "로")]
        [TestCase("2", "는", "가", "를", "로")]
        [TestCase("3", "은", "이", "을", "으로")]
        [TestCase("4", "는", "가", "를", "로")]
        [TestCase("5", "는", "가", "를", "로")]
        [TestCase("6", "은", "이", "을", "으로")]
        [TestCase("7", "은", "이", "을", "로")]
        [TestCase("8", "은", "이", "을", "로")]
        [TestCase("9", "는", "가", "를", "로")]
        [TestCase("10", "은", "이", "을", "으로")]
        [TestCase("12", "는", "가", "를", "로")]
        [TestCase("13", "은", "이", "을", "으로")]
        [TestCase("20", "은", "이", "을", "으로")]
        [TestCase("100", "은", "이", "을", "으로")]
        [TestCase("1000", "은", "이", "을", "으로")]
        [TestCase("88", "은", "이", "을", "로")]
        [TestCase("F", "는", "가", "를", "로")]     // 에프
        [TestCase("L", "은", "이", "을", "로")]     // 엘
        [TestCase("M", "은", "이", "을", "으로")]   // 엠
        [TestCase("사우나", "는", "가", "를", "로")]
        [TestCase("식당", "은", "이", "을", "으로")]
        [TestCase("호텔", "은", "이", "을", "로")]
        public void KoreanParticles_ByNumberReading(string word, string topic, string subject, string obj, string direction)
        {
            Assert.AreEqual(word + topic, KoreanParticles.Attach(word, "은"), word + " 은/는");
            Assert.AreEqual(word + topic, KoreanParticles.Attach(word, "는"), word + " (either spelling)");
            Assert.AreEqual(word + subject, KoreanParticles.Attach(word, "이"), word + " 이/가");
            Assert.AreEqual(word + obj, KoreanParticles.Attach(word, "를"), word + " 을/를");
            Assert.AreEqual(word + direction, KoreanParticles.Attach(word, "로"), word + " (으)로");
        }

        [Test]
        public void KoreanParticles_FormatSpecifiers_InTemplates()
        {
            Assert.AreEqual("행운! 3은 그 손님", KoreanParticles.Format("행운! {0:은} 그 손님", 3));
            Assert.AreEqual("행운! 2는 그 손님", KoreanParticles.Format("행운! {0:은} 그 손님", "2"));
            Assert.AreEqual("LUCKY! 3 is", KoreanParticles.Format("LUCKY! {0} is", 3), "plain placeholders untouched");
            string ko = KoreanParticles.Format(UIStrings.TutWhyUnluckyFloor.Ko, 4);
            StringAssert.StartsWith("불길! 4는 ", ko);
            StringAssert.DoesNotContain("(는)", ko);
            StringAssert.StartsWith("행운! 3은 ", KoreanParticles.Format(UIStrings.TutWhyLuckyFloor.Ko, 3));
            StringAssert.StartsWith("행운! 3명은 ", KoreanParticles.Format(UIStrings.TutWhyLuckyCount.Ko, 3));
        }

        [Test]
        public void DayLabel_TrainingShiftIsDayZero()
        {
            Assert.AreEqual("DAY 0", UIFormat.DayLabel(0, 5, true, false), "the training shift does not count toward /5");
            Assert.AreEqual("0일차", UIFormat.DayLabel(0, 5, true, true));
            Assert.AreEqual("DAY 2/5", UIFormat.DayLabel(2, 5, false, false));
            Assert.AreEqual("2/5일차", UIFormat.DayLabel(2, 5, false, true));
        }

        [Test]
        public void TrainingStrings_HaveBothLanguages()
        {
            foreach (var l in new[] { UIStrings.Practice, UIStrings.SkipTutorial, UIStrings.TrainingDone, UIStrings.PracticeReset, UIStrings.TutRules,
                         UIStrings.TutChips, UIStrings.TutWaitAllLucky, UIStrings.TutFloorTwo, UIStrings.TutOpenAll, UIStrings.TutWhySticker })
            {
                Assert.IsFalse(string.IsNullOrEmpty(l.En));
                Assert.IsFalse(string.IsNullOrEmpty(l.Ko), l.En);
            }
            StringAssert.Contains("연습", UIStrings.Practice.Ko);
            StringAssert.Contains("튜토리얼 건너뛰기", UIStrings.SkipTutorial.Ko);
            StringAssert.Contains("수습 근무 끝", UIStrings.TrainingDone.Ko);
            string rules = string.Format(UIStrings.TutRules.En, 5, 70);
            StringAssert.Contains("Haunted", rules);
            StringAssert.Contains("Day 5", rules);
            StringAssert.Contains("70", rules);
            StringAssert.Contains("leave", rules);
        }

        [Test]
        public void ApplyResult_NamesTheVenue()
        {
            Assert.AreEqual("Floor 4 (Karaoke) is now F!", UIFormat.ApplyResult(StickerApplyResult.Applied, "4", "F", false, "Karaoke"));
            Assert.AreEqual("4층(노래방)이 이제 F층!", UIFormat.ApplyResult(StickerApplyResult.Applied, "4", "F", true, "노래방"));
            Assert.AreEqual("Floor 4 is now F!", UIFormat.ApplyResult(StickerApplyResult.Applied, "4", "F", false));
        }

        [Test]
        public void CreditStrings_HaveRequiredWording()
        {
            const string ccBy = "\"Local Forecast - Elevator\" Kevin MacLeod (incompetech.com)\n" +
                                "Licensed under Creative Commons: By Attribution 4.0\n" +
                                "https://creativecommons.org/licenses/by/4.0/";
            Assert.AreEqual(ccBy, UIStrings.CreditsMusicCcBy, "CC BY 4.0 attribution, word for word");
            foreach (bool ko in new[] { false, true })
            {
                string all = UIStrings.CreditsSound.Get(ko) + UIStrings.CreditsArt.Get(ko) + UIStrings.CreditsAi.Get(ko);
                foreach (var who in new[] { "LilMati", "PagDev", "LaurenPonder", "egomassive", "danlucaz", "BlondPanda", "Freesound",
                             "Kenney", "CC0", "Mulmaru", "mushsooni", "Galmuri11", "Galmuri11 Bold", "quiple", "SIL Open Font License 1.1",
                             "Material Symbols", "Apache License 2.0", "Claude", "Anthropic" })
                    StringAssert.Contains(who, all, (ko ? "ko" : "en") + ": " + who);
            }
        }
    }
}
