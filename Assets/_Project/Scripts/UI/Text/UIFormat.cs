using System.Globalization;
using System.Text;
using FloorF.Core;

namespace FloorF.UI
{
    /// <summary>Pure string building for the UI (EditMode-tested, no Unity types).</summary>
    static class UIFormat
    {
        public const string LuckyHex = "#FFC83D";
        public const string UnluckyHex = "#FF4B4B";
        public const string HiddenHex = "#C9A7FF";

        static readonly StringBuilder sb = new StringBuilder(64);

        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Rich text for a guest's numbers: lucky in gold, unlucky in red + strikethrough ("3 <s>4</s>").
        /// Mystery guests (hideNumbers) show "?". A guest without numbers shows "-".
        /// </summary>
        public static string Numbers(LuckProfile luck, bool hide)
        {
            if (hide) return "<color=" + HiddenHex + ">?</color>";
            if (luck == null) luck = LuckProfile.None;
            sb.Clear();
            for (int i = 0; i < luck.Lucky.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("<color=").Append(LuckyHex).Append('>').Append(N(luck.Lucky[i])).Append("</color>");
            }
            for (int i = 0; i < luck.Unlucky.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("<color=").Append(UnluckyHex).Append("><s>").Append(N(luck.Unlucky[i])).Append("</s></color>");
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        /// <summary>"1:05" (rounded up, never negative).</summary>
        public static string Clock(float seconds)
        {
            int s = seconds <= 0f ? 0 : (int)System.Math.Ceiling(seconds);
            return N(s / 60) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Signed(int v) => v > 0 ? "+" + N(v) : N(v);

        public static string Verdict(LuckVerdict v, bool ko) =>
            v == LuckVerdict.Lucky ? UIStrings.LuckyWord.Get(ko) : v == LuckVerdict.Unlucky ? UIStrings.UnluckyWord.Get(ko) : "";

        /// <summary>
        /// Popup caption under the big number: FloorLabel source -> "floor F" (the label as shown), Occupancy -> "3 people".
        /// <paramref name="floorLabel"/> is the CURRENT label of e.Floor (the judged number for a label like "88" is still 88).
        /// </summary>
        public static string JudgedCaption(LuckSource source, int number, string floorLabel, bool ko)
        {
            switch (source)
            {
                case LuckSource.FloorLabel:
                    return string.Format(UIStrings.FloorN.Get(ko), string.IsNullOrEmpty(floorLabel) ? N(number) : floorLabel);
                case LuckSource.Occupancy:
                    return string.Format((number == 1 ? UIStrings.PeopleOne : UIStrings.PeopleN).Get(ko), N(number));
                default:
                    return "";
            }
        }

        /// <summary>Big popup number: the judged number, or the label text when the source has no number.</summary>
        public static string JudgedNumber(int number, string floorLabel) => number >= 0 ? N(number) : (floorLabel ?? "?");

        /// <summary>
        /// Head-count preview for the panel: what the riders would think of <paramref name="occupancy"/> if the doors
        /// closed now (any rider finds it unlucky -> Unlucky, else any lucky -> Lucky).
        /// </summary>
        public static LuckVerdict OccupancyPreview(int occupancy, LuckProfile[] riders, int riderCount)
        {
            bool lucky = false;
            for (int i = 0; i < riderCount; i++)
            {
                var p = riders[i];
                if (p == null) continue;
                var v = p.Judge(occupancy);
                if (v == LuckVerdict.Unlucky) return LuckVerdict.Unlucky;
                if (v == LuckVerdict.Lucky) lucky = true;
            }
            return lucky ? LuckVerdict.Lucky : LuckVerdict.Neutral;
        }

        /// <summary>Hand-picked short names per hotel.json destination id (en, ko) - never a cut-off word.</summary>
        static readonly System.Collections.Generic.Dictionary<string, string[]> shortNames =
            new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "lobby", new[] { "Lobby", "로비" } },
                { "restaurant", new[] { "Dining", "식당" } },
                { "gym", new[] { "Gym", "헬스" } },
                { "karaoke", new[] { "Karaoke", "노래방" } },
                { "sauna", new[] { "Sauna", "사우나" } },
                { "rooms", new[] { "Rooms", "객실" } },
                { "bar", new[] { "Bar", "바" } },
                { "casino", new[] { "Casino", "카지노" } },
                { "rooftop", new[] { "Roof", "옥상" } },
            };

        /// <summary>Curated short name for <paramref name="destinationId"/> ("rooms" -> "Rooms" / "객실"), or null when there is none.</summary>
        public static string DestinationShortName(string destinationId, bool ko)
        {
            if (string.IsNullOrEmpty(destinationId) || !shortNames.TryGetValue(destinationId.Trim(), out var n)) return null;
            return ko ? n[1] : n[0];
        }

        /// <summary>Truncated tag for a name without a curated short name ("Restaurant" -> "Restau.").</summary>
        public static string ShortName(string name, int max = 7)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            name = name.Trim();
            return name.Length <= max ? name : name.Substring(0, max - 1) + ".";
        }

        /// <summary>Shop result line. <paramref name="destination"/> (optional, e.g. InlineIcon markup) names the floor's venue.</summary>
        public static string ApplyResult(StickerApplyResult r, string floorOriginal, string newLabel, bool ko, string destination = null)
        {
            switch (r)
            {
                case StickerApplyResult.Applied:
                    return string.IsNullOrEmpty(destination)
                        ? KoreanParticles.Format(UIStrings.ResultApplied.Get(ko), floorOriginal, newLabel)
                        : KoreanParticles.Format(UIStrings.ResultAppliedAt.Get(ko), floorOriginal, newLabel, destination);
                case StickerApplyResult.NotEnoughTips: return UIStrings.ResultNotEnough.Get(ko);
                case StickerApplyResult.NotApplicable: return UIStrings.ResultNotApplicable.Get(ko);
                case StickerApplyResult.OutOfStock: return UIStrings.ResultOutOfStock.Get(ko);
                default: return UIStrings.ResultOther.Get(ko);
            }
        }

        /// <summary>Sticker headline from its data ("4 → F", "* → 8" shown as "any → 8").</summary>
        public static string StickerHeadline(StickerDef s, bool ko)
        {
            if (s == null) return "?";
            string from = s.MatchesAnyLabel ? (ko ? "아무" : "any") : s.from.Trim();
            return from + " → " + (s.to ?? "").Trim();
        }

        /// <summary>
        /// HUD / fallback day label from IDayQuery.DisplayDay: "DAY 0" / "0일차" on the training shift (no "/5" - it does
        /// not count), "DAY 2/5" / "2/5일차" on real days.
        /// </summary>
        public static string DayLabel(int displayDay, int playableDays, bool tutorial, bool ko)
        {
            if (tutorial || playableDays <= 0) return string.Format(UIStrings.DayOnly.Get(ko), N(displayDay));
            return string.Format(UIStrings.DayOf.Get(ko), N(displayDay), N(playableDays));
        }

        /// <summary>Tip multiplier as shown next to the streak: "x1.15", "x1.1", "x1.0" (two decimals at most).</summary>
        public static string Multiplier(float m) => "x" + ((float)System.Math.Round(m, 2)).ToString("0.0#", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ day grade (real-day summary stamp)
        // Points (max 9), each from the DaySummary:
        //   delivered share  delivered / (delivered + left):  >= 95% +3,  >= 80% +2,  >= 60% +1
        //   unlucky moments  0 +2,  1-2 +1,  3+ 0
        //   luck delta       >= +10 +2,  >= 0 +1,  negative 0
        //   best streak      >= 5 +2,  >= 3 +1
        // Grade: S >= 8, A >= 6, B >= 4, else C. Nobody delivered = C.
        public const int GradeS = 8, GradeA = 6, GradeB = 4;

        public static int GradePoints(DaySummary s)
        {
            int served = s.Delivered + s.Left;
            float share = served > 0 ? (float)s.Delivered / served : 0f;
            int p = share >= 0.95f ? 3 : share >= 0.8f ? 2 : share >= 0.6f ? 1 : 0;
            p += s.UnluckyJudgements == 0 ? 2 : s.UnluckyJudgements <= 2 ? 1 : 0;
            p += s.LuckDelta >= 10 ? 2 : s.LuckDelta >= 0 ? 1 : 0;
            p += s.BestStreak >= 5 ? 2 : s.BestStreak >= 3 ? 1 : 0;
            return p;
        }

        /// <summary>"S" / "A" / "B" / "C" for a real day's summary (see the thresholds above).</summary>
        public static string DayGrade(DaySummary s)
        {
            if (s.Delivered <= 0) return "C";
            int p = GradePoints(s);
            return p >= GradeS ? "S" : p >= GradeA ? "A" : p >= GradeB ? "B" : "C";
        }

        public static void Ending(EndingKind kind, bool ko, out string title, out string text)
        {
            switch (kind)
            {
                case EndingKind.Prosperous: title = UIStrings.EndProsperousTitle.Get(ko); text = UIStrings.EndProsperousText.Get(ko); break;
                case EndingKind.Haunted: title = UIStrings.EndHauntedTitle.Get(ko); text = UIStrings.EndHauntedText.Get(ko); break;
                case EndingKind.FourthFloor: title = UIStrings.EndFourthTitle.Get(ko); text = UIStrings.EndFourthText.Get(ko); break;
                default: title = UIStrings.EndModestTitle.Get(ko); text = UIStrings.EndModestText.Get(ko); break;
            }
        }
    }
}
