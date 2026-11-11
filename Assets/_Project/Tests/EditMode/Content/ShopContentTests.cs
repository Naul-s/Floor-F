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
    /// appear day by day, Day 5 (Friday the 13th) rains and only the winter season snows.
    /// </summary>
    public class ShopContentTests
    {
        static readonly string[] KnownEffects = { StickerEffects.Patience, StickerEffects.Speed, StickerEffects.Shield, StickerEffects.Tips };

        GameData data;

        [OneTimeSetUp]
        public void Load() => data = GameDataResources.Load();

        /// <summary>
        /// Days followed by a shop: every real day but the very last one. The base run's last day (the star night) has the
        /// winter-season shop, reached only through ISeasonFlow.ExtendSeason; the last season day has none.
        /// </summary>
        IEnumerable<DayDef> ShopDays() => data.Days.Where(d => !d.tutorial && d.day < data.AllDayCount);

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
            Assert.IsTrue(data.Days.Where(d => d != friday).All(d => d.weather != "rain"), "only Friday the 13th rains");
            Assert.IsTrue(data.Days.Where(d => !data.IsExtendedDay(d.Id)).Where(d => d != friday).All(d => string.IsNullOrEmpty(d.weather)),
                "the rest of the base run is clear");

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

        /// <summary>Snow is presentation only and belongs to the winter season: "snow" weather iff snow > 0, growing to Christmas.</summary>
        [Test]
        public void Snow_OnlyInTheWinterSeason_GrowingToChristmas()
        {
            float previous = 0f;
            foreach (var d in data.Days)
            {
                Assert.That(d.snow, Is.InRange(0f, 1f), "day " + d.day + ".snow");
                Assert.AreEqual(d.weather == "snow", d.snow > 0f, "day " + d.day + ": weather 'snow' goes with snow > 0");
                Assert.That(d.weather, Is.EqualTo("").Or.EqualTo("rain").Or.EqualTo("snow"), "day " + d.day + ".weather");
                if (!data.IsExtendedDay(d.Id)) { Assert.AreEqual(0f, d.snow, "day " + d.day + ": no snow before the winter season"); continue; }
                Assert.GreaterOrEqual(d.snow, previous, "day " + d.day + ": the snow should not melt before Christmas");
                previous = d.snow;
            }
            if (!data.HasExtendedDays) return;
            var eve = data.GetDay(new DayId(data.AllDayCount));
            Assert.AreEqual(1f, eve.snow, "Christmas Eve: heavy snow on every ledge");
            Assert.IsTrue(data.Days.Any(d => d.snow > 0f && d.snow < 0.5f), "first flurries before the heavy snow");
        }

        /// <summary>The 11 and 12 stickers are on sale the night before the day whose number they are.</summary>
        [Test]
        public void WinterStickers_OnSaleTheNightBefore()
        {
            if (!data.HasExtendedDays) return;
            // Singles' Day is the first winter day; its 11 is sold in the season shop after the star night
            CollectionAssert.Contains(data.GetDay(new DayId(data.DayCount)).shopStickers, "any_to_11", "the shop before Singles' Day sells 11");
            var eve = data.Days.Single(d => d.christmas);
            CollectionAssert.Contains(data.GetDay(new DayId(eve.day - 1)).shopStickers, "any_to_12", "the shop before Christmas Eve sells 12");
            Assert.IsFalse(data.Days.Where(d => d.day < data.DayCount).Any(d => d.shopStickers.Contains("any_to_11") || d.shopStickers.Contains("any_to_12")),
                "winter stickers only appear once the base run is over");
            foreach (var id in new[] { "any_to_11", "any_to_12" })
            {
                var s = data.GetSticker(new StickerId(id));
                Assert.IsTrue(s.MatchesAnyLabel, id + ": any floor");
                Assert.AreEqual(id.Substring("any_to_".Length), s.to.Trim(), id + ".to");
            }
        }
    }
}
