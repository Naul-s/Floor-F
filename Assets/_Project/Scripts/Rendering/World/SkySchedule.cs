using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>Star kinds of the night sky (see <see cref="StarField"/>).</summary>
    enum StarKind { Steady, Twinkle, Glint }

    /// <summary>One star of the field, in low-res target pixels (0..640 x 0..360, origin bottom-left = viewport origin).</summary>
    readonly struct SkyStar
    {
        public readonly int X, Y;
        public readonly StarKind Kind;
        /// <summary>Steady: colour tone 0..3. Twinkle: twinkle group. Glint: glint index.</summary>
        public readonly int Tone;
        public readonly int Group;
        /// <summary>Drawn as a little "+" (bright stars only).</summary>
        public readonly bool Cross;

        public SkyStar(int x, int y, StarKind kind, int tone, int group, bool cross)
        {
            X = x; Y = y; Kind = kind; Tone = tone; Group = group; Cross = cross;
        }
    }

    /// <summary>
    /// Pure, seeded star field (EditMode-tested): positions in viewport pixels, three kinds - steady, gently twinkling
    /// (slow brightness waves shared by a few groups) and "glint" stars that are invisible until they flash once.
    /// </summary>
    static class StarField
    {
        public const int Width = RenderingStyle.PixelWidth;
        public const int Height = RenderingStyle.PixelHeight;
        public const int SteadyCount = 100;
        public const int TwinkleCount = 36;
        public const int GlintCount = 16;
        public const int TwinkleGroups = 8;
        /// <summary>Lowest star row: everything below is street and buildings in both camera frames.</summary>
        public const int MinY = 100;
        /// <summary>Glints flash in the open upper sky only (a glint behind a building would be wasted).</summary>
        public const int GlintMinY = 190;
        /// <summary>Stars keep this Chebyshev distance from each other (room for a "+").</summary>
        public const int Spacing = 3;

        public static List<SkyStar> Generate(int seed, Vector2Int moon, int moonKeepOut)
        {
            int total = GlintCount + TwinkleCount + SteadyCount;
            var list = new List<SkyStar>(total);
            int keep2 = moonKeepOut * moonKeepOut;
            for (int attempt = 0; list.Count < total && attempt < total * 60; attempt++)
            {
                int i = list.Count;
                var kind = i < GlintCount ? StarKind.Glint : i < GlintCount + TwinkleCount ? StarKind.Twinkle : StarKind.Steady;
                int minY = kind == StarKind.Glint ? GlintMinY : MinY;
                float hx = LightSchedule.Hash(seed, attempt, 40, 0), hy = LightSchedule.Hash(seed, attempt, 41, 0);
                int x = 3 + (int)(hx * (Width - 6));
                // denser towards the top (sqrt), never in the last rows
                int y = minY + (int)(Mathf.Sqrt(hy) * (Height - 3 - minY));
                int mx = x - moon.x, my = y - moon.y;
                if (mx * mx + my * my <= keep2) continue;
                bool clear = true;
                foreach (var s in list)
                    if (Mathf.Abs(s.X - x) <= Spacing && Mathf.Abs(s.Y - y) <= Spacing) { clear = false; break; }
                if (!clear) continue;
                float ht = LightSchedule.Hash(seed, attempt, 42, 0);
                int tone = ht < 0.5f ? 0 : ht < 0.8f ? 1 : ht < 0.92f ? 2 : 3;
                bool cross = false;
                int group = 0;
                if (kind == StarKind.Steady) cross = tone >= 2 && LightSchedule.Hash(seed, attempt, 43, 0) < 0.4f;
                else if (kind == StarKind.Twinkle)
                {
                    group = (int)(LightSchedule.Hash(seed, attempt, 44, 0) * TwinkleGroups) % TwinkleGroups;
                    cross = LightSchedule.Hash(seed, attempt, 43, 0) < 0.22f;
                    tone = 2;
                }
                else group = i;
                list.Add(new SkyStar(x, y, kind, tone, group, cross));
            }
            return list;
        }
    }

    /// <summary>
    /// Pure, seeded sky schedules (EditMode-tested, PhotosensitivityTests): twinkle waves, glints, the star-night meteor
    /// shower and the wish-star envelopes. Like <see cref="LightSchedule"/> everything is a function of (seed, index,
    /// time) - no RNG state - so the same seed shows the same sky at the same time, and a frozen clock freezes it.
    /// Photosensitivity: twinkles are slow waves (&gt;= 2.4 s period), glints and meteors are one rise + one fall each and
    /// live in their own time slots (never two inside one second), wish stars fade in/out once.
    /// </summary>
    static class SkySchedule
    {
        // ---- twinkle: slow brightness waves (level TwinkleLow..1)
        public const float TwinkleMinPeriod = 2.4f;
        public const float TwinkleMaxPeriod = 5.2f;
        public const float TwinkleLow = 0.38f;

        public static float TwinklePeriod(int seed, int group) => Mathf.Lerp(TwinkleMinPeriod, TwinkleMaxPeriod, LightSchedule.Hash(seed, group, 31, 0));

        public static float TwinkleLevel(int seed, int group, float time)
        {
            float period = TwinklePeriod(seed, group);
            float a = (time / period + LightSchedule.Hash(seed, group, 32, 0)) * Mathf.PI * 2f;
            // main wave + a small slower drift so groups never look metronomic
            float s = 0.5f + 0.42f * Mathf.Sin(a) + 0.08f * Mathf.Sin(a * 0.37f + 2f);
            return Mathf.Lerp(TwinkleLow, 1f, Mathf.Clamp01(s));
        }

        // ---- glints: invisible stars that flash once (one glint per slot at most)
        public const float GlintSlot = 2.6f;
        public const float GlintChance = 0.75f;
        public const float GlintRise = 0.12f;
        public const float GlintHold = 0.1f;
        public const float GlintFall = 0.5f;
        public const float GlintSeconds = GlintRise + GlintHold + GlintFall;

        static float GlintEnvelope(float r)
        {
            if (r < 0f || r >= GlintSeconds) return 0f;
            if (r < GlintRise) return r / GlintRise;
            if (r < GlintRise + GlintHold) return 1f;
            float k = 1f - (r - GlintRise - GlintHold) / GlintFall;
            return k * k;
        }

        /// <summary>Level (0 = none) of the glint flashing at <paramref name="time"/> and which glint star it is.</summary>
        public static float Glint(int seed, int count, float time, out int index)
        {
            index = -1;
            if (count <= 0) return 0f;
            float t = Mathf.Max(0f, time);
            int k = Mathf.FloorToInt(t / GlintSlot);
            if (LightSchedule.Hash(seed, 50, k, 0) >= GlintChance) return 0f;
            // starts late enough after the slot edge and ends >= 0.25 s before the next slot: >= 0.45 s dark between glints
            float start = k * GlintSlot + 0.2f + LightSchedule.Hash(seed, 50, k, 1) * (GlintSlot - 0.45f - GlintSeconds);
            float level = GlintEnvelope(t - start);
            if (level <= 0f) return 0f;
            index = Mathf.Min(count - 1, (int)(LightSchedule.Hash(seed, 50, k, 2) * count));
            return level;
        }

        /// <summary>Brightness of glint star <paramref name="index"/> (0 while invisible).</summary>
        public static float GlintLevel(int seed, int count, int index, float time)
        {
            float l = Glint(seed, count, time, out int i);
            return i == index ? l : 0f;
        }

        // ---- meteor shower (star night): at most one meteor per slot, ~one every 5 s
        public const float MeteorSlot = 3.4f;
        public const float MeteorChance = 0.62f;
        public const float MeteorMinSeconds = 0.5f;
        public const float MeteorMaxSeconds = 0.85f;

        public struct MeteorPose
        {
            /// <summary>Head position in viewport pixels (float).</summary>
            public Vector2 Head;
            /// <summary>Unit direction of travel (pixels).</summary>
            public Vector2 Dir;
            public float Level;
            public int Tail;
            public int Slot;
        }

        public static bool Meteor(int seed, float time, out MeteorPose m)
        {
            m = default;
            float t = Mathf.Max(0f, time);
            int k = Mathf.FloorToInt(t / MeteorSlot);
            if (LightSchedule.Hash(seed, 60, k, 0) >= MeteorChance) return false;
            float duration = Mathf.Lerp(MeteorMinSeconds, MeteorMaxSeconds, LightSchedule.Hash(seed, 60, k, 1));
            float start = k * MeteorSlot + 0.2f + LightSchedule.Hash(seed, 60, k, 2) * (MeteorSlot - 0.5f - duration);
            float r = t - start;
            if (r < 0f || r >= duration) return false;
            float p = r / duration;
            // radiant up and to the right: every meteor falls down-left, a little spread
            float ang = (205f + (LightSchedule.Hash(seed, 60, k, 3) - 0.5f) * 22f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var from = new Vector2(Mathf.Lerp(90f, 630f, LightSchedule.Hash(seed, 60, k, 4)), Mathf.Lerp(235f, 352f, LightSchedule.Hash(seed, 60, k, 5)));
            float travel = Mathf.Lerp(70f, 130f, LightSchedule.Hash(seed, 60, k, 6));
            m.Head = from + dir * (travel * p);
            m.Dir = dir;
            m.Level = MeteorLevel(p);
            m.Tail = Mathf.RoundToInt(Mathf.Lerp(13f, 24f, LightSchedule.Hash(seed, 60, k, 7)) * Mathf.Min(1f, p * 3f + 0.2f));
            m.Slot = k;
            return true;
        }

        /// <summary>Meteor brightness over its life <paramref name="p"/> (0..1): quick rise, long fade.</summary>
        public static float MeteorLevel(float p)
        {
            if (p < 0f || p >= 1f) return 0f;
            return p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f;
        }

        /// <summary>Sum of meteor brightness at <paramref name="time"/> (tests: the shower as a whole).</summary>
        public static float MeteorBrightness(int seed, float time) => Meteor(seed, time, out var m) ? m.Level : 0f;

        // ---- wish stars
        public const float WishFadeIn = 0.3f;
        public const float WishFadeOut = 0.45f;
        public const int WishTail = 26;
        public const float SparkleEvery = 0.055f;
        public const float SparkleLife = 0.85f;
        public const float BurstLife = 1.0f;
        public const int BurstCount = 18;
        /// <summary>Wish head halo pulse (outer arm on/off), slow.</summary>
        public const float WishPulsePeriod = 1.6f;

        /// <summary>Head brightness <paramref name="age"/> s after it appeared; <paramref name="endAge"/> &lt; 0 while crossing.</summary>
        public static float WishHeadLevel(float age, float endAge)
        {
            float l = Mathf.Clamp01(age / WishFadeIn);
            if (endAge >= 0f) l *= Mathf.Clamp01(1f - (age - endAge) / WishFadeOut);
            return l;
        }

        public static bool WishPulse(float age) => Mathf.Repeat(age / WishPulsePeriod, 1f) < 0.5f;

        /// <summary>Tail sparkle brightness (monotonic fade).</summary>
        public static float SparkleLevel(float age)
        {
            if (age < 0f || age >= SparkleLife) return 0f;
            return Mathf.Pow(1f - age / SparkleLife, 1.3f);
        }

        /// <summary>Wish burst spark brightness (monotonic fade, slightly different lives).</summary>
        public static float BurstLevel(float age, int index)
        {
            float life = BurstLife * (0.7f + 0.3f * LightSchedule.Hash(7, index, 70, 0));
            if (age < 0f || age >= life) return 0f;
            return 1f - age / life;
        }

        /// <summary>Burst spark offset (pixels) from the burst point.</summary>
        public static Vector2 BurstOffset(float age, int index)
        {
            float ang = (index + 0.4f * LightSchedule.Hash(7, index, 71, 0)) * Mathf.PI * 2f / BurstCount;
            float speed = 34f + 30f * LightSchedule.Hash(7, index, 72, 0);
            float dist = speed * (1f - Mathf.Exp(-3f * age)) / 3f;
            return new Vector2(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist - 9f * age * age);
        }
    }
}
