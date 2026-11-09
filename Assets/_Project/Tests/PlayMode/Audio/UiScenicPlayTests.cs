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
    /// UI feedback (Tap spam guard, click + tink, sounds while paused, Reveal on the title) and the scenic view
    /// (player's scenic mode keeps the music while paused, SFX lowered; establishing shot whoosh) on the real Main scene.
    /// Checks AudioRoot's request log (batch mode may have no audio device).
    /// </summary>
    public class UiScenicPlayTests
    {
        static readonly FloorIndex Floor4 = FloorIndex.FromPhysicalNumber(4);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            AudioUnlock.RequireGestureOverride = null;
            if (GameServices.TryGet<IMenuFlow>(out var m)) m.SetPaused(false);
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
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

        static IEnumerator LoadMain(bool title = false)
        {
            GameplayRoot.SkipTitleForTests = !title;
            GameplayRoot.SkipTutorialForTests = !title;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(title ? DayPhase.Title : DayPhase.Intro, GameServices.Get<IDayQuery>().Phase);
        }

        static int CountOf(AudioRoot root, AudioCue cue)
        {
            int n = 0;
            foreach (var r in root.Played) if (r.Cue == cue) n++;
            return n;
        }

        static void Ui(UiSoundKind kind) => GameServices.Events.Publish(new UiSound(kind));

        [UnityTest]
        public IEnumerator Tap_SpamIsGuarded_BurstsGetSofter_ClickCarriesTheTink()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.3f);
            root.ClearRecords();
            var def = root.Library.Find(AudioCue.Tap);

            Ui(UiSoundKind.Tap);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Tap, out var first), "tap plays");
            Assert.AreEqual(def.volume, first.Volume, 1e-4f, "lone tap at its row volume");
            Assert.Less(first.Volume, root.Library.Find(AudioCue.UiClick).volume, "tap quieter than a button click");
            for (int i = 0; i < 5; i++) Ui(UiSoundKind.Tap);
            Assert.AreEqual(1, CountOf(root, AudioCue.Tap), "same-frame taps collapse to one");
            Assert.AreEqual(5, root.TapsDropped);

            // a click every frame for 1 s
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < 1f) { Ui(UiSoundKind.Tap); yield return null; }
            int taps = CountOf(root, AudioCue.Tap);
            Assert.LessOrEqual(taps, Mathf.CeilToInt(1f / def.minInterval) + 2, "rate capped by the min interval");
            Assert.Greater(taps, 3, "taps still answer the clicks");
            float prev = float.MaxValue, prevTime = float.NegativeInfinity;
            foreach (var r in root.Played)
            {
                if (r.Cue != AudioCue.Tap) continue;
                // (a batch-mode frame hitch longer than the repeat window legitimately restores the full tink)
                if (r.Time - prevTime < root.Library.tapRepeatWindow)
                    Assert.LessOrEqual(r.Volume, prev + 1e-5f, "a burst never gets louder");
                Assert.LessOrEqual(r.Volume, def.volume + 1e-5f, "never above a lone tap");
                Assert.GreaterOrEqual(r.Volume, def.volume * root.Library.tapRepeatFloor - 1e-4f, "floor");
                prev = r.Volume;
                prevTime = r.Time;
            }

            // a quiet moment, then a button click: the click plays with the tink layered; a tap in the same gesture is dropped
            yield return new WaitForSecondsRealtime(0.5f);
            root.ClearRecords();
            Ui(UiSoundKind.Click);
            Ui(UiSoundKind.Tap);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.UiClick, out _), "click");
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.Tap, out _), "no extra tap on a button click");
            var tink = root.Library.Find(AudioCue.UiClick).layer;
            Assert.IsNotNull(tink);
            bool held = false;
            foreach (var s in root.SfxSources) held |= s.clip == tink && root.IsUiVoice(s);
            Assert.IsTrue(held, "the click's tink layer is on a UI voice");
            yield return new WaitForSecondsRealtime(0.4f);
            Ui(UiSoundKind.Tap);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Tap, out var later));
            Assert.AreEqual(def.volume, later.Volume, 1e-4f, "after a quiet gap: full tink again");
        }

        [UnityTest]
        public IEnumerator UiSounds_PlayWhilePaused_GameSoundsDoNot()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return new WaitForSecondsRealtime(0.3f);
            root.ClearRecords();
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Ui(UiSoundKind.Click); // e.g. the pause button itself
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(true), "pause");
            yield return null;
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Ding, out var ding));
            foreach (var s in root.SfxSources)
                if (s.clip == ding.Clip) Assert.IsFalse(s.isPlaying, "game sound stopped on pause");

            yield return new WaitForSecondsRealtime(0.1f);
            int before = root.Played.Count;
            GameServices.Events.Publish(new DoorsOpened(Floor4, new FloorLabel("4"), 0));
            Assert.AreEqual(before, root.Played.Count, "game SFX silent while paused");
            Ui(UiSoundKind.Click);
            Ui(UiSoundKind.Error);
            Assert.AreEqual(2, CountOf(root, AudioCue.UiClick), "pause-menu clicks are heard");
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Error, out _), "UI error heard while paused");
            yield return new WaitForSecondsRealtime(0.2f);
            Ui(UiSoundKind.Tap);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Tap, out _), "taps heard while paused");
        }

        [UnityTest]
        public IEnumerator PlayerScenic_KeepsTheMusicThoughPaused_LowersSfx()
        {
            yield return LoadMain();
            var root = Root();
            var lib = root.Library;
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return new WaitForSecondsRealtime(1.6f); // music faded in
            float loud = root.UnduckedMusicVolume;
            Assert.Greater(loud, 0.05f);
            root.ClearRecords();

            // UI: scenic on, then the game pauses
            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(true), "scenic mode pauses the game");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsTrue(root.Scenic);
            Assert.IsTrue(root.Paused);
            Assert.AreEqual(AudioRules.DbToLinear(lib.scenicMusicDb), root.DuckGain, 0.01f, "music NOT pause-ducked");
            Assert.Greater(root.MusicSource.volume, loud * 0.5f, "music keeps playing at its level");
            float scen = AudioRules.DbToLinear(lib.scenicSfxDb);
            Ui(UiSoundKind.Tap);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Tap, out var tap), "tap while looking at the city");
            Assert.AreEqual(lib.Find(AudioCue.Tap).volume * scen, tap.Volume, 1e-4f, "SFX a little lower");
            Assert.AreEqual(scen, root.SfxScale(false), 1e-5f);

            // off: unpause first, then the scenic flag (either order works)
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(false));
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(root.Scenic);
            Assert.AreEqual(1f, root.DuckGain, 0.01f);
            Assert.AreEqual(1f, root.SfxScale(false), 1e-5f, "SFX back to full");
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ScenicWhoosh, out _), "the player's scenic mode does not whoosh");

            // the other order: pause first, then scenic -> the duck releases to the scenic level
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(true));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(AudioRules.DbToLinear(lib.pauseDuckDb), root.DuckGain, 0.01f, "a plain pause still ducks");
            GameServices.Events.Publish(new ScenicViewChanged(true, false));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(1f, root.DuckGain, 0.01f, "scenic lifts the pause duck");
            GameServices.Events.Publish(new ScenicViewChanged(false, false));
            Assert.IsTrue(GameServices.Get<IMenuFlow>().SetPaused(false));
        }

        [UnityTest]
        public IEnumerator EstablishingShot_LeavesLevels_WhooshesOnTheZoomIn()
        {
            yield return LoadMain();
            var root = Root();
            var lib = root.Library;
            yield return new WaitForSecondsRealtime(0.3f);
            root.ClearRecords();

            GameServices.Events.Publish(new ScenicViewChanged(true, true));
            Assert.IsTrue(root.Establishing);
            Assert.IsFalse(root.Scenic, "not the player's scenic mode");
            Assert.IsFalse(root.TryGetLastPlayed(AudioCue.ScenicWhoosh, out _), "silent as it zooms out");
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Ding, out var ding));
            Assert.AreEqual(lib.Find(AudioCue.Ding).volume, ding.Volume, 1e-4f, "levels untouched during the shot");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(1f, root.DuckGain, 0.01f, "music untouched");

            yield return new WaitForSecondsRealtime(0.2f);
            GameServices.Events.Publish(new ScenicViewChanged(false, true));
            Assert.IsFalse(root.Establishing);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.ScenicWhoosh, out var whoosh), "whoosh on the zoom-in");
            var def = lib.Find(AudioCue.ScenicWhoosh);
            CollectionAssert.Contains(def.clips, whoosh.Clip);
            Assert.AreEqual(def.volume, whoosh.Volume, 1e-4f, "soft, at its row volume");
            GameServices.Events.Publish(new ScenicViewChanged(false, true));
            Assert.AreEqual(1, CountOf(root, AudioCue.ScenicWhoosh), "a repeated end message does not whoosh twice");
        }

        [UnityTest]
        public IEnumerator Reveal_OnTheTitle_OpensTheAudioLock_AndIsNotTitleScaled()
        {
            AudioUnlock.RequireGestureOverride = true;
            yield return LoadMain(title: true);
            var root = Root();
            Assert.IsFalse(root.IsUnlocked, "locked until a gesture");

            Ui(UiSoundKind.Reveal); // "press any key" -> the menu appears
            Assert.IsTrue(root.IsUnlocked, "the reveal is the player's key press: lock opens");
            Assert.IsTrue(root.MusicStarted, "title music starts with it");
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Reveal, out var rev), "reveal shimmer plays (not dropped by the lock)");
            var def = root.Library.Find(AudioCue.Reveal);
            CollectionAssert.Contains(def.clips, rev.Clip);
            Assert.AreEqual(def.volume, rev.Volume, 1e-4f, "UI sound: no title demo scale");

            // control: a game sound from the demo is still title-scaled
            yield return new WaitForSecondsRealtime(0.3f);
            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Ding, out var ding));
            Assert.AreEqual(root.Library.Find(AudioCue.Ding).volume * root.Library.titleSfxScale, ding.Volume, 1e-4f);
        }
    }
}
