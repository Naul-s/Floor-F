using System;
using System.Linq;
using FloorF.Audio;
using FloorF.Core;
using NUnit.Framework;
using UnityEngine;

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
            Assert.AreEqual(-6f, lib.holdDuckDb, 0.01f, "coach-mark hold duck -6 dB");
        }

        [Test]
        public void MusicTracks_MainIsBlondPandaLoop_ShopIsMacLeod()
        {
            var lib = LoadLibrary();
            var main = lib.Find(MusicTrack.Elevator);
            StringAssert.Contains("blondpanda__short-elevator-music-loop", main.clip.name, "main BGM = BlondPanda loop");
            Assert.That(main.seamFade, Is.InRange(0.05f, 0.2f), "short fade hides the loop's sample step without a long dip");
            Assert.That(main.volume, Is.InRange(0.3f, 0.6f), "dense loop sits under the SFX");
            var shop = lib.Find(MusicTrack.ShopLoop);
            StringAssert.Contains("Local Forecast - Elevator", shop.clip.name, "shop = Kevin MacLeod");
            Assert.AreEqual(0.3f, shop.seamFade, 1e-4f, "mp3 encoder gap still faded");
            Assert.AreNotEqual(main.clip, shop.clip, "shop sounds different from the days");
            // the moments that use the main BGM
            foreach (var phase in new[] { DayPhase.Title, DayPhase.Intro, DayPhase.Running, DayPhase.Summary })
                Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(phase, EndingKind.None).Track, phase.ToString());
            Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(DayPhase.Ending, EndingKind.Prosperous).Track);
            Assert.AreEqual(MusicTrack.Elevator, AudioRules.MusicFor(DayPhase.Ending, EndingKind.Modest).Track);
            Assert.AreEqual(MusicTrack.ShopLoop, AudioRules.MusicFor(DayPhase.Shop, EndingKind.None).Track);
        }

        [Test]
        public void Streak_PitchClimbsOneSemitonePerStep_Capped()
        {
            var lib = LoadLibrary();
            Assert.AreEqual(1f, lib.streakSemitoneStep, 1e-4f);
            Assert.AreEqual(7f, lib.streakMaxSemitones, 1e-4f);
            float semitone = Mathf.Pow(2f, 1f / 12f);
            Assert.AreEqual(1f, AudioRules.StreakPitch(1, 1f, 7f), 1e-4f, "first lucky = base pitch");
            for (int s = 2; s <= 8; s++)
                Assert.AreEqual(AudioRules.StreakPitch(s - 1, 1f, 7f) * semitone, AudioRules.StreakPitch(s, 1f, 7f), 1e-4f, "step " + s);
            Assert.AreEqual(Mathf.Pow(2f, 7f / 12f), AudioRules.StreakPitch(8, 1f, 7f), 1e-4f, "+7 semitones at streak 8");
            Assert.AreEqual(AudioRules.StreakPitch(8, 1f, 7f), AudioRules.StreakPitch(20, 1f, 7f), 1e-4f, "capped");
            Assert.AreEqual(1f, AudioRules.StreakPitch(0, 1f, 7f), 1e-4f, "never below base");
        }

        [Test]
        public void Streak_EventsMapToUpAndBroken()
        {
            Assert.AreEqual(AudioCue.StreakUp, AudioEventMap.For(new LuckStreakChanged(1, 0, 1.1f)));
            Assert.AreEqual(AudioCue.StreakUp, AudioEventMap.For(new LuckStreakChanged(5, 4, 1.5f)));
            Assert.AreEqual(AudioCue.StreakBroken, AudioEventMap.For(new LuckStreakChanged(0, 3, 1f)));
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new LuckStreakChanged(0, 0, 1f)), "0 -> 0 is not a break");
            Assert.AreEqual(AudioCue.None, AudioEventMap.For(new LuckStreakChanged(2, 3, 1.2f)), "a drop that is not a break is silent");
        }

        [Test]
        public void Countdown_TicksEachOfTheLastTenSecondsOnce()
        {
            var t = new CountdownTicker();
            var ticks = Run(t, 15f, 0f, running: true, silentFrom: -1f, silentTo: -1f);
            CollectionAssert.AreEqual(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, ticks, "10..1, once each");

            t.Reset();
            CollectionAssert.IsEmpty(Run(t, 15f, 0f, running: false, silentFrom: -1f, silentTo: -1f), "not Running (title demo / summary)");

            t.Reset();
            CollectionAssert.IsEmpty(Run(t, 60f, 11f, running: true, silentFrom: -1f, silentTo: -1f), "outside the window");

            t.Reset();
            var paused = Run(t, 15f, 0f, running: true, silentFrom: 7.5f, silentTo: 4.5f);
            CollectionAssert.AreEqual(new[] { 10, 9, 8, 4, 3, 2, 1 }, paused, "silent seconds skipped, not played late");

            // next day re-arms
            t.Reset();
            Run(t, 15f, 0f, running: true, silentFrom: -1f, silentTo: -1f);
            Assert.AreEqual(0, t.Step(false, false, 75f, 10), "summary/intro");
            CollectionAssert.AreEqual(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, Run(t, 12f, 0f, true, -1f, -1f), "day 2 ticks again");

            // a frame that jumps several seconds ticks once
            t.Reset();
            Assert.AreEqual(0, t.Step(true, false, 12f, 10));
            Assert.AreEqual(6, t.Step(true, false, 5.5f, 10));
            Assert.AreEqual(0, t.Step(true, false, 5.1f, 10));

            Assert.AreEqual(1f, AudioRules.ClockTickPitch(10, 10, 0.2f), 1e-4f, "first tick at base");
            Assert.AreEqual(1.2f, AudioRules.ClockTickPitch(1, 10, 0.2f), 1e-4f, "last tick +20%");
            for (int s = 10; s > 1; s--)
                Assert.Greater(AudioRules.ClockTickPitch(s - 1, 10, 0.2f), AudioRules.ClockTickPitch(s, 10, 0.2f), "rising");
            var lib = LoadLibrary();
            Assert.AreEqual(10, lib.clockTickSeconds);
            Assert.That(lib.Find(AudioCue.ClockTick).volume, Is.InRange(0.1f, 0.4f), "soft tick");
        }

        /// <summary>Simulates a 60 fps clock from <paramref name="from"/> to <paramref name="to"/> seconds left.</summary>
        static System.Collections.Generic.List<int> Run(CountdownTicker t, float from, float to, bool running, float silentFrom, float silentTo)
        {
            var ticks = new System.Collections.Generic.List<int>();
            for (float r = from; r > to; r -= 1f / 60f)
            {
                bool silent = r <= silentFrom && r > silentTo;
                int s = t.Step(running, silent, r, 10);
                if (s > 0) ticks.Add(s);
            }
            return ticks;
        }

        [Test]
        public void Duck_PauseBeatsTheLighterCoachMarkHold()
        {
            Assert.AreEqual(1f, AudioRules.DuckTarget(false, false, -12f, -6f), 1e-4f);
            Assert.AreEqual(0.501f, AudioRules.DuckTarget(false, true, -12f, -6f), 0.002f, "hold -6 dB");
            Assert.AreEqual(0.251f, AudioRules.DuckTarget(true, false, -12f, -6f), 0.002f, "pause -12 dB");
            Assert.AreEqual(0.251f, AudioRules.DuckTarget(true, true, -12f, -6f), 0.002f, "pause wins");
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
