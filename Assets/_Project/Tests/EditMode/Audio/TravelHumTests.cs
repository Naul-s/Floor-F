using System.IO;
using FloorF.Audio;
using FloorF.Core;
using FloorF.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>
    /// Elevator travel hum (procedural "wing-wing" loop on one dedicated voice) and the star-night wish cues:
    /// the generated clips, their import settings, the library rows and the pure fade/pitch rules.
    /// </summary>
    public class TravelHumTests
    {
        static readonly TravelHumSettings S = new TravelHumSettings(0.15f, 0.2f, 0.93f, 0.5f, 0.95f, 0.4f);

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

        [Test]
        public void HumFile_IsASeamlessMonoLoop()
        {
            var d = ReadMono(ProceduralSfxSynth.HumPath, out int rate);
            Assert.AreEqual(ProceduralSfxSynth.Rate, rate);
            Assert.AreEqual(ProceduralSfxSynth.HumSeconds, d.Length / (float)rate, 1e-4f, "3.2 s");
            // every component is periodic in the clip length: the wrap-around step is an ordinary step
            float maxStep = 0f;
            double sum = 0;
            for (int i = 0; i + 1 < d.Length; i++) { maxStep = Mathf.Max(maxStep, Mathf.Abs(d[i + 1] - d[i])); sum += d[i]; }
            Assert.AreEqual(0f, d[0], 1e-4f, "starts on a zero crossing");
            Assert.LessOrEqual(Mathf.Abs(d[0] - d[d.Length - 1]), maxStep * 1.02f + 1e-4f, "loop seam = no click");
            Assert.AreEqual(d[1] - d[0], d[0] - d[d.Length - 1], maxStep * 0.05f, "slope continues across the seam");
            Assert.AreEqual(0.0, sum / d.Length, 1e-3, "no DC");
            // the generator is what is on disk (up to 16-bit rounding)
            var gen = ProceduralSfxSynth.Hum();
            Assert.AreEqual(gen.Length, d.Length);
            for (int i = 0; i < d.Length; i += 101) Assert.AreEqual(gen[i], d[i], 1f / 16000f, "sample " + i);
        }

        [Test]
        public void Hum_WingsAt2_5Hz_WithAWeeAccentEveryOtherWing()
        {
            var d = ProceduralSfxSynth.Hum();
            int rate = ProceduralSfxSynth.Rate;
            int win = rate / 20; // 50 ms
            float Rms(int start)
            {
                double s = 0;
                for (int i = start; i < start + win; i++) s += d[i] * d[i];
                return 20f * Mathf.Log10((float)System.Math.Sqrt(s / win));
            }
            int wing = Mathf.RoundToInt(rate / ProceduralSfxSynth.HumWingHz); // 0.4 s
            // wing peaks at 0, 0.4, 0.8 ... s, troughs halfway
            float peakA = Rms(0), trough = Rms(wing / 2 - win / 2), peakB = Rms(wing - win / 2);
            Assert.Greater(peakA - trough, 4f, "clear wing wobble (" + peakA + " vs " + trough + ")");
            Assert.Less(peakA - trough, 9f, "gentle, not a pulse");
            Assert.Greater(peakA - Rms(wing), 0.5f, "every other wing is the accented 'wee'");
            Assert.AreEqual(Rms(0), Rms(2 * wing), 0.05f, "wee-wing pair repeats every 0.8 s");
            Assert.That(peakB, Is.LessThan(peakA));
            // clean: no sample-to-sample jump beyond what a 880 Hz top partial can make (no noise / grit)
            float peak = 0f, maxStep = 0f;
            for (int i = 0; i + 1 < d.Length; i++) { peak = Mathf.Max(peak, Mathf.Abs(d[i])); maxStep = Mathf.Max(maxStep, Mathf.Abs(d[i + 1] - d[i])); }
            Assert.AreEqual(AudioRules.DbToLinear(ProceduralSfxSynth.PeakDb), peak, 1e-3f, "peak -3 dBFS");
            Assert.Less(maxStep, peak * 0.1f, "band-limited (max step " + maxStep + ")");
        }

        [Test]
        public void GeneratedClips_ImportLikeTheOtherSfx()
        {
            foreach (var path in new[] { ProceduralSfxSynth.HumPath, ProceduralSfxSynth.WishStarPath, ProceduralSfxSynth.WishChimePath, ProceduralSfxSynth.StarSparklePath })
            {
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.IsNotNull(ai, path + ": imported as audio (not a stub-meta mis-import)");
                var s = ai.defaultSampleSettings;
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, path);
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, s.loadType, path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.IsNotNull(clip, path);
                Assert.AreEqual(1, clip.channels, path + " mono");
            }
        }

        [Test]
        public void WishClips_StartAndEndSilent()
        {
            foreach (var path in new[] { ProceduralSfxSynth.WishStarPath, ProceduralSfxSynth.WishChimePath, ProceduralSfxSynth.StarSparklePath })
            {
                var d = ReadMono(path, out _);
                Assert.AreEqual(0f, d[0], 2e-3f, path + " first sample");
                Assert.AreEqual(0f, d[d.Length - 1], 1e-4f, path + " last sample (faded)");
            }
        }

        [Test]
        public void Library_HumRowAndWishCues()
        {
            var lib = LoadLibrary();
            Assert.IsNotNull(lib.travelHum, "travel hum clip");
            Assert.AreEqual("elevator_hum_loop", lib.travelHum.name);
            Assert.AreEqual(0.15f, lib.travelFadeInSeconds, 1e-4f);
            Assert.AreEqual(0.2f, lib.travelFadeOutSeconds, 1e-4f);
            Assert.That(lib.travelHumVolume, Is.InRange(0.05f, 0.25f), "ambience level (file rms -10.8 dBFS)");
            Assert.Less(lib.travelPitchStart, 1f, "spins up");
            Assert.Less(lib.travelPitchArrive, 1f, "dips on braking");
            // played below the ding and the door clunk (file levels: hum max100ms -8.1, ding -1.3, clunk imported -10.7)
            float hum = -8.1f + 20f * Mathf.Log10(lib.travelHumVolume);
            Assert.Less(hum, -1.3f + 20f * Mathf.Log10(lib.Find(AudioCue.Ding).volume) - 5f, "hum under the ding");
            Assert.Less(hum, -10.7f + 20f * Mathf.Log10(lib.Find(AudioCue.DoorsOpen).volume) - 5f, "hum under the doors");

            Assert.AreEqual("wish_star_twinkle", lib.Find(AudioCue.WishStar).clips[0].name);
            Assert.AreEqual("wish_chime", lib.Find(AudioCue.Wish).clips[0].name);
            Assert.AreEqual("star_sparkle", lib.Find(AudioCue.StarSparkle).clips[0].name);
            Assert.Greater(lib.Find(AudioCue.Wish).volume, lib.Find(AudioCue.WishStar).volume, "the wish is brighter than the star");
            Assert.Less(lib.Find(AudioCue.StarSparkle).volume, lib.Find(AudioCue.Lucky).volume, "sparkle = a quiet layer");
        }

        [Test]
        public void WishEvents_MapToCues_FadeIsSilent_SparkleOnlyOnStarNightLucky()
        {
            bool star = false, wish = false;
            foreach (var m in AudioEventMap.Mapped)
            {
                star |= m.Event == typeof(WishStarAppeared) && m.Cue == AudioCue.WishStar;
                wish |= m.Event == typeof(WishMade) && m.Cue == AudioCue.Wish;
                Assert.AreNotEqual(typeof(WishStarFaded), m.Event, "a fading star makes no sound");
            }
            Assert.IsTrue(star, "WishStarAppeared -> WishStar");
            Assert.IsTrue(wish, "WishMade -> Wish");
            Assert.AreEqual(AudioCue.StarSparkle, AudioEventMap.StarNightLayer(AudioCue.Lucky, true));
            Assert.AreEqual(AudioCue.None, AudioEventMap.StarNightLayer(AudioCue.Lucky, false), "normal night: no sparkle");
            Assert.AreEqual(AudioCue.None, AudioEventMap.StarNightLayer(AudioCue.Unlucky, true));
            Assert.AreEqual(AudioCue.None, AudioEventMap.StarNightLayer(AudioCue.None, true));
        }

        [Test]
        public void Hum_FadesInOnDeparture_OutAtArrival()
        {
            var h = new TravelHum();
            Assert.IsFalse(h.Active);
            h.Step(0.1f, -1f, true, S);
            Assert.AreEqual(0f, h.Gain, "silent before any departure");

            h.Depart();
            Assert.IsTrue(h.Moving);
            h.Step(0.075f, -1f, true, S);
            Assert.AreEqual(0.5f, h.Gain, 1e-4f, "half way after 0.075 of 0.15 s");
            Assert.IsTrue(h.Active);
            h.Step(0.1f, -1f, true, S);
            Assert.AreEqual(1f, h.Gain, 1e-6f, "full after 0.15 s");

            h.Arrive();
            Assert.IsTrue(h.Active, "still fading out");
            h.Step(0.1f, -1f, true, S);
            Assert.AreEqual(0.5f, h.Gain, 1e-4f, "unknown eta: fades over 0.2 s after arrival");
            h.Step(0.1f, -1f, true, S);
            Assert.AreEqual(0f, h.Gain, 1e-6f);
            Assert.IsFalse(h.Active, "voice can stop");
        }

        [Test]
        public void Hum_PredictedArrival_IsSilentByTheDing()
        {
            var h = new TravelHum();
            h.Depart();
            const float dt = 1f / 60f;
            float eta = 1.8f; // 3 floors at 0.6 s
            while (eta > 0f)
            {
                h.Step(dt, eta, true, S);
                if (eta > 0.25f && h.SinceDepart > 0.16f) Assert.AreEqual(1f, h.Gain, 1e-5f, "full while travelling");
                eta -= dt;
            }
            Assert.LessOrEqual(h.Gain, dt / 0.2f + 1e-4f, "out (within a frame) when the car arrives");
            h.Arrive();
            h.Step(dt, -1f, true, S);
            Assert.AreEqual(0f, h.Gain, 1e-6f);
            Assert.IsFalse(h.Active);

            // retarget farther while fading: comes back up, no second voice needed (same state object)
            h.Depart();
            h.Step(0.2f, 0.1f, true, S);
            Assert.AreEqual(0f, h.Gain, 1e-6f, "1-floor hop already inside the fade window");
            h.Step(0.05f, 1.2f, true, S);
            Assert.Greater(h.Gain, 0f, "new farther target: fades back in");
        }

        [Test]
        public void Hum_RedepartureKeepsItGoing_LockedStaysSilent_KillIsImmediate()
        {
            var h = new TravelHum();
            h.Depart();
            h.Step(0.3f, -1f, true, S);
            float since = h.SinceDepart;
            h.Arrive();
            h.Step(0.05f, -1f, true, S);
            h.Depart(); // departs again while still sounding
            Assert.AreEqual(since, h.SinceDepart, 1e-6f, "no restart of the spin-up");
            h.Step(0.05f, -1f, true, S);
            Assert.AreEqual(1f, h.Gain, 1e-5f, "back up from where it was");

            var locked = new TravelHum();
            locked.Depart();
            locked.Step(0.5f, -1f, false, S);
            Assert.AreEqual(0f, locked.Gain, "WebGL lock: no hum");
            Assert.IsFalse(locked.Active);

            h.Kill();
            Assert.AreEqual(0f, h.Gain);
            Assert.IsFalse(h.Moving);
            Assert.IsFalse(h.Active);
        }

        [Test]
        public void HumPitch_RisesWhileAccelerating_DipsWhileBraking()
        {
            Assert.AreEqual(0.93f, TravelHum.PitchAt(0f, -1f, S), 1e-5f, "departure");
            Assert.AreEqual(1f, TravelHum.PitchAt(0.5f, -1f, S), 1e-5f, "up to speed");
            float prev = 0f;
            for (float t = 0f; t <= 0.5f; t += 0.05f)
            {
                float p = TravelHum.PitchAt(t, -1f, S);
                Assert.GreaterOrEqual(p, prev - 1e-6f, "rising at " + t);
                prev = p;
            }
            Assert.AreEqual(1f, TravelHum.PitchAt(2f, 1f, S), 1e-5f, "cruising");
            Assert.AreEqual(0.95f, TravelHum.PitchAt(2f, 0f, S), 1e-5f, "arrival");
            Assert.Less(TravelHum.PitchAt(2f, 0.1f, S), TravelHum.PitchAt(2f, 0.3f, S), "dipping as it brakes");
            Assert.Less(TravelHum.PitchAt(0.1f, 0.1f, S), 0.95f, "short hop: both effects");
        }
    }
}
