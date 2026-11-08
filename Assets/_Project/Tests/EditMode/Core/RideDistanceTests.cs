using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Rule 3 (ride distance), the devout guest's "666" and Friday the 13th. Fixture timing: 1 s per floor, instant doors
    /// and boarding (SimFixture), so distances are exact and easy to read.
    /// </summary>
    public class RideDistanceTests
    {
        const float TipX = 1.5f;
        const int Bonus = 2, Penalty = 6;

        static GuestLineDef Line(string trigger) => new GuestLineDef { trigger = trigger, text = trigger, textKo = "ko:" + trigger };

        static GuestDef Guest(string id, int[] lucky, int[] unlucky, int party = 1, string[] traits = null) => new GuestDef
        {
            id = id, lucky = lucky, unlucky = unlucky, patienceSeconds = 1000f, baseTip = 10, partySize = party,
            traits = traits ?? new string[0], color = "#FFFFFF",
            lines = new[]
            {
                Line(GuestLineTriggers.LuckyDistance), Line(GuestLineTriggers.UnluckyDistance), Line(GuestLineTriggers.Delivered),
            },
        };

        /// <summary>
        /// "seven": lucky 7, unlucky 6. "far": lucky 12, unlucky 10 (only a distance can show those). "four": unlucky 4 only.
        /// "pair": lucky 7, party of two. "devout": lucky 7, unlucky 6 + trait devout. "italian": lucky 13, unlucky 17.
        /// </summary>
        static void Setup(SimFixture.Defs d)
        {
            var r = d.Hotel.rules;
            r.luckyDistanceTipMultiplier = TipX;
            r.luckyDistanceLuckBonus = Bonus;
            r.unluckyDistanceLuckPenalty = Penalty;
            r.friday13PenaltyMultiplier = 1.5f;
            r.deliveryLuckBonus = 0;
            var list = d.Guests.guests.ToList();
            list.Add(Guest("seven", new[] { 7 }, new[] { 6 }));
            list.Add(Guest("far", new[] { 12 }, new[] { 10 }));
            list.Add(Guest("four", new[] { 99 }, new[] { 4 }));
            list.Add(Guest("pair", new[] { 7 }, new int[0], party: 2));
            list.Add(Guest("devout", new[] { 7 }, new[] { 6 }, traits: new[] { GuestTraits.Devout }));
            list.Add(Guest("italian", new[] { 13 }, new[] { 17 }));
            d.Guests.guests = list.ToArray();
        }

        static Sim RunningDistance(Action<SimFixture.Defs> tweak = null) => Running(d => { Setup(d); tweak?.Invoke(d); });

        /// <summary>Real day 1 is a Friday the 13th.</summary>
        static Sim RunningFriday(Action<SimFixture.Defs> tweak = null) => RunningDistance(d => { d.Days.days[0].friday13 = true; tweak?.Invoke(d); });

        static GuestInfo Info(Sim sim, GuestId g)
        {
            Assert.IsTrue(sim.S.Guests.TryGet(g, out var info), "guest " + g + " active");
            return info;
        }

        static List<LuckJudged> Distance(Sim sim) => sim.Events<LuckJudged>().Where(j => j.Source == LuckSource.RideDistance).ToList();

        /// <summary>Boards everyone waiting here (doors open, everyone in, doors closed).</summary>
        static void Board(Sim sim)
        {
            sim.Open();
            sim.Close();
        }

        // ------------------------------------------------------------------ counting

        [Test]
        public void RideDistance_Direct_CountsFloors_JudgedOnExit()
        {
            var sim = RunningDistance();
            var g = sim.Spawn("seven", 1, 8);
            Assert.AreEqual(0, Info(sim, g).FloorsTravelled, "waiting: 0");
            Board(sim);
            sim.GoTo(8);
            Assert.AreEqual(7, Info(sim, g).FloorsTravelled, "1 -> 8");
            Assert.IsEmpty(Distance(sim), "not judged before stepping out");
            sim.Open();

            var j = Distance(sim).Single();
            Assert.AreEqual(g, j.Guest);
            Assert.AreEqual(7, j.Number);
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict);
            Assert.AreEqual(F(8), j.Floor, "judged at the exit floor");
        }

        [Test]
        public void RideDistance_Detour_CountsEveryFloorMoved()
        {
            var sim = RunningDistance();
            var g = sim.Spawn("seven", 1, 2);
            Board(sim);
            sim.GoTo(5); // passing / stopping without opening does not judge anything
            sim.GoTo(2);
            Assert.AreEqual(4 + 3, Info(sim, g).FloorsTravelled, "up 4, back down 3");
            sim.Open();
            var j = Distance(sim).Single();
            Assert.AreEqual(7, j.Number);
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict, "a detour turned a 1-floor ride into a lucky 7");
        }

        [Test]
        public void RideDistance_ReversalMidFloor_CountsBothWays()
        {
            var sim = RunningDistance();
            var g = sim.Spawn("four", 1, 2);
            Board(sim);
            Assert.IsTrue(sim.S.RequestFloor(F(6)));
            sim.Run(2.5f); // car at 3.5
            Assert.IsTrue(sim.S.RequestFloor(F(2)));
            sim.RunUntil(() => sim.S.Elevator.Motion == ElevatorMotion.Idle);
            Assert.AreEqual(4, Info(sim, g).FloorsTravelled, "2.5 up + 1.5 down");
            sim.Open();
            var j = Distance(sim).Single();
            Assert.AreEqual(4, j.Number);
            Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict);
        }

        [Test]
        public void RideDistance_RidersBoardingAtDifferentFloors_CountSeparately()
        {
            var sim = RunningDistance();
            var a = sim.Spawn("seven", 1, 9);
            Board(sim);
            sim.GoTo(3);
            var b = sim.Spawn("seven", 3, 9);
            Assert.AreEqual(2, Info(sim, a).FloorsTravelled);
            Assert.AreEqual(0, Info(sim, b).FloorsTravelled);
            Board(sim);
            sim.GoTo(9);
            Assert.AreEqual(8, Info(sim, a).FloorsTravelled);
            Assert.AreEqual(6, Info(sim, b).FloorsTravelled);
            sim.Open();

            var judged = Distance(sim);
            Assert.AreEqual(1, judged.Count, "8 floors is neutral for a; 6 is unlucky for b");
            Assert.AreEqual(b, judged[0].Guest);
            Assert.AreEqual(6, judged[0].Number);
            Assert.AreEqual(LuckVerdict.Unlucky, judged[0].Verdict);
        }

        [Test]
        public void RideDistance_Party_EachMemberJudged()
        {
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            sim.Spawn("pair", 1, 8);
            Board(sim);
            sim.GoTo(8);
            sim.Open();
            var judged = Distance(sim);
            Assert.AreEqual(2, judged.Count, "both members rode 7 floors");
            Assert.IsTrue(judged.All(j => j.Number == 7 && j.Verdict == LuckVerdict.Lucky));
            Assert.AreEqual(2, judged.Select(j => j.Guest).Distinct().Count());
            Assert.AreEqual(luck + 2 * Bonus, sim.S.Score.Luck);
            Assert.AreEqual(1, sim.Events<GuestSpoke>().Count(e => e.LineTrigger == GuestLineTriggers.LuckyDistance), "the party speaks once");
        }

        [Test]
        public void RideDistance_Neutral_NothingPublished_DeliveredLineSaid()
        {
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            sim.Spawn("seven", 1, 4); // 3 floors, label 4: neutral for "seven"
            Board(sim);
            sim.GoTo(4);
            sim.Open();
            Assert.IsEmpty(sim.Events<LuckJudged>());
            Assert.AreEqual(luck, sim.S.Score.Luck);
            Assert.AreEqual(10, sim.Events<GuestDelivered>().Single().Tip);
            CollectionAssert.AreEqual(new[] { GuestLineTriggers.Delivered }, sim.Events<GuestSpoke>().Select(e => e.LineTrigger).ToArray());
        }

        // ------------------------------------------------------------------ timing / order / payment

        [Test]
        public void RideDistance_Lucky_OrderBeforePayment_TipMultiplied_LineReplacesDelivered()
        {
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            var g = sim.Spawn("seven", 1, 8);
            Board(sim);
            sim.GoTo(8);
            sim.Log.Clear();
            sim.Open();

            sim.AssertOrder(typeof(GuestExited), typeof(LuckJudged), typeof(LuckChanged), typeof(GuestMoodChanged), typeof(GuestSpoke),
                typeof(GuestDelivered), typeof(TipsChanged));
            Assert.AreEqual(LuckSource.RideDistance, sim.Events<LuckChanged>().First().Source);
            Assert.AreEqual(luck + Bonus, sim.S.Score.Luck, "rules.luckyDistanceLuckBonus");
            Assert.AreEqual(15, sim.Events<GuestDelivered>().Single().Tip, "10 x luckyDistanceTipMultiplier 1.5");
            var spoke = sim.Events<GuestSpoke>().Single();
            Assert.AreEqual(GuestLineTriggers.LuckyDistance, spoke.LineTrigger, "one bubble per exit: the distance line replaces 'delivered'");
            Assert.AreEqual(g, spoke.Guest);
            Assert.AreEqual(GuestMood.Happy, sim.Events<GuestMoodChanged>().Single().Current);
        }

        [Test]
        public void RideDistance_Lucky_StepsTheStreak_BeforePayment()
        {
            var sim = RunningDistance(d => { d.Hotel.rules.streakTipBonusPerStep = 0.1f; d.Hotel.rules.streakMaxTipMultiplier = 2f; });
            sim.Spawn("seven", 1, 8);
            Board(sim);
            sim.GoTo(8);
            sim.Log.Clear();
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            Assert.AreEqual(1, sim.Events<LuckStreakChanged>().Count);
            Assert.Less(sim.IndexOf<LuckStreakChanged>(), sim.IndexOf<GuestDelivered>(), "streak moved before the tip");
            Assert.AreEqual(17, sim.Events<GuestDelivered>().Single().Tip, "10 x 1.5 (distance) x 1.1 (streak 1) = 16.5 -> 17");
        }

        [Test]
        public void RideDistance_Unlucky_PenaltyScaredLine()
        {
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            sim.Spawn("seven", 1, 7); // 6 floors, label 7 lucky
            Board(sim);
            sim.GoTo(7);
            sim.Log.Clear();
            sim.Open();
            var j = Distance(sim).Single();
            Assert.AreEqual(6, j.Number);
            Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict);
            Assert.AreEqual(luck + 2 - Penalty, sim.S.Score.Luck, "lucky label +2, unlucky distance -6");
            Assert.AreEqual(GuestMood.Scared, sim.Events<GuestMoodChanged>().Last().Current);
            Assert.AreEqual(20, sim.Events<GuestDelivered>().Single().Tip, "unlucky distance does not cut the tip (label x2 stays)");
            Assert.IsTrue(sim.Events<GuestSpoke>().Any(e => e.LineTrigger == GuestLineTriggers.UnluckyDistance));
            Assert.IsFalse(sim.Events<GuestSpoke>().Any(e => e.LineTrigger == GuestLineTriggers.Delivered));
        }

        [Test]
        public void RideDistance_UnluckyEmptiesLuck_HauntedBeforePayment()
        {
            var sim = RunningDistance(d => d.Hotel.rules.startLuck = Penalty);
            sim.Spawn("far", 1, 3);
            Board(sim);
            sim.GoTo(7);
            sim.GoTo(3); // 6 + 4 = 10: unlucky
            sim.Log.Clear();
            Assert.IsTrue(sim.S.OpenDoors());
            sim.RunUntil(() => sim.S.Phase != DayPhase.Running);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.AreEqual(10, Distance(sim).Single().Number);
            Assert.IsEmpty(sim.Events<GuestDelivered>(), "the hotel was haunted before the guest paid");
            sim.AssertOrder(typeof(GuestExited), typeof(LuckJudged), typeof(LuckChanged), typeof(DayEnded), typeof(EndingReached));
        }

        [Test]
        public void RideDistance_OnTutorialDay_JudgedButNeverHaunts()
        {
            var data = SimFixture.WithTutorial(new SpawnDef[0], d => { Setup(d); d.Hotel.rules.startLuck = Penalty; });
            var sim = new Sim(data);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsTrue(sim.S.IsTutorial);
            sim.Spawn("far", 1, 3);
            Board(sim);
            sim.GoTo(7);
            sim.GoTo(3); // 10 floors: unlucky, Luck 6 -> 0
            sim.Open();
            Assert.AreEqual(LuckVerdict.Unlucky, Distance(sim).Single().Verdict);
            Assert.AreEqual(0, sim.S.Score.Luck);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "no Haunted on the training shift");
            Assert.AreEqual(1, sim.Events<GuestDelivered>().Count);
        }

        // ------------------------------------------------------------------ devout (666)

        [Test]
        public void Devout_Unlucky6_CountsDouble_AnySource()
        {
            // label 6
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            sim.Spawn("devout", 1, 9);
            Board(sim);
            sim.GoTo(6);
            sim.Open();
            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(LuckSource.FloorLabel, j.Source);
            Assert.AreEqual(6, j.Number);
            Assert.AreEqual(luck - 2 * 10, sim.S.Score.Luck, "unluckyFloorLuckPenalty 10 x 2");

            // headcount 6 (five plain riders + the devout guest)
            sim = RunningDistance();
            luck = sim.S.Score.Luck;
            for (int i = 0; i < 5; i++) sim.Spawn("plain", 1, 9);
            sim.Spawn("devout", 1, 9);
            Board(sim);
            j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(LuckSource.Occupancy, j.Source);
            Assert.AreEqual(luck - 2 * 5, sim.S.Score.Luck, "unluckyOccupancyLuckPenalty 5 x 2");

            // ride distance 6
            sim = RunningDistance();
            luck = sim.S.Score.Luck;
            sim.Spawn("devout", 1, 7);
            Board(sim);
            sim.GoTo(7);
            sim.Open();
            j = Distance(sim).Single();
            Assert.AreEqual(6, j.Number);
            Assert.AreEqual(luck + 2 - 2 * Penalty, sim.S.Score.Luck, "lucky label 7 +2, distance 6 x 2");
        }

        [Test]
        public void NotDevout_Unlucky6_SinglePenalty()
        {
            var sim = RunningDistance();
            int luck = sim.S.Score.Luck;
            sim.Spawn("seven", 1, 9);
            Board(sim);
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(luck - 10, sim.S.Score.Luck);
        }

        [Test]
        public void Devout_OtherUnluckyNumbers_NotDoubled()
        {
            var sim = RunningDistance(d => d.Guests.guests.First(g => g.id == "devout").unlucky = new[] { 4, 6 });
            int luck = sim.S.Score.Luck;
            sim.Spawn("devout", 1, 9);
            Board(sim);
            sim.GoTo(4);
            sim.Open();
            Assert.AreEqual(luck - 10, sim.S.Score.Luck, "only the 6 is the Beast's number");
        }

        // ------------------------------------------------------------------ Friday the 13th

        [Test]
        public void Friday13_Flag_FromDayDef()
        {
            var sim = RunningFriday();
            Assert.IsTrue(sim.S.IsFriday13);
            Assert.IsFalse(RunningDistance().S.IsFriday13);
        }

        [Test]
        public void Friday13_UnluckyPenalties_Multiplied()
        {
            var sim = RunningFriday();
            int luck = sim.S.Score.Luck;
            sim.Spawn("granny", 1, 9);
            Board(sim);
            sim.GoTo(4);
            sim.Open(); // label 4: 10 x 1.5
            Assert.AreEqual(luck - 15, sim.S.Score.Luck);

            sim = RunningFriday();
            luck = sim.S.Score.Luck;
            for (int i = 0; i < 3; i++) sim.Spawn("plain", 1, 9);
            sim.Spawn("granny", 1, 9);
            Board(sim); // headcount 4: 5 x 1.5 = 7.5 -> 8
            Assert.AreEqual(luck - 8, sim.S.Score.Luck);

            sim = RunningFriday();
            luck = sim.S.Score.Luck;
            sim.Spawn("seven", 1, 7);
            Board(sim);
            sim.GoTo(7);
            sim.Open(); // label 7 +2 (bonuses untouched), distance 6: 6 x 1.5 = 9
            Assert.AreEqual(luck + 2 - 9, sim.S.Score.Luck);
        }

        [Test]
        public void Friday13_Devout6_StacksBothMultipliers()
        {
            var sim = RunningFriday();
            int luck = sim.S.Score.Luck;
            sim.Spawn("devout", 1, 9);
            Board(sim);
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(luck - 30, sim.S.Score.Luck, "10 x 2 (666) x 1.5 (Friday)");
        }

        [Test]
        public void Friday13_PatiencePenalty_NotMultiplied()
        {
            var sim = RunningFriday();
            int luck = sim.S.Score.Luck;
            sim.Spawn("impatient", 9, 1);
            sim.Run(6f);
            Assert.AreEqual(luck - 5, sim.S.Score.Luck, "patience is not a number verdict");
        }

        [Test]
        public void Friday13_Distance13_UnluckyForEveryone_EvenLucky13()
        {
            // normal day: the Italian's 13 is lucky
            var sim = RunningDistance();
            sim.Spawn("italian", 1, 4);
            Board(sim);
            sim.GoTo(9);
            sim.GoTo(4); // 8 + 5 = 13
            sim.Open();
            var j = Distance(sim).Single();
            Assert.AreEqual(13, j.Number);
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict);

            // Friday: 13 is unlucky for him and for a guest without any 13
            foreach (var id in new[] { "italian", "seven" })
            {
                sim = RunningFriday();
                int luck = sim.S.Score.Luck;
                sim.Spawn(id, 1, 4);
                Board(sim);
                sim.GoTo(9);
                sim.GoTo(4);
                sim.Log.Clear();
                sim.Open();
                j = Distance(sim).Single();
                Assert.AreEqual(13, j.Number, id);
                Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict, id + ": Friday the 13th");
                Assert.AreEqual(luck - 9, sim.S.Score.Luck, id + ": 6 x 1.5");
                Assert.AreEqual(10, sim.Events<GuestDelivered>().Single().Tip, id + ": no lucky tip");
                Assert.IsFalse(sim.Events<GuestSpoke>().Any(e => e.LineTrigger == GuestLineTriggers.UnluckyDistance),
                    id + ": not their own superstition - no own-number line");
                Assert.IsTrue(sim.Events<GuestSpoke>().Any(e => e.LineTrigger == GuestLineTriggers.Delivered), id + ": says the plain line");
            }
        }

        [Test]
        public void Friday13_OwnUnlucky13_SaysOwnLine()
        {
            var sim = RunningFriday(d => d.Guests.guests.First(g => g.id == "seven").unlucky = new[] { 6, 13 });
            sim.Spawn("seven", 1, 4);
            Board(sim);
            sim.GoTo(9);
            sim.GoTo(4);
            sim.Open();
            Assert.AreEqual(LuckVerdict.Unlucky, Distance(sim).Single().Verdict);
            Assert.IsTrue(sim.Events<GuestSpoke>().Any(e => e.LineTrigger == GuestLineTriggers.UnluckyDistance));
        }

        [Test]
        public void Friday13_HonestStickerPenalty_Multiplied()
        {
            var sim = RunningDistance(d => { d.Hotel.rules.startTips = 50; d.Days.days[1].friday13 = true; });
            sim.EndDayToShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId("any_to_8"), F(5)));
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsTrue(sim.S.IsFriday13);
            int luck = sim.S.Score.Luck;
            sim.Spawn("honest", 1, 9);
            Board(sim);
            sim.GoTo(5);
            sim.Open();
            Assert.AreEqual(luck - 8, sim.S.Score.Luck, "honestGuestLuckPenalty 5 x 1.5 = 7.5 -> 8");
        }

        // ------------------------------------------------------------------ AutoPilot

        /// <summary>LuckAware knows rule 3: a lone guest whose direct ride is unlucky gets a short detour instead.</summary>
        [Test]
        public void AutoPilot_LuckAware_DetoursAroundUnluckyDistance()
        {
            foreach (var strategy in new[] { AutoPilotStrategy.LuckBlind, AutoPilotStrategy.LuckAware })
            {
                var sim = RunningDistance();
                var pilot = new AutoPilot(sim.S.Elevator, sim.S, sim.S.Guests, sim.S.Building, strategy) { Data = sim.S.Data };
                sim.Spawn("four", 1, 5); // direct: 4 floors = unlucky
                for (float t = 0f; t < 60f && sim.Events<GuestDelivered>().Count == 0; t += 0.05f)
                {
                    pilot.Step();
                    sim.Run(0.05f);
                }
                Assert.AreEqual(1, sim.Events<GuestDelivered>().Count, strategy + ": delivered");
                bool unlucky = Distance(sim).Any(j => j.Verdict == LuckVerdict.Unlucky);
                if (strategy == AutoPilotStrategy.LuckAware) Assert.IsFalse(unlucky, "LuckAware avoids the 4-floor ride");
                else Assert.IsTrue(unlucky, "LuckBlind goes straight there");
            }
        }
    }
}
