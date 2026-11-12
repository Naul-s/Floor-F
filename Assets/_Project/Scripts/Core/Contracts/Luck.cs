using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FloorF.Core
{
    /// <summary>Result of judging one number for one guest.</summary>
    public enum LuckVerdict
    {
        Neutral = 0,
        Lucky = 1,
        Unlucky = 2,
    }

    /// <summary>
    /// Where the judged number came from. GDD 4 defines exactly two sources of numbers
    /// (<see cref="FloorLabel"/> and <see cref="Occupancy"/>); the others are non-number Luck changes.
    /// </summary>
    public enum LuckSource
    {
        /// <summary>Label of a floor whose doors opened while the guest was inside (or boarding there).</summary>
        FloorLabel = 0,
        /// <summary>Number of riders when the doors closed.</summary>
        Occupancy = 1,
        /// <summary>Patience ran out - guest took the stairs.</summary>
        Patience = 2,
        /// <summary>Guest arrived at their destination.</summary>
        Delivery = 3,
        /// <summary>A sticker was applied (e.g. the honest guest dislikes fake labels).</summary>
        Sticker = 4,
        /// <summary>Scripted story beats (mystery guest, endings).</summary>
        Story = 5,
        /// <summary>Day start/reset or anything else.</summary>
        Other = 6,
        /// <summary>
        /// Rule 3: floors the guest travelled since boarding (sum of every floor the car moved with them inside, detours
        /// included), judged once when they step out at their destination. Can exceed the floor count (e.g. 13, 17).
        /// </summary>
        RideDistance = 7,
        /// <summary>Lottery night jackpot: a guest stepped out on a floor whose label shows the drawn number (see LotteryWon).</summary>
        Lottery = 8,
    }

    /// <summary>Immutable, sorted, de-duplicated set of small integers (lucky / unlucky numbers).</summary>
    public sealed class NumberSet : IReadOnlyCollection<int>, IEquatable<NumberSet>
    {
        public static readonly NumberSet Empty = new NumberSet(Array.Empty<int>());

        readonly int[] values;

        NumberSet(int[] sortedUnique) { values = sortedUnique; }

        public static NumberSet Of(params int[] numbers) => From(numbers);

        public static NumberSet From(IEnumerable<int> numbers)
        {
            if (numbers == null) return Empty;
            var list = new List<int>(numbers);
            if (list.Count == 0) return Empty;
            list.Sort();
            var unique = new List<int>(list.Count);
            for (int i = 0; i < list.Count; i++)
                if (i == 0 || list[i] != list[i - 1]) unique.Add(list[i]);
            return new NumberSet(unique.ToArray());
        }

        public int Count => values.Length;
        public bool IsEmpty => values.Length == 0;
        public int this[int i] => values[i];
        public bool Contains(int number) => Array.BinarySearch(values, number) >= 0;

        public bool Overlaps(NumberSet other)
        {
            if (other == null) return false;
            foreach (var v in values) if (other.Contains(v)) return true;
            return false;
        }

        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();

        public bool Equals(NumberSet other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || other.values.Length != values.Length) return false;
            for (int i = 0; i < values.Length; i++) if (values[i] != other.values[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as NumberSet);

        public override int GetHashCode()
        {
            int h = 17;
            foreach (var v in values) h = h * 31 + v;
            return h;
        }

        /// <summary>"3, 4" (empty set -> "").</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// A guest's lucky and unlucky numbers. Data validation guarantees the two sets do not overlap;
    /// if they ever do, <see cref="Judge"/> treats the number as Unlucky (superstition wins).
    /// How a <see cref="FloorLabel"/> maps to numbers ("88", "12A", "F") is a gameplay rule, not part of this type.
    /// </summary>
    public sealed class LuckProfile
    {
        public static readonly LuckProfile None = new LuckProfile(NumberSet.Empty, NumberSet.Empty);

        public NumberSet Lucky { get; }
        public NumberSet Unlucky { get; }

        public LuckProfile(NumberSet lucky, NumberSet unlucky)
        {
            Lucky = lucky ?? NumberSet.Empty;
            Unlucky = unlucky ?? NumberSet.Empty;
        }

        public static LuckProfile Of(int[] lucky, int[] unlucky) => new LuckProfile(NumberSet.From(lucky), NumberSet.From(unlucky));

        public LuckVerdict Judge(int number)
        {
            if (Unlucky.Contains(number)) return LuckVerdict.Unlucky;
            if (Lucky.Contains(number)) return LuckVerdict.Lucky;
            return LuckVerdict.Neutral;
        }

        public bool IsLucky(int number) => Judge(number) == LuckVerdict.Lucky;
        public bool IsUnlucky(int number) => Judge(number) == LuckVerdict.Unlucky;

        public override string ToString() => "lucky[" + Lucky + "] unlucky[" + Unlucky + "]";
    }

    /// <summary>
    /// GDD rule 2 (headcount when the doors close), with one exception: a pair is unavoidable (any shared ride, every
    /// couple), so a headcount below <see cref="MinUnluckyHeadcount"/> is never unlucky. It can still be lucky.
    /// Floor labels and ride distances are judged as before (the solo traveller still dislikes floor 2).
    /// </summary>
    public static class HeadcountRules
    {
        public const int MinUnluckyHeadcount = 3;

        public static LuckVerdict Judge(LuckProfile profile, int headcount)
        {
            if (profile == null) return LuckVerdict.Neutral;
            var v = profile.Judge(headcount);
            return v == LuckVerdict.Unlucky && headcount < MinUnluckyHeadcount ? LuckVerdict.Neutral : v;
        }

        /// <summary>Can <paramref name="headcount"/> ever be an unlucky headcount (ignoring who rides)?</summary>
        public static bool CanBeUnlucky(int headcount) => headcount >= MinUnluckyHeadcount;
    }
}
