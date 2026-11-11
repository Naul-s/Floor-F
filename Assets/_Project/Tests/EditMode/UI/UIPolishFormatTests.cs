using System.Linq;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>Intro "TODAY:" lines (DayTwist), the summary hint (SummaryHint), the run journey and the best record.</summary>
    public class UIPolishFormatTests
    {
        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "");

        DayDef First(System.Func<DayDef, bool> pick) => data.Days.First(d => pick(d));
        bool Has(DayDef d, string guest) => d.spawns.Any(s => s.guest == guest);

        // ------------------------------------------------------------------ TODAY lines

        [Test]
        public void EveryDay_HasAShortTodayLine_InBothLanguages()
        {
            foreach (var d in data.Days)
                foreach (bool ko in new[] { false, true })
                {
                    string line = DayTwist.Line(d, data, ko);
                    Assert.IsNotNull(line, "day " + d.day + " has a TODAY line (" + (ko ? "ko" : "en") + ")");
                    string plain = Plain(line);
                    StringAssert.StartsWith(ko ? "오늘: " : "TODAY: ", plain);
                    StringAssert.DoesNotContain("{", plain, "no placeholder left");
                    Assert.LessOrEqual(plain.Length, 110, "day " + d.day + ": one or two lines on the card: " + plain);
                }
        }

        [Test]
        public void TodayKinds_FollowTheData()
        {
            Assert.AreEqual(DayTwist.Kind.Training, DayTwist.Resolve(First(d => d.tutorial), data));
            var real = data.Days.Where(d => !d.tutorial).ToArray();
            Assert.AreEqual(DayTwist.Kind.Basics, DayTwist.Resolve(real[0], data), "first real day: rules 1 + 2");
            Assert.AreEqual(DayTwist.Kind.Ride, DayTwist.Resolve(real[1], data), "second real day: rule 3");
            Assert.AreEqual(DayTwist.Kind.Couple, DayTwist.Resolve(First(d => Has(d, "western_priest")), data), "the priest's first day");
            Assert.AreEqual(DayTwist.Kind.Gambler, DayTwist.Resolve(First(d => Has(d, "gambler")), data));
            Assert.AreEqual(DayTwist.Kind.Friday, DayTwist.Resolve(First(d => d.friday13), data));
            Assert.AreEqual(DayTwist.Kind.Wedding, DayTwist.Resolve(First(d => Has(d, "chinese_wedding_party") && !d.starNight), data));
            Assert.AreEqual(DayTwist.Kind.Star, DayTwist.Resolve(First(d => d.starNight), data));
            // the winter season (extended days): a new guest, the lottery, a number lucky for all
            Assert.AreEqual(DayTwist.Kind.NewGuest, DayTwist.Resolve(First(d => d.extended && !d.lottery && d.luckyForAll == 0), data), "Singles Day: the solo traveller");
            Assert.AreEqual(DayTwist.Kind.Lottery, DayTwist.Resolve(First(d => d.lottery), data));
            Assert.AreEqual(DayTwist.Kind.LuckyForAll, DayTwist.Resolve(First(d => d.luckyForAll > 0), data));
            // every real day says something different
            var kinds = real.Select(d => DayTwist.Resolve(d, data)).ToArray();
            Assert.AreEqual(kinds.Length, kinds.Distinct().Count(), string.Join(",", kinds));
        }

        [Test]
        public void WinterTodayLines_StateTheRealNumbers()
        {
            var solo = data.GetGuest(new GuestArchetypeId("solo_traveler"));
            var singles = First(d => d.extended && !d.lottery && d.luckyForAll == 0);
            Assert.AreSame(solo, DayTwist.NewGuest(singles, data), "the solo traveller checks in for the first time");
            string en = Plain(DayTwist.Line(singles, data, false));
            StringAssert.Contains("Solo Traveller loves 1 and 11 and sulks at 2", en);
            StringAssert.Contains("혼행족 - 1, 11을 좋아하고 2를 싫어해요", Plain(DayTwist.Line(singles, data, true)));

            var lottery = First(d => d.lottery);
            string l = Plain(DayTwist.Line(lottery, data, false));
            StringAssert.Contains("drawn " + lottery.lotteryDraws + "x", l);
            StringAssert.Contains("tips x" + UIFormat.Factor(data.Rules.lotteryTipMultiplier), l);
            StringAssert.Contains("Luck +" + data.Rules.lotteryLuckBonus, l);
            Assert.AreEqual("2", UIFormat.Factor(2f));
            Assert.AreEqual("1.5", UIFormat.Factor(1.5f));
            StringAssert.Contains(lottery.lotteryDraws + "번 뽑아요", Plain(DayTwist.Line(lottery, data, true)));

            var xmas = First(d => d.christmas);
            Assert.AreEqual(12, xmas.luckyForAll);
            StringAssert.Contains("12 is LUCKY for everyone", Plain(DayTwist.Line(xmas, data, false)));
            StringAssert.Contains("12는 누구에게나 행운", Plain(DayTwist.Line(xmas, data, true)));
            // the mystery guest never gives itself away on a TODAY line
            foreach (var d in data.Days) Assert.AreNotEqual("mystery_guest", DayTwist.NewGuest(d, data)?.id);
        }

        [Test]
        public void TodayLines_StateTheRealNumbers()
        {
            var couple = data.GetGuest(new GuestArchetypeId("japanese_couple"));
            var priest = data.GetGuest(new GuestArchetypeId("western_priest"));
            Assert.IsTrue(priest.HasTrait(GuestTraits.Devout));
            CollectionAssert.Contains(priest.unlucky, StoryRules.DevoutNumber);
            Assert.AreEqual(2, StoryRules.DevoutPenaltyFactor, "the line says DOUBLE");
            var coupleDay = First(d => Has(d, "western_priest"));
            Assert.IsTrue(Has(coupleDay, couple.id), "the couple rides on the priest's day");
            string c = Plain(DayTwist.Line(coupleDay, data, false));
            StringAssert.Contains(couple.partySize + " riders", c);
            StringAssert.Contains("unlucky " + StoryRules.DevoutNumber + " costs DOUBLE", c);
            StringAssert.Contains("6은 운이 두 배", Plain(DayTwist.Line(coupleDay, data, true)));

            var gambler = data.GetGuest(new GuestArchetypeId("gambler"));
            Assert.IsTrue(gambler.HasTrait(GuestTraits.Gambler));
            StringAssert.Contains("exactly " + gambler.lucky[0] + " riders", Plain(DayTwist.Line(First(d => Has(d, "gambler")), data, false)));
            StringAssert.Contains("딱 " + gambler.lucky[0] + "명", Plain(DayTwist.Line(First(d => Has(d, "gambler")), data, true)));

            var friday = First(d => d.friday13);
            string f = Plain(DayTwist.Line(friday, data, false));
            StringAssert.Contains(UIFormat.Multiplier(data.Rules.friday13PenaltyMultiplier).Substring(1) + "x Luck", f);
            StringAssert.Contains(StoryRules.FridayDistance + " floors", f);
            StringAssert.Contains("1.5배", Plain(DayTwist.Line(friday, data, true)));

            var star = First(d => d.starNight);
            string s = Plain(DayTwist.Line(star, data, false));
            StringAssert.StartsWith("TODAY: " + StoryRules.StarNightNumber + " is LUCKY", s);
            StringAssert.Contains("+" + data.Rules.wishLuckBonus + " Luck", s);
            StringAssert.Contains("7이 누구에게나", Plain(DayTwist.Line(star, data, true)));

            var wedding = First(d => Has(d, "chinese_wedding_party") && !d.starNight);
            string w = Plain(DayTwist.Line(wedding, data, false));
            StringAssert.Contains("love 6 and 8", w);
            StringAssert.Contains("odd 3, 5 and 7", w);
            Assert.AreEqual(2, data.GetGuest(new GuestArchetypeId("chinese_wedding_party")).partySize, "'in pairs'");
            StringAssert.Contains("6, 8을", Plain(DayTwist.Line(wedding, data, true)));
        }

        [Test]
        public void TodayLine_Colours_MatchTheCard()
        {
            Assert.AreEqual(UIFormat.UnluckyHex, DayTwist.Hex(DayTwist.Kind.Friday));
            Assert.AreEqual(UIFormat.LuckyHex, DayTwist.Hex(DayTwist.Kind.Basics));
            StringAssert.StartsWith("<color=" + UIFormat.UnluckyHex + ">", DayTwist.Line(First(d => d.friday13), data, false));
            Assert.IsNull(DayTwist.Line(null, data, false));
        }

        // ------------------------------------------------------------------ summary hint

        static DaySummary S(int delivered, int left, int luckDelta, int lucky, int unlucky, int streak) =>
            new DaySummary(delivered, left, delivered * 10, 50, 50 + luckDelta, lucky, unlucky, streak);

        [Test]
        public void SummaryHint_ExplainsTheWeakestPart()
        {
            Assert.AreEqual(SummaryHint.Kind.NoneDelivered, SummaryHint.Pick(S(0, 0, 0, 0, 0, 0)));
            // many left (share 50%): 3 points lost there
            Assert.AreEqual(SummaryHint.Kind.Left, SummaryHint.Pick(S(3, 3, 12, 5, 0, 5)));
            Assert.AreEqual("3 guests gave up waiting - serve the queues faster.", SummaryHint.Text(S(3, 3, 12, 5, 0, 5), false));
            Assert.AreEqual("손님 3명이 기다리다 떠났어요 - 줄 선 손님을 더 빨리 태우세요.", SummaryHint.Text(S(3, 3, 12, 5, 0, 5), true));
            Assert.AreEqual("1 guest gave up waiting - serve the queues faster.", SummaryHint.Text(S(9, 1, 12, 3, 0, 3), false));
            // unlucky moments cost the most
            Assert.AreEqual(SummaryHint.Kind.Unlucky, SummaryHint.Pick(S(10, 0, 12, 5, 4, 5)));
            StringAssert.StartsWith("4 unlucky moments", SummaryHint.Text(S(10, 0, 12, 5, 4, 5), false));
            // Luck went down
            var down = S(10, 0, -8, 5, 1, 5);
            Assert.AreEqual(SummaryHint.Kind.Luck, SummaryHint.Pick(down));
            StringAssert.StartsWith("Luck -8 today", SummaryHint.Text(down, false));
            StringAssert.StartsWith("오늘 운 -8", SummaryHint.Text(down, true));
            // no streak
            var flat = S(10, 0, 12, 1, 0, 1);
            Assert.AreEqual(SummaryHint.Kind.Streak, SummaryHint.Pick(flat));
            StringAssert.StartsWith("Best streak x1", SummaryHint.Text(flat, false));
            // S days get praise
            Assert.AreEqual("S", UIFormat.DayGrade(S(10, 0, 12, 6, 0, 6)));
            Assert.AreEqual(SummaryHint.Kind.PraiseStreak, SummaryHint.Pick(S(10, 0, 12, 6, 0, 6)));
            Assert.AreEqual("6 lucky moments in a row - superb shift!", SummaryHint.Text(S(10, 0, 12, 6, 0, 6), false));
            Assert.AreEqual(SummaryHint.Kind.PraiseClean, SummaryHint.Pick(S(10, 0, 12, 4, 0, 4)));
        }

        [Test]
        public void SummaryHint_NeverEmpty_NoPlaceholders()
        {
            for (int d = 0; d <= 6; d += 2)
                for (int l = 0; l <= 4; l += 2)
                    for (int luck = -12; luck <= 12; luck += 6)
                        for (int u = 0; u <= 4; u += 2)
                            for (int st = 0; st <= 6; st += 3)
                                foreach (bool ko in new[] { false, true })
                                {
                                    string t = SummaryHint.Text(S(d, l, luck, st, u, st), ko);
                                    Assert.IsFalse(string.IsNullOrEmpty(t));
                                    StringAssert.DoesNotContain("{", t);
                                    Assert.LessOrEqual(t.Length, 72, "short: " + t);
                                }
        }

        // ------------------------------------------------------------------ run journey

        [Test]
        public void Journey_RecordsGradesInDayOrder_ReplaysReplace_Totals()
        {
            var j = new RunJourney();
            j.Add(2, S(10, 0, 12, 6, 0, 6)); // S
            j.Add(1, S(0, 2, -5, 0, 3, 0));  // C
            j.Add(0, S(10, 0, 0, 0, 0, 0));  // day 0: ignored
            Assert.AreEqual(2, j.Count);
            Assert.AreEqual(1, j.Days[0].Day);
            Assert.AreEqual("C", j.GradeOf(1));
            Assert.AreEqual("S", j.GradeOf(2));
            Assert.IsNull(j.GradeOf(3));
            Assert.AreEqual(10, j.Delivered);
            Assert.AreEqual(100, j.TipsEarned);
            Assert.AreEqual(6, j.BestStreak);
            j.Add(1, S(4, 0, 0, 0, 0, 2)); // a replay of day 1
            Assert.AreEqual(2, j.Count);
            Assert.AreEqual(14, j.Delivered);
            j.Clear();
            Assert.AreEqual(0, j.Count);
            Assert.AreEqual(0, j.TipsEarned);
        }

        // ------------------------------------------------------------------ best record

        [Test]
        public void BestRecord_RoundTrips_OldStringsStillRead()
        {
            var r = EndingRecord.Parse("1:2,3:1");
            Assert.IsFalse(r.HasBest, "an old save has no best yet");
            Assert.AreEqual(2, r.Times(EndingKind.Prosperous));
            r.OfferBest(60, 200, out bool luckBeaten, out bool tipsBeaten);
            Assert.IsFalse(luckBeaten || tipsBeaten, "the first record is not a NEW BEST");
            Assert.AreEqual("1:2,3:1,L:60,T:200", r.Serialize());
            r.OfferBest(70, 150, out luckBeaten, out tipsBeaten);
            Assert.IsTrue(luckBeaten);
            Assert.IsFalse(tipsBeaten);
            Assert.AreEqual(70, r.BestLuck);
            Assert.AreEqual(200, r.BestTips, "each best is kept on its own");
            var back = EndingRecord.Parse(r.Serialize());
            Assert.AreEqual(70, back.BestLuck);
            Assert.AreEqual(200, back.BestTips);
            Assert.AreEqual(1, back.Times(EndingKind.Haunted));
            // junk best values are skipped, never thrown
            var junk = EndingRecord.Parse("L:x,T:-3,L:,2:1,T:99999999999");
            Assert.AreEqual(-1, junk.BestLuck);
            Assert.Greater(junk.BestTips, 0);
            Assert.AreEqual(1, junk.Times(EndingKind.Modest));
        }

        [Test]
        public void EndingCollection_SavesBest_FlagsNewBest()
        {
            var store = new MemoryEndingStore("2:1,L:40,T:100");
            var c = new EndingCollection(store);
            c.Add(EndingKind.Modest, 55, 90);
            Assert.IsTrue(c.NewBestLuck);
            Assert.IsFalse(c.NewBestTips);
            Assert.AreEqual("2:2,L:55,T:100", store.Value);
            c.ClearNew();
            Assert.IsFalse(c.NewBestLuck || c.NewBestTips);
            Assert.AreEqual(EndingKind.None, c.LastNew);
            c.Add(EndingKind.Haunted);
            Assert.AreEqual("2:2,3:1,L:55,T:100", store.Value, "no numbers: the best stays");
        }

        [Test]
        public void BestRecord_TitleText()
        {
            Assert.AreEqual("BEST  LUCK 72  ·  $340", UIFormat.BestRecord(72, 340, false));
            Assert.AreEqual("최고 기록  운 72  ·  $340", UIFormat.BestRecord(72, 340, true));
        }
    }
}
