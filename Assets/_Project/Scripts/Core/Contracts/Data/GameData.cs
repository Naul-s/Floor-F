using System;
using System.Collections.Generic;
using System.Globalization;

namespace FloorF.Core
{
    /// <summary>Thrown when data files are missing, malformed or inconsistent. <see cref="Errors"/> lists every problem.</summary>
    public sealed class GameDataException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public GameDataException(IReadOnlyList<string> errors)
            : base("Game data invalid (" + errors.Count + "):\n  " + string.Join("\n  ", errors)) { Errors = errors; }
    }

    /// <summary>
    /// Validated, indexed, read-only view over the four data files. Registered in GameServices by Gameplay.
    /// Build with <see cref="GameDataLoader.Load"/> (from text) or <see cref="Create"/> (from DTOs, e.g. test fixtures).
    /// </summary>
    public sealed class GameData
    {
        readonly Dictionary<GuestArchetypeId, GuestDef> guestById = new Dictionary<GuestArchetypeId, GuestDef>();
        readonly Dictionary<GuestArchetypeId, LuckProfile> luckById = new Dictionary<GuestArchetypeId, LuckProfile>();
        readonly Dictionary<StickerId, StickerDef> stickerById = new Dictionary<StickerId, StickerDef>();
        readonly Dictionary<DestinationId, DestinationDef> destinationById = new Dictionary<DestinationId, DestinationDef>();
        readonly FloorDef[] floorsByIndex;
        readonly DayDef[] daysByIndex;

        public HotelDef Hotel { get; }
        public RulesDef Rules => Hotel.rules;
        public HotelLayoutDef Layout => Hotel.layout;
        public int FloorCount => Hotel.floorCount;
        public int Capacity => Hotel.capacity;
        public IReadOnlyList<GuestDef> Guests { get; }
        public IReadOnlyList<DayDef> Days => daysByIndex;
        public IReadOnlyList<StickerDef> Stickers { get; }
        public IReadOnlyList<DestinationDef> Destinations { get; }
        public int DayCount => daysByIndex.Length;
        /// <summary>
        /// Leading days flagged <c>tutorial</c> (the "Day 0" training shift). Shown day number = DayId.Number - this,
        /// so the first tutorial day reads "Day 0" and the first real day "Day 1". Tutorial days only count from the start.
        /// </summary>
        public int TutorialDayCount
        {
            get
            {
                int n = 0;
                while (n < daysByIndex.Length && daysByIndex[n].tutorial) n++;
                return n;
            }
        }
        /// <summary>Days that count toward the run (DayCount - TutorialDayCount): the "/5" in "Day 2/5".</summary>
        public int PlayableDayCount => DayCount - TutorialDayCount;
        /// <summary>Shown number of a day: 0 for the (first) tutorial day, 1 for the first real day.</summary>
        public int DisplayDayNumber(DayId day) => day.Number - TutorialDayCount;

        GameData(HotelDef hotel, GuestCatalogDef guests, DayCatalogDef days, StickerCatalogDef stickers)
        {
            Hotel = hotel;
            Guests = guests.guests;
            Stickers = stickers.stickers;
            Destinations = hotel.destinations;
            foreach (var g in guests.guests) { guestById[g.Id] = g; luckById[g.Id] = g.CreateLuckProfile(); }
            foreach (var s in stickers.stickers) stickerById[s.Id] = s;
            foreach (var d in hotel.destinations) destinationById[d.Id] = d;
            floorsByIndex = new FloorDef[hotel.floorCount];
            foreach (var f in hotel.floors) floorsByIndex[f.number - 1] = f;
            daysByIndex = new DayDef[days.days.Length];
            foreach (var d in days.days) daysByIndex[d.day - 1] = d;
        }

        /// <summary>Validates and indexes. Throws <see cref="GameDataException"/> listing every problem.</summary>
        public static GameData Create(HotelDef hotel, GuestCatalogDef guests, DayCatalogDef days, StickerCatalogDef stickers)
        {
            var errors = Validate(hotel, guests, days, stickers);
            if (errors.Count > 0) throw new GameDataException(errors);
            return new GameData(hotel, guests, days, stickers);
        }

        // ------------------------------------------------------------ lookups

        public bool TryGetGuest(GuestArchetypeId id, out GuestDef guest) => guestById.TryGetValue(id, out guest);
        public GuestDef GetGuest(GuestArchetypeId id) => guestById.TryGetValue(id, out var g) ? g : throw new KeyNotFoundException("guest '" + id + "'");
        public LuckProfile GetLuckProfile(GuestArchetypeId id) => luckById.TryGetValue(id, out var p) ? p : LuckProfile.None;

        public bool TryGetSticker(StickerId id, out StickerDef sticker) => stickerById.TryGetValue(id, out sticker);
        public StickerDef GetSticker(StickerId id) => stickerById.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException("sticker '" + id + "'");

        public bool TryGetDestination(DestinationId id, out DestinationDef destination) => destinationById.TryGetValue(id, out destination);

        public FloorDef GetFloor(FloorIndex floor) => floorsByIndex[floor.Value];

        public bool TryFindDestinationFloor(DestinationId id, out FloorIndex floor)
        {
            for (int i = 0; i < floorsByIndex.Length; i++)
                if (floorsByIndex[i].Destination == id) { floor = new FloorIndex(i); return true; }
            floor = default;
            return false;
        }

        public bool TryGetDay(DayId day, out DayDef def)
        {
            if (day.Number >= 1 && day.Number <= daysByIndex.Length) { def = daysByIndex[day.Number - 1]; return true; }
            def = null;
            return false;
        }

        public DayDef GetDay(DayId day) => TryGetDay(day, out var d) ? d : throw new KeyNotFoundException(day.ToString());

        public bool IsLastDay(DayId day) => day.Number == daysByIndex.Length;

        // ------------------------------------------------------------ validation

        public static List<string> Validate(HotelDef hotel, GuestCatalogDef guests, DayCatalogDef days, StickerCatalogDef stickers)
        {
            var e = new List<string>();
            if (hotel == null) e.Add("hotel: missing");
            if (guests == null || guests.guests == null) e.Add("guests: missing 'guests' array");
            if (days == null || days.days == null) e.Add("days: missing 'days' array");
            if (stickers == null || stickers.stickers == null) e.Add("stickers: missing 'stickers' array");
            if (e.Count > 0) return e;

            var destIds = ValidateHotel(hotel, e);
            var guestIds = ValidateGuests(guests, hotel, destIds, e);
            var stickerIds = ValidateStickers(stickers, e);
            ValidateDays(days, hotel, destIds, guests, guestIds, stickerIds, e);
            return e;
        }

        static HashSet<string> ValidateHotel(HotelDef h, List<string> e)
        {
            var destIds = new HashSet<string>(StringComparer.Ordinal);
            if (h.floorCount < 2) e.Add("hotel: floorCount must be >= 2 (is " + h.floorCount + ")");
            if (h.capacity < 1) e.Add("hotel: capacity must be >= 1");
            if (h.rules == null) { e.Add("hotel: missing 'rules'"); h.rules = new RulesDef(); }
            if (h.layout == null) { e.Add("hotel: missing 'layout'"); h.layout = new HotelLayoutDef(); }
            if (h.destinations == null) h.destinations = Array.Empty<DestinationDef>();
            if (h.floors == null) h.floors = Array.Empty<FloorDef>();

            for (int i = 0; i < h.destinations.Length; i++)
            {
                var d = h.destinations[i];
                if (d == null || string.IsNullOrWhiteSpace(d.id)) { e.Add("hotel.destinations[" + i + "]: empty id"); continue; }
                if (!destIds.Add(d.id.Trim())) e.Add("hotel.destinations: duplicate id '" + d.id + "'");
            }

            if (h.floors.Length != h.floorCount) e.Add("hotel.floors: has " + h.floors.Length + " entries, floorCount is " + h.floorCount);
            var seen = new HashSet<int>();
            for (int i = 0; i < h.floors.Length; i++)
            {
                var f = h.floors[i];
                var at = "hotel.floors[" + i + "]";
                if (f == null) { e.Add(at + ": null"); continue; }
                if (f.number < 1 || f.number > h.floorCount) e.Add(at + ": number " + f.number + " outside 1.." + h.floorCount);
                else if (!seen.Add(f.number)) e.Add(at + ": duplicate number " + f.number);
                if (string.IsNullOrWhiteSpace(f.destination)) e.Add(at + ": empty destination");
                else if (!destIds.Contains(f.destination.Trim())) e.Add(at + ": unknown destination '" + f.destination + "'");
            }

            var r = h.rules;
            if (r.maxLuck < 1) e.Add("hotel.rules: maxLuck must be >= 1");
            if (r.startLuck < 1 || r.startLuck > r.maxLuck) e.Add("hotel.rules: startLuck must be in 1..maxLuck");
            if (r.prosperousEndingLuck < 0 || r.prosperousEndingLuck > r.maxLuck) e.Add("hotel.rules: prosperousEndingLuck must be in 0..maxLuck");
            if (r.elevatorSecondsPerFloor <= 0) e.Add("hotel.rules: elevatorSecondsPerFloor must be > 0");
            if (r.doorSeconds < 0) e.Add("hotel.rules: doorSeconds must be >= 0");
            if (r.startTips < 0) e.Add("hotel.rules: startTips must be >= 0");
            if (h.layout.floorHeight <= 0) e.Add("hotel.layout: floorHeight must be > 0");
            return destIds;
        }

        static HashSet<string> ValidateGuests(GuestCatalogDef c, HotelDef h, HashSet<string> destIds, List<string> e)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < c.guests.Length; i++)
            {
                var g = c.guests[i];
                if (g == null) { e.Add("guests[" + i + "]: null"); continue; }
                var at = "guests[" + i + "] '" + g.id + "'";
                if (string.IsNullOrWhiteSpace(g.id)) { e.Add("guests[" + i + "]: empty id"); continue; }
                if (!ids.Add(g.id.Trim())) e.Add(at + ": duplicate id");
                g.lucky ??= Array.Empty<int>();
                g.unlucky ??= Array.Empty<int>();
                g.destinations ??= Array.Empty<string>();
                g.traits ??= Array.Empty<string>();
                g.lines ??= Array.Empty<GuestLineDef>();
                if (g.lucky.Length == 0 && !g.hideNumbers) e.Add(at + ": needs at least one lucky number (or hideNumbers)");
                if (NumberSet.From(g.lucky).Overlaps(NumberSet.From(g.unlucky))) e.Add(at + ": a number is both lucky and unlucky");
                if (g.partySize < 1 || g.partySize > h.capacity) e.Add(at + ": partySize must be in 1..capacity");
                if (g.patienceSeconds <= 0) e.Add(at + ": patienceSeconds must be > 0");
                if (g.baseTip < 0) e.Add(at + ": baseTip must be >= 0");
                foreach (var d in g.destinations)
                    if (!destIds.Contains((d ?? "").Trim())) e.Add(at + ": unknown destination '" + d + "'");
                if (!IsHexColor(g.color)) e.Add(at + ": color must be #RRGGBB (is '" + g.color + "')");
            }
            return ids;
        }

        static HashSet<string> ValidateStickers(StickerCatalogDef c, List<string> e)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < c.stickers.Length; i++)
            {
                var s = c.stickers[i];
                if (s == null) { e.Add("stickers[" + i + "]: null"); continue; }
                var at = "stickers[" + i + "] '" + s.id + "'";
                if (string.IsNullOrWhiteSpace(s.id)) { e.Add("stickers[" + i + "]: empty id"); continue; }
                if (!ids.Add(s.id.Trim())) e.Add(at + ": duplicate id");
                if (string.IsNullOrWhiteSpace(s.to)) e.Add(at + ": empty 'to' label");
                if (s.price < 0) e.Add(at + ": price must be >= 0");
                if (s.stock < -1) e.Add(at + ": stock must be -1 (unlimited) or >= 0");
            }
            return ids;
        }

        static void ValidateDays(DayCatalogDef c, HotelDef h, HashSet<string> destIds, GuestCatalogDef guests,
            HashSet<string> guestIds, HashSet<string> stickerIds, List<string> e)
        {
            if (c.days.Length == 0) { e.Add("days: at least one day required"); return; }
            var seen = new HashSet<int>();
            for (int i = 0; i < c.days.Length; i++)
            {
                var d = c.days[i];
                if (d == null) { e.Add("days[" + i + "]: null"); continue; }
                var at = "days[" + i + "] (day " + d.day + ")";
                if (d.day < 1 || d.day > c.days.Length) e.Add(at + ": day numbers must be 1.." + c.days.Length + " without gaps");
                else if (!seen.Add(d.day)) e.Add(at + ": duplicate day number");
                if (d.durationSeconds <= 0) e.Add(at + ": durationSeconds must be > 0");
                d.shopStickers ??= Array.Empty<string>();
                d.spawns ??= Array.Empty<SpawnDef>();
                foreach (var s in d.shopStickers)
                    if (!stickerIds.Contains((s ?? "").Trim())) e.Add(at + ": unknown shop sticker '" + s + "'");

                for (int k = 0; k < d.spawns.Length; k++)
                {
                    var s = d.spawns[k];
                    var sat = at + ".spawns[" + k + "]";
                    if (s == null) { e.Add(sat + ": null"); continue; }
                    if (s.time < 0 || s.time >= d.durationSeconds) e.Add(sat + ": time " + s.time.ToString(CultureInfo.InvariantCulture) + " outside 0..durationSeconds");
                    if (s.floor < 1 || s.floor > h.floorCount) e.Add(sat + ": floor " + s.floor + " outside 1.." + h.floorCount);
                    if (s.destinationFloor < 0 || s.destinationFloor > h.floorCount) e.Add(sat + ": destinationFloor " + s.destinationFloor + " outside 0.." + h.floorCount);
                    if (!string.IsNullOrWhiteSpace(s.destination) && !destIds.Contains(s.destination.Trim())) e.Add(sat + ": unknown destination '" + s.destination + "'");
                    if (string.IsNullOrWhiteSpace(s.guest) || !guestIds.Contains(s.guest.Trim())) { e.Add(sat + ": unknown guest '" + s.guest + "'"); continue; }
                    GuestDef g = null;
                    foreach (var x in guests.guests) if (x != null && x.id != null && x.id.Trim() == s.guest.Trim()) { g = x; break; }
                    bool hasDest = s.destinationFloor > 0 || !string.IsNullOrWhiteSpace(s.destination) || (g != null && g.destinations.Length > 0);
                    if (!hasDest) e.Add(sat + ": no destination (set destination/destinationFloor or give the guest destinations)");
                    if (s.destinationFloor > 0 && s.destinationFloor == s.floor) e.Add(sat + ": destinationFloor equals spawn floor");
                }
            }
        }

        static bool IsHexColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 7 || s[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(s[i])) return false;
            return true;
        }
    }

    /// <summary>Reads a named text blob ("hotel", "guests", ...). Unity: Resources/Data/&lt;name&gt;.json. Tests: a dictionary.</summary>
    public interface ITextSource
    {
        bool TryRead(string name, out string text);
    }

    /// <summary>JSON -> DTO. Unity: JsonUtility (FloorF.Gameplay.UnityJsonDeserializer).</summary>
    public interface IJsonDeserializer
    {
        T FromJson<T>(string json);
    }

    /// <summary>In-memory <see cref="ITextSource"/> (tests, tools).</summary>
    public sealed class DictionaryTextSource : ITextSource
    {
        readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        public DictionaryTextSource Set(string name, string text) { texts[name] = text; return this; }
        public bool TryRead(string name, out string text) => texts.TryGetValue(name, out text);
    }

    public static class GameDataLoader
    {
        /// <summary>Folder under Resources/.</summary>
        public const string ResourcesFolder = "Data";
        public const string HotelFile = "hotel";
        public const string GuestsFile = "guests";
        public const string DaysFile = "days";
        public const string StickersFile = "stickers";

        public static readonly string[] AllFiles = { HotelFile, GuestsFile, DaysFile, StickersFile };

        /// <summary>Reads, parses and validates all four files. Throws <see cref="GameDataException"/> with every problem found.</summary>
        public static GameData Load(ITextSource source, IJsonDeserializer json)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (json == null) throw new ArgumentNullException(nameof(json));
            var errors = new List<string>();
            var hotel = Read<HotelDef>(source, json, HotelFile, errors);
            var guests = Read<GuestCatalogDef>(source, json, GuestsFile, errors);
            var days = Read<DayCatalogDef>(source, json, DaysFile, errors);
            var stickers = Read<StickerCatalogDef>(source, json, StickersFile, errors);
            if (errors.Count > 0) throw new GameDataException(errors);
            return GameData.Create(hotel, guests, days, stickers);
        }

        static T Read<T>(ITextSource source, IJsonDeserializer json, string name, List<string> errors) where T : class
        {
            if (!source.TryRead(name, out var text) || string.IsNullOrWhiteSpace(text))
            {
                errors.Add(name + ".json: not found or empty");
                return null;
            }
            try
            {
                var value = json.FromJson<T>(text);
                if (value == null) errors.Add(name + ".json: parsed to null");
                return value;
            }
            catch (Exception ex)
            {
                errors.Add(name + ".json: parse error: " + ex.Message);
                return null;
            }
        }
    }
}
