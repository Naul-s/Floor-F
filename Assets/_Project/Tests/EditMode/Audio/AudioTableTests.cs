using System;
using System.Linq;
using FloorF.Audio;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>The audio data table (real Resources/Audio/AudioLibrary asset) and the pure audio rules.</summary>
    public class AudioTableTests
    {
        static AudioLibrary LoadLibrary()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath + " (run FloorF.EditorTools.AudioLibraryBuilder.Build)");
            return lib;
        }

        [Test]
        public void AudioTable_EveryMappedEventHasClip()
        {
            var lib = LoadLibrary();
            Assert.IsNotEmpty(AudioEventMap.Mapped);
            foreach (var m in AudioEventMap.Mapped)
            {
                var def = lib.Find(m.Cue);
                Assert.IsNotNull(def, m + ": cue row");
                Assert.IsNotEmpty(def.clips, m + ": clips");
                Assert.IsTrue(def.clips.All(c => c != null), m + ": no empty clip slot");
                Assert.Greater(def.volume, 0f, m + ": volume");
                Assert.LessOrEqual(def.volume, 1f, m + ": volume");
            }
            // every cue in the enum (incl. reserved ones) has a row, so wiring a new event is one line of code
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                if (cue == AudioCue.None) continue;
                var def = lib.Find(cue);
                Assert.IsNotNull(def, cue + " row");
                Assert.IsTrue(def.clips.Length > 0 && def.clips.All(c => c != null), cue + " clips");
            }
            Assert.AreEqual(lib.cues.Length, lib.cues.Select(c => c.cue).Distinct().Count(), "one row per cue");
            foreach (MusicTrack t in Enum.GetValues(typeof(MusicTrack)))
            {
                if (t == MusicTrack.None) continue;
                Assert.IsNotNull(lib.Find(t)?.clip, t + " music clip");
            }
            Assert.AreEqual(-12f, lib.pauseDuckDb, 0.01f, "pause duck -12 dB");
        }

        [Test]
        public void AudioTable_HandlersUseTheMappedCues()
        {
            var g = new GuestId(1);
            var f = new FloorIndex(3);
            Assert.AreEqual(AudioCue.Lucky, AudioEventMap.For(new LuckJudged(g, LuckSource.FloorLabel, 8, LuckVerdict.Lucky, f)));
            Assert.AreEqual(AudioCue.Unlucky, AudioEventMap.For(new LuckJudged(g, LuckSource.Occupancy, 4, LuckVerdict.Unlucky, f)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new LuckJudged(g, LuckSource.Other, -1, LuckVerdict.Neutral, f)));
            Assert.AreEqual(AudioCue.Tip, AudioEventMap.For(new TipsChanged(10, 25)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new TipsChanged(25, 10)), "spending tips (sticker) is not a tip sound");
            Assert.AreEqual(AudioCue.GuestLeft, AudioEventMap.For(new GuestLeft(g, f, GuestLeaveReason.PatienceRanOut)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new GuestLeft(g, f, GuestLeaveReason.DayEnded)), "day-end sweep is silent");
            Assert.AreEqual(AudioCue.Impatient, AudioEventMap.For(new GuestMoodChanged(g, GuestMood.Neutral, GuestMood.Impatient)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new GuestMoodChanged(g, GuestMood.Impatient, GuestMood.Angry)));
        }

        [Test]
        public void PitchVariation_StaysInRange()
        {
            var lib = LoadLibrary();
            var rng = new System.Random(1234);
            foreach (var def in lib.cues)
            {
                Assert.GreaterOrEqual(def.pitchJitter, AudioRules.MinJitter - 1e-6f, def.cue + " jitter >= 5%");
                Assert.LessOrEqual(def.pitchJitter, AudioRules.MaxJitter + 1e-6f, def.cue + " jitter <= 10%");
                float lo = def.pitch * (1f - def.pitchJitter), hi = def.pitch * (1f + def.pitchJitter);
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < 2000; i++)
                {
                    float p = AudioRules.PickPitch(def.pitch, def.pitchJitter, rng);
                    Assert.GreaterOrEqual(p, lo - 1e-4f, def.cue + " pitch low");
                    Assert.LessOrEqual(p, hi + 1e-4f, def.cue + " pitch high");
                    min = Math.Min(min, p);
                    max = Math.Max(max, p);
                }
                Assert.Greater(max - min, def.pitch * def.pitchJitter, def.cue + " actually varies");
            }
            // out-of-range jitter is clamped into 5..10%
            for (int i = 0; i < 500; i++)
            {
                float p = AudioRules.PickPitch(1f, 0.5f, rng);
                Assert.That(p, Is.InRange(0.9f - 1e-4f, 1.1f + 1e-4f));
            }
        }

        [Test]
        public void Throttle_DropsRapidRepeats()
        {
            var t = new CueThrottle();
            Assert.IsTrue(t.TryConsume(AudioCue.Lucky, 10f, 0.12f), "first plays");
            Assert.IsFalse(t.TryConsume(AudioCue.Lucky, 10f, 0.12f), "same frame (3 guests judged) -> dropped");
            Assert.IsFalse(t.TryConsume(AudioCue.Lucky, 10.05f, 0.12f), "inside the interval -> dropped");
            Assert.IsTrue(t.TryConsume(AudioCue.Tip, 10.05f, 0.1f), "other cues are independent");
            Assert.IsTrue(t.TryConsume(AudioCue.Lucky, 10.13f, 0.12f), "after the interval plays again");
            Assert.IsFalse(t.TryConsume(AudioCue.Lucky, 10.2f, 0.12f), "interval measured from the last play");
            t.Clear();
            Assert.IsTrue(t.TryConsume(AudioCue.Lucky, 10.2f, 0.12f), "Clear resets");
        }

        [Test]
        public void PitchDip_DropsAndRecovers()
        {
            var d = new PitchDip();
            Assert.AreEqual(1f, d.Evaluate(0f, 0.75f, 0.15f, 0.6f));
            d.Trigger(1f);
            Assert.AreEqual(0.75f, d.Evaluate(1.15f, 0.75f, 0.15f, 0.6f), 0.01f, "bottom after 0.15 s");
            float mid = d.Evaluate(1.45f, 0.75f, 0.15f, 0.6f);
            Assert.That(mid, Is.InRange(0.76f, 0.99f), "gliding back");
            Assert.AreEqual(1f, d.Evaluate(1.8f, 0.75f, 0.15f, 0.6f), 1e-4f, "back after 0.75 s");
            Assert.IsFalse(d.Active);
        }

        [Test]
        public void MusicPlan_FollowsPhaseAndEnding()
        {
            Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(DayPhase.Title, EndingKind.None).Track);
            Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(DayPhase.Running, EndingKind.None).Track);
            Assert.Less(AudioRules.MusicFor(DayPhase.Summary, EndingKind.None).Volume, 1f, "summary quieter");
            Assert.AreEqual(MusicTrack.ShopLoop, AudioRules.MusicFor(DayPhase.Shop, EndingKind.None).Track);
            Assert.AreEqual(MusicTrack.Mystery, AudioRules.MusicFor(DayPhase.Ending, EndingKind.FourthFloor).Track);
            var haunted = AudioRules.MusicFor(DayPhase.Ending, EndingKind.Haunted);
            Assert.AreEqual(MusicTrack.Mystery, haunted.Track);
            Assert.Less(haunted.Pitch, 1f, "haunted = mystery loop pitched down");
            var rich = AudioRules.MusicFor(DayPhase.Ending, EndingKind.Prosperous);
            Assert.AreEqual(MusicTrack.Elevator, rich.Track);
            Assert.Greater(rich.Pitch, 1f, "prosperous = brighter");
            Assert.IsTrue(AudioRules.IsPlayerDrivenTransition(DayPhase.Title, DayPhase.Intro));
            Assert.IsTrue(AudioRules.IsPlayerDrivenTransition(DayPhase.Intro, DayPhase.Running));
            Assert.IsFalse(AudioRules.IsPlayerDrivenTransition(DayPhase.Running, DayPhase.Summary), "clock ran out");
            Assert.IsFalse(AudioRules.IsPlayerDrivenTransition(DayPhase.None, DayPhase.Title), "boot");
        }

        [Test]
        public void SeamGain_FadesOnlyTheLoopEdges()
        {
            Assert.AreEqual(0f, AudioRules.SeamGain(0f, 60f, 0.3f), 1e-4f);
            Assert.AreEqual(1f, AudioRules.SeamGain(30f, 60f, 0.3f), 1e-4f);
            Assert.AreEqual(0.5f, AudioRules.SeamGain(59.85f, 60f, 0.3f), 1e-3f);
            Assert.AreEqual(1f, AudioRules.SeamGain(0f, 60f, 0f), "no fade = seamless loop");
            Assert.AreEqual(0.251f, AudioRules.DbToLinear(-12f), 0.002f);
        }
    }
}
