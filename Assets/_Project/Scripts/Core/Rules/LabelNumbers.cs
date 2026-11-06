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

        /// <summary>Below this patience fraction a waiting guest turns Impatient (mood + "impatient" line).</summary>
        public const float ImpatientFraction = 0.3f;
    }
}
