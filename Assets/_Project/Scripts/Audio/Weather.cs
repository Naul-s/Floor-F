using FloorF.Core;
using UnityEngine;

namespace FloorF.Audio
{
    static class WeatherRules
    {
        /// <summary>
        /// Rain is heard on a rainy day (IDayQuery.IsRainy) while that day is on screen: Intro, Running and its Summary.
        /// Never on the title (its attract demo), in the shop or on an ending.
        /// </summary>
        public static bool RainAudible(bool rainy, DayPhase phase)
            => rainy && (phase == DayPhase.Intro || phase == DayPhase.Running || phase == DayPhase.Summary);

        /// <summary>The shortest possible time between two scheduled rumbles: min flash gap - (max delay - min delay).</summary>
        public static float MinStrikeSpacing(in ThunderSettings s) => s.MinGap - (s.DelayMax - s.DelayMin);
    }

    /// <summary>The rain bed's fade: linear in over FadeIn s while wanted, out over FadeOut s otherwise.</summary>
    sealed class RainBed
    {
        public float Gain { get; private set; }
        public bool Wanted { get; private set; }
        /// <summary>The voice should be playing (fading in, on, or fading out).</summary>
        public bool Active => Gain > 0f || Wanted;

        public void Step(float dt, bool wanted, float fadeIn, float fadeOut)
        {
            Wanted = wanted;
            Gain = TravelHum.StepGain(Gain, wanted ? 1f : 0f, Mathf.Max(0f, dt), fadeIn, fadeOut);
        }

        public void Kill() { Gain = 0f; Wanted = false; }
    }

    /// <summary>Thunder timing (copied from <see cref="AudioLibrary"/> so the schedule stays testable without the asset).</summary>
    readonly struct ThunderSettings
    {
        public readonly float MinGap, MaxGap, DelayMin, DelayMax;
        public ThunderSettings(float minGap, float maxGap, float delayMin, float delayMax)
        {
            MinGap = Mathf.Max(0.5f, minGap);
            MaxGap = Mathf.Max(MinGap, maxGap);
            DelayMin = Mathf.Max(0f, delayMin);
            DelayMax = Mathf.Max(DelayMin, delayMax);
        }

        public static ThunderSettings From(AudioLibrary lib)
            => new ThunderSettings(lib.thunderMinGap, lib.thunderMaxGap, lib.thunderDelayMin, lib.thunderDelayMax);
    }

    /// <summary>
    /// When distant thunder rumbles on a rainy day. Rendering draws its own lightning (it cannot talk to Audio), so the
    /// sound keeps a schedule of implied flashes: one every MinGap..MaxGap s (uniform), each heard DelayMin..DelayMax s
    /// later. Seeded (per day, <see cref="SeedFor"/>): the same day always rumbles at the same times. Rumbles are at least
    /// <see cref="WeatherRules.MinStrikeSpacing"/> apart; a Step reports at most one. The caller only Steps it while the
    /// rain is wanted and the game is not paused / held, so the clock is "rainy, unpaused seconds".
    /// </summary>
    sealed class ThunderSchedule
    {
        System.Random rng;
        // double: an hour of 1/60 s steps must not drift
        double clock, flashAt, strikeAt;

        /// <summary>Seconds stepped since <see cref="Reset"/>.</summary>
        public float Clock => (float)clock;
        /// <summary>Clock time of the next rumble.</summary>
        public float NextStrikeAt => (float)strikeAt;
        public float NextStrikeIn => (float)(strikeAt - clock);
        public bool Armed => rng != null;

        public static int SeedFor(int day) => unchecked(0x7D13 * 31 + day * 7919);

        public void Reset(int seed, in ThunderSettings s)
        {
            rng = new System.Random(seed);
            clock = 0;
            flashAt = Gap(s);
            strikeAt = flashAt + Delay(s);
        }

        public void Disarm() => rng = null;

        /// <summary>Advances by <paramref name="dt"/>; true when a rumble is due now (then schedules the next one).</summary>
        public bool Step(float dt, in ThunderSettings s)
        {
            if (rng == null) return false;
            clock += Mathf.Max(0f, dt);
            if (clock < strikeAt) return false;
            // the next flash after this one; after a long jump (huge dt) it counts from now, never piling up rumbles
            flashAt = System.Math.Max(flashAt, clock - s.DelayMax) + Gap(s);
            strikeAt = flashAt + Delay(s);
            return true;
        }

        double Gap(in ThunderSettings s) => s.MinGap + rng.NextDouble() * (s.MaxGap - s.MinGap);
        double Delay(in ThunderSettings s) => s.DelayMin + rng.NextDouble() * (s.DelayMax - s.DelayMin);
    }
}
