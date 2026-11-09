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
    /// Photosensitivity: twinkles are slow waves (&gt;= 2.4 s period), glints are one rise + one fall each in their own time
    /// slots (never two inside one second); meteors are small MOVING streaks (one rise + one fall each, a few dozen pixels,
    /// never a flashing area - several may cross the sky at once); wish stars fade in/out once.
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

        // ---- meteor shower (star night): a steady stream of shooting stars, now and then a burst or a fireball.
        // Slot k (ShowerSlot s long) always starts one lead meteor, ShowerJitter into the slot, so consecutive leads are
        // 0.6..1.5 s apart; BurstChance of the slots add 1-2 followers close behind; FireballChance of the leads are big,
        // slow, green-white fireballs. Every meteor is ONE small moving streak with one rise + one fall of brightness.

        public const float ShowerSlot = 1.05f;
        public const float ShowerJitter = 0.45f;
        public const float BurstChance = 0.2f;
        public const float BurstMinDelay = 0.12f;
        public const float BurstMaxDelay = 0.38f;
        public const float FireballChance = 0.1f;
        public const float MeteorMinSeconds = 0.45f;
        public const float MeteorMaxSeconds = 0.8f;
        public const float FireballMinSeconds = 1.0f;
        public const float FireballMaxSeconds = 1.4f;
        public const int MaxPerSlot = 3;
        /// <summary>Longest a slot's meteors can still be in the sky after the slot starts.</summary>
        public const float MaxSlotLife = ShowerJitter + (MaxPerSlot - 1) * BurstMaxDelay + FireballMaxSeconds;

        public struct MeteorPose
        {
            /// <summary>Head position in viewport pixels (float).</summary>
            public Vector2 Head;
            /// <summary>Unit direction of travel (pixels).</summary>
            public Vector2 Dir;
            public float Level;
            public int Tail;
            public int Slot;
            /// <summary>0 = the slot's lead meteor, 1..2 = burst followers.</summary>
            public int Index;
            public bool Fireball;
            /// <summary>Unique per meteor (slot * MaxPerSlot + index).</summary>
            public int Id => Slot * MaxPerSlot + Index;
            /// <summary>Streak width in pixels near the head (fireballs are thick).</summary>
            public int Width => Fireball ? 3 : 1;
        }

        static float H(int seed, int k, int j, int field) => LightSchedule.Hash(seed, 600 + j * 20 + field, k, 7);

        /// <summary>Meteors in slot <paramref name="k"/>: 1, or 2-3 in a burst.</summary>
        public static int SlotCount(int seed, int k)
        {
            if (H(seed, k, 0, 1) >= BurstChance) return 1;
            return H(seed, k, 0, 2) < 0.6f ? 2 : 3;
        }

        public static float MeteorStart(int seed, int k, int j)
        {
            float lead = k * ShowerSlot + H(seed, k, 0, 0) * ShowerJitter;
            float t = lead;
            for (int i = 1; i <= j; i++) t += Mathf.Lerp(BurstMinDelay, BurstMaxDelay, H(seed, k, i, 0));
            return t;
        }

        public static bool IsFireball(int seed, int k, int j) => j == 0 && H(seed, k, 0, 3) < FireballChance;

        public static float MeteorDuration(int seed, int k, int j)
            => IsFireball(seed, k, j) ? Mathf.Lerp(FireballMinSeconds, FireballMaxSeconds, H(seed, k, j, 4)) : Mathf.Lerp(MeteorMinSeconds, MeteorMaxSeconds, H(seed, k, j, 4));

        /// <summary>Meteor j of slot k at <paramref name="time"/> (false while it is not in the sky).</summary>
        public static bool MeteorAt(int seed, int k, int j, float time, out MeteorPose m)
        {
            m = default;
            if (k < 0 || j < 0 || j >= SlotCount(seed, k)) return false;
            float start = MeteorStart(seed, k, j), duration = MeteorDuration(seed, k, j);
            float r = time - start;
            if (r < 0f || r >= duration) return false;
            float p = r / duration;
            bool fire = IsFireball(seed, k, j);
            // one radiant up and to the right: every meteor falls down-left, a little spread
            float ang = (205f + (H(seed, k, j, 5) - 0.5f) * 24f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 from;
            if (j == 0) from = new Vector2(Mathf.Lerp(70f, 660f, H(seed, k, 0, 6)), Mathf.Lerp(250f, 356f, H(seed, k, 0, 7)));
            else
            {
                // burst followers start near their lead (same patch of sky, slightly apart)
                var lead = new Vector2(Mathf.Lerp(70f, 660f, H(seed, k, 0, 6)), Mathf.Lerp(250f, 356f, H(seed, k, 0, 7)));
                from = lead + new Vector2((H(seed, k, j, 6) - 0.5f) * 140f, (H(seed, k, j, 7) - 0.5f) * 50f);
                from.y = Mathf.Min(from.y, 356f);
            }
            float travel = fire ? Mathf.Lerp(150f, 210f, H(seed, k, j, 8)) : Mathf.Lerp(60f, 120f, H(seed, k, j, 8));
            m.Head = from + dir * (travel * p);
            m.Dir = dir;
            m.Level = MeteorLevel(p);
            float tail = fire ? Mathf.Lerp(26f, 36f, H(seed, k, j, 9)) : Mathf.Lerp(11f, 20f, H(seed, k, j, 9));
            m.Tail = Mathf.RoundToInt(tail * Mathf.Min(1f, p * 3f + 0.2f));
            m.Slot = k;
            m.Index = j;
            m.Fireball = fire;
            return true;
        }

        /// <summary>Every meteor in the sky at <paramref name="time"/> (appended to <paramref name="into"/>; returns the count).</summary>
        public static int Meteors(int seed, float time, List<MeteorPose> into)
        {
            float t = Mathf.Max(0f, time);
            int k1 = Mathf.FloorToInt(t / ShowerSlot), k0 = Mathf.Max(0, Mathf.FloorToInt((t - MaxSlotLife) / ShowerSlot));
            int n = 0;
            for (int k = k0; k <= k1; k++)
                for (int j = 0; j < MaxPerSlot; j++)
                    if (MeteorAt(seed, k, j, t, out var m)) { into?.Add(m); n++; }
            return n;
        }

        /// <summary>The brightest meteor in the sky at <paramref name="time"/>.</summary>
        public static bool Meteor(int seed, float time, out MeteorPose m)
        {
            m = default;
            bool any = false;
            float t = Mathf.Max(0f, time);
            int k1 = Mathf.FloorToInt(t / ShowerSlot), k0 = Mathf.Max(0, Mathf.FloorToInt((t - MaxSlotLife) / ShowerSlot));
            for (int k = k0; k <= k1; k++)
                for (int j = 0; j < MaxPerSlot; j++)
                    if (MeteorAt(seed, k, j, t, out var c) && (!any || c.Level > m.Level)) { m = c; any = true; }
            return any;
        }

        /// <summary>Meteor brightness over its life <paramref name="p"/> (0..1): quick rise, long fade.</summary>
        public static float MeteorLevel(float p)
        {
            if (p < 0f || p >= 1f) return 0f;
            return p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f;
        }

        /// <summary>Brightness of ONE meteor (slot k, index j) over time (photosensitivity: one rise + one fall).</summary>
        public static float MeteorLevelOf(int seed, int k, int j, float time) => MeteorAt(seed, k, j, time, out var m) ? m.Level : 0f;

        /// <summary>
        /// Target pixels a meteor lights (streak length x width + the head's "+" and glow allowance) - tests: the shower
        /// is small moving objects, never a flashing area.
        /// </summary>
        public static int MeteorPixels(in MeteorPose m) => (m.Tail + 1) * m.Width + (m.Fireball ? 24 : 5);

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
