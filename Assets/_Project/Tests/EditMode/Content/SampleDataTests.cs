using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// The REAL Resources/Data/*.json must load and validate. content-designer: extend with balance checks
    /// (e.g. every day can be cleared, the tutorial has exactly 3 guests).
    /// </summary>
    public class SampleDataTests
    {
        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        [Test]
        public void Hotel_IsNineFloorsCapacityEight()
        {
            Assert.AreEqual(9, data.FloorCount);
            Assert.AreEqual(8, data.Capacity);
            Assert.AreEqual(new FloorLabel("4"), data.GetFloor(FloorIndex.FromPhysicalNumber(4)).InitialLabel, "GDD 4: 4 and 9 exist from the start");
            Assert.AreEqual(new FloorLabel("9"), data.GetFloor(FloorIndex.FromPhysicalNumber(9)).InitialLabel);
        }

        [TestCase("korean_grandma")]
        [TestCase("chinese_businessman")]
        [TestCase("western_tourist")]
        [TestCase("japanese_couple")]
        [TestCase("mystery_guest")]
        [TestCase("western_priest")]
        [TestCase("italian_nonno")]
        [TestCase("chinese_wedding_party")]
        [TestCase("japanese_wedding_guest")]
        [TestCase("solo_traveler")]
        [TestCase("santa")]
        [TestCase("carolers")]
        public void MvpGuest_Exists(string id)
        {
            Assert.IsTrue(data.TryGetGuest(new GuestArchetypeId(id), out _), id);
        }

        [Test]
        public void JapaneseCouple_TravelsAsTwo()
        {
            Assert.AreEqual(2, data.GetGuest(new GuestArchetypeId("japanese_couple")).partySize);
        }

        [Test]
        public void SevenDays_PlusDay0Tutorial()
        {
            Assert.AreEqual(8, data.DayCount, "Day 0 training shift + 7 real days");
            Assert.AreEqual(1, data.TutorialDayCount);
            Assert.AreEqual(7, data.PlayableDayCount);
            Assert.IsTrue(data.GetDay(DayId.First).tutorial);
            Assert.AreEqual(0, data.DisplayDayNumber(DayId.First), "internal day 1 reads Day 0");
            Assert.IsFalse(data.GetDay(DayId.First.Next).tutorial, "the first real day has a clock");
            Assert.AreEqual(1, data.DisplayDayNumber(DayId.First.Next));
        }

        [Test]
        public void MysteryGuest_AppearsOnLastDay_HeadingToRealFourth()
        {
            var last = data.GetDay(new DayId(data.DayCount));
            var spawn = last.spawns.SingleOrDefault(s => s.guest == "mystery_guest");
            Assert.IsNotNull(spawn);
            Assert.AreEqual(4, spawn.destinationFloor);
        }

        [Test]
        public void MvpStickers_Exist()
        {
            Assert.IsTrue(data.TryGetSticker(new StickerId("four_to_f"), out var f));
            Assert.IsTrue(f.CanApplyTo(new FloorLabel("4")));
            Assert.IsTrue(data.TryGetSticker(new StickerId("any_to_8"), out var eight));
            Assert.IsTrue(eight.MatchesAnyLabel);
        }
    }
}
