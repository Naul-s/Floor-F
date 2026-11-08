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
    /// Elevator travel hum on the real Main scene: one looping voice that follows the car (departure in, out before the
    /// ding), never stacks, pauses with the game / coach-mark hold, follows MasterVolume and the title scale.
    /// Plus the star-night wish cues.
    /// </summary>
    public class TravelHumPlayTests
    {
        static readonly FloorIndex Floor4 = FloorIndex.FromPhysicalNumber(4);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayRoot.SkipTitleForTests = false;
            GameplayRoot.SkipTutorialForTests = false;
            Time.timeScale = 1f;
            if (GameServices.TryGet<IGameSettings>(out var s)) s.MasterVolume = 1f;
            if (GameServices.TryGet<IMenuFlow>(out var m) && m.IsPaused) m.SetPaused(false);
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
            Time.timeScale = 1f;
            GameplayRoot.SkipTitleForTests = !title;
            GameplayRoot.SkipTutorialForTests = !title;
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(title ? DayPhase.Title : DayPhase.Intro, GameServices.Get<IDayQuery>().Phase);
        }

        static void Depart() => GameServices.Events.Publish(new ElevatorDeparted(new FloorIndex(0), Floor4));

        static int CountOf(AudioRoot root, AudioCue cue)
        {
            int n = 0;
            foreach (var r in root.Played) if (r.Cue == cue) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Departure_StartsOneLoopingVoice_ArrivalFadesIt_NoStacking()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(root.IsUnlocked);
            root.ClearRecords();
            var src = root.HumSource;
            Assert.IsNotNull(src, "hum voice");
            CollectionAssert.DoesNotContain(root.SfxSources, src, "own voice, not the SFX pool");
            Assert.IsFalse(root.HumOn, "silent while parked");

            Depart();
            yield return null;
            Assert.IsTrue(root.HumMoving);
            Assert.IsTrue(root.HumOn, "voice started");
            Assert.IsTrue(src.loop, "looping");
            Assert.AreSame(root.Library.travelHum, src.clip);
            Assert.Less(root.HumGain, 1f, "fading in, not a jump");
            if (AudioSettings.outputSampleRate > 0) Assert.IsTrue(src.isPlaying, "isPlaying (audio output available)");

            yield return new WaitForSecondsRealtime(root.Library.travelFadeInSeconds + 0.1f);
            Assert.AreEqual(1f, root.HumGain, 1e-4f, "full after the 0.15 s fade-in");
            Assert.AreEqual(root.HumFullVolume, src.volume, 1e-4f, "volume = master x hum volume");
            Assert.AreEqual(root.Library.travelHumVolume, root.HumFullVolume, 1e-4f, "game (not title): no title scale");

            // re-triggers (a retarget, a second departure) keep the same voice going
            for (int i = 0; i < 3; i++) { Depart(); yield return null; }
            Assert.AreEqual(1, root.HumStarts, "one Play() for the whole trip");
            Assert.AreEqual(1f, root.HumGain, 1e-4f, "no dip on re-trigger");

            GameServices.Events.Publish(new ElevatorArrived(Floor4, new FloorLabel("4")));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Ding, out var ding), "ding");
            foreach (var s in root.SfxSources) Assert.AreNotSame(src, s);
            yield return null;
            Assert.IsFalse(root.HumMoving);
            Assert.Less(root.HumGain, 1f, "fading out under the ding");
            yield return new WaitForSecondsRealtime(root.Library.travelFadeOutSeconds + 0.1f);
            Assert.AreEqual(0f, root.HumGain, 1e-6f);
            Assert.IsFalse(root.HumOn, "voice stopped after the fade");
            Assert.IsFalse(src.isPlaying);

            // the next trip starts the voice again (one at a time)
            Depart();
            yield return null;
            Assert.AreEqual(2, root.HumStarts);
            Assert.IsTrue(root.HumOn);
        }

        [UnityTest]
        public IEnumerator RealTrip_HumRisesInPitch_AndIsOutBeforeTheDing()
        {
            yield return LoadMain();
            var root = Root();
            root.ClearRecords();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay(), "BeginDay");
            yield return null;
            var elevator = GameServices.Get<IElevatorQuery>();
            Assert.IsTrue(GameServices.Get<IElevatorControl>().RequestFloor(new FloorIndex(elevator.CurrentFloor.Value + 4)), "RequestFloor");

            float until = Time.realtimeSinceStartup + 20f;
            float maxGain = 0f, maxPitch = 0f, gainBeforeDing = -1f, minEta = float.MaxValue;
            bool sawEta = false;
            while (Time.realtimeSinceStartup < until && !root.TryGetLastPlayed(AudioCue.Ding, out _))
            {
                gainBeforeDing = root.HumGain;
                maxGain = Mathf.Max(maxGain, root.HumGain);
                maxPitch = Mathf.Max(maxPitch, root.HumPitch);
                if (root.HumEta > 0f) { sawEta = true; minEta = Mathf.Min(minEta, root.HumEta); }
                yield return null;
            }
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Ding, out _), "arrived");
            Assert.AreEqual(1f, maxGain, 1e-3f, "full level while travelling (4 floors)");
            Assert.AreEqual(1f, maxPitch, 0.01f, "spun up to pitch 1");
            Assert.IsTrue(sawEta, "arrival predicted from the measured car speed");
            Assert.Less(minEta, root.Library.travelFadeOutSeconds, "prediction reached the fade window");
            Assert.LessOrEqual(gainBeforeDing, 0.2f, "hum (nearly) out when the ding plays: " + gainBeforeDing);
            Assert.Less(root.HumPitch, 0.99f, "braking dip at arrival");
            Assert.AreEqual(1, root.HumStarts, "one voice start for the trip");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(root.HumOn, "stopped after arrival");
        }

        [UnityTest]
        public IEnumerator PauseAndHold_PauseTheHum_ResumeContinues()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return null;
            root.ClearRecords();
            Depart();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(1f, root.HumGain, 1e-4f);

            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(menu.SetPaused(true), "pause");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsTrue(root.HumPaused, "hum paused with the game");
            Assert.IsTrue(root.HumOn, "paused, not stopped");
            Assert.IsFalse(root.HumSource.isPlaying, "not sounding while paused");
            Assert.AreEqual(1f, root.HumGain, 1e-4f, "state frozen");
            Assert.IsTrue(menu.SetPaused(false), "resume");
            yield return null;
            Assert.IsFalse(root.HumPaused);
            if (AudioSettings.outputSampleRate > 0) Assert.IsTrue(root.HumSource.isPlaying, "resumed");
            Assert.AreEqual(1, root.HumStarts, "resumed, not restarted");

            GameServices.Events.Publish(new TutorialHoldChanged(true));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(root.HumPaused, "coach-mark hold freezes the car: hum paused");
            Assert.AreEqual(1f, root.HumGain, 1e-4f);
            GameServices.Events.Publish(new TutorialHoldChanged(false));
            yield return null;
            Assert.IsFalse(root.HumPaused);
            Assert.IsTrue(root.HumOn);
            Assert.AreEqual(1, root.HumStarts);
        }

        [UnityTest]
        public IEnumerator Hum_FollowsMasterVolume()
        {
            yield return LoadMain();
            var root = Root();
            Depart();
            yield return new WaitForSecondsRealtime(0.3f);
            var settings = GameServices.Get<IGameSettings>();
            settings.MasterVolume = 0.5f;
            yield return null;
            Assert.AreEqual(0.5f * root.Library.travelHumVolume, root.HumSource.volume, 1e-4f, "x master");
            settings.MasterVolume = 0f;
            yield return null;
            Assert.AreEqual(0f, root.HumSource.volume, 1e-6f, "muted");
        }

        [UnityTest]
        public IEnumerator TitleDemo_HumAtTheTitleSfxScale()
        {
            yield return LoadMain(title: true);
            var root = Root();
            var lib = root.Library;
            Assert.AreEqual(lib.travelHumVolume * lib.titleSfxScale, root.HumFullVolume, 1e-4f, "title demo scale");
            // the demo car drives the hum (its own departures / arrivals)
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && !(root.HumOn && root.HumGain >= 1f)) yield return null;
            Assert.IsTrue(root.HumOn, "a demo trip started the hum");
            Assert.AreEqual(1f, root.HumGain, 1e-4f);
            Assert.AreEqual(root.HumFullVolume, root.HumSource.volume, 1e-4f, "played at the title scale");
            Assert.Less(root.HumSource.volume, lib.travelHumVolume * 0.5f, "quieter than in game");
            Assert.AreEqual(DayPhase.Title, GameServices.Get<IDayQuery>().Phase);
        }

        [UnityTest]
        public IEnumerator WishStar_Twinkles_WishMade_Chimes_FadeIsSilent()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.2f);
            root.ClearRecords();
            var bus = GameServices.Events;
            bus.Publish(new WishStarAppeared(1, 0.1f, 0.8f, 0.9f, 0.7f, 3f));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.WishStar, out var star), "star twinkle");
            Assert.AreEqual("wish_star_twinkle", star.Clip.name);
            bus.Publish(new WishMade(1, 3));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Wish, out var wish), "wish chime");
            Assert.AreEqual("wish_chime", wish.Clip.name);
            int n = root.Played.Count;
            bus.Publish(new WishStarFaded(2));
            Assert.AreEqual(n, root.Played.Count, "fade: nothing");

            // not a star night (Day 1): a Lucky gets no star sparkle
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsStarNight);
            bus.Publish(new LuckJudged(new GuestId(9), LuckSource.FloorLabel, 7, LuckVerdict.Lucky, Floor4));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Lucky, out _));
            Assert.AreEqual(0, CountOf(root, AudioCue.StarSparkle), "no sparkle off star night");
        }
    }
}
