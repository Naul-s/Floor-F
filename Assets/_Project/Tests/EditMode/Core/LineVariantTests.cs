using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Line variants (the same trigger more than once in guests.json): GameSession.Speak takes them in turn per
    /// archetype+trigger - deterministic, no RNG; a throttled line does not use a turn; the turn survives the day
    /// change and resets with a new run. A single line behaves exactly as before.
    /// </summary>
    public class LineVariantTests
    {
        static GuestLineDef Line(string trigger, string text) => new GuestLineDef { trigger = trigger, text = text, textKo = "ko:" + text };

        static Sim WithSpawnVariants(params string[] texts)
            => Running(d =>
            {
                var g = d.Guests.guests.First(x => x.id == "granny");
                g.lines = texts.Select(t => Line(GuestLineTriggers.Spawn, t))
                               .Concat(new[] { Line(GuestLineTriggers.Left, "bye") })
                               .ToArray();
            });

        static string[] Said(Sim sim, string trigger)
            => sim.Events<GuestSpoke>().Where(e => e.LineTrigger == trigger).Select(e => e.Text).ToArray();

        /// <summary>Spawns a granny and waits out the same-line cooldown, so the next one may speak.</summary>
        static void SpawnAndWait(Sim sim, int floor)
        {
            sim.Spawn("granny", floor, floor == 3 ? 5 : 3);
            sim.Run(GameSession.SameLineCooldownSeconds);
        }

        [Test]
        public void Variants_TakeTurns_InOrder_AndWrap()
        {
            var sim = WithSpawnVariants("a", "b", "c");
            for (int i = 0; i < 4; i++) SpawnAndWait(sim, 1 + i % 2);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "a" }, Said(sim, GuestLineTriggers.Spawn));
        }

        [Test]
        public void ThrottledLine_DoesNotUseATurn()
        {
            var sim = WithSpawnVariants("a", "b");
            sim.Spawn("granny", 1, 3);
            sim.Spawn("granny", 2, 3); // same archetype + trigger inside the cooldown: no bubble
            CollectionAssert.AreEqual(new[] { "a" }, Said(sim, GuestLineTriggers.Spawn));
            sim.Run(GameSession.SameLineCooldownSeconds);
            sim.Spawn("granny", 1, 3);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Said(sim, GuestLineTriggers.Spawn), "the throttled spawn did not skip 'b'");
        }

        [Test]
        public void Turn_SurvivesTheNextDay_ResetsWithANewRun()
        {
            var sim = WithSpawnVariants("a", "b", "c");
            SpawnAndWait(sim, 1);
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
            SpawnAndWait(sim, 1);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Said(sim, GuestLineTriggers.Spawn), "per run, not per day");

            sim.S.RestartGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.Log.Clear();
            SpawnAndWait(sim, 1);
            CollectionAssert.AreEqual(new[] { "a" }, Said(sim, GuestLineTriggers.Spawn), "a new run starts at the first variant");
        }

        [Test]
        public void Turns_ArePerTrigger_AndKoreanFollowsTheSameTurn()
        {
            var sim = Running(d =>
            {
                var g = d.Guests.guests.First(x => x.id == "granny");
                g.lines = new[]
                {
                    Line(GuestLineTriggers.Spawn, "s1"), Line(GuestLineTriggers.Board, "b1"),
                    Line(GuestLineTriggers.Spawn, "s2"), Line(GuestLineTriggers.Board, "b2"),
                };
            });
            sim.S.UseKorean = true;
            SpawnAndWait(sim, 1);
            sim.Open(); // boards: the first board line
            sim.Close();
            sim.Run(GameSession.SameLineCooldownSeconds);
            SpawnAndWait(sim, 2); // waits on another floor: spawns, does not board
            Assert.AreEqual(F(1), sim.S.Elevator.CurrentFloor, "the car never moved");
            CollectionAssert.AreEqual(new[] { "ko:s1", "ko:s2" }, Said(sim, GuestLineTriggers.Spawn));
            CollectionAssert.AreEqual(new[] { "ko:b1" }, Said(sim, GuestLineTriggers.Board), "board keeps its own turn");
        }

        [Test]
        public void SingleLine_AlwaysTheSame()
        {
            var sim = WithSpawnVariants("only");
            for (int i = 0; i < 3; i++) SpawnAndWait(sim, 1 + i % 2);
            CollectionAssert.AreEqual(new[] { "only", "only", "only" }, Said(sim, GuestLineTriggers.Spawn));
        }

        [Test]
        public void FindLine_PickWraps()
        {
            var g = new GuestDef { lines = new[] { Line("spawn", "a"), Line("board", "x"), Line("spawn", "b") } };
            Assert.AreEqual(2, g.LineCount("spawn"));
            Assert.AreEqual("a", g.FindLine("spawn", 0).text);
            Assert.AreEqual("b", g.FindLine("spawn", 1).text);
            Assert.AreEqual("a", g.FindLine("spawn", 2).text);
            Assert.AreSame(g.FindLine("spawn"), g.FindLine("spawn", 0));
            Assert.IsNull(g.FindLine("left", 3));
        }
    }
}
