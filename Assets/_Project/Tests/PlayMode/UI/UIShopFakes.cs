using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// IShopService built on the contract for UI tests while Gameplay's real shop rework is pending: label stickers are
    /// passed through to the real shop (so floors really relabel), charms / rerolls / ActiveCharms are simulated here
    /// (tips spent through the real ScoreKeeper, the contract's events published on the real bus).
    /// </summary>
    sealed class FakeShop : IShopService
    {
        readonly IShopService real;
        readonly GameSession session;
        readonly List<StickerDef> pool;
        readonly Dictionary<string, int> remaining = new Dictionary<string, int>();
        readonly List<StickerDef> active = new List<StickerDef>();
        readonly List<ShopOffer> offers = new List<ShopOffer>();
        int drawStart;
        public int OfferCount;
        public int Left, Cost;
        public float Multiplier = 2f;

        public FakeShop(GameSession session, IShopService real, List<StickerDef> pool, int offerCount, int rerolls = 3, int cost = 20)
        {
            this.session = session;
            this.real = real;
            this.pool = pool;
            OfferCount = offerCount;
            Left = rerolls;
            Cost = cost;
            foreach (var s in pool) remaining[s.id] = s.stock;
        }

        int Tips => session.Score.Tips;
        public List<StickerDef> Active => active;

        public bool IsOpen => real.IsOpen;

        public IReadOnlyList<ShopOffer> Offers
        {
            get
            {
                offers.Clear();
                for (int i = 0; i < OfferCount && i < pool.Count; i++)
                {
                    var s = pool[(drawStart + i) % pool.Count];
                    if (!s.IsCharm && TryReal(s.Id, out var r)) offers.Add(r);
                    else offers.Add(new ShopOffer(s, s.price, remaining[s.id]));
                }
                return offers;
            }
        }

        bool TryReal(StickerId id, out ShopOffer offer)
        {
            var list = real.Offers;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) { offer = list[i]; return true; }
            offer = default;
            return false;
        }

        StickerDef Def(StickerId id)
        {
            foreach (var s in pool) if (s.Id == id) return s;
            return null;
        }

        public StickerApplyResult CanBuyAndApply(StickerId sticker, FloorIndex floor)
        {
            var d = Def(sticker);
            return d != null && d.IsCharm ? StickerApplyResult.WrongKind : real.CanBuyAndApply(sticker, floor);
        }

        public StickerApplyResult TryBuyAndApply(StickerId sticker, FloorIndex floor)
        {
            var d = Def(sticker);
            return d != null && d.IsCharm ? StickerApplyResult.WrongKind : real.TryBuyAndApply(sticker, floor);
        }

        public StickerApplyResult CanBuy(StickerId charm)
        {
            if (!IsOpen) return StickerApplyResult.ShopClosed;
            var d = Def(charm);
            if (d == null) return StickerApplyResult.UnknownSticker;
            if (!d.IsCharm) return StickerApplyResult.WrongKind;
            if (active.Contains(d)) return StickerApplyResult.AlreadyActive;
            if (remaining[d.id] == 0) return StickerApplyResult.OutOfStock;
            if (Tips < d.price) return StickerApplyResult.NotEnoughTips;
            return StickerApplyResult.Applied;
        }

        public StickerApplyResult TryBuy(StickerId charm)
        {
            var r = CanBuy(charm);
            if (r != StickerApplyResult.Applied) return r;
            var d = Def(charm);
            active.Add(d);
            if (remaining[d.id] > 0) remaining[d.id]--;
            GameServices.Events.Publish(new CharmBought(d.Id, d.price));
            session.Score.AddTips(-d.price, earned: false);
            return r;
        }

        public int RerollsLeft => Left;
        public int RerollCost => Cost;

        public bool Reroll()
        {
            if (!IsOpen || Left <= 0 || Tips < Cost) return false;
            int paid = Cost;
            Left--;
            Cost = (int)(Cost * Multiplier);
            drawStart = (drawStart + OfferCount) % pool.Count;
            GameServices.Events.Publish(new ShopRerolled(paid, Left, Cost));
            session.Score.AddTips(-paid, earned: false);
            return true;
        }

        public IReadOnlyList<StickerDef> ActiveCharms => active;

        // ------------------------------------------------------------------ charm data for tests

        public static StickerDef Charm(string id, string effect, float amount, int price, string name, string nameKo, string desc, string descKo, int stock = 1) =>
            new StickerDef
            {
                id = id, kind = StickerKinds.Charm, effect = effect, amount = amount, price = price, stock = stock,
                name = name, nameKo = nameKo, description = desc, descriptionKo = descKo,
            };

        public static StickerDef Talisman() => Charm("talisman", StickerEffects.Shield, 1f, 40, "Lucky Talisman", "행운 부적",
            "Blocks the first UNLUCKY moment of tomorrow: that verdict costs no Luck at all.",
            "내일 처음 한 번의 불길한 판정을 막아요: 그 판정은 운을 하나도 깎지 않아요.");
        public static StickerDef Bell() => Charm("calm_bell", StickerEffects.Patience, 0.5f, 30, "Calm Bell", "평온의 종",
            "A soft chime in the lobby: guests wait half again as long before they give up and take the stairs.",
            "로비에 울리는 맑은 종소리: 손님이 포기하고 계단으로 가기까지 1.5배 더 기다려요.");
        public static StickerDef Cable() => Charm("swift_cable", StickerEffects.Speed, 0.25f, 35, "Swift Cable", "빠른 케이블",
            "Freshly oiled cables: the car and its doors move a quarter faster all day long.",
            "새로 기름칠한 케이블: 하루 종일 엘리베이터와 문이 4분의 1 더 빨라요.");
        public static StickerDef Coin() => Charm("golden_coin", StickerEffects.Tips, 0.3f, 50, "Golden Coin", "황금 동전",
            "A golden coin taped under the panel. Nobody knows why it works, but every tip tomorrow is 30% bigger - even from the grumpy accountant.",
            "패널 밑에 붙인 황금 동전. 왜 통하는지는 아무도 모르지만 내일은 모든 팁이 30% 커져요 - 깐깐한 회계사의 팁까지도.");

        /// <summary>The day's real label stickers first, then the charms (offerCount picks from the front).</summary>
        public static List<StickerDef> Pool(GameData data, params StickerDef[] charms)
        {
            var list = new List<StickerDef>();
            foreach (var id in new[] { "four_to_f", "any_to_8" })
                if (data.TryGetSticker(new StickerId(id), out var s)) list.Add(s);
            list.InsertRange(1, charms.Length > 0 ? new[] { charms[0] } : new StickerDef[0]);
            for (int i = 1; i < charms.Length; i++) list.Add(charms[i]);
            return list;
        }
    }

    /// <summary>IBuildingQuery over the real building with per-floor label overrides (labels the stub shop never reverts).</summary>
    sealed class FakeBuilding : IBuildingQuery
    {
        readonly IBuildingQuery real;
        public readonly Dictionary<int, string> Labels = new Dictionary<int, string>();
        public FakeBuilding(IBuildingQuery real) { this.real = real; }
        public int FloorCount => real.FloorCount;
        public FloorLabel GetLabel(FloorIndex floor) => Labels.TryGetValue(floor.Value, out var l) ? new FloorLabel(l) : real.GetLabel(floor);
        public FloorLabel GetOriginalLabel(FloorIndex floor) => real.GetOriginalLabel(floor);
        public bool IsRelabelled(FloorIndex floor) => GetLabel(floor) != GetOriginalLabel(floor);
        public DestinationId GetDestination(FloorIndex floor) => real.GetDestination(floor);
        public bool TryFindFloor(DestinationId destination, out FloorIndex floor) => real.TryFindFloor(destination, out floor);
        public bool TryFindFloorByLabel(FloorLabel label, out FloorIndex floor)
        {
            for (int f = 0; f < FloorCount; f++) if (GetLabel(new FloorIndex(f)) == label) { floor = new FloorIndex(f); return true; }
            floor = default;
            return false;
        }
    }
}
