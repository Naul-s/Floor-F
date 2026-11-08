using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Lighting mood hooks, one entry per floor: a point light, the ceiling tube (self-lit) and a tint on the floor's
    /// wallpaper so the mood reads even where point lights blend between floors.
    ///   SetBaseColor  - the floor's resting light colour (per destination by default)
    ///   Flash         - temporary colour burst that eases back (unlucky = red, lucky = gold, door "ding" pulse)
    ///   SetFlicker    - broken fluorescent tube (the "F" floor)
    /// Driven by RenderingRoot from Core events; Tick() once per frame.
    /// </summary>
    sealed class FloorLighting
    {
        sealed class Entry
        {
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<Material> Tubes = new List<Material>();
            public Material Wall;
            public Color WallBase;
            public Color BaseColor;
            public Color DefaultColor;
            public float BaseIntensity;
            /// <summary>Ending moods: light level multiplier (0 = off, 1 = normal, &gt;1 = brighter), eased toward LevelTarget.</summary>
            public float Level = 1f;
            public float LevelTarget = 1f;
            public float LevelSpeed;
            public float LastIntensity;
            public bool Flicker;
            public Color FlashColor;
            public float FlashStrength;
            public float FlashDuration;
            public float FlashElapsed = float.MaxValue;
            /// <summary>Seconds since the last flash onset (rate limit, see MinFlashGap).</summary>
            public float SinceOnset = float.MaxValue;
            /// <summary>Seconds since a visible flicker drop-out / sag started (flashes wait, see FlickerFlashGap).</summary>
            public float SinceFlickerEvent = float.MaxValue;
            public bool InFlickerEvent;
            /// <summary>A flash requested right after a flicker event, fired once the gap has passed.</summary>
            public bool Pending;
            public Color PendingColor;
            public float PendingSeconds, PendingStrength, PendingWait;
            public Color Current;
            public Color Tube;
            public float CurrentIntensity01 = 1f;
        }

        readonly List<Entry> floors = new List<Entry>();
        /// <summary>Time of the last Tick (flicker clock).</summary>
        float clock;

        public int FloorCount => floors.Count;

        internal void AddFloor(Material wall, Color wallBase, Color baseColor, float baseIntensity)
        {
            floors.Add(new Entry
            {
                Wall = wall, WallBase = wallBase, BaseColor = baseColor, DefaultColor = baseColor, BaseIntensity = baseIntensity, Current = baseColor,
            });
        }

        internal void AddLight(int floor, Light light)
        {
            if (Valid(floor) && light != null) floors[floor].Lights.Add(light);
        }

        internal void AddTube(int floor, Material tube)
        {
            if (Valid(floor)) floors[floor].Tubes.Add(tube);
        }

        bool Valid(int floor) => floor >= 0 && floor < floors.Count;

        public void SetBaseColor(FloorIndex floor, Color color)
        {
            if (Valid(floor.Value)) floors[floor.Value].BaseColor = color;
        }

        public Color GetBaseColor(FloorIndex floor) => Valid(floor.Value) ? floors[floor.Value].BaseColor : Color.black;

        /// <summary>Colour of the floor light as of the last Tick (base + flash), before flicker dimming.</summary>
        public Color GetCurrentColor(FloorIndex floor) => Valid(floor.Value) ? floors[floor.Value].Current : Color.black;

        /// <summary>
        /// Burst of <paramref name="color"/> that fades back to the base colour over <paramref name="seconds"/>.
        /// Photosensitivity: flashes requested in the same frame merge into one (the strongest wins); a flash requested
        /// less than <see cref="MinFlashGap"/> after the last onset does not rise again (the running flash fades on);
        /// strengths &lt;= <see cref="MinorFlashStrength"/> are a sub-threshold glow; on a flickering floor a flash waits
        /// until <see cref="FlickerFlashGap"/> after the last drop-out started (and that drop-out is over).
        /// </summary>
        public void Flash(FloorIndex floor, Color color, float seconds, float strength = 1f)
        {
            if (!Valid(floor.Value)) return;
            var e = floors[floor.Value];
            strength = Mathf.Clamp01(strength);
            seconds = Mathf.Max(0.05f, seconds);
            bool active = e.FlashElapsed < e.FlashDuration;
            if (strength <= MinorFlashStrength)
            {
                // below the flash threshold: a soft glow that never blocks (or interrupts) a real flash
                if (active && e.FlashStrength > MinorFlashStrength) return;
                e.FlashColor = color;
                e.FlashDuration = seconds;
                e.FlashStrength = strength;
                e.FlashElapsed = 0f;
                return;
            }
            float wait = RequiredWait(e, active);
            if (e.Pending || wait > 0f)
            {
                // the broken tube just dropped out: the flash waits (at most FlickerFlashGap) so the drop-out and the
                // flash never stack into a strobe; the tube holds still meanwhile
                Defer(e, color, seconds, strength, e.Pending ? e.PendingWait : wait);
                return;
            }
            if (active && e.SinceOnset <= 0f)
            {
                // same frame (e.g. the door "ding" and a luck verdict): one onset, strongest colour
                if (strength >= e.FlashStrength) e.FlashColor = color;
                e.FlashStrength = Mathf.Max(e.FlashStrength, strength);
                e.FlashDuration = Mathf.Max(e.FlashDuration, seconds);
                return;
            }
            if (e.SinceOnset < MinFlashGap)
            {
                // too soon for another flash: no new peak and no colour swap (that would be a flash of its own) -
                // the running flash just fades on over the new duration
                if (!active) return;
                float k = 1f - Mathf.Clamp01(e.FlashElapsed / e.FlashDuration);
                float now = k * k * e.FlashStrength;
                float remaining = e.FlashDuration - e.FlashElapsed;
                e.FlashStrength = now;
                e.FlashDuration = Mathf.Max(remaining, seconds * 0.5f);
                e.FlashElapsed = 0f;
                return;
            }
            e.FlashColor = color;
            e.FlashDuration = seconds;
            e.FlashStrength = strength;
            e.FlashElapsed = 0f;
            e.SinceOnset = 0f;
        }

        /// <summary>
        /// The same flash on every floor at once (the "666" pulse, the haunted ending). If any floor has to wait (a
        /// flicker drop-out or its own flash gap), the whole building waits with it, so neighbouring floors never
        /// flash out of step. Call it BEFORE a same-frame single-floor flash so that one merges into the pulse.
        /// </summary>
        public void FlashAll(Color color, float seconds, float strength = 1f)
        {
            if (strength > MinorFlashStrength)
            {
                // all floors rise together: wait for flickering floors' drop-outs AND for every floor's flash gap
                float wait = 0f;
                foreach (var e in floors)
                {
                    bool active = e.FlashElapsed < e.FlashDuration;
                    wait = Mathf.Max(wait, e.Pending ? e.PendingWait : RequiredWait(e, active));
                    if (!(active && e.SinceOnset <= 0f)) wait = Mathf.Max(wait, MinFlashGap - e.SinceOnset);
                }
                if (wait > 0f)
                {
                    foreach (var e in floors) Defer(e, color, Mathf.Max(0.05f, seconds), Mathf.Clamp01(strength), wait);
                    return;
                }
            }
            for (int i = 0; i < floors.Count; i++) Flash(new FloorIndex(i), color, seconds, strength);
        }

        /// <summary>
        /// Seconds a new flash on this floor has to wait for its flicker event (0 = may fire now): at least
        /// <see cref="FlickerFlashGap"/> after the event started, and never while it is still under way.
        /// </summary>
        float RequiredWait(Entry e, bool active)
        {
            if (!e.Flicker || (active && e.SinceOnset <= 0f)) return 0f;
            float wait = Mathf.Max(0f, FlickerFlashGap - e.SinceFlickerEvent);
            if (e.InFlickerEvent) wait = Mathf.Max(wait, FlickerEventRemaining(clock, WiringSeed) + 0.1f);
            return wait;
        }

        /// <summary>Seconds until the flicker event under way at <paramref name="time"/> is over (0 = none).</summary>
        public static float FlickerEventRemaining(float time, int seed)
        {
            const float step = 0.02f;
            for (float s = 0f; s < FlickerSlotSeconds; s += step)
            {
                float t = Mathf.Max(0f, time + s);
                if (FlickerValue(t, seed) >= FlickerHum(t, seed) - 0.03f) return s;
            }
            return FlickerSlotSeconds;
        }

        static void Defer(Entry e, Color color, float seconds, float strength, float wait)
        {
            if (!e.Pending || strength >= e.PendingStrength) e.PendingColor = color;
            e.PendingStrength = e.Pending ? Mathf.Max(e.PendingStrength, strength) : strength;
            e.PendingSeconds = e.Pending ? Mathf.Max(e.PendingSeconds, seconds) : seconds;
            e.PendingWait = e.Pending ? Mathf.Max(e.PendingWait, wait) : wait;
            e.Pending = true;
        }

        /// <summary>Flashing now, or a flash is waiting out a flicker drop-out (fires within FlickerFlashGap).</summary>
        public bool IsFlashing(FloorIndex floor) => Valid(floor.Value) && (floors[floor.Value].FlashElapsed < floors[floor.Value].FlashDuration || floors[floor.Value].Pending);

        public void SetFlicker(FloorIndex floor, bool on)
        {
            if (Valid(floor.Value)) floors[floor.Value].Flicker = on;
        }

        public bool IsFlickering(FloorIndex floor) => Valid(floor.Value) && floors[floor.Value].Flicker;

        /// <summary>Eases the floor's light level toward <paramref name="target"/> (per second; &lt;= 0 = immediately).</summary>
        public void SetLevel(FloorIndex floor, float target, float speedPerSecond = 0f)
        {
            if (!Valid(floor.Value)) return;
            var e = floors[floor.Value];
            e.LevelTarget = Mathf.Max(0f, target);
            e.LevelSpeed = speedPerSecond;
            if (speedPerSecond <= 0f) e.Level = e.LevelTarget;
        }

        public float GetLevel(FloorIndex floor) => Valid(floor.Value) ? floors[floor.Value].Level : 0f;

        /// <summary>Point-light intensity applied in the last Tick (after level, flicker and flash).</summary>
        public float GetLightIntensity(FloorIndex floor) => Valid(floor.Value) ? floors[floor.Value].LastIntensity : 0f;

        /// <summary>Self-lit ceiling tube colour applied in the last Tick.</summary>
        public Color GetTubeColor(FloorIndex floor) => Valid(floor.Value) ? floors[floor.Value].Tube : Color.black;

        /// <summary>Lit = level up (flicker drop-outs do not count as off).</summary>
        public bool IsLit(FloorIndex floor) => GetLevel(floor) > 0.5f;

        /// <summary>Back to the per-destination colours at full level, no flashes (restart after an ending).</summary>
        public void ResetMood()
        {
            foreach (var e in floors)
            {
                e.BaseColor = e.DefaultColor;
                e.Level = e.LevelTarget = 1f;
                e.LevelSpeed = 0f;
                e.FlashElapsed = float.MaxValue;
                e.Pending = false;
            }
        }

        // ------------------------------------------------------------------ photosensitivity (WCAG 2.3.1)
        // Every light change in the game stays at or below 3 bright<->dark transitions in any second (EditMode-tested
        // by PhotosensitivityTests): the broken tube hums slowly, dims now and then and drops out once at most per
        // slot; flashes on one floor start at most once per MinFlashGap.

        /// <summary>One flicker event (drop-out, stutter-restart or slow dim) at most per slot of this many seconds.</summary>
        public const float FlickerSlotSeconds = 2.5f;
        /// <summary>Share of slots with a sudden drop-out (the rest: some slow dims, most steady).</summary>
        public const float DropChance = 0.45f;
        public const float DimChance = 0.25f;
        /// <summary>Seconds between two flash onsets on one floor; a flash inside the gap recolours without re-rising.</summary>
        public const float MinFlashGap = 1.05f;
        /// <summary>A flash on a flickering floor waits until this long after the last drop-out / sag started.</summary>
        public const float FlickerFlashGap = 1.1f;
        /// <summary>Flashes at or below this strength are a soft glow below the flash threshold (door "ding").</summary>
        public const float MinorFlashStrength = 0.12f;
        /// <summary>All flickering lights share the building's wiring: they drop out together, never out of phase.</summary>
        public const int WiringSeed = 4;

        /// <summary>
        /// Broken-fluorescent pattern in 0..1: a slow hum around ~0.9, and in some <see cref="FlickerSlotSeconds"/> slots
        /// one event - a sudden drop-out (0.12-0.3 s dark), a drop that struggles back up over ~0.7 s, or a slow
        /// dim to ~0.5 and back. Never more than 3 bright/dark transitions in any second. Deterministic in time.
        /// </summary>
        public static float FlickerValue(float time, int seed)
        {
            float t = Mathf.Max(0f, time);
            float hum = FlickerHum(t, seed);
            int k = Mathf.FloorToInt(t / FlickerSlotSeconds);
            float u = t - k * FlickerSlotSeconds;
            float roll = Hash01(seed, k, 0);
            if (roll < DropChance)
            {
                // drop-out somewhere in the first half of the slot (the rest of the slot stays steady)
                float start = 0.15f + 0.85f * Hash01(seed, k, 1);
                float len = 0.12f + 0.18f * Hash01(seed, k, 2);
                float dark = 0.1f + 0.15f * Hash01(seed, k, 3);
                bool struggle = Hash01(seed, k, 4) < 0.35f;
                float r = u - start;
                if (r < 0f) return hum;
                if (r < len) return dark;
                if (!struggle) return hum;
                const float ramp = 0.7f;
                float back = (r - len) / ramp;
                return back >= 1f ? hum : Mathf.Lerp(dark, hum, back * back);
            }
            if (roll < DropChance + DimChance)
            {
                // slow brown-out: smooth dip over ~1.6 s
                float start = 0.2f + 0.5f * Hash01(seed, k, 5);
                float r = (u - start) / 1.6f;
                if (r <= 0f || r >= 1f) return hum;
                float dip = Mathf.Sin(r * Mathf.PI);
                return hum * (1f - 0.45f * dip * dip);
            }
            return hum;
        }

        /// <summary>
        /// The tube's resting hum: ~0.9, peak-to-peak 0.09 (below the 10% flash threshold), 0.2-0.5 Hz - a living
        /// tube, never a flash.
        /// </summary>
        public static float FlickerHum(float t, int seed)
            => 0.9f + 0.045f * (0.6f * Mathf.Sin(t * 1.32f + seed * 1.7f) + 0.4f * Mathf.Sin(t * 3.1f + seed * 2.9f));

        static float Hash01(int seed, int k, int salt) => LightSchedule.Hash(seed, 31, k, salt);

        /// <summary>Tube/wall brightness factor for a flash value (pure; same curve Tick uses).</summary>
        public static float FlashIntensity(float flash) => 1f + 0.6f * flash;

        public void Tick(float time, float dt)
        {
            clock = time;
            for (int i = 0; i < floors.Count; i++)
            {
                var e = floors[i];
                float flash = 0f;
                if (e.SinceOnset < float.MaxValue) e.SinceOnset += dt;
                if (e.SinceFlickerEvent < float.MaxValue) e.SinceFlickerEvent += dt;
                if (e.Pending)
                {
                    e.PendingWait -= dt;
                    if (e.PendingWait <= 0f)
                    {
                        e.Pending = false;
                        e.SinceFlickerEvent = float.MaxValue; // the wait is over: fire even if the tube were to drop now
                        Flash(new FloorIndex(i), e.PendingColor, e.PendingSeconds, e.PendingStrength);
                    }
                }
                bool flashing = e.FlashElapsed < e.FlashDuration;
                if (flashing)
                {
                    e.FlashElapsed += dt;
                    float k = 1f - Mathf.Clamp01(e.FlashElapsed / e.FlashDuration);
                    flash = k * k * e.FlashStrength;
                }
                if (e.Level != e.LevelTarget)
                    e.Level = e.LevelSpeed > 0f ? Mathf.MoveTowards(e.Level, e.LevelTarget, e.LevelSpeed * dt) : e.LevelTarget;
                e.Current = Color.Lerp(e.BaseColor, e.FlashColor, flash);
                if (e.Flicker)
                {
                    // around a flash (while it waits, its onset gap and while it shines) no NEW drop-out or sag may
                    // start - one already under way plays out (snapping it back would be a transition of its own)
                    bool quiet = e.Pending || e.SinceOnset < MinFlashGap || (flashing && e.FlashStrength > MinorFlashStrength);
                    float now = Mathf.Max(0f, time);
                    float hum = FlickerHum(now, WiringSeed);
                    float raw = FlickerValue(now, WiringSeed);
                    bool rawEvent = raw < hum - 0.03f;
                    float fv = raw;
                    if (rawEvent && !e.InFlickerEvent)
                    {
                        if (quiet) fv = hum;            // blocked: the tube keeps humming
                        else e.SinceFlickerEvent = 0f;  // a new event starts
                    }
                    e.InFlickerEvent = rawEvent && (e.InFlickerEvent || !quiet);
                    e.CurrentIntensity01 = fv;
                }
                else
                {
                    e.CurrentIntensity01 = 1f;
                    e.InFlickerEvent = false;
                }
                float level = e.Level;
                float intensity = e.BaseIntensity * level * e.CurrentIntensity01 * FlashIntensity(flash);
                e.LastIntensity = intensity;

                for (int l = 0; l < e.Lights.Count; l++)
                {
                    var light = e.Lights[l];
                    if (light == null) continue;
                    light.color = e.Current;
                    light.intensity = intensity;
                }
                var tube = e.Current * ((0.35f + 0.85f * e.CurrentIntensity01) * Mathf.Lerp(0.06f, 1f, Mathf.Clamp01(level)));
                tube.a = 1f;
                e.Tube = tube;
                for (int t = 0; t < e.Tubes.Count; t++) RenderAssets.SetColor(e.Tubes[t], tube);
                if (e.Wall != null)
                {
                    float tint = 0.25f + 0.6f * flash;
                    var wall = e.WallBase * Color.Lerp(Color.white, e.Current, tint) * ((0.75f + 0.25f * e.CurrentIntensity01) * Mathf.Lerp(0.25f, 1f, Mathf.Min(level, 1.2f)));
                    wall.a = 1f;
                    RenderAssets.SetColor(e.Wall, wall);
                }
            }
        }
    }
}
