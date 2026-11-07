using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Lucky streak (LuckStreakChanged / IScoreQuery.LuckStreak): Lucky +1, Unlucky -> 0 (event only if it was above 0),
    /// Neutral keeps, silent reset at day start; tips x StreakTipMultiplier (1 + streak x bonus, capped) taken at payment;
    /// best streak in the day summary. The shared fixture has the bonus off, so these tests turn it on.
    /// </summary>
    public class LuckStreakTests
    {
        static void Streak(SimFixture.Defs d, float bonus = 0.1f, float max = 2f)
        {
            d.Hotel.rules.streakTipBonusPerStep = bonus;
            d.Hotel.rules.streakMaxTipMultiplier = max;
        }

        static ScoreKeeper Keeper(EventBus bus, float bonus = 0.1f, float max = 2f)
            => new ScoreKeeper(new RulesDef { startLuck = 50, maxLuck = 100, streakTipBonusPerStep = bonus, streakMaxTipMultiplier = max }, bus);

        // ------------------------------------------------------------------ ScoreKeeper rules

        [Test]
        public void Streak_LuckyVerdicts_Increment_AndPublish()
        {
            var bus = new EventBus();
            var log = new System.Collections.Generic.List<LuckStreakChanged>();
            bus.Subscribe<LuckStreakChanged>(log.Add);
            var k = Keeper(bus);
            Assert.AreEqual(0, k.LuckStreak);
            Assert.AreEqual(1f, k.StreakTipMultiplier);

            for (int i = 0; i < 3; i++) k.OnVerdict(LuckVerdict.Lucky);
            Assert.AreEqual(3, k.LuckStreak);
            Assert.AreEqual(3, k.BestStreakToday);
            Assert.AreEqual(1.3f, k.StreakTipMultiplier, 1e-5f);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, log.Select(e => e.Streak));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, log.Select(e => e.Previous));
            Assert.AreEqual(1.3f, log.Last().TipMultiplier, 1e-5f, "TipMultiplier = what the next tips get");
            Assert.IsFalse(log.Any(e => e.Broken));
        }

        [Test]
        public void Streak_NeutralVerdict_KeepsStreak_NoEvent()
        {
            var bus = new EventBus();
            int events = 0;
            bus.Subscribe<LuckStreakChanged>(_ => events++);
            var k = Keeper(bus);
            k.OnVerdict(LuckVerdict.Lucky);
            k.OnVerdict(LuckVerdict.Neutral);
            Assert.AreEqual(1, k.LuckStreak);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void Streak_UnluckyVerdict_Breaks_EventOnlyIfAboveZero()
        {
            var bus = new EventBus();
            var log = new System.Collections.Generic.List<LuckStreakChanged>();
            bus.Subscribe<LuckStreakChanged>(log.Add);
            var k = Keeper(bus);

            k.OnVerdict(LuckVerdict.Unlucky);
            Assert.IsEmpty(log, "nothing to break at 0");

            k.OnVerdict(LuckVerdict.Lucky);
            k.OnVerdict(LuckVerdict.Lucky);
            k.OnVerdict(LuckVerdict.Unlucky);
            Assert.AreEqual(0, k.LuckStreak);
            Assert.AreEqual(2, k.BestStreakToday, "best of the day survives the break");
            Assert.AreEqual(1f, k.StreakTipMultiplier);
            var broke = log.Last();
            Assert.IsTrue(broke.Broken);
            Assert.AreEqual(0, broke.Streak);
            Assert.AreEqual(2, broke.Previous);
            Assert.AreEqual(1f, broke.TipMultiplier);

            k.OnVerdict(LuckVerdict.Unlucky);
            Assert.AreEqual(3, log.Count, "second Unlucky at 0: no event");
        }

        [Test]
        public void Streak_TipMultiplier_IsCapped_AndBadDataIsClamped()
        {
            var bus = new EventBus();
            var k = Keeper(bus, bonus: 0.5f, max: 1.75f);
            k.OnVerdict(LuckVerdict.Lucky);
            Assert.AreEqual(1.5f, k.StreakTipMultiplier, 1e-5f);
            k.OnVerdict(LuckVerdict.Lucky);
            Assert.AreEqual(1.75f, k.StreakTipMultiplier, 1e-5f, "capped at streakMaxTipMultiplier");
            k.OnVerdict(LuckVerdict.Lucky);
            Assert.AreEqual(1.75f, k.StreakTipMultiplier, 1e-5f);

            Assert.AreEqual(1f, Keeper(bus, bonus: -1f).MultiplierFor(5), "negative bonus = off");
            Assert.AreEqual(1f, Keeper(bus, bonus: float.NaN).MultiplierFor(5), "NaN bonus = off");
            Assert.AreEqual(1f, Keeper(bus, bonus: 0.5f, max: 0.5f).MultiplierFor(5), "max below 1 = 1 (never lowers tips)");
            Assert.AreEqual(1f, Keeper(bus, bonus: 0.5f, max: float.NaN).MultiplierFor(5));
            Assert.AreEqual(1f, Keeper(bus, bonus: 0f).MultiplierFor(5), "0 = off");
        }

        [Test]
        public void Streak_BeginDay_ResetsSilently()
        {
            var bus = new EventBus();
            int events = 0;
            bus.Subscribe<LuckStreakChanged>(_ => events++);
            var k = Keeper(bus);
            k.OnVerdict(LuckVerdict.Lucky);
            k.OnVerdict(LuckVerdict.Lucky);
            events = 0;
            k.BeginDay();
            Assert.AreEqual(0, k.LuckStreak);
            Assert.AreEqual(0, k.BestStreakToday);
            Assert.AreEqual(0, events, "day start resets without an event");
        }

        // ------------------------------------------------------------------ session

        [Test]
        public void Streak_LuckyFloor_PublishedRightAfterLuckJudged_TipMultipliedAtPayment()
        {
            var sim = Running(d => Streak(d));
            var g = sim.Spawn("granny", 1, 3);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();

            int judged = sim.IndexOf<LuckJudged>();
            Assert.AreEqual(judged + 1, sim.IndexOf<LuckStreakChanged>(), "LuckStreakChanged right after the LuckJudged");
            var e = sim.Events<LuckStreakChanged>().Single();
            Assert.AreEqual(1, e.Streak);
            Assert.AreEqual(1.1f, e.TipMultiplier, 1e-5f);
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            Assert.AreEqual(22, sim.Events<GuestDelivered>().Single(d => d.Guest == g).Tip, "10 x lucky floor 2 x streak 1.1");
            Assert.AreEqual(22, sim.S.Score.Tips);
            Assert.AreEqual(22, sim.S.Score.TipsEarnedToday);
        }

        [Test]
        public void Streak_BrokenBeforePayment_CostsTheBonus()
        {
            var sim = Running(d => Streak(d));
            var granny = sim.Spawn("granny", 1, 3);
            var biz = sim.Spawn("biz", 1, 5);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open(); // granny lucky at 3 (streak 1), steps out
            Assert.AreEqual(22, sim.Events<GuestDelivered>().Single(d => d.Guest == granny).Tip);
            sim.Close();
            sim.GoTo(4);
            sim.Open(); // biz: 4 is unlucky -> streak broken
            Assert.AreEqual(0, sim.S.Score.LuckStreak);
            int unlucky = sim.IndexOf<LuckJudged>(j => j.Verdict == LuckVerdict.Unlucky);
            Assert.AreEqual(unlucky + 1, sim.IndexOf<LuckStreakChanged>(s => s.Broken));
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            Assert.AreEqual(20, sim.Events<GuestDelivered>().Single(d => d.Guest == biz).Tip, "streak 0 at payment: base 20 only");
        }

        [Test]
        public void Streak_Party_CountsEachVerdict_BestStreakInSummary()
        {
            var sim = Running(d => Streak(d));
            sim.Spawn("couple", 1, 7);
            sim.Open();
            sim.Close();
            sim.GoTo(7);
            sim.Open();
            Assert.AreEqual(2, sim.S.Score.LuckStreak, "two riders judged lucky = two steps");
            Assert.IsTrue(sim.Events<GuestDelivered>().All(d => d.Tip == 24), "10 x 2 x 1.2");

            sim.Close();
            sim.Spawn("granny", 7, 4);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            sim.Open(); // granny: 4 unlucky
            Assert.AreEqual(0, sim.S.Score.LuckStreak);
            Assert.AreEqual(2, sim.S.Score.BestStreakToday);

            sim.S.EndDayNow();
            Assert.AreEqual(2, sim.Events<DayEnded>().Single().Summary.BestStreak);
            Assert.AreEqual(2, sim.S.LastSummary.BestStreak);
        }

        [Test]
        public void Streak_NeutralRide_KeepsStreak()
        {
            var sim = Running(d => Streak(d));
            sim.Spawn("granny", 1, 3);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            sim.Close();
            sim.Spawn("plain", 3, 6); // lucky 99 only: every verdict Neutral
            sim.Open();
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            Assert.AreEqual(1, sim.Events<LuckStreakChanged>().Count);
            Assert.AreEqual(11, sim.Events<GuestDelivered>().Last().Tip, "neutral guest still gets the running streak: 10 x 1.1");
        }

        [Test]
        public void Streak_NextDay_StartsAtZeroSilently()
        {
            var sim = Running(d => Streak(d));
            sim.Spawn("granny", 1, 3);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.FinishShopping());
            int before = sim.Events<LuckStreakChanged>().Count;
            Assert.IsTrue(sim.S.BeginDay());
            Assert.AreEqual(0, sim.S.Score.LuckStreak);
            Assert.AreEqual(0, sim.S.Score.BestStreakToday);
            Assert.AreEqual(1f, sim.S.Score.StreakTipMultiplier);
            Assert.AreEqual(before, sim.Events<LuckStreakChanged>().Count, "no event for the day-start reset");
        }

        [Test]
        public void Streak_HonestStickerReaction_BreaksStreak()
        {
            var sim = Running(d => { Streak(d); d.Hotel.rules.startTips = 100; });
            sim.EndDayToShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId("any_to_8"), F(5)));
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            sim.Spawn("granny", 1, 3);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            sim.Close();
            sim.Spawn("honest", 3, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open(); // relabelled floor: honest guest's sticker penalty is an Unlucky verdict
            Assert.AreEqual(0, sim.S.Score.LuckStreak);
            Assert.IsTrue(sim.Events<LuckStreakChanged>().Last().Broken);
        }

        [Test]
        public void Streak_WorksOnTutorialDay()
        {
            var sim = new Sim(SimFixture.WithTutorial(new[] { new SpawnDef { time = 0f, guest = "granny", floor = 1, destinationFloor = 3 } }, d => Streak(d)));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.IsTutorial);
            Assert.IsTrue(sim.S.BeginDay());
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();
            Assert.AreEqual(1, sim.S.Score.LuckStreak);
            Assert.AreEqual(22, sim.Events<GuestDelivered>().Single().Tip);
            sim.S.EndDayNow();
            Assert.AreEqual(1, sim.S.LastSummary.BestStreak);
        }
    }
}
