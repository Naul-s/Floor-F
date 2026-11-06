using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Balance and content checks on the REAL Resources/Data/*.json (content-designer).
    /// Pure data reasoning - no gameplay simulation, so these stay valid while the rule engine is built in parallel.
    /// Intent behind the numbers: Docs/Content/balance.md.
    /// </summary>
    public class ContentBalanceTests
    {
        /// <summary>Triggers every guest must have a line for (the mystery guest included - no exception needed).</summary>
        static readonly string[] CoreTriggers =
        {
            GuestLineTriggers.Spawn, GuestLineTriggers.Board, GuestLineTriggers.LuckyFloor, GuestLineTriggers.UnluckyFloor,
            GuestLineTriggers.Delivered, GuestLineTriggers.Impatient, GuestLineTriggers.Left,
        };

        static readonly string[] KnownTriggers =
        {
            GuestLineTriggers.Spawn, GuestLineTriggers.Board, GuestLineTriggers.LuckyFloor, GuestLineTriggers.UnluckyFloor,
            GuestLineTriggers.LuckyOccupancy, GuestLineTriggers.UnluckyOccupancy, GuestLineTriggers.Delivered,
            GuestLineTriggers.Impatient, GuestLineTriggers.Left, GuestLineTriggers.Relabelled, GuestLineTriggers.Special,
        };

        static readonly string[] NumberWords = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        /// <summary>Expansion traits that gameplay may not implement yet - keep them out of the core-loop days.</summary>
        static readonly string[] ExpansionTraits = { GuestTraits.Gambler, GuestTraits.Honest };
        const int FirstExpansionDay = 4;

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ helpers

        GuestDef Guest(SpawnDef s) => data.GetGuest(s.Guest);

        LuckProfile Profile(SpawnDef s) => data.GetLuckProfile(s.Guest);

        /// <summary>Physical 1-based destination of a spawn, or -1 when the guest picks at runtime.</summary>
        int DestinationNumber(SpawnDef s)
        {
            if (s.HasDestinationFloor) return s.destinationFloor;
            if (string.IsNullOrWhiteSpace(s.destination)) return -1;
            Assert.IsTrue(data.TryFindDestinationFloor(new DestinationId(s.destination.Trim()), out var f),
                "no floor serves destination '" + s.destination + "'");
            return f.PhysicalNumber;
        }

        /// <summary>Verdict for the INITIAL label of a physical floor (stickers come later; day 1 has none).</summary>
        LuckVerdict JudgeInitialLabel(LuckProfile p, int physicalFloor)
        {
            var label = data.GetFloor(FloorIndex.FromPhysicalNumber(physicalFloor)).InitialLabel;
            return label.TryGetNumber(out var n) ? p.Judge(n) : LuckVerdict.Neutral;
        }

        /// <summary>Waiting-line slots between the shaft and the exit (hotel.json layout).</summary>
        int WaitingSlots()
        {
            var l = data.Layout;
            return (int)Math.Floor((l.exitOffsetX - l.waitingStartOffsetX) / l.waitingSlotSpacing) + 1;
        }

        /// <summary>Worst case: nobody is picked up, every guest waits its full patience (or until the day ends).</summary>
        static float WaitEnd(SpawnDef s, GuestDef g, DayDef d) => Math.Min(s.time + g.patienceSeconds, d.durationSeconds);

        static bool ContainsNumber(string text, int n)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains(n.ToString())) return true;
            return n >= 0 && n < NumberWords.Length && text.IndexOf(NumberWords[n], StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool MentionsAny(string text, IEnumerable<int> numbers) => numbers.Any(n => ContainsNumber(text, n));

        // ------------------------------------------------------------ schedule shape

        [Test]
        public void Spawns_SortedByTime()
        {
            foreach (var d in data.Days)
                for (int i = 1; i < d.spawns.Length; i++)
                    Assert.LessOrEqual(d.spawns[i - 1].time, d.spawns[i].time, "day " + d.day + " spawn " + i + " is out of order");
        }

        [Test]
        public void Spawn_DestinationNotOwnFloor()
        {
            foreach (var d in data.Days)
                foreach (var s in d.spawns)
                {
                    var at = "day " + d.day + " t=" + s.time + " " + s.guest;
                    int dest = DestinationNumber(s);
                    if (dest > 0)
                    {
                        Assert.AreNotEqual(s.floor, dest, at + ": destination is the spawn floor");
                        continue;
                    }
                    // Runtime pick: at least one of the guest's destinations must be elsewhere.
                    var g = Guest(s);
                    bool anyElsewhere = g.destinations.Any(id =>
                        data.TryFindDestinationFloor(new DestinationId(id), out var f) && f.PhysicalNumber != s.floor);
                    Assert.IsTrue(anyElsewhere, at + ": every possible destination is the spawn floor");
                }
        }

        [Test]
        public void DayDurations_FitTargets()
        {
            float total = 0;
            foreach (var d in data.Days)
            {
                total += d.durationSeconds;
                if (d.tutorial) Assert.LessOrEqual(d.durationSeconds, 60f, "tutorial day " + d.day + " should be short");
                else Assert.That(d.durationSeconds, Is.InRange(90f, 120f), "day " + d.day + " should be 90-120 s");
                foreach (var s in d.spawns)
                {
                    var g = Guest(s);
                    if (g.HasTrait(GuestTraits.Mystery)) continue;
                    // A guest spawned this late could not be served before the day ends.
                    Assert.LessOrEqual(s.time, d.durationSeconds - 10f, "day " + d.day + " " + s.guest + " spawns too late");
                }
            }
            const float shopBudget = 30f;
            Assert.LessOrEqual(total + shopBudget * (data.DayCount - 1), 15f * 60f, "full run must stay under 15 minutes");
        }

        [Test]
        public void PeakWaiting_PerFloor_Reasonable()
        {
            int slots = WaitingSlots();
            foreach (var d in data.Days)
            {
                var entries = d.spawns.Select(s => (s, g: Guest(s))).ToArray();
                foreach (var t in entries.Select(e => e.s.time).Distinct())
                {
                    var waiting = entries.Where(e => e.s.time <= t && t < WaitEnd(e.s, e.g, d)).ToArray();
                    int building = waiting.Sum(e => e.g.partySize);
                    Assert.LessOrEqual(building, data.Capacity + data.Capacity / 2,
                        "day " + d.day + " t=" + t + ": worst-case " + building + " people waiting in the building");
                    foreach (var floor in waiting.GroupBy(e => e.s.floor))
                    {
                        int people = floor.Sum(e => e.g.partySize);
                        Assert.LessOrEqual(people, slots,
                            "day " + d.day + " t=" + t + " floor " + floor.Key + ": " + people + " waiting > " + slots + " slots");
                    }
                }
            }
        }

        // ------------------------------------------------------------ day 1 tutorial

        [Test]
        public void Day1_ThreeGuests_TeachesBothRules()
        {
            var day = data.GetDay(DayId.First);
            Assert.IsTrue(day.tutorial);
            var sp = day.spawns;
            Assert.AreEqual(3, sp.Length, "GDD 3: 3-guest tutorial");
            Assert.IsTrue(sp.All(s => !s.HasDestinationFloor && DestinationNumber(s) > 0), "tutorial destinations must be explicit");
            Assert.IsTrue(sp.All(s => s.time <= 10f), "the lesson must start immediately");

            // Rule 1, positive: some guest's destination label is their lucky number -> guaranteed sparkle.
            Assert.IsTrue(sp.Any(s => JudgeInitialLabel(Profile(s), DestinationNumber(s)) == LuckVerdict.Lucky),
                "rule 1: some guest must arrive at a lucky-numbered floor");

            // Rule 1, negative: a co-rider's destination is an unlucky label lying on another guest's route
            // -> the player is tempted to open there with that guest inside (and learns 'passing is fine').
            bool unluckyTemptation = sp.Any(a => sp.Any(b =>
            {
                if (ReferenceEquals(a, b) || a.floor != b.floor) return false;
                int da = DestinationNumber(a), db = DestinationNumber(b);
                bool onRoute = db > Math.Min(a.floor, da) && db < Math.Max(a.floor, da);
                return onRoute && JudgeInitialLabel(Profile(a), db) == LuckVerdict.Unlucky;
            }));
            Assert.IsTrue(unluckyTemptation, "rule 1: a stop on someone's unlucky floor must be possible");

            // Rule 2: a group that can board together on one floor (all still waiting) totals someone's lucky number.
            bool luckyOccupancy = false;
            for (int mask = 1; mask < (1 << sp.Length) && !luckyOccupancy; mask++)
            {
                var group = Enumerable.Range(0, sp.Length).Where(i => (mask & (1 << i)) != 0).Select(i => sp[i]).ToArray();
                if (group.Length < 2 || group.Select(s => s.floor).Distinct().Count() != 1) continue;
                float lastArrival = group.Max(s => s.time);
                if (group.Any(s => s.time + Guest(s).patienceSeconds <= lastArrival)) continue;
                int riders = group.Sum(s => Guest(s).partySize);
                luckyOccupancy = group.Any(s => Profile(s).Judge(riders) == LuckVerdict.Lucky);
            }
            Assert.IsTrue(luckyOccupancy, "rule 2: boarding together must hit someone's lucky headcount");

            // Under 30 s: one sweep (board, up to the highest stop, back down to the lowest) leaves >= 10 s for thinking.
            var r = data.Rules;
            int start = sp[0].floor;
            var dests = sp.Select(DestinationNumber).Distinct().ToArray();
            int top = Math.Max(start, dests.Max());
            int floorsTravelled = (top - start) + (top - dests.Min());
            int people = sp.Sum(s => Guest(s).partySize);
            float sweep = floorsTravelled * r.elevatorSecondsPerFloor + (dests.Length + 1) * 2f * r.doorSeconds + 2f * people * r.boardSeconds;
            Assert.LessOrEqual(sp.Max(s => s.time) + sweep, 20f, "day 1 must be playable well inside 30 s");
        }

        // ------------------------------------------------------------ progression

        [Test]
        public void EachDay_IntroducesNewGuest()
        {
            var seen = new HashSet<string>(data.GetDay(DayId.First).spawns.Select(s => s.guest));
            for (int n = 2; n <= data.DayCount; n++)
            {
                var ids = data.GetDay(new DayId(n)).spawns.Select(s => s.guest).Distinct().ToArray();
                Assert.IsTrue(ids.Any(id => !seen.Contains(id)), "day " + n + " brings no new guest (one new concept per day)");
                seen.UnionWith(ids);
            }
            foreach (var g in data.Guests)
                Assert.IsTrue(seen.Contains(g.id), "guest '" + g.id + "' is in the catalog but never scheduled");
        }

        [Test]
        public void ExpansionGuests_OnlyOnLateDays()
        {
            foreach (var d in data.Days)
                foreach (var s in d.spawns)
                {
                    var g = Guest(s);
                    if (ExpansionTraits.Any(g.HasTrait))
                        Assert.GreaterOrEqual(d.day, FirstExpansionDay, s.guest + " (expansion trait) scheduled on day " + d.day);
                }
        }

        [Test]
        public void Gambler_CanFindSevenRiders()
        {
            foreach (var d in data.Days)
                foreach (var s in d.spawns.Where(x => Guest(x).HasTrait(GuestTraits.Gambler)))
                {
                    var g = Guest(s);
                    int target = g.lucky.Where(n => n >= 1 && n <= data.Capacity).DefaultIfEmpty(0).Max();
                    Assert.Greater(target, 0, s.guest + ": needs a lucky number that fits in the car");
                    // Others who spawn on the same floor and are still waiting at some moment of the gambler's wait.
                    float gEnd = WaitEnd(s, g, d);
                    int others = d.spawns.Where(o => !ReferenceEquals(o, s) && o.floor == s.floor
                                                     && o.time < gEnd && WaitEnd(o, Guest(o), d) > s.time)
                                         .Sum(o => Guest(o).partySize);
                    Assert.GreaterOrEqual(others + g.partySize, target,
                        "day " + d.day + ": the gambler can never ride with " + target);
                }
        }

        // ------------------------------------------------------------ text

        [Test]
        public void AllLines_HaveKorean()
        {
            foreach (var g in data.Guests)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(g.name), g.id + ".name");
                Assert.IsFalse(string.IsNullOrWhiteSpace(g.nameKo), g.id + ".nameKo");
                foreach (var l in g.lines)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(l.text), g.id + "/" + l.trigger + ".text");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(l.textKo), g.id + "/" + l.trigger + ".textKo");
                }
            }
            foreach (var d in data.Days)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(d.titleKo), "day " + d.day + ".titleKo");
                Assert.IsFalse(string.IsNullOrWhiteSpace(d.introKo), "day " + d.day + ".introKo");
                Assert.IsFalse(string.IsNullOrWhiteSpace(d.intro), "day " + d.day + ".intro");
            }
            foreach (var s in data.Stickers)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.nameKo), s.id + ".nameKo");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.descriptionKo), s.id + ".descriptionKo");
            }
            foreach (var dest in data.Destinations)
                Assert.IsFalse(string.IsNullOrWhiteSpace(dest.nameKo), dest.id + ".nameKo");
        }

        [Test]
        public void EveryGuest_HasCoreTriggers()
        {
            foreach (var g in data.Guests)
                foreach (var t in CoreTriggers)
                    Assert.IsNotNull(g.FindLine(t), g.id + " has no '" + t + "' line");
        }

        [Test]
        public void Lines_UseKnownTriggers_OncePerGuest()
        {
            foreach (var g in data.Guests)
            {
                var triggers = g.lines.Select(l => l.trigger).ToArray();
                foreach (var t in triggers) CollectionAssert.Contains(KnownTriggers, t, g.id + ": unknown trigger");
                Assert.AreEqual(triggers.Length, triggers.Distinct().Count(), g.id + ": duplicate trigger (only the first is ever used)");
            }
        }

        [Test]
        public void GuestsWithReachableOccupancyNumbers_HaveOccupancyLines()
        {
            foreach (var g in data.Guests)
            {
                if (g.lucky.Any(n => n >= 1 && n <= data.Capacity))
                    Assert.IsNotNull(g.FindLine(GuestLineTriggers.LuckyOccupancy), g.id + ": lucky headcount possible but no line");
                if (g.unlucky.Any(n => n >= 1 && n <= data.Capacity))
                    Assert.IsNotNull(g.FindLine(GuestLineTriggers.UnluckyOccupancy), g.id + ": unlucky headcount possible but no line");
            }
        }

        /// <summary>Theme (x2 in judging): the reaction lines name the number, so the player learns WHY it matters.</summary>
        [Test]
        public void LuckLines_NameTheirNumber()
        {
            foreach (var g in data.Guests.Where(x => !x.hideNumbers))
            {
                Assert.IsTrue(MentionsAny(g.FindLine(GuestLineTriggers.LuckyFloor).text, g.lucky), g.id + ": lucky_floor line should name a lucky number");
                if (g.unlucky.Length > 0)
                    Assert.IsTrue(MentionsAny(g.FindLine(GuestLineTriggers.UnluckyFloor).text, g.unlucky), g.id + ": unlucky_floor line should name an unlucky number");
                var reachableLucky = g.lucky.Where(n => n <= data.Capacity).ToArray();
                if (reachableLucky.Length > 0)
                    Assert.IsTrue(MentionsAny(g.FindLine(GuestLineTriggers.LuckyOccupancy).text, reachableLucky), g.id + ": lucky_occupancy line should name the headcount");
                var reachableUnlucky = g.unlucky.Where(n => n <= data.Capacity).ToArray();
                if (reachableUnlucky.Length > 0)
                    Assert.IsTrue(MentionsAny(g.FindLine(GuestLineTriggers.UnluckyOccupancy).text, reachableUnlucky), g.id + ": unlucky_occupancy line should name the headcount");
            }
        }

        [Test]
        public void MysteryGuest_OnlyOnLastDay_LinesPointAtFour()
        {
            var mystery = data.Guests.Single(g => g.HasTrait(GuestTraits.Mystery));
            Assert.IsTrue(mystery.hideNumbers);
            for (int n = 1; n < data.DayCount; n++)
                Assert.IsFalse(data.GetDay(new DayId(n)).spawns.Any(s => s.guest == mystery.id), "mystery guest before the last day");
            Assert.IsTrue(ContainsNumber(mystery.FindLine(GuestLineTriggers.Spawn).text, 4), "spawn line must ask for the 4th floor");
        }

        /// <summary>
        /// 4th-floor ending (choice A: only when the real 4th floor is relabelled) must be discoverable from the text:
        /// the request hints at a HIDDEN fourth floor, a plain delivery says "not this one", the ending has its own twist line,
        /// and 4 -> F is on sale at every shop.
        /// </summary>
        [Test]
        public void MysteryGuest_LinesHintTheHiddenFourth()
        {
            var mystery = data.Guests.Single(g => g.HasTrait(GuestTraits.Mystery));
            foreach (var trigger in new[] { GuestLineTriggers.Spawn, GuestLineTriggers.Delivered, GuestLineTriggers.Special })
            {
                var line = mystery.FindLine(trigger);
                Assert.IsNotNull(line, "mystery guest needs a '" + trigger + "' line");
                Assert.IsTrue(line.text.IndexOf("hid", StringComparison.OrdinalIgnoreCase) >= 0, trigger + ": should talk about hiding - " + line.text);
                Assert.IsTrue(line.textKo.Contains("숨") || line.textKo.Contains("가려") || line.textKo.Contains("가렸"),
                    trigger + ".textKo: should talk about hiding - " + line.textKo);
            }
            Assert.AreNotEqual(mystery.FindLine(GuestLineTriggers.Delivered).text, mystery.FindLine(GuestLineTriggers.Special).text,
                "plain delivery and the ending need different lines");
            Assert.IsNotNull(mystery.FindLine(GuestLineTriggers.Relabelled), "mystery guest should react to stickers");

            var fourToF = data.Stickers.Single(s => s.from.Trim() == "4");
            for (int n = 1; n < data.DayCount; n++)
                CollectionAssert.Contains(data.GetDay(new DayId(n)).shopStickers, fourToF.id,
                    "day " + n + ": 4 -> F must be on sale at every shop (the ending stays reachable)");
        }
    }
}
