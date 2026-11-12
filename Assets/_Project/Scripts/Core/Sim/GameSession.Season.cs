using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// Winter season (<see cref="ISeasonQuery"/>, <see cref="ISeasonFlow"/>), lottery night, Christmas Eve's lucky-for-all
    /// number, and the persistent run history that makes the mystery guest rare (<see cref="IRunHistory"/>).
    ///
    /// Season: the base run is days 1..data.DayCount (its last day ends Prosperous/Modest as before). In that Ending,
    /// <see cref="CanExtendSeason"/> (the data has trailing <c>extended</c> days); <see cref="ExtendSeason"/> publishes
    /// SeasonExtended(first winter day, count) and opens the shop of the base last day (its shopStickers) - or goes
    /// straight to the first winter day's Intro when that shop is empty. Luck and tips are kept, the ending is None again.
    /// While extended, DayCount / PlayableDayCount read the season totals ("/10"). After a winter day's summary: the last
    /// season day ends WinterMiracle (Luck &gt;= rules.prosperousEndingLuck) or SilentNight; other winter days go to the shop.
    /// Haunted (Luck 0) works on winter days as on any real day. RestartGame / ReturnToTitle end the season; a debug jump
    /// to a winter day turns it on (to a base day: off).
    ///
    /// Lottery night (<see cref="DayDef.lottery"/>, not on tutorial days): the k-th of lotteryDraws winning numbers is drawn
    /// once elapsed &gt;= k x duration / lotteryDraws (the first right at BeginDay, after DayStarted). A draw picks, with the
    /// session Random, uniformly among the DISTINCT numbers the floor buttons show right now (labels that are a whole
    /// number, so stickers count and letter labels like "F" do not), avoiding the previous winning number when another one
    /// is shown. LotteryDrawn(number, index, count). Jackpot (see <see cref="GameSession"/> remarks for the event order):
    /// a guest stepping out at their destination on a floor whose label is the winning number wins tip x
    /// rules.lotteryTipMultiplier and Luck +rules.lotteryLuckBonus (each party member). <see cref="LotteryNumber"/> is 0
    /// before the first draw and again once the day ended.
    ///
    /// Mystery guest (spawns whose archetype has the mystery trait): whether it comes tonight is decided when such a day's
    /// Intro is entered (next day, new run, skipped tutorial, debug jump): never while <see cref="FinishedRuns"/> is 0,
    /// guaranteed once <see cref="MysteryMisses"/> &gt;= rules.mysteryPityRuns (&gt; 0), else random &lt; rules.mysteryChance.
    /// Not tonight = that day's mystery spawns are skipped. The counter (the first run never counts): a night without it
    /// = +1 at that day's BeginDay; the night it comes = 0 when it actually appears (RunHistoryChanged right before its
    /// GuestSpawned) - a night it was due but the day ended first (Haunted, a quit to the title) keeps the pity. <see cref="FinishedRuns"/> +1 at a run's first EndingReached (the winter
    /// ending after an extension is the same run). Each change publishes RunHistoryChanged (the owner saves it).
    /// </summary>
    public sealed partial class GameSession : ISeasonQuery, ISeasonFlow, IRunHistory
    {
        bool seasonExtended;
        bool mysteryTonight;
        /// <summary>This run already reached an ending (FinishedRuns counted once per run).</summary>
        bool runCounted;
        int finishedRuns, mysteryMisses;
        int lotteryNumber, lotteryDrawsDone;
        readonly List<int> lotteryCandidates = new List<int>();

        // =========================================================================================== ISeasonQuery

        public bool IsSeasonExtended => seasonExtended;
        public bool IsExtendedDay => dayDef != null && data.IsExtendedDay(day) && !onTitle;
        public bool MysteryTonight => mysteryTonight;
        public bool IsLotteryNight => dayDef != null && dayDef.lottery && !dayDef.tutorial;
        public int LotteryNumber => lotteryNumber;
        public bool IsChristmas => dayDef != null && dayDef.christmas;
        public int LuckyForAll => dayDef != null && dayDef.luckyForAll > 0 ? dayDef.luckyForAll : 0;
        public float SnowAmount => dayDef != null && dayDef.snow > 0f ? Math.Min(1f, dayDef.snow) : 0f; // NaN -> 0

        /// <summary>Lottery draws done today (tests).</summary>
        internal int LotteryDrawsDone => lotteryDrawsDone;

        // =========================================================================================== ISeasonFlow

        public bool CanExtendSeason
            => phase == DayPhase.Ending && !onTitle && !paused && !seasonExtended && data.HasExtendedDays && data.IsLastDay(day)
               && (ending == EndingKind.Prosperous || ending == EndingKind.Modest);

        public bool ExtendSeason()
        {
            if (!CanExtendSeason) return false;
            seasonExtended = true;
            ending = EndingKind.None;
            bus.Publish(new SeasonExtended(day.Next, data.ExtendedDayCount));
            if (OpenShop()) SetPhase(DayPhase.Shop);
            else GoToNextDayIntro();
            return true;
        }

        /// <summary>After a winter day's summary: the last season day's ending, else false (shop as usual).</summary>
        bool TryReachWinterEnding()
        {
            if (!data.IsLastSeasonDay(day)) return false;
            ReachEnding(Score.Luck >= rules.WinterMiracleLuck ? EndingKind.WinterMiracle : EndingKind.SilentNight);
            return true;
        }

        // =========================================================================================== IRunHistory

        public int FinishedRuns => finishedRuns;
        public int MysteryMisses => mysteryMisses;

        public void LoadRunHistory(int finished, int misses)
        {
            finishedRuns = Math.Max(0, finished);
            mysteryMisses = Math.Max(0, misses);
        }

        void PublishRunHistory() => bus.Publish(new RunHistoryChanged(finishedRuns, mysteryMisses));

        /// <summary>ReachEnding: the run's first ending counts it as finished (never on the title demo).</summary>
        void CountFinishedRun()
        {
            if (onTitle || runCounted) return;
            runCounted = true;
            finishedRuns++;
            PublishRunHistory();
        }

        /// <summary>Fresh run (ResetRun): no season, nothing counted, no lottery, no mystery decided.</summary>
        void ResetSeason()
        {
            seasonExtended = false;
            runCounted = false;
            mysteryTonight = false;
            ClearLottery();
        }

        // =========================================================================================== mystery guest

        static bool IsMysterySpawn(GameData data, SpawnDef s)
            => data.TryGetGuest(s.Guest, out var def) && def.HasTrait(GuestTraits.Mystery);

        /// <summary>The day schedules the mystery guest (an "eligible" night).</summary>
        bool HasMysterySpawn(DayDef def)
        {
            if (def == null || def.tutorial) return false;
            foreach (var s in def.spawns) if (IsMysterySpawn(data, s)) return true;
            return false;
        }

        /// <summary>Entering the current day's Intro: decides <see cref="MysteryTonight"/> (see the class remarks).</summary>
        void DecideMysteryTonight()
        {
            mysteryTonight = false;
            if (onTitle || !HasMysterySpawn(dayDef) || finishedRuns <= 0) return;
            if (rules.mysteryPityRuns > 0 && mysteryMisses >= rules.mysteryPityRuns) { mysteryTonight = true; return; }
            float chance = rules.mysteryChance > 0f ? Math.Min(1f, rules.mysteryChance) : 0f; // NaN -> 0
            mysteryTonight = random.NextDouble() < chance;
        }

        /// <summary>BeginDay of an eligible night without the mystery guest: one more miss (not on the first run).</summary>
        void SettleMysteryNight()
        {
            if (onTitle || mysteryTonight || !HasMysterySpawn(dayDef) || finishedRuns <= 0) return;
            mysteryMisses++;
            PublishRunHistory();
        }

        /// <summary>The mystery guest is about to appear: the pity counter starts over (not on the first run / the demo).</summary>
        void OnMysteryArrives()
        {
            if (onTitle || !mysteryTonight || finishedRuns <= 0 || mysteryMisses == 0) return;
            mysteryMisses = 0;
            PublishRunHistory();
        }

        /// <summary>BeginDay: today's spawns, without the mystery guest when it does not come tonight.</summary>
        SpawnDef[] TonightsSpawns(DayDef def)
        {
            if (mysteryTonight && !onTitle) return (SpawnDef[])def.spawns.Clone();
            var list = new List<SpawnDef>(def.spawns.Length);
            foreach (var s in def.spawns) if (!IsMysterySpawn(data, s)) list.Add(s);
            return list.ToArray();
        }

        /// <summary>Debug (Shift+X in an Intro of a day with the mystery guest): flips <see cref="MysteryTonight"/>.</summary>
        internal bool DebugToggleMysteryTonight()
        {
            if (onTitle || phase != DayPhase.Intro || !HasMysterySpawn(dayDef)) return false;
            mysteryTonight = !mysteryTonight;
            return true;
        }

        // =========================================================================================== lottery

        int LotteryDrawCount => Math.Max(1, dayDef.lotteryDraws);

        void ClearLottery()
        {
            lotteryNumber = 0;
            lotteryDrawsDone = 0;
        }

        /// <summary>Due draws on the day clock (the first one at 0 s: called right after DayStarted too).</summary>
        void StepLottery()
        {
            if (!IsLotteryNight || phase != DayPhase.Running) return;
            int count = LotteryDrawCount;
            float duration = Math.Max(0f, dayDef.durationSeconds);
            while (lotteryDrawsDone < count && elapsed + 1e-4f >= lotteryDrawsDone * duration / count)
                DrawLottery(lotteryDrawsDone++, count);
        }

        void DrawLottery(int index, int count)
        {
            lotteryCandidates.Clear();
            for (int f = 0; f < Building.FloorCount; f++)
                if (Building.GetLabel(new FloorIndex(f)).TryGetNumber(out int n) && n > 0 && !lotteryCandidates.Contains(n))
                    lotteryCandidates.Add(n);
            if (lotteryCandidates.Count == 0) return; // nothing numeric on the panel: no draw
            if (lotteryCandidates.Count > 1) lotteryCandidates.Remove(lotteryNumber);
            lotteryCandidates.Sort();
            lotteryNumber = lotteryCandidates[random.Next(lotteryCandidates.Count)];
            bus.Publish(new LotteryDrawn(lotteryNumber, index, count));
        }

        /// <summary>The guest stepping out at <paramref name="floor"/> hits the jackpot (label == the winning number).</summary>
        bool IsLotteryWin(FloorIndex floor)
            => IsLotteryNight && lotteryNumber > 0 && Building.GetLabel(floor).TryGetNumber(out int n) && n == lotteryNumber;

        /// <summary>rules.lotteryTipMultiplier (NaN / not positive = 1).</summary>
        double LotteryTipFactor => rules.lotteryTipMultiplier > 0f && !float.IsInfinity(rules.lotteryTipMultiplier) ? rules.lotteryTipMultiplier : 1.0;

        // =========================================================================================== lucky for all

        /// <summary>Numbers a guest is judged by today (<see cref="GuestRuntime.JudgeLuck"/>): star night 7, luckyForAll.</summary>
        LuckProfile JudgingProfile(LuckProfile own)
        {
            var p = IsStarNight ? StarNightRules.Apply(own) : own;
            return LuckyForAll > 0 ? LuckyForAllRules.Apply(p, LuckyForAll) : p;
        }
    }
}
