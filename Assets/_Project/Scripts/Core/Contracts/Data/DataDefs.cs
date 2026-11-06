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
        public int patienceOutLuckPenalty = 5;
        public int deliveryLuckBonus = 1;

        // honest guests (trait "honest") at a relabelled floor
        public int honestGuestLuckPenalty = 5;

        // endings
        /// <summary>Luck at the end of the last day needed for the Prosperous ending (else Modest).</summary>
        public int prosperousEndingLuck = 70;

        // elevator feel
        public float elevatorSecondsPerFloor = 0.6f;
        public float doorSeconds = 0.35f;
        /// <summary>Seconds for one guest to walk in/out.</summary>
        public float boardSeconds = 0.4f;
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
        public const string Delivered = "delivered";
        public const string Impatient = "impatient";
        public const string Left = "left";
        public const string Relabelled = "relabelled";
        public const string Special = "special";
    }

    /// <summary>Known values for GuestDef.traits. Unknown traits are ignored by gameplay (data can run ahead of code).</summary>
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

        public StickerId Id => new StickerId(id);
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
