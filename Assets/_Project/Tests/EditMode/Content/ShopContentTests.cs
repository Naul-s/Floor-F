using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// stickers.json / days.json shop content on the REAL data: every item is well-formed (labels have a 'to', charms a
    /// known effect), prices are sane, every shop pool has more items than offers (rerolls show something new), new items
    /// appear day by day, and Day 5 (Friday the 13th) rains.
    /// </summary>
    public class ShopContentTests
    {
        static readonly string[] KnownEffects = { StickerEffects.Patience, StickerEffects.Speed, StickerEffects.Shield, StickerEffects.Tips };

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        IEnumerable<DayDef> ShopDays() => data.Days.Where(d => !d.tutorial && d.day < data.DayCount);

        [Test]
        public void Stickers_WellFormed_TextInBothLanguages()
        {
            foreach (var s in data.Stickers)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.name), s.id + ".name");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.nameKo), s.id + ".nameKo");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.description), s.id + ".description");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.descriptionKo), s.id + ".descriptionKo");
                Assert.LessOrEqual(s.description.Length, 55, s.id + ": one short line (4 offer cards must fit the shop panel)");
                Assert.LessOrEqual(s.descriptionKo.Length, 30, s.id + ": one short line (ko)");
                Assert.That(s.kind, Is.EqualTo(StickerKinds.Label).Or.EqualTo(StickerKinds.Charm), s.id + ".kind");
                Assert.That(s.weight, Is.GreaterThan(0f).And.LessThan(100f), s.id + ".weight (0 = never offered)");
                Assert.That(s.stock, Is.EqualTo(-1).Or.GreaterThanOrEqualTo(1), s.id + ".stock");
            }
        }

        [Test]
        public void LabelStickers_NeedATo_ThatFitsSomeFloor()
        {
            foreach (var s in data.Stickers.Where(x => !x.IsCharm))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.to), s.id + ": a label sticker needs 'to'");
                Assert.AreNotEqual("-", s.to.Trim(), s.id + ": '-' is the charm placeholder");
                Assert.IsTrue(Enumerable.Range(0, data.FloorCount).Any(f => s.CanApplyTo(data.GetFloor(new FloorIndex(f)).InitialLabel)),
                    s.id + ": fits no floor of the hotel");
                Assert.IsTrue(string.IsNullOrEmpty(s.effect), s.id + ": labels have no effect");
            }
        }

        [Test]
        public void Charms_NeedAKnownEffect_AndASaneAmount()
        {
            var charms = data.Stickers.Where(x => x.IsCharm).ToList();
            Assert.GreaterOrEqual(charms.Count, 4, "one charm per effect at least");
            foreach (var effect in KnownEffects) Assert.IsTrue(charms.Any(c => c.effect == effect), "no charm with effect " + effect);
            foreach (var c in charms)
            {
                CollectionAssert.Contains(KnownEffects, c.effect, c.id + ": unknown effect");
                if (c.effect == StickerEffects.Shield)
                    Assert.That(c.amount, Is.GreaterThanOrEqualTo(1f).And.LessThanOrEqualTo(3f), c.id + ": shields 1-3 verdicts");
                else
                    Assert.That(c.amount, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f), c.id + ": +1..100%");
                Assert.AreEqual(1, c.stock, c.id + ": a charm is bought once (same id = AlreadyActive)");
            }
        }

        [Test]
        public void Prices_Sane()
        {
            foreach (var s in data.Stickers) Assert.That(s.price, Is.InRange(10, 300), s.id + ".price");
            var four = data.Stickers.Single(x => x.id == "four_to_f");
            Assert.AreEqual(data.Stickers.Min(x => x.price), four.price, "the 4 -> F staple is the cheapest thing in the shop");
            var r = data.Rules;
            Assert.AreEqual(4, r.shopOfferCount);
            var session = new GameSession(data, new EventBus(), new Random(1));
            CollectionAssert.AreEqual(new[] { 20, 40, 80 }, Enumerable.Range(0, r.maxRerolls).Select(session.CostOfReroll).ToArray(), "reroll prices");
            foreach (var d in ShopDays())
            {
                var pool = d.shopStickers.Select(id => data.GetSticker(new StickerId(id))).ToList();
                Assert.LessOrEqual(pool.Min(x => x.price), 60, "day " + d.day + ": something affordable after a weak day");
            }
        }

        [Test]
        public void EveryShopDay_HasAPoolBiggerThanTheOffers_GrowingDayByDay()
        {
            var seen = new HashSet<string>();
            int previous = 0;
            foreach (var d in ShopDays())
            {
                var pool = d.shopStickers.Distinct().ToList();
                Assert.Greater(pool.Count, data.Rules.shopOfferCount, "day " + d.day + ": a reroll should show something new");
                Assert.IsTrue(pool.Any(id => data.GetSticker(new StickerId(id)).IsCharm), "day " + d.day + ": a charm on sale");
                Assert.IsTrue(pool.Any(id => !data.GetSticker(new StickerId(id)).IsCharm && data.GetSticker(new StickerId(id)).MatchesAnyLabel),
                    "day " + d.day + ": an any-floor label on sale");
                CollectionAssert.Contains(pool, "four_to_f", "day " + d.day + ": the staple 4 -> F");
                Assert.IsTrue(seen.All(pool.Contains), "day " + d.day + ": items do not vanish from later pools");
                Assert.GreaterOrEqual(pool.Count, previous);
                previous = pool.Count;
                seen.UnionWith(pool);
            }
            CollectionAssert.AreEquivalent(data.Stickers.Select(s => s.id), seen, "every item is on sale some day");
            var firstPool = ShopDays().First().shopStickers;
            Assert.Less(firstPool.Length, seen.Count, "new items appear progressively");
        }

        [Test]
        public void Day5_FridayThe13th_Rains()
        {
            var friday = data.Days.Single(d => d.friday13);
            Assert.AreEqual(5, data.DisplayDayNumber(new DayId(friday.day)));
            Assert.AreEqual("rain", friday.weather);
            Assert.IsTrue(data.Days.Where(d => d != friday).All(d => string.IsNullOrEmpty(d.weather)), "only Friday the 13th rains");

            var s = new GameSession(data, new EventBus(), new Random(1));
            s.StartNewGame();
            for (int guard = 0; guard < 40 && s.CurrentDay.Number != friday.day; guard++)
            {
                if (s.Phase == DayPhase.Intro && s.IsTutorial) { s.SkipTutorial(); continue; }
                if (s.Phase == DayPhase.Intro) { s.BeginDay(); s.EndDayNow(); continue; }
                if (s.Phase == DayPhase.Summary) { s.ContinueFromSummary(); continue; }
                if (s.Phase == DayPhase.Shop) s.FinishShopping();
            }
            Assert.AreEqual(friday.day, s.CurrentDay.Number);
            Assert.IsTrue(s.IsRainy);
        }
    }
}
