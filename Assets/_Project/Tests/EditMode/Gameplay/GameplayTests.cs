using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Tests.EditMode.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Gameplay
{
    public class GameplayTests
    {
        static void Apply(GameSession s, PlayerIntent i) => PlayerCommands.Apply(i, s.Elevator, s, s.Building, s, s);

        [Test]
        public void Keys_WSpaceEnter_DriveTheSession()
        {
            var s = new GameSession(SimFixture.Create(), new EventBus(), new System.Random(1));
            s.StartNewGame();
            Assert.IsFalse(PlayerCommands.Apply(PlayerIntent.Up, s.Elevator, s, s.Building, s, s), "no elevator input during Intro");

            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Running, s.Phase, "Enter: Intro -> Running");

            Apply(s, PlayerIntent.Up);
            Apply(s, PlayerIntent.Up);
            Assert.AreEqual(2, s.Elevator.TargetFloor.Value, "W twice: two floors up");
            s.Tick(5f);
            Assert.AreEqual(2, s.Elevator.CurrentFloor.Value);
            Apply(s, PlayerIntent.Down);
            s.Tick(5f);
            Assert.AreEqual(1, s.Elevator.CurrentFloor.Value, "S: one floor down");

            Apply(s, PlayerIntent.ToggleDoors);
            s.Tick(0.1f);
            Assert.AreEqual(DoorState.Open, s.Elevator.Doors);
            Apply(s, PlayerIntent.ToggleDoors);
            s.Tick(0.1f);
            Assert.AreEqual(DoorState.Closed, s.Elevator.Doors);

            s.EndDayNow();
            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Shop, s.Phase);
            Apply(s, PlayerIntent.Advance);
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.AreEqual(2, s.CurrentDay.Number);
        }

        static bool Press(GameSession s, KeyPress key)
            => PlayerCommands.Apply(PlayerCommands.IntentForKey(key), s.Elevator, s, s.Building, s, s, s);

        [Test]
        public void Esc_TogglesPause()
        {
            Assert.AreEqual(PlayerIntent.TogglePause, PlayerCommands.IntentForKey(KeyPress.Escape));
            Assert.AreEqual(PlayerIntent.Advance, PlayerCommands.IntentForKey(KeyPress.Enter));
            Assert.AreEqual(PlayerIntent.Advance, PlayerCommands.IntentForKey(KeyPress.NumpadEnter));
            Assert.AreEqual(PlayerIntent.Up, PlayerCommands.IntentForKey(KeyPress.W));
            Assert.AreEqual(PlayerIntent.Up, PlayerCommands.IntentForKey(KeyPress.UpArrow));
            Assert.AreEqual(PlayerIntent.Down, PlayerCommands.IntentForKey(KeyPress.S));
            Assert.AreEqual(PlayerIntent.Down, PlayerCommands.IntentForKey(KeyPress.DownArrow));
            Assert.AreEqual(PlayerIntent.ToggleDoors, PlayerCommands.IntentForKey(KeyPress.Space));
            Assert.AreEqual(PlayerIntent.None, PlayerCommands.IntentForKey(KeyPress.None));

            var s = new GameSession(SimFixture.Create(), new EventBus(), new System.Random(1));
            s.StartNewGame();
            Assert.IsTrue(Press(s, KeyPress.Enter));
            Assert.AreEqual(DayPhase.Running, s.Phase);

            Assert.IsTrue(Press(s, KeyPress.Escape), "Esc pauses");
            Assert.IsTrue(s.IsPaused);
            Assert.IsFalse(Press(s, KeyPress.W), "no elevator input while paused");
            Assert.IsFalse(Press(s, KeyPress.Space));
            Assert.IsTrue(Press(s, KeyPress.Escape), "Esc resumes");
            Assert.IsFalse(s.IsPaused);
            Assert.IsTrue(Press(s, KeyPress.W));

            Assert.IsFalse(PlayerCommands.Apply(PlayerIntent.TogglePause, s.Elevator, s, s.Building, s, s), "no menu: refused");
        }

        [Test]
        public void Title_EnterStartsGame_EscRefused()
        {
            var s = new GameSession(SimFixture.Create(), new EventBus(), new System.Random(1));
            s.ReturnToTitle();
            Assert.IsFalse(Press(s, KeyPress.Escape), "no pause on the title");
            Assert.IsFalse(s.IsPaused);
            Assert.IsFalse(Press(s, KeyPress.W), "player keys ignored during the demo");
            Assert.IsFalse(PlayerCommands.Apply(PlayerIntent.Advance, s.Elevator, s, s.Building, s, s), "old overload: no menu, no start");
            Assert.AreEqual(DayPhase.Title, s.Phase);
            Assert.IsTrue(Press(s, KeyPress.Enter), "Enter on the title = StartGameFromTitle");
            Assert.AreEqual(DayPhase.Intro, s.Phase);
            Assert.IsTrue(Press(s, KeyPress.Enter), "then Enter starts the day as before");
            Assert.AreEqual(DayPhase.Running, s.Phase);
        }

        [Test]
        public void RealData_TitleDemo_PlaysDay1AndLoops()
        {
            var data = GameDataResources.Load();
            var bus = new EventBus();
            var s = new GameSession(data, bus, new System.Random(3));
            int delivered = 0, flow = 0, spawned = 0;
            bus.Subscribe<GuestDelivered>(e => delivered++);
            bus.Subscribe<GuestSpawned>(e => spawned++);
            bus.Subscribe<DayStarted>(e => flow++);
            bus.Subscribe<DayEnded>(e => flow++);
            bus.Subscribe<GameStarted>(e => flow++);
            s.ReturnToTitle();
            var demoDay = new DayId(data.TutorialDayCount + 1);
            Assert.AreEqual(demoDay, s.CurrentDay, "the demo plays the first real day, never the Day 0 training shift");
            int loops = 0;
            float last = 0f;
            float horizon = data.GetDay(demoDay).durationSeconds * 2f + 10f;
            for (float t = 0f; t < horizon; t += 1f / 30f)
            {
                s.Tick(1f / 30f);
                if (s.ElapsedSeconds < last) loops++;
                last = s.ElapsedSeconds;
            }
            Assert.AreEqual(DayPhase.Title, s.Phase);
            Assert.That(delivered, Is.GreaterThan(0), "the LuckAware demo delivers guests");
            Assert.That(loops, Is.GreaterThan(0), "the demo day looped");
            Assert.AreEqual(0, flow, "no flow events from the demo");
            Assert.AreEqual(demoDay, s.CurrentDay, "still the real day after looping");
            TestContext.WriteLine($"demo: {delivered} delivered, {spawned} spawned, {loops} loops over {horizon:0}s, luck {s.Score.Luck}");
        }

        [Test]
        public void RealData_AutoPilot_PlaysEveryDay_ReachesAnEnding()
        {
            var data = GameDataResources.Load();
            var bus = new EventBus();
            var s = new GameSession(data, bus, new System.Random(7));
            var ended = new List<int>();
            int delivered = 0;
            bus.Subscribe<DayEnded>(e => ended.Add(e.Day.Number));
            bus.Subscribe<GuestDelivered>(e => delivered++);
            var pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building);

            s.StartNewGame();
            for (int frame = 0; frame < 200000 && s.Phase != DayPhase.Ending; frame++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Intro: if (!s.SkipTutorial()) s.BeginDay(); break; // bots never play the Day 0 training shift
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop:
                        foreach (var o in s.Offers) s.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor);
                        s.FinishShopping();
                        break;
                    case DayPhase.Running:
                        pilot.Step();
                        s.Tick(1f / 30f);
                        break;
                }
            }

            Assert.AreEqual(DayPhase.Ending, s.Phase);
            Assert.AreNotEqual(EndingKind.None, s.Ending);
            Assert.That(ended.Count, Is.GreaterThanOrEqualTo(1));
            CollectionAssert.AreEqual(ended, new SortedSet<int>(ended), "days end in order");
            Assert.That(delivered, Is.GreaterThan(0), "the bot delivers someone");
            TestContext.WriteLine($"ending {s.Ending} after day {s.CurrentDay.Number}: luck {s.Score.Luck}, tips {s.Score.Tips}, delivered {delivered}");
        }

        /// <summary>Plays the whole game on the real JSON with one bot. Shop: every offer onto the real 4th floor (same for both bots).</summary>
        static (int luck, EndingKind ending, int day, int tips, int lucky, int unlucky) PlayGame(GameData data, int seed, AutoPilotStrategy strategy)
        {
            var bus = new EventBus();
            var s = new GameSession(data, bus, new System.Random(seed));
            int lucky = 0, unlucky = 0;
            bus.Subscribe<LuckJudged>(e => { if (e.Verdict == LuckVerdict.Lucky) lucky++; else unlucky++; });
            var pilot = new AutoPilot(s.Elevator, s, s.Guests, s.Building, strategy) { Data = data };
            Assert.AreEqual(strategy, pilot.Strategy);
            s.StartNewGame();
            for (int frame = 0; frame < 200000 && s.Phase != DayPhase.Ending; frame++)
            {
                switch (s.Phase)
                {
                    case DayPhase.Intro: if (!s.SkipTutorial()) s.BeginDay(); break; // bots never play the Day 0 training shift
                    case DayPhase.Summary: s.ContinueFromSummary(); break;
                    case DayPhase.Shop:
                        foreach (var o in s.Offers) s.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor);
                        s.FinishShopping();
                        break;
                    case DayPhase.Running:
                        pilot.Step();
                        s.Tick(1f / 30f);
                        break;
                }
            }
            Assert.AreEqual(DayPhase.Ending, s.Phase, strategy + " seed " + seed + " reaches an ending");
            return (s.Score.Luck, s.Ending, s.CurrentDay.Number, s.Score.Tips, lucky, unlucky);
        }

        /// <summary>
        /// Real JSON, several seeds. Main check on an UNCAPPED Luck meter (rules.maxLuck raised in memory only): with the
        /// real cap both bots can sit at max Luck, which says nothing about how they play. With the real cap, aware must
        /// never be worse.
        /// </summary>
        [Test]
        public void AutoPilot_LuckAware_BeatsLuckBlind()
        {
            int[] seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };
            var capped = GameDataResources.Load();
            var uncapped = GameDataResources.Load();
            uncapped.Rules.maxLuck = 100000;
            double blind = 0, aware = 0, blindCapped = 0, awareCapped = 0;
            foreach (int seed in seeds)
            {
                var b = PlayGame(uncapped, seed, AutoPilotStrategy.LuckBlind);
                var a = PlayGame(uncapped, seed, AutoPilotStrategy.LuckAware);
                var bc = PlayGame(capped, seed, AutoPilotStrategy.LuckBlind);
                var ac = PlayGame(capped, seed, AutoPilotStrategy.LuckAware);
                blind += b.luck; aware += a.luck; blindCapped += bc.luck; awareCapped += ac.luck;
                TestContext.WriteLine($"seed {seed}: blind luck {b.luck} ({b.ending} d{b.day}, tips {b.tips}, +{b.lucky}/-{b.unlucky})" +
                                      $" | aware luck {a.luck} ({a.ending} d{a.day}, tips {a.tips}, +{a.lucky}/-{a.unlucky})" +
                                      $" | capped: blind {bc.luck} ({bc.ending}), aware {ac.luck} ({ac.ending})");
            }
            int n = seeds.Length;
            blind /= n; aware /= n; blindCapped /= n; awareCapped /= n;
            var summary = $"average final luck over {n} seeds - uncapped: LuckBlind {blind:0.0}, LuckAware {aware:0.0}; " +
                          $"real cap {capped.Rules.maxLuck}: LuckBlind {blindCapped:0.0}, LuckAware {awareCapped:0.0}";
            TestContext.WriteLine(summary);
            UnityEngine.Debug.Log("[AutoPilot] " + summary);
            Assert.Greater(aware, blind, "LuckAware should end with more Luck than LuckBlind on average");
            Assert.GreaterOrEqual(awareCapped, blindCapped, "with the real cap LuckAware is never worse on average");
        }

        [Test]
        public void AutoPilot_DefaultStrategy_IsLuckBlind()
        {
            var s = new GameSession(SimFixture.Create(), new EventBus(), new System.Random(1));
            Assert.AreEqual(AutoPilotStrategy.LuckBlind, new AutoPilot(s.Elevator, s, s.Guests, s.Building).Strategy);
        }
    }
}
