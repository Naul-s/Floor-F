using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;

namespace FloorF.Tests.EditMode.Content
{
    /// <summary>
    /// Greedy shopper for balance runs (public IShopService API only). Like a player it forecasts tomorrow from the day it
    /// just played (<see cref="KnowsTomorrow"/> = cheat and read tomorrow's schedule): a label sticker is worth
    /// (lucky - 2 x unlucky - honest) guest-arrivals it changes on its best floor, a charm has a fixed value. It
    /// buys the most valuable affordable thing with a positive value until nothing is left, then rerolls while it can
    /// afford one and still keep <see cref="KeepAfterReroll"/> tips to spend. Deterministic.
    /// </summary>
    sealed class ShopBot
    {
        /// <summary>Label stickers below this value (guest-arrivals improved) are not worth it.</summary>
        public const float MinLabelValue = 1f;
        /// <summary>Tips the bot wants left after paying for a reroll (otherwise it stops).</summary>
        public const int KeepAfterReroll = 40;

        static readonly Dictionary<string, float> CharmValue = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            { StickerEffects.Shield, 4f }, { StickerEffects.Tips, 3f }, { StickerEffects.Patience, 2f }, { StickerEffects.Speed, 2f },
        };

        readonly GameData data;
        /// <summary>Optional filter: only these stickers are considered (balance experiments).</summary>
        public Func<StickerDef, bool> Allow;
        /// <summary>True = values labels with tomorrow's real schedule (perfect knowledge); false = with today's guests.</summary>
        public bool KnowsTomorrow;
        public ShopBot(GameData data) { this.data = data; }

        public sealed class Visit
        {
            public int Day;
            public int TipsBefore, TipsAfter, Rerolls;
            public readonly List<string> Bought = new List<string>();
            /// <summary>It still wanted something (an item or a reroll) it could not afford.</summary>
            public bool MoneyWasTheLimit;
            public override string ToString() =>
                "day " + Day + ": had " + TipsBefore + " -> bought " + Bought.Count + " [" + string.Join(", ", Bought) + "], rerolls " + Rerolls
                + " -> left " + TipsAfter + (MoneyWasTheLimit ? " (money ran out)" : " (wanted nothing more)");
        }

        public Visit Shop(GameSession s)
        {
            var v = new Visit { Day = s.CurrentDay.Number, TipsBefore = s.Score.Tips };
            for (int guard = 0; guard < 100; guard++)
            {
                var wants = Wants(s);
                var buy = wants.FirstOrDefault(w => w.price <= s.Score.Tips);
                if (buy.offer.Sticker != null)
                {
                    var r = buy.offer.Sticker.IsCharm ? s.TryBuy(buy.offer.Id) : s.TryBuyAndApply(buy.offer.Id, buy.floor);
                    if (r != StickerApplyResult.Applied) throw new InvalidOperationException("bot buy refused: " + r + " " + buy.offer.Id);
                    v.Bought.Add(buy.offer.Id.Value + (buy.offer.Sticker.IsCharm ? "" : "@" + buy.floor.PhysicalNumber));
                    continue;
                }
                if (wants.Count > 0) v.MoneyWasTheLimit = true;
                if (s.RerollsLeft > 0)
                {
                    if (s.Score.Tips >= s.RerollCost + KeepAfterReroll && s.Reroll()) { v.Rerolls++; continue; }
                    v.MoneyWasTheLimit = true;
                }
                break;
            }
            v.TipsAfter = s.Score.Tips;
            return v;
        }

        /// <summary>Positive-value purchases on offer, most valuable first.</summary>
        List<(ShopOffer offer, FloorIndex floor, float value, int price)> Wants(GameSession s)
        {
            var list = new List<(ShopOffer, FloorIndex, float, int)>();
            var tomorrow = data.GetDay(s.CurrentDay.Next);
            var forecast = KnowsTomorrow ? tomorrow.spawns : data.GetDay(s.CurrentDay).spawns;
            bool starNight = tomorrow.starNight; // announced in the day intros
            foreach (var o in s.Offers)
            {
                if (o.Remaining == 0 || (Allow != null && !Allow(o.Sticker))) continue;
                if (o.Sticker.IsCharm)
                {
                    if (s.CanBuy(o.Id) == StickerApplyResult.AlreadyActive) continue;
                    if (CharmValue.TryGetValue(o.Sticker.effect ?? "", out float cv)) list.Add((o, default, cv, o.Price));
                    continue;
                }
                float best = 0f;
                FloorIndex bestFloor = default;
                for (int f = 0; f < data.FloorCount; f++)
                {
                    var floor = new FloorIndex(f);
                    var r = s.CanBuyAndApply(o.Id, floor);
                    if (r != StickerApplyResult.Applied && r != StickerApplyResult.NotEnoughTips) continue;
                    float gain = FloorValue(s.Building, forecast, starNight, floor, o.Sticker.ResultLabel, true)
                                 - FloorValue(s.Building, forecast, starNight, floor, s.Building.GetLabel(floor), s.Building.IsRelabelled(floor));
                    if (gain > best) { best = gain; bestFloor = floor; }
                }
                if (best >= MinLabelValue) list.Add((o, bestFloor, best, o.Price));
            }
            return list.OrderByDescending(w => w.Item3).ThenBy(w => w.Item4).ToList();
        }

        /// <summary>Expected verdict value of the forecast arrivals at <paramref name="floor"/> under <paramref name="label"/>.</summary>
        float FloorValue(IBuildingQuery building, SpawnDef[] spawns, bool starNight, FloorIndex floor, FloorLabel label, bool relabelled)
        {
            float sum = 0f;
            foreach (var sp in spawns)
            {
                if (!data.TryGetGuest(sp.Guest, out var g)) continue;
                float share = ArrivalShare(building, sp, g, floor);
                if (share <= 0f) continue;
                var p = data.GetLuckProfile(g.Id);
                if (starNight) p = StarNightRules.Apply(p);
                var verdict = LabelNumbers.Judge(p, label, out _);
                float v = verdict == LuckVerdict.Lucky ? 1f : verdict == LuckVerdict.Unlucky ? -2f : 0f;
                if (relabelled && g.HasTrait(GuestTraits.Honest)) v -= 1f;
                sum += share * Math.Max(1, g.partySize) * v;
            }
            return sum;
        }

        float ArrivalShare(IBuildingQuery building, SpawnDef sp, GuestDef g, FloorIndex floor)
        {
            if (sp.HasDestinationFloor) return sp.DestinationFloor == floor ? 1f : 0f;
            if (!string.IsNullOrWhiteSpace(sp.destination))
                return building.TryFindFloor(new DestinationId(sp.destination), out var f) && f == floor ? 1f : 0f;
            int n = 0, hit = 0;
            foreach (var d in g.destinations)
                if (building.TryFindFloor(new DestinationId(d), out var f) && f != sp.Floor) { n++; if (f == floor) hit++; }
            return n == 0 ? 0f : hit / (float)n;
        }
    }
}
