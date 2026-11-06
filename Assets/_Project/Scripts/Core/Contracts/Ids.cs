using System;
using System.Globalization;

namespace FloorF.Core
{
    /// <summary>
    /// A PHYSICAL floor of the hotel. 0-based: Value 0 = ground floor (physical floor 1).
    /// Never shown to the player directly - the player only ever sees a <see cref="FloorLabel"/>.
    /// "The real 4th floor" is <c>FloorIndex.FromPhysicalNumber(4)</c> whatever its label says.
    /// </summary>
    public readonly struct FloorIndex : IEquatable<FloorIndex>, IComparable<FloorIndex>
    {
        public readonly int Value;

        public FloorIndex(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "FloorIndex must be >= 0");
            Value = value;
        }

        public static readonly FloorIndex Ground = new FloorIndex(0);

        /// <summary>1-based floor number as the building was originally numbered (1..FloorCount). JSON uses this.</summary>
        public int PhysicalNumber => Value + 1;

        public static FloorIndex FromPhysicalNumber(int number) => new FloorIndex(number - 1);

        public bool IsValidFor(int floorCount) => Value < floorCount;

        public FloorIndex Above => new FloorIndex(Value + 1);
        public FloorIndex Below => new FloorIndex(Value - 1);

        public bool Equals(FloorIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FloorIndex o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(FloorIndex other) => Value.CompareTo(other.Value);
        public static bool operator ==(FloorIndex a, FloorIndex b) => a.Value == b.Value;
        public static bool operator !=(FloorIndex a, FloorIndex b) => a.Value != b.Value;
        public static bool operator <(FloorIndex a, FloorIndex b) => a.Value < b.Value;
        public static bool operator >(FloorIndex a, FloorIndex b) => a.Value > b.Value;
        public static bool operator <=(FloorIndex a, FloorIndex b) => a.Value <= b.Value;
        public static bool operator >=(FloorIndex a, FloorIndex b) => a.Value >= b.Value;
        public override string ToString() => "Floor#" + PhysicalNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What the floor is CALLED: the text on the panel button, the LED and the floor sign ("1", "F", "8", "12A", "88").
    /// Luck judgement on door-open uses this, not the physical index. Stickers change it.
    /// Comparison is ordinal and trimmed ("8" == " 8 ").
    /// </summary>
    public readonly struct FloorLabel : IEquatable<FloorLabel>
    {
        readonly string text;

        public FloorLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("FloorLabel text must not be empty", nameof(text));
            this.text = text.Trim();
        }

        /// <summary>"" only for default(FloorLabel); every constructed label is non-empty.</summary>
        public string Text => text ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(text);

        /// <summary>The label a floor has before any sticker: its physical number ("1".."9").</summary>
        public static FloorLabel Original(FloorIndex floor) => new FloorLabel(floor.PhysicalNumber.ToString(CultureInfo.InvariantCulture));

        /// <summary>True when the whole label is a number ("8", "88"); false for "F", "12A".</summary>
        public bool TryGetNumber(out int number)
        {
            number = 0;
            if (IsEmpty) return false;
            for (int i = 0; i < text.Length; i++) if (text[i] < '0' || text[i] > '9') return false;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>Leading digits only: "12A" -> 12, "88" -> 88, "F" -> false.</summary>
        public bool TryGetLeadingNumber(out int number)
        {
            number = 0;
            if (IsEmpty) return false;
            int n = 0;
            while (n < text.Length && text[n] >= '0' && text[n] <= '9') n++;
            return n > 0 && int.TryParse(text.Substring(0, n), NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        public bool ContainsDigit(int digit)
        {
            if (digit < 0 || digit > 9 || IsEmpty) return false;
            return text.IndexOf((char)('0' + digit)) >= 0;
        }

        public bool Equals(FloorLabel other) => string.Equals(Text, other.Text, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is FloorLabel o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
        public static bool operator ==(FloorLabel a, FloorLabel b) => a.Equals(b);
        public static bool operator !=(FloorLabel a, FloorLabel b) => !a.Equals(b);
        public override string ToString() => Text;
    }

    /// <summary>One guest instance in the running game (unique per spawn, never reused within a game).</summary>
    public readonly struct GuestId : IEquatable<GuestId>
    {
        public readonly int Value;
        public GuestId(int value) { Value = value; }
        public static readonly GuestId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(GuestId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GuestId o && Equals(o);
        public override int GetHashCode() => Value;
        public static bool operator ==(GuestId a, GuestId b) => a.Value == b.Value;
        public static bool operator !=(GuestId a, GuestId b) => a.Value != b.Value;
        public override string ToString() => "Guest#" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Guests that travel together (the Japanese couple). <see cref="None"/> = travels alone.</summary>
    public readonly struct PartyId : IEquatable<PartyId>
    {
        public readonly int Value;
        public PartyId(int value) { Value = value; }
        public static readonly PartyId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(PartyId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PartyId o && Equals(o);
        public override int GetHashCode() => Value;
        public static bool operator ==(PartyId a, PartyId b) => a.Value == b.Value;
        public static bool operator !=(PartyId a, PartyId b) => a.Value != b.Value;
        public override string ToString() => "Party#" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>1-based day number (Day 1 = tutorial).</summary>
    public readonly struct DayId : IEquatable<DayId>, IComparable<DayId>
    {
        public readonly int Number;
        public DayId(int number)
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(number), number, "DayId is 1-based");
            Number = number;
        }
        public static readonly DayId First = new DayId(1);
        public DayId Next => new DayId(Number + 1);
        public bool Equals(DayId other) => Number == other.Number;
        public override bool Equals(object obj) => obj is DayId o && Equals(o);
        public override int GetHashCode() => Number;
        public int CompareTo(DayId other) => Number.CompareTo(other.Number);
        public static bool operator ==(DayId a, DayId b) => a.Number == b.Number;
        public static bool operator !=(DayId a, DayId b) => a.Number != b.Number;
        public override string ToString() => "Day" + Number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Base for string ids that come from JSON ("korean_grandma", "four_to_f", "sauna"). Ordinal, case-sensitive.</summary>
    static class StringId
    {
        public static string Normalize(string value) => value == null ? string.Empty : value.Trim();
    }

    /// <summary>Guest type from guests.json ("korean_grandma").</summary>
    public readonly struct GuestArchetypeId : IEquatable<GuestArchetypeId>
    {
        readonly string value;
        public GuestArchetypeId(string value) { this.value = StringId.Normalize(value); }
        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(value);
        public bool Equals(GuestArchetypeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GuestArchetypeId o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(GuestArchetypeId a, GuestArchetypeId b) => a.Equals(b);
        public static bool operator !=(GuestArchetypeId a, GuestArchetypeId b) => !a.Equals(b);
        public override string ToString() => Value;
    }

    /// <summary>Sticker from stickers.json ("four_to_f", "any_to_8").</summary>
    public readonly struct StickerId : IEquatable<StickerId>
    {
        readonly string value;
        public StickerId(string value) { this.value = StringId.Normalize(value); }
        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(value);
        public bool Equals(StickerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StickerId o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(StickerId a, StickerId b) => a.Equals(b);
        public static bool operator !=(StickerId a, StickerId b) => !a.Equals(b);
        public override string ToString() => Value;
    }

    /// <summary>
    /// Where a guest wants to go, shown as an ICON (GDD 4: destinations are icons, not numbers, so relabelling never
    /// loses anyone). Ids come from hotel.json "destinations" ("lobby", "sauna", "rooftop").
    /// </summary>
    public readonly struct DestinationId : IEquatable<DestinationId>
    {
        readonly string value;
        public DestinationId(string value) { this.value = StringId.Normalize(value); }
        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(value);
        public bool Equals(DestinationId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is DestinationId o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(DestinationId a, DestinationId b) => a.Equals(b);
        public static bool operator !=(DestinationId a, DestinationId b) => !a.Equals(b);
        public override string ToString() => Value;
    }
}
