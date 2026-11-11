using System;
using FloorF.Core;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>Endings collection record (EndingRecord / EndingCollection / stores) and its strings.</summary>
    public class EndingRecordTests
    {
        [TearDown]
        public void TearDown() => EndingCollection.StoreOverride = null;

        [Test]
        public void Empty_NothingUnlocked_SixEndings_OnlyFourthFloorHidden()
        {
            var r = new EndingRecord();
            Assert.AreEqual(6, EndingRecord.Total);
            CollectionAssert.AreEqual(new[] { EndingKind.Prosperous, EndingKind.Modest, EndingKind.Haunted, EndingKind.WinterMiracle, EndingKind.SilentNight, EndingKind.FourthFloor }, EndingRecord.All);
            Assert.AreEqual(0, r.UnlockedCount);
            foreach (var k in EndingRecord.All)
            {
                Assert.IsFalse(r.IsUnlocked(k));
                Assert.AreEqual(0, r.Times(k));
                Assert.AreEqual(k == EndingKind.FourthFloor, EndingRecord.IsHidden(k), k.ToString());
            }
            Assert.IsFalse(EndingRecord.IsHidden(EndingKind.None));
            Assert.AreEqual("", r.Serialize());
        }

        [Test]
        public void Add_UnlocksOnce_CountsEveryTime_IgnoresNone()
        {
            var r = new EndingRecord();
            Assert.IsTrue(r.Add(EndingKind.Modest), "first time = a new unlock");
            Assert.IsFalse(r.Add(EndingKind.Modest), "second time = just counted");
            Assert.IsTrue(r.Add(EndingKind.FourthFloor));
            Assert.IsFalse(r.Add(EndingKind.None));
            Assert.IsFalse(r.Add((EndingKind)42));
            Assert.AreEqual(2, r.Times(EndingKind.Modest));
            Assert.AreEqual(1, r.Times(EndingKind.FourthFloor));
            Assert.AreEqual(0, r.Times(EndingKind.None));
            Assert.AreEqual(2, r.UnlockedCount);
            Assert.IsTrue(r.IsUnlocked(EndingKind.FourthFloor));
            Assert.IsFalse(r.IsUnlocked(EndingKind.Prosperous));
        }

        [Test]
        public void Serialize_Parse_RoundTrip()
        {
            var r = new EndingRecord();
            for (int i = 0; i < 3; i++) r.Add(EndingKind.Prosperous);
            r.Add(EndingKind.Haunted);
            r.Add(EndingKind.FourthFloor);
            string s = r.Serialize();
            Assert.AreEqual("1:3,3:1,4:1", s);
            var back = EndingRecord.Parse(s);
            Assert.AreEqual(3, back.UnlockedCount);
            Assert.AreEqual(3, back.Times(EndingKind.Prosperous));
            Assert.AreEqual(0, back.Times(EndingKind.Modest));
            Assert.AreEqual(1, back.Times(EndingKind.Haunted));
            Assert.AreEqual(1, back.Times(EndingKind.FourthFloor));
            Assert.AreEqual(s, back.Serialize());
        }

        [Test]
        public void Parse_ToleratesCorruptValues()
        {
            foreach (var junk in new[] { null, "", "   ", "garbage", ",,,", ":", "1:", ":3", "x:y", "{\"a\":1}", "0:5", "9:3", "-1:2", "2:0", "2:-4" })
            {
                EndingRecord r = null;
                Assert.DoesNotThrow(() => r = EndingRecord.Parse(junk), "'" + junk + "'");
                Assert.AreEqual(0, r.UnlockedCount, "'" + junk + "' unlocks nothing");
            }
            // the readable parts survive the junk around them
            var mixed = EndingRecord.Parse(" 1 : 2 ,banana,9:3,2:-1,,3:5,3:2, 4:abc ,4:1");
            Assert.AreEqual(2, mixed.Times(EndingKind.Prosperous), "spaces allowed");
            Assert.AreEqual(0, mixed.Times(EndingKind.Modest), "negative count skipped");
            Assert.AreEqual(5, mixed.Times(EndingKind.Haunted), "duplicate keeps the larger count");
            Assert.AreEqual(1, mixed.Times(EndingKind.FourthFloor), "the good 4:1 after a bad 4:abc");
            Assert.AreEqual(3, mixed.UnlockedCount);
            // absurd counts are clamped, never overflow
            var big = EndingRecord.Parse("1:99999999999999");
            Assert.AreEqual(EndingRecord.MaxCount, big.Times(EndingKind.Prosperous));
            big.Add(EndingKind.Prosperous);
            Assert.AreEqual(EndingRecord.MaxCount, big.Times(EndingKind.Prosperous));
        }

        [Test]
        public void Collection_LoadsFromStore_SavesEveryEnding_LastNew()
        {
            var store = new MemoryEndingStore("2:1");
            var c = new EndingCollection(store);
            Assert.AreEqual(1, c.Unlocked);
            Assert.AreEqual(6, EndingCollection.Total);
            Assert.AreEqual(EndingKind.None, c.LastNew);

            Assert.IsTrue(c.Add(EndingKind.FourthFloor));
            Assert.AreEqual(EndingKind.FourthFloor, c.LastNew);
            Assert.AreEqual("2:1,4:1", store.Value, "saved at once");
            Assert.IsFalse(c.Add(EndingKind.Modest));
            Assert.AreEqual(EndingKind.None, c.LastNew, "seen before: nothing new");
            Assert.AreEqual("2:2,4:1", store.Value);
            Assert.AreEqual(2, store.Saves);

            // a relaunch reads what was saved
            var again = new EndingCollection(store);
            Assert.AreEqual(2, again.Unlocked);
            Assert.AreEqual(2, again.Record.Times(EndingKind.Modest));
        }

        sealed class BrokenStore : IEndingStore
        {
            public string Load() => throw new InvalidOperationException("disk on fire");
            public void Save(string value) => throw new InvalidOperationException("disk on fire");
        }

        [Test]
        public void Collection_StorageErrors_AreLoggedNotThrown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                EndingCollection c = null;
                Assert.DoesNotThrow(() => c = new EndingCollection(new BrokenStore()));
                Assert.AreEqual(0, c.Unlocked);
                Assert.DoesNotThrow(() => c.Add(EndingKind.Haunted));
                Assert.AreEqual(1, c.Unlocked, "still counted for this session");
                Assert.DoesNotThrow(() => new EndingCollection(null).Add(EndingKind.Modest), "no store at all");
            }
            finally { UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void PlayerPrefsStore_RoundTrip_PrivateKey()
        {
            var store = new PlayerPrefsEndingStore("floorf.endings.test." + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsNull(store.Load(), "no key yet");
                var c = new EndingCollection(store);
                c.Add(EndingKind.Prosperous);
                c.Add(EndingKind.Prosperous);
                Assert.AreEqual("1:2", PlayerPrefs.GetString(store.Key));
                Assert.AreEqual(2, new EndingCollection(store).Record.Times(EndingKind.Prosperous));
                PlayerPrefs.SetString(store.Key, "%%corrupt%%");
                Assert.AreEqual(0, new EndingCollection(store).Unlocked, "a corrupt key reads as nothing found");
            }
            finally { store.Delete(); }
            Assert.IsFalse(PlayerPrefs.HasKey(store.Key), "test key removed");
            Assert.AreEqual("floorf.endings.v1", PlayerPrefsEndingStore.DefaultKey);
        }

        [Test]
        public void CreateStore_OverrideFirst_BatchModeNeverTouchesPlayerPrefs()
        {
            var mem = new MemoryEndingStore();
            EndingCollection.StoreOverride = mem;
            Assert.AreSame(mem, EndingCollection.CreateStore());
            EndingCollection.StoreOverride = null;
            if (Application.isBatchMode) Assert.IsInstanceOf<MemoryEndingStore>(EndingCollection.CreateStore());
            else Assert.IsInstanceOf<PlayerPrefsEndingStore>(EndingCollection.CreateStore());
        }

        [Test]
        public void WinterEndings_AreCounted_Saved_AndReadBack()
        {
            var r = new EndingRecord();
            Assert.IsTrue(r.Add(EndingKind.Prosperous));
            Assert.IsTrue(r.Add(EndingKind.WinterMiracle), "a base ending and then the winter one in the same run");
            Assert.IsTrue(r.Add(EndingKind.SilentNight));
            Assert.IsFalse(r.Add(EndingKind.SilentNight));
            Assert.AreEqual(3, r.UnlockedCount);
            string s = r.Serialize();
            Assert.AreEqual("1:1,5:1,6:2", s);
            var back = EndingRecord.Parse(s + ",7:3");
            Assert.AreEqual(2, back.Times(EndingKind.SilentNight));
            Assert.AreEqual(1, back.Times(EndingKind.WinterMiracle));
            Assert.AreEqual(3, back.UnlockedCount, "an unknown future kind is skipped");
        }

        [Test]
        public void Strings_NamesLinesHints_EnKo()
        {
            foreach (bool ko in new[] { false, true })
                foreach (var k in EndingRecord.All)
                {
                    Assert.IsNotEmpty(UIFormat.EndingName(k, ko));
                    Assert.IsNotEmpty(UIFormat.EndingShortLine(k, ko));
                    Assert.IsNotEmpty(UIFormat.EndingHint(k, ko, 7, 70));
                }
            Assert.AreEqual("PROSPEROUS", UIFormat.EndingName(EndingKind.Prosperous, false));
            Assert.AreEqual("번창", UIFormat.EndingName(EndingKind.Prosperous, true));
            Assert.AreEqual("보통", UIFormat.EndingName(EndingKind.Modest, true));
            Assert.AreEqual("흉가", UIFormat.EndingName(EndingKind.Haunted, true));
            Assert.AreEqual("4층", UIFormat.EndingName(EndingKind.FourthFloor, true));
            // the Luck endings state their rule from the data
            Assert.AreEqual("Finish Day 6 with Luck 65 or more.", UIFormat.EndingHint(EndingKind.Prosperous, false, 6, 65));
            Assert.AreEqual("6일차를 운 65 이상으로 마치기.", UIFormat.EndingHint(EndingKind.Prosperous, true, 6, 65));
            Assert.AreEqual("Finish Day 6 with Luck under 65.", UIFormat.EndingHint(EndingKind.Modest, false, 6, 65));
            StringAssert.Contains("0", UIFormat.EndingHint(EndingKind.Haunted, false, 6, 65));
            // the hidden one only whispers: no rule, no numbers
            string hidden = UIFormat.EndingHint(EndingKind.FourthFloor, false, 6, 65);
            Assert.AreEqual("Not on your first stay. Some nights, someone asks for a floor that isn't there…", hidden);
            Assert.AreEqual("첫 방문엔 볼 수 없어요. 어떤 밤엔 누군가 없는 층을 찾습니다…", UIFormat.EndingHint(EndingKind.FourthFloor, true, 6, 65));
            // the winter endings: Christmas Eve, the miracle with the prosperous Luck
            Assert.AreEqual("Keep the hotel open through Christmas Eve with Luck 65+.", UIFormat.EndingHint(EndingKind.WinterMiracle, false, 6, 65));
            Assert.AreEqual("영업을 이어가 크리스마스 이브를 운 65 이상으로 마치기.", UIFormat.EndingHint(EndingKind.WinterMiracle, true, 6, 65));
            Assert.AreEqual("Keep the hotel open through Christmas Eve.", UIFormat.EndingHint(EndingKind.SilentNight, false, 6, 65));
            Assert.AreEqual("MIRACLE", UIFormat.EndingName(EndingKind.WinterMiracle, false));
            Assert.AreEqual("고요한 밤", UIFormat.EndingName(EndingKind.SilentNight, true));
            UIFormat.Ending(EndingKind.WinterMiracle, false, out var mt, out _);
            UIFormat.Ending(EndingKind.SilentNight, true, out var st, out _);
            Assert.AreEqual("A CHRISTMAS MIRACLE", mt);
            Assert.AreEqual("고요한 밤", st);
            Assert.IsTrue(UIFormat.IsWinterEnding(EndingKind.WinterMiracle) && UIFormat.IsWinterEnding(EndingKind.SilentNight));
            Assert.IsFalse(UIFormat.IsWinterEnding(EndingKind.Prosperous));
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(hidden, "[0-9]"));
            Assert.AreEqual("Endings 3/4 unlocked", string.Format(UIStrings.EndingsUnlocked.En, 3, 4));
            Assert.AreEqual("엔딩 3/4 해금", string.Format(UIStrings.EndingsUnlocked.Ko, 3, 4));
            Assert.AreEqual("엔딩 2/4", string.Format(UIStrings.EndingsButton.Ko, 2, 4));
            Assert.AreEqual("HIDDEN ENDING", UIStrings.HiddenEnding.En);
            Assert.AreEqual("히든 엔딩", UIStrings.HiddenEnding.Ko);
        }
    }
}
