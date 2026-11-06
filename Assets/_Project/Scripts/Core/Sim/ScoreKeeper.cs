using System;

namespace FloorF.Core
{
    /// <summary><see cref="IScoreQuery"/>: hotel Luck meter (0..max) and tips. Publishes LuckChanged / TipsChanged on every change.</summary>
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

        internal void BeginDay() => TipsEarnedToday = 0;

        /// <summary>New game: back to start values (publishes changes).</summary>
        internal void ResetForNewGame()
        {
            SetLuck(rules.startLuck, LuckSource.Other);
            TipsEarnedToday = 0;
            if (Tips != rules.startTips)
            {
                int prev = Tips;
                Tips = rules.startTips;
                bus.Publish(new TipsChanged(prev, Tips));
            }
        }
    }
}
