using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Star night (IDayQuery.IsStarNight: 7 Lucky for everyone from every source) and wish stars (DayDef.wishStars,
    /// IGameFlow.MakeWish, WishStarAppeared / WishMade / WishStarFaded). Fixture: 1 s per floor, instant doors and boarding;
    /// RulesDef defaults wishStarSeconds 3, wishLuckBonus 4.
    /// </summary>
    public class StarNightTests
    {
        const float Seconds = 3f;
        const int Bonus = 4;

        static GuestLineDef Line(string trigger) => new GuestLineDef { trigger = trigger, text = trigger, textKo = "ko:" + trigger };

        static GuestDef Talker(string id, int[] lucky, int[] unlucky, string[] traits = null) => new GuestDef
        {
            id = id, lucky = lucky, unlucky = unlucky, patienceSeconds = 1000f, baseTip = 10, partySize = 1,
            traits = traits ?? new string[0], color = "#FFFFFF",
            lines = new[]
            {
                Line(GuestLineTriggers.LuckyFloor), Line(GuestLineTriggers.UnluckyFloor), Line(GuestLineTriggers.LuckyOccupancy),
                Line(GuestLineTriggers.UnluckyOccupancy), Line(GuestLineTriggers.LuckyDistance), Line(GuestLineTriggers.UnluckyDistance),
            },
        };

        /// <summary>"hater7": lucky 99, unlucky 7. "seven": lucky 7 (own). "gambler99": gambler with lucky 99 only.</summary>
        static void Guests(SimFixture.Defs d)
        {
            var list = d.Guests.guests.ToList();
            list.Add(Talker("hater7", new[] { 99 }, new[] { 7 }));
            list.Add(Talker("seven", new[] { 7 }, new int[0]));
            list.Add(Talker("gambler99", new[] { 99 }, new int[0], new[] { GuestTraits.Gambler }));
            d.Guests.guests = list.ToArray();
            d.Hotel.rules.deliveryLuckBonus = 0;
        }

        /// <summary>Real day 1 is a star night (100 s, <paramref name="stars"/> wish stars); day 2 is plain.</summary>
        static Sim StarNight(int stars = 4, int seed = 1, Action<SimFixture.Defs> tweak = null)
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                Guests(d);
                d.Days.days[0].starNight = true;
                d.Days.days[0].wishStars = stars;
                d.Days.days[0].durationSeconds = 100f;
                tweak?.Invoke(d);
            }), seed);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        static Sim PlainNight() => Running(Guests);

        static List<LuckJudged> Judged(Sim sim, LuckSource source) => sim.Events<LuckJudged>().Where(e => e.Source == source).ToList();

        static List<string> Said(Sim sim) => sim.Events<GuestSpoke>().Select(e => e.LineTrigger).ToList();

        /// <summary>Runs the clock until the first wish star is up; returns its event.</summary>
        static WishStarAppeared FirstStar(Sim sim)
        {
            sim.RunUntil(() => sim.Events<WishStarAppeared>().Count > 0);
            return sim.Events<WishStarAppeared>()[0];
        }

        // ================================================================================== lucky 7 for everyone

        [Test]
        public void StarNight_Flag_FromDayDef()
        {
            Assert.IsTrue(StarNight().S.IsStarNight);
            Assert.IsFalse(PlainNight().S.IsStarNight);
        }

        [Test]
        public void StarNightRules_AddSeven_RemoveUnluckySeven()
        {
            var p = StarNightRules.Apply(LuckProfile.Of(new[] { 3 }, new[] { 4, 7 }));
            Assert.AreEqual(LuckVerdict.Lucky, p.Judge(7));
            Assert.AreEqual(LuckVerdict.Lucky, p.Judge(3));
            Assert.AreEqual(LuckVerdict.Unlucky, p.Judge(4));
            var own = LuckProfile.Of(new[] { 7 }, new int[0]);
            Assert.AreSame(own, StarNightRules.Apply(own), "already lucky 7: unchanged");
            Assert.AreEqual(LuckVerdict.Lucky, StarNightRules.Apply(null).Judge(7));
        }

        [Test]
        public void StarNight_FloorSeven_LuckyForAll_EvenOwnUnluckySeven()
        {
            var sim = StarNight(0);
            var plain = sim.Spawn("plain", 1, 9);
            var hater = sim.Spawn("hater7", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(7);
            sim.Open();
            var floor = Judged(sim, LuckSource.FloorLabel);
            Assert.AreEqual(2, floor.Count);
            Assert.IsTrue(floor.All(e => e.Verdict == LuckVerdict.Lucky && e.Number == 7), "7 is lucky for both");
            CollectionAssert.AreEquivalent(new[] { plain, hater }, floor.Select(e => e.Guest));
            CollectionAssert.DoesNotContain(Said(sim), GuestLineTriggers.UnluckyFloor, "the star night wins over the own unlucky 7");
            CollectionAssert.DoesNotContain(Said(sim), GuestLineTriggers.LuckyFloor, "no own-number line for a star-made 7");
        }

        [Test]
        public void StarNight_OwnLuckySeven_StillSaysItsLine()
        {
            var sim = StarNight(0);
            sim.Spawn("seven", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(7);
            sim.Open();
            Assert.AreEqual(LuckVerdict.Lucky, Judged(sim, LuckSource.FloorLabel).Single().Verdict);
            CollectionAssert.Contains(Said(sim), GuestLineTriggers.LuckyFloor);
        }

        [Test]
        public void PlainNight_FloorSeven_StaysOwnVerdict()
        {
            var sim = PlainNight();
            sim.Spawn("plain", 1, 9);
            sim.Spawn("hater7", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(7);
            sim.Open();
            var floor = Judged(sim, LuckSource.FloorLabel);
            Assert.AreEqual(1, floor.Count, "plain is neutral, hater7 unlucky");
            Assert.AreEqual(LuckVerdict.Unlucky, floor[0].Verdict);
        }

        [Test]
        public void StarNight_SevenRiders_LuckyHeadcountForAll()
        {
            var sim = StarNight(0);
            for (int i = 0; i < 6; i++) sim.Spawn("plain", 1, 6);
            sim.Spawn("hater7", 1, 6);
            sim.Open();
            sim.Close();
            var heads = Judged(sim, LuckSource.Occupancy);
            Assert.AreEqual(7, heads.Count, "everyone reacts to 7 riders");
            Assert.IsTrue(heads.All(e => e.Verdict == LuckVerdict.Lucky && e.Number == 7));
            CollectionAssert.DoesNotContain(Said(sim), GuestLineTriggers.LuckyOccupancy, "star-made: no own-number line");
        }

        [Test]
        public void StarNight_SevenFloorRide_LuckyDistanceForAll()
        {
            var sim = StarNight(0);
            var g = sim.Spawn("hater7", 1, 8);
            sim.Open();
            sim.Close();
            sim.GoTo(8);
            sim.Open();
            var dist = Judged(sim, LuckSource.RideDistance).Single();
            Assert.AreEqual(g, dist.Guest);
            Assert.AreEqual(7, dist.Number);
            Assert.AreEqual(LuckVerdict.Lucky, dist.Verdict);
            CollectionAssert.DoesNotContain(Said(sim), GuestLineTriggers.UnluckyDistance);
            CollectionAssert.DoesNotContain(Said(sim), GuestLineTriggers.LuckyDistance, "star-made: no own-number line");
        }

        [Test]
        public void StarNight_GuestInfo_KeepsOwnNumbers()
        {
            var sim = StarNight(0);
            var g = sim.Spawn("hater7", 1, 8);
            Assert.IsTrue(sim.S.Guests.TryGet(g, out var info));
            Assert.AreEqual(LuckVerdict.Unlucky, info.Luck.Judge(7), "bubbles show the guest's own numbers");
        }

        [Test]
        public void StarNight_Gambler_RidesWithSeven()
        {
            var sim = StarNight(0);
            for (int i = 0; i < 6; i++) sim.Spawn("plain", 1, 6);
            var gambler = sim.Spawn("gambler99", 1, 6);
            sim.Open();
            Assert.IsTrue(sim.S.Guests.TryGet(gambler, out var info));
            Assert.AreEqual(GuestState.Riding, info.State, "7 riders count as lucky for the gambler tonight");
        }

        // ================================================================================== wish star schedule

        [Test]
        public void WishStars_Schedule_SeededAndSpread()
        {
            var a = StarNight(5, seed: 3).S.PendingWishStarTimes.ToArray();
            var b = StarNight(5, seed: 3).S.PendingWishStarTimes.ToArray();
            CollectionAssert.AreEqual(a, b, "same seed, same sky");
            Assert.AreEqual(5, a.Length);
            float start = GameSession.WishStarMarginSeconds, end = 100f - GameSession.WishStarMarginSeconds - Seconds;
            float slice = (end - start) / 5f;
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(a[i], Is.InRange(start + slice * i, start + slice * (i + 1)), "star " + i + " inside its slice");
                if (i > 0) Assert.Greater(a[i], a[i - 1]);
            }
            bool differs = Enumerable.Range(10, 5).Any(seed => !StarNight(5, seed).S.PendingWishStarTimes.SequenceEqual(a));
            Assert.IsTrue(differs, "other seeds, other skies");
        }

        [Test]
        public void WishStars_NoneWithoutWishStars_NorOnTutorialDays()
        {
            Assert.IsEmpty(PlainNight().S.PendingWishStarTimes);
            var t = new Sim(SimFixture.WithTutorial(new[] { new SpawnDef { time = 0f, guest = "plain", floor = 3, destinationFloor = 6 } },
                d => d.Days.days[0].wishStars = 3));
            t.S.StartNewGame();
            Assert.IsTrue(t.S.BeginDay());
            Assert.IsTrue(t.S.IsTutorial);
            Assert.IsEmpty(t.S.PendingWishStarTimes, "the training shift has no clock, so no stars");
        }

        [Test]
        public void WishStar_Appears_OnTheDayClock_InTheUpperSky()
        {
            var sim = StarNight(5, seed: 7);
            var times = sim.S.PendingWishStarTimes.ToArray();
            sim.RunUntil(() => sim.Events<WishStarAppeared>().Count == 5, 200f);
            var stars = sim.Events<WishStarAppeared>();
            Assert.AreEqual(5, stars.Select(s => s.Id).Distinct().Count(), "unique ids");
            foreach (var s in stars)
            {
                Assert.AreEqual(Seconds, s.Seconds, 1e-4f);
                foreach (var x in new[] { s.FromX, s.ToX }) Assert.That(x, Is.InRange(0.3f, 0.75f), "clear of the HUD corners");
                foreach (var y in new[] { s.FromY, s.ToY }) Assert.That(y, Is.InRange(0.6f, 1f), "upper sky");
                Assert.Less(s.ToY, s.FromY, "falls");
                Assert.GreaterOrEqual(Math.Abs(s.ToX - s.FromX), 0.2f - 1e-4f, "diagonal");
            }
            Assert.GreaterOrEqual(sim.S.ElapsedSeconds, times[4] - 1e-3f);
            Assert.Less(sim.S.ElapsedSeconds, times[4] + 0.06f, "appears on time");
        }

        // ================================================================================== MakeWish

        [Test]
        public void MakeWish_WhileCrossing_GivesLuck_Once()
        {
            var sim = StarNight();
            var star = FirstStar(sim);
            int luck = sim.S.Score.Luck;
            int from = sim.Log.Count;
            Assert.IsTrue(sim.S.MakeWish(star.Id));
            Assert.AreEqual(luck + Bonus, sim.S.Score.Luck);
            Assert.AreEqual(1, sim.S.WishesMadeToday);
            int made = sim.IndexOf<WishMade>(e => e.Id == star.Id, from);
            int changed = sim.IndexOf<LuckChanged>(e => e.Source == LuckSource.Story, from);
            Assert.GreaterOrEqual(made, 0);
            Assert.Greater(changed, made, "WishMade, then LuckChanged(Story)");
            Assert.AreEqual(Bonus, ((WishMade)sim.Log[made]).LuckGain);

            Assert.IsFalse(sim.S.MakeWish(star.Id), "twice");
            sim.Run(Seconds + 1f);
            Assert.IsFalse(sim.Events<WishStarFaded>().Any(e => e.Id == star.Id), "a wished star does not also fade");
            Assert.AreEqual(luck + Bonus, sim.S.Score.Luck);
        }

        /// <summary>Regression (playthrough bot): a restart / the title mid star night kept the old run's wish count.</summary>
        [Test]
        public void WishesMadeToday_IsZero_AfterRestartOrTitle([Values(false, true)] bool viaTitle)
        {
            var sim = StarNight();
            Assert.IsTrue(sim.S.MakeWish(FirstStar(sim).Id));
            Assert.AreEqual(1, sim.S.WishesMadeToday);
            if (viaTitle)
            {
                sim.S.ReturnToTitle();
                Assert.AreEqual(0, sim.S.WishesMadeToday, "title");
                Assert.IsTrue(sim.S.StartGameFromTitle());
            }
            else sim.S.RestartGame();
            Assert.AreEqual(0, sim.S.WishesMadeToday, "new run");
            Assert.AreEqual(0, sim.S.GetWishStarsInSky(new List<int>()));
        }

        [Test]
        public void MakeWish_UnknownOrNotYetAppeared_IsRefused()
        {
            var sim = StarNight();
            Assert.IsFalse(sim.S.MakeWish(999));
            Assert.IsFalse(sim.S.MakeWish(1), "star 1 has not appeared yet");
            Assert.IsFalse(sim.S.MakeWish(0));
            Assert.AreEqual(0, sim.Events<WishMade>().Count);
        }

        [Test]
        public void WishStar_Missed_Fades_ThenWishIsLate()
        {
            var sim = StarNight();
            var star = FirstStar(sim);
            sim.Run(Seconds - 0.2f);
            Assert.IsEmpty(sim.Events<WishStarFaded>(), "still crossing");
            sim.Run(0.3f);
            Assert.AreEqual(star.Id, sim.Events<WishStarFaded>().Single().Id, "gone after wishStarSeconds");
            int luck = sim.S.Score.Luck;
            Assert.IsFalse(sim.S.MakeWish(star.Id), "too late");
            Assert.AreEqual(luck, sim.S.Score.Luck);
        }

        [Test]
        public void MakeWish_AtFullLuck_ReportsTheRealGain()
        {
            var sim = StarNight();
            sim.S.Score.SetLuck(98, LuckSource.Other);
            var star = FirstStar(sim);
            Assert.IsTrue(sim.S.MakeWish(star.Id));
            Assert.AreEqual(2, sim.Events<WishMade>().Single().LuckGain);
            Assert.AreEqual(100, sim.S.Score.Luck);
        }

        [Test]
        public void WishStar_FreezesWhilePaused_AndWishIsRefused()
        {
            var sim = StarNight();
            var star = FirstStar(sim);
            Assert.IsTrue(sim.S.SetPaused(true));
            Assert.IsFalse(sim.S.MakeWish(star.Id), "paused");
            sim.Run(30f);
            Assert.IsEmpty(sim.Events<WishStarFaded>(), "frozen with the clock");
            Assert.IsTrue(sim.S.SetPaused(false));
            var up = new List<int>();
            Assert.AreEqual(1, sim.S.GetWishStarsInSky(up));
            Assert.AreEqual(star.Id, up[0]);
            Assert.IsTrue(sim.S.MakeWish(star.Id), "still crossing after the pause");
        }

        [Test]
        public void DayEnd_FadesTheStarInTheSky_AndDropsTheRest()
        {
            var sim = StarNight();
            var star = FirstStar(sim);
            sim.S.EndDayNow();
            var faded = sim.Events<WishStarFaded>();
            Assert.AreEqual(star.Id, faded.Single().Id, "the crossing star fades");
            Assert.Less(sim.IndexOf<WishStarFaded>(), sim.IndexOf<DayEnded>(), "before DayEnded");
            Assert.IsEmpty(sim.S.PendingWishStarTimes, "the rest never come");
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.IsFalse(sim.S.MakeWish(star.Id), "not Running");
            Assert.AreEqual(1, sim.Events<WishStarAppeared>().Count);
        }

        [Test]
        public void Restart_FadesTheStarInTheSky()
        {
            var sim = StarNight();
            var star = FirstStar(sim);
            sim.S.RestartGame();
            Assert.AreEqual(star.Id, sim.Events<WishStarFaded>().Single().Id);
            Assert.IsEmpty(sim.S.PendingWishStarTimes);
        }

        [Test]
        public void ClockOut_EveryStarEndsOnce()
        {
            var sim = StarNight(5);
            sim.RunUntil(() => sim.S.Phase != DayPhase.Running, 200f);
            var appeared = sim.Events<WishStarAppeared>().Select(e => e.Id).ToList();
            Assert.AreEqual(5, appeared.Count, "every star fits inside the day");
            CollectionAssert.AreEquivalent(appeared, sim.Events<WishStarFaded>().Select(e => e.Id), "each missed star fades once");
        }

        [Test]
        public void EarlyDayEnd_WaitsForTheLastStar()
        {
            var sim = StarNight(1, tweak: d => d.Days.days[0].spawns = new[] { new SpawnDef { time = 0f, guest = "plain", floor = 1, destinationFloor = 2 } });
            float at = sim.S.PendingWishStarTimes.Single();
            sim.Open();
            sim.Close();
            sim.GoTo(2);
            sim.Open();
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count, "everyone delivered");
            sim.Run(5f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "a star is still to come");
            sim.RunUntil(() => sim.S.Phase != DayPhase.Running, 200f);
            Assert.GreaterOrEqual(sim.S.ElapsedSeconds, at + Seconds, "ended after the star was gone");
            Assert.Less(sim.S.ElapsedSeconds, 100f, "still an early end");
        }

        [Test]
        public void WishStarIds_UniqueAcrossRestarts()
        {
            var sim = StarNight(2);
            var first = sim.S.PendingWishStarTimes.Count();
            sim.RunUntil(() => sim.Events<WishStarAppeared>().Count == 2, 200f);
            sim.S.RestartGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.RunUntil(() => sim.Events<WishStarAppeared>().Count == 4, 200f);
            var ids = sim.Events<WishStarAppeared>().Select(e => e.Id).ToList();
            Assert.AreEqual(2, first);
            Assert.AreEqual(4, ids.Distinct().Count());
        }

        [Test]
        public void TitleDemo_RefusesWishes()
        {
            var sim = new Sim(SimFixture.Create(d => { Guests(d); d.Days.days[0].starNight = true; d.Days.days[0].wishStars = 2; d.Days.days[0].durationSeconds = 100f; }));
            sim.S.ReturnToTitle();
            sim.RunUntil(() => sim.Events<WishStarAppeared>().Count > 0, 200f);
            var star = sim.Events<WishStarAppeared>()[0];
            Assert.IsFalse(sim.S.MakeWish(star.Id), "the player cannot wish on the title");
        }

        // ================================================================================== last day + bot

        [Test]
        public void StarNight_LastDay_WishesCountTowardTheEnding()
        {
            var sim = new Sim(SimFixture.Create(d =>
            {
                Guests(d);
                d.Days.days[1].starNight = true;
                d.Days.days[1].wishStars = 5;
                d.Days.days[1].durationSeconds = 100f;
                d.Hotel.rules.startLuck = 52;
            }));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsTrue(sim.S.IsStarNight);
            var up = new List<int>();
            sim.RunUntil(() =>
            {
                if (sim.S.GetWishStarsInSky(up) > 0) foreach (var id in up.ToArray()) sim.S.MakeWish(id);
                return sim.S.Phase != DayPhase.Running;
            }, 200f);
            Assert.AreEqual(5, sim.S.WishesMadeToday);
            Assert.AreEqual(72, sim.S.Score.Luck, "52 + 5 x 4");
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(EndingKind.Prosperous, sim.S.Ending);
        }

        [Test]
        public void AutoPilot_LuckAware_WishesOnEveryStar_LuckBlindDoesNot()
        {
            foreach (var strategy in new[] { AutoPilotStrategy.LuckAware, AutoPilotStrategy.LuckBlind })
            {
                var sim = StarNight(4);
                var pilot = new AutoPilot(sim.S.Elevator, sim.S, sim.S.Guests, sim.S.Building, strategy);
                for (int i = 0; i < 4000 && sim.S.Phase == DayPhase.Running; i++)
                {
                    pilot.Step();
                    sim.Run(0.05f);
                }
                Assert.AreEqual(strategy == AutoPilotStrategy.LuckAware ? 4 : 0, sim.Events<WishMade>().Count, strategy.ToString());
            }
        }

        /// <summary>
        /// A rider with no numbers of its own (lucky 99) and two pickups, on floors 3 and 7: only the star night makes the
        /// 7th floor's label worth more than the nearer pickup.
        /// </summary>
        [Test]
        public void AutoPilot_LuckAware_StarNight_PrefersTheSeventhFloor([Values] bool starNight)
        {
            var sim = starNight ? StarNight(0) : PlainNight();
            sim.Spawn("plain", 1, 9);
            sim.Spawn("plain", 3, 2);
            sim.Spawn("plain", 7, 8);
            var pilot = new AutoPilot(sim.S.Elevator, sim.S, sim.S.Guests, sim.S.Building, AutoPilotStrategy.LuckAware);
            for (int i = 0; i < 400 && sim.Events<ElevatorDeparted>().Count == 0; i++)
            {
                pilot.Step();
                sim.Run(0.05f);
            }
            Assert.AreEqual(1, sim.S.Elevator.Occupancy, "picked up the lobby rider first");
            Assert.AreEqual(starNight ? F(7) : F(3), sim.Events<ElevatorDeparted>()[0].To);
        }
    }
}
