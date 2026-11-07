using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>GDD 4/5/7 rules, one test per rule. Fixture numbers: SimFixture (startLuck 50, door/board 0 s, 1 s per floor).</summary>
    public class RuleTests
    {
        // ------------------------------------------------------------------ rule 1: label on door open

        [Test]
        public void DoorOpen_OnUnluckyLabel_LowersLuck()
        {
            var sim = Running();
            var g = sim.Spawn("granny", 1, 6);
            sim.Open();
            CollectionAssert.Contains(sim.S.Elevator.Riders.ToList(), g);
            sim.Close();
            Assert.AreEqual(50, sim.S.Score.Luck, "occupancy 1 is neutral for granny");

            sim.GoTo(4);
            sim.Open();

            Assert.AreEqual(40, sim.S.Score.Luck);
            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(g, j.Guest);
            Assert.AreEqual(LuckSource.FloorLabel, j.Source);
            Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict);
            Assert.AreEqual(4, j.Number);
            Assert.AreEqual(F(4), j.Floor);
            Assert.AreEqual(GuestMood.Scared, Info(sim, g).Mood);
        }

        [Test]
        public void DoorOpen_OnLuckyLabel_MultipliesTip()
        {
            var sim = Running();
            var g = sim.Spawn("granny", 1, 3);
            sim.Open();
            sim.Close();
            sim.GoTo(3);
            sim.Open();

            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict);
            Assert.AreEqual(3, j.Number);
            var d = sim.Events<GuestDelivered>().Single();
            Assert.AreEqual(g, d.Guest);
            Assert.AreEqual(20, d.Tip, "baseTip 10 x luckyFloorTipMultiplier 2");
            Assert.AreEqual(20, sim.S.Score.Tips);
            Assert.AreEqual(50 + 2 + 1, sim.S.Score.Luck, "lucky floor bonus + delivery bonus");
        }

        [Test]
        public void PassingFloor_DoesNotJudge()
        {
            var sim = Running();
            sim.Spawn("granny", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(6);

            var passed = sim.Events<ElevatorPassedFloor>().Select(p => p.Floor.PhysicalNumber).ToList();
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, passed);
            Assert.AreEqual(0, sim.Events<LuckJudged>().Count, "passing 4 is fine");
            Assert.AreEqual(50, sim.S.Score.Luck);

            sim.Open();
            Assert.AreEqual(10, sim.Events<GuestDelivered>().Single().Tip);
            Assert.AreEqual(51, sim.S.Score.Luck);
        }

        [Test]
        public void LabelNumbers_WholeLeadingAndDigits()
        {
            var biz = LuckProfile.Of(new[] { 8, 9 }, new[] { 4 });
            Assert.AreEqual(LuckVerdict.Neutral, LabelNumbers.Judge(biz, new FloorLabel("F"), out int n));
            Assert.AreEqual(-1, n);
            Assert.AreEqual(LuckVerdict.Lucky, LabelNumbers.Judge(biz, new FloorLabel("88"), out n));
            Assert.AreEqual(8, n, "88 reads as 88 and as the digit 8");
            Assert.AreEqual(LuckVerdict.Unlucky, LabelNumbers.Judge(biz, new FloorLabel("14"), out n));
            Assert.AreEqual(4, n, "unlucky digit wins");
            var western = LuckProfile.Of(new[] { 7 }, new[] { 13 });
            Assert.AreEqual(LuckVerdict.Unlucky, LabelNumbers.Judge(western, new FloorLabel("13"), out n));
            Assert.AreEqual(LuckVerdict.Neutral, LabelNumbers.Judge(western, new FloorLabel("12A"), out n));
            Assert.AreEqual(12, n);
        }

        // ------------------------------------------------------------------ rule 2: occupancy on door close

        [Test]
        public void DoorClose_LuckyOccupancy_Bonus()
        {
            var sim = Running();
            var g = sim.Spawn("loner", 1, 6);
            sim.Open();
            sim.Close();

            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(LuckSource.Occupancy, j.Source);
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict);
            Assert.AreEqual(1, j.Number);
            Assert.AreEqual(53, sim.S.Score.Luck);

            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(15, sim.Events<GuestDelivered>().Single(d => d.Guest == g).Tip, "10 x luckyOccupancyTipMultiplier 1.5");
        }

        [Test]
        public void DoorClose_UnluckyOccupancy_Penalty()
        {
            var sim = Running();
            for (int i = 0; i < 4; i++) sim.Spawn("granny", 1, 6);
            sim.Open();
            Assert.AreEqual(4, sim.S.Elevator.Occupancy);
            sim.Close();

            var judged = sim.Events<LuckJudged>();
            Assert.AreEqual(4, judged.Count, "every rider is judged");
            Assert.IsTrue(judged.All(j => j.Source == LuckSource.Occupancy && j.Number == 4 && j.Verdict == LuckVerdict.Unlucky));
            Assert.AreEqual(50 - 4 * 5, sim.S.Score.Luck);
        }

        [Test]
        public void DoorReopen_SameFloorAndHeadcount_JudgedOnce()
        {
            var sim = Running();
            sim.Spawn("loner", 1, 6);
            sim.Open();
            sim.Close();   // headcount 1: lucky
            sim.Open();    // now INSIDE while doors open at "1": lucky label
            sim.Close();
            sim.Open();
            sim.Close();
            var judged = sim.Events<LuckJudged>();
            Assert.AreEqual(2, judged.Count, "no open/close farming: each floor / headcount once per ride");
            Assert.AreEqual(LuckSource.Occupancy, judged[0].Source);
            Assert.AreEqual(LuckSource.FloorLabel, judged[1].Source);
            Assert.AreEqual(50 + 3 + 2, sim.S.Score.Luck);
        }

        // ------------------------------------------------------------------ elevator / passengers

        [Test]
        public void Car_CapacityEight_StopsBoardingWhenFull()
        {
            var sim = Running();
            for (int i = 0; i < 10; i++) sim.Spawn("plain", 1, 6);
            sim.Open();
            sim.Run(1f);

            Assert.AreEqual(8, sim.S.Elevator.Occupancy);
            Assert.IsTrue(sim.S.Elevator.IsFull);
            Assert.AreEqual(8, sim.Events<GuestBoarded>().Count);
            Assert.AreEqual(2, sim.S.Guests.GetWaiting(F(1), new List<GuestId>()));
        }

        [Test]
        public void Party_BoardsAndExitsTogether()
        {
            var sim = Running();
            var first = sim.Spawn("couple", 1, 6);
            var spawned = sim.Events<GuestSpawned>();
            Assert.AreEqual(2, spawned.Count);
            Assert.IsFalse(spawned[0].Party.IsNone);
            Assert.AreEqual(spawned[0].Party, spawned[1].Party);

            sim.Open();
            var boarded = sim.Events<GuestBoarded>();
            Assert.AreEqual(2, boarded.Count);
            CollectionAssert.AreEquivalent(spawned.Select(s => s.Guest), boarded.Select(b => b.Guest));
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            var exited = sim.Events<GuestExited>();
            Assert.AreEqual(2, exited.Count);
            Assert.IsTrue(exited.All(e => e.AtDestination && e.Floor == F(6)));
            Assert.AreEqual(0, sim.S.Elevator.Occupancy);
            Assert.AreEqual(first, spawned[0].Guest);
        }

        [Test]
        public void Party_DoesNotSplitWhenOnlyOneSeatLeft()
        {
            var sim = Running();
            for (int i = 0; i < 7; i++) sim.Spawn("plain", 1, 6);
            sim.Open();
            sim.Spawn("couple", 1, 6);
            sim.Run(1f);
            Assert.AreEqual(7, sim.S.Elevator.Occupancy);
            Assert.AreEqual(2, sim.S.Guests.GetWaiting(F(1), new List<GuestId>()));
        }

        // ------------------------------------------------------------------ patience / Luck meter / endings

        [Test]
        public void Patience_RunsOut_GuestLeaves_LuckDrops()
        {
            var sim = Running();
            var g = sim.Spawn("impatient", 3, 6);
            sim.Run(4f);
            Assert.AreEqual(GuestMood.Impatient, Info(sim, g).Mood);
            sim.Run(2f);

            var left = sim.Events<GuestLeft>().Single();
            Assert.AreEqual(g, left.Guest);
            Assert.AreEqual(GuestLeaveReason.PatienceRanOut, left.Reason);
            Assert.AreEqual(45, sim.S.Score.Luck);
            Assert.IsFalse(sim.S.Guests.TryGet(g, out _));
            Assert.AreEqual(LuckSource.Patience, sim.Events<LuckChanged>().Last().Source);
        }

        [Test]
        public void LuckZero_HauntedEnding()
        {
            var sim = Running(d => d.Hotel.rules.startLuck = 10);
            sim.Spawn("granny", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            Assert.IsTrue(sim.S.OpenDoors());
            sim.Run(0.2f);

            Assert.AreEqual(0, sim.S.Score.Luck);
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            Assert.AreEqual(EndingKind.Haunted, sim.Events<EndingReached>().Single().Ending);
            sim.AssertOrder(typeof(LuckChanged), typeof(DayEnded), typeof(DayPhaseChanged), typeof(EndingReached));
            Assert.IsFalse(sim.S.AcceptsInput);
        }

        static void MysteryLines(SimFixture.Defs d)
        {
            d.Guests.guests.First(x => x.id == "mystery").lines = new[]
            {
                new GuestLineDef { trigger = GuestLineTriggers.Delivered, text = "m:delivered" },
                new GuestLineDef { trigger = GuestLineTriggers.Special, text = "m:special" },
            };
        }

        /// <summary>4th-floor ending (option A): only while a sticker hides the real 4th floor.</summary>
        [Test]
        public void MysteryGuest_AtPhysicalFourth_WithSticker_FourthFloorEnding()
        {
            var sim = Running(d => { d.Hotel.rules.startTips = 50; MysteryLines(d); });
            sim.EndDayToShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId("four_to_f"), F(4)));
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            Assert.IsTrue(sim.S.Building.IsRelabelled(F(4)));
            sim.Log.Clear();

            var g = sim.Spawn("mystery", 1, 4);
            Assert.IsTrue(Info(sim, g).HideNumbers);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            Assert.IsTrue(sim.S.OpenDoors());
            sim.Run(0.2f);

            Assert.AreEqual(g, sim.Events<GuestDelivered>().Single().Guest);
            var spoke = sim.Events<GuestSpoke>().Single();
            Assert.AreEqual(g, spoke.Guest);
            Assert.AreEqual(GuestLineTriggers.Special, spoke.LineTrigger, "special line instead of the delivered one");
            Assert.AreEqual("m:special", spoke.Text);
            Assert.AreEqual(EndingKind.FourthFloor, sim.Events<EndingReached>().Single().Ending);
            Assert.AreEqual(EndingKind.FourthFloor, sim.S.Ending);
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            sim.AssertOrder(typeof(GuestDelivered), typeof(GuestSpoke), typeof(DayEnded), typeof(DayPhaseChanged), typeof(EndingReached));
        }

        [Test]
        public void MysteryGuest_AtPhysicalFourth_NoSticker_NormalDelivery()
        {
            // an original "F" label is not a sticker: still just a delivery
            var sim = Running(d => { d.Hotel.floors[3].label = "F"; MysteryLines(d); });
            Assert.IsFalse(sim.S.Building.IsRelabelled(F(4)));
            var g = sim.Spawn("mystery", 1, 4);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            sim.Open();

            Assert.AreEqual(g, sim.Events<GuestDelivered>().Single().Guest);
            var spoke = sim.Events<GuestSpoke>().Single();
            Assert.AreEqual(GuestLineTriggers.Delivered, spoke.LineTrigger);
            Assert.AreEqual(0, sim.Events<EndingReached>().Count);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "the game goes on");

            // the run ends normally: last day -> Summary -> Prosperous/Modest
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(EndingKind.Modest, sim.S.Ending, "luck about 51 is below prosperousEndingLuck 70");
        }

        [Test]
        public void MysteryGuest_ElsewhereIsJustADelivery()
        {
            var sim = Running();
            sim.Spawn("mystery", 1, 5);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            Assert.AreEqual(1, sim.Events<GuestDelivered>().Count);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
        }

        // ------------------------------------------------------------------ stickers / shop

        [Test]
        public void Sticker_FourToF_ChangesLabelAndJudgement()
        {
            var sim = Running(d => d.Hotel.rules.startTips = 50);
            sim.EndDayToShop();
            var four = new StickerId("four_to_f");
            Assert.AreEqual(2, sim.S.Offers.Count);
            Assert.AreEqual(StickerApplyResult.NotApplicable, sim.S.CanBuyAndApply(four, F(5)));

            int from = sim.Log.Count;
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(four, F(4)));
            Assert.AreEqual(new FloorLabel("F"), sim.S.Building.GetLabel(F(4)));
            Assert.AreEqual(new FloorLabel("4"), sim.S.Building.GetOriginalLabel(F(4)));
            Assert.IsTrue(sim.S.Building.IsRelabelled(F(4)));
            Assert.AreEqual(30, sim.S.Score.Tips);
            Assert.That(sim.IndexOf<StickerApplied>(null, from), Is.LessThan(sim.IndexOf<FloorLabelChanged>(null, from)));
            Assert.That(sim.IndexOf<FloorLabelChanged>(null, from), Is.LessThan(sim.IndexOf<TipsChanged>(null, from)));
            Assert.AreEqual(StickerApplyResult.OutOfStock, sim.S.TryBuyAndApply(four, F(4)));

            Assert.IsTrue(sim.S.FinishShopping());
            Assert.AreEqual(2, sim.S.CurrentDay.Number);
            Assert.IsTrue(sim.S.BeginDay());
            sim.Spawn("granny", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            sim.Open();
            Assert.AreEqual(0, sim.Events<LuckJudged>().Count, "F is not a number: granny stays calm");
            Assert.AreEqual(50, sim.S.Score.Luck);
            Assert.IsTrue(sim.S.Building.TryFindFloorByLabel(new FloorLabel("F"), out var f) && f == F(4));
        }

        [Test]
        public void Shop_NotEnoughTips_Rejected()
        {
            var sim = Running();
            var four = new StickerId("four_to_f");
            Assert.AreEqual(StickerApplyResult.ShopClosed, sim.S.TryBuyAndApply(four, F(4)));
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.IsOpen);
            Assert.AreEqual(StickerApplyResult.NotEnoughTips, sim.S.CanBuyAndApply(four, F(4)));
            Assert.AreEqual(StickerApplyResult.NotEnoughTips, sim.S.TryBuyAndApply(four, F(4)));
            Assert.AreEqual(new FloorLabel("4"), sim.S.Building.GetLabel(F(4)));
            Assert.AreEqual(0, sim.S.Score.Tips);
            Assert.AreEqual(0, sim.Events<StickerApplied>().Count);
            Assert.AreEqual(StickerApplyResult.UnknownSticker, sim.S.TryBuyAndApply(new StickerId("nope"), F(4)));
        }

        [Test]
        public void HonestGuest_RelabelledFloor_Penalty()
        {
            var sim = Running(d => d.Hotel.rules.startTips = 50);
            sim.EndDayToShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId("any_to_8"), F(5)));
            sim.S.FinishShopping();
            sim.S.BeginDay();
            sim.Spawn("honest", 1, 5);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(LuckSource.Sticker, j.Source);
            Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict);
            Assert.AreEqual(50 - 5 + 1, sim.S.Score.Luck);
        }

        // ------------------------------------------------------------------ day loop

        [Test]
        public void Day_EndsAfterDuration_GoesToSummary()
        {
            var sim = Running(d => d.Days.days[0].durationSeconds = 10f);
            sim.Spawn("plain", 3, 6);
            sim.Run(9.9f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase);
            sim.Run(0.5f);

            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            Assert.AreEqual(1, sim.Events<DayEnded>().Single().Day.Number);
            sim.AssertOrder(typeof(DayEnded), typeof(DayPhaseChanged));
            Assert.AreEqual(DayPhase.Summary, sim.Events<DayPhaseChanged>().Last().Current);
            Assert.AreEqual(GuestLeaveReason.DayEnded, sim.Events<GuestLeft>().Single().Reason);
            Assert.AreEqual(50, sim.S.Score.Luck, "day-end leavers cost nothing");
            Assert.AreEqual(0, sim.S.LastSummary.Left);
            Assert.AreEqual(0f, sim.S.RemainingSeconds);
            Assert.IsFalse(sim.S.AcceptsInput);
            Assert.IsFalse(sim.S.RequestFloor(F(3)));
        }

        [Test]
        public void Day_SpawnsFromData_AtTheirTime()
        {
            var sim = Running(d => d.Days.days[0].spawns = new[]
            {
                new SpawnDef { time = 2f, guest = "granny", floor = 1, destination = "d3" },
                new SpawnDef { time = 1f, guest = "couple", floor = 2, destination = "d5" },
            });
            sim.Run(0.5f);
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count);
            sim.Run(1f);
            Assert.AreEqual(2, sim.S.Guests.ActiveGuests.Count, "couple at t=1");
            sim.Run(1f);
            Assert.AreEqual(3, sim.S.Guests.ActiveGuests.Count);
            var granny = Info(sim, sim.S.Guests.ActiveGuests[2]);
            Assert.AreEqual(F(1), granny.Floor);
            Assert.AreEqual(F(3), granny.DestinationFloor);
            Assert.AreEqual(new DestinationId("d3"), granny.Destination);
        }

        [Test]
        public void Flow_TwoDays_SummaryShopIntro_ThenEnding()
        {
            var sim = new Sim(SimFixture.Create());
            sim.S.StartNewGame();
            sim.AssertOrder(typeof(GameStarted), typeof(DayPhaseChanged));
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.IsFalse(sim.S.IsTutorial, "the rule fixture has no tutorial day (TutorialShiftTests cover it)");
            Assert.IsFalse(sim.S.ContinueFromSummary());

            Assert.IsTrue(sim.S.BeginDay());
            sim.AssertOrder(typeof(GameStarted), typeof(DayPhaseChanged), typeof(DayPhaseChanged), typeof(DayStarted));
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, sim.S.Phase, "day 1 has shop stickers");
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(2, sim.S.CurrentDay.Number);

            Assert.IsTrue(sim.S.BeginDay());
            sim.S.EndDayNow();
            Assert.IsTrue(sim.S.ContinueFromSummary(), "last day: summary -> ending");
            Assert.AreEqual(DayPhase.Ending, sim.S.Phase);
            Assert.AreEqual(EndingKind.Modest, sim.Events<EndingReached>().Single().Ending, "luck 50 < 70");

            sim.S.RestartGame();
            Assert.AreEqual(DayPhase.Intro, sim.S.Phase);
            Assert.AreEqual(1, sim.S.CurrentDay.Number);
            Assert.AreEqual(EndingKind.None, sim.S.Ending);
            Assert.AreEqual(2, sim.Events<GameStarted>().Count);
        }

        [Test]
        public void Flow_HighLuckAtLastDay_Prosperous()
        {
            var sim = Running(d => { d.Hotel.rules.startLuck = 80; d.Days.days = new[] { d.Days.days[1] }; d.Days.days[0].day = 1; });
            sim.S.EndDayNow();
            sim.S.ContinueFromSummary();
            Assert.AreEqual(EndingKind.Prosperous, sim.S.Ending);
        }

        [Test]
        public void Restart_ResetsLabelsLuckAndTips()
        {
            var sim = Running(d => d.Hotel.rules.startTips = 50);
            sim.EndDayToShop();
            sim.S.TryBuyAndApply(new StickerId("four_to_f"), F(4));
            sim.S.RestartGame();
            Assert.AreEqual(new FloorLabel("4"), sim.S.Building.GetLabel(F(4)));
            Assert.AreEqual(50, sim.S.Score.Tips);
            Assert.AreEqual(50, sim.S.Score.Luck);
        }

        // ------------------------------------------------------------------ event order

        [Test]
        public void Events_DoorSequence_Order()
        {
            var sim = Running();
            var a = sim.Spawn("tourist", 1, 7);
            sim.Open();
            sim.Close();
            var b = sim.Spawn("loner", 7, 2);
            sim.Log.Clear();

            sim.GoTo(7);
            sim.Open();
            Assert.IsTrue(sim.S.RequestFloor(F(2)), "request while open closes the doors first");
            sim.RunUntil(() => sim.S.Elevator.Motion != ElevatorMotion.Idle);

            sim.AssertOrder(
                typeof(ElevatorArrived), typeof(DoorsOpened), typeof(LuckJudged), typeof(LuckChanged),
                typeof(GuestExited), typeof(GuestDelivered), typeof(GuestBoarded),
                typeof(DoorsClosed), typeof(LuckJudged), typeof(LuckChanged), typeof(ElevatorDeparted));
            var judged = sim.Events<LuckJudged>();
            Assert.AreEqual(2, judged.Count);
            Assert.AreEqual(a, judged[0].Guest);
            Assert.AreEqual(LuckSource.FloorLabel, judged[0].Source);
            Assert.AreEqual(b, judged[1].Guest);
            Assert.AreEqual(LuckSource.Occupancy, judged[1].Source);
            Assert.That(sim.IndexOf<GuestDelivered>(), Is.LessThan(sim.IndexOf<TipsChanged>()));
            var dep = sim.Events<ElevatorDeparted>().Last();
            Assert.AreEqual(F(7), dep.From);
            Assert.AreEqual(F(2), dep.To);
        }

        [Test]
        public void Events_GameStart_PublishedOnlyOnStart()
        {
            var sim = new Sim(SimFixture.Create());
            Assert.AreEqual(0, sim.Log.Count, "constructing publishes nothing (Gameplay starts after every Start)");
            sim.S.StartNewGame();
            Assert.IsInstanceOf<GameStarted>(sim.Log[0]);
            var p = (DayPhaseChanged)sim.Log[1];
            Assert.AreEqual(DayPhase.None, p.Previous);
            Assert.AreEqual(DayPhase.Intro, p.Current);
        }

        // ------------------------------------------------------------------ locator / timing

        [Test]
        public void ActorLocator_WaitingAndRidingPositions()
        {
            var data = SimFixture.Create(d => { d.Hotel.rules.doorSeconds = 0.5f; d.Hotel.rules.boardSeconds = 0.5f; });
            var sim = new Sim(data);
            sim.S.StartNewGame();
            sim.S.BeginDay();
            var layout = HotelLayout.From(data.Hotel);
            var loc = new ActorLocator(sim.S, layout);
            var g = sim.Spawn("plain", 1, 3);
            Assert.IsTrue(loc.TryGetGuestPosition(g, out var p));
            Assert.AreEqual(layout.GetWaitingSlot(F(1), 0).X, p.X, 1e-4);

            Assert.IsTrue(sim.S.OpenDoors());
            sim.Run(0.3f);
            Assert.AreEqual(DoorState.Opening, sim.S.Elevator.Doors);
            sim.Run(0.5f);
            Assert.AreEqual(GuestState.Boarding, Info(sim, g).State);
            sim.Run(0.5f);
            Assert.AreEqual(GuestState.Riding, Info(sim, g).State);
            Assert.IsTrue(loc.TryGetGuestPosition(g, out p));
            var car = loc.CarPosition;
            Assert.AreEqual(car.X + layout.GetCarSlotOffset(0).X, p.X, 1e-4);
        }

        static GuestInfo Info(Sim sim, GuestId g)
        {
            Assert.IsTrue(sim.S.Guests.TryGet(g, out var info), "guest " + g + " active");
            return info;
        }
    }
}
