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
        public void FiveDays_Day1IsTutorial()
        {
            Assert.AreEqual(5, data.DayCount);
            Assert.IsTrue(data.GetDay(DayId.First).tutorial);
            Assert.AreEqual(3, data.GetDay(DayId.First).spawns.Length, "GDD 3: day 1 is a 3-guest tutorial");
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
