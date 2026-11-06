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

        /// <summary>Short destination tag for bubbles ("Sauna", "Restaurant" -> "Restau.").</summary>
        public static string ShortName(string name, int max = 7)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            name = name.Trim();
            return name.Length <= max ? name : name.Substring(0, max - 1) + ".";
        }

        public static string ApplyResult(StickerApplyResult r, string floorOriginal, string newLabel, bool ko)
        {
            switch (r)
            {
                case StickerApplyResult.Applied: return string.Format(UIStrings.ResultApplied.Get(ko), floorOriginal, newLabel);
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
            string from = s.MatchesAnyLabel ? (ko ? "아무 층" : "any") : s.from.Trim();
            return from + " → " + (s.to ?? "").Trim();
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
