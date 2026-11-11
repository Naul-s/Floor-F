using System.Globalization;
using FloorF.Core;

namespace FloorF.UI
{
    /// <summary>
    /// The summary card's one-line hint (pure C#, EditMode-tested): it explains the grade stamp from the same points as
    /// <see cref="UIFormat.GradePoints"/>. An S day gets praise; otherwise the part that lost the most points gets advice
    /// (ties: guests who left, unlucky moments, Luck, streak). Nobody delivered = its own line.
    /// </summary>
    static class SummaryHint
    {
        public enum Kind { NoneDelivered, Left, Unlucky, Luck, Streak, PraiseStreak, PraiseClean, PraiseTop }

        public static Kind Pick(DaySummary s)
        {
            if (s.Delivered <= 0) return Kind.NoneDelivered;
            if (UIFormat.DayGrade(s) == "S")
                return s.BestStreak >= 5 ? Kind.PraiseStreak : s.UnluckyJudgements == 0 ? Kind.PraiseClean : Kind.PraiseTop;
            int served = s.Delivered + s.Left;
            float share = served > 0 ? (float)s.Delivered / served : 0f;
            int left = 3 - (share >= 0.95f ? 3 : share >= 0.8f ? 2 : share >= 0.6f ? 1 : 0);
            int unlucky = 2 - (s.UnluckyJudgements == 0 ? 2 : s.UnluckyJudgements <= 2 ? 1 : 0);
            int luck = 2 - (s.LuckDelta >= 10 ? 2 : s.LuckDelta >= 0 ? 1 : 0);
            int streak = 2 - (s.BestStreak >= 5 ? 2 : s.BestStreak >= 3 ? 1 : 0);
            var k = Kind.Left;
            int worst = left;
            if (unlucky > worst) { worst = unlucky; k = Kind.Unlucky; }
            if (luck > worst) { worst = luck; k = Kind.Luck; }
            if (streak > worst) { k = Kind.Streak; }
            return k;
        }

        public static string Text(DaySummary s, bool ko)
        {
            switch (Pick(s))
            {
                case Kind.NoneDelivered: return UIStrings.HintNoneDelivered.Get(ko);
                case Kind.Left: return s.Left == 1 ? UIStrings.HintLeftOne.Get(ko) : F(UIStrings.HintLeftN, ko, N(s.Left));
                case Kind.Unlucky: return s.UnluckyJudgements == 1 ? UIStrings.HintUnluckyOne.Get(ko) : F(UIStrings.HintUnluckyN, ko, N(s.UnluckyJudgements));
                case Kind.Luck: return F(s.LuckDelta < 0 ? UIStrings.HintLuckDown : UIStrings.HintLuckFlat, ko, UIFormat.Signed(s.LuckDelta));
                case Kind.Streak: return s.BestStreak <= 0 ? UIStrings.HintStreakNone.Get(ko) : F(UIStrings.HintStreakShort, ko, N(s.BestStreak));
                case Kind.PraiseStreak: return F(UIStrings.PraiseStreak, ko, N(s.BestStreak));
                case Kind.PraiseClean: return UIStrings.PraiseClean.Get(ko);
                default: return UIStrings.PraiseTop.Get(ko);
            }
        }

        static string F(Loc l, bool ko, params object[] args) => KoreanParticles.Format(l.Get(ko), args);
        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
