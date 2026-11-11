using System;

namespace FloorF.Core
{
    // =====================================================================================================
    // JSON schema for Assets/_Project/Resources/Data/*.json (hotel, guests, days, stickers).
    // Rules for these classes (they are parsed by UnityEngine.JsonUtility in the game):
    //   - [Serializable], public FIELDS only (no properties), arrays not Lists, no dictionaries, no nullables.
    //   - Enums are written as strings in JSON and parsed in code (JsonUtility would want ints).
    //   - "Unset" is 0 / "" / empty array. Floor numbers in JSON are PHYSICAL 1-based numbers.
    //   - Field initialisers are the defaults when a key is missing.
    // Changing a field name/meaning = contract change (orchestrator). Adding a new optional field is cheap - ask.
    // Read-only convenience members (methods) are fine; JsonUtility ignores them.
    // =====================================================================================================

    // ---------------------------------------------------------------- hotel.json

    [Serializable]
    public sealed class HotelDef
    {
        public int floorCount = 9;
        /// <summary>Elevator capacity (GDD: 8 - the lucky joke).</summary>
        public int capacity = 8;
        /// <summary>Exactly floorCount entries, numbers 1..floorCount.</summary>
        public FloorDef[] floors = Array.Empty<FloorDef>();
        public DestinationDef[] destinations = Array.Empty<DestinationDef>();
        public RulesDef rules = new RulesDef();
        public HotelLayoutDef layout = new HotelLayoutDef();
    }

    [Serializable]
    public sealed class FloorDef
    {
        /// <summary>Physical number 1..floorCount.</summary>
        public int number;
        /// <summary>Initial label. Empty = the physical number.</summary>
        public string label = "";
        /// <summary>DestinationDef.id served by this floor.</summary>
        public string destination = "";

        public FloorIndex Index => FloorIndex.FromPhysicalNumber(number);
        public FloorLabel InitialLabel => string.IsNullOrWhiteSpace(label) ? FloorLabel.Original(Index) : new FloorLabel(label);
        public DestinationId Destination => new DestinationId(destination);
    }

    [Serializable]
    public sealed class DestinationDef
    {
        public string id = "";
        public string name = "";
        public string nameKo = "";
        /// <summary>Icon key for UI (sprite name under Resources/UI/Icons, decided by ui-dev). Empty = id.</summary>
        public string icon = "";

        public DestinationId Id => new DestinationId(id);
        public string IconKey => string.IsNullOrEmpty(icon) ? id : icon;
    }

    /// <summary>All tunable rule numbers. gameplay-dev reads these; content-designer tunes them.</summary>
    [Serializable]
    public sealed class RulesDef
    {
        public int startLuck = 50;
        public int maxLuck = 100;
        public int startTips = 0;

        // rule 1: label of a floor whose doors open while the guest is inside
        public float luckyFloorTipMultiplier = 2f;
        public int luckyFloorLuckBonus = 2;
        public int unluckyFloorLuckPenalty = 10;

        // rule 2: occupancy when the doors close
        public float luckyOccupancyTipMultiplier = 1.5f;
        public int luckyOccupancyLuckBonus = 3;
        public int unluckyOccupancyLuckPenalty = 5;

        // patience / delivery
        /// <summary>Patience01 below this -> mood Impatient + "impatient" line.</summary>
        public float impatientPatienceFraction = 0.3f;
        public int patienceOutLuckPenalty = 5;
        public int deliveryLuckBonus = 1;

        // honest guests (trait "honest") at a relabelled floor
        public int honestGuestLuckPenalty = 5;

        // lucky streak: consecutive Lucky judgements in a day (Unlucky resets, Neutral keeps)
        /// <summary>Tip multiplier per streak step: x(1 + streak * this), capped at <see cref="streakMaxTipMultiplier"/>. 0 = off.</summary>
        public float streakTipBonusPerStep = 0.05f;
        public float streakMaxTipMultiplier = 1.15f;

        // rule 3: floors travelled since boarding, judged when the guest steps out at their destination
        public float luckyDistanceTipMultiplier = 1.5f;
        public int luckyDistanceLuckBonus = 2;
        public int unluckyDistanceLuckPenalty = 6;

        // "Friday the 13th" event day
        public float friday13PenaltyMultiplier = 1.5f;

        // "Star night" wish stars
        /// <summary>Seconds a wish star takes to cross the sky (the click window).</summary>
        public float wishStarSeconds = 3f;
        public int wishLuckBonus = 4;

        // shop: random offers + rerolls (per visit)
        /// <summary>Offers drawn (weighted, without repeats) from the day's shopStickers pool each visit/reroll.</summary>
        public int shopOfferCount = 4;
        public int rerollBaseCost = 20;
        /// <summary>Each reroll costs the previous cost x this (20, 40, 80 ...).</summary>
        public float rerollCostMultiplier = 2f;
        public int maxRerolls = 3;

        // endings
        /// <summary>Luck at the end of the last day needed for the Prosperous ending (else Modest).</summary>
        public int prosperousEndingLuck = 70;
        /// <summary>Luck at the end of the last winter-season day for WinterMiracle (else SilentNight). 0 = prosperousEndingLuck.</summary>
        public int winterMiracleLuck;
        /// <summary>Effective WinterMiracle line: <see cref="winterMiracleLuck"/>, or prosperousEndingLuck when 0.</summary>
        public int WinterMiracleLuck => winterMiracleLuck > 0 ? winterMiracleLuck : prosperousEndingLuck;

        // hidden 4th-floor ending: the mystery guest is rare and never on the first run
        /// <summary>Chance (0..1) the mystery guest comes on its night once the player has finished at least one run.</summary>
        public float mysteryChance = 0.3f;
        /// <summary>After this many eligible nights without the mystery guest, the next eligible night is guaranteed (0 = never).</summary>
        public int mysteryPityRuns = 3;

        // lottery night (DayDef.lottery)
        /// <summary>Jackpot: the guest's tip x this when they step out on a floor showing the drawn number.</summary>
        public float lotteryTipMultiplier = 2f;
        public int lotteryLuckBonus = 3;

        // elevator feel
        public float elevatorSecondsPerFloor = 0.6f;
        public float doorSeconds = 0.35f;
        /// <summary>Seconds for one guest to walk in/out.</summary>
        public float boardSeconds = 0.4f;

        // day flow
        /// <summary>Day ends this many seconds after all spawns are done and no guest is active.</summary>
        public float earlyEndDelaySeconds = 2f;
    }

    /// <summary>World-unit geometry. Consumed through IWorldLayout (HotelLayout).</summary>
    [Serializable]
    public sealed class HotelLayoutDef
    {
        public float floorHeight = 3f;
        public float floorWidth = 16f;
        public float depth = 4f;
        public float shaftX = 0f;
        public float carWidth = 2.4f;
        /// <summary>Waiting line starts this far right of the shaft centre.</summary>
        public float waitingStartOffsetX = 2f;
        public float waitingSlotSpacing = 0.9f;
        /// <summary>Exit point offset from the shaft centre (positive = right edge).</summary>
        public float exitOffsetX = 8f;
    }

    // ---------------------------------------------------------------- guests.json

    [Serializable]
    public sealed class GuestCatalogDef
    {
        public GuestDef[] guests = Array.Empty<GuestDef>();
    }

    [Serializable]
    public sealed class GuestDef
    {
        public string id = "";
        public string name = "";
        public string nameKo = "";
        /// <summary>Exactly what GDD 4 says: 1 lucky number (more allowed), 0-2 unlucky.</summary>
        public int[] lucky = Array.Empty<int>();
        public int[] unlucky = Array.Empty<int>();
        /// <summary>Show "?" instead of the numbers (mystery guest).</summary>
        public bool hideNumbers;
        public float patienceSeconds = 30f;
        public int baseTip = 10;
        /// <summary>Guests spawned together per spawn entry, board/exit together (Japanese couple = 2).</summary>
        public int partySize = 1;
        /// <summary>Destination ids this guest picks from when a spawn entry does not say.</summary>
        public string[] destinations = Array.Empty<string>();
        /// <summary>Free-form behaviour tags, see GuestTraits.</summary>
        public string[] traits = Array.Empty<string>();
        /// <summary>Tint as "#RRGGBB" for the low-poly body / bubble border.</summary>
        public string color = "#FFFFFF";
        /// <summary>Accessory key for render-dev (hat/glasses/camera...). Empty = none.</summary>
        public string accessory = "";
        public GuestLineDef[] lines = Array.Empty<GuestLineDef>();

        public GuestArchetypeId Id => new GuestArchetypeId(id);
        public LuckProfile CreateLuckProfile() => LuckProfile.Of(lucky, unlucky);

        public bool HasTrait(string trait)
        {
            if (traits == null || string.IsNullOrEmpty(trait)) return false;
            foreach (var t in traits) if (string.Equals(t, trait, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>First line for <paramref name="trigger"/> (GuestLineTriggers), or null.</summary>
        public GuestLineDef FindLine(string trigger)
        {
            if (lines == null) return null;
            foreach (var l in lines) if (l != null && string.Equals(l.trigger, trigger, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }
    }

    [Serializable]
    public sealed class GuestLineDef
    {
        /// <summary>One of GuestLineTriggers.</summary>
        public string trigger = "";
        public string text = "";
        public string textKo = "";

        public string Get(bool korean) => korean && !string.IsNullOrEmpty(textKo) ? textKo : text;
    }

    /// <summary>Known values for GuestLineDef.trigger. Gameplay raises GuestSpoke with these.</summary>
    public static class GuestLineTriggers
    {
        public const string Spawn = "spawn";
        public const string Board = "board";
        public const string LuckyFloor = "lucky_floor";
        public const string UnluckyFloor = "unlucky_floor";
        public const string LuckyOccupancy = "lucky_occupancy";
        public const string UnluckyOccupancy = "unlucky_occupancy";
        /// <summary>Rule 3 (ride distance) verdicts, said when stepping out.</summary>
        public const string LuckyDistance = "lucky_distance";
        public const string UnluckyDistance = "unlucky_distance";
        public const string Delivered = "delivered";
        public const string Impatient = "impatient";
        public const string Left = "left";
        public const string Relabelled = "relabelled";
        public const string Special = "special";
    }

    /// <summary>Known values for GuestDef.traits. Unknown traits are ignored by gameplay (data can run ahead of code).</summary>
    /// <summary>StickerDef.kind values.</summary>
    public static class StickerKinds
    {
        public const string Label = "label";
        public const string Charm = "charm";
    }

    /// <summary>Charm effects (StickerDef.effect) — all last exactly the next day. Unknown effects are ignored by gameplay.</summary>
    public static class StickerEffects
    {
        /// <summary>Guests' patience x (1 + amount).</summary>
        public const string Patience = "patience";
        /// <summary>Elevator travel and doors x (1 + amount) faster.</summary>
        public const string Speed = "speed";
        /// <summary>The first (amount, rounded, min 1) Unlucky verdict(s) of the day cost no Luck.</summary>
        public const string Shield = "shield";
        /// <summary>Tips x (1 + amount).</summary>
        public const string Tips = "tips";
    }

    public static class GuestTraits
    {
        /// <summary>Day-1 tutorial guest: patient, explains the rule.</summary>
        public const string Tutorial = "tutorial";
        /// <summary>Hates relabelled floors (GDD 5 counterweight to sticker spam).</summary>
        public const string Honest = "honest";
        /// <summary>Only boards when the car would hold exactly a lucky number of riders.</summary>
        public const string Gambler = "gambler";
        /// <summary>Last-day mystery guest whose destination is the real 4th floor (EndingKind.FourthFloor).</summary>
        public const string Mystery = "mystery";
        /// <summary>Tips a lot, short patience (flavour; numbers still come from fields).</summary>
        public const string VIP = "vip";
        /// <summary>"666": an Unlucky 6 from this guest counts double (penalty x2) and presentation shows the 666 curse.</summary>
        public const string Devout = "devout";
    }

    // ---------------------------------------------------------------- days.json

    [Serializable]
    public sealed class DayCatalogDef
    {
        public DayDef[] days = Array.Empty<DayDef>();
    }

    [Serializable]
    public sealed class DayDef
    {
        /// <summary>1-based, contiguous.</summary>
        public int day;
        public string title = "";
        public string titleKo = "";
        public string intro = "";
        public string introKo = "";
        public float durationSeconds = 90f;
        public bool tutorial;
        /// <summary>"Friday the 13th" event day (see IDayQuery.IsFriday13).</summary>
        public bool friday13;
        /// <summary>"Star night" (see IDayQuery.IsStarNight).</summary>
        public bool starNight;
        /// <summary>Presentation weather: "" (clear), "rain" (rain + distant lightning) or "snow". No rule effect.</summary>
        public string weather = "";
        /// <summary>Wish stars that cross the sky during this day (spread over the clock, seeded). 0 = none.</summary>
        public int wishStars;
        /// <summary>
        /// Winter-season day (days 8-10): only reached through ISeasonFlow.ExtendSeason after the base run's last day ended
        /// Prosperous or Modest. Extended days must be a trailing block. Not counted in GameData.DayCount / PlayableDayCount.
        /// </summary>
        public bool extended;
        /// <summary>Lottery night (see ISeasonQuery.IsLotteryNight).</summary>
        public bool lottery;
        /// <summary>Lottery night: how many winning numbers are drawn over the day (spread over the clock; the first at the start).</summary>
        public int lotteryDraws = 2;
        /// <summary>Christmas Eve presentation (tree, wreaths, lights, Santa's sleigh). See ISeasonQuery.IsChristmas.</summary>
        public bool christmas;
        /// <summary>
        /// A number that is Lucky for EVERY guest from every source tonight (floor label, head count, ride distance; a guest's
        /// own unlucky copy loses), like star night's 7. 0 = none. Christmas Eve: 12.
        /// </summary>
        public int luckyForAll;
        /// <summary>Snow 0..1 (presentation): 0 none, ~0.3 first flurries, 1 heavy snow with snow piled on ledges/roofs.</summary>
        public float snow;
        /// <summary>Extra intro sentence shown only when the mystery guest comes tonight (ISeasonQuery.MysteryTonight).</summary>
        public string mysteryIntro = "";
        public string mysteryIntroKo = "";
        /// <summary>Sticker ids offered in the shop AFTER this day.</summary>
        public string[] shopStickers = Array.Empty<string>();
        public SpawnDef[] spawns = Array.Empty<SpawnDef>();

        public DayId Id => new DayId(day);
    }

    [Serializable]
    public sealed class SpawnDef
    {
        /// <summary>Seconds after the day starts.</summary>
        public float time;
        /// <summary>GuestDef.id.</summary>
        public string guest = "";
        /// <summary>Physical floor number where the guest appears.</summary>
        public int floor = 1;
        /// <summary>DestinationDef.id. Empty = pick from the guest's destinations (or use destinationFloor).</summary>
        public string destination = "";
        /// <summary>Physical floor number. 0 = resolve from destination. Mystery guest uses 4 (the real 4th).</summary>
        public int destinationFloor;

        public GuestArchetypeId Guest => new GuestArchetypeId(guest);
        public FloorIndex Floor => FloorIndex.FromPhysicalNumber(floor);
        public bool HasDestinationFloor => destinationFloor > 0;
        public FloorIndex DestinationFloor => FloorIndex.FromPhysicalNumber(destinationFloor);
    }

    // ---------------------------------------------------------------- stickers.json

    [Serializable]
    public sealed class StickerCatalogDef
    {
        public StickerDef[] stickers = Array.Empty<StickerDef>();
    }

    [Serializable]
    public sealed class StickerDef
    {
        /// <summary>Matches any current label.</summary>
        public const string AnyLabel = "*";

        public string id = "";
        public string name = "";
        public string nameKo = "";
        public string description = "";
        public string descriptionKo = "";
        /// <summary>Label it can cover ("4"), or "*" for any floor.</summary>
        public string from = AnyLabel;
        /// <summary>Label it shows afterwards ("F", "8").</summary>
        public string to = "";
        public int price = 10;
        /// <summary>Purchases per shop visit. -1 = unlimited.</summary>
        public int stock = 1;
        /// <summary>
        /// "label" (default): relabels one floor (from/to). "charm": no floor, a one-day effect (<see cref="effect"/>).
        /// EVERY purchase lasts ONE day: bought in the shop after day N, active during day N+1, gone when it ends.
        /// </summary>
        public string kind = StickerKinds.Label;
        /// <summary>Charm effect id (StickerEffects), ignored for labels.</summary>
        public string effect = "";
        /// <summary>Charm strength (meaning per effect, see StickerEffects).</summary>
        public float amount;
        /// <summary>Weight in the random shop draw (0 = never offered at random).</summary>
        public float weight = 1f;

        public StickerId Id => new StickerId(id);
        public bool IsCharm => string.Equals(kind, StickerKinds.Charm, StringComparison.OrdinalIgnoreCase);
        public bool MatchesAnyLabel => string.IsNullOrWhiteSpace(from) || from.Trim() == AnyLabel;
        public FloorLabel ResultLabel => new FloorLabel(to);

        /// <summary>True when the sticker may be applied over <paramref name="current"/> (and would change it).</summary>
        public bool CanApplyTo(FloorLabel current)
        {
            if (string.IsNullOrWhiteSpace(to)) return false;
            if (current.Text == to.Trim()) return false;
            return MatchesAnyLabel || current.Text == from.Trim();
        }
    }
}
