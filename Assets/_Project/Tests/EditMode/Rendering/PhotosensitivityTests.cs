using System;
using System.Collections.Generic;
using FloorF.Core;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>
    /// WCAG 2.3.1 (three flashes or below threshold): every light that flickers, blinks or flashes in the game is
    /// sampled over minutes and must never make more than 3 bright/dark transitions in any 1 s window.
    /// A transition = the signal turning around after moving at least <see cref="Delta"/> (10% of full brightness)
    /// away from its last extreme, so slow hums below that size do not count and every real flash counts twice.
    /// </summary>
    public class PhotosensitivityTests
    {
        const float Delta = 0.1f;
        const int MaxPerSecond = 3;
        const float Dt = 1f / 120f;

        /// <summary>Times at which <paramref name="f"/> reverses by at least <paramref name="delta"/>.</summary>
        internal static List<float> Transitions(Func<float, float> f, float t0, float t1, float dt = Dt, float delta = Delta)
        {
            var list = new List<float>();
            float v0 = f(t0), hi = v0, lo = v0;
            int dir = 0;
            for (float t = t0 + dt; t < t1; t += dt)
            {
                float v = f(t);
                if (dir >= 0)
                {
                    hi = Mathf.Max(hi, v);
                    if (hi - v >= delta) { list.Add(t); dir = -1; lo = v; continue; }
                }
                if (dir <= 0)
                {
                    lo = Mathf.Min(lo, v);
                    if (v - lo >= delta) { list.Add(t); dir = 1; hi = v; }
                }
            }
            return list;
        }

        /// <summary>Fails when 4 transitions fall inside less than a second. Returns the transition count.</summary>
        internal static int AssertAtMostThreePerSecond(List<float> times, string what)
        {
            for (int i = 0; i + MaxPerSecond < times.Count; i++)
            {
                float span = times[i + MaxPerSecond] - times[i];
                Assert.GreaterOrEqual(span, 1f - 1e-4f,
                    what + ": " + (MaxPerSecond + 1) + " transitions within " + span.ToString("0.000") + " s (at " +
                    string.Join(", ", times.GetRange(i, MaxPerSecond + 1).ConvertAll(x => x.ToString("0.000"))) + " s)");
            }
            return times.Count;
        }

        static int Check(Func<float, float> f, float seconds, string what) => AssertAtMostThreePerSecond(Transitions(f, 0f, seconds), what);

        static float B(bool on) => on ? 1f : 0f;

        // ------------------------------------------------------------------ pure schedules

        [Test]
        public void FloorTubeFlicker_EverySeed_AtMostThreeTransitionsPerSecond_AndStillEerie()
        {
            int total = 0;
            for (int seed = 0; seed < 16; seed++)
                total += Check(t => FloorLighting.FlickerValue(t, seed), 600f, "FlickerValue seed " + seed);
            total += Check(t => FloorLighting.FlickerValue(t, FloorLighting.WiringSeed), 1200f, "FlickerValue wiring seed");
            // still alive: roughly one drop-out or sag every few seconds
            Assert.Greater(total, 16 * 600 / 10, "the broken tube still misbehaves");
        }

        [Test]
        public void EndingSigns_AtMostThreeTransitionsPerSecond()
        {
            Check(EndingDirector.HauntedSignLevel, 600f, "haunted HOTEL sign");
            Check(t => B(EndingDirector.ProsperousSignLit(t)), 120f, "prosperous HOTEL sign blink");
        }

        [Test]
        public void JuiceSparks_Twinkle_AtMostThreeTransitionsPerSecond()
        {
            for (int i = 0; i < 72; i++)
            {
                int index = i;
                Check(age => B(JuiceFx.Twinkle(age, index)), 10f, "spark " + i + " twinkle");
            }
        }

        [Test]
        public void CityWindows_Lamps_Sign_Bulbs_Beacon_AtMostThreeTransitionsPerSecond([Values(false, true)] bool friday13)
        {
            const int seed = 1408;
            float ratio = 0.36f * (friday13 ? LightSchedule.EerieWindowFactor : 1f);
            for (int i = 0; i < 150; i++)
            {
                int index = i;
                Check(t => B(LightSchedule.WindowLit(seed, index, t, ratio)), 900f, "window " + i);
                Check(t => B(LightSchedule.WindowLit(seed, index, t, 1f)), 600f, "lamp " + i);
            }
            for (int l = 0; l < 5; l++)
            {
                int letter = l;
                Check(t => LightSchedule.SignLetterLevel(seed, letter, 5, t, friday13), 900f, "HOTEL letter " + l);
            }
            // the sign as a whole (letters are next to each other)
            Check(t =>
            {
                float sum = 0f;
                for (int l = 0; l < 5; l++) sum += LightSchedule.SignLetterLevel(seed, l, 5, t, friday13);
                return sum / 5f;
            }, 900f, "HOTEL sign");
            Check(t => B(LightSchedule.BulbPhase(t) == 0), 60f, "marquee bulbs");
            Check(t => B(LightSchedule.BeaconLit(t)), 60f, "beacon");
            Check(t => B(LightSchedule.StartFlicker(t)), 3f, "fluorescent start");
        }

        [Test]
        public void NightSky_Twinkles_Glints_Meteors_WishStars_AtMostThreeTransitionsPerSecond([Values(1408, 3)] int seed)
        {
            for (int g = 0; g < StarField.TwinkleGroups; g++)
            {
                int group = g;
                Check(t => SkySchedule.TwinkleLevel(seed, group, t), 600f, "twinkle group " + g);
            }
            // every glint star alone, and all glints together (one sky)
            for (int i = 0; i < StarField.GlintCount; i++)
            {
                int index = i;
                Check(t => SkySchedule.GlintLevel(seed, StarField.GlintCount, index, t), 900f, "glint " + i);
            }
            int glints = Check(t => SkySchedule.Glint(seed, StarField.GlintCount, t, out _), 900f, "all glints");
            Assert.Greater(glints, 100, "glints do happen");
            int meteors = Check(t => SkySchedule.MeteorBrightness(seed, t), 900f, "meteor shower");
            Assert.Greater(meteors, 100, "meteors do happen");
            // wish star head: fade in, cross, fade out (wished on early / late)
            foreach (float end in new[] { 0.1f, 0.5f, 3f })
                Check(t => SkySchedule.WishHeadLevel(t, t >= end ? end : -1f), 6f, "wish head ending at " + end);
            Check(t => SkySchedule.WishPulse(t) ? 1f : 0.5f, 20f, "wish head pulse");
            Check(SkySchedule.SparkleLevel, 2f, "tail sparkle");
            for (int i = 0; i < SkySchedule.BurstCount; i++)
            {
                int index = i;
                Check(t => SkySchedule.BurstLevel(t, index), 2f, "wish burst spark " + i);
            }
        }

        // ------------------------------------------------------------------ event-driven floor lights

        static FloorLighting Make(int floors, Color baseColor)
        {
            var l = new FloorLighting();
            for (int i = 0; i < floors; i++) l.AddFloor(null, Color.grey, baseColor, 1f);
            return l;
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>
        /// Drives a lighting rig for <paramref name="seconds"/> at 60 fps, calling <paramref name="events"/> each frame,
        /// and records per floor the lit luminance (light intensity x colour luminance) and the tube luminance.
        /// </summary>
        static void Simulate(FloorLighting l, float seconds, Action<float> events, out List<float>[] intensity, out List<float>[] tube)
        {
            const float dt = 1f / 60f;
            intensity = new List<float>[l.FloorCount];
            tube = new List<float>[l.FloorCount];
            for (int i = 0; i < l.FloorCount; i++) { intensity[i] = new List<float>(); tube[i] = new List<float>(); }
            float time = 0f;
            for (int frame = 0; frame * dt < seconds; frame++)
            {
                events(time);
                l.Tick(time, dt);
                for (int i = 0; i < l.FloorCount; i++)
                {
                    var f = new FloorIndex(i);
                    // what the lit room shows: light intensity x luminance of its colour (a red flash darkens a white room)
                    intensity[i].Add(l.GetLightIntensity(f) * Lum(l.GetCurrentColor(f)));
                    tube[i].Add(Lum(l.GetTubeColor(f)));
                }
                time += dt;
            }
        }

        static void CheckSeries(List<float> samples, float scale, string what)
        {
            const float dt = 1f / 60f;
            Check(t => samples[Mathf.Clamp(Mathf.RoundToInt(t / dt), 0, samples.Count - 1)] / scale, samples.Count * dt, what);
        }

        [Test]
        public void SpammedFlashes_OnAFlickeringFloor_AtMostThreeTransitionsPerSecond([Values(false, true)] bool flicker, [Values(13, 5)] int seed)
        {
            var l = Make(1, Color.white);
            var f = new FloorIndex(0);
            l.SetFlicker(f, flicker);
            var rng = new System.Random(seed);
            float next = 0f;
            var colors = new[] { RenderingStyle.UnluckyFlash, RenderingStyle.LuckyFlash, Color.white };
            int flashes = 0;
            Simulate(l, 300f, time =>
            {
                if (time < next) return;
                // door ding + verdict in one frame, or a lone flash; next request 0.05-0.8 s later (worst case spam)
                if (rng.NextDouble() < 0.5) l.Flash(f, Color.white, 0.35f, FloorLighting.MinorFlashStrength);
                l.Flash(f, colors[rng.Next(colors.Length)], RenderingStyle.UnluckyFlashSeconds, (float)(0.4 + 0.6 * rng.NextDouble()));
                flashes++;
                next = time + 0.05f + 0.75f * (float)rng.NextDouble();
            }, out var intensity, out var tube);
            Assert.Greater(flashes, 300);
            CheckSeries(intensity[0], 1f, "floor light (flicker " + flicker + ")");
            CheckSeries(tube[0], 1f, "tube (flicker " + flicker + ")");
        }

        [Test]
        public void UnluckyFlash_StillFlashesRed_AfterTheDoorDing()
        {
            var l = Make(1, Color.white);
            var f = new FloorIndex(0);
            l.Flash(f, Color.white, 0.35f, FloorLighting.MinorFlashStrength);
            l.Flash(f, RenderingStyle.UnluckyFlash, RenderingStyle.UnluckyFlashSeconds);
            l.Tick(0f, 1f / 60f);
            var c = l.GetCurrentColor(f);
            Assert.Greater(c.r, 0.9f);
            Assert.Less(c.g, 0.2f, "same-frame flashes merge, the strongest (red) wins");
            Assert.Greater(l.GetLightIntensity(f), 1.5f, "full-strength peak");

            // a ding a moment later never blocks a later verdict flash (it is below the flash threshold)
            var g = Make(1, Color.white);
            g.Flash(f, Color.white, 0.35f, FloorLighting.MinorFlashStrength);
            for (int i = 0; i < 18; i++) g.Tick(i / 60f, 1f / 60f);
            g.Flash(f, RenderingStyle.UnluckyFlash, RenderingStyle.UnluckyFlashSeconds);
            g.Tick(0.3f, 1f / 60f);
            Assert.Less(g.GetCurrentColor(f).g, 0.2f, "red after the ding");
            // a second verdict inside MinFlashGap does not re-rise
            float before = g.GetLightIntensity(f);
            for (int i = 0; i < 12; i++) g.Tick(0.3f + i / 60f, 1f / 60f);
            float faded = g.GetLightIntensity(f);
            Assert.Less(faded, before);
            g.Flash(f, RenderingStyle.LuckyFlash, 0.6f, 1f);
            g.Tick(0.5f, 1f / 60f);
            Assert.LessOrEqual(g.GetLightIntensity(f), faded + 1e-3f, "no new peak within the gap");
        }

        [Test]
        public void Endings_AcrossTheBuilding_AtMostThreeTransitionsPerSecond()
        {
            const int n = 9;
            // haunted: every floor red, flickering, one flash, level easing down
            var haunted = Make(n, Color.white);
            for (int i = 0; i < n; i++)
            {
                var f = new FloorIndex(i);
                haunted.SetBaseColor(f, EndingDirector.HauntedLight);
                haunted.SetFlicker(f, true);
                haunted.SetLevel(f, 0.85f, 2f);
            }
            haunted.FlashAll(EndingDirector.HauntedLight, 0.8f);
            Simulate(haunted, 300f, _ => { }, out var hi, out var ht);
            CheckAreas(hi, "haunted lights");
            CheckAreas(ht, "haunted tubes");

            // prosperous: bottom-up gold wave (0.15 s per floor), level 0.5 -> 1.3
            var gold = Make(n, Color.white);
            int wave = 0;
            for (int i = 0; i < n; i++) gold.SetLevel(new FloorIndex(i), 0.5f, 0f);
            Simulate(gold, 20f, time =>
            {
                while (wave < n && time >= 0.15f * wave)
                {
                    var f = new FloorIndex(wave);
                    gold.SetLevel(f, 1.3f, 3f);
                    gold.Flash(f, RenderingStyle.LuckyFlash, 0.6f, 0.8f);
                    wave++;
                }
            }, out var gi, out var gt);
            CheckAreas(gi, "prosperous wave lights");
            CheckAreas(gt, "prosperous wave tubes");
        }

        [Test]
        public void CursePulse_UnluckyStreak_AcrossTheBuilding_AtMostThreeTransitionsPerSecond([Values(6, 21, 77)] int seed)
        {
            const int n = 9;
            // "666": unlucky flash on one floor + a red pulse on every floor, repeated by an unlucky streak;
            // two neighbouring F floors flicker (shared wiring)
            var curse = Make(n, Color.white);
            curse.SetFlicker(new FloorIndex(3), true);
            curse.SetFlicker(new FloorIndex(4), true);
            var rng = new System.Random(seed);
            float next = 0f;
            Simulate(curse, 300f, time =>
            {
                if (time < next) return;
                var at = new FloorIndex(rng.Next(n));
                // same order as RenderingRoot: door ding, then the pulse, then the judged floor's own flash
                curse.Flash(at, Color.white, 0.35f, FloorLighting.MinorFlashStrength);
                curse.FlashAll(RenderingStyle.UnluckyFlash, RenderingStyle.CurseFlashSeconds, RenderingStyle.CurseFlashStrength);
                curse.Flash(at, RenderingStyle.UnluckyFlash, RenderingStyle.UnluckyFlashSeconds);
                next = time + 0.1f + 0.9f * (float)rng.NextDouble();
            }, out var ci, out var ct);
            CheckAreas(ci, "666 pulse lights");
            CheckAreas(ct, "666 pulse tubes");
        }

        /// <summary>Each floor alone and every 3 adjacent floors together (about one 10-degree field of view).</summary>
        static void CheckAreas(List<float>[] floors, string what)
        {
            float scale = 0f;
            foreach (var s in floors) foreach (var v in s) scale = Mathf.Max(scale, v);
            scale = Mathf.Max(scale, 1e-3f);
            for (int i = 0; i < floors.Length; i++) CheckSeries(floors[i], scale, what + " floor " + i);
            for (int i = 0; i + 2 < floors.Length; i++)
            {
                var avg = new List<float>(floors[i].Count);
                for (int k = 0; k < floors[i].Count; k++) avg.Add((floors[i][k] + floors[i + 1][k] + floors[i + 2][k]) / 3f);
                CheckSeries(avg, scale, what + " floors " + i + "-" + (i + 2));
            }
        }
    }
}
