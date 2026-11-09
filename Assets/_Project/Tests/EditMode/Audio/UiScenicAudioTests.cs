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
    /// UI feedback cues (Tap gold tink, Reveal shimmer, the tink layered on the button click), the scenic view
    /// (ScenicViewChanged: SFX lowered, music kept, establishing-shot whoosh) and the Tap spam guard.
    /// </summary>
    public class UiScenicAudioTests
    {
        static AudioLibrary LoadLibrary()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath + " (run FloorF.EditorTools.AudioLibraryBuilder.Build)");
            return lib;
        }

        static float[] ReadMono(string assetPath)
        {
            Assert.IsTrue(DoorClipTrimmer.TryReadWav(File.ReadAllBytes(Path.GetFullPath(assetPath)),
                out var data, out int ch, out int rate, out string err), assetPath + ": " + err);
            Assert.AreEqual(1, ch, assetPath + " mono");
            Assert.AreEqual(ProceduralSfxSynth.Rate, rate, assetPath);
            return data;
        }

        static double Rms(float[] d, int start, int len)
        {
            double s = 0;
            for (int i = start; i < start + len; i++) s += d[i] * (double)d[i];
            return System.Math.Sqrt(s / System.Math.Max(1, len));
        }

        static float Db(double linear) => 20f * Mathf.Log10((float)System.Math.Max(linear, 1e-9));

        static int Samples(float seconds) => Mathf.RoundToInt(seconds * ProceduralSfxSynth.Rate);

        static readonly TapSettings Taps = new TapSettings(0.07f, 0.35f, 0.8f, 0.45f, 0.08f);

        // ------------------------------------------------------------------ mapping

        [Test]
        public void UiSoundKinds_MapToTheirCues()
        {
            Assert.AreEqual(AudioCue.UiClick, AudioEventMap.For(UiSoundKind.Click));
            Assert.AreEqual(AudioCue.DayStart, AudioEventMap.For(UiSoundKind.Confirm), "confirm reuses the day-start chime");
            Assert.AreEqual(AudioCue.Error, AudioEventMap.For(UiSoundKind.Error));
            Assert.AreEqual(AudioCue.Tap, AudioEventMap.For(UiSoundKind.Tap));
            Assert.AreEqual(AudioCue.Reveal, AudioEventMap.For(UiSoundKind.Reveal));
            Assert.AreEqual(AudioCue.UiClick, AudioEventMap.For((UiSoundKind)99), "unknown kind -> plain click");
            foreach (var cue in new[] { AudioCue.UiClick, AudioCue.DayStart, AudioCue.Error, AudioCue.Tap, AudioCue.Reveal })
                Assert.IsTrue(AudioEventMap.Mapped.Any(m => m.Event == typeof(UiSound) && m.Cue == cue), cue + " listed for the table test");
        }

        [Test]
        public void ScenicView_OnlyTheEstablishingZoomInWhooshes()
        {
            Assert.AreEqual(AudioCue.ScenicWhoosh, AudioEventMap.For(new ScenicViewChanged(false, true)), "establishing shot zooms back in");
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new ScenicViewChanged(true, true)), "establishing shot starts: silent");
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new ScenicViewChanged(true, false)), "player's scenic on: the button clicks");
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new ScenicViewChanged(false, false)), "player's scenic off: silent");
            Assert.IsTrue(AudioEventMap.Mapped.Any(m => m.Event == typeof(ScenicViewChanged) && m.Cue == AudioCue.ScenicWhoosh));
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void ScenicDuck_PlayerScenicKeepsTheMusicThoughPaused()
        {
            Assert.AreEqual(1f, AudioRules.DuckTarget(true, false, true, -12f, -6f, 0f), 1e-4f, "scenic + paused: music kept");
            Assert.AreEqual(0.251f, AudioRules.DuckTarget(true, false, false, -12f, -6f, 0f), 0.002f, "plain pause still -12 dB");
            Assert.AreEqual(0.501f, AudioRules.DuckTarget(false, true, false, -12f, -6f, 0f), 0.002f, "hold -6 dB");
            Assert.AreEqual(1f, AudioRules.DuckTarget(false, false, false, -12f, -6f, 0f), 1e-4f);
            Assert.AreEqual(AudioRules.DbToLinear(-2f), AudioRules.DuckTarget(true, false, true, -12f, -6f, -2f), 1e-4f, "scenic level tunable");
            var lib = LoadLibrary();
            Assert.AreEqual(0f, lib.scenicMusicDb, 1e-4f, "music not ducked during the player's scenic view");
            Assert.That(lib.scenicSfxDb, Is.InRange(-6f, -2f), "SFX a little lower, not muted");
        }

        [Test]
        public void SfxScale_TitleScalesGameSoundsOnly_ScenicLowersEverything()
        {
            const float title = 0.35f, scenicDb = -4f;
            float scen = AudioRules.DbToLinear(scenicDb);
            Assert.AreEqual(1f, AudioRules.SfxScale(false, false, false, title, scenicDb), 1e-5f);
            Assert.AreEqual(title, AudioRules.SfxScale(true, false, false, title, scenicDb), 1e-5f, "title demo game sounds");
            Assert.AreEqual(1f, AudioRules.SfxScale(true, true, false, title, scenicDb), 1e-5f, "UI on the title = same as in game");
            Assert.AreEqual(scen, AudioRules.SfxScale(false, false, true, title, scenicDb), 1e-5f, "scenic: game sounds lower");
            Assert.AreEqual(scen, AudioRules.SfxScale(false, true, true, title, scenicDb), 1e-5f, "scenic: taps lower too");
        }

        [Test]
        public void TapGuard_DropsSpam_BurstsOnlyGetSofter_QuietGapRestores()
        {
            var g = new TapGuard();
            Assert.AreEqual(1f, g.TryTap(10f, Taps), 1e-5f, "first tap full");
            Assert.AreEqual(0f, g.TryTap(10f, Taps), "same frame -> dropped");
            Assert.AreEqual(0f, g.TryTap(10.05f, Taps), "inside the min interval -> dropped");

            // a 10 Hz click burst: every tap plays, never louder than the one before, floor reached
            float prev = 1f, t = 10f;
            for (int i = 0; i < 20; i++)
            {
                t += 0.1f;
                float gain = g.TryTap(t, Taps);
                Assert.Greater(gain, 0f, "10 Hz taps all play");
                Assert.LessOrEqual(gain, prev + 1e-6f, "a burst never gets louder");
                Assert.GreaterOrEqual(gain, Taps.RepeatFloor - 1e-6f, "never below the floor");
                prev = gain;
            }
            Assert.AreEqual(Taps.RepeatFloor, prev, 1e-5f, "long burst sits at the floor");

            // mashing at 100 Hz: at most one tap per min interval
            int played = 0;
            for (int i = 0; i < 100; i++) if (g.TryTap(t + 1f + i * 0.01f, Taps) > 0f) played++;
            Assert.LessOrEqual(played, Mathf.CeilToInt(1f / Taps.MinInterval) + 1, "100 clicks/s -> ~14 taps/s at most");

            // a quiet moment restores the full tink
            Assert.AreEqual(1f, g.TryTap(t + 5f, Taps), 1e-5f, "after a pause: full gain");

            // a button click carries the tink itself: a tap in the same gesture is dropped
            g.NoteClick(20f);
            Assert.AreEqual(0f, g.TryTap(20f, Taps), "tap in the same frame as a click");
            Assert.AreEqual(0f, g.TryTap(20.05f, Taps), "tap right after a click");
            Assert.Greater(g.TryTap(20.2f, Taps), 0f, "a later tap plays");
            g.Clear();
            Assert.AreEqual(1f, g.TryTap(20.21f, Taps), 1e-5f, "Clear resets");
        }

        /// <summary>Sum of overlapping tap levels during constant clicking stays at or under one lone full tap's level x 2.</summary>
        [Test]
        public void TapGuard_ConstantClicking_NeverStacksLouderThanTwoTaps()
        {
            var lib = LoadLibrary();
            var def = lib.Find(AudioCue.Tap);
            var s = TapSettings.From(lib, def);
            var g = new TapGuard();
            var starts = new System.Collections.Generic.List<(float t, float gain)>();
            for (int i = 0; i < 300; i++)
            {
                float t = i * 0.016f; // a click every frame at 60 fps
                float gain = g.TryTap(t, s);
                if (gain > 0f) starts.Add((t, gain));
            }
            float audible = ProceduralSfxSynth.TinkAudibleSeconds;
            float worst = 0f;
            for (float t = 0f; t < 4.8f; t += 0.005f)
            {
                float sum = 0f;
                foreach (var st in starts) if (t >= st.t && t < st.t + audible) sum += st.gain;
                worst = Mathf.Max(worst, sum);
            }
            Assert.LessOrEqual(worst, 2f, "overlapping tinks (linear gain sum) never exceed two full taps");
            Assert.LessOrEqual(starts.Count, 4.8f / s.MinInterval + 1f, "rate capped by the min interval");
        }

        // ------------------------------------------------------------------ table rows + levels

        [Test]
        public void UiRows_TapIsQuietUnderTheClick_ClickCarriesTheTink()
        {
            var lib = LoadLibrary();
            var tap = lib.Find(AudioCue.Tap);
            var click = lib.Find(AudioCue.UiClick);
            var reveal = lib.Find(AudioCue.Reveal);
            var whoosh = lib.Find(AudioCue.ScenicWhoosh);
            foreach (var d in new[] { tap, click, reveal, whoosh }) Assert.IsNotNull(d);

            var tink = AssetDatabase.LoadAssetAtPath<AudioClip>(ProceduralSfxSynth.GoldTinkPath);
            Assert.IsNotNull(tink, "gold tink imported");
            CollectionAssert.AreEqual(new[] { tink }, tap.clips, "Tap = the gold tink");
            Assert.AreSame(tink, click.layer, "button click carries the same tink (one family)");
            Assert.Less(click.layerVolume, 0.5f, "lightly");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AudioClip>(ProceduralSfxSynth.MenuRevealPath), reveal.clips[0]);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AudioClip>(ProceduralSfxSynth.ScenicWhooshPath), whoosh.clips[0]);

            Assert.That(tap.pitchJitter, Is.InRange(AudioRules.MinJitter, 0.07f), "small random pitch");
            Assert.GreaterOrEqual(tap.minInterval, 0.05f, "strict spam interval");
            Assert.AreEqual(AudioLibraryBuilder.TapMinInterval, tap.minInterval, 1e-5f);
            Assert.That(lib.tapRepeatStep, Is.InRange(0.5f, 0.95f), "repeats get softer");
            Assert.That(lib.tapRepeatFloor, Is.InRange(0.3f, 0.7f));
            Assert.Greater(lib.tapAfterClickSeconds, 0f);
            Assert.GreaterOrEqual(whoosh.minInterval, 0.5f, "one whoosh per zoom");
        }

        [Test]
        public void UiLevels_TapUnderClick_RevealUnderDing_WhooshSubtle()
        {
            var lib = LoadLibrary();
            float Played(AudioClip clip, float gain)
            {
                Assert.IsTrue(AudioLevelProbe.TryMeasure(AssetDatabase.GetAssetPath(clip), out var l), clip.name);
                return l.Max100msDb + 20f * Mathf.Log10(gain);
            }
            var click = lib.Find(AudioCue.UiClick);
            float clickDb = Played(click.clips[0], click.volume);
            float tapDb = Played(lib.Find(AudioCue.Tap).clips[0], lib.Find(AudioCue.Tap).volume);
            float layerDb = Played(click.layer, click.volume * click.layerVolume);
            var ding = lib.Find(AudioCue.Ding);
            float dingDb = Played(ding.clips[0], ding.volume);
            var rev = lib.Find(AudioCue.Reveal);
            float revealDb = Played(rev.clips[0], rev.volume);
            var wh = lib.Find(AudioCue.ScenicWhoosh);
            float whooshDb = Played(wh.clips[0], wh.volume);
            Debug.LogFormat("[UiScenicAudioTests] played max100ms: click {0:0.0} tap {1:0.0} click-tink {2:0.0} reveal {3:0.0} whoosh {4:0.0} ding {5:0.0}",
                clickDb, tapDb, layerDb, revealDb, whooshDb, dingDb);
            Assert.LessOrEqual(tapDb, clickDb, "a tap is quieter than a button click");
            Assert.Less(layerDb, clickDb, "the tink sits under the click's snap");
            Assert.Less(revealDb, dingDb - 3f, "reveal: not too loud");
            Assert.Greater(revealDb, -30f, "reveal still clearly heard");
            Assert.That(whooshDb, Is.InRange(-36f, -27f), "whoosh: subtle");
        }

        [Test]
        public void EveryLibraryClip_IsLongEnoughForWebGL()
        {
            var lib = LoadLibrary();
            foreach (var c in lib.cues)
            {
                foreach (var clip in c.clips) Assert.GreaterOrEqual(clip.length, AudioLibraryBuilder.MinClipSeconds - 1e-3f, c.cue + " " + clip.name);
                if (c.layer != null) Assert.GreaterOrEqual(c.layer.length, AudioLibraryBuilder.MinClipSeconds - 1e-3f, c.cue + " layer " + c.layer.name);
            }
        }

        // ------------------------------------------------------------------ generated files

        static void AssertImported(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            Assert.IsNotNull(ai, path + ": imported as audio (not a stub-meta mis-import)");
            Assert.AreEqual(AudioCompressionFormat.Vorbis, ai.defaultSampleSettings.compressionFormat, path);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, ai.defaultSampleSettings.loadType, path);
        }

        [Test]
        public void GoldTink_IsATinyBrightPing_SilentAfter130ms()
        {
            var d = ReadMono(ProceduralSfxSynth.GoldTinkPath);
            AssertImported(ProceduralSfxSynth.GoldTinkPath);
            Assert.AreEqual(ProceduralSfxSynth.UiClipSeconds, d.Length / (float)ProceduralSfxSynth.Rate, 1e-3f, "0.25 s file (WebGL)");
            Assert.AreEqual(-3f, Db(d.Max(v => Mathf.Abs(v))), 0.1f, "peak -3 dBFS");
            int audible = Samples(ProceduralSfxSynth.TinkAudibleSeconds);
            for (int i = audible; i < d.Length; i++) Assert.AreEqual(0f, d[i], 1e-4f, "silent after the ping");
            double head = Rms(d, 0, Samples(0.03f)), tail = Rms(d, Samples(0.09f), Samples(0.03f));
            Assert.Greater(Db(head) - Db(tail), 12f, "a pluck: decays fast");
            // bright: ~2.3 kHz fundamental -> many zero crossings
            int zc = 0, n = Samples(0.05f);
            for (int i = 1; i < n; i++) if ((d[i - 1] < 0f) != (d[i] < 0f)) zc++;
            Assert.Greater(zc / 0.05f / 2f, 1800f, "fundamental above 1.8 kHz");
            Assert.AreEqual(0f, d[0], 0.05f, "soft start (no click)");
        }

        [Test]
        public void MenuReveal_RisesThenBlooms_AndRingsOut()
        {
            var d = ReadMono(ProceduralSfxSynth.MenuRevealPath);
            AssertImported(ProceduralSfxSynth.MenuRevealPath);
            float len = d.Length / (float)ProceduralSfxSynth.Rate;
            Assert.That(len, Is.InRange(0.7f, 0.95f), "~0.8 s");
            double start = Rms(d, 0, Samples(0.06f)), bloom = Rms(d, Samples(0.26f), Samples(0.08f)), end = Rms(d, d.Length - Samples(0.05f), Samples(0.05f));
            Assert.Greater(Db(bloom) - Db(start), 6f, "swells up to the bloom");
            Assert.Greater(Db(bloom) - Db(end), 15f, "rings out");
            Assert.AreEqual(0f, d[d.Length - 1], 1e-4f, "ends at zero");
        }

        [Test]
        public void ScenicWhoosh_SwellsAndCloses_NoTone()
        {
            var d = ReadMono(ProceduralSfxSynth.ScenicWhooshPath);
            AssertImported(ProceduralSfxSynth.ScenicWhooshPath);
            Assert.AreEqual(ProceduralSfxSynth.WhooshSeconds, d.Length / (float)ProceduralSfxSynth.Rate, 1e-3f);
            double early = Rms(d, 0, Samples(0.1f)), peak = Rms(d, Samples(0.55f), Samples(0.1f));
            Assert.Greater(Db(peak) - Db(early), 10f, "swells in (approaching)");
            for (int i = Samples(0.87f); i < d.Length; i++) Assert.AreEqual(0f, d[i], 1e-4f, "closed by the end");
        }
    }
}
