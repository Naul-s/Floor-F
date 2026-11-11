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
        /// Rich text for a guest's numbers: lucky in gold, unlucky in red (colour alone: no strikethrough).
        /// Mystery guests (hideNumbers) show "?". A guest without numbers shows "-".
        /// </summary>
        public static string Numbers(LuckProfile luck, bool hide) => Numbers(luck, hide, false);

        /// <summary>Star night: a guest's own unlucky 7 loses - it shows as a lucky (gold) 7.</summary>
        public static string Numbers(LuckProfile luck, bool hide, bool starNight) => Numbers(luck, hide, starNight ? StarNightNumber : 0);

        /// <summary>
        /// <paramref name="forAll"/> = a number Lucky for every guest tonight (star night 7, Christmas Eve 12; 0 = none):
        /// a guest's own unlucky copy loses - it shows as a lucky (gold) number. Their own lucky one is not repeated.
        /// </summary>
        public static string Numbers(LuckProfile luck, bool hide, int forAll)
        {
            if (hide) return "<color=" + HiddenHex + ">?</color>";
            if (luck == null) luck = LuckProfile.None;
            sb.Clear();
            bool forAllShown = false;
            for (int i = 0; i < luck.Lucky.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("<color=").Append(LuckyHex).Append('>').Append(N(luck.Lucky[i])).Append("</color>");
                if (forAll > 0 && luck.Lucky[i] == forAll) forAllShown = true;
            }
            for (int i = 0; i < luck.Unlucky.Count; i++)
            {
                if (forAll > 0 && luck.Unlucky[i] == forAll)
                {
                    if (forAllShown) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("<color=").Append(LuckyHex).Append('>').Append(N(forAll)).Append("</color>");
                    forAllShown = true;
                    continue;
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("<color=").Append(UnluckyHex).Append('>').Append(N(luck.Unlucky[i])).Append("</color>");
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        /// <summary>
        /// The number Lucky for every guest today: ISeasonQuery.LuckyForAll (Christmas Eve 12) or star night's 7
        /// (IDayQuery.IsStarNight); 0 = none. Never on the training shift.
        /// </summary>
        public static int LuckyForAll(bool tutorial, bool starNight, int seasonLuckyForAll) =>
            tutorial ? 0 : seasonLuckyForAll > 0 ? seasonLuckyForAll : starNight ? StarNightNumber : 0;

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
        public static LuckVerdict DistanceVerdict(LuckProfile luck, bool hideNumbers, int floors, bool friday13, bool starNight) =>
            DistanceVerdict(luck, hideNumbers, floors, friday13, starNight ? StarNightNumber : 0);

        /// <summary><paramref name="forAll"/>: the number lucky for everyone tonight (<see cref="LuckyForAll"/>), 0 = none.</summary>
        public static LuckVerdict DistanceVerdict(LuckProfile luck, bool hideNumbers, int floors, bool friday13, int forAll)
        {
            if (floors <= 0) return LuckVerdict.Neutral;
            if (friday13 && floors == Friday13Number) return LuckVerdict.Unlucky;
            if (forAll > 0 && floors == forAll) return LuckVerdict.Lucky;
            if (hideNumbers || luck == null) return LuckVerdict.Neutral;
            return luck.Judge(floors);
        }

        public const int Friday13Number = 13, StarNightNumber = 7;

        /// <summary>Head-count preview on star night: 7 riders is lucky for everyone.</summary>
        public static LuckVerdict OccupancyPreview(int occupancy, LuckProfile[] riders, int riderCount, bool starNight) =>
            OccupancyPreview(occupancy, riders, riderCount, starNight ? StarNightNumber : 0);

        /// <summary>Head-count preview with a number lucky for everyone tonight (0 = none).</summary>
        public static LuckVerdict OccupancyPreview(int occupancy, LuckProfile[] riders, int riderCount, int forAll) =>
            forAll > 0 && occupancy == forAll ? LuckVerdict.Lucky : OccupancyPreview(occupancy, riders, riderCount);

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
                { "karaoke", new[] { "Sing", "노래방" } },   // "Karaoke" is too wide for the panel badge at 24 px in Mulmaru; the mic icon carries it
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

        /// <summary>Sticker headline from its data ("4 → F", "* → 8" shown as "any → 8" / "원하는 층 → 8").</summary>
        public static string StickerHeadline(StickerDef s, bool ko)
        {
            if (s == null) return "?";
            string from = s.MatchesAnyLabel ? UIStrings.AnyFloor.Get(ko) : s.from.Trim();
            return from + " → " + (s.to ?? "").Trim();
        }

        /// <summary>
        /// What a label sticker does, in plain words, from its data: "Renumbers any one floor to 8 for a day" /
        /// "Renumbers floor 4 to F for a day". "" for charms (see <see cref="CharmEffect"/>) and stickers without a 'to'.
        /// </summary>
        public static string LabelEffect(StickerDef s, bool ko)
        {
            if (s == null || s.IsCharm) return "";
            string to = (s.to ?? "").Trim();
            if (to.Length == 0) return "";
            return s.MatchesAnyLabel
                ? KoreanParticles.Format(UIStrings.EffectRelabelAny.Get(ko), to)
                : KoreanParticles.Format(UIStrings.EffectRelabelFrom.Get(ko), to, (s.from ?? "").Trim());
        }

        /// <summary>The "what it does" line of any shop item: <see cref="LabelEffect"/> or <see cref="CharmEffect"/>.</summary>
        public static string StickerEffect(StickerDef s, bool ko) => s == null ? "" : s.IsCharm ? CharmEffect(s, ko) : LabelEffect(s, ko);

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
        /// <summary>Plain factor for "x2" / "x1.5" texts (no trailing ".0").</summary>
        public static string Factor(float m) => ((float)System.Math.Round(m, 2)).ToString("0.##", CultureInfo.InvariantCulture);

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

        /// <summary>Title plate: "BEST  LUCK 72  ·  $340" ("최고 기록  운 72  ·  $340").</summary>
        public static string BestRecord(int luck, int tips, bool ko) => string.Format(UIStrings.BestRecord.Get(ko), N(luck), N(tips));

        /// <summary>The winter-season endings (after ISeasonFlow.ExtendSeason).</summary>
        public static bool IsWinterEnding(EndingKind k) => k == EndingKind.WinterMiracle || k == EndingKind.SilentNight;

        public static void Ending(EndingKind kind, bool ko, out string title, out string text)
        {
            switch (kind)
            {
                case EndingKind.Prosperous: title = UIStrings.EndProsperousTitle.Get(ko); text = UIStrings.EndProsperousText.Get(ko); break;
                case EndingKind.Haunted: title = UIStrings.EndHauntedTitle.Get(ko); text = UIStrings.EndHauntedText.Get(ko); break;
                case EndingKind.FourthFloor: title = UIStrings.EndFourthTitle.Get(ko); text = UIStrings.EndFourthText.Get(ko); break;
                case EndingKind.WinterMiracle: title = UIStrings.EndMiracleTitle.Get(ko); text = UIStrings.EndMiracleText.Get(ko); break;
                case EndingKind.SilentNight: title = UIStrings.EndSilentTitle.Get(ko); text = UIStrings.EndSilentText.Get(ko); break;
                default: title = UIStrings.EndModestTitle.Get(ko); text = UIStrings.EndModestText.Get(ko); break;
            }
        }

        /// <summary>Short name of an ending for the collection card ("PROSPEROUS" / "번창").</summary>
        public static string EndingName(EndingKind kind, bool ko) =>
            (kind == EndingKind.Prosperous ? UIStrings.EndNameProsperous : kind == EndingKind.Haunted ? UIStrings.EndNameHaunted
                : kind == EndingKind.FourthFloor ? UIStrings.EndNameFourth : kind == EndingKind.WinterMiracle ? UIStrings.EndNameMiracle
                : kind == EndingKind.SilentNight ? UIStrings.EndNameSilent : UIStrings.EndNameModest).Get(ko);

        /// <summary>One line from the ending text for an unlocked collection slot.</summary>
        public static string EndingShortLine(EndingKind kind, bool ko) =>
            (kind == EndingKind.Prosperous ? UIStrings.EndShortProsperous : kind == EndingKind.Haunted ? UIStrings.EndShortHaunted
                : kind == EndingKind.FourthFloor ? UIStrings.EndShortFourth : kind == EndingKind.WinterMiracle ? UIStrings.EndShortMiracle
                : kind == EndingKind.SilentNight ? UIStrings.EndShortSilent : UIStrings.EndShortModest).Get(ko);

        /// <summary>
        /// Locked-slot hint: the Luck endings state their rule (<paramref name="lastDay"/> = PlayableDayCount,
        /// <paramref name="prosperousLuck"/> = rules.prosperousEndingLuck), the hidden one only whispers.
        /// </summary>
        public static string EndingHint(EndingKind kind, bool ko, int lastDay, int prosperousLuck)
        {
            switch (kind)
            {
                case EndingKind.Prosperous: return KoreanParticles.Format(UIStrings.EndHintProsperous.Get(ko), lastDay, prosperousLuck);
                case EndingKind.Modest: return KoreanParticles.Format(UIStrings.EndHintModest.Get(ko), lastDay, prosperousLuck);
                case EndingKind.Haunted: return UIStrings.EndHintHaunted.Get(ko);
                case EndingKind.WinterMiracle: return KoreanParticles.Format(UIStrings.EndHintMiracle.Get(ko), lastDay, prosperousLuck);
                case EndingKind.SilentNight: return UIStrings.EndHintSilent.Get(ko);
                default: return UIStrings.EndHintFourth.Get(ko);
            }
        }
    }
}
