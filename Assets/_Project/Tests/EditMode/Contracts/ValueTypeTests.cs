using System;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Contracts
{
    public class ValueTypeTests
    {
        [Test]
        public void FloorIndex_PhysicalNumber_IsOneBased()
        {
            Assert.AreEqual(1, FloorIndex.Ground.PhysicalNumber);
            Assert.AreEqual(3, FloorIndex.FromPhysicalNumber(4).Value);
            Assert.AreEqual(4, FloorIndex.FromPhysicalNumber(4).PhysicalNumber);
            Assert.IsTrue(new FloorIndex(8).IsValidFor(9));
            Assert.IsFalse(new FloorIndex(9).IsValidFor(9));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FloorIndex(-1));
            Assert.IsTrue(new FloorIndex(2) < new FloorIndex(3));
        }

        [Test]
        public void FloorLabel_Original_IsPhysicalNumber()
        {
            Assert.AreEqual("4", FloorLabel.Original(FloorIndex.FromPhysicalNumber(4)).Text);
        }

        [TestCase("8", true, 8)]
        [TestCase("88", true, 88)]
        [TestCase("F", false, 0)]
        [TestCase("12A", false, 0)]
        public void FloorLabel_TryGetNumber(string text, bool ok, int expected)
        {
            var label = new FloorLabel(text);
            Assert.AreEqual(ok, label.TryGetNumber(out var n));
            Assert.AreEqual(expected, n);
        }

        [Test]
        public void FloorLabel_LeadingNumberAndDigits()
        {
            Assert.IsTrue(new FloorLabel("12A").TryGetLeadingNumber(out var n));
            Assert.AreEqual(12, n);
            Assert.IsFalse(new FloorLabel("F").TryGetLeadingNumber(out _));
            Assert.IsTrue(new FloorLabel("888").ContainsDigit(8));
            Assert.IsFalse(new FloorLabel("F").ContainsDigit(4));
        }

        [Test]
        public void FloorLabel_EqualityIsTrimmedOrdinal()
        {
            Assert.AreEqual(new FloorLabel("8"), new FloorLabel(" 8 "));
            Assert.AreNotEqual(new FloorLabel("f"), new FloorLabel("F"));
            Assert.IsTrue(default(FloorLabel).IsEmpty);
            Assert.Throws<ArgumentException>(() => new FloorLabel("  "));
        }

        [Test]
        public void StringIds_CompareTrimmed()
        {
            Assert.AreEqual(new GuestArchetypeId("korean_grandma"), new GuestArchetypeId(" korean_grandma"));
            Assert.AreNotEqual(new StickerId("four_to_f"), new StickerId("any_to_8"));
            Assert.IsTrue(default(DestinationId).IsEmpty);
            Assert.IsTrue(GuestId.None.IsNone);
            Assert.IsTrue(PartyId.None.IsNone);
            Assert.AreEqual(2, DayId.First.Next.Number);
            Assert.Throws<ArgumentOutOfRangeException>(() => new DayId(0));
        }

        [Test]
        public void NumberSet_SortsAndDeduplicates()
        {
            var s = NumberSet.Of(9, 4, 9, 7);
            Assert.AreEqual(3, s.Count);
            Assert.AreEqual("4, 7, 9", s.ToString());
            Assert.IsTrue(s.Contains(7));
            Assert.IsFalse(s.Contains(8));
            Assert.IsTrue(NumberSet.Empty.IsEmpty);
            Assert.IsTrue(s.Overlaps(NumberSet.Of(1, 9)));
            Assert.IsFalse(s.Overlaps(NumberSet.Of(1, 2)));
            Assert.AreEqual(NumberSet.Of(4, 7, 9), s);
        }

        [Test]
        public void LuckProfile_Judge()
        {
            var couple = LuckProfile.Of(new[] { 7 }, new[] { 4, 9 });
            Assert.AreEqual(LuckVerdict.Lucky, couple.Judge(7));
            Assert.AreEqual(LuckVerdict.Unlucky, couple.Judge(9));
            Assert.AreEqual(LuckVerdict.Neutral, couple.Judge(8));
        }

        [Test]
        public void LuckProfile_Judge_UnluckyWinsOnOverlap()
        {
            var weird = LuckProfile.Of(new[] { 4 }, new[] { 4 });
            Assert.AreEqual(LuckVerdict.Unlucky, weird.Judge(4));
        }

        [Test]
        public void Sticker_FourToF_AppliesOnlyToFour()
        {
            var s = new StickerDef { id = "four_to_f", from = "4", to = "F" };
            Assert.IsTrue(s.CanApplyTo(new FloorLabel("4")));
            Assert.IsFalse(s.CanApplyTo(new FloorLabel("5")));
            Assert.IsFalse(s.CanApplyTo(new FloorLabel("F")));
            Assert.AreEqual(new FloorLabel("F"), s.ResultLabel);
        }

        [Test]
        public void Sticker_AnyToEight_AppliesToAnythingButEight()
        {
            var s = new StickerDef { id = "any_to_8", from = StickerDef.AnyLabel, to = "8" };
            Assert.IsTrue(s.MatchesAnyLabel);
            Assert.IsTrue(s.CanApplyTo(new FloorLabel("5")));
            Assert.IsTrue(s.CanApplyTo(new FloorLabel("F")));
            Assert.IsFalse(s.CanApplyTo(new FloorLabel("8")));
        }

        [Test]
        public void HotelLayout_GeometryFollowsDef()
        {
            var layout = new HotelLayout(new HotelLayoutDef { floorHeight = 3f, shaftX = 0f, waitingStartOffsetX = 2f, waitingSlotSpacing = 1f }, 9, 8);
            Assert.AreEqual(9f, layout.GetFloorAnchor(new FloorIndex(3)).Y, 1e-5);
            Assert.AreEqual(7.5f, layout.GetCarPosition(2.5f).Y, 1e-5);
            Assert.AreEqual(2f, layout.GetWaitingSlot(FloorIndex.Ground, 0).X, 1e-5);
            Assert.AreEqual(4f, layout.GetWaitingSlot(FloorIndex.Ground, 2).X, 1e-5);
            Assert.AreEqual(27f, layout.BuildingHeight, 1e-5);

            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 8; i++) Assert.IsTrue(seen.Add(layout.GetCarSlotOffset(i).ToString()), "car slot " + i + " overlaps another");
        }
    }
}
