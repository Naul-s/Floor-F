using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FloorF.Core;

namespace FloorF.UI
{
    /// <summary>
    /// The "TODAY:" line of a day's intro card: the day's mechanical twist, stated plainly (the data intro above it is the
    /// flavour). Picked from the day's data (pure C#, EditMode-tested): the training shift, Friday the 13th, star night and
    /// the wedding day by their flags / guests, the first day a gambler or a devout guest checks in by those traits, the
    /// first two real days by their order (rules 1+2, then rule 3). Every number comes from the data or StoryRules.
    /// </summary>
    static class DayTwist
    {
        public enum Kind { None, Training, Basics, Ride, Couple, Gambler, Friday, Wedding, Star, NewGuest, Lottery, LuckyForAll }

        public static Kind Resolve(DayDef d, GameData data)
        {
            if (d == null) return Kind.None;
            if (d.tutorial) return Kind.Training;
            if (d.friday13) return Kind.Friday;
            if (d.starNight) return Kind.Star;
            if (d.lottery) return Kind.Lottery;
            if (d.luckyForAll > 0) return Kind.LuckyForAll;
            if (WeddingGuests(d, data, out _, out _)) return Kind.Wedding;
            if (FirstWith(d, data, GuestTraits.Gambler)) return Kind.Gambler;
            if (FirstWith(d, data, GuestTraits.Devout)) return Kind.Couple;
            int real = RealDayOrdinal(d, data);
            if (real == 1) return Kind.Basics;
            if (real == 2) return Kind.Ride;
            return NewGuest(d, data) != null ? Kind.NewGuest : Kind.None;
        }

        /// <summary>Colour of the line on the card (the card look's accent: red Friday, starlight, rose wedding, gold).</summary>
        public static string Hex(Kind k) =>
            k == Kind.Friday ? UIFormat.UnluckyHex : k == Kind.Star ? StarHex : k == Kind.Wedding ? WeddingHex
            : k == Kind.LuckyForAll ? HollyHex : k == Kind.NewGuest ? IceHex : UIFormat.LuckyHex;

        public const string StarHex = "#DCD4FF", WeddingHex = "#FF8FB8", IceHex = "#BFE6FF", HollyHex = "#9BE89B";

        /// <summary>The effect text (no "TODAY:" prefix), or null for <see cref="Kind.None"/>.</summary>
        public static string Text(Kind k, DayDef d, GameData data, bool ko)
        {
            var rules = data != null ? data.Rules : new RulesDef();
            switch (k)
            {
                case Kind.Training: return UIStrings.TodayTraining.Get(ko);
                case Kind.Basics: return UIStrings.TodayBasics.Get(ko);
                case Kind.Ride: return UIStrings.TodayRide.Get(ko);
                case Kind.Couple:
                    return KoreanParticles.Format(UIStrings.TodayCouple.Get(ko), N(PartySize(d, data)), N(StoryRules.DevoutNumber));
                case Kind.Gambler:
                    return KoreanParticles.Format(UIStrings.TodayGambler.Get(ko), N(GamblerNumber(d, data)));
                case Kind.Friday:
                    return KoreanParticles.Format(UIStrings.Friday13Intro.Get(ko), UIFormat.Multiplier(rules.friday13PenaltyMultiplier).Substring(1), N(StoryRules.FridayDistance));
                case Kind.Star:
                    return KoreanParticles.Format(UIStrings.StarNightIntro.Get(ko), N(StoryRules.StarNightNumber), N(rules.wishLuckBonus));
                case Kind.Wedding:
                    WeddingGuests(d, data, out var pair, out var solo);
                    return KoreanParticles.Format(UIStrings.TodayWedding.Get(ko), List(pair, ko), List(solo, ko));
                case Kind.Lottery:
                    return KoreanParticles.Format(UIStrings.TodayLottery.Get(ko), N(d.lotteryDraws > 0 ? d.lotteryDraws : 1),
                        UIFormat.Factor(rules.lotteryTipMultiplier), N(rules.lotteryLuckBonus));
                case Kind.LuckyForAll:
                    return KoreanParticles.Format(UIStrings.TodayLuckyForAll.Get(ko), N(d.luckyForAll));
                case Kind.NewGuest:
                    var g = NewGuest(d, data);
                    if (g == null) return null;
                    string name = ko && !string.IsNullOrEmpty(g.nameKo) ? g.nameKo : g.name;
                    return g.unlucky != null && g.unlucky.Length > 0
                        ? KoreanParticles.Format(UIStrings.TodayNewGuest.Get(ko), name, List(g.lucky, ko), List(g.unlucky, ko))
                        : KoreanParticles.Format(UIStrings.TodayNewGuestNoUnlucky.Get(ko), name, List(g.lucky, ko));
                default: return null;
            }
        }

        /// <summary>"TODAY: ..." coloured for the card, or null when the day has no twist line.</summary>
        public static string Line(DayDef d, GameData data, bool ko)
        {
            var k = Resolve(d, data);
            string text = Text(k, d, data, ko);
            if (string.IsNullOrEmpty(text)) return null;
            return "<color=" + Hex(k) + "><b>" + UIStrings.Today.Get(ko) + "</b> " + text + "</color>";
        }

        // ------------------------------------------------------------------ data lookups

        static IEnumerable<GuestDef> Guests(DayDef d, GameData data)
        {
            if (d?.spawns == null || data == null) yield break;
            var seen = new HashSet<string>();
            foreach (var s in d.spawns)
                if (s != null && !string.IsNullOrEmpty(s.guest) && seen.Add(s.guest) && data.TryGetGuest(s.Guest, out var g)) yield return g;
        }

        /// <summary>This day has a guest with <paramref name="trait"/> and no earlier real day had one.</summary>
        static bool FirstWith(DayDef d, GameData data, string trait)
        {
            bool here = false;
            foreach (var g in Guests(d, data)) if (g.HasTrait(trait)) { here = true; break; }
            if (!here) return false;
            foreach (var other in data.Days)
            {
                if (other == null || other.day >= d.day || other.tutorial) continue;
                foreach (var g in Guests(other, data)) if (g.HasTrait(trait)) return false;
            }
            return true;
        }

        /// <summary>
        /// A guest who checks in for the first time today (no earlier day has them) and shows their numbers - never the
        /// mystery guest (that one stays a secret). The first in spawn order; null when there is none.
        /// </summary>
        internal static GuestDef NewGuest(DayDef d, GameData data)
        {
            if (d == null || data == null) return null;
            foreach (var g in Guests(d, data))
            {
                if (g.hideNumbers || g.HasTrait(GuestTraits.Mystery) || g.lucky == null || g.lucky.Length == 0) continue;
                bool seen = false;
                foreach (var other in data.Days)
                {
                    if (other == null || other.day >= d.day) continue;
                    if (other.spawns == null) continue;
                    foreach (var s in other.spawns) if (s != null && s.guest == g.id) { seen = true; break; }
                    if (seen) break;
                }
                if (!seen) return g;
            }
            return null;
        }

        /// <summary>1 = the first real (non-tutorial) day, 2 = the second ...; 0 when unknown.</summary>
        static int RealDayOrdinal(DayDef d, GameData data)
        {
            if (data == null) return 0;
            int n = 0;
            foreach (var other in data.Days)
            {
                if (other == null || other.tutorial) continue;
                n++;
                if (other.day == d.day) return n;
            }
            return 0;
        }

        /// <summary>Wedding guests (ids "..wedding.."): the party that boards together and the one who comes alone.</summary>
        internal static bool WeddingGuests(DayDef d, GameData data, out GuestDef pair, out GuestDef solo)
        {
            pair = solo = null;
            foreach (var g in Guests(d, data))
            {
                if (g.id.IndexOf("wedding", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (g.partySize > 1) { if (pair == null) pair = g; }
                else if (solo == null) solo = g;
            }
            return pair != null && solo != null;
        }

        /// <summary>Largest party on the day (the couple), at least 2.</summary>
        static int PartySize(DayDef d, GameData data)
        {
            int n = 2;
            foreach (var g in Guests(d, data)) if (g.partySize > n) n = g.partySize;
            return n;
        }

        static int GamblerNumber(DayDef d, GameData data)
        {
            foreach (var g in Guests(d, data))
                if (g.HasTrait(GuestTraits.Gambler) && g.lucky != null && g.lucky.Length > 0) return g.lucky[0];
            return StoryRules.StarNightNumber;
        }

        /// <summary>Lucky numbers as "6 and 8" / "3, 5 and 7" ("6, 8" / "3, 5, 7" in Korean).</summary>
        static string List(GuestDef g, bool ko) => List(g?.lucky, ko);

        static string List(int[] numbers, bool ko)
        {
            if (numbers == null || numbers.Length == 0) return "?";
            var sb = new StringBuilder();
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) sb.Append(ko ? ", " : i == numbers.Length - 1 ? " and " : ", ");
                sb.Append(N(numbers[i]));
            }
            return sb.ToString();
        }

        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
