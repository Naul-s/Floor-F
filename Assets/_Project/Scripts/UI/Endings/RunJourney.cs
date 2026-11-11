using System.Collections.Generic;
using FloorF.Core;

namespace FloorF.UI
{
    /// <summary>
    /// This run's real days as the summary cards graded them (UIRoot feeds it from DayEnded; GameStarted clears it): the
    /// ending card's row of grade stamps and its totals. Presentation only, pure C#.
    /// </summary>
    sealed class RunJourney
    {
        public readonly struct Entry
        {
            public readonly int Day;
            public readonly string Grade;
            public Entry(int day, string grade) { Day = day; Grade = grade; }
        }

        readonly List<Entry> days = new List<Entry>();
        readonly List<DaySummary> summaries = new List<DaySummary>();

        public IReadOnlyList<Entry> Days => days;
        public int Count => days.Count;

        public int Delivered { get { int n = 0; foreach (var s in summaries) n += s.Delivered; return n; } }
        public int TipsEarned { get { int n = 0; foreach (var s in summaries) n += s.TipsEarned; return n; } }
        public int BestStreak { get { int n = 0; foreach (var s in summaries) if (s.BestStreak > n) n = s.BestStreak; return n; } }

        public void Clear()
        {
            days.Clear();
            summaries.Clear();
        }

        /// <summary>Records real day <paramref name="displayDay"/> (1-based); the same day again replaces it (a replay).</summary>
        public void Add(int displayDay, DaySummary s)
        {
            if (displayDay <= 0) return;
            var e = new Entry(displayDay, UIFormat.DayGrade(s));
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i].Day != displayDay) continue;
                days[i] = e;
                summaries[i] = s;
                return;
            }
            int at = days.Count;
            while (at > 0 && days[at - 1].Day > displayDay) at--;
            days.Insert(at, e);
            summaries.Insert(at, s);
        }

        /// <summary>The grade of <paramref name="displayDay"/>, or null when it was not played.</summary>
        public string GradeOf(int displayDay)
        {
            foreach (var d in days) if (d.Day == displayDay) return d.Grade;
            return null;
        }
    }
}
