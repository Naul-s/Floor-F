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
        public static string Numbers(LuckProfile luck, bool hide) => Numbers(luck, hide, false);

        /// <summary>Star night: a guest's own unlucky 7 loses - it shows as a lucky (gold) 7.</summary>
        public static string Numbers(LuckProfile luck, bool hide, bool starNight)
        {
            if (hide) return "<color=" + HiddenHex + ">?</color>";
            if (luck == null) luck = LuckProfile.None;
            sb.Clear();
            bool sevenLucky = false;
            for (int i = 0; i < luck.Lucky.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("<color=").Append(LuckyHex).Append('>').Append(N(luck.Lucky[i])).Append("</color>");
                if (luck.Lucky[i] == StarNightNumber) sevenLucky = true;
            }
            for (int i = 0; i < luck.Unlucky.Count; i++)
            {
                if (starNight && luck.Unlucky[i] == StarNightNumber)
                {
                    if (sevenLucky) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("<color=").Append(LuckyHex).Append('>').Append(N(StarNightNumber)).Append("</color>");
                    sevenLucky = true;
                    continue;
                }
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
                case LuckSource.RideDistance:
                    return string.Format((number == 1 ? UIStrings.RodeOne : UIStrings.RodeN).Get(ko), N(number));
                default:
                    return "";
            }
        }

        /// <summary>Big popup number: the judged number, or the label text when the source has no number.</summary>
        public static string JudgedNumber(int number, string floorLabel) => number >= 0 ? N(number) : (floorLabel ?? "?");

        /// <summary>Big popup number per source: ride distances carry the chip's odometer glyph ("↕13").</summary>
        public static string JudgedNumber(LuckSource source, int number, string floorLabel) =>
            source == LuckSource.RideDistance && number >= 0 ? Distance(number) : JudgedNumber(number, floorLabel);

        // ------------------------------------------------------------------ rule 3: ride distance

        /// <summary>Odometer glyph on rider chips and distance popups (Galmuri11 has U+2195).</summary>
        public const string DistanceGlyph = "↕";

        /// <summary>Rider chip odometer: "↕5", "↕13" (never negative).</summary>
        public static string Distance(int floors) => DistanceGlyph + N(floors < 0 ? 0 : floors);

        /// <summary>
        /// What a rider would think of <paramref name="floors"/> travelled if they stepped out now (rule 3): their own
        /// numbers (hidden for a mystery guest), plus Friday the 13th's "13 is unlucky for everyone" (it beats their own
        /// lucky 13). 0 floors = nothing travelled yet = Neutral.
        /// </summary>
        public static LuckVerdict DistanceVerdict(LuckProfile luck, bool hideNumbers, int floors, bool friday13) =>
            DistanceVerdict(luck, hideNumbers, floors, friday13, false);

        /// <summary>Star night (IDayQuery.IsStarNight): 7 is lucky for every guest, beating their own unlucky 7.</summary>
        public static LuckVerdict DistanceVerdict(LuckProfile luck, bool hideNumbers, int floors, bool friday13, bool starNight)
        {
            if (floors <= 0) return LuckVerdict.Neutral;
            if (friday13 && floors == Friday13Number) return LuckVerdict.Unlucky;
            if (starNight && floors == StarNightNumber) return LuckVerdict.Lucky;
            if (hideNumbers || luck == null) return LuckVerdict.Neutral;
            return luck.Judge(floors);
        }

        public const int Friday13Number = 13, StarNightNumber = 7;

        /// <summary>Head-count preview on star night: 7 riders is lucky for everyone.</summary>
        public static LuckVerdict OccupancyPreview(int occupancy, LuckProfile[] riders, int riderCount, bool starNight) =>
            starNight && occupancy == StarNightNumber ? LuckVerdict.Lucky : OccupancyPreview(occupancy, riders, riderCount);

        /// <summary>The 666 flourish: an UNLUCKY 6 (any source) from a devout guest (GuestTraits.Devout).</summary>
        public const int CurseNumber = 6;
        public static bool IsCurse(LuckVerdict verdict, int number, bool devout) => devout && verdict == LuckVerdict.Unlucky && number == CurseNumber;

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
                case StickerApplyResult.WrongKind: return UIStrings.ResultWrongKind.Get(ko);
                case StickerApplyResult.AlreadyActive: return UIStrings.ResultAlreadyActive.Get(ko);
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
        /// One glyph per charm effect (KS X 1001 symbols - Galmuri11 has them): shield ◈, patience ♥, speed ≫, tips $,
        /// anything else ◆. Shown in front of the charm's name on cards, the Tomorrow strip and the HUD row.
        /// </summary>
        public static string CharmGlyph(string effect)
        {
            switch ((effect ?? "").Trim().ToLowerInvariant())
            {
                case StickerEffects.Shield: return "◈";
                case StickerEffects.Patience: return "♥";
                case StickerEffects.Speed: return "≫";
                case StickerEffects.Tips: return "$";
                default: return "◆";
            }
        }

        /// <summary>Charm colour (hex) per effect: shield cyan, patience pink, speed green, tips gold, else violet.</summary>
        public static string CharmHex(string effect)
        {
            switch ((effect ?? "").Trim().ToLowerInvariant())
            {
                case StickerEffects.Shield: return ShieldHex;
                case StickerEffects.Patience: return "#FF8FB8";
                case StickerEffects.Speed: return "#5BE37D";
                case StickerEffects.Tips: return "#FFC83D";
                default: return "#B79CFF";
            }
        }

        public const string ShieldHex = "#7FDBFF";

        /// <summary>How many Unlucky verdicts a shield charm eats (amount rounded, at least 1).</summary>
        public static int ShieldCount(float amount) => System.Math.Max(1, (int)System.Math.Round(amount));

        /// <summary>The charm's effect in a few words ("Tips +30%", "Blocks the first UNLUCKY moment"), "" for labels / unknown effects.</summary>
        public static string CharmEffect(StickerDef s, bool ko)
        {
            if (s == null || !s.IsCharm) return "";
            string pct = N((int)System.Math.Round(s.amount * 100f));
            switch ((s.effect ?? "").Trim().ToLowerInvariant())
            {
                case StickerEffects.Patience: return string.Format(UIStrings.EffectPatience.Get(ko), pct);
                case StickerEffects.Speed: return string.Format(UIStrings.EffectSpeed.Get(ko), pct);
                case StickerEffects.Tips: return string.Format(UIStrings.EffectTips.Get(ko), pct);
                case StickerEffects.Shield:
                    int n = ShieldCount(s.amount);
                    return n == 1 ? UIStrings.EffectShieldOne.Get(ko) : string.Format(UIStrings.EffectShieldN.Get(ko), N(n));
                default: return "";
            }
        }

        /// <summary>Reroll button label: "REROLL $20 (3)" or "NO REROLLS LEFT".</summary>
        public static string RerollLabel(int cost, int left, bool ko) =>
            left <= 0 ? UIStrings.RerollNone.Get(ko) : string.Format(UIStrings.Reroll.Get(ko), N(cost), N(left));

        /// <summary>Shop result line for a charm purchase (TryBuy).</summary>
        public static string CharmResult(StickerApplyResult r, string charmName, bool ko)
        {
            switch (r)
            {
                case StickerApplyResult.Applied: return KoreanParticles.Format(UIStrings.CharmBoughtResult.Get(ko), charmName);
                case StickerApplyResult.AlreadyActive: return UIStrings.ResultAlreadyActive.Get(ko);
                case StickerApplyResult.WrongKind: return UIStrings.ResultWrongKind.Get(ko);
                default: return ApplyResult(r, "", "", ko);
            }
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
