using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>Phase 2 rules: settings, gambler, honest/relabelled reactions, early day end.</summary>
    public class Phase2RuleTests
    {
        static GuestLineDef Line(string trigger, string text, string ko = "") => new GuestLineDef { trigger = trigger, text = text, textKo = ko };

        /// <summary>Gives <paramref name="id"/> a line for every trigger: "id:trigger" (+ Korean "ko:id:trigger").</summary>
        static void AddLines(SimFixture.Defs d, string id)
        {
            var g = d.Guests.guests.First(x => x.id == id);
            var triggers = new[]
            {
                GuestLineTriggers.Spawn, GuestLineTriggers.Board, GuestLineTriggers.LuckyFloor, GuestLineTriggers.UnluckyFloor,
                GuestLineTriggers.LuckyOccupancy, GuestLineTriggers.UnluckyOccupancy, GuestLineTriggers.Delivered,
                GuestLineTriggers.Impatient, GuestLineTriggers.Left, GuestLineTriggers.Relabelled,
            };
            g.lines = triggers.Select(t => Line(t, id + ":" + t, "ko:" + id + ":" + t)).ToArray();
        }

        static void AddGambler(SimFixture.Defs d)
        {
            var list = d.Guests.guests.ToList();
            list.Add(new GuestDef { id = "gambler", lucky = new[] { 7 }, unlucky = new int[0], patienceSeconds = 100f, baseTip = 10, traits = new[] { GuestTraits.Gambler }, color = "#FFFFFF" });
            d.Guests.guests = list.ToArray();
        }

        static GuestInfo Info(Sim sim, GuestId g)
        {
            Assert.IsTrue(sim.S.Guests.TryGet(g, out var info), "guest " + g + " active");
            return info;
        }

        static List<GuestSpoke> Lines(Sim sim, string trigger) => sim.Events<GuestSpoke>().Where(e => e.LineTrigger == trigger).ToList();

        /// <summary>Day 1 -> shop (with tips) -> stickers applied -> Day 2 running.</summary>
        static void StickerDay2(Sim sim, params (string sticker, int floor)[] stickers)
        {
            sim.EndDayToShop();
            foreach (var (sticker, floor) in stickers)
                Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(new StickerId(sticker), F(floor)), sticker);
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            sim.Log.Clear();
        }

        // ------------------------------------------------------------------ settings

        [Test]
        public void Settings_ToggleKorean_PublishesAndLocalizesLines()
        {
            var sim = Running(d => AddLines(d, "granny"));
            var changes = new List<SettingsChanged>();
            sim.Bus.Subscribe<SettingsChanged>(e => changes.Add(e));
            IGameSettings settings = sim.S.Settings;
            Assert.IsFalse(settings.UseKorean, "English by default");
            Assert.AreEqual(1f, settings.MasterVolume);
            Assert.IsTrue(settings.ShowTutorialHints);

            sim.Spawn("granny", 1, 3);
            Assert.AreEqual("granny:spawn", Lines(sim, GuestLineTriggers.Spawn).Last().Text);

            settings.UseKorean = true;
            Assert.AreEqual(1, changes.Count);
            Assert.IsTrue(changes[0].UseKorean);
            Assert.AreEqual(1f, changes[0].MasterVolume);
            Assert.IsTrue(changes[0].ShowTutorialHints);
            Assert.IsTrue(sim.S.UseKorean, "GameSession.UseKorean is the same setting");
            settings.UseKorean = true;
            Assert.AreEqual(1, changes.Count, "no event when the value does not change");

            sim.Spawn("granny", 2, 3);
            Assert.AreEqual("ko:granny:spawn", Lines(sim, GuestLineTriggers.Spawn).Last().Text);

            sim.S.UseKorean = false; // legacy setter goes through the settings
            Assert.AreEqual(2, changes.Count);
            Assert.IsFalse(changes[1].UseKorean);
            sim.Spawn("granny", 3, 5);
            Assert.AreEqual("granny:spawn", Lines(sim, GuestLineTriggers.Spawn).Last().Text);

            settings.MasterVolume = 3f;
            Assert.AreEqual(1f, settings.MasterVolume, "clamped to 0..1");
            Assert.AreEqual(2, changes.Count, "clamped to the same value: no event");
            settings.MasterVolume = 0.25f;
            settings.MasterVolume = float.NaN;
            Assert.AreEqual(0.25f, settings.MasterVolume);
            settings.ShowTutorialHints = false;
            Assert.AreEqual(4, changes.Count);
            Assert.AreEqual(0.25f, changes[3].MasterVolume);
            Assert.IsFalse(changes[3].ShowTutorialHints);
        }

        // ------------------------------------------------------------------ gambler

        [Test]
        public void Gambler_BoardsOnlyWhenMakingSeven()
        {
            var sim = Running(AddGambler);
            var gambler = sim.Spawn("gambler", 1, 9);
            sim.Open();
            Assert.AreEqual(0, sim.S.Elevator.Occupancy, "alone he would make 1, not 7");
            Assert.AreEqual(GuestState.Waiting, Info(sim, gambler).State);
            sim.Run(5f);
            Assert.AreEqual(0, sim.Events<GuestLeft>().Count, "refusing is not leaving");
            Assert.Less(Info(sim, gambler).Patience01, 1f, "patience keeps draining while he waits");
            sim.Close();

            // six others line up behind him: they board past him, he joins as the 7th
            var others = new List<GuestId>();
            for (int i = 0; i < 6; i++) others.Add(sim.Spawn("plain", 1, 2));
            sim.Open();
            Assert.AreEqual(7, sim.S.Elevator.Occupancy);
            var boarded = sim.Events<GuestBoarded>();
            Assert.AreEqual(gambler, boarded.Last().Guest, "he boards last");
            Assert.AreEqual(7, boarded.Last().Occupancy);
            sim.Close();
            var j = sim.Events<LuckJudged>().Single(e => e.Guest == gambler);
            Assert.AreEqual(LuckSource.Occupancy, j.Source);
            Assert.AreEqual(7, j.Number);
            Assert.AreEqual(LuckVerdict.Lucky, j.Verdict);
            Assert.AreEqual(0, sim.Events<GuestLeft>().Count);
        }

        [Test]
        public void Gambler_FullerCar_StaysWaiting()
        {
            var sim = Running(AddGambler);
            for (int i = 0; i < 7; i++) sim.Spawn("plain", 1, 2);
            var gambler = sim.Spawn("gambler", 1, 9);
            sim.Open();
            Assert.AreEqual(7, sim.S.Elevator.Occupancy, "8 is not his number");
            Assert.AreEqual(GuestState.Waiting, Info(sim, gambler).State);
        }

        // ------------------------------------------------------------------ honest / relabelled

        [Test]
        public void Honest_RelabelledFloor_Penalty()
        {
            var sim = Running(d => { d.Hotel.rules.startTips = 50; AddLines(d, "honest"); });
            StickerDay2(sim, ("any_to_8", 5));
            int luck = sim.S.Score.Luck;
            var g = sim.Spawn("honest", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();

            var j = sim.Events<LuckJudged>().Single();
            Assert.AreEqual(g, j.Guest);
            Assert.AreEqual(LuckSource.Sticker, j.Source);
            Assert.AreEqual(LuckVerdict.Unlucky, j.Verdict);
            Assert.AreEqual(F(5), j.Floor);
            Assert.AreEqual(luck - 5, sim.S.Score.Luck, "rules.honestGuestLuckPenalty");
            Assert.AreEqual(GuestMood.Angry, Info(sim, g).Mood);
            var line = Lines(sim, GuestLineTriggers.Relabelled).Single();
            Assert.AreEqual(g, line.Guest);
            Assert.AreEqual("honest:relabelled", line.Text);
            Assert.That(sim.IndexOf<LuckChanged>(), Is.LessThan(sim.IndexOf<GuestSpoke>(e => e.LineTrigger == GuestLineTriggers.Relabelled)));

            // an original label is fine
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(1, sim.Events<LuckJudged>().Count);
        }

        [Test]
        public void RelabelledLine_OncePerGuest()
        {
            var sim = Running(d => { d.Hotel.rules.startTips = 100; AddLines(d, "plain"); AddLines(d, "loner"); });
            StickerDay2(sim, ("four_to_f", 4), ("any_to_8", 6));
            int luck = sim.S.Score.Luck;
            var a = sim.Spawn("plain", 1, 9);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            sim.Open();
            var lines = Lines(sim, GuestLineTriggers.Relabelled);
            Assert.AreEqual(1, lines.Count, "a sticker gets a comment");
            Assert.AreEqual(a, lines[0].Guest);
            Assert.AreEqual(0, sim.Events<LuckJudged>().Count, "not honest: no penalty");
            Assert.AreEqual(luck, sim.S.Score.Luck);

            var b = sim.Spawn("loner", 4, 9); // boards here, has not seen a sticker yet
            sim.RunUntil(() => sim.S.Elevator.Occupancy == 2);
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            lines = Lines(sim, GuestLineTriggers.Relabelled);
            Assert.AreEqual(2, lines.Count, "a: second sticker, no repeat; b: first sticker");
            Assert.AreEqual(b, lines[1].Guest);

            sim.Close();
            sim.GoTo(4);
            sim.Open();
            Assert.AreEqual(2, Lines(sim, GuestLineTriggers.Relabelled).Count, "nobody repeats it");
            Assert.AreEqual(luck, sim.S.Score.Luck);
        }

        // ------------------------------------------------------------------ early day end

        [Test]
        public void Day_EndsEarly_WhenAllGuestsDone()
        {
            var sim = Running(d => d.Days.days[0].spawns = new[]
            {
                new SpawnDef { time = 0.5f, guest = "plain", floor = 1, destination = "d2" },
                new SpawnDef { time = 20f, guest = "plain", floor = 1, destination = "d2" },
            });
            sim.Run(1f);
            sim.Open();
            sim.Close();
            sim.GoTo(2);
            sim.Open();
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count);
            sim.Run(5f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "a spawn is still scheduled");

            sim.RunUntil(() => sim.S.Guests.ActiveGuests.Count == 1, 30f);
            sim.Close();
            sim.GoTo(1);
            sim.Open();
            sim.Close();
            sim.GoTo(2);
            sim.Open();
            Assert.AreEqual(0, sim.S.Guests.ActiveGuests.Count);
            int luck = sim.S.Score.Luck;
            float elapsed = sim.S.ElapsedSeconds;

            sim.Run(GameSession.EarlyEndDelaySeconds - 0.1f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "short pause first");
            sim.Run(0.2f);
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
            var ended = sim.Events<DayEnded>().Single();
            Assert.AreEqual(2, ended.Summary.Delivered);
            Assert.AreEqual(luck, sim.S.Score.Luck, "no bonus for the time left");
            Assert.AreEqual(elapsed + GameSession.EarlyEndDelaySeconds, sim.S.ElapsedSeconds, 0.11f);
            Assert.Greater(sim.S.RemainingSeconds, 900f);
            sim.AssertOrder(typeof(DayEnded), typeof(DayPhaseChanged));
        }

        [Test]
        public void Day_NoScheduledSpawns_DoesNotEndEarly()
        {
            var sim = Running(d => d.Days.days[0].durationSeconds = 30f);
            sim.Run(10f);
            Assert.AreEqual(DayPhase.Running, sim.S.Phase, "a day without data spawns only ends on the clock");
        }
    }
}
