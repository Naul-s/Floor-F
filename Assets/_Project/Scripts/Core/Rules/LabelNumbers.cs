namespace FloorF.Core
{
    /// <summary>
    /// GDD 4 rule 1 helper: which numbers a floor LABEL "reads as", and how one guest judges it.
    /// A label reads as: its whole number ("8" -> 8, "88" -> 88), or its leading number ("12A" -> 12),
    /// plus every digit it contains ("88" -> 8, "14" -> 1 and 4). Letters carry no number ("F" -> nothing = neutral).
    /// One verdict per guest per label: any unlucky reading wins (superstition), else any lucky reading, else neutral.
    /// </summary>
    public static class LabelNumbers
    {
        /// <summary>Judges <paramref name="label"/> for <paramref name="profile"/>. <paramref name="number"/> = the deciding number (-1 if none).</summary>
        public static LuckVerdict Judge(LuckProfile profile, FloorLabel label, out int number)
        {
            number = -1;
            if (profile == null || label.IsEmpty) return LuckVerdict.Neutral;

            bool hasWhole = label.TryGetNumber(out int whole) || label.TryGetLeadingNumber(out whole);
            string text = label.Text;

            // Unlucky first: whole number, then digits.
            if (hasWhole && profile.IsUnlucky(whole)) { number = whole; return LuckVerdict.Unlucky; }
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                if (d >= 0 && d <= 9 && profile.IsUnlucky(d)) { number = d; return LuckVerdict.Unlucky; }
            }

            if (hasWhole && profile.IsLucky(whole)) { number = whole; return LuckVerdict.Lucky; }
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                if (d >= 0 && d <= 9 && profile.IsLucky(d)) { number = d; return LuckVerdict.Lucky; }
            }

            number = hasWhole ? whole : -1;
            return LuckVerdict.Neutral;
        }

        /// <summary>Main number shown for a label (whole or leading number), -1 for letter-only labels like "F".</summary>
        public static int PrimaryNumber(FloorLabel label)
            => label.TryGetNumber(out int n) || label.TryGetLeadingNumber(out n) ? n : -1;
    }

    /// <summary>Story facts (not balancing numbers): the real 4th floor and the patience fraction that reads as "impatient".</summary>
    public static class StoryRules
    {
        /// <summary>GDD 2/7: the hotel has a REAL 4th floor, whatever its label says.</summary>
        public static FloorIndex RealFourthFloor => FloorIndex.FromPhysicalNumber(4);

        /// <summary>Default of RulesDef.impatientPatienceFraction (the data value is what GameSession uses).</summary>
        public const float ImpatientFraction = 0.3f;

        /// <summary>"666": the number a devout guest (<see cref="GuestTraits.Devout"/>) fears twice as much.</summary>
        public const int DevoutNumber = 6;

        /// <summary>An Unlucky <see cref="DevoutNumber"/> from a devout guest counts double (any source).</summary>
        public const int DevoutPenaltyFactor = 2;

        /// <summary>Friday the 13th (<see cref="IDayQuery.IsFriday13"/>): this ride distance is Unlucky for every guest.</summary>
        public const int FridayDistance = 13;

        /// <summary>
        /// Star night (<see cref="IDayQuery.IsStarNight"/>): this number is Lucky for every guest from every source (floor
        /// label, head count, ride distance) - even for a guest whose own superstition calls it unlucky.
        /// </summary>
        public const int StarNightNumber = 7;
    }

    /// <summary>Star-night luck: a guest's own numbers with <see cref="StoryRules.StarNightNumber"/> forced Lucky.</summary>
    public static class StarNightRules
    {
        /// <summary>
        /// <paramref name="own"/> plus 7 as Lucky (removed from Unlucky: the star night wins over a guest's own unlucky 7).
        /// Returns <paramref name="own"/> itself when it already is that way.
        /// </summary>
        public static LuckProfile Apply(LuckProfile own)
        {
            own = own ?? LuckProfile.None;
            int seven = StoryRules.StarNightNumber;
            if (own.Lucky.Contains(seven) && !own.Unlucky.Contains(seven)) return own;
            var lucky = new System.Collections.Generic.List<int>(own.Lucky) { seven };
            var unlucky = new System.Collections.Generic.List<int>();
            foreach (var n in own.Unlucky) if (n != seven) unlucky.Add(n);
            return new LuckProfile(NumberSet.From(lucky), NumberSet.From(unlucky));
        }
    }
}
