using System.Linq;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// GameSession debug entry points (GameSession.Debug.cs): state after each cheat and the normal events it publishes.
    /// Fixture: "Day 0" tutorial (internal day 1) + 3 real days (display 1..3) with a shop after each, startLuck 50/100,
    /// prosperous at 70, 1000 start tips.
    /// </summary>
    public class DebugCheatTests
    {
        static readonly string[] Pool = { "four_to_f", "cat" };
        static readonly FloorIndex Fourth = StoryRules.RealFourthFloor;

        static GameData Data()
        {
            return SimFixture.Create(d =>
            {
                d.Hotel.rules.startTips = 1000;
                d.Hotel.rules.shopOfferCount = 99;
                d.Hotel.rules.maxRerolls = 3;
                d.Hotel.rules.rerollBaseCost = 20;
                d.Hotel.rules.rerollCostMultiplier = 2f;
                d.Stickers.stickers = new[]
                {
                    new StickerDef { id = "four_to_f", name = "four_to_f", from = "4", to = "F", price = 20, stock = 1 },
                    new StickerDef { id = "cat", name = "cat", kind = StickerKinds.Charm, effect = StickerEffects.Tips, amount = 0.5f, price = 10, stock = 1 },
                };
                d.Days.days = new[]
                {
                    new DayDef { day = 1, title = "Day 0", durationSeconds = 30f, tutorial = true, shopStickers = new string[0], spawns = new SpawnDef[0] },
                    new DayDef { day = 2, durationSeconds = 1000f, shopStickers = Pool },
                    new DayDef { day = 3, durationSeconds = 1000f, shopStickers = Pool },
                    new DayDef { day = 4, durationSeconds = 1000f, shopStickers = new string[0] },
                };
            });
        }

        /// <summary>Display day 1 running (tutorial skipped).</summary>
        static Sim RunningDay1()
        {
            var sim = new Sim(Data());
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.SkipTutorial());
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        /// <summary>Display day 2 running with the 4th floor stickered to F and the tips charm active.</summary>
        static Sim RunningDay2WithPurchases()
        {
            var sim = RunningDay1();
            sim.EndDayToShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId("four_to_f"), Fourth));
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(new StickerId("cat")));
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            Assert.AreEqual(2, sim.S.DisplayDay);
            Assert.AreEqual("F", sim.S.Building.GetLabel(Fourth).Text);
            Assert.AreEqual(1, sim.S.ActiveCharms.Count);
            return sim;
        }

        // ------------------------------------------------------------------ jump to day

        [Test]
        public void JumpToDay_FromRunning_GoesToIntro_KeepsLuckAndTips_ResetsLabelsCharmsGuests()
        {
            var sim = RunningDay2WithPurchases();
            var s = sim.S;
            sim.Spawn("plain", 3, 6);
            s.DebugAddLuck(7);
            int luck = s.Score.Luck, tips = s.Score.Tips;
            sim.Log.Clear();

            Assert.IsTrue(s.DebugJumpToDay(1));

            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(1, s.DisplayDay);
            Assert.AreEqual(2, s.CurrentDay.Number);
            Assert.AreEqual(luck, s.Score.Luck, "Luck kept");
            Assert.AreEqual(tips, s.Score.Tips, "tips kept");
            Assert.AreEqual("4", s.Building.GetLabel(Fourth).Text, "stickered floor back to its label");
            Assert.IsEmpty(s.ActiveCharms, "charms cleared");
            Assert.IsEmpty(s.Guests.ActiveGuests, "guests dropped");
            Assert.AreEqual(EndingKind.None, s.Ending);
            Assert.AreEqual(0f, s.ElapsedSeconds);
            Assert.IsTrue(sim.Events<GuestLeft>().All(e => e.Reason == GuestLeaveReason.DayEnded));
            Assert.AreEqual(1, sim.Events<GuestLeft>().Count);
            Assert.IsEmpty(sim.Events<DayEnded>(), "an interrupted day publishes no DayEnded");
            sim.AssertOrder(typeof(GuestLeft), typeof(FloorLabelChanged), typeof(GameStarted), typeof(DayPhaseChanged));
            var started = sim.Events<GameStarted>().Single();
            Assert.AreEqual(luck, started.StartLuck);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Running, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);
            Assert.AreEqual(2, phase.Day.Number);

            Assert.IsTrue(s.BeginDay(), "the jumped-to day plays normally");
            Assert.AreEqual(DayPhase.Running, s.Phase);
        }

        [Test]
        public void JumpToDay_Zero_IsTheTrainingShift()
        {
            var sim = RunningDay1();
            Assert.IsTrue(sim.S.DebugJumpToDay(0));
            Assert.IsTrue(sim.S.IsTutorial);
            Assert.AreEqual(0, sim.S.DisplayDay);
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
        }

        [Test]
        public void JumpToDay_FromTitle_StartsAFreshRunAtThatDay()
        {
            var sim = new Sim(Data());
            var s = sim.S;
            s.ReturnToTitle();
            for (int i = 0; i < 100; i++) s.Tick(0.05f);
            sim.Log.Clear();

            Assert.IsTrue(s.DebugJumpToDay(3));

            Assert.IsFalse(s.IsOnTitle);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(3, s.DisplayDay);
            Assert.AreEqual(50, s.Score.Luck, "start Luck, not the demo's");
            Assert.AreEqual(1000, s.Score.Tips);
            Assert.IsTrue(s.BeginDay(), "the player owns the run now");
            var phase = sim.Events<DayPhaseChanged>().First();
            Assert.AreEqual(DayPhase.Title, phase.Previous);
            Assert.AreEqual(DayPhase.Intro, phase.Current);
            Assert.AreEqual(1, sim.Events<GameStarted>().Count);
        }

        [Test]
        public void JumpToDay_FromEnding_ClearsTheEnding()
        {
            var sim = RunningDay1();
            var s = sim.S;
            Assert.IsTrue(s.DebugForceEnding(EndingKind.Prosperous));
            sim.Log.Clear();

            Assert.IsTrue(s.DebugJumpToDay(2));

            Assert.AreEqual(EndingKind.None, s.Ending);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(2, s.DisplayDay);
            var phase = sim.Events<DayPhaseChanged>().Single();
            Assert.AreEqual(DayPhase.Ending, phase.Previous);
            Assert.AreEqual(1, sim.Events<GameStarted>().Count, "GameStarted lets every view drop the ending screen");
        }

        [Test]
        public void JumpToDay_WhilePausedOrInShop_Works()
        {
            var sim = RunningDay1();
            var s = sim.S;
            Assert.IsTrue(s.SetPaused(true));
            Assert.IsTrue(s.DebugJumpToDay(3));
            Assert.IsFalse(s.IsPaused);
            Assert.AreEqual(3, s.DisplayDay);

            Assert.IsTrue(s.DebugJumpToDay(1));
            Assert.IsTrue(s.BeginDay());
            sim.EndDayToShop();
            Assert.IsTrue(s.DebugJumpToDay(2));
            Assert.IsFalse(s.IsOpen, "shop closed");
            Assert.IsEmpty(s.Offers);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
        }

        [Test]
        public void JumpToDay_OutOfRange_DoesNothing()
        {
            var sim = RunningDay1();
            sim.Log.Clear();
            Assert.IsFalse(sim.S.DebugJumpToDay(-1));
            Assert.IsFalse(sim.S.DebugJumpToDay(4));
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
            Assert.IsEmpty(sim.Log);
        }

        // ------------------------------------------------------------------ endings

        [Test]
        public void ForceEnding_Haunted_WhileRunning_FollowsTheRealPath()
        {
            var sim = RunningDay2WithPurchases();
            var s = sim.S;
            sim.Spawn("plain", 3, 6);
            sim.Log.Clear();

            Assert.IsTrue(s.DebugForceEnding(EndingKind.Haunted));

            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.AreEqual(EndingKind.Haunted, s.Ending);
            Assert.AreEqual(0, s.Score.Luck);
            Assert.AreEqual("4", s.Building.GetLabel(Fourth).Text, "one-day purchases expired");
            sim.AssertOrder(typeof(LuckChanged), typeof(GuestLeft), typeof(DayEnded), typeof(FloorLabelChanged),
                typeof(StickersExpired), typeof(DayPhaseChanged), typeof(EndingReached));
            var e = sim.Events<EndingReached>().Single();
            Assert.AreEqual(EndingKind.Haunted, e.Ending);
            Assert.AreEqual(0, e.FinalLuck);
            Assert.AreEqual(2 + 1, e.Day.Number, "Haunted happens on the current day");
        }

        [Test]
        public void ForceEnding_Prosperous_FromIntro_RaisesLuck_OnTheLastDay()
        {
            var sim = new Sim(Data());
            sim.S.StartNewGame();
            sim.Log.Clear();

            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Prosperous));

            Assert.AreEqual(70, sim.S.Score.Luck, "at least the prosperity threshold");
            Assert.IsEmpty(sim.Events<DayEnded>(), "no day was running");
            var e = sim.Events<EndingReached>().Single();
            Assert.AreEqual(EndingKind.Prosperous, e.Ending);
            Assert.AreEqual(4, e.Day.Number, "Luck endings come on the last day");
            Assert.AreEqual(70, e.FinalLuck);
            Assert.AreEqual(1000, e.TotalTips);
            sim.AssertOrder(typeof(LuckChanged), typeof(DayPhaseChanged), typeof(EndingReached));
            Assert.AreEqual(DayPhase.Ending, sim.Events<DayPhaseChanged>().Single().Current);
        }

        [Test]
        public void ForceEnding_Modest_KeepsLuckBelowTheThreshold()
        {
            var sim = RunningDay1();
            var s = sim.S;
            s.DebugAddLuck(40); // 90
            Assert.IsTrue(s.DebugForceEnding(EndingKind.Modest));
            Assert.AreEqual(69, s.Score.Luck);
            Assert.AreEqual(EndingKind.Modest, sim.Events<EndingReached>().Single().Ending);
            Assert.AreEqual(1, sim.Events<DayEnded>().Count, "the running day ended first");
        }

        [Test]
        public void ForceEnding_FourthFloor_FromTitle_StartsARunFirst_LuckUnchanged()
        {
            var sim = new Sim(Data());
            sim.S.ReturnToTitle();
            sim.Log.Clear();

            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.FourthFloor));

            Assert.IsFalse(sim.S.IsOnTitle);
            Assert.AreEqual(50, sim.S.Score.Luck);
            sim.AssertOrder(typeof(GameStarted), typeof(DayPhaseChanged), typeof(DayPhaseChanged), typeof(EndingReached));
            Assert.AreEqual(EndingKind.FourthFloor, sim.Events<EndingReached>().Single().Ending);
        }

        [Test]
        public void ForceEnding_AgainFromAnEnding_RecordsTheNewOne()
        {
            var sim = RunningDay1();
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.FourthFloor));
            Assert.IsTrue(sim.S.DebugForceEnding(EndingKind.Haunted));
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.AreEqual(2, sim.Events<EndingReached>().Count);
        }

        [Test]
        public void ForceEnding_None_IsRefused()
        {
            var sim = RunningDay1();
            Assert.IsFalse(sim.S.DebugForceEnding(EndingKind.None));
            Assert.IsFalse(sim.S.DebugForceEnding((EndingKind)99));
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
        }

        // ------------------------------------------------------------------ tips / luck

        [Test]
        public void AddTips_PublishesTipsChanged_NotCountedAsEarned()
        {
            var sim = RunningDay1();
            sim.Log.Clear();
            Assert.IsTrue(sim.S.DebugAddTips(100));
            Assert.IsTrue(sim.S.DebugAddTips(1000));
            Assert.AreEqual(2100, sim.S.Score.Tips);
            Assert.AreEqual(0, sim.S.Score.TipsEarnedToday);
            var t = sim.Events<TipsChanged>();
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual(1000, t[0].Previous);
            Assert.AreEqual(1100, t[0].Current);
        }

        [Test]
        public void AddLuck_ClampsAndPublishes()
        {
            var sim = RunningDay1();
            var s = sim.S;
            sim.Log.Clear();
            Assert.AreEqual(10, s.DebugAddLuck(10));
            Assert.AreEqual(40, s.DebugAddLuck(60), "clamped at max");
            Assert.AreEqual(100, s.Score.Luck);
            Assert.AreEqual(0, s.DebugAddLuck(10), "already full: nothing");
            Assert.AreEqual(-10, s.DebugAddLuck(-10));
            var l = sim.Events<LuckChanged>();
            Assert.AreEqual(3, l.Count);
            Assert.IsTrue(l.All(e => e.Source == LuckSource.Other));
            Assert.AreEqual(90, l[2].Current);
            Assert.AreEqual(DayPhase.Running, s.Phase);
        }

        [Test]
        public void AddLuck_ToZero_WhileRunning_HauntsTheHotel()
        {
            var sim = RunningDay1();
            var s = sim.S;
            Assert.IsTrue(s.SetPaused(true));
            s.DebugAddLuck(-60);
            Assert.AreEqual(0, s.Score.Luck);
            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.AreEqual(EndingKind.Haunted, s.Ending);
            Assert.IsFalse(s.IsPaused);
            sim.AssertOrder(typeof(LuckChanged), typeof(DayEnded), typeof(EndingReached));
        }

        [Test]
        public void AddLuck_ToZero_OutsideARealRunningDay_NoEnding()
        {
            var sim = new Sim(Data());
            var s = sim.S;
            s.StartNewGame(); // tutorial Intro
            s.DebugAddLuck(-100);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.IsTrue(s.BeginDay());
            s.DebugAddLuck(-10);
            Assert.AreEqual(DayPhase.Running, s.Phase, "no Haunted on the training shift");
            Assert.AreEqual(EndingKind.None, s.Ending);
        }

        [Test]
        public void Cheats_OnTitle_DoNothing()
        {
            var sim = new Sim(Data());
            var s = sim.S;
            s.ReturnToTitle();
            sim.Log.Clear();
            Assert.IsFalse(s.DebugAddTips(100));
            Assert.AreEqual(0, s.DebugAddLuck(-10));
            Assert.IsFalse(s.DebugEndDay());
            Assert.IsTrue(s.DebugSpawnMystery().IsNone);
            Assert.IsFalse(s.DebugRefillRerolls());
            Assert.IsEmpty(sim.Events<TipsChanged>());
            Assert.IsEmpty(sim.Events<LuckChanged>());
            Assert.AreEqual(DayPhase.Title, s.Phase);
        }

        // ------------------------------------------------------------------ extras

        [Test]
        public void RefillRerolls_InShop_RestoresEveryReroll()
        {
            var sim = RunningDay1();
            var s = sim.S;
            Assert.IsFalse(s.DebugRefillRerolls(), "shop closed");
            sim.EndDayToShop();
            Assert.IsTrue(s.Reroll());
            Assert.IsTrue(s.Reroll());
            Assert.AreEqual(1, s.RerollsLeft);
            sim.Log.Clear();

            Assert.IsTrue(s.DebugRefillRerolls());

            Assert.AreEqual(3, s.RerollsLeft);
            Assert.AreEqual(20, s.RerollCost);
            var e = sim.Events<ShopRerolled>().Single();
            Assert.AreEqual(0, e.Cost);
            Assert.AreEqual(3, e.RerollsLeft);
            Assert.AreEqual(20, e.NextCost);
            Assert.IsEmpty(sim.Events<TipsChanged>());
        }

        [Test]
        public void SpawnMystery_WhileRunning_WaitsInTheLobby_ForTheReal4th()
        {
            var sim = RunningDay1();
            var id = sim.S.DebugSpawnMystery();
            Assert.IsFalse(id.IsNone);
            var e = sim.Events<GuestSpawned>().Single();
            Assert.AreEqual("mystery", e.Archetype.Value);
            Assert.AreEqual(new FloorIndex(0), e.Floor);
            Assert.AreEqual(Fourth, e.DestinationFloor);

            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.DebugSpawnMystery().IsNone, "only while a day runs");
        }

        [Test]
        public void EndDay_WhileRunning_GoesToSummary()
        {
            var sim = RunningDay1();
            Assert.IsTrue(sim.S.DebugEndDay());
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.AreEqual(1, sim.Events<DayEnded>().Count);
            Assert.IsFalse(sim.S.DebugEndDay(), "not running any more");
        }
    }
}
