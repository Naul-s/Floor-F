using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// <see cref="IShopService"/>: the shop between days, one-day purchases and charms.
    ///
    /// One day: label stickers stuck and charms bought in the shop after day N are active during day N+1 only. When that
    /// day ends (any way: clock, early end, Haunted, 4th floor) the session publishes, in order:
    ///   DayEnded -> FloorLabelChanged (each stickered floor back to its hotel.json label) -> StickersExpired -> Summary/Ending.
    /// (StickersExpired only when something expired.) A new game / the title also clears them (silently for charms).
    ///
    /// Offers: each visit shows up to rules.shopOfferCount offers drawn from the day's shopStickers pool (duplicates in
    /// the pool ignored), weighted by StickerDef.weight (0/NaN = never at random), without repeats, using the session's
    /// seeded Random, listed in pool order. Staple: a pool sticker made for the real 4th floor (not "any", its from = the
    /// original label of <see cref="StoryRules.RealFourthFloor"/>, i.e. 4 -> F) is ALWAYS one of the offers and survives
    /// rerolls - the floor the whole story is about (the 4th-floor ending needs it in the shop before the last day).
    ///
    /// Reroll: cost = rerollBaseCost x rerollCostMultiplier^k (k = rerolls already done this visit; 20, 40, 80), at most
    /// rules.maxRerolls per visit, both reset at the next visit. A reroll redraws the non-staple offers, preferring
    /// stickers not currently shown (it fills up with shown ones only when the pool runs short). Stock is counted per
    /// sticker per VISIT, across rerolls: a sticker bought out (or a charm already bought) is not drawn again that visit,
    /// and one redrawn after a partial purchase keeps its reduced Remaining (rerolling never restocks).
    ///
    /// Charms (StickerDef.kind "charm", bought with <see cref="TryBuy"/>, no floor; same id twice = AlreadyActive):
    ///   patience: new guests' patience x (1 + amount) | speed: elevator travel and door open/close x (1 + amount) faster
    ///   (boarding walks are not sped up) | tips: every tip x (1 + amount), published CharmTriggered after GuestDelivered
    ///   | shield: the first round(amount) (min 1) Unlucky verdicts of the day with a Luck cost cost nothing:
    ///   CharmTriggered(shield, guest) -> LuckJudged(Unlucky) -> LuckStreakChanged? -> (no LuckChanged) -> mood/lines as usual.
    ///   Several charms with the same effect (different ids) add their amounts. Unknown effects do nothing.
    /// </summary>
    public sealed partial class GameSession
    {
        // ---- shop visit state
        readonly List<StickerDef> pool = new List<StickerDef>();
        readonly List<ShopOffer> offers = new List<ShopOffer>();
        ShopOffer[] offersView = Array.Empty<ShopOffer>();
        /// <summary>Purchases per sticker id this visit (stock is per visit, across rerolls).</summary>
        readonly Dictionary<StickerId, int> boughtThisVisit = new Dictionary<StickerId, int>();
        int rerollsDone;

        // ---- one-day charms (bought in the shop, active during the next day)
        readonly List<StickerDef> charms = new List<StickerDef>();
        StickerDef[] charmsView = Array.Empty<StickerDef>();
        /// <summary>Shield uses today per charm id.</summary>
        readonly Dictionary<StickerId, int> shieldUsed = new Dictionary<StickerId, int>();

        public bool IsOpen => phase == DayPhase.Shop;
        /// <summary>Snapshot: replaced (not mutated) on purchase/reroll, so buying while iterating it is safe.</summary>
        public IReadOnlyList<ShopOffer> Offers => offersView;
        public IReadOnlyList<StickerDef> ActiveCharms => charmsView;
        public bool IsRainy => dayDef != null && string.Equals((dayDef.weather ?? "").Trim(), "rain", StringComparison.OrdinalIgnoreCase);

        int MaxRerolls => Math.Max(0, rules.maxRerolls);
        public int RerollsLeft => IsOpen ? Math.Max(0, MaxRerolls - rerollsDone) : 0;
        /// <summary>Price of the next reroll; 0 when the shop is closed or no reroll is left.</summary>
        public int RerollCost => RerollsLeft > 0 ? CostOfReroll(rerollsDone) : 0;

        /// <summary>rerollBaseCost x multiplier^k, rounded (bad data: base &lt; 0 = 0, multiplier NaN/&lt; 1 = 1).</summary>
        internal int CostOfReroll(int k)
        {
            double b = Math.Max(0, rules.rerollBaseCost);
            double m = rules.rerollCostMultiplier >= 1f ? rules.rerollCostMultiplier : 1.0;
            double c = b * Math.Pow(m, Math.Max(0, k));
            return c >= int.MaxValue ? int.MaxValue : (int)Math.Round(c, MidpointRounding.AwayFromZero);
        }

        // =========================================================================================== open / close

        bool OpenShop()
        {
            CloseShop();
            if (dayDef == null) return false;
            foreach (var idText in dayDef.shopStickers)
            {
                if (!data.TryGetSticker(new StickerId(idText), out var s) || pool.Contains(s)) continue;
                pool.Add(s);
            }
            DrawOffers(reroll: false);
            return offers.Count > 0;
        }

        void CloseShop()
        {
            pool.Clear();
            offers.Clear();
            offersView = Array.Empty<ShopOffer>();
            boughtThisVisit.Clear();
            rerollsDone = 0;
        }

        /// <summary>The staple: a pool label sticker made for the real 4th floor's original label (4 -> F).</summary>
        bool IsStaple(StickerDef s)
        {
            if (s.IsCharm || s.MatchesAnyLabel) return false;
            var original = Building.GetOriginalLabel(StoryRules.RealFourthFloor);
            return !original.IsEmpty && s.from.Trim() == original.Text;
        }

        int Bought(StickerId id) => boughtThisVisit.TryGetValue(id, out int n) ? n : 0;

        int RemainingOf(StickerDef s) => s.stock < 0 ? -1 : Math.Max(0, s.stock - Bought(s.Id));

        bool HasCharm(StickerId id)
        {
            foreach (var c in charms) if (c.Id == id) return true;
            return false;
        }

        /// <summary>Can still be bought this visit (in stock, not an owned charm).</summary>
        bool Buyable(StickerDef s) => RemainingOf(s) != 0 && !(s.IsCharm && HasCharm(s.Id));

        static bool Drawable(StickerDef s) => s.weight > 0f && !float.IsInfinity(s.weight); // NaN fails the comparison

        void DrawOffers(bool reroll)
        {
            int count = Math.Max(0, rules.shopOfferCount);
            var chosen = new List<StickerDef>();
            foreach (var s in pool) if (IsStaple(s) && chosen.Count < count) chosen.Add(s);

            var fresh = new List<StickerDef>();
            var shown = new List<StickerDef>();
            foreach (var s in pool)
            {
                if (chosen.Contains(s) || !Drawable(s) || !Buyable(s)) continue;
                bool isShown = false;
                if (reroll) foreach (var o in offers) if (o.Sticker == s) isShown = true;
                (isShown ? shown : fresh).Add(s);
            }
            DrawWeighted(fresh, chosen, count);
            DrawWeighted(shown, chosen, count);

            offers.Clear();
            foreach (var s in pool)
                if (chosen.Contains(s)) offers.Add(new ShopOffer(s, s.price, RemainingOf(s)));
            offersView = offers.ToArray();
        }

        /// <summary>Moves weighted picks from <paramref name="candidates"/> into <paramref name="chosen"/> until it holds <paramref name="count"/>.</summary>
        void DrawWeighted(List<StickerDef> candidates, List<StickerDef> chosen, int count)
        {
            while (chosen.Count < count && candidates.Count > 0)
            {
                double total = 0;
                foreach (var c in candidates) total += c.weight;
                double r = random.NextDouble() * total;
                int pick = candidates.Count - 1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    r -= candidates[i].weight;
                    if (r < 0) { pick = i; break; }
                }
                chosen.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        int IndexOfOffer(StickerId id)
        {
            for (int i = 0; i < offers.Count; i++) if (offers[i].Id == id) return i;
            return -1;
        }

        void RecordPurchase(int offerIndex, StickerDef def)
        {
            boughtThisVisit[def.Id] = Bought(def.Id) + 1;
            offers[offerIndex] = new ShopOffer(def, offers[offerIndex].Price, RemainingOf(def));
            offersView = offers.ToArray();
        }

        bool ShopAcceptsInput => phase == DayPhase.Shop && !paused && !onTitle;

        // =========================================================================================== labels

        public StickerApplyResult CanBuyAndApply(StickerId sticker, FloorIndex floor) => EvaluateLabel(sticker, floor, out _, out _);

        StickerApplyResult EvaluateLabel(StickerId sticker, FloorIndex floor, out StickerDef def, out int offerIndex)
        {
            offerIndex = -1;
            def = null;
            if (!ShopAcceptsInput) return StickerApplyResult.ShopClosed;
            if (!data.TryGetSticker(sticker, out def)) return StickerApplyResult.UnknownSticker;
            if (def.IsCharm) return StickerApplyResult.WrongKind;
            offerIndex = IndexOfOffer(sticker);
            if (offerIndex < 0) return StickerApplyResult.NotInShop;
            if (offers[offerIndex].Remaining == 0) return StickerApplyResult.OutOfStock;
            if (!Building.IsValid(floor) || !def.CanApplyTo(Building.GetLabel(floor))) return StickerApplyResult.NotApplicable;
            if (Score.Tips < offers[offerIndex].Price) return StickerApplyResult.NotEnoughTips;
            return StickerApplyResult.Applied;
        }

        /// <summary>StickerApplied -> FloorLabelChanged -> TipsChanged. The label lasts until the end of the next day.</summary>
        public StickerApplyResult TryBuyAndApply(StickerId sticker, FloorIndex floor)
        {
            var result = EvaluateLabel(sticker, floor, out var def, out int idx);
            if (result != StickerApplyResult.Applied) return result;
            int price = offers[idx].Price;
            RecordPurchase(idx, def);
            var prev = Building.GetLabel(floor);
            var next = def.ResultLabel;
            bus.Publish(new StickerApplied(def.Id, floor, prev, next, price));
            Building.SetLabel(floor, next);
            Score.AddTips(-price, earned: false);
            return StickerApplyResult.Applied;
        }

        // =========================================================================================== charms

        public StickerApplyResult CanBuy(StickerId charm) => EvaluateCharm(charm, out _, out _);

        StickerApplyResult EvaluateCharm(StickerId charm, out StickerDef def, out int offerIndex)
        {
            offerIndex = -1;
            def = null;
            if (!ShopAcceptsInput) return StickerApplyResult.ShopClosed;
            if (!data.TryGetSticker(charm, out def)) return StickerApplyResult.UnknownSticker;
            if (!def.IsCharm) return StickerApplyResult.WrongKind;
            offerIndex = IndexOfOffer(charm);
            if (offerIndex < 0) return StickerApplyResult.NotInShop;
            if (HasCharm(def.Id)) return StickerApplyResult.AlreadyActive;
            if (offers[offerIndex].Remaining == 0) return StickerApplyResult.OutOfStock;
            if (Score.Tips < offers[offerIndex].Price) return StickerApplyResult.NotEnoughTips;
            return StickerApplyResult.Applied;
        }

        /// <summary>CharmBought -> TipsChanged. Active during the next day.</summary>
        public StickerApplyResult TryBuy(StickerId charm)
        {
            var result = EvaluateCharm(charm, out var def, out int idx);
            if (result != StickerApplyResult.Applied) return result;
            int price = offers[idx].Price;
            RecordPurchase(idx, def);
            charms.Add(def);
            charmsView = charms.ToArray();
            bus.Publish(new CharmBought(def.Id, price));
            Score.AddTips(-price, earned: false);
            return StickerApplyResult.Applied;
        }

        // =========================================================================================== reroll

        public bool Reroll()
        {
            if (!ShopAcceptsInput || RerollsLeft <= 0) return false;
            int cost = RerollCost;
            if (Score.Tips < cost) return false;
            rerollsDone++;
            DrawOffers(reroll: true);
            bus.Publish(new ShopRerolled(cost, RerollsLeft, RerollCost));
            Score.AddTips(-cost, earned: false);
            return true;
        }

        // =========================================================================================== one-day expiry

        /// <summary>End of the day the purchases were active: labels back to the originals, charms gone.</summary>
        void ExpirePurchases()
        {
            int labels = 0;
            for (int i = 0; i < Building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                if (Building.IsRelabelled(f) && Building.SetLabel(f, Building.GetOriginalLabel(f))) labels++;
            }
            int expiredCharms = charms.Count;
            ClearCharms();
            if (labels > 0 || expiredCharms > 0) bus.Publish(new StickersExpired(labels, expiredCharms));
        }

        void ClearCharms()
        {
            charms.Clear();
            charmsView = Array.Empty<StickerDef>();
            shieldUsed.Clear();
        }

        // =========================================================================================== charm effects

        /// <summary>Sum of today's charm amounts for <paramref name="effect"/> (only while a day runs).</summary>
        float CharmAmount(string effect)
        {
            if (phase != DayPhase.Running || charms.Count == 0) return 0f;
            float sum = 0f;
            foreach (var c in charms)
                if (string.Equals(c.effect, effect, StringComparison.OrdinalIgnoreCase) && c.amount > 0f && !float.IsInfinity(c.amount)) sum += c.amount;
            return sum;
        }

        /// <summary>Patience charm: 1 + amount (1 without).</summary>
        internal float PatienceFactor => 1f + CharmAmount(StickerEffects.Patience);
        /// <summary>Speed charm: travel/door speed 1 + amount (1 without).</summary>
        internal float SpeedFactor => 1f + CharmAmount(StickerEffects.Speed);
        /// <summary>Tips charm: 1 + amount (1 without).</summary>
        internal float TipFactor => 1f + CharmAmount(StickerEffects.Tips);

        float SecondsPerFloor => rules.elevatorSecondsPerFloor / SpeedFactor;
        float DoorSeconds => rules.doorSeconds / SpeedFactor;

        /// <summary>The tips charm that boosted this payment (first one), or null.</summary>
        StickerDef TipsCharm()
        {
            if (phase != DayPhase.Running) return null;
            foreach (var c in charms)
                if (string.Equals(c.effect, StickerEffects.Tips, StringComparison.OrdinalIgnoreCase) && c.amount > 0f) return c;
            return null;
        }

        static int ShieldCapacity(StickerDef c) => Math.Max(1, (int)Math.Round(c.amount, MidpointRounding.AwayFromZero));

        /// <summary>
        /// Called for each Unlucky verdict BEFORE its LuckJudged is published: if a shield charm has a use left and the
        /// verdict would cost Luck (<paramref name="penalty"/> &gt; 0), uses it and publishes CharmTriggered(shield, guest).
        /// True = the caller skips the Luck loss (no LuckChanged for that verdict).
        /// </summary>
        bool TryShield(GuestRuntime g, int penalty)
        {
            if (penalty <= 0 || phase != DayPhase.Running) return false;
            foreach (var c in charms)
            {
                if (!string.Equals(c.effect, StickerEffects.Shield, StringComparison.OrdinalIgnoreCase)) continue;
                shieldUsed.TryGetValue(c.Id, out int used);
                if (used >= ShieldCapacity(c)) continue;
                shieldUsed[c.Id] = used + 1;
                bus.Publish(new CharmTriggered(c.Id, g.Id));
                return true;
            }
            return false;
        }

        /// <summary>Shield uses left today (tests / debug).</summary>
        internal int ShieldLeft
        {
            get
            {
                int left = 0;
                foreach (var c in charms)
                {
                    if (!string.Equals(c.effect, StickerEffects.Shield, StringComparison.OrdinalIgnoreCase)) continue;
                    shieldUsed.TryGetValue(c.Id, out int used);
                    left += Math.Max(0, ShieldCapacity(c) - used);
                }
                return left;
            }
        }
    }
}
