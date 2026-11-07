using System.Collections;
using FloorF.Audio;
using FloorF.Core;
using FloorF.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Audio
{
    /// <summary>
    /// Audio on the real Main scene. Batch mode may run without an audio device, so besides AudioSource state the tests
    /// check AudioRoot's request log (which clip was started, when, at what pitch/volume) - see Docs/Notes/audio-dev.
    /// </summary>
    public class AudioPlayTests
    {
        static readonly FloorIndex Floor4 = FloorIndex.FromPhysicalNumber(4);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            AudioUnlock.RequireGestureOverride = null;
            Time.timeScale = 1f;
            if (GameServices.TryGet<IGameSettings>(out var s)) s.MasterVolume = 1f;
            yield return null;
        }

        static AudioRoot Root()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (go.name == AudioRoot.RootName && go.TryGetComponent<AudioRoot>(out var r)) return r;
            Assert.Fail("[Audio] root not installed");
            return null;
        }

        static IEnumerator LoadMain(bool title = false, bool tutorial = false)
        {
            Time.timeScale = 1f;
            GameplayRoot.SkipTitleForTests = !title;
            // Day 0 coach marks (real UI) would hold/duck the game mid-test: start on the first real day
            GameplayRoot.SkipTutorialForTests = !title && !tutorial;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(title ? DayPhase.Title : DayPhase.Intro, GameServices.Get<IDayQuery>().Phase);
        }

        /// <summary>The cue was requested with one of its table clips, and a voice holds that clip (playing when a device exists).</summary>
        static void AssertCuePlayed(AudioRoot root, AudioCue cue)
        {
            Assert.IsTrue(root.TryGetLastPlayed(cue, out var rec), cue + " requested");
            var def = root.Library.Find(cue);
            CollectionAssert.Contains(def.clips, rec.Clip, cue + " clip from the table");
            Assert.That(rec.Pitch, Is.InRange(def.pitch * (1f - def.pitchJitter) - 1e-4f, def.pitch * (1f + def.pitchJitter) + 1e-4f));
            bool held = false, playing = false;
            foreach (var s in root.SfxSources)
                if (s.clip == rec.Clip) { held = true; playing |= s.isPlaying; }
            Assert.IsTrue(held, cue + ": an SFX voice holds the clip");
            bool recent = Time.unscaledTime - rec.Time < rec.Clip.length / Mathf.Max(0.5f, rec.Pitch) * 0.5f;
            if (recent && AudioSettings.outputSampleRate > 0)
                Assert.IsTrue(playing, cue + ": voice isPlaying (audio output available)");
            else if (!playing)
                Debug.Log("[AudioPlayTests] " + cue + " voice not isPlaying (finished, or no audio output) - request log verified instead");
        }

        [UnityTest]
        public IEnumerator CoreEvents_PlayTheirCues()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(root.IsUnlocked, "editor/batch: no gesture lock");
            root.ClearRecords();
            var bus = GameServices.Events;
            var label = new FloorLabel("4");
            var g = new GuestId(77);

            bus.Publish(new ElevatorArrived(Floor4, label));
            bus.Publish(new DoorsOpened(Floor4, label, 1));
            bus.Publish(new LuckJudged(g, LuckSource.FloorLabel, 8, LuckVerdict.Lucky, Floor4));
            bus.Publish(new GuestDelivered(g, Floor4, 10));
            bus.Publish(new GuestLeft(new GuestId(78), Floor4, GuestLeaveReason.PatienceRanOut));
            bus.Publish(new GuestMoodChanged(new GuestId(79), GuestMood.Neutral, GuestMood.Impatient));
            yield return null;
            foreach (var c in new[] { AudioCue.Ding, AudioCue.DoorsOpen, AudioCue.Lucky, AudioCue.Tip, AudioCue.GuestLeft, AudioCue.Impatient })
                AssertCuePlayed(root, c);

            // spam guard: 3 guests judged lucky in the same frame = one sparkle
            int before = CountOf(root, AudioCue.Lucky);
            for (int i = 0; i < 3; i++) bus.Publish(new LuckJudged(new GuestId(80 + i), LuckSource.Occupancy, 8, LuckVerdict.Lucky, Floor4));
            Assert.LessOrEqual(CountOf(root, AudioCue.Lucky) - before, 1, "3 judgements in one frame -> at most one sparkle");

            yield return new WaitForSecondsRealtime(0.35f);
            bus.Publish(new DoorsClosed(Floor4, label, 0));
            bus.Publish(new StickerApplied(new StickerId("four_to_f"), Floor4, label, new FloorLabel("F"), 30));
            bus.Publish(new TipsChanged(100, 70));
            yield return null;
            AssertCuePlayed(root, AudioCue.DoorsClose);
            AssertCuePlayed(root, AudioCue.Sticker);
            Assert.Less(root.Library.Find(AudioCue.DoorsClose).pitch, 1f, "doors close = door sample pitched down");
        }

        static int CountOf(AudioRoot root, AudioCue cue)
        {
            int n = 0;
            foreach (var r in root.Played) if (r.Cue == cue) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator RealDay_DoorsAndArrivalMakeSound()
        {
            yield return LoadMain();
            var root = Root();
            root.ClearRecords();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay(), "BeginDay");
            var control = GameServices.Get<IElevatorControl>();
            var elevator = GameServices.Get<IElevatorQuery>();
            Assert.IsTrue(control.RequestFloor(new FloorIndex(2)), "RequestFloor");
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && !root.TryGetLastPlayed(AudioCue.Ding, out _)) yield return null;
            AssertCuePlayed(root, AudioCue.Ding);
            while (Time.realtimeSinceStartup < until && elevator.Motion != ElevatorMotion.Idle) yield return null;
            Assert.IsTrue(control.OpenDoors(), "OpenDoors");
            while (Time.realtimeSinceStartup < until && !root.TryGetLastPlayed(AudioCue.DoorsOpen, out _)) yield return null;
            AssertCuePlayed(root, AudioCue.DoorsOpen);
            Assert.IsTrue(control.CloseDoors(), "CloseDoors");
            while (Time.realtimeSinceStartup < until && !root.TryGetLastPlayed(AudioCue.DoorsClose, out _)) yield return null;
            AssertCuePlayed(root, AudioCue.DoorsClose);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.DayStart, out _), "day start chime");
        }

        [UnityTest]
        public IEnumerator Unlucky_DipsMusicPitchThenRecovers()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(root.MusicStarted, "music playing");
            Assert.AreEqual(MusicTrack.Elevator, root.CurrentTrack);
            Assert.AreEqual(1f, root.MusicSource.pitch, 0.01f);

            GameServices.Events.Publish(new LuckJudged(new GuestId(5), LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, Floor4));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Unlucky, out _), "gasp");
            float min = 2f, start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.45f)
            {
                yield return null;
                min = Mathf.Min(min, root.MusicSource.pitch);
            }
            Assert.LessOrEqual(min, 0.85f, "pitch dipped (target 0.75)");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return null;
            Assert.AreEqual(1f, root.MusicSource.pitch, 0.01f, "pitch back after ~0.75 s");
            Assert.IsFalse(root.DipActive);
        }

        [UnityTest]
        public IEnumerator Pause_DucksMusicAndStopsSfx()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return new WaitForSecondsRealtime(1.6f); // music fade-in done
            float loud = root.UnduckedMusicVolume;
            Assert.Greater(loud, 0.05f, "music audible before pause");
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));

            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(true), "pause");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsTrue(root.Paused);
            Assert.AreEqual(AudioRules.DbToLinear(-12f), root.DuckGain, 0.01f, "-12 dB duck");
            Assert.LessOrEqual(root.MusicSource.volume, loud * 0.26f, "music ducked");
            Assert.Greater(root.MusicSource.volume, 0f, "ducked, not muted");
            foreach (var s in root.SfxSources) Assert.IsFalse(s.isPlaying, "SFX stopped on pause");
            int played = root.Played.Count;
            GameServices.Events.Publish(new DoorsOpened(Floor4, new FloorLabel("4"), 0));
            Assert.AreEqual(played, root.Played.Count, "no new SFX while paused");

            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(false), "resume");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(1f, root.DuckGain, 0.01f, "duck released");
        }

        [UnityTest]
        public IEnumerator MasterVolumeZero_SilencesEverything()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.5f);
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Assert.Greater(root.MusicSource.volume, 0f, "music on at volume 1");

            GameServices.Get<IGameSettings>().MasterVolume = 0f;
            yield return null;
            Assert.AreEqual(0f, root.MasterVolume);
            Assert.AreEqual(0f, root.MusicSource.volume, 1e-5f, "music volume 0");
            foreach (var s in root.SfxSources) Assert.AreEqual(0f, s.volume, 1e-5f, "sfx voice volume 0");
            yield return new WaitForSecondsRealtime(0.35f);
            GameServices.Events.Publish(new DoorsOpened(Floor4, new FloorLabel("4"), 0));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.DoorsOpen, out var rec));
            Assert.AreEqual(0f, rec.Volume, 1e-5f, "new SFX at volume 0 too");

            GameServices.Get<IGameSettings>().MasterVolume = 0.5f;
            yield return null;
            Assert.Greater(root.MusicSource.volume, 0f, "volume back");
        }

        [UnityTest]
        public IEnumerator FourthFloorEnding_CrossfadesToMysteryLoop()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.3f);
            var elevatorSource = root.MusicSource;
            var day = GameServices.Get<IDayQuery>();
            GameServices.Events.Publish(new EndingReached(EndingKind.FourthFloor, day.CurrentDay, 50, 0));
            yield return null;
            Assert.AreEqual(MusicTrack.Mystery, root.CurrentTrack);
            Assert.AreEqual(root.Library.Find(MusicTrack.Mystery).clip, root.MusicSource.clip, "mystery loop on the active source");
            Assert.AreNotSame(elevatorSource, root.MusicSource, "crossfade uses the other source");
            Assert.IsTrue(root.MusicStarted);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0f, elevatorSource.volume, 1e-4f, "elevator song faded out");
            Assert.Greater(root.MusicSource.volume, 0.05f, "mystery loop faded in");

            GameServices.Events.Publish(new EndingReached(EndingKind.Haunted, day.CurrentDay, 0, 0));
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual(MusicTrack.Mystery, root.CurrentTrack, "haunted = same loop");
            Assert.AreEqual(0.8f, root.MusicSource.pitch, 0.02f, "haunted = pitched down");
        }

        [UnityTest]
        public IEnumerator MainMusic_IsTheBlondPandaLoop()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(MusicTrack.Elevator, root.CurrentTrack);
            Assert.AreSame(root.Library.Find(MusicTrack.Elevator).clip, root.MusicSource.clip);
            StringAssert.Contains("blondpanda", root.MusicSource.clip.name);
            Assert.IsTrue(root.MusicSource.loop, "looping");
            Assert.IsTrue(root.MusicStarted);
        }

        [UnityTest]
        public IEnumerator Streak_ClimbsInSemitonesThenCaps_AndBreaksWithADeflate()
        {
            yield return LoadMain();
            var root = Root();
            root.ClearRecords();
            var bus = GameServices.Events;
            var def = root.Library.Find(AudioCue.StreakUp);
            float semitone = Mathf.Pow(2f, 1f / 12f);
            float prev = 0f;
            for (int s = 1; s <= 10; s++)
            {
                bus.Publish(new LuckStreakChanged(s, s - 1, 1f + 0.1f * s));
                yield return null; // flushed in LateUpdate
                Assert.IsTrue(root.TryGetLastPlayed(AudioCue.StreakUp, out var rec), "streak " + s + " note");
                Assert.AreEqual(s, CountOf(root, AudioCue.StreakUp), "one note per step");
                CollectionAssert.Contains(def.clips, rec.Clip);
                float expected = def.pitch * Mathf.Pow(2f, Mathf.Min(s - 1, 7) / 12f);
                Assert.AreEqual(expected, rec.Pitch, 1e-3f, "streak " + s + " exact pitch (no jitter)");
                if (s >= 2 && s <= 8) Assert.AreEqual(prev * semitone, rec.Pitch, 1e-3f, "+1 semitone at " + s);
                if (s > 8) Assert.AreEqual(prev, rec.Pitch, 1e-4f, "capped at +7");
                prev = rec.Pitch;
                yield return new WaitForSecondsRealtime(def.minInterval + 0.02f);
            }

            // several guests judged lucky in one frame: one note, at the top step
            root.ClearRecords();
            bus.Publish(new LuckStreakChanged(1, 0, 1.1f));
            bus.Publish(new LuckStreakChanged(2, 1, 1.2f));
            bus.Publish(new LuckStreakChanged(3, 2, 1.3f));
            yield return null;
            Assert.AreEqual(1, CountOf(root, AudioCue.StreakUp), "same frame -> one note");
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.StreakUp, out var top));
            Assert.AreEqual(def.pitch * semitone * semitone, top.Pitch, 1e-3f, "the highest step");

            yield return new WaitForSecondsRealtime(0.2f);
            bus.Publish(new LuckStreakChanged(0, 0, 1f));
            yield return null;
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.StreakBroken, out _), "0 -> 0 is no break");
            bus.Publish(new LuckStreakChanged(0, 3, 1f));
            yield return null;
            AssertCuePlayed(root, AudioCue.StreakBroken);
            Assert.Less(root.Library.Find(AudioCue.StreakBroken).pitch, 1f, "deflate = slid down");
            Assert.AreEqual(1, CountOf(root, AudioCue.StreakUp), "a break plays no climb note");
        }

        [UnityTest]
        public IEnumerator RealDay_LastTenSeconds_TickOncePerSecond_NotWhilePaused()
        {
            yield return LoadMain();
            var root = Root();
            var flow = GameServices.Get<IGameFlow>();
            var day = GameServices.Get<IDayQuery>();
            if (day.IsTutorial) Assert.IsTrue(flow.SkipTutorial(), "skip the Day 0 training shift");
            yield return null;
            Assert.IsTrue(flow.BeginDay(), "BeginDay");
            Assert.IsFalse(day.IsTutorial, "a real day");
            Time.timeScale = 6f;
            float until = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < until && day.RemainingSeconds > 11f) yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(DayPhase.Running, day.Phase);
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ClockTick, out _), "no ticks before the last 10 s");
            root.ClearRecords();

            while (Time.realtimeSinceStartup < until && day.RemainingSeconds > 7.6f) yield return null;
            Assert.AreEqual(3, CountOf(root, AudioCue.ClockTick), "10, 9, 8");
            float last = 0f;
            foreach (var r in root.Played)
                if (r.Cue == AudioCue.ClockTick) { Assert.Greater(r.Pitch, last, "rising"); last = r.Pitch; }

            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(true), "pause");
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.AreEqual(3, CountOf(root, AudioCue.ClockTick), "no ticks while paused");
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(false), "resume");
            while (Time.realtimeSinceStartup < until && day.RemainingSeconds > 6.6f) yield return null;
            Assert.AreEqual(4, CountOf(root, AudioCue.ClockTick), "7 after resume, no late/double tick");
        }

        [UnityTest]
        public IEnumerator Countdown_SilentOnTutorialAndTitleDemo()
        {
            // Day 0: the clock never runs down, and the tutorial flag silences ticks on its own
            yield return LoadMain(tutorial: true);
            var root = Root();
            Assert.IsTrue(GameServices.Get<IDayQuery>().IsTutorial, "data starts with the Day 0 shift");
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return new WaitForSecondsRealtime(0.5f);
            root.ClearRecords();
            root.TickCountdown(DayPhase.Running, tutorial: true, remaining: 9.5f);
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ClockTick, out _), "tutorial: no tick");
            yield return null;

            // title screen: the demo's visible phase is Title
            yield return LoadMain(title: true);
            root = Root();
            root.ClearRecords();
            root.TickCountdown(DayPhase.Title, tutorial: false, remaining: 9.5f);
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ClockTick, out _), "title demo: no tick");
            yield return new WaitForSecondsRealtime(2f);
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ClockTick, out _), "title demo running: no tick");
            root.TickCountdown(DayPhase.Running, tutorial: false, remaining: 9.5f);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.ClockTick, out _), "control: a real Running clock does tick");
        }

        [UnityTest]
        public IEnumerator TutorialHold_DucksMusicLighterThanPause()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return new WaitForSecondsRealtime(1.6f);
            float loud = root.UnduckedMusicVolume;
            Assert.Greater(loud, 0.02f, "music audible");
            Assert.AreEqual(1f, root.DuckGain, 0.01f);

            GameServices.Events.Publish(new TutorialHoldChanged(true));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsTrue(root.TutorialHeld);
            Assert.AreEqual(AudioRules.DbToLinear(-6f), root.DuckGain, 0.01f, "-6 dB while the coach marks hold");
            Assert.LessOrEqual(root.MusicSource.volume, loud * 0.52f, "music ducked");
            Assert.Greater(root.MusicSource.volume, loud * 0.2f, "lighter than the pause duck");

            GameServices.Events.Publish(new TutorialHoldChanged(false));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsFalse(root.TutorialHeld);
            Assert.AreEqual(1f, root.DuckGain, 0.01f, "restored on release");
        }

        [UnityTest]
        public IEnumerator WebGLLock_ReservesMusicUntilPlayerStarts()
        {
            AudioUnlock.RequireGestureOverride = true;
            yield return LoadMain(title: true);
            var root = Root();
            Assert.IsFalse(root.IsUnlocked, "locked until a gesture");
            Assert.AreEqual(MusicTrack.Elevator, root.CurrentTrack, "title music reserved");
            Assert.IsFalse(root.MusicStarted, "not started while locked");
            Assert.IsFalse(root.MusicSource.isPlaying);
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.Ding, out _), "SFX dropped while locked (no burst on first click)");
            Assert.Greater(root.SuppressedCount, 0);

            Assert.IsTrue(GameServices.Get<IMenuFlow>().StartGameFromTitle(), "Start pressed");
            yield return null;
            Assert.IsTrue(root.IsUnlocked, "Title -> Intro unlocks");
            Assert.IsTrue(root.MusicStarted, "music starts on the first gesture");
            Assert.AreEqual(MusicTrack.Elevator, root.CurrentTrack);
        }
    }
}
