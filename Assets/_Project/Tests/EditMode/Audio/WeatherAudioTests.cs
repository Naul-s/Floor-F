using System.Collections.Generic;
using System.IO;
using System.Linq;
using FloorF.Audio;
using FloorF.Core;
using FloorF.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>
    /// Rainy-day ambience (procedural rain loop + distant thunder on a seeded schedule) and the shop cues (reroll riffle,
    /// charm bell, shield chime, sticker peel): generated files, library rows, levels and the pure rules.
    /// </summary>
    public class WeatherAudioTests
    {
        static readonly ThunderSettings S = new ThunderSettings(8f, 20f, 1f, 3f);

        static AudioLibrary LoadLibrary()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath + " (run FloorF.EditorTools.AudioLibraryBuilder.Build)");
            return lib;
        }

        static float[] ReadMono(string assetPath, out int rate)
        {
            Assert.IsTrue(DoorClipTrimmer.TryReadWav(File.ReadAllBytes(Path.GetFullPath(assetPath)),
                out var data, out int ch, out rate, out string err), assetPath + ": " + err);
            Assert.AreEqual(1, ch, assetPath + " mono");
            return data;
        }

        static float Db(double linear) => 20f * Mathf.Log10((float)System.Math.Max(linear, 1e-9));

        static double Rms(float[] d, int start, int len)
        {
            double s = 0;
            for (int i = start; i < start + len; i++) s += d[i] * (double)d[i];
            return System.Math.Sqrt(s / len);
        }

        /// <summary>rms of the first difference / rms: ~1.4 for white noise, small for dark (low-passed) sound.</summary>
        static double Brightness(float[] d)
        {
            double s = 0, ds = 0;
            for (int i = 1; i < d.Length; i++) { s += d[i] * (double)d[i]; double x = d[i] - d[i - 1]; ds += x * x; }
            return System.Math.Sqrt(ds / s);
        }

        static void AssertImported(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            Assert.IsNotNull(ai, path + ": imported as audio (not a stub-meta mis-import)");
            Assert.AreEqual(AudioCompressionFormat.Vorbis, ai.defaultSampleSettings.compressionFormat, path);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, ai.defaultSampleSettings.loadType, path);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AudioClip>(path), path);
        }

        // ------------------------------------------------------------------ rain file

        [Test]
        public void RainFile_IsASeamlessSteadySoftLoop()
        {
            var d = ReadMono(ProceduralSfxSynth.RainPath, out int rate);
            Assert.AreEqual(ProceduralSfxSynth.Rate, rate);
            Assert.AreEqual(ProceduralSfxSynth.RainSeconds, d.Length / (float)rate, 1e-4f, "8 s loop");
            AssertImported(ProceduralSfxSynth.RainPath);

            // seam: the wrap-around step is an ordinary step of the noise (99.9th percentile), not a click
            var steps = new List<float>(d.Length);
            for (int i = 0; i + 1 < d.Length; i++) steps.Add(Mathf.Abs(d[i + 1] - d[i]));
            steps.Sort();
            float p999 = steps[(int)(steps.Count * 0.999)];
            Assert.LessOrEqual(Mathf.Abs(d[0] - d[d.Length - 1]), p999, "loop seam = no click");
            // the windows right around the seam are as loud as the rest (equal-power crossfade, no dip / bump)
            double all = Rms(d, 0, d.Length);
            int w = rate / 4;
            Assert.AreEqual(Db(all), Db(Rms(d, 0, w)), 2.5f, "head level");
            Assert.AreEqual(Db(all), Db(Rms(d, d.Length - w, w)), 2.5f, "tail level");
            // steady bed: every second within +-2 dB (the slow swell is +-10 %)
            for (int s = 0; s < 8; s++)
                Assert.AreEqual(Db(all), Db(Rms(d, s * rate, rate)), 2f, "second " + s);
            Assert.AreEqual(ProceduralSfxSynth.RainRmsDb, Db(all), 1f, "bed level");
            double mean = d.Average(x => (double)x);
            Assert.AreEqual(0.0, mean, 2e-3, "no DC");
            float peak = d.Max(x => Mathf.Abs(x));
            Assert.LessOrEqual(Db(peak), -1f, "headroom");
            Assert.Less(Brightness(d), 0.6, "low-passed: a soft 'shhh', no harsh hiss (white noise = 1.4)");

            var gen = ProceduralSfxSynth.Rain();
            Assert.AreEqual(gen.Length, d.Length, "generator is what is on disk");
            for (int i = 0; i < d.Length; i += 997) Assert.AreEqual(gen[i], d[i], 1f / 16000f, "sample " + i);
        }

        [Test]
        public void ThunderFiles_AreSoftLowRumbles_ShorterThanTheirSpacing()
        {
            var lib = LoadLibrary();
            float spacing = WeatherRules.MinStrikeSpacing(ThunderSettings.From(lib));
            var def = lib.Find(AudioCue.Thunder);
            for (int v = 0; v < ProceduralSfxSynth.ThunderVariants; v++)
            {
                string path = ProceduralSfxSynth.ThunderPath(v);
                AssertImported(path);
                var d = ReadMono(path, out int rate);
                float len = d.Length / (float)rate;
                Assert.Less(len / (def.pitch * (1f - def.pitchJitter)), spacing, path + ": over before the next rumble can come");
                Assert.AreEqual(0f, d[0], 1e-4f, path + " starts silent");
                Assert.AreEqual(0f, d[d.Length - 1], 1e-4f, path + " ends silent");
                int win = rate / 10;
                double loudest = 0; int loudestAt = 0;
                for (int i = 0; i + win <= d.Length; i += win / 2)
                {
                    double r = Rms(d, i, win);
                    if (r > loudest) { loudest = r; loudestAt = i; }
                }
                Assert.Less(Db(Rms(d, 0, win)), Db(loudest) - 12f, path + ": first 100 ms far under the peak (no crack)");
                Assert.Greater(loudestAt / (float)rate, 0.2f, path + ": swells in slowly");
                Assert.Less(Brightness(d), 0.08, path + ": a low rumble");
            }
            // three different rumbles
            var a = ProceduralSfxSynth.Thunder(0);
            var b = ProceduralSfxSynth.Thunder(1);
            Assert.AreNotEqual(a.Length, b.Length);
        }

        [Test]
        public void Library_RainAndThunder_RowsAndLevels()
        {
            var lib = LoadLibrary();
            Assert.IsNotNull(lib.rainLoop, "rain clip");
            Assert.AreEqual("rain_loop", lib.rainLoop.name);
            Assert.AreEqual(AudioLibraryBuilder.RainVolume, lib.rainVolume, 1e-4f);
            Assert.That(lib.rainFadeInSeconds, Is.InRange(1.5f, 5f), "fades in with the day");
            Assert.That(lib.rainFadeOutSeconds, Is.InRange(1.5f, 5f));
            Assert.AreEqual(8f, lib.thunderMinGap, 1e-4f, "lightning every ~8-20 s");
            Assert.AreEqual(20f, lib.thunderMaxGap, 1e-4f);
            Assert.AreEqual(1f, lib.thunderDelayMin, 1e-4f, "rumble 1-3 s after the implied flash");
            Assert.AreEqual(3f, lib.thunderDelayMax, 1e-4f);
            var t = lib.Find(AudioCue.Thunder);
            Assert.AreEqual(ProceduralSfxSynth.ThunderVariants, t.clips.Length, "thunder variants");
            Assert.IsTrue(t.clips.All(c => c != null && c.name.StartsWith("thunder_rumble_")));

            // levels as played (source file x volumes): rain well under the main music, thunder under the ding
            Assert.IsTrue(AudioLevelProbe.TryMeasure(ProceduralSfxSynth.RainPath, out var rain));
            var music = lib.Find(MusicTrack.Elevator);
            Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(music.clip), out var song));
            float rainRms = rain.RmsDb + Db(lib.rainVolume);
            float songRms = song.RmsDb + Db(lib.musicVolume * music.volume);
            Assert.Less(rainRms, songRms - 5f, "rain is a gentle bed under the music");
            Assert.Greater(rainRms, songRms - 12f, "... but still heard");
            var ding = lib.Find(AudioCue.Ding);
            Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(ding.clips[0]), out var dingL));
            foreach (var c in t.clips)
            {
                Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(c), out var th));
                Assert.Less(th.Max100msDb + Db(t.volume), dingL.Max100msDb + Db(ding.volume) - 3f, c.name + ": soft, under the ding");
            }
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void Rain_OnlyOnRainyDays_WhileThatDayIsOnScreen()
        {
            foreach (DayPhase p in System.Enum.GetValues(typeof(DayPhase)))
            {
                bool expect = p == DayPhase.Intro || p == DayPhase.Running || p == DayPhase.Summary;
                Assert.AreEqual(expect, WeatherRules.RainAudible(true, p), "rainy day, " + p);
                Assert.IsFalse(WeatherRules.RainAudible(false, p), "dry day, " + p);
            }
        }

        [Test]
        public void RainBed_FadesInAndOut()
        {
            var r = new RainBed();
            Assert.IsFalse(r.Active);
            r.Step(1.5f, true, 3f, 2.5f);
            Assert.AreEqual(0.5f, r.Gain, 1e-4f, "half way in after 1.5 of 3 s");
            Assert.IsTrue(r.Active);
            r.Step(5f, true, 3f, 2.5f);
            Assert.AreEqual(1f, r.Gain, 1e-4f);
            r.Step(1.25f, false, 3f, 2.5f);
            Assert.AreEqual(0.5f, r.Gain, 1e-4f, "half way out after 1.25 of 2.5 s");
            Assert.IsTrue(r.Active, "still fading out");
            r.Step(2f, false, 3f, 2.5f);
            Assert.AreEqual(0f, r.Gain);
            Assert.IsFalse(r.Active, "silent: voice may stop");
        }

        static List<float> Strikes(int seed, float dt, float seconds, in ThunderSettings s)
        {
            var sch = new ThunderSchedule();
            sch.Reset(seed, s);
            var times = new List<float>();
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++)
                if (sch.Step(dt, s)) times.Add(sch.Clock);
            return times;
        }

        [Test]
        public void ThunderSchedule_IsSeededPerDay_AndDeterministic()
        {
            int day5 = ThunderSchedule.SeedFor(5);
            Assert.AreEqual(day5, ThunderSchedule.SeedFor(5));
            Assert.AreNotEqual(day5, ThunderSchedule.SeedFor(4));
            var a = Strikes(day5, 1f / 60f, 300f, S);
            var b = Strikes(day5, 1f / 60f, 300f, S);
            CollectionAssert.AreEqual(a, b, "same day, same rumbles");
            var c = Strikes(day5, 1f / 30f, 300f, S);
            Assert.AreEqual(a.Count, c.Count, "frame rate does not change the schedule");
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], c[i], 1f / 30f + 1e-3f, "strike " + i);
            var other = Strikes(ThunderSchedule.SeedFor(6), 1f / 60f, 300f, S);
            Assert.AreNotEqual(a.Take(3).ToArray(), other.Take(3).ToArray(), "another day rumbles at other times");
        }

        [Test]
        public void ThunderSchedule_SpacingFollowsTheFlashGapsAndDelays()
        {
            float minSpacing = WeatherRules.MinStrikeSpacing(S);
            Assert.AreEqual(6f, minSpacing, 1e-4f, "8 s gap - (3 - 1) s delay spread");
            float maxSpacing = S.MaxGap + (S.DelayMax - S.DelayMin);
            int total = 0;
            for (int day = 1; day <= 12; day++)
            {
                var t = Strikes(ThunderSchedule.SeedFor(day), 1f / 60f, 600f, S);
                Assert.That(t[0], Is.InRange(S.MinGap + S.DelayMin - 0.02f, S.MaxGap + S.DelayMax + 0.02f), "first rumble");
                for (int i = 1; i < t.Count; i++)
                {
                    float gap = t[i] - t[i - 1];
                    Assert.GreaterOrEqual(gap, minSpacing - 0.02f, "day " + day + " rumble " + i + ": never crowded");
                    Assert.LessOrEqual(gap, maxSpacing + 0.02f, "day " + day + " rumble " + i + ": never a long silence");
                }
                // ~ 600 s / 14 s mean gap
                Assert.That(t.Count, Is.InRange(30, 60), "day " + day);
                total += t.Count;
            }
            Assert.Greater(total, 0);
        }

        [Test]
        public void ThunderSchedule_LongJump_GivesOneRumble_NotABurst()
        {
            var sch = new ThunderSchedule();
            Assert.IsFalse(sch.Step(100f, S), "not armed: silent");
            sch.Reset(ThunderSchedule.SeedFor(5), S);
            Assert.IsTrue(sch.Step(500f, S), "a rumble is due");
            Assert.IsFalse(sch.Step(0f, S), "only one per step");
            Assert.GreaterOrEqual(sch.NextStrikeIn, S.MinGap - S.DelayMax - 1e-3f, "the next one is a normal gap away");
            sch.Disarm();
            Assert.IsFalse(sch.Armed);
            Assert.IsFalse(sch.Step(100f, S));
        }

        // ------------------------------------------------------------------ shop cues

        [Test]
        public void ShopEvents_MapToTheirCues()
        {
            var map = AudioEventMap.Mapped;
            Assert.IsTrue(map.Any(m => m.Event == typeof(ShopRerolled) && m.Cue == AudioCue.ShopReroll));
            Assert.IsTrue(map.Any(m => m.Event == typeof(CharmBought) && m.Cue == AudioCue.CharmBought));
            Assert.IsTrue(map.Any(m => m.Event == typeof(CharmTriggered) && m.Cue == AudioCue.CharmShield));
            Assert.IsTrue(map.Any(m => m.Event == typeof(StickersExpired) && m.Cue == AudioCue.StickersPeel));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new StickersExpired(0, 0)), "nothing expired: silent");
            Assert.AreEqual(AudioCue.StickersPeel, AudioEventMap.For(new StickersExpired(2, 0)));
            Assert.AreEqual(AudioCue.StickersPeel, AudioEventMap.For(new StickersExpired(0, 1)));
            // a reroll / charm spend is a TipsChanged decrease: no coin sound on top
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new TipsChanged(70, -30)));
        }

        [Test]
        public void ShopCues_Library_UsesTheRiffleAndTheProceduralClips()
        {
            var lib = LoadLibrary();
            Assert.AreEqual("card-shuffle", lib.Find(AudioCue.ShopReroll).clips[0].name, "reroll = card riffle");
            Assert.AreEqual("charm_bell", lib.Find(AudioCue.CharmBought).clips[0].name);
            Assert.AreEqual("charm_shield", lib.Find(AudioCue.CharmShield).clips[0].name);
            Assert.AreEqual("sticker_peel", lib.Find(AudioCue.StickersPeel).clips[0].name);
            Assert.Less(lib.Find(AudioCue.StickersPeel).volume, lib.Find(AudioCue.CharmBought).volume, "the peel is quiet");
            foreach (var path in new[] { ProceduralSfxSynth.CharmBellPath, ProceduralSfxSynth.ShieldChimePath, ProceduralSfxSynth.StickerPeelPath })
            {
                AssertImported(path);
                var d = ReadMono(path, out int rate);
                Assert.AreEqual(0f, d[0], 1e-3f, path + ": no click at the start");
                Assert.AreEqual(0f, d[d.Length - 1], 1e-4f, path + ": ends silent");
                Assert.AreEqual(-3f, Db(d.Max(x => Mathf.Abs(x))), 0.2f, path + " peak");
            }
            Assert.Less(ReadMono(ProceduralSfxSynth.StickerPeelPath, out int r).Length / (float)r, 0.5f, "a short peel");
            // played levels: the bell and shield sit at or under the ding, the peel far under
            var ding = lib.Find(AudioCue.Ding);
            Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(ding.clips[0]), out var dl));
            float dingDb = dl.Max100msDb + Db(ding.volume);
            float Played(AudioCue cue)
            {
                var def = lib.Find(cue);
                Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(def.clips[0]), out var l), cue.ToString());
                return l.Max100msDb + Db(def.volume);
            }
            Assert.LessOrEqual(Played(AudioCue.CharmBought), dingDb, "soft bell");
            Assert.LessOrEqual(Played(AudioCue.CharmShield), dingDb + 0.5f, "shield");
            Assert.Less(Played(AudioCue.StickersPeel), dingDb - 8f, "quiet peel");
        }
    }
}
