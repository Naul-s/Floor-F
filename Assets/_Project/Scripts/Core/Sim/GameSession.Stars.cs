using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// Star night (<see cref="IDayQuery.IsStarNight"/>) and wish stars (<see cref="DayDef.wishStars"/>).
    ///
    /// Lucky 7: on a star night every verdict uses <see cref="StarNightRules.Apply"/> of the guest's own numbers, so 7 is
    /// Lucky for everyone from every source (floor label, head count, ride distance; a guest's own unlucky 7 loses, and a
    /// gambler counts 7 riders as lucky). <see cref="GuestInfo.Luck"/> stays the guest's OWN numbers. When 7 is Lucky only
    /// because of the star night (not by the guest's own numbers) no own-number line is said - like the Friday override.
    /// The same rule, generalised (<see cref="LuckyForAllRules"/>), applies to <see cref="DayDef.luckyForAll"/> (Christmas
    /// Eve's 12) on any day, on top of the star night's 7.
    ///
    /// Wish stars (any real day with wishStars &gt; 0; never the tutorial shift): at BeginDay the appear times are drawn
    /// from one seed taken from the session Random - one per equal slice of
    /// [<see cref="WishStarMarginSeconds"/>, duration - margin - wishStarSeconds], uniformly inside its slice, so they are
    /// spread over the day and every star can finish crossing before the clock runs out. Each star publishes
    /// <see cref="WishStarAppeared"/> on the day clock (viewport upper sky, see <see cref="SkyMinX"/>..), crosses for
    /// rules.wishStarSeconds of SIMULATION time (it freezes with the clock: menu pause, tutorial hold) and ends with exactly
    /// one of <see cref="WishMade"/> (<see cref="MakeWish"/> while it crosses: +rules.wishLuckBonus Luck, source Story,
    /// published after WishMade) or <see cref="WishStarFaded"/> (crossed without a wish, or the day ended / the run was
    /// abandoned while it was up). The early day end waits until every star of the day has appeared and is gone.
    /// Star ids are unique for the session (they keep counting across days and restarts).
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>No wish star appears in the first / finishes in the last this many seconds of a day.</summary>
        public const float WishStarMarginSeconds = 8f;

        /// <summary>Viewport box every wish star stays in (upper sky, clear of the HUD corners).</summary>
        public const float SkyMinX = 0.3f, SkyMaxX = 0.75f, SkyMinY = 0.6f, SkyMaxY = 0.95f;

        /// <summary>Used when rules.wishStarSeconds is not positive (bad data).</summary>
        const float DefaultWishStarSeconds = 3f;

        sealed class WishStar
        {
            public int Id;
            public float AppearAt;
            public float Age;
            public float FromX, FromY, ToX, ToY;
        }

        readonly List<WishStar> wishPending = new List<WishStar>();
        readonly List<WishStar> wishSky = new List<WishStar>();
        int nextWishStarId = 1;
        int wishesMadeToday;

        public bool IsStarNight => dayDef != null && dayDef.starNight;

        /// <summary>Seconds a wish star crosses the sky (rules.wishStarSeconds; not positive/NaN = 3).</summary>
        public float WishStarSeconds => rules.wishStarSeconds > 0f ? rules.wishStarSeconds : DefaultWishStarSeconds;

        /// <summary>Wishes made on the current/last day (reset at BeginDay).</summary>
        public int WishesMadeToday => wishesMadeToday;

        /// <summary>Appear times (day clock) of today's wish stars that have not appeared yet, in order. Tests / bots.</summary>
        internal IEnumerable<float> PendingWishStarTimes
        {
            get { foreach (var s in wishPending) yield return s.AppearAt; }
        }

        /// <summary>Clears <paramref name="results"/>, fills it with the ids of the stars crossing now (MakeWish targets).</summary>
        public int GetWishStarsInSky(List<int> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (var s in wishSky) results.Add(s.Id);
            return results.Count;
        }

        /// <summary>True while a star of today is still to appear or crossing.</summary>
        internal bool WishStarsRemaining => wishPending.Count > 0 || wishSky.Count > 0;

        public bool MakeWish(int starId)
        {
            if (!AcceptsInput || tutorialHold) return false;
            int i = wishSky.FindIndex(s => s.Id == starId);
            if (i < 0) return false;
            wishSky.RemoveAt(i);
            wishesMadeToday++;
            int bonus = Math.Max(0, rules.wishLuckBonus);
            int gain = Math.Max(0, Math.Min(bonus, Score.MaxLuck - Score.Luck));
            bus.Publish(new WishMade(starId, gain));
            Score.AddLuck(bonus, LuckSource.Story);
            return true;
        }

        // JudgingProfile (star night 7 + DayDef.luckyForAll): GameSession.Season.cs

        /// <summary>A Lucky verdict that only the star night made Lucky (no own-number line for it).</summary>
        static bool IsStarOverride(LuckVerdict own, LuckVerdict judged) => judged == LuckVerdict.Lucky && own != LuckVerdict.Lucky;

        /// <summary>BeginDay: draws today's wish stars (nothing on tutorial days or with wishStars 0).</summary>
        void ScheduleWishStars()
        {
            wishPending.Clear();
            wishSky.Clear();
            wishesMadeToday = 0;
            int n = dayDef.tutorial ? 0 : Math.Max(0, dayDef.wishStars);
            if (n == 0) return;
            var r = new Random(random.Next());
            float secs = WishStarSeconds;
            float start = WishStarMarginSeconds;
            float end = dayDef.durationSeconds - WishStarMarginSeconds - secs;
            if (end < start) start = end = Math.Max(0f, (dayDef.durationSeconds - secs) * 0.5f);
            float slice = (end - start) / n;
            for (int i = 0; i < n; i++)
            {
                var star = new WishStar { Id = nextWishStarId++, AppearAt = start + slice * (i + (float)r.NextDouble()) };
                // a diagonal streak falling left or right across the upper sky
                float dx = 0.2f + 0.05f * (float)r.NextDouble();
                float dy = 0.14f + 0.06f * (float)r.NextDouble();
                bool leftward = r.Next(2) == 0;
                float x0 = leftward ? SkyMinX + dx : SkyMinX;
                float x1 = leftward ? SkyMaxX : SkyMaxX - dx;
                star.FromX = x0 + (x1 - x0) * (float)r.NextDouble();
                star.ToX = leftward ? star.FromX - dx : star.FromX + dx;
                star.FromY = SkyMaxY - 0.09f * (float)r.NextDouble();
                star.ToY = star.FromY - dy;
                wishPending.Add(star);
            }
        }

        /// <summary>One simulation slice: stars in the sky age (and fade when done), due stars appear.</summary>
        void StepWishStars(float dt)
        {
            if (wishSky.Count == 0 && wishPending.Count == 0) return;
            float secs = WishStarSeconds;
            for (int i = 0; i < wishSky.Count; i++)
            {
                var s = wishSky[i];
                s.Age += dt;
                if (s.Age < secs - 1e-5f) continue;
                wishSky.RemoveAt(i--);
                bus.Publish(new WishStarFaded(s.Id));
            }
            while (wishPending.Count > 0 && wishPending[0].AppearAt <= elapsed + 1e-5f)
            {
                var s = wishPending[0];
                wishPending.RemoveAt(0);
                s.Age = Math.Max(0f, elapsed - s.AppearAt);
                wishSky.Add(s);
                bus.Publish(new WishStarAppeared(s.Id, s.FromX, s.FromY, s.ToX, s.ToY, secs));
            }
        }

        /// <summary>Abandoned run (restart / title): stars fade and today's wish count is the new run's (0).</summary>
        void ResetWishStars()
        {
            FadeAllWishStars();
            wishesMadeToday = 0;
        }

        /// <summary>Day end / abandoned run: every star still up fades; the ones still to come are dropped.</summary>
        void FadeAllWishStars()
        {
            wishPending.Clear();
            if (wishSky.Count == 0) return;
            var up = wishSky.ToArray();
            wishSky.Clear();
            foreach (var s in up) bus.Publish(new WishStarFaded(s.Id));
        }
    }
}
