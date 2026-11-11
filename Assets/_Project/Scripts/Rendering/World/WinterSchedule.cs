using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pure, seeded winter schedules (EditMode-tested, see WinterTests): snowfall, the slow fade of the Christmas bulbs,
    /// the lottery highlight pulse, the Silent Night lights-out order and Santa's sleigh across the moon. Like
    /// <see cref="WeatherSchedule"/> everything is a function of (seed, index, time): a frozen clock freezes the season.
    /// Photosensitivity: nothing here switches on/off - bulbs and highlights are smooth sine fades whose period is far
    /// longer than 1/3 s (<see cref="BulbPeriod"/>, <see cref="HighlightPeriod"/>), never a strobe.
    /// </summary>
    static class WinterSchedule
    {
        /// <summary>Flakes at SnowAmount 1 (Christmas Eve). The count scales with the amount: WebGL-cheap.</summary>
        public const int MaxFlakes = 150;
        public const float FlakeMinSpeed = 13f;
        public const float FlakeMaxSpeed = 32f;
        /// <summary>Wind from the right: pixels per second of drift for the slowest flake (big ones drift more).</summary>
        public const float Drift = 5f;
        /// <summary>Snow cover appears from this amount (Christmas Eve 1.0); below it only a light dusting.</summary>
        public const float HeavyCover = 0.7f;
        /// <summary>Lowest amount that dusts the roofs (first snow, day 9 = 0.3).</summary>
        public const float DustCover = 0.15f;

        public struct Flake
        {
            /// <summary>Bottom-left pixel of the flake in viewport pixels.</summary>
            public Vector2 Pos;
            /// <summary>1 = single pixel, 2 = a 2x2 "near" flake.</summary>
            public int Size;
            public float Level;
        }

        public static int FlakeCount(float amount) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(amount) * MaxFlakes), 0, MaxFlakes);

        /// <summary>
        /// Flake <paramref name="i"/> at <paramref name="time"/>: drifts down through the viewport and wraps; sways on the
        /// wind (a slow sine of a few pixels) while it falls. <paramref name="calm"/> 0..1 slows the fall (Silent Night).
        /// </summary>
        public static Flake SnowFlake(int seed, int i, float time, float calm = 0f)
        {
            float near = LightSchedule.Hash(seed, i, 201, 0);
            bool big = near > 0.72f;
            float speed = Mathf.Lerp(FlakeMinSpeed, FlakeMaxSpeed, near) * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(calm));
            float span = StarField.Height + 12f;
            float t = Mathf.Max(0f, time);
            float fall = Mathf.Repeat(LightSchedule.Hash(seed, i, 202, 0) * span + t * speed, span);
            float y = StarField.Height + 4f - fall;
            float width = StarField.Width + 24f;
            float swayAmp = Mathf.Lerp(1.5f, big ? 6f : 4f, LightSchedule.Hash(seed, i, 203, 0));
            float swayHz = Mathf.Lerp(0.18f, 0.45f, LightSchedule.Hash(seed, i, 204, 0));
            float sway = Mathf.Sin((t * swayHz + LightSchedule.Hash(seed, i, 205, 0)) * Mathf.PI * 2f) * swayAmp;
            float drift = Drift * (0.6f + near) * (1f - 0.5f * Mathf.Clamp01(calm));
            float x = Mathf.Repeat(LightSchedule.Hash(seed, i, 206, 0) * width - t * drift + sway, width) - 12f;
            return new Flake
            {
                Pos = new Vector2(x, y),
                Size = big ? 2 : 1,
                Level = big ? Mathf.Lerp(0.9f, 1f, near) : Mathf.Lerp(0.62f, 0.92f, near),
            };
        }

        // ------------------------------------------------------------------ Christmas bulbs (slow, smooth fades)

        public const int BulbGroups = 4;
        /// <summary>One full fade cycle of a bulb group (seconds): 2 brightness changes per 3.6 s, far below 3 per second.</summary>
        public const float BulbPeriod = 3.6f;
        public const float BulbLow = 0.42f;

        /// <summary>Brightness 0..1 of bulb group <paramref name="group"/>: a raised cosine between BulbLow and 1, groups out of phase.</summary>
        public static float BulbLevel(int group, float time)
        {
            float phase = Mathf.Max(0f, time) / BulbPeriod + group / (float)BulbGroups;
            float wave = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            return Mathf.Lerp(BulbLow, 1f, wave);
        }

        /// <summary>The tree's star: a gentle glow (never off).</summary>
        public static float StarLevel(float time) => 0.8f + 0.2f * Mathf.Sin(Mathf.Max(0f, time) * 0.9f);

        // ------------------------------------------------------------------ lottery

        /// <summary>Lucky-floor gold highlight: one soft breath per <see cref="HighlightPeriod"/> s.</summary>
        public const float HighlightPeriod = 2.2f;

        public static float HighlightLevel(float time)
        {
            float wave = 0.5f - 0.5f * Mathf.Cos(Mathf.Max(0f, time) / HighlightPeriod * Mathf.PI * 2f);
            return Mathf.Lerp(0.55f, 1f, wave);
        }

        /// <summary>Lottery draw animation: drum spins up, a ball drops out at <see cref="DropAt"/>, lands at <see cref="DrawSeconds"/>.</summary>
        public const float DrawSeconds = 1.8f;
        public const float DropAt = 1.2f;

        /// <summary>Drum spin speed (degrees per second) at <paramref name="age"/> s into a draw: ramps up, holds, slows down.</summary>
        public static float DrumSpeed(float age)
        {
            if (age < 0f || age >= DrawSeconds) return 0f;
            float u = age / DrawSeconds;
            float k = u < 0.2f ? u / 0.2f : u > 0.75f ? (1f - u) / 0.25f : 1f;
            return 540f * k;
        }

        // ------------------------------------------------------------------ endings

        /// <summary>Silent Night: seconds between two floors going dark, top floor first.</summary>
        public const float LightsOutStep = 1.4f;
        public const float LightsOutStart = 1.0f;
        /// <summary>Fading floors drop by this much level per second (a slow dim, never a cut).</summary>
        public const float LightsOutSpeed = 0.7f;
        public const float LightsOutLevel = 0.1f;

        /// <summary>Order index -> floor index, top floor first, the lobby last (it keeps its tree lit).</summary>
        public static int LightsOutFloor(int order, int floorCount) => floorCount - 1 - order;

        /// <summary>Number of floors already told to go dark <paramref name="t"/> s into Silent Night (the lobby never).</summary>
        public static int LightsOutCount(float t, int floorCount)
        {
            if (t < LightsOutStart) return 0;
            return Mathf.Min(floorCount - 1, 1 + Mathf.FloorToInt((t - LightsOutStart) / LightsOutStep));
        }

        /// <summary>Winter Miracle: one flight of Santa's sleigh across the sky every <see cref="SleighPeriod"/> s.</summary>
        public const float SleighPeriod = 12f;
        public const float SleighSeconds = 7f;
        public const float SleighDelay = 1.5f;

        /// <summary>
        /// Sleigh position (viewport pixels, team leader = the first reindeer) <paramref name="t"/> s into the ending; false
        /// between flights. A shallow arc from the left edge through the moon's disc to the right edge.
        /// </summary>
        public static bool Sleigh(float t, Vector2Int moon, out Vector2 at, out float u)
        {
            at = default;
            u = 0f;
            if (t < SleighDelay) return false;
            float r = Mathf.Repeat(t - SleighDelay, SleighPeriod);
            if (r >= SleighSeconds) return false;
            u = r / SleighSeconds;
            // x: -40 .. width + 40 ; crosses the moon centre at u = moonU
            float x0 = -40f, x1 = StarField.Width + 40f;
            float x = Mathf.Lerp(x0, x1, u);
            float moonU = (moon.x - x0) / (x1 - x0);
            float dy = (u - moonU) * 70f;
            float y = moon.y - 2f + dy - 120f * (u - moonU) * (u - moonU);
            at = new Vector2(x, y);
            return true;
        }
    }
}
