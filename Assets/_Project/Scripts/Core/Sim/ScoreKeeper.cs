using System;

namespace FloorF.Core
{
    /// <summary>
    /// <see cref="IScoreQuery"/>: hotel Luck meter (0..max), tips and the lucky streak. Publishes LuckChanged / TipsChanged
    /// on every change, and LuckStreakChanged as documented on <see cref="LuckStreakChanged"/> (the session calls
    /// <see cref="OnVerdict"/> right after each LuckJudged).
    ///
    /// Lucky streak: consecutive Lucky verdicts today (Lucky +1, Unlucky -> 0, Neutral keeps; day start -> 0 silently).
    /// The streak multiplier is taken at PAYMENT time: a guest's tip = baseTip x its own lucky floor/occupancy multipliers
    /// x <see cref="StreakTipMultiplier"/> at the moment it steps out (a streak broken during the ride costs the bonus,
    /// one built during it earns it). Bad data is clamped here: bonus &lt; 0 or NaN = 0, max &lt; 1 or NaN = 1.
    /// </summary>
    public sealed class ScoreKeeper : IScoreQuery
    {
        readonly RulesDef rules;
        readonly IEventBus bus;

        public ScoreKeeper(RulesDef rules, IEventBus bus)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Luck = rules.startLuck;
            Tips = rules.startTips;
        }

        public int Luck { get; private set; }
        public int MaxLuck => rules.maxLuck;
        public int Tips { get; private set; }
        public int TipsEarnedToday { get; private set; }
        public int LuckStreak { get; private set; }
        public int BestStreakToday { get; private set; }
        public float StreakTipMultiplier => MultiplierFor(LuckStreak);

        /// <summary>1 + streak x bonus, capped at the max (exactly 1 when the streak is 0).</summary>
        internal float MultiplierFor(int streak)
        {
            if (streak <= 0) return 1f;
            float bonus = rules.streakTipBonusPerStep > 0f ? rules.streakTipBonusPerStep : 0f; // NaN -> 0
            float max = rules.streakMaxTipMultiplier >= 1f ? rules.streakMaxTipMultiplier : 1f;  // NaN -> 1
            return Math.Min(max, 1f + streak * bonus);
        }

        /// <summary>Applies one luck verdict to the streak; publishes LuckStreakChanged when it changed.</summary>
        internal void OnVerdict(LuckVerdict verdict)
        {
            int prev = LuckStreak;
            if (verdict == LuckVerdict.Lucky)
            {
                LuckStreak = prev + 1;
                if (LuckStreak > BestStreakToday) BestStreakToday = LuckStreak;
            }
            else if (verdict == LuckVerdict.Unlucky && prev > 0) LuckStreak = 0;
            else return;
            bus.Publish(new LuckStreakChanged(LuckStreak, prev, StreakTipMultiplier));
        }

        /// <summary>Adds (clamped to 0..MaxLuck). Returns the applied delta; publishes LuckChanged only if it changed.</summary>
        internal int AddLuck(int delta, LuckSource source) => SetLuck(Luck + delta, source);

        internal int SetLuck(int value, LuckSource source)
        {
            int next = Math.Max(0, Math.Min(MaxLuck, value));
            int prev = Luck;
            if (next == prev) return 0;
            Luck = next;
            bus.Publish(new LuckChanged(prev, next, MaxLuck, source));
            return next - prev;
        }

        /// <summary>Earned (delivery) when positive and <paramref name="earned"/>; spending passes a negative delta.</summary>
        internal void AddTips(int delta, bool earned)
        {
            if (delta == 0) return;
            int prev = Tips;
            Tips = Math.Max(0, Tips + delta);
            if (earned && delta > 0) TipsEarnedToday += delta;
            if (Tips != prev) bus.Publish(new TipsChanged(prev, Tips));
        }

        /// <summary>Day start: today's tips and the streak back to 0 (silently).</summary>
        internal void BeginDay()
        {
            TipsEarnedToday = 0;
            ResetStreak();
        }

        void ResetStreak()
        {
            LuckStreak = 0;
            BestStreakToday = 0;
        }

        /// <summary>New game: back to start values (publishes Luck/tips changes; the streak resets silently).</summary>
        internal void ResetForNewGame()
        {
            SetLuck(rules.startLuck, LuckSource.Other);
            TipsEarnedToday = 0;
            ResetStreak();
            if (Tips != rules.startTips)
            {
                int prev = Tips;
                Tips = rules.startTips;
                bus.Publish(new TipsChanged(prev, Tips));
            }
        }
    }
}
