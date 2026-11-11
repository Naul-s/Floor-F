using System;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// The winter season on the REAL Resources/Data/*.json (content-designer): days 8-10 (DayDef.extended) only come after the
    /// star night through ISeasonFlow.ExtendSeason - 8 Singles' Day (solo travellers, 1 and 11), 9 Lottery Night (first snow),
    /// 10 Christmas Eve (Santa, carolers, 12 lucky for everyone, the last day of the season). Pure data reasoning.
    /// </summary>
    public class WinterSeasonTests
    {
        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        // ------------------------------------------------------------ helpers

        DayDef[] Winter() => data.Days.Where(d => data.IsExtendedDay(d.Id)).ToArray();

        int People(DayDef d) => d.spawns.Sum(s => data.GetGuest(s.Guest).partySize);

        GuestDef Guest(string id) => data.GetGuest(new GuestArchetypeId(id));

        /// <summary>Physical 1-based destination of a spawn, or -1 when the guest picks at runtime.</summary>
        int Destination(SpawnDef s)
        {
            if (s.HasDestinationFloor) return s.destinationFloor;
            if (string.IsNullOrWhiteSpace(s.destination)) return -1;
            return data.TryFindDestinationFloor(new DestinationId(s.destination.Trim()), out var f) ? f.PhysicalNumber : -1;
        }

        /// <summary>A guest's numbers as the day judges them (star night 7, DayDef.luckyForAll).</summary>
        LuckProfile Tonight(DayDef d, SpawnDef s)
        {
            var p = data.GetLuckProfile(s.Guest);
            if (d.starNight) p = StarNightRules.Apply(p);
            if (d.luckyForAll > 0) p = ContentBalanceTests.LuckyForAll(p, d.luckyForAll);
            return p;
        }

        LuckVerdict InitialLabelVerdict(DayDef d, SpawnDef s, int physicalFloor)
            => LabelNumbers.Judge(Tonight(d, s), data.GetFloor(FloorIndex.FromPhysicalNumber(physicalFloor)).InitialLabel, out _);

        bool OneTurnReaches(int start, int dest, int distance)
            => Enumerable.Range(1, data.FloorCount).Any(x => Math.Abs(x - start) + Math.Abs(dest - x) == distance);

        // ------------------------------------------------------------ shape

        [Test]
        public void Season_ThreeWinterDays_AfterTheStarNight_EachAStepHarder()
        {
            Assert.AreEqual(3, data.ExtendedDayCount, "days 8, 9, 10");
            Assert.AreEqual(data.PlayableDayCount + data.ExtendedDayCount, data.SeasonPlayableDayCount);
            var star = data.GetDay(new DayId(data.DayCount));
            Assert.IsTrue(star.starNight, "the season follows the star night");

            float duration = star.durationSeconds;
            int people = People(star), spawns = star.spawns.Length;
            foreach (var w in Winter())
            {
                int shown = data.DisplayDayNumber(w.Id);
                Assert.AreEqual(data.PlayableDayCount + (w.day - data.DayCount), shown);
                StringAssert.Contains(shown.ToString(), w.title, "day " + w.day + ".title names its number");
                StringAssert.Contains(shown.ToString(), w.titleKo, "day " + w.day + ".titleKo names its number");
                Assert.IsFalse(w.tutorial || w.friday13 || w.starNight, "day " + w.day + ": no base-run event on a winter day");
                Assert.AreEqual(0, w.wishStars, "day " + w.day + ": wish stars belong to the star night");
                Assert.That(w.durationSeconds, Is.InRange(90f, 120f), "day " + w.day);
                Assert.GreaterOrEqual(w.durationSeconds, duration, "day " + w.day + ": winter days do not get shorter");
                Assert.Greater(People(w), people, "day " + w.day + ": a bit busier than the star night");
                Assert.GreaterOrEqual(w.spawns.Length, spawns, "day " + w.day + ": at least as many arrivals as the star night");
                duration = w.durationSeconds;
            }
            Assert.IsTrue(data.IsLastSeasonDay(Winter().Last().Id));
            Assert.IsFalse(data.IsLastSeasonDay(star.Id));
            Assert.IsTrue(data.IsLastDay(star.Id), "the base run still ends on the star night");
        }

        [Test]
        public void Shops_AfterEveryRealDay_ButTheLastOfTheSeason()
        {
            foreach (var d in data.Days.Where(x => !x.tutorial))
            {
                if (data.IsLastSeasonDay(d.Id)) Assert.IsEmpty(d.shopStickers, "no shop after the last day of the season");
                else Assert.IsNotEmpty(d.shopStickers, "day " + d.day + ": a shop follows");
            }
        }

        /// <summary>
        /// Fair play: a scripted destination never shows a number its guest fears on the initial labels (only stickers can make
        /// one unlucky). The training shift's deliberate 4 -> F lesson is the one exception.
        /// </summary>
        [Test]
        public void ScriptedDestinations_NeverAnUnluckyLabel()
        {
            foreach (var d in data.Days.Where(x => !x.tutorial))
                foreach (var s in d.spawns)
                {
                    int dest = Destination(s);
                    if (dest > 0)
                        Assert.AreNotEqual(LuckVerdict.Unlucky, InitialLabelVerdict(d, s, dest),
                            "day " + d.day + " t=" + s.time + " " + s.guest + ": destination label " + dest + " is unlucky for them");
                }
        }

        // ------------------------------------------------------------ day 8: Singles' Day

        [Test]
        public void SinglesDay_Day8_SoloTravellers_NeverForcedOntoA2()
        {
            var day = data.GetDay(new DayId(data.DayCount + 1));
            Assert.AreEqual(8, data.DisplayDayNumber(day.Id));
            StringAssert.Contains("Singles", day.title);
            foreach (var t in new[] { day.intro, day.introKo }) { StringAssert.Contains("11", t); StringAssert.Contains("2", t); }

            var solo = Guest("solo_traveler");
            CollectionAssert.AreEquivalent(new[] { 1, 11 }, solo.lucky, "11/11: four lonely 1s");
            CollectionAssert.AreEquivalent(new[] { 2 }, solo.unlucky, "a pair");
            Assert.AreEqual(1, solo.partySize, "travels alone");
            CollectionAssert.DoesNotContain(solo.destinations, "restaurant", "the restaurant is on floor 2");
            Assert.AreEqual(day, data.Days.First(d => d.spawns.Any(s => s.guest == solo.id)), "introduced on Singles' Day");

            var counts = day.spawns.GroupBy(s => s.guest).ToDictionary(g => g.Key, g => g.Count());
            Assert.GreaterOrEqual(counts[solo.id], 5, "a day full of solo travellers");
            Assert.AreEqual(counts.Values.Max(), counts[solo.id], "the most common guest tonight");
            Assert.GreaterOrEqual(counts.TryGetValue("japanese_couple", out int couples) ? couples : 0, 2, "...and couples everywhere");

            var soloRides = data.Days.SelectMany(d => d.spawns.Select(s => (d, s))).Where(x => x.s.guest == solo.id).ToArray();
            foreach (var (d, s) in soloRides)
            {
                var at = "day " + d.day + " t=" + s.time;
                Assert.AreNotEqual(LuckVerdict.Unlucky, InitialLabelVerdict(d, s, s.floor), at + ": a solo traveller waits on a 2");
                int dest = Destination(s);
                Assert.Greater(dest, 0, at + ": solo rides are scripted");
                Assert.AreNotEqual(LuckVerdict.Unlucky, ContentBalanceTests.JudgeDistance(d, Tonight(d, s), Math.Abs(dest - s.floor)),
                    at + ": a straight solo ride is 2 floors");
            }
            // the lessons happen: a 1-floor ride, a ride to the lobby's 1, and a ride alone is always a lucky head count
            Assert.IsTrue(soloRides.Any(x => Math.Abs(Destination(x.s) - x.s.floor) == 1), "a lucky 1-floor solo ride");
            Assert.IsTrue(soloRides.Any(x => InitialLabelVerdict(x.d, x.s, Destination(x.s)) == LuckVerdict.Lucky), "a solo ride to a lucky 1");
            Assert.AreEqual(LuckVerdict.Lucky, data.GetLuckProfile(solo.Id).Judge(1), "riding alone = 1");
        }

        // ------------------------------------------------------------ day 9: Lottery Night

        [Test]
        public void LotteryNight_Day9_TwoDraws_FirstSnow()
        {
            var nights = data.Days.Where(d => d.lottery).ToArray();
            Assert.AreEqual(1, nights.Length, "one lottery night");
            var n = nights[0];
            Assert.IsTrue(data.IsExtendedDay(n.Id));
            Assert.AreEqual(9, data.DisplayDayNumber(n.Id));
            Assert.That(n.lotteryDraws, Is.InRange(1, 3), "a couple of draws");
            Assert.IsFalse(n.christmas);
            Assert.AreEqual(0, n.luckyForAll, "the lottery is tonight's twist");
            Assert.That(n.snow, Is.GreaterThan(0f).And.LessThan(0.5f), "first flurries");
            StringAssert.Contains("jackpot", n.intro);
            StringAssert.Contains("잭팟", n.introKo); // 잭팟
            Assert.GreaterOrEqual(n.spawns.Count(s => data.GetGuest(s.Guest).HasTrait(GuestTraits.Gambler)), 2, "a gambler's night");

            var r = data.Rules;
            Assert.Greater(r.lotteryTipMultiplier, 1f, "a jackpot pays more");
            Assert.That(r.lotteryLuckBonus, Is.InRange(1, 10));
        }

        // ------------------------------------------------------------ day 10: Christmas Eve

        [Test]
        public void ChristmasEve_Day10_SantaAndCarolers_12ForEveryone()
        {
            var eves = data.Days.Where(d => d.christmas).ToArray();
            Assert.AreEqual(1, eves.Length, "one Christmas Eve");
            var eve = eves[0];
            Assert.IsTrue(data.IsLastSeasonDay(eve.Id), "the last day of the season");
            Assert.AreEqual(10, data.DisplayDayNumber(eve.Id));
            Assert.AreEqual(12, eve.luckyForAll, "12 days of Christmas, lucky for everyone");
            Assert.IsEmpty(eve.shopStickers);
            foreach (var t in new[] { eve.intro, eve.introKo }) { StringAssert.Contains("12", t); StringAssert.Contains("25", t); }
            Assert.IsFalse(data.Guests.Any(g => g.unlucky.Contains(eve.luckyForAll)), "nobody's own superstition fights tonight's 12");

            var santa = Guest("santa");
            CollectionAssert.IsSupersetOf(santa.lucky, new[] { 12, 25 });
            Assert.IsEmpty(santa.unlucky, "Santa fears nothing");
            Assert.IsTrue(santa.HasTrait(GuestTraits.VIP));
            Assert.IsTrue(data.Guests.Where(g => g != santa).All(g => g.baseTip < santa.baseTip), "Santa tips the most");
            var carolers = Guest("carolers");
            Assert.AreEqual(3, carolers.partySize, "carolers come in threes");
            CollectionAssert.IsSupersetOf(carolers.lucky, new[] { 3, 12 });
            CollectionAssert.AreEquivalent(new[] { 4 }, carolers.unlucky);
            Assert.AreEqual(LuckVerdict.Lucky, data.GetLuckProfile(carolers.Id).Judge(carolers.partySize), "the trio alone is a lucky head count");

            foreach (var d in data.Days.Where(x => x != eve))
                Assert.IsFalse(d.spawns.Any(s => s.guest == santa.id || s.guest == carolers.id), "day " + d.day + ": Christmas guests out of season");
            var santas = eve.spawns.Where(s => s.guest == santa.id).ToArray();
            Assert.IsNotEmpty(santas);
            Assert.IsTrue(santas.All(s => Destination(s) == data.FloorCount), "Santa's sleigh waits on the roof");
            Assert.IsTrue(santas.Any(s => OneTurnReaches(s.floor, Destination(s), eve.luckyForAll)),
                "some Santa ride can be stretched to 12 floors with one turn");
            Assert.GreaterOrEqual(eve.spawns.Count(s => s.guest == carolers.id), 3, "carolling all evening");
        }
    }
}
