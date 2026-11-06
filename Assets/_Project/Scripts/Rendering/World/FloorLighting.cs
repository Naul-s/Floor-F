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
            public float BaseIntensity;
            public bool Flicker;
            public Color FlashColor;
            public float FlashStrength;
            public float FlashDuration;
            public float FlashElapsed = float.MaxValue;
            public Color Current;
            public float CurrentIntensity01 = 1f;
        }

        readonly List<Entry> floors = new List<Entry>();

        public int FloorCount => floors.Count;

        internal void AddFloor(Material wall, Color wallBase, Color baseColor, float baseIntensity)
        {
            floors.Add(new Entry
            {
                Wall = wall, WallBase = wallBase, BaseColor = baseColor, BaseIntensity = baseIntensity, Current = baseColor,
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

        /// <summary>Burst of <paramref name="color"/> that fades back to the base colour over <paramref name="seconds"/>.</summary>
        public void Flash(FloorIndex floor, Color color, float seconds, float strength = 1f)
        {
            if (!Valid(floor.Value)) return;
            var e = floors[floor.Value];
            e.FlashColor = color;
            e.FlashDuration = Mathf.Max(0.05f, seconds);
            e.FlashStrength = Mathf.Clamp01(strength);
            e.FlashElapsed = 0f;
        }

        public bool IsFlashing(FloorIndex floor) => Valid(floor.Value) && floors[floor.Value].FlashElapsed < floors[floor.Value].FlashDuration;

        public void SetFlicker(FloorIndex floor, bool on)
        {
            if (Valid(floor.Value)) floors[floor.Value].Flicker = on;
        }

        public bool IsFlickering(FloorIndex floor) => Valid(floor.Value) && floors[floor.Value].Flicker;

        /// <summary>
        /// Broken-fluorescent pattern in 0..1: mostly on, short drop-outs, occasional stutter bursts. Deterministic
        /// in time (no RNG state), different per floor.
        /// </summary>
        public static float FlickerValue(float time, int seed)
        {
            float slot = Mathf.Floor(time * 14f);
            float burst = Hash(Mathf.Floor(time * 0.8f) + seed * 17.0f);
            float n = Hash(slot + seed * 31.0f);
            float threshold = burst > 0.7f ? 0.55f : 0.12f; // every few seconds a stuttering burst
            return n < threshold ? 0.12f + 0.2f * n : 1f;
        }

        static float Hash(float x)
        {
            float s = Mathf.Sin(x * 12.9898f + 78.233f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        public void Tick(float time, float dt)
        {
            for (int i = 0; i < floors.Count; i++)
            {
                var e = floors[i];
                float flash = 0f;
                if (e.FlashElapsed < e.FlashDuration)
                {
                    e.FlashElapsed += dt;
                    float k = 1f - Mathf.Clamp01(e.FlashElapsed / e.FlashDuration);
                    flash = k * k * e.FlashStrength;
                }
                e.Current = Color.Lerp(e.BaseColor, e.FlashColor, flash);
                e.CurrentIntensity01 = e.Flicker ? FlickerValue(time, i) : 1f;
                float intensity = e.BaseIntensity * e.CurrentIntensity01 * (1f + 0.6f * flash);

                for (int l = 0; l < e.Lights.Count; l++)
                {
                    var light = e.Lights[l];
                    if (light == null) continue;
                    light.color = e.Current;
                    light.intensity = intensity;
                }
                var tube = e.Current * (0.35f + 0.85f * e.CurrentIntensity01);
                tube.a = 1f;
                for (int t = 0; t < e.Tubes.Count; t++) RenderAssets.SetColor(e.Tubes[t], tube);
                if (e.Wall != null)
                {
                    float tint = 0.25f + 0.6f * flash;
                    var wall = e.WallBase * Color.Lerp(Color.white, e.Current, tint) * (0.75f + 0.25f * e.CurrentIntensity01);
                    wall.a = 1f;
                    RenderAssets.SetColor(e.Wall, wall);
                }
            }
        }
    }
}
