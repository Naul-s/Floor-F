using System.Collections.Generic;
using System.Diagnostics;
using FloorF.Audio;
using FloorF.Core;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>
    /// Winter season sounds (procedural, WinterSynth / WinterBank): generated clips, levels as played against the
    /// imported SFX, seamless loops, the elevator-song grid of the Christmas bell layer, and the pure rules.
    /// </summary>
    public class WinterAudioTests
    {
        static readonly AudioCue[] WinterCues =
            { AudioCue.SleighBells, AudioCue.CarolChime, AudioCue.LotteryDraw, AudioCue.LotteryJackpot, AudioCue.WinterStart };

        /// <summary>
        /// The elevator song as played (AudioLevelProbe, AudioLibraryBuilder: max100ms -27.2 dBFS at musicVolume x track
        /// volume). Its clip is Compressed In Memory, so GetData cannot read it here.
        /// </summary>
        const float ElevatorPlayedDb = -27.2f;

        static AudioLibrary LoadLibrary()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath);
            return lib;
        }

        static float[] Samples(AudioClip clip)
        {
            Assert.IsNotNull(clip);
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var d = new float[clip.samples * clip.channels];
            Assert.IsTrue(clip.GetData(d, 0), clip.name + " samples");
            return d;
        }

        /// <summary>The clip's level as played at <paramref name="volume"/> (max100ms dBFS; channels interleaved = same yardstick for mono).</summary>
        static WinterSynth.Levels Played(AudioClip clip, float volume)
        {
            var d = Samples(clip);
            for (int i = 0; i < d.Length; i++) d[i] *= volume;
            return WinterSynth.Measure(d, clip.frequency * clip.channels);
        }

        /// <summary>The wrap-around step is an ordinary step of the clip (99.9th percentile), not a click.</summary>
        static void AssertSeamless(float[] d, string what)
        {
            var steps = new List<float>(d.Length);
            for (int i = 0; i + 1 < d.Length; i++) steps.Add(Mathf.Abs(d[i + 1] - d[i]));
            steps.Sort();
            float p999 = steps[(int)(steps.Count * 0.999)];
            Assert.LessOrEqual(Mathf.Abs(d[0] - d[d.Length - 1]), Mathf.Max(p999, 1e-4f), what + ": loop seam = no click");
        }

        [Test]
        public void WinterRows_SynthesizeOnce_MonoClips_NotInTheAsset()
        {
            var lib = LoadLibrary();
            var sw = Stopwatch.StartNew();
            while (WinterBank.PrewarmOne()) { }
            Debug.Log("[WinterAudioTests] all winter clips synthesized in " + sw.ElapsedMilliseconds + " ms (editor, cached after)");
            Assert.AreEqual(0, WinterBank.Pending);
            foreach (var cue in WinterCues)
            {
                Assert.IsNull(lib.Find(cue), cue + ": not an asset row (procedural)");
                Assert.IsTrue(WinterBank.Has(cue), cue.ToString());
                var def = lib.FindAny(cue);
                Assert.IsNotNull(def, cue.ToString());
                Assert.AreEqual(cue, def.cue);
                Assert.AreEqual(1, def.clips.Length);
                var clip = def.clips[0];
                Assert.IsNotNull(clip, cue + " clip");
                Assert.AreEqual(1, clip.channels, cue + " mono");
                Assert.That(clip.length, Is.InRange(0.8f, 3f), cue + " short");
                Assert.That(def.volume, Is.InRange(0.01f, 1f), cue + " volume");
                Assert.That(def.pitchJitter, Is.InRange(AudioRules.MinJitter, AudioRules.MaxJitter), cue + " jitter");
                Assert.AreSame(def, lib.FindAny(cue), cue + ": cached, not re-synthesized");
                Assert.AreSame(clip, WinterBank.Find(cue).clips[0]);
            }
            foreach (var t in new[] { MusicTrack.WinterBells, MusicTrack.MusicBox })
            {
                Assert.IsNull(lib.Find(t), t + " not in the asset");
                var def = lib.FindAny(t);
                Assert.IsNotNull(def?.clip, t.ToString());
                Assert.AreEqual(0f, def.seamFade, t + ": seamless by construction");
                Assert.AreSame(def, lib.FindAny(t));
            }
            Assert.AreEqual(0.45f, lib.musicVolume, 1e-4f, "track volumes assume the 0.45 music bus");
            Assert.AreEqual(WinterBank.AssumedMusicBus, lib.musicVolume, 1e-4f);
            Assert.IsNotNull(WinterBank.WindLoop);
            Assert.IsNotNull(WinterBank.ChristmasBells);
            Assert.IsFalse(WinterBank.Has(AudioCue.Tip), "imported cues stay in the asset");
        }

        [Test]
        public void WinterCues_PlayAtTheirTargetLevels_NoClipping()
        {
            var lib = LoadLibrary();
            var targets = new Dictionary<AudioCue, float>
            {
                { AudioCue.SleighBells, WinterBank.SleighDb }, { AudioCue.CarolChime, WinterBank.CarolDb },
                { AudioCue.LotteryDraw, WinterBank.LotteryDrawDb }, { AudioCue.LotteryJackpot, WinterBank.JackpotDb },
                { AudioCue.WinterStart, WinterBank.WinterStartDb },
            };
            foreach (var kv in targets)
            {
                var def = lib.FindAny(kv.Key);
                var l = Played(def.clips[0], def.volume);
                Debug.LogFormat("[WinterAudioTests] {0} vol {1:0.000}: {2} (file {3})", kv.Key, def.volume, l, WinterBank.FileLevels(def.clips[0]));
                Assert.AreEqual(kv.Value, l.Max100msDb, 0.75f, kv.Key + " plays at its level");
                Assert.Less(l.PeakDb, -6f, kv.Key + " peak headroom (layered with other cues)");
            }
            // the band: none louder than the ding (-17.8 as played), none buried under the music (-27.2)
            foreach (var v in targets.Values) Assert.That(v, Is.InRange(-27f, -17.5f));
        }

        [Test]
        public void Jackpot_FlashierThanATip_ButShort()
        {
            var lib = LoadLibrary();
            var tip = lib.Find(AudioCue.Tip);
            var jack = lib.FindAny(AudioCue.LotteryJackpot);
            float tipDb = float.MinValue;
            foreach (var c in tip.clips) tipDb = Mathf.Max(tipDb, Played(c, tip.volume).Max100msDb);
            var j = Played(jack.clips[0], jack.volume);
            Debug.LogFormat("[WinterAudioTests] tip max100ms {0:0.0} vs jackpot {1}", tipDb, j);
            Assert.Greater(j.Max100msDb, tipDb, "louder than the loudest tip variant");
            float tipLen = 0f;
            foreach (var c in tip.clips) tipLen = Mathf.Max(tipLen, c.length);
            Assert.Greater(jack.clips[0].length, tipLen, "a longer shower than the chip stack");
            Assert.LessOrEqual(jack.clips[0].length, 2f, "not too long");
            Assert.GreaterOrEqual(jack.minInterval, 0.5f, "a party winning together = one shower");
        }

        [Test]
        public void Wind_SeamlessSoftLoop_UnderTheRain()
        {
            var lib = LoadLibrary();
            var clip = WinterBank.WindLoop;
            Assert.AreEqual(WinterSynth.LowRate, clip.frequency);
            Assert.AreEqual(WinterSynth.WindSeconds, clip.length, 0.01f, "10 s loop");
            var d = Samples(clip);
            AssertSeamless(d, "wind");
            var played = Played(clip, WinterBank.WindVolume);
            Debug.Log("[WinterAudioTests] wind at full snow: " + played);
            Assert.AreEqual(WinterBank.WindRmsDb, played.RmsDb, 0.5f, "rms as played");
            var rain = WinterSynth.Measure(Samples(lib.rainLoop), lib.rainLoop.frequency);
            float rainPlayed = rain.RmsDb + 20f * Mathf.Log10(lib.rainVolume);
            Assert.Less(played.RmsDb, rainPlayed, "softer than the rain bed");
            Assert.Less(played.PeakDb, -20f, "no gust spikes");
            // soft: dark spectrum (rms of the first difference / rms; white noise ~1.4 - and at 22.05 kHz a given
            // spectrum reads twice as bright as at 44.1 kHz, the rain bed's rate)
            double s = 0, ds = 0;
            for (int i = 1; i < d.Length; i++) { s += d[i] * (double)d[i]; double x = d[i] - d[i - 1]; ds += x * x; }
            Assert.Less(System.Math.Sqrt(ds / s), 0.3, "dark, no hiss");
            // gusts move but never drop out: every second within 12 dB of the loudest
            int sec = clip.frequency;
            float hi = float.MinValue, lo = float.MaxValue;
            for (int k = 0; k + sec <= d.Length; k += sec)
            {
                double e = 0;
                for (int i = k; i < k + sec; i++) e += d[i] * (double)d[i];
                float db = WinterSynth.Db(System.Math.Sqrt(e / sec));
                hi = Mathf.Max(hi, db); lo = Mathf.Min(lo, db);
            }
            Assert.That(hi - lo, Is.InRange(1.5f, 12f), "gusting, steady bed");
        }

        [Test]
        public void ChristmasBells_OneBarOfTheElevatorSong_Seamless_UnderTheMusic()
        {
            var lib = LoadLibrary();
            var song = lib.Find(MusicTrack.Elevator).clip;
            Assert.AreEqual(1192563, song.samples, "the grid is measured on this exact loop (BlondPanda 659889)");
            Assert.AreEqual(WinterSynth.ElevatorLoopSeconds, song.length, 1e-3f);
            var bells = WinterBank.ChristmasBells;
            Assert.AreEqual(song.length / WinterSynth.ElevatorBars, bells.length, 1e-3f, "one bar of the song");
            Assert.AreEqual(WinterSynth.ElevatorBars, song.length / bells.length, 0.001f, "the song is a whole number of layer loops");
            var d = Samples(bells);
            AssertSeamless(d, "bell bar");
            var l = Played(bells, WinterBank.ChristmasBellsVolume);
            Debug.Log("[WinterAudioTests] bell layer at full music level: " + l);
            Assert.AreEqual(WinterBank.BellsLayerDb, l.Max100msDb, 0.75f);
            Assert.Less(l.Max100msDb, ElevatorPlayedDb - 6f, "a layer under the song, not over it");
        }

        [Test]
        public void EndingTracks_SeamlessLoops_AtTheMusicLevel()
        {
            var lib = LoadLibrary();
            var miracle = lib.FindAny(MusicTrack.WinterBells);
            var box = lib.FindAny(MusicTrack.MusicBox);
            var m = Played(miracle.clip, lib.musicVolume * miracle.volume);
            var b = Played(box.clip, lib.musicVolume * box.volume);
            Debug.LogFormat("[WinterAudioTests] miracle vol {0:0.000}: {1} | music box vol {2:0.000}: {3}",
                miracle.volume, m, box.volume, b);
            Assert.AreEqual(WinterBank.MiracleTrackDb, m.Max100msDb, 0.75f, "warm bells with the elevator song");
            Assert.AreEqual(WinterBank.MusicBoxTrackDb, b.Max100msDb, 0.75f, "music box");
            Assert.That(m.Max100msDb, Is.InRange(ElevatorPlayedDb - 3f, ElevatorPlayedDb + 1.5f), "no jump from the day music");
            Assert.Less(b.Max100msDb, m.Max100msDb, "the silent night is quieter");
            Assert.AreEqual(WinterSynth.MiracleSeconds, miracle.clip.length, 0.01f);
            Assert.AreEqual(WinterSynth.SilentNightSeconds, box.clip.length, 0.01f);
            Assert.That(box.clip.length, Is.InRange(30f, 45f), "the whole tune, not a jingle");
            AssertSeamless(Samples(miracle.clip), "miracle loop");
            AssertSeamless(Samples(box.clip), "music box loop");
        }

        [Test]
        public void Synth_CostIsSmall()
        {
            var fns = new (string, System.Func<float[]>)[]
            {
                ("wind", WinterSynth.Wind), ("bells bar", WinterSynth.ChristmasBellsBar), ("sleigh", WinterSynth.SleighShake),
                ("carol", WinterSynth.CarolChime), ("lottery draw", WinterSynth.LotteryDraw), ("jackpot", WinterSynth.Jackpot),
                ("winter start", WinterSynth.WinterStart), ("miracle loop", WinterSynth.WinterMiracleLoop),
                ("music box", WinterSynth.SilentNightLoop),
            };
            long total = 0, samples = 0;
            var log = new System.Text.StringBuilder("[WinterAudioTests] synth cost (editor Mono):");
            foreach (var (name, fn) in fns)
            {
                var sw = Stopwatch.StartNew();
                var d = fn();
                sw.Stop();
                total += sw.ElapsedMilliseconds;
                samples += d.Length;
                log.AppendFormat(" {0} {1} ms ({2} smp);", name, sw.ElapsedMilliseconds, d.Length);
            }
            log.AppendFormat(" total {0} ms, {1} samples ({2:0.0} MB as float)", total, samples, samples * 4 / 1048576.0);
            Debug.Log(log.ToString());
            Assert.Less(samples, 2_500_000, "all winter clips together stay small");
            Assert.Less(total, 4000, "synthesized in a few frames' worth of time (spread one clip per frame)");
        }

        [Test]
        public void Synth_IsDeterministic()
        {
            CollectionAssert.AreEqual(WinterSynth.Jackpot(), WinterSynth.Jackpot());
            CollectionAssert.AreEqual(WinterSynth.ChristmasBellsBar(), WinterSynth.ChristmasBellsBar());
            Assert.AreEqual(440.0, WinterSynth.Midi(69), 1e-9);
            Assert.AreEqual(523.25, WinterSynth.Midi(72), 0.01);
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void Wind_SameScreensAsTheRain_RisesWithTheSnow()
        {
            foreach (DayPhase p in System.Enum.GetValues(typeof(DayPhase)))
            {
                Assert.AreEqual(WeatherRules.RainAudible(true, p), WinterRules.WindAudible(0.3f, p), p + ": rain's screen rule");
                Assert.IsFalse(WinterRules.WindAudible(0f, p), p + ": no snow, no wind");
            }
            Assert.IsTrue(WinterRules.WindAudible(0.3f, DayPhase.Running));
            Assert.IsFalse(WinterRules.WindAudible(1f, DayPhase.Title), "never on the title demo");
            Assert.IsFalse(WinterRules.WindAudible(1f, DayPhase.Shop));
            Assert.AreEqual(0f, WinterRules.WindLevel(0f));
            Assert.AreEqual(1f, WinterRules.WindLevel(1f), 1e-5f);
            float day9 = WinterRules.WindLevel(0.3f), day10 = WinterRules.WindLevel(1f);
            Assert.Less(day9, day10, "more snow = more wind");
            float more = 20f * Mathf.Log10(day10 / day9);
            Assert.That(more, Is.InRange(1.5f, 4.5f), "Christmas Eve a little stronger, not a storm");
            Assert.Greater(WinterRules.WindLevel(0.01f), 0.5f, "a light flurry is still audible");
            Assert.AreEqual(1f, WinterRules.WindLevel(3f), 1e-5f, "clamped");
        }

        [Test]
        public void ChristmasBells_OnlyOnTheElevatorSong_OfTheDayOnScreen()
        {
            Assert.IsTrue(WinterRules.ChristmasBellsAudible(true, DayPhase.Running, MusicTrack.Elevator));
            Assert.IsTrue(WinterRules.ChristmasBellsAudible(true, DayPhase.Intro, MusicTrack.Elevator));
            Assert.IsTrue(WinterRules.ChristmasBellsAudible(true, DayPhase.Summary, MusicTrack.Elevator));
            Assert.IsFalse(WinterRules.ChristmasBellsAudible(false, DayPhase.Running, MusicTrack.Elevator), "other days");
            Assert.IsFalse(WinterRules.ChristmasBellsAudible(true, DayPhase.Title, MusicTrack.Elevator), "title demo");
            Assert.IsFalse(WinterRules.ChristmasBellsAudible(true, DayPhase.Shop, MusicTrack.ShopLoop), "shop music");
            Assert.IsFalse(WinterRules.ChristmasBellsAudible(true, DayPhase.Ending, MusicTrack.WinterBells), "ending");
            Assert.IsFalse(WinterRules.ChristmasBellsAudible(true, DayPhase.Running, MusicTrack.Mystery));
        }

        [Test]
        public void BellsResync_CircularDistance()
        {
            const float bar = 3.38f;
            Assert.AreEqual(0f, WinterRules.BellsResync(10.0f, Mathf.Repeat(10.0f, bar) + 0.02f, bar, 0.04f), 1e-5f, "in tolerance");
            Assert.AreEqual(0.1f, WinterRules.BellsResync(10.1f, Mathf.Repeat(10.0f, bar), bar, 0.04f), 1e-4f, "behind: jump forward");
            Assert.AreEqual(-0.1f, WinterRules.BellsResync(10.0f, Mathf.Repeat(10.1f, bar), bar, 0.04f), 1e-4f, "ahead: jump back");
            // across the bar line: song just past it, layer just before it = 30 ms apart, not a bar
            Assert.AreEqual(0f, WinterRules.BellsResync(bar * 2 + 0.01f, bar - 0.02f, bar, 0.04f), 1e-5f);
            Assert.AreEqual(0f, WinterRules.BellsResync(5f, 1f, 0f, 0.04f), "no clip length: nothing");
        }

        [Test]
        public void WinterEvents_MapToTheirCues()
        {
            Assert.AreEqual(AudioCue.SleighBells, AudioEventMap.ForSpawn(new GuestArchetypeId("santa")));
            Assert.AreEqual(AudioCue.None, AudioEventMap.ForSpawn(new GuestArchetypeId("carolers")), "carolers ring when they board");
            Assert.AreEqual(AudioCue.None, AudioEventMap.ForSpawn(new GuestArchetypeId("solo_traveler")));
            Assert.AreEqual(AudioCue.None, AudioEventMap.ForSpawn(new GuestArchetypeId("korean_grandma")));
            Assert.AreEqual(AudioCue.SleighBells, AudioEventMap.ForBoarding(new GuestArchetypeId("santa")));
            Assert.AreEqual(AudioCue.CarolChime, AudioEventMap.ForBoarding(new GuestArchetypeId("carolers")));
            Assert.AreEqual(AudioCue.None, AudioEventMap.ForBoarding(new GuestArchetypeId("solo_traveler")));
            Assert.AreEqual(AudioCue.None, AudioEventMap.ForBoarding(default), "unknown guest");
            Assert.AreEqual(AudioCue.LotteryDraw, AudioEventMap.For(new LotteryDrawn(7, 0, 2)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new LotteryDrawn(0, 0, 2)), "nothing drawn");
            Assert.AreEqual(AudioCue.LotteryJackpot, AudioEventMap.For(new LotteryWon(new GuestId(1), new FloorIndex(6), 7)));
            Assert.AreEqual(AudioCue.WinterStart, AudioEventMap.For(new SeasonExtended(new DayId(9), 3)));
        }

        [Test]
        public void WinterEndings_HaveTheirOwnMusic()
        {
            var miracle = AudioRules.MusicFor(DayPhase.Ending, EndingKind.WinterMiracle);
            var silent = AudioRules.MusicFor(DayPhase.Ending, EndingKind.SilentNight);
            Assert.AreEqual(MusicTrack.WinterBells, miracle.Track);
            Assert.AreEqual(MusicTrack.MusicBox, silent.Track);
            Assert.AreEqual(1f, miracle.Pitch, 1e-5f);
            Assert.AreEqual(1f, silent.Pitch, 1e-5f);
            // the base-run endings are untouched
            Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(DayPhase.Ending, EndingKind.Prosperous).Track);
            Assert.AreEqual(MusicTrack.Mystery, AudioRules.MusicFor(DayPhase.Ending, EndingKind.Haunted).Track);
        }
    }
}
