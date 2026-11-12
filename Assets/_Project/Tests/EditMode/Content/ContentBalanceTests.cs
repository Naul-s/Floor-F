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
            GuestLineTriggers.LuckyDistance, GuestLineTriggers.UnluckyDistance,
        };

        static readonly string[] NumberWords = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        /// <summary>Expansion traits that gameplay may not implement yet - keep them out of the core-loop days.</summary>
        static readonly string[] ExpansionTraits = { GuestTraits.Honest };
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

        /// <summary>Most variants one trigger may have (GameSession.Speak takes them in turn).</summary>
        const int MaxVariantsPerTrigger = 3;

        /// <summary>Every variant of <paramref name="trigger"/> (in file order; empty when none).</summary>
        static GuestLineDef[] LinesOf(GuestDef g, string trigger)
            => (g.lines ?? new GuestLineDef[0]).Where(l => l != null && string.Equals(l.trigger, trigger, StringComparison.OrdinalIgnoreCase)).ToArray();

        /// <summary>Asserts EVERY variant of <paramref name="trigger"/> names one of <paramref name="numbers"/>.</summary>
        static void AssertAllName(GuestDef g, string trigger, IEnumerable<int> numbers, string what)
        {
            var lines = LinesOf(g, trigger);
            Assert.IsNotEmpty(lines, g.id + ": no '" + trigger + "' line");
            foreach (var l in lines) Assert.IsTrue(MentionsAny(l.text, numbers), g.id + ": " + what + " - " + l.text);
        }

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
                else if (d.day == data.TutorialDayCount + 1) Assert.That(d.durationSeconds, Is.InRange(60f, 90f), "first real day " + d.day + " should be a gentle 60-90 s");
                else Assert.That(d.durationSeconds, Is.InRange(90f, 120f), "day " + d.day + " should be 90-120 s");
                foreach (var s in d.spawns)
                {
                    var g = Guest(s);
                    if (g.HasTrait(GuestTraits.Mystery)) continue;
                    // A guest spawned this late could not be served before the day ends.
                    Assert.LessOrEqual(s.time, d.durationSeconds - 10f, "day " + d.day + " " + s.guest + " spawns too late");
                }
            }
            // Base run = 7 real days (5 days were capped at 15 min): the clock-out worst case may run ~17 min; early day ends
            // make real runs shorter. The winter season (days 8-10, only after ExtendSeason) adds about 6-7 minutes on top.
            const float shopBudget = 30f;
            float baseTotal = data.Days.Where(d => !data.IsExtendedDay(d.Id)).Sum(d => d.durationSeconds);
            Assert.LessOrEqual(baseTotal + shopBudget * (data.DayCount - 1), 17f * 60f, "the base run must stay under 17 minutes");
            Assert.LessOrEqual(total + shopBudget * (data.AllDayCount - 1), 24f * 60f, "base run + winter season must stay under 24 minutes");
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

        // ------------------------------------------------------------ day 0 tutorial (training shift, waves)

        /// <summary>
        /// Day 0: three waves (the next one comes once the hotel is empty), each teaching one lesson -
        /// wave 1 a lucky door-open floor, wave 2 a lucky head-count, wave 3 an unlucky "4" that the 4 -> F sticker fixes.
        /// </summary>
        [Test]
        public void Day0_Waves_TeachOneLessonEach()
        {
            var day = data.GetDay(DayId.First);
            Assert.IsTrue(day.tutorial);
            Assert.IsEmpty(day.shopStickers, "no shop after the training shift");
            var sp = day.spawns;
            Assert.IsTrue(sp.All(s => !s.HasDestinationFloor && DestinationNumber(s) > 0), "tutorial destinations must be explicit");
            var waves = sp.GroupBy(s => s.time).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
            Assert.AreEqual(3, waves.Length, "three waves");
            Assert.IsTrue(sp.All(s => s.floor == sp[0].floor), "every wave boards in the lobby");

            Assert.AreEqual(1, waves[0].Length, "wave 1: a single guest");
            Assert.AreEqual(LuckVerdict.Lucky, JudgeInitialLabel(Profile(waves[0][0]), DestinationNumber(waves[0][0])),
                "wave 1: the guest's floor is their lucky number");

            int heads = waves[1].Sum(s => Guest(s).partySize);
            Assert.IsTrue(waves[1].Any(s => Profile(s).Judge(heads) == LuckVerdict.Lucky), "wave 2: boarding together hits a lucky head-count");
            Assert.IsTrue(waves[1].All(s => JudgeInitialLabel(Profile(s), DestinationNumber(s)) == LuckVerdict.Neutral),
                "wave 2: only the head-count matters");

            var fourToF = data.Stickers.Single(s => s.from.Trim() == "4");
            var scared = waves[2].Where(s => JudgeInitialLabel(Profile(s), DestinationNumber(s)) == LuckVerdict.Unlucky).ToArray();
            Assert.IsNotEmpty(scared, "wave 3: someone's floor is unlucky for them");
            Assert.IsTrue(scared.Any(s => fourToF.CanApplyTo(data.GetFloor(FloorIndex.FromPhysicalNumber(DestinationNumber(s))).InitialLabel)),
                "wave 3: the unlucky floor is the one 4 -> F hides");
            Assert.IsTrue(waves[2].Any(s => JudgeInitialLabel(Profile(s), DestinationNumber(s)) == LuckVerdict.Lucky),
                "wave 3: different guests, different numbers - someone else is lucky");
        }

        /// <summary>The first real day (formerly the tutorial) still teaches both rules with its first three guests.</summary>
        [Test]
        public void Day1_FirstGuests_TeachBothRules()
        {
            var day = data.GetDay(new DayId(data.TutorialDayCount + 1));
            Assert.IsFalse(day.tutorial);
            var sp = day.spawns.Take(3).ToArray();
            Assert.AreEqual(3, sp.Length, "GDD 3: the first three guests");
            Assert.IsTrue(sp.All(s => !s.HasDestinationFloor && DestinationNumber(s) > 0), "first-day destinations must be explicit");
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
            Assert.LessOrEqual(sp.Max(s => s.time) + sweep, 20f, "the opening of day 1 must be playable well inside 30 s");
            Assert.LessOrEqual(day.spawns.Length, 6, "the first real day stays gentle (about 5 guests)");
        }

        // ------------------------------------------------------------ progression

        [Test]
        public void EachDay_IntroducesNewGuest()
        {
            // Day 0 only rehearses the guests of Day 1; "one new concept per day" starts after Day 1.
            int first = data.TutorialDayCount + 1;
            var seen = new HashSet<string>(data.GetDay(new DayId(first)).spawns.Select(s => s.guest));
            for (int n = 1; n < first; n++)
            {
                var tutorialIds = data.GetDay(new DayId(n)).spawns.Select(s => s.guest).Distinct();
                Assert.IsTrue(tutorialIds.All(seen.Contains), "tutorial day " + n + " should only use guests Day 1 has");
            }
            for (int n = first + 1; n <= data.AllDayCount; n++)
            {
                var day = data.GetDay(new DayId(n));
                var ids = day.spawns.Select(s => s.guest).Distinct().ToArray();
                // winter season: a special-night rule (the lottery) is that day's new concept too
                bool newRule = data.IsExtendedDay(day.Id) && (day.lottery || day.christmas || day.luckyForAll > 0);
                Assert.IsTrue(newRule || ids.Any(id => !seen.Contains(id)), "day " + n + " brings no new guest (one new concept per day)");
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
                        Assert.GreaterOrEqual(data.DisplayDayNumber(d.Id), FirstExpansionDay, s.guest + " (expansion trait) scheduled on day " + d.day);
                }
        }

        /// <summary>
        /// The gambler is a plain guest who loves 7 (the old "boards only at 7 riders" rule is gone): he heads for the casino,
        /// and some scheduled ride of his is a lucky 7 floors - the lobby -> casino straight ride, or one turn away.
        /// </summary>
        [Test]
        public void Gambler_LovesSeven_AndCanRideIt()
        {
            var g = data.GetGuest(new GuestArchetypeId("gambler"));
            CollectionAssert.Contains(g.lucky, 7, "lucky seven");
            Assert.IsFalse(g.HasTrait(GuestTraits.Gambler), "no boarding gimmick any more");
            CollectionAssert.AreEqual(new[] { "casino" }, g.destinations);
            Assert.IsFalse(g.lines.Any(l => l.text.IndexOf("only ride", StringComparison.OrdinalIgnoreCase) >= 0
                                            || l.text.IndexOf("seven of us", StringComparison.OrdinalIgnoreCase) >= 0),
                "no line promises the old head-count rule");
            var rides = data.Days.SelectMany(d => d.spawns).Where(s => s.guest == g.id).ToArray();
            Assert.IsNotEmpty(rides, "the gambler is scheduled");
            Assert.IsTrue(rides.All(s => DestinationNumber(s) > 0), "his casino trips are scripted");
            Assert.IsTrue(rides.Any(s => OneTurnDistances(s.floor, DestinationNumber(s)).Contains(7)), "a lucky 7-floor ride is possible");
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
        public void Lines_UseKnownTriggers_AtMostThreeVariants()
        {
            foreach (var g in data.Guests)
            {
                var triggers = g.lines.Select(l => l.trigger).ToArray();
                foreach (var t in triggers) CollectionAssert.Contains(KnownTriggers, t, g.id + ": unknown trigger");
                foreach (var grp in triggers.GroupBy(t => t))
                    Assert.LessOrEqual(grp.Count(), MaxVariantsPerTrigger, g.id + "/" + grp.Key + ": too many variants");
                foreach (var grp in g.lines.GroupBy(l => l.trigger).Where(x => x.Count() > 1))
                {
                    Assert.AreEqual(grp.Count(), grp.Select(l => l.text).Distinct().Count(), g.id + "/" + grp.Key + ": identical variants (en)");
                    Assert.AreEqual(grp.Count(), grp.Select(l => l.textKo).Distinct().Count(), g.id + "/" + grp.Key + ": identical variants (ko)");
                }
                Assert.LessOrEqual(g.LineCount(GuestLineTriggers.Special), 1, g.id + ": one special line (the ending scene quotes it)");
            }
        }

        [Test]
        public void GuestsWithReachableOccupancyNumbers_HaveOccupancyLines()
        {
            foreach (var g in data.Guests)
            {
                if (g.lucky.Any(n => n >= 1 && n <= data.Capacity))
                    Assert.IsNotNull(g.FindLine(GuestLineTriggers.LuckyOccupancy), g.id + ": lucky headcount possible but no line");
                if (g.unlucky.Any(n => HeadcountRules.CanBeUnlucky(n) && n <= data.Capacity))
                    Assert.IsNotNull(g.FindLine(GuestLineTriggers.UnluckyOccupancy), g.id + ": unlucky headcount possible but no line");
            }
        }

        /// <summary>Theme (x2 in judging): the reaction lines name the number, so the player learns WHY it matters.</summary>
        [Test]
        public void LuckLines_NameTheirNumber()
        {
            foreach (var g in data.Guests.Where(x => !x.hideNumbers))
            {
                AssertAllName(g, GuestLineTriggers.LuckyFloor, g.lucky, "lucky_floor line should name a lucky number");
                if (g.unlucky.Length > 0)
                    AssertAllName(g, GuestLineTriggers.UnluckyFloor, g.unlucky, "unlucky_floor line should name an unlucky number");
                var reachableLucky = g.lucky.Where(n => n <= data.Capacity).ToArray();
                if (reachableLucky.Length > 0)
                    AssertAllName(g, GuestLineTriggers.LuckyOccupancy, reachableLucky, "lucky_occupancy line should name the headcount");
                var reachableUnlucky = g.unlucky.Where(n => HeadcountRules.CanBeUnlucky(n) && n <= data.Capacity).ToArray();
                if (reachableUnlucky.Length > 0)
                    AssertAllName(g, GuestLineTriggers.UnluckyOccupancy, reachableUnlucky, "unlucky_occupancy line should name the headcount");
            }
        }

        // ------------------------------------------------------------ rule 3: ride distance, 666, Friday the 13th

        /// <summary>Ride distance of a direct trip (no detour) for a spawn with a known destination, else -1.</summary>
        int DirectDistance(SpawnDef s)
        {
            int dest = DestinationNumber(s);
            return dest > 0 ? Math.Abs(dest - s.floor) : -1;
        }

        /// <summary>
        /// Rule 3 verdict for <paramref name="distance"/> floors on day <paramref name="d"/> (Friday: 13 is unlucky for all;
        /// star night: 7 is lucky for all; DayDef.luckyForAll, Christmas Eve's 12, likewise).
        /// </summary>
        internal static LuckVerdict JudgeDistance(DayDef d, LuckProfile p, int distance)
        {
            if (d.starNight) p = StarNightRules.Apply(p);
            if (d.luckyForAll > 0) p = LuckyForAll(p, d.luckyForAll);
            return d.friday13 && distance == StoryRules.FridayDistance ? LuckVerdict.Unlucky : p.Judge(distance);
        }

        /// <summary><paramref name="own"/> with <paramref name="number"/> Lucky (a guest's own unlucky copy loses), like the star night's 7.</summary>
        internal static LuckProfile LuckyForAll(LuckProfile own, int number)
        {
            own = own ?? LuckProfile.None;
            var lucky = new List<int>(own.Lucky) { number };
            var unlucky = own.Unlucky.Where(n => n != number).ToList();
            return new LuckProfile(NumberSet.From(lucky), NumberSet.From(unlucky));
        }

        /// <summary>Ride lengths reachable with at most one turn: start -> x -> destination (x = destination: direct).</summary>
        IEnumerable<int> OneTurnDistances(int start, int dest)
            => Enumerable.Range(1, data.FloorCount).Select(x => Math.Abs(x - start) + Math.Abs(dest - x)).Distinct();

        /// <summary>Theme: every guest who can feel a ride distance has the reaction lines, and they name the number.</summary>
        [Test]
        public void DistanceLines_ExistAndNameTheirNumber()
        {
            foreach (var g in data.Guests.Where(x => !x.hideNumbers))
            {
                if (g.lucky.Any(n => n >= 1))
                    AssertAllName(g, GuestLineTriggers.LuckyDistance, g.lucky, "lucky_distance should name the distance");
                if (g.unlucky.Any(n => n >= 1))
                    AssertAllName(g, GuestLineTriggers.UnluckyDistance, g.unlucky, "unlucky_distance should name the distance");
            }
        }

        [Test]
        public void NewSuperstitions_Numbers()
        {
            var tourist = data.GetGuest(new GuestArchetypeId("western_tourist"));
            CollectionAssert.Contains(tourist.unlucky, 13, "the West's 13");
            CollectionAssert.Contains(tourist.lucky, 7);

            var priest = data.GetGuest(new GuestArchetypeId("western_priest"));
            Assert.IsTrue(priest.HasTrait(GuestTraits.Devout), "666 counts double");
            CollectionAssert.Contains(priest.unlucky, StoryRules.DevoutNumber);
            CollectionAssert.Contains(priest.lucky, 7);
            var accountant = data.GetGuest(new GuestArchetypeId("honest_accountant"));
            CollectionAssert.Contains(accountant.lucky, StoryRules.DevoutNumber, "the priest's 6 is the accountant's lucky 6 (conflict)");

            var nonno = data.GetGuest(new GuestArchetypeId("italian_nonno"));
            CollectionAssert.AreEqual(new[] { 13 }, nonno.lucky, "Italy: 'fare tredici'");
            CollectionAssert.AreEqual(new[] { 17 }, nonno.unlucky, "XVII = VIXI");
            Assert.IsTrue(nonno.lucky.Concat(nonno.unlucky).All(n => n > data.FloorCount),
                "his numbers are neither a label nor a headcount: only a ride distance (detour) reaches them");
        }

        /// <summary>Every new archetype's lesson actually happens on the days that introduce it (no dead content).</summary>
        [Test]
        public void NewArchetypes_LessonsReachable()
        {
            var days = data.Days.Where(d => !d.tutorial).ToArray();

            // tourist: a lucky 7-floor ride is on the schedule (directly or with one turn)
            Assert.IsTrue(days.SelectMany(d => d.spawns).Where(s => s.guest == "western_tourist" && DestinationNumber(s) > 0)
                    .Any(s => OneTurnDistances(s.floor, DestinationNumber(s)).Contains(7)), "tourist: a 7-floor ride must be possible");

            // priest: a ride that is exactly 6 floors when driven straight (the 666 lesson), with a cheap detour that avoids it
            var priestRides = days.SelectMany(d => d.spawns).Where(s => s.guest == "western_priest").ToArray();
            Assert.IsNotEmpty(priestRides, "the priest is never scheduled");
            var p6 = priestRides.Where(s => DirectDistance(s) == StoryRules.DevoutNumber).ToArray();
            Assert.IsNotEmpty(p6, "priest: some direct ride is 6 floors (666)");
            Assert.IsTrue(p6.All(s => OneTurnDistances(s.floor, DestinationNumber(s)).Any(n => n != 6 && n <= 10)),
                "priest: the 6-floor ride can be fixed with a short detour");

            // Italian: some ride reaches 13 with one turn AND a co-rider (same floor and time) wants to go to that turning floor
            bool natural = false;
            foreach (var d in days)
                foreach (var s in d.spawns.Where(x => x.guest == "italian_nonno"))
                {
                    int dest = DestinationNumber(s);
                    Assert.Greater(dest, 0, "Italian destinations are scripted (the lesson needs a known route)");
                    Assert.AreEqual(1, Math.Abs(dest - s.floor) % 2, "a ride of 13 needs an odd straight distance (parity)");
                    Assert.IsTrue(OneTurnDistances(s.floor, dest).Contains(13), "day " + d.day + ": 13 needs one turn at most");
                    natural |= d.spawns.Any(o => !ReferenceEquals(o, s) && o.floor == s.floor && Math.Abs(o.time - s.time) < 0.01f
                                                 && DestinationNumber(o) > 0
                                                 && Math.Abs(DestinationNumber(o) - s.floor) + Math.Abs(dest - DestinationNumber(o)) == 13);
                }
            Assert.IsTrue(natural, "Italian: delivering a co-rider first should ride him exactly 13 floors");
        }

        /// <summary>
        /// Fair rule 3: a scripted guest's straight ride is unlucky by distance at most once per day (the deliberate lesson),
        /// and never on Day 0 or in the first real day's opening (those teach rules 1 and 2).
        /// </summary>
        [Test]
        public void DirectRides_RarelyUnluckyByDistance()
        {
            foreach (var d in data.Days)
            {
                var bad = d.spawns.Where(s => DirectDistance(s) > 0 && JudgeDistance(d, Profile(s), DirectDistance(s)) == LuckVerdict.Unlucky)
                               .Select(s => s.guest + "@" + s.time).ToArray();
                bool lesson = d.tutorial || data.DisplayDayNumber(d.Id) == 1;
                Assert.LessOrEqual(bad.Length, lesson ? 0 : 1, "day " + d.day + ": straight rides unlucky by distance: " + string.Join(", ", bad));
            }
        }

        [Test]
        public void DayIntros_ExplainNewRules()
        {
            DayDef FirstWith(string guest) => data.Days.First(d => d.spawns.Any(s => s.guest == guest));
            var priestDay = FirstWith("western_priest");
            StringAssert.Contains("6", priestDay.intro, "the priest's day explains 666");
            StringAssert.Contains("6", priestDay.introKo);
            var nonnoDay = FirstWith("italian_nonno");
            foreach (var t in new[] { nonnoDay.intro, nonnoDay.introKo })
            {
                StringAssert.Contains("13", t);
                StringAssert.Contains("17", t);
            }
            Assert.Greater(nonnoDay.day, priestDay.day, "one new superstition at a time");

            // rule 3 is explained on a real day before the priest arrives
            var distanceDay = data.Days.First(d => !d.tutorial && d.intro.Contains("13") && d.intro.Contains("7"));
            Assert.Less(distanceDay.day, priestDay.day);
            Assert.IsTrue(distanceDay.introKo.Contains("13") && distanceDay.introKo.Contains("7"));
        }

        [Test]
        public void Friday13_IsDay5_AndSaysSo()
        {
            var fridays = data.Days.Where(d => d.friday13).ToArray();
            Assert.AreEqual(1, fridays.Length, "one Friday the 13th");
            var f = fridays[0];
            Assert.AreEqual(5, data.DisplayDayNumber(f.Id), "Friday the 13th is Day 5");
            Assert.Less(f.day, data.DayCount, "no longer the last day");
            Assert.IsFalse(f.tutorial);
            Assert.IsFalse(f.starNight);
            foreach (var t in new[] { f.title, f.titleKo, f.intro, f.introKo }) StringAssert.Contains("13", t);
            Assert.Greater(data.Rules.friday13PenaltyMultiplier, 1f, "bad luck must hit harder on Friday the 13th");
            Assert.IsFalse(f.intro.Contains("last"), "Friday is not the last night any more");
            Assert.IsFalse(f.introKo.Contains("\uB9C8\uC9C0\uB9C9"), "Friday is not the last night any more (ko)");
        }

        /// <summary>
        /// Day 7 = the last day of the base run = the star night: lucky 7 for all, 4-5 wish stars, the (rare) 4th-floor guest.
        /// Its shop only opens when the season is extended (ISeasonFlow.ExtendSeason) and gets Singles' Day ready.
        /// </summary>
        [Test]
        public void StarNight_IsTheLastBaseDay_AndSaysSo()
        {
            var nights = data.Days.Where(d => d.starNight).ToArray();
            Assert.AreEqual(1, nights.Length, "one star night");
            var n = nights[0];
            Assert.AreEqual(data.DayCount, n.day, "the last day of the base run");
            Assert.IsTrue(data.IsLastDay(n.Id));
            Assert.IsFalse(n.extended);
            Assert.AreEqual(7, data.DisplayDayNumber(n.Id), "Day 7");
            Assert.IsFalse(n.tutorial);
            Assert.IsFalse(n.friday13);
            Assert.That(n.wishStars, Is.InRange(4, 5), "about five wishes");
            Assert.IsTrue(data.Days.Where(d => !d.starNight).All(d => d.wishStars == 0), "wish stars only on the star night");
            foreach (var t in new[] { n.title, n.titleKo, n.intro, n.introKo }) StringAssert.Contains("7", t);
            StringAssert.Contains("wish", n.intro);
            StringAssert.Contains("\uC18C\uC6D0", n.introKo);
            if (data.HasExtendedDays)
            {
                Assert.IsNotEmpty(n.shopStickers, "the shop after the star night opens the winter season");
                CollectionAssert.Contains(n.shopStickers, "any_to_11", "Singles' Day (day 8) needs its 11 on sale the night before");
            }
            else Assert.IsEmpty(n.shopStickers, "no shop after the last day");
            Assert.IsTrue(n.spawns.Any(s => s.guest == "mystery_guest"), "the night of the 4th-floor guest");
            // the mystery guest is rare now: the hint lives in mysteryIntro (shown only when it comes), not in the intro
            Assert.IsFalse(string.IsNullOrWhiteSpace(n.mysteryIntro), "mysteryIntro");
            Assert.IsFalse(string.IsNullOrWhiteSpace(n.mysteryIntroKo), "mysteryIntroKo");
            Assert.IsFalse(n.intro.Contains(n.mysteryIntro), "the plain intro must not promise the mystery guest");
            Assert.IsFalse(n.introKo.Contains(n.mysteryIntroKo), "the plain intro must not promise the mystery guest (ko)");
            Assert.IsTrue(data.Days.Where(d => d != n).All(d => string.IsNullOrEmpty(d.mysteryIntro) && string.IsNullOrEmpty(d.mysteryIntroKo)),
                "only the mystery guest's night has a mystery intro");

            var r = data.Rules;
            Assert.That(r.wishStarSeconds, Is.InRange(2f, 5f), "a wish window a person can hit");
            Assert.That(r.wishLuckBonus, Is.InRange(1, 10));
            // the stars fit inside the day (GameSession margin on both ends)
            Assert.Greater(n.durationSeconds, 2f * GameSession.WishStarMarginSeconds + n.wishStars * r.wishStarSeconds);
            // the theme: some scheduled straight rides are exactly 7 floors, for someone lucky only because of the stars
            var sevens = n.spawns.Where(s => DirectDistance(s) == StoryRules.StarNightNumber).ToArray();
            Assert.GreaterOrEqual(sevens.Length, 2, "a few 7-floor rides tonight");
            Assert.IsTrue(sevens.Any(s => Profile(s).Judge(7) != LuckVerdict.Lucky), "someone gets a 7 only the stars make lucky");
        }

        /// <summary>
        /// Day 6, the wedding day: even numbers are lucky for the Chinese wedding guests (6, 8; "good things come in pairs"),
        /// odd ones for the Japanese guest's gift money (3, 5, 7: an odd sum cannot be split in two). A head count that pleases
        /// one side leaves the other cold; both fear 4.
        /// </summary>
        [Test]
        public void WeddingDay_EvenVersusOdd()
        {
            var cn = data.GetGuest(new GuestArchetypeId("chinese_wedding_party"));
            var jp = data.GetGuest(new GuestArchetypeId("japanese_wedding_guest"));
            Assert.IsTrue(cn.lucky.Length > 0 && cn.lucky.All(x => x % 2 == 0), "Chinese wedding: even numbers");
            CollectionAssert.AreEquivalent(new[] { 6, 8 }, cn.lucky);
            Assert.IsTrue(jp.lucky.Length > 0 && jp.lucky.All(x => x % 2 == 1), "Japanese wedding gift: odd numbers");
            CollectionAssert.AreEquivalent(new[] { 3, 5, 7 }, jp.lucky);
            CollectionAssert.Contains(cn.unlucky, 4);
            CollectionAssert.Contains(jp.unlucky, 4);
            CollectionAssert.Contains(jp.unlucky, 9);
            Assert.AreEqual(2, cn.partySize, "the Chinese guests come as a pair");

            var day = data.Days.First(d => d.spawns.Any(s => s.guest == cn.id));
            Assert.AreEqual(day, data.Days.First(d => d.spawns.Any(s => s.guest == jp.id)), "both arrive on the same day");
            Assert.AreEqual(6, data.DisplayDayNumber(day.Id), "the wedding is Day 6");
            foreach (var t in new[] { day.intro, day.introKo }) { StringAssert.Contains("6, 8", t); StringAssert.Contains("3, 5, 7", t); }
            // the opening: a pair and the Japanese guest board together -> 3 riders, lucky for one side only
            var open = day.spawns.Where(s => s.time <= 5f).ToArray();
            Assert.IsTrue(open.Any(s => s.guest == cn.id) && open.Any(s => s.guest == jp.id) && open.Select(s => s.floor).Distinct().Count() == 1,
                "Day 6 opens with both wedding guests on one floor");
            int heads = open.Sum(s => Guest(s).partySize);
            Assert.AreNotEqual(data.GetLuckProfile(cn.Id).Judge(heads), data.GetLuckProfile(jp.Id).Judge(heads), "one side's lucky head count");
        }

        [Test]
        public void MysteryGuest_OnlyOnLastDay_LinesPointAtFour()
        {
            var mystery = data.Guests.Single(g => g.HasTrait(GuestTraits.Mystery));
            Assert.IsTrue(mystery.hideNumbers);
            for (int n = 1; n <= data.AllDayCount; n++)
                if (n != data.DayCount)
                    Assert.IsFalse(data.GetDay(new DayId(n)).spawns.Any(s => s.guest == mystery.id), "mystery guest on day " + n + " (only the base run's last day)");
            AssertAllName(mystery, GuestLineTriggers.Spawn, new[] { 4 }, "spawn line must ask for the 4th floor");
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
                var variants = LinesOf(mystery, trigger);
                Assert.IsNotEmpty(variants, "mystery guest needs a '" + trigger + "' line");
                foreach (var line in variants)
                {
                    Assert.IsTrue(line.text.IndexOf("hid", StringComparison.OrdinalIgnoreCase) >= 0, trigger + ": should talk about hiding - " + line.text);
                    Assert.IsTrue(line.textKo.Contains("숨") || line.textKo.Contains("가려") || line.textKo.Contains("가렸"),
                        trigger + ".textKo: should talk about hiding - " + line.textKo);
                }
            }
            Assert.AreNotEqual(mystery.FindLine(GuestLineTriggers.Delivered).text, mystery.FindLine(GuestLineTriggers.Special).text,
                "plain delivery and the ending need different lines");
            Assert.IsNotNull(mystery.FindLine(GuestLineTriggers.Relabelled), "mystery guest should react to stickers");

            var fourToF = data.Stickers.Single(s => s.from.Trim() == "4");
            for (int n = data.TutorialDayCount + 1; n < data.DayCount; n++) // the training shift has no shop
                CollectionAssert.Contains(data.GetDay(new DayId(n)).shopStickers, fourToF.id,
                    "day " + n + ": 4 -> F must be on sale at every shop (the ending stays reachable)");
        }
    }
}
