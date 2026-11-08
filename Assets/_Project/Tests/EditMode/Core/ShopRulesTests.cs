using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using static FloorF.Tests.EditMode.Core.Sim;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Shop rework (IShopService): one-day purchases, random offers + rerolls, charms. Fixture: SimFixture plus the
    /// stickers below; 3 days (shops after days 1 and 2), lots of tips, every offer visible unless a test lowers shopOfferCount.
    /// </summary>
    public class ShopRulesTests
    {
        static readonly string[] Pool = { "four_to_f", "any_to_8", "any_to_3", "any_to_7", "any_to_9", "tea", "tune", "shield", "cat" };

        static StickerDef Label(string id, string from, string to, int price, int stock, float weight = 1f)
            => new StickerDef { id = id, name = id, from = from, to = to, price = price, stock = stock, weight = weight };

        // GameData validation still wants a 'to' on every sticker (CONTRACT REQUEST: skip it for charms)
        static StickerDef Charm(string id, string effect, float amount, int price = 10)
            => new StickerDef { id = id, name = id, kind = StickerKinds.Charm, effect = effect, amount = amount, to = "-", price = price, stock = 1 };

        static void ShopData(SimFixture.Defs d)
        {
            var r = d.Hotel.rules;
            r.startTips = 1000;
            r.shopOfferCount = 99;
            r.rerollBaseCost = 20;
            r.rerollCostMultiplier = 2f;
            r.maxRerolls = 3;
            d.Stickers.stickers = new[]
            {
                Label("four_to_f", "4", "F", 20, 1),
                Label("any_to_8", "*", "8", 40, 1),
                Label("any_to_3", "*", "3", 10, 2),
                Label("any_to_7", "*", "7", 30, 1),
                Label("any_to_9", "*", "9", 30, 1),
                Charm("tea", StickerEffects.Patience, 0.5f),
                Charm("tune", StickerEffects.Speed, 1f),
                Charm("shield", StickerEffects.Shield, 2f),
                Charm("cat", StickerEffects.Tips, 0.5f),
            };
            d.Days.days = new[]
            {
                new DayDef { day = 1, durationSeconds = 1000f, shopStickers = Pool },
                new DayDef { day = 2, durationSeconds = 1000f, shopStickers = Pool },
                new DayDef { day = 3, durationSeconds = 1000f, shopStickers = new string[0] },
            };
        }

        static Sim InShop(Action<SimFixture.Defs> tweak = null, int seed = 1)
        {
            var sim = new Sim(SimFixture.Create(d => { ShopData(d); tweak?.Invoke(d); }), seed);
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            sim.EndDayToShop();
            return sim;
        }

        static StickerId Id(string id) => new StickerId(id);

        static string[] OfferIds(Sim sim) => sim.S.Offers.Select(o => o.Id.Value).ToArray();

        /// <summary>Shop -> next day Running.</summary>
        static void NextDay(Sim sim)
        {
            Assert.IsTrue(sim.S.FinishShopping());
            Assert.IsTrue(sim.S.BeginDay());
        }

        // ------------------------------------------------------------------ one-day purchases

        [Test]
        public void Label_ActiveNextDay_RevertsAtItsEnd_BeforeSummary()
        {
            var sim = InShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(Id("any_to_8"), F(5)));
            Assert.AreEqual(new FloorLabel("8"), sim.S.Building.GetLabel(F(5)), "visible at once in the shop");
            NextDay(sim);
            Assert.AreEqual(new FloorLabel("8"), sim.S.Building.GetLabel(F(5)), "active during the next day");

            sim.Log.Clear();
            sim.S.EndDayNow();
            Assert.AreEqual(new FloorLabel("5"), sim.S.Building.GetLabel(F(5)));
            Assert.IsFalse(sim.S.Building.IsRelabelled(F(5)));
            var changed = sim.Events<FloorLabelChanged>().Single();
            Assert.AreEqual(F(5), changed.Floor);
            Assert.AreEqual(new FloorLabel("8"), changed.Previous);
            Assert.AreEqual(new FloorLabel("5"), changed.Current);
            var expired = sim.Events<StickersExpired>().Single();
            Assert.AreEqual(1, expired.Labels);
            Assert.AreEqual(0, expired.Charms);
            sim.AssertOrder(typeof(DayEnded), typeof(FloorLabelChanged), typeof(StickersExpired), typeof(DayPhaseChanged));
            Assert.AreEqual(DayPhase.Summary, sim.S.Phase);
        }

        [Test]
        public void Charm_ActiveNextDay_ClearedAtItsEnd()
        {
            var sim = InShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("tea")));
            CollectionAssert.AreEqual(new[] { "tea" }, sim.S.ActiveCharms.Select(c => c.id).ToArray(), "bought for tomorrow");
            NextDay(sim);
            Assert.AreEqual(1, sim.S.ActiveCharms.Count, "active during the next day");
            sim.Log.Clear();
            sim.S.EndDayNow();
            Assert.IsEmpty(sim.S.ActiveCharms);
            var expired = sim.Events<StickersExpired>().Single();
            Assert.AreEqual(0, expired.Labels);
            Assert.AreEqual(1, expired.Charms);
            Assert.AreEqual(0, sim.Events<FloorLabelChanged>().Count);
            sim.AssertOrder(typeof(DayEnded), typeof(StickersExpired), typeof(DayPhaseChanged));
        }

        [Test]
        public void NothingBought_NoStickersExpired()
        {
            var sim = InShop();
            NextDay(sim);
            sim.S.EndDayNow();
            Assert.AreEqual(0, sim.Events<StickersExpired>().Count);
        }

        [Test]
        public void TwoVisits_SecondDayStartsWithOriginalLabels()
        {
            var sim = InShop();
            sim.S.TryBuyAndApply(Id("four_to_f"), F(4));
            sim.S.TryBuy(Id("cat"));
            NextDay(sim);
            sim.EndDayToShop();
            Assert.AreEqual(new FloorLabel("4"), sim.S.Building.GetLabel(F(4)), "the shop after the active day starts clean");
            Assert.IsEmpty(sim.S.ActiveCharms);
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("cat")), "the same charm can be bought again for the next day");
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(Id("four_to_f"), F(4)));
        }

        [Test]
        public void HauntedEnding_ExpiresBeforeEndingReached()
        {
            var sim = InShop(d => d.Hotel.rules.startLuck = 10);
            sim.S.TryBuyAndApply(Id("any_to_8"), F(5));
            NextDay(sim);
            sim.Log.Clear();
            sim.Spawn("granny", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(4);
            Assert.IsTrue(sim.S.OpenDoors());
            sim.Run(0.2f);
            Assert.AreEqual(EndingKind.Haunted, sim.S.Ending);
            sim.AssertOrder(typeof(DayEnded), typeof(FloorLabelChanged), typeof(StickersExpired), typeof(EndingReached));
        }

        [Test]
        public void HonestGuest_StillHatesActiveSticker()
        {
            var sim = InShop();
            sim.S.TryBuyAndApply(Id("any_to_3"), F(5));
            NextDay(sim);
            sim.Spawn("honest", 1, 5);
            sim.Open();
            sim.Close();
            sim.GoTo(5);
            sim.Open();
            Assert.AreEqual(LuckSource.Sticker, sim.Events<LuckJudged>().Single().Source);
        }

        [Test]
        public void Restart_ClearsCharms()
        {
            var sim = InShop();
            sim.S.TryBuy(Id("shield"));
            sim.S.RestartGame();
            Assert.IsEmpty(sim.S.ActiveCharms);
        }

        // ------------------------------------------------------------------ random offers

        [Test]
        public void Offers_Count_NoRepeats_PoolOrder_StapleAlwaysIn()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var sim = InShop(d => d.Hotel.rules.shopOfferCount = 3, seed);
                var ids = OfferIds(sim);
                Assert.AreEqual(3, ids.Length, "seed " + seed);
                Assert.AreEqual(ids.Length, ids.Distinct().Count(), "seed " + seed + ": no repeats");
                Assert.AreEqual("four_to_f", ids[0], "seed " + seed + ": the 4 -> F staple is always offered (first in pool order)");
                CollectionAssert.AreEqual(ids.OrderBy(x => Array.IndexOf(Pool, x)).ToArray(), ids, "seed " + seed + ": pool order");
            }
        }

        [Test]
        public void Offers_SameSeed_SameOffers_DifferentSeeds_Vary()
        {
            var sets = new HashSet<string>();
            for (int seed = 1; seed <= 20; seed++)
            {
                var a = string.Join(",", OfferIds(InShop(d => d.Hotel.rules.shopOfferCount = 3, seed)));
                var b = string.Join(",", OfferIds(InShop(d => d.Hotel.rules.shopOfferCount = 3, seed)));
                Assert.AreEqual(a, b, "seed " + seed + ": deterministic");
                sets.Add(a);
            }
            Assert.Greater(sets.Count, 3, "different seeds draw different shops");
        }

        [Test]
        public void Offers_FollowWeights_ZeroNeverDrawn()
        {
            int heavy = 0, light = 0, never = 0;
            const int Runs = 300;
            for (int seed = 1; seed <= Runs; seed++)
            {
                var sim = InShop(d =>
                {
                    d.Hotel.rules.shopOfferCount = 1;
                    d.Stickers.stickers = new[]
                    {
                        Label("heavy", "*", "8", 10, 1, weight: 9f),
                        Label("light", "*", "7", 10, 1, weight: 1f),
                        Label("never", "*", "3", 10, 1, weight: 0f),
                    };
                    foreach (var day in d.Days.days.Take(2)) day.shopStickers = new[] { "heavy", "light", "never" };
                }, seed);
                switch (OfferIds(sim).Single())
                {
                    case "heavy": heavy++; break;
                    case "light": light++; break;
                    default: never++; break;
                }
            }
            TestContext.WriteLine("heavy " + heavy + ", light " + light + ", never " + never);
            Assert.AreEqual(0, never, "weight 0 is never drawn");
            Assert.That(heavy / (double)Runs, Is.InRange(0.82, 0.97), "weight 9 vs 1 -> about 90%");
        }

        [Test]
        public void Offers_PoolDuplicatesIgnored()
        {
            var sim = InShop(d => d.Days.days[0].shopStickers = new[] { "any_to_3", "any_to_3", "four_to_f" });
            CollectionAssert.AreEqual(new[] { "any_to_3", "four_to_f" }, OfferIds(sim));
        }

        // ------------------------------------------------------------------ rerolls

        [Test]
        public void Reroll_CostDoubles_AtMostThree_ResetsNextVisit()
        {
            var sim = InShop(d => d.Hotel.rules.shopOfferCount = 3);
            Assert.AreEqual(3, sim.S.RerollsLeft);
            var costs = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int cost = sim.S.RerollCost;
                int tips = sim.S.Score.Tips;
                sim.Log.Clear();
                Assert.IsTrue(sim.S.Reroll(), "reroll " + (i + 1));
                costs.Add(cost);
                Assert.AreEqual(tips - cost, sim.S.Score.Tips);
                var e = sim.Events<ShopRerolled>().Single();
                Assert.AreEqual(cost, e.Cost);
                Assert.AreEqual(2 - i, e.RerollsLeft);
                Assert.AreEqual(sim.S.RerollCost, e.NextCost);
                sim.AssertOrder(typeof(ShopRerolled), typeof(TipsChanged));
            }
            CollectionAssert.AreEqual(new[] { 20, 40, 80 }, costs);
            Assert.AreEqual(0, sim.S.RerollsLeft);
            Assert.AreEqual(0, sim.S.RerollCost, "no reroll left -> no price");
            int before = sim.S.Score.Tips;
            Assert.IsFalse(sim.S.Reroll(), "a 4th reroll is refused");
            Assert.AreEqual(before, sim.S.Score.Tips);

            NextDay(sim);
            Assert.AreEqual(0, sim.S.RerollsLeft, "closed shop");
            Assert.IsFalse(sim.S.Reroll());
            sim.EndDayToShop();
            Assert.AreEqual(3, sim.S.RerollsLeft, "next visit: rerolls back");
            Assert.AreEqual(20, sim.S.RerollCost, "next visit: price back to the base");
        }

        [Test]
        public void Reroll_NotEnoughTips_Refused()
        {
            var sim = InShop(d => { d.Hotel.rules.startTips = 19; d.Hotel.rules.shopOfferCount = 3; });
            var before = OfferIds(sim);
            sim.Log.Clear();
            Assert.IsFalse(sim.S.Reroll());
            CollectionAssert.AreEqual(before, OfferIds(sim));
            Assert.AreEqual(3, sim.S.RerollsLeft);
            Assert.AreEqual(19, sim.S.Score.Tips);
            Assert.AreEqual(0, sim.Events<ShopRerolled>().Count);
        }

        [Test]
        public void Reroll_ShowsNewStickers_KeepsStaple()
        {
            // pool of 9: staple + 3 drawn -> 5 others left, so a reroll can replace all 3 drawn ones
            var sim = InShop(d => d.Hotel.rules.shopOfferCount = 4);
            var before = OfferIds(sim);
            Assert.IsTrue(sim.S.Reroll());
            var after = OfferIds(sim);
            Assert.AreEqual(4, after.Length);
            Assert.AreEqual("four_to_f", after[0], "the staple survives rerolls");
            CollectionAssert.IsEmpty(before.Skip(1).Intersect(after), "a reroll prefers stickers that were not shown");
        }

        [Test]
        public void Reroll_DoesNotRestock()
        {
            var sim = InShop(d => { d.Hotel.rules.shopOfferCount = 3; d.Days.days[0].shopStickers = new[] { "four_to_f", "any_to_8", "any_to_3" }; });
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(Id("any_to_8"), F(5)));
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuyAndApply(Id("any_to_3"), F(6)));
            Assert.IsTrue(sim.S.Reroll());
            CollectionAssert.AreEqual(new[] { "four_to_f", "any_to_3" }, OfferIds(sim), "the sold-out sticker is not drawn again this visit");
            Assert.AreEqual(1, sim.S.Offers.Single(o => o.Id.Value == "any_to_3").Remaining, "stock counts across rerolls");
        }

        [Test]
        public void Reroll_ClosedOrPaused_Refused()
        {
            var sim = Running(ShopData);
            Assert.IsFalse(sim.S.Reroll(), "not in the shop");
            sim.EndDayToShop();
            Assert.IsTrue(sim.S.SetPaused(true));
            Assert.IsFalse(sim.S.Reroll(), "paused");
            Assert.AreEqual(StickerApplyResult.ShopClosed, sim.S.TryBuy(Id("tea")));
        }

        // ------------------------------------------------------------------ charms: buying

        [Test]
        public void Charm_Buy_EventsAndRules()
        {
            var sim = InShop();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.CanBuy(Id("cat")));
            sim.Log.Clear();
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("cat")));
            var bought = sim.Events<CharmBought>().Single();
            Assert.AreEqual(Id("cat"), bought.Charm);
            Assert.AreEqual(10, bought.Price);
            Assert.AreEqual(990, sim.S.Score.Tips);
            sim.AssertOrder(typeof(CharmBought), typeof(TipsChanged));
            Assert.AreEqual(0, sim.S.Offers.Single(o => o.Id.Value == "cat").Remaining);

            Assert.AreEqual(StickerApplyResult.AlreadyActive, sim.S.TryBuy(Id("cat")), "the same charm does not stack");
            Assert.AreEqual(StickerApplyResult.WrongKind, sim.S.TryBuyAndApply(Id("tea"), F(5)), "charms need no floor");
            Assert.AreEqual(StickerApplyResult.WrongKind, sim.S.TryBuy(Id("any_to_8")), "labels need a floor");
            Assert.AreEqual(StickerApplyResult.UnknownSticker, sim.S.TryBuy(Id("nope")));
            Assert.AreEqual(990, sim.S.Score.Tips);
        }

        [Test]
        public void Charm_NotEnoughTips_NotInShop()
        {
            var sim = InShop(d => { d.Hotel.rules.startTips = 5; d.Days.days[0].shopStickers = new[] { "tea" }; });
            Assert.AreEqual(StickerApplyResult.NotEnoughTips, sim.S.CanBuy(Id("tea")));
            Assert.AreEqual(StickerApplyResult.NotEnoughTips, sim.S.TryBuy(Id("tea")));
            Assert.AreEqual(StickerApplyResult.NotInShop, sim.S.TryBuy(Id("cat")));
            Assert.IsEmpty(sim.S.ActiveCharms);
        }

        [Test]
        public void Charm_RerollDoesNotOfferOwnedCharm()
        {
            var sim = InShop(d => { d.Hotel.rules.shopOfferCount = 2; d.Days.days[0].shopStickers = new[] { "tea", "cat" }; });
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("tea")));
            Assert.IsTrue(sim.S.Reroll());
            CollectionAssert.AreEqual(new[] { "cat" }, OfferIds(sim));
        }

        // ------------------------------------------------------------------ charms: effects (active the next day only)

        [Test]
        public void Charm_Patience_GuestsWaitLonger()
        {
            var sim = InShop();
            sim.S.TryBuy(Id("tea"));
            NextDay(sim);
            sim.Spawn("impatient", 3, 6); // 5 s x 1.5 = 7.5 s
            sim.Run(7f);
            Assert.AreEqual(0, sim.Events<GuestLeft>().Count, "still waiting after 7 s");
            sim.Run(1f);
            Assert.AreEqual(GuestLeaveReason.PatienceRanOut, sim.Events<GuestLeft>().Single().Reason);
        }

        [Test]
        public void Charm_Patience_OnlyTheNextDay()
        {
            var sim = InShop();
            sim.S.TryBuy(Id("tea"));
            NextDay(sim);
            sim.EndDayToShop();
            NextDay(sim);
            sim.Spawn("impatient", 3, 6);
            sim.Run(5.2f);
            Assert.AreEqual(1, sim.Events<GuestLeft>().Count(e => e.Reason == GuestLeaveReason.PatienceRanOut), "day 3: plain 5 s");
        }

        [Test]
        public void Charm_Speed_TravelAndDoorsFaster()
        {
            (float travel, float door) Measure(bool tuned)
            {
                var sim = InShop(d => d.Hotel.rules.doorSeconds = 1f);
                if (tuned) Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("tune")));
                NextDay(sim);
                Assert.IsTrue(sim.S.RequestFloor(F(5))); // 4 floors x 1 s
                float t = 0f;
                while (sim.Events<ElevatorArrived>().Count == 0) { sim.Run(0.05f); t += 0.05f; Assert.Less(t, 10f); }
                Assert.IsTrue(sim.S.OpenDoors());
                float open = 0f;
                while (sim.S.Elevator.Doors != DoorState.Open) { sim.Run(0.05f); open += 0.05f; Assert.Less(open, 5f); }
                return (t, open);
            }
            var plain = Measure(false);
            var fast = Measure(true);
            Assert.AreEqual(4f, plain.travel, 0.11f, "plain travel 4 s");
            Assert.AreEqual(2f, fast.travel, 0.11f, "speed x2: travel 2 s");
            Assert.AreEqual(1f, plain.door, 0.11f, "plain door 1 s");
            Assert.AreEqual(0.5f, fast.door, 0.11f, "speed x2: door 0.5 s");
        }

        [Test]
        public void Charm_Shield_FirstTwoUnluckyVerdictsCostNothing()
        {
            var sim = InShop();
            sim.S.TryBuy(Id("shield"));
            NextDay(sim);
            var g1 = sim.Spawn("granny", 1, 6);
            var g2 = sim.Spawn("granny", 1, 6);
            var g3 = sim.Spawn("granny", 1, 6);
            sim.Open();
            sim.Close();
            int luck = sim.S.Score.Luck;
            sim.Log.Clear();
            sim.GoTo(4);
            sim.Open(); // floor 4: three Unlucky verdicts, the first two shielded

            Assert.AreEqual(3, sim.Events<LuckJudged>().Count(j => j.Verdict == LuckVerdict.Unlucky), "verdicts still happen");
            var triggered = sim.Events<CharmTriggered>();
            Assert.AreEqual(2, triggered.Count);
            Assert.IsTrue(triggered.All(t => t.Charm == Id("shield")));
            CollectionAssert.AreEqual(new[] { g1, g2 }, triggered.Select(t => t.Guest).ToArray());
            Assert.AreEqual(1, sim.Events<LuckChanged>().Count(c => c.Source == LuckSource.FloorLabel), "only the third one costs Luck");
            Assert.AreEqual(luck - 10, sim.S.Score.Luck);
            Assert.AreEqual(0, sim.S.ShieldLeft);
            Assert.IsTrue(sim.Events<GuestMoodChanged>().Any(m => m.Guest == g3 && m.Current == GuestMood.Scared));

            // order: each shielded verdict = CharmTriggered IMMEDIATELY before its LuckJudged, no LuckChanged in between
            foreach (var g in new[] { g1, g2 })
            {
                int t = sim.IndexOf<CharmTriggered>(e => e.Guest == g);
                Assert.IsInstanceOf<LuckJudged>(sim.Log[t + 1], "CharmTriggered right before the verdict it protects");
                Assert.AreEqual(g, ((LuckJudged)sim.Log[t + 1]).Guest);
                int next = sim.IndexOf<LuckJudged>(null, t + 2);
                int end = next < 0 ? sim.Log.Count : next;
                Assert.AreEqual(-1, sim.Log.Skip(t).Take(end - t).ToList().FindIndex(o => o is LuckChanged), "no Luck loss for a shielded verdict");
            }
            int third = sim.IndexOf<LuckJudged>(e => e.Guest == g3);
            Assert.IsNotInstanceOf<CharmTriggered>(sim.Log[third - 1]);
        }

        [Test]
        public void Charm_Tips_MultipliesTip_AndSaysSo()
        {
            var sim = InShop();
            sim.S.TryBuy(Id("cat"));
            NextDay(sim);
            var g = sim.Spawn("plain", 1, 6);
            sim.Open();
            sim.Close();
            sim.Log.Clear();
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(15, sim.Events<GuestDelivered>().Single().Tip, "10 x 1.5");
            Assert.AreEqual(g, sim.Events<CharmTriggered>().Single().Guest);
            sim.AssertOrder(typeof(GuestDelivered), typeof(CharmTriggered), typeof(TipsChanged));
        }

        [Test]
        public void Charm_DifferentCharmsStack_SameEffectsAdd()
        {
            var sim = InShop(d =>
            {
                var list = d.Stickers.stickers.ToList();
                list.Add(Charm("cat2", StickerEffects.Tips, 1f));
                d.Stickers.stickers = list.ToArray();
                d.Days.days[0].shopStickers = new[] { "cat", "cat2" };
            });
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("cat")));
            Assert.AreEqual(StickerApplyResult.Applied, sim.S.TryBuy(Id("cat2")));
            NextDay(sim);
            sim.Spawn("plain", 1, 6);
            sim.Open();
            sim.Close();
            sim.GoTo(6);
            sim.Open();
            Assert.AreEqual(25, sim.Events<GuestDelivered>().Single().Tip, "10 x (1 + 0.5 + 1)");
        }
    }
}
