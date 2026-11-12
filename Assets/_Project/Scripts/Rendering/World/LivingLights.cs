using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pure, seeded light schedules (EditMode-tested): city windows switching on/off, rare fluorescent-start flickers,
    /// the HOTEL sign losing a letter now and then, marquee bulbs, the antenna beacon. Everything is a function of
    /// (seed, index, time) - no RNG state - so the same seed shows the same city at the same time.
    /// Photosensitivity (WCAG 2.3.1): windows change every 40-150 s, flickers are short, rare, small and never make more
    /// than 3 bright/dark transitions in any second (PhotosensitivityTests); nothing ever strobes.
    /// </summary>
    static class LightSchedule
    {
        public const float MinPeriod = 40f;
        public const float MaxPeriod = 150f;
        /// <summary>Chance that a window turning on starts with a short fluorescent stutter.</summary>
        public const float StartFlickerChance = 0.3f;
        /// <summary>Chance per lit stretch of one brief drop-out.</summary>
        public const float DropChance = 0.1f;
        public const float DropSeconds = 0.14f;
        public const float SignEventPeriod = 11f;
        public const float SignEventChance = 0.6f;
        public const float SignDim = 0.2f;
        public const float BulbStep = 0.75f;
        public const float EerieSignPeriod = 6.5f;
        /// <summary>Friday the 13th: fewer windows lit across the city.</summary>
        public const float EerieWindowFactor = 0.5f;

        static uint Mix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU;
            x ^= x >> 15; x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Deterministic hash in [0, 1).</summary>
        public static float Hash(int seed, int a, int b, int c)
        {
            uint h = Mix((uint)seed * 0x9E3779B1U + 0x632BE5ABU);
            h = Mix(h ^ ((uint)a * 0x85EBCA77U));
            h = Mix(h ^ ((uint)b * 0xC2B2AE3DU));
            h = Mix(h ^ ((uint)c * 0x27D4EB2FU));
            return (h >> 8) * (1f / 16777216f);
        }

        /// <summary>Is window <paramref name="index"/> lit at <paramref name="time"/> (seconds &gt;= 0)?</summary>
        public static bool WindowLit(int seed, int index, float time, float onRatio)
        {
            if (onRatio >= 1f) return !Dropped(seed, index, time, 97f);
            float period = Mathf.Lerp(MinPeriod, MaxPeriod, Hash(seed, index, 1, 0));
            float t = Mathf.Max(0f, time) + Hash(seed, index, 2, 0) * period;
            int k = Mathf.FloorToInt(t / period);
            float u = t - k * period;
            bool on = Hash(seed, index, 3, k) < onRatio;
            if (!on) return false;
            bool wasOn = Hash(seed, index, 3, k - 1) < onRatio;
            if (!wasOn && Hash(seed, index, 4, k) < StartFlickerChance && u < StartFlickerSeconds) return StartFlicker(u);
            if (Hash(seed, index, 5, k) < DropChance)
            {
                float at = period * (0.25f + 0.5f * Hash(seed, index, 6, k));
                if (u >= at && u < at + DropSeconds) return false;
            }
            return true;
        }

        /// <summary>Always-on lamps: a single brief drop-out now and then (period ~ <paramref name="period"/> s).</summary>
        static bool Dropped(int seed, int index, float time, float period)
        {
            float t = Mathf.Max(0f, time) + Hash(seed, index, 7, 0) * period;
            int k = Mathf.FloorToInt(t / period);
            float u = t - k * period;
            if (Hash(seed, index, 8, k) >= 0.5f) return false;
            float at = period * (0.2f + 0.6f * Hash(seed, index, 9, k));
            return u >= at && u < at + DropSeconds;
        }

        public const float StartFlickerSeconds = 0.8f;

        /// <summary>Fluorescent start, 0.8 s: blip on, off, on (3 changes, never more than 3 in any second).</summary>
        public static bool StartFlicker(float u) => u < 0.2f || u >= StartFlickerSeconds;

        /// <summary>
        /// HOTEL sign letter brightness (1 = lit, <see cref="SignDim"/> = out). Every ~11 s slot there may be one event on
        /// one letter: a short stutter, sometimes followed by a few seconds dark.
        /// </summary>
        public static float SignLetterLevel(int seed, int letter, int letterCount, float time, bool eerie = false)
        {
            if (letterCount <= 0) return 1f;
            // eerie (Friday the 13th): an event in every ~6 s slot instead of 60% of ~11 s slots - same gentle pattern
            float p = eerie ? EerieSignPeriod : SignEventPeriod;
            float tt = Mathf.Max(0f, time);
            int k = Mathf.FloorToInt(tt / p);
            float u = tt - k * p;
            if (!eerie && Hash(seed, 70, k, 0) >= SignEventChance) return 1f;
            int target = Mathf.Min(letterCount - 1, (int)(Hash(seed, 70, k, 1) * letterCount));
            if (target != letter) return 1f;
            float start = 1f + Hash(seed, 70, k, 2) * (p - 6f);
            float r = u - start;
            if (r < 0f) return 1f;
            bool longOut = Hash(seed, 70, k, 3) < 0.3f;
            // out, back, out again (and maybe stays out): transitions at 0, 0.3, 0.95, 1.3 -> at most 3 in any second
            if (r < 0.30f) return SignDim;
            if (r < 0.95f) return 1f;
            if (r < 1.30f) return SignDim;
            if (longOut && r < 4.3f) return SignDim;
            return 1f;
        }

        /// <summary>Marquee bulbs: two interleaved sets swap every <see cref="BulbStep"/> s (gentle chase).</summary>
        public static int BulbPhase(float time) => Mathf.FloorToInt(Mathf.Max(0f, time) / BulbStep) & 1;

        /// <summary>Someone watching TV: the blue window changes its tint (the picture cuts) every <see cref="TvStep"/> s.</summary>
        public const float TvStep = 0.45f;
        /// <summary>Seconds of the soft blend from one TV tint to the next (never a hard cut).</summary>
        public const float TvBlend = 0.15f;

        /// <summary>
        /// Tints multiplied onto the TV window's blue: blue, teal, lavender, pale - a change of HUE at almost the same
        /// luminance (within ~0.06 of each other on the window's blue), so the picture lives without ever flashing.
        /// </summary>
        public static readonly Color[] TvTints =
        {
            new Color(1f, 1f, 1f), new Color(0.75f, 1.17f, 0.90f), new Color(1.24f, 0.94f, 1.00f), new Color(1.24f, 1.14f, 0.93f),
        };

        static Color TvTarget(int seed, int index, int k) => TvTints[Mathf.Min(TvTints.Length - 1, (int)(Hash(seed, index, 40, k) * TvTints.Length))];

        /// <summary>
        /// TV tint of window <paramref name="index"/>: a new hashed tint every <see cref="TvStep"/> s, blended over
        /// <see cref="TvBlend"/> s (pure, seeded; PhotosensitivityTests).
        /// </summary>
        public static Color TvTint(int seed, int index, float time)
        {
            float t = Mathf.Max(0f, time) / TvStep + Hash(seed, index, 41, 0);
            int k = Mathf.FloorToInt(t);
            float u = (t - k) * TvStep / TvBlend;
            return Color.Lerp(TvTarget(seed, index, k - 1), TvTarget(seed, index, k), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u)));
        }

        /// <summary>Rooftop aircraft beacon: on 0.9 s every 2.4 s.</summary>
        public static bool BeaconLit(float time) => Mathf.Repeat(Mathf.Max(0f, time), 2.4f) < 0.9f;
    }

    /// <summary>
    /// Runtime side: every registered light owns one dynamic palette texel; Tick evaluates the schedule and rewrites
    /// texels that changed (the atlas uploads at most once per frame).
    /// </summary>
    sealed class LivingLights
    {
        /// <summary>Tv = a window (same on/off schedule) whose blue light changes tint while lit (<see cref="LightSchedule.TvTint"/>).</summary>
        public enum Kind { Window, Lamp, BulbA, BulbB, Beacon, Tv }

        struct Light
        {
            public int Texel;
            public Color On, Off;
            public float OnRatio;
            public Kind Kind;
            public bool Lit;
        }

        readonly PaletteAtlas atlas;
        readonly int seed;
        readonly List<Light> lights = new List<Light>();
        bool first = true;

        public LivingLights(PaletteAtlas atlas, int seed)
        {
            this.atlas = atlas;
            this.seed = seed;
        }

        public int Seed => seed;
        public int Count => lights.Count;
        public int WindowCount { get; private set; }
        /// <summary>Windows with a TV on (a subset of <see cref="WindowCount"/>).</summary>
        public int TvCount { get; private set; }
        /// <summary>Texel changes applied by Tick so far.</summary>
        public int Changes { get; private set; }

        public int Add(Kind kind, Color on, Color off, float onRatio = 1f)
        {
            int texel = atlas.Dynamic(on);
            lights.Add(new Light { Texel = texel, On = on, Off = off, OnRatio = onRatio, Kind = kind, Lit = true });
            if (kind == Kind.Window || kind == Kind.Tv) WindowCount++;
            if (kind == Kind.Tv) TvCount++;
            return texel;
        }

        public bool IsLit(int i) => lights[i].Lit;
        public Kind KindOf(int i) => lights[i].Kind;
        public int TexelOf(int i) => lights[i].Texel;

        public int LitWindows
        {
            get
            {
                int n = 0;
                foreach (var l in lights) if ((l.Kind == Kind.Window || l.Kind == Kind.Tv) && l.Lit) n++;
                return n;
            }
        }

        bool Evaluate(int i, in Light l, float time)
        {
            switch (l.Kind)
            {
                case Kind.BulbA: return LightSchedule.BulbPhase(time) == 0;
                case Kind.BulbB: return LightSchedule.BulbPhase(time) == 1;
                case Kind.Beacon: return LightSchedule.BeaconLit(time);
                case Kind.Window:
                case Kind.Tv: return LightSchedule.WindowLit(seed, i, time, Eerie ? l.OnRatio * LightSchedule.EerieWindowFactor : l.OnRatio);
                default: return LightSchedule.WindowLit(seed, i, time, l.OnRatio);
            }
        }

        /// <summary>Friday the 13th mood: half as many windows lit (changes roll in over the windows' own schedules).</summary>
        public bool Eerie { get; set; }

        public void Tick(float time)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                bool lit = Evaluate(i, l, time);
                // a TV wavers while it is on (Set skips texels whose colour did not change)
                if (l.Kind == Kind.Tv && lit && l.Lit && !first)
                {
                    atlas.Set(l.Texel, l.On * LightSchedule.TvTint(seed, i, time));
                    continue;
                }
                if (lit == l.Lit && !first) continue;
                if (lit != l.Lit) Changes++;
                l.Lit = lit;
                lights[i] = l;
                atlas.Set(l.Texel, lit ? (l.Kind == Kind.Tv ? l.On * LightSchedule.TvTint(seed, i, time) : l.On) : l.Off);
            }
            first = false;
        }
    }
}
