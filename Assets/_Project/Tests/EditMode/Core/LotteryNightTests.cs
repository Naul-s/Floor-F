using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Lottery night (DayDef.lottery: LotteryDrawn / LotteryWon, jackpot tip x lotteryTipMultiplier and Luck
    /// +lotteryLuckBonus) and lucky-for-all numbers (DayDef.luckyForAll, generalising star night's 7).
    /// Fixture: 1 s per floor, instant doors/boarding, deliveryLuckBonus 1, streak bonus off.
    /// </summary>
    public class LotteryNightTests
    {
        const int Duration = 90;

        /// <summary>Real day 1 is a lottery night with <paramref name="draws"/> draws over 90 s.</summary>
        static Sim Lottery(int draws = 2, int seed = 1, Action<SimFixture.Defs> tweak = null, Action<Sim> beforeBegin = null)
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                d.Days.days[0].lottery = true;
                d.Days.days[0].lotteryDraws = draws;
                d.Days.days[0].durationSeconds = Duration;
                d.Hotel.rules.lotteryTipMultiplier = 2f;
                d.Hotel.rules.lotteryLuckBonus = 3;
                tweak?.Invoke(d);
            }), seed);
            sim.S.StartNewGame();
            beforeBegin?.Invoke(sim);
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        /// <summary>Every floor reads "F" except the given physical floors, which show <paramref name="label"/>.</summary>
        static Action<Sim> OnlyNumber(string label, params int[] floors) => sim =>
        {
            for (int f = 1; f <= 9; f++)
                sim.S.Building.SetLabel(F(f), new FloorLabel(floors.Contains(f) ? label : "F"));
        };

        static void Ride(Sim sim, int from, int to)
        {
            sim.Open();
            sim.Close();
            sim.GoTo(to);
            sim.Open();
        }

        // ================================================================================== draws

        [Test]
        public void LotteryNight_FirstDraw_RightAtTheDayStart_AfterDayStarted()
        {
            var sim = Lottery();
            Assert.IsTrue(sim.S.IsLotteryNight);
            sim.AssertOrder(typeof(DayStarted), typeof(LotteryDrawn));
            var draw = sim.Events<LotteryDrawn>().Single();
            Assert.AreEqual(0, draw.DrawIndex);
            Assert.AreEqual(2, draw.DrawCount);
            Assert.That(draw.Number, Is.InRange(1, 9), "one of the floor buttons");
            Assert.AreEqual(draw.Number, sim.S.LotteryNumber);
        }

        [Test]
        public void LotteryNight_KthDraw_AtKTimesDurationOverDraws()
        {
            var sim = Lottery(draws: 3);
            sim.Run(29.9f);
            Assert.AreEqual(1, sim.Events<LotteryDrawn>().Count);
            sim.Run(0.2f);
            Assert.AreEqual(2, sim.Events<LotteryDrawn>().Count, "second draw at 30 s");
            sim.Run(30f);
            Assert.AreEqual(3, sim.Events<LotteryDrawn>().Count, "third draw at 60 s");
            sim.Run(25f);
            Assert.AreEqual(3, sim.Events<LotteryDrawn>().Count, "no more draws");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, sim.Events<LotteryDrawn>().Select(e => e.DrawIndex));
            Assert.IsTrue(sim.Events<LotteryDrawn>().All(e => e.DrawCount == 3));
        }

        [Test]
        public void LotteryNight_NextDraw_DiffersFromThePrevious_UniformOverTheButtons()
        {
            var firsts = new HashSet<int>();
            for (int seed = 1; seed <= 300; seed++)
            {
                var sim = Lottery(seed: seed);
                sim.Run(Duration / 2f + 0.1f);
                var draws = sim.Events<LotteryDrawn>();
                Assert.AreEqual(2, draws.Count);
                Assert.AreNotEqual(draws[0].Number, draws[1].Number, "seed " + seed);
                firsts.Add(draws[0].Number);
            }
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 9), firsts, "every shown number can win");
        }

        [Test]
        public void LotteryNight_DrawsAmongShownNumberLabels_StickersCount_LettersDoNot()
        {
            var sim = Lottery(beforeBegin: s =>
            {
                OnlyNumber("8", 3, 6)(s);
                s.S.Building.SetLabel(F(9), new FloorLabel("12A")); // not a whole number
            });
            Assert.AreEqual(8, sim.S.LotteryNumber, "the only number on the panel");
            sim.Run(Duration / 2f + 0.1f);
            Assert.IsTrue(sim.Events<LotteryDrawn>().All(e => e.Number == 8), "only one candidate: drawn again");
        }

        [Test]
        public void LotteryNight_NoNumberShown_NoDraw()
        {
            var sim = Lottery(beforeBegin: OnlyNumber("1"));
            Assert.AreEqual(0, sim.S.LotteryNumber);
            Assert.IsEmpty(sim.Events<LotteryDrawn>());
        }

        [Test]
        public void LotteryNumber_BackToZero_WhenTheDayEnds()
        {
            var sim = Lottery();
            Assert.Greater(sim.S.LotteryNumber, 0);
            sim.S.EndDayNow();
            Assert.AreEqual(0, sim.S.LotteryNumber);
        }

        [Test]
        public void PlainNight_NoLottery()
        {
            var sim = Running();
            Assert.IsFalse(sim.S.IsLotteryNight);
            sim.Run(60f);
            Assert.AreEqual(0, sim.S.LotteryNumber);
            Assert.IsEmpty(sim.Events<LotteryDrawn>());
        }

        // ================================================================================== jackpot

        [Test]
        public void Jackpot_StepOutOnTheWinningFloor_LotteryWonBeforeDelivered_DoubleTip_LuckBonusAfter()
        {
            var sim = Lottery(beforeBegin: OnlyNumber("5", 5));
            Assert.AreEqual(5, sim.S.LotteryNumber);
            var g = sim.Spawn("plain", 1, 5);
            int luck = sim.S.Score.Luck;
            sim.Log.Clear();
            Ride(sim, 1, 5);
            var won = sim.Events<LotteryWon>().Single();
            Assert.AreEqual(g, won.Guest);
            Assert.AreEqual(F(5), won.Floor);
            Assert.AreEqual(5, won.Number);
            Assert.AreEqual(20, sim.Events<GuestDelivered>().Single().Tip, "10 x lotteryTipMultiplier 2");
            int iExit = sim.IndexOf<GuestExited>();
            int iWon = sim.IndexOf<LotteryWon>();
            int iDelivered = sim.IndexOf<GuestDelivered>();
            int iDeliveryLuck = sim.IndexOf<LuckChanged>(e => e.Source == LuckSource.Delivery);
            int iLotteryLuck = sim.IndexOf<LuckChanged>(e => e.Source == LuckSource.Lottery);
            int iTips = sim.IndexOf<TipsChanged>();
            Assert.That(new[] { iExit, iWon, iDelivered, iDeliveryLuck, iLotteryLuck, iTips }, Is.Ordered,
                "GuestExited -> LotteryWon -> GuestDelivered -> LuckChanged(Delivery) -> LuckChanged(Lottery) -> TipsChanged");
            Assert.IsTrue(iExit >= 0 && iLotteryLuck >= 0);
            Assert.AreEqual(3, sim.Events<LuckChanged>().Single(e => e.Source == LuckSource.Lottery).Delta, "lottery bonus +3");
            Assert.AreEqual(luck + 1 + 3, sim.S.Score.Luck, "delivery +1, jackpot +3");
        }

        [Test]
        public void Jackpot_OtherFloor_NoWin()
        {
            var sim = Lottery(beforeBegin: OnlyNumber("5", 5));
            sim.Spawn("plain", 1, 6);
            Ride(sim, 1, 6);
            Assert.IsEmpty(sim.Events<LotteryWon>());
            Assert.AreEqual(10, sim.Events<GuestDelivered>().Single().Tip);
            Assert.IsFalse(sim.Events<LuckChanged>().Any(e => e.Source == LuckSource.Lottery));
        }

        [Test]
        public void Jackpot_Party_EveryMemberWins()
        {
            var sim = Lottery(beforeBegin: OnlyNumber("6", 6));
            sim.Spawn("couple", 1, 6); // distance 5: neutral for the couple
            Ride(sim, 1, 6);
            Assert.AreEqual(2, sim.Events<LotteryWon>().Count);
            Assert.IsTrue(sim.Events<GuestDelivered>().All(e => e.Tip == 20));
            Assert.AreEqual(2, sim.Events<LuckChanged>().Count(e => e.Source == LuckSource.Lottery));
        }

        [Test]
        public void Jackpot_MultipliesOnTopOfLuckyFloor_RoundedOnce()
        {
            // biz: lucky 8 -> lucky floor x2, then the jackpot x3 (tip 20 -> 120)
            var sim = Lottery(tweak: d => d.Hotel.rules.lotteryTipMultiplier = 3f, beforeBegin: OnlyNumber("8", 5));
            Assert.AreEqual(8, sim.S.LotteryNumber);
            sim.Spawn("biz", 1, 5);
            Ride(sim, 1, 5);
            Assert.AreEqual(1, sim.Events<LotteryWon>().Count);
            Assert.AreEqual(120, sim.Events<GuestDelivered>().Single().Tip);
        }

        [Test]
        public void Jackpot_HauntedByTheRideDistance_PaysNothing()
        {
            // couple: unlucky 4 -> a 4-floor ride costs 5 Luck; at Luck 5 the hotel is haunted before any payment
            var sim = Lottery(beforeBegin: OnlyNumber("5", 5));
            sim.S.Score.SetLuck(5, LuckSource.Other);
            sim.Spawn("couple", 1, 5);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            Assert.IsTrue(sim.S.OpenDoors());
            sim.RunUntil(() => sim.S.Phase != DayPhase.Running);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.IsEmpty(sim.Events<LotteryWon>());
            Assert.IsEmpty(sim.Events<GuestDelivered>());
        }

        // ================================================================================== lucky for all

        static GuestLineDef Line(string trigger) => new GuestLineDef { trigger = trigger, text = trigger, textKo = trigger };

        static void Talkers(SimFixture.Defs d)
        {
            var list = d.Guests.guests.ToList();
            list.Add(new GuestDef
            {
                id = "hater5", lucky = new[] { 99 }, unlucky = new[] { 5 }, patienceSeconds = 1000f, baseTip = 10, partySize = 1,
                traits = new string[0], color = "#FFFFFF",
                lines = new[] { Line(GuestLineTriggers.LuckyFloor), Line(GuestLineTriggers.UnluckyFloor), Line(GuestLineTriggers.LuckyDistance), Line(GuestLineTriggers.UnluckyDistance), Line(GuestLineTriggers.LuckyOccupancy) },
            });
            d.Guests.guests = list.ToArray();
            d.Hotel.rules.deliveryLuckBonus = 0;
        }

        static Sim LuckyForAll(int number, bool starNight = false)
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                Talkers(d);
                d.Days.days[0].luckyForAll = number;
                d.Days.days[0].starNight = starNight;
            }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        [Test]
        public void LuckyForAllRules_AddsTheNumber_BeatsAnOwnUnluckyCopy()
        {
            var p = LuckyForAllRules.Apply(LuckProfile.Of(new[] { 3 }, new[] { 4, 12 }), 12);
            Assert.AreEqual(LuckVerdict.Lucky, p.Judge(12));
            Assert.AreEqual(LuckVerdict.Lucky, p.Judge(3));
            Assert.AreEqual(LuckVerdict.Unlucky, p.Judge(4));
            var own = LuckProfile.Of(new[] { 12 }, new int[0]);
            Assert.AreSame(own, LuckyForAllRules.Apply(own, 12));
            Assert.AreSame(own, LuckyForAllRules.Apply(own, 0), "0 = none");
            Assert.AreEqual(LuckVerdict.Lucky, StarNightRules.Apply(null).Judge(7), "star night is the same rule for 7");
        }

        [Test]
        public void LuckyForAll_FloorAndDistance_LuckyForEveryone_NoOwnNumberLine()
        {
            var sim = LuckyForAll(5);
            Assert.AreEqual(5, sim.S.LuckyForAll);
            var plain = sim.Spawn("plain", 1, 6);
            var hater = sim.Spawn("hater5", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            var floor = sim.Events<LuckJudged>().Where(e => e.Source == LuckSource.FloorLabel).ToList();
            Assert.AreEqual(2, floor.Count);
            Assert.IsTrue(floor.All(e => e.Verdict == LuckVerdict.Lucky && e.Number == 5), "label 5 lucky for both, even the one who fears 5");
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            var dist = sim.Events<LuckJudged>().Where(e => e.Source == LuckSource.RideDistance).ToList();
            Assert.AreEqual(2, dist.Count);
            Assert.IsTrue(dist.All(e => e.Verdict == LuckVerdict.Lucky && e.Number == 5), "ride of 5 floors lucky for both");
            CollectionAssert.AreEquivalent(new[] { plain, hater }, dist.Select(e => e.Guest));
            var said = sim.Events<GuestSpoke>().Select(e => e.LineTrigger).ToList();
            CollectionAssert.DoesNotContain(said, GuestLineTriggers.UnluckyFloor);
            CollectionAssert.DoesNotContain(said, GuestLineTriggers.LuckyFloor, "not their own number: no own-number line");
            CollectionAssert.DoesNotContain(said, GuestLineTriggers.LuckyDistance);
        }

        [Test]
        public void LuckyForAll_Occupancy_LuckyForEveryone()
        {
            var sim = LuckyForAll(5);
            for (int i = 0; i < 4; i++) sim.Spawn("plain", 1, 3);
            sim.Spawn("hater5", 1, 3);
            sim.Open();
            sim.Close();
            var occ = sim.Events<LuckJudged>().Where(e => e.Source == LuckSource.Occupancy).ToList();
            Assert.AreEqual(5, occ.Count);
            Assert.IsTrue(occ.All(e => e.Verdict == LuckVerdict.Lucky && e.Number == 5));
        }

        [Test]
        public void LuckyForAll_OnAStarNight_BothSevenAndTheNumber()
        {
            var sim = LuckyForAll(5, starNight: true);
            sim.Spawn("hater5", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            sim.Close();
            sim.GoTo(7);
            sim.Open();
            var floor = sim.Events<LuckJudged>().Where(e => e.Source == LuckSource.FloorLabel).ToList();
            CollectionAssert.AreEqual(new[] { 5, 7 }, floor.Select(e => e.Number));
            Assert.IsTrue(floor.All(e => e.Verdict == LuckVerdict.Lucky));
        }

        /// <summary>
        /// Bug (polish pass, found auditing Christmas Eve's data): the solo traveller fears 2, so the "12" sticker read
        /// Unlucky through its digit 2 although tonight 12 is lucky for EVERYONE. The whole lucky-for-all number wins;
        /// a label that only CONTAINS a feared digit without being the number ("2" itself, "21") is still feared.
        /// </summary>
        [Test]
        public void LuckyForAll_WholeNumberLabel_BeatsAFearedDigit_NoOwnLine()
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                Talkers(d);
                var list = d.Guests.guests.ToList();
                list.Add(new GuestDef
                {
                    id = "hater2", lucky = new[] { 99 }, unlucky = new[] { 2 }, patienceSeconds = 1000f, baseTip = 10, partySize = 1,
                    traits = new string[0], color = "#FFFFFF",
                    lines = new[] { Line(GuestLineTriggers.LuckyFloor), Line(GuestLineTriggers.UnluckyFloor) },
                });
                d.Guests.guests = list.ToArray();
                d.Days.days[0].luckyForAll = 12;
            }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.Building.SetLabel(F(5), new FloorLabel("12"));
            sim.S.Building.SetLabel(F(6), new FloorLabel("21"));
            sim.Spawn("hater2", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            var twelve = sim.Events<LuckJudged>().Single(e => e.Source == LuckSource.FloorLabel);
            Assert.AreEqual(LuckVerdict.Lucky, twelve.Verdict, "12 is lucky for all tonight, its digit 2 does not count against it");
            Assert.AreEqual(12, twelve.Number);
            var said = sim.Events<GuestSpoke>().Select(e => e.LineTrigger).ToList();
            CollectionAssert.DoesNotContain(said, GuestLineTriggers.UnluckyFloor);
            CollectionAssert.DoesNotContain(said, GuestLineTriggers.LuckyFloor, "not their own number: no own-number line");
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            var twentyOne = sim.Events<LuckJudged>().Where(e => e.Source == LuckSource.FloorLabel).Last();
            Assert.AreEqual(LuckVerdict.Unlucky, twentyOne.Verdict, "21 is not tonight's number: the feared 2 still counts");
            Assert.AreEqual(2, twentyOne.Number);
        }

        [Test]
        public void LuckyForAllRules_JudgeLabel_StarNightSeven_AndPlainNights()
        {
            var fearsOne = LuckProfile.Of(new[] { 3 }, new[] { 1 });
            var star = StarNightRules.Apply(fearsOne);
            Assert.AreEqual(LuckVerdict.Unlucky, LuckyForAllRules.JudgeLabel(star, new FloorLabel("17"), out int n, true, 0), "17 is not 7");
            Assert.AreEqual(1, n);
            Assert.AreEqual(LuckVerdict.Lucky, LuckyForAllRules.JudgeLabel(star, new FloorLabel("7"), out n, true, 0));
            Assert.AreEqual(7, n);
            Assert.AreEqual(LuckVerdict.Unlucky, LuckyForAllRules.JudgeLabel(fearsOne, new FloorLabel("12"), out n, false, 0), "no lucky-for-all number: as LabelNumbers.Judge");
            Assert.AreEqual(LuckVerdict.Lucky, LuckyForAllRules.JudgeLabel(LuckyForAllRules.Apply(fearsOne, 12), new FloorLabel("12"), out n, false, 12));
            Assert.AreEqual(12, n);
            Assert.AreEqual(LuckVerdict.Neutral, LuckyForAllRules.JudgeLabel(fearsOne, new FloorLabel("F"), out n, true, 12));
        }

        [Test]
        public void NoLuckyForAll_OwnUnluckyStays()
        {
            var sim = LuckyForAll(0);
            sim.Spawn("hater5", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            Assert.AreEqual(LuckVerdict.Unlucky, sim.Events<LuckJudged>().Single(e => e.Source == LuckSource.FloorLabel).Verdict);
        }
    }
}
