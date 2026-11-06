using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Contracts
{
    /// <summary>JSON -> DTO (via JsonUtility, as in the game) -> GameData validation. Fixtures are inline, not the real files.</summary>
    public class GameDataParsingTests
    {
        const string Hotel = @"{
  ""floorCount"": 3, ""capacity"": 4,
  ""floors"": [ { ""number"": 1, ""destination"": ""lobby"" }, { ""number"": 2, ""label"": ""F"", ""destination"": ""sauna"" }, { ""number"": 3, ""destination"": ""roof"" } ],
  ""destinations"": [ { ""id"": ""lobby"", ""name"": ""Lobby"" }, { ""id"": ""sauna"", ""name"": ""Sauna"" }, { ""id"": ""roof"", ""name"": ""Roof"" } ],
  ""rules"": { ""startLuck"": 40, ""maxLuck"": 80, ""unluckyFloorLuckPenalty"": 7 },
  ""layout"": { ""floorHeight"": 2.5 }
}";

        const string Guests = @"{ ""guests"": [
  { ""id"": ""granny"", ""lucky"": [3], ""unlucky"": [4], ""patienceSeconds"": 40, ""baseTip"": 5, ""destinations"": [""sauna""], ""traits"": [""tutorial""], ""color"": ""#AABBCC"",
    ""lines"": [ { ""trigger"": ""spawn"", ""text"": ""Hello"", ""textKo"": ""안녕"" } ] },
  { ""id"": ""couple"", ""lucky"": [7], ""unlucky"": [4, 9], ""partySize"": 2, ""destinations"": [""lobby""] },
  { ""id"": ""mystery"", ""hideNumbers"": true }
] }";

        const string Days = @"{ ""days"": [
  { ""day"": 1, ""durationSeconds"": 60, ""tutorial"": true, ""shopStickers"": [""f""],
    ""spawns"": [ { ""time"": 1, ""guest"": ""granny"", ""floor"": 1, ""destination"": ""sauna"" }, { ""time"": 5, ""guest"": ""mystery"", ""floor"": 1, ""destinationFloor"": 2 } ] },
  { ""day"": 2, ""durationSeconds"": 90, ""spawns"": [ { ""time"": 2, ""guest"": ""couple"", ""floor"": 3 } ] }
] }";

        const string Stickers = @"{ ""stickers"": [ { ""id"": ""f"", ""from"": ""4"", ""to"": ""F"", ""price"": 20 }, { ""id"": ""eight"", ""from"": ""*"", ""to"": ""8"", ""price"": 40, ""stock"": -1 } ] }";

        static DictionaryTextSource Source(string hotel = Hotel, string guests = Guests, string days = Days, string stickers = Stickers)
        {
            var s = new DictionaryTextSource();
            if (hotel != null) s.Set(GameDataLoader.HotelFile, hotel);
            if (guests != null) s.Set(GameDataLoader.GuestsFile, guests);
            if (days != null) s.Set(GameDataLoader.DaysFile, days);
            if (stickers != null) s.Set(GameDataLoader.StickersFile, stickers);
            return s;
        }

        static GameData Load(DictionaryTextSource s) => GameDataLoader.Load(s, new UnityJsonDeserializer());

        static GameDataException LoadFails(DictionaryTextSource s) => Assert.Throws<GameDataException>(() => Load(s));

        [Test]
        public void ValidFixture_LoadsAndIndexes()
        {
            var data = Load(Source());
            Assert.AreEqual(3, data.FloorCount);
            Assert.AreEqual(4, data.Capacity);
            Assert.AreEqual(3, data.Guests.Count);
            Assert.AreEqual(2, data.DayCount);
            Assert.AreEqual(2, data.Stickers.Count);

            Assert.AreEqual(new FloorLabel("F"), data.GetFloor(new FloorIndex(1)).InitialLabel);
            Assert.AreEqual(new FloorLabel("1"), data.GetFloor(FloorIndex.Ground).InitialLabel, "empty label = physical number");
            Assert.IsTrue(data.TryFindDestinationFloor(new DestinationId("roof"), out var roof));
            Assert.AreEqual(2, roof.Value);

            var granny = data.GetGuest(new GuestArchetypeId("granny"));
            Assert.IsTrue(granny.HasTrait(GuestTraits.Tutorial));
            Assert.AreEqual("안녕", granny.FindLine(GuestLineTriggers.Spawn).Get(korean: true));
            Assert.AreEqual(LuckVerdict.Unlucky, data.GetLuckProfile(granny.Id).Judge(4));

            var day1 = data.GetDay(DayId.First);
            Assert.IsTrue(day1.tutorial);
            Assert.AreEqual(2, day1.spawns.Length);
            Assert.IsTrue(day1.spawns[1].HasDestinationFloor);
            Assert.AreEqual(1, day1.spawns[1].DestinationFloor.Value);
            Assert.IsTrue(data.IsLastDay(new DayId(2)));

            Assert.IsTrue(data.TryGetSticker(new StickerId("eight"), out var eight));
            Assert.IsTrue(eight.MatchesAnyLabel);
            Assert.AreEqual(-1, eight.stock);
        }

        [Test]
        public void MissingKeys_UseFieldDefaults()
        {
            var data = Load(Source());
            Assert.AreEqual(40, data.Rules.startLuck);
            Assert.AreEqual(7, data.Rules.unluckyFloorLuckPenalty);
            Assert.AreEqual(new RulesDef().luckyFloorTipMultiplier, data.Rules.luckyFloorTipMultiplier, 1e-5, "rules default");
            Assert.AreEqual(2.5f, data.Layout.floorHeight, 1e-5);
            Assert.AreEqual(new HotelLayoutDef().floorWidth, data.Layout.floorWidth, 1e-5, "layout default");
            var couple = data.GetGuest(new GuestArchetypeId("couple"));
            Assert.AreEqual(2, couple.partySize);
            Assert.AreEqual("#FFFFFF", couple.color);
            Assert.AreEqual(1, data.GetGuest(new GuestArchetypeId("granny")).partySize, "partySize default");
            Assert.AreEqual(1, data.GetSticker(new StickerId("f")).stock, "stock default");
        }

        [Test]
        public void MissingFile_IsReported()
        {
            var ex = LoadFails(Source(days: null));
            Assert.IsTrue(ex.Errors.Any(e => e.StartsWith("days.json")), ex.Message);
        }

        [Test]
        public void MalformedJson_IsReported()
        {
            var ex = LoadFails(Source(stickers: "{ \"stickers\": [ { \"id\": "));
            Assert.IsTrue(ex.Errors.Any(e => e.StartsWith("stickers.json")), ex.Message);
        }

        [Test]
        public void LuckyAndUnluckyOverlap_IsRejected()
        {
            var ex = LoadFails(Source(guests: Guests.Replace("\"unlucky\": [4, 9]", "\"unlucky\": [7]")));
            StringAssert.Contains("both lucky and unlucky", ex.Message);
        }

        [Test]
        public void UnknownGuestInSpawn_IsRejected()
        {
            var ex = LoadFails(Source(days: Days.Replace("\"guest\": \"couple\"", "\"guest\": \"ghost\"")));
            StringAssert.Contains("unknown guest 'ghost'", ex.Message);
        }

        [Test]
        public void SpawnOutsideBuilding_IsRejected()
        {
            var ex = LoadFails(Source(days: Days.Replace("\"floor\": 3 }", "\"floor\": 12 }")));
            StringAssert.Contains("floor 12 outside", ex.Message);
        }

        [Test]
        public void FloorCountMismatch_IsRejected()
        {
            var ex = LoadFails(Source(hotel: Hotel.Replace("\"floorCount\": 3", "\"floorCount\": 4")));
            StringAssert.Contains("hotel.floors", ex.Message);
        }

        [Test]
        public void DuplicateIds_AreRejected()
        {
            var ex = LoadFails(Source(stickers: Stickers.Replace("\"id\": \"eight\"", "\"id\": \"f\"")));
            StringAssert.Contains("duplicate id", ex.Message);
        }

        [Test]
        public void UnknownShopSticker_IsRejected()
        {
            var ex = LoadFails(Source(days: Days.Replace("[\"f\"]", "[\"nope\"]")));
            StringAssert.Contains("unknown shop sticker 'nope'", ex.Message);
        }

        [Test]
        public void DayGap_IsRejected()
        {
            var ex = LoadFails(Source(days: Days.Replace("\"day\": 2", "\"day\": 3")));
            StringAssert.Contains("without gaps", ex.Message);
        }

        [Test]
        public void PartyBiggerThanCar_IsRejected()
        {
            var ex = LoadFails(Source(guests: Guests.Replace("\"partySize\": 2", "\"partySize\": 5")));
            StringAssert.Contains("partySize", ex.Message);
        }

        [Test]
        public void AllErrorsAreCollected_NotJustTheFirst()
        {
            var badDays = Days.Replace("\"guest\": \"couple\"", "\"guest\": \"ghost\"").Replace("[\"f\"]", "[\"nope\"]");
            var ex = LoadFails(Source(days: badDays));
            Assert.GreaterOrEqual(ex.Errors.Count, 2, ex.Message);
        }
    }
}
