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
    /// Rainy-day ambience on the real Main scene (rain bed + seeded distant thunder on their own voices; the rainy day is
    /// forced with AudioRoot.ForceRainy since the day data decides which day rains) and the shop cues.
    /// Batch mode may have no audio device: the tests check voice state / volumes and the request log.
    /// </summary>
    public class WeatherPlayTests
    {
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

        static int CountOf(AudioRoot root, AudioCue cue)
        {
            int n = 0;
            foreach (var r in root.Played) if (r.Cue == cue) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator DryDay_NoRainNoThunder()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsFalse(GameServices.Get<IDayQuery>().IsRainy, "the first day is dry");
            root.ClearRecords();
            yield return new WaitForSecondsRealtime(0.3f);
            root.UpdateWeather(60f);
            Assert.IsFalse(root.RainOn, "no rain voice");
            Assert.AreEqual(0f, root.RainGain);
            Assert.AreEqual(0, root.ThunderStrikes, "no thunder");
            Assert.IsFalse(root.Thunder.Armed);
        }

        [UnityTest]
        public IEnumerator RainyDay_RainFadesIn_OwnLoopingVoice_FadesOutAndStops()
        {
            yield return LoadMain();
            var root = Root();
            var lib = root.Library;
            var src = root.RainSource;
            Assert.IsNotNull(src, "rain voice");
            CollectionAssert.DoesNotContain(root.SfxSources, src, "own voice, not the SFX pool");
            Assert.AreNotSame(root.HumSource, src);

            root.ForceRainy = true;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(root.RainOn, "rain started on the rainy day's intro");
            Assert.IsTrue(src.loop, "looping");
            Assert.AreSame(lib.rainLoop, src.clip);
            Assert.That(root.RainGain, Is.InRange(0.05f, 0.5f), "fading in, not a jump");
            if (AudioSettings.outputSampleRate > 0) Assert.IsTrue(src.isPlaying, "isPlaying (audio output available)");

            // the day starts: same voice keeps going
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            root.UpdateWeather(lib.rainFadeInSeconds);
            yield return null;
            Assert.AreEqual(DayPhase.Running, GameServices.Get<IDayQuery>().Phase);
            Assert.AreEqual(1f, root.RainGain, 1e-4f, "full");
            Assert.AreEqual(root.RainFullVolume, src.volume, 1e-4f);
            Assert.AreEqual(lib.rainVolume, src.volume, 1e-3f, "gentle level (unducked)");

            root.ForceRainy = false; // e.g. the shop opened / next day is dry
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(root.RainGain, Is.InRange(0.5f, 0.99f), "fading out");
            Assert.IsTrue(root.RainOn);
            root.UpdateWeather(lib.rainFadeOutSeconds);
            yield return null;
            Assert.AreEqual(0f, root.RainGain);
            Assert.IsFalse(root.RainOn, "stopped once silent");
            Assert.IsFalse(src.isPlaying);
            Assert.IsFalse(root.Thunder.Armed, "schedule disarmed with the rain");
        }

        [UnityTest]
        public IEnumerator TitleDemo_NeverRains()
        {
            yield return LoadMain(title: true);
            var root = Root();
            root.ForceRainy = true;
            yield return new WaitForSecondsRealtime(0.3f);
            root.UpdateWeather(60f);
            Assert.IsFalse(root.RainOn, "no rain on the title");
            Assert.AreEqual(0, root.ThunderStrikes, "no thunder on the title");
        }

        [UnityTest]
        public IEnumerator Thunder_OwnVoice_OnTheSchedule_NeverStacks()
        {
            yield return LoadMain();
            var root = Root();
            root.ClearRecords();
            root.ForceRainy = true;
            yield return null;
            Assert.IsTrue(root.Thunder.Armed, "schedule armed with the rain");
            Assert.AreEqual(0, root.ThunderStrikes, "no rumble right away");
            float next = root.Thunder.NextStrikeIn;
            Assert.That(next, Is.InRange(root.Library.thunderMinGap + root.Library.thunderDelayMin - 0.5f,
                root.Library.thunderMaxGap + root.Library.thunderDelayMax), "first rumble 9-23 s in");

            root.UpdateWeather(next + 0.05f);
            Assert.AreEqual(1, root.ThunderStrikes, "rumble when due");
            var src = root.ThunderSource;
            CollectionAssert.DoesNotContain(root.SfxSources, src, "own voice");
            CollectionAssert.Contains(root.Library.Find(AudioCue.Thunder).clips, src.clip, "a thunder variant");
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Thunder, out var rec));
            Assert.IsTrue(root.ThunderSounding);
            Assert.That(root.Thunder.NextStrikeIn, Is.GreaterThanOrEqualTo(WeatherRules.MinStrikeSpacing(ThunderSettings.From(root.Library)) - 0.1f),
                "next rumble a normal gap away");
            Assert.AreEqual(root.Library.Find(AudioCue.Thunder).volume * root.RainGain, src.volume, 1e-4f, "fades with the rain");

            // a rumble requested while one still sounds is dropped, not stacked
            root.StrikeThunder();
            Assert.AreEqual(1, root.ThunderStrikes, "no second voice");
            Assert.AreEqual(1, root.ThunderSkipped);
            Assert.AreSame(rec.Clip, src.clip, "the sounding rumble is not cut");
            Assert.AreEqual(1, CountOf(root, AudioCue.Thunder));
        }

        [UnityTest]
        public IEnumerator Pause_DucksRainAndThunder_FreezesTheSchedule()
        {
            yield return LoadMain();
            var root = Root();
            var lib = root.Library;
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            root.ForceRainy = true;
            yield return null;
            root.UpdateWeather(lib.rainFadeInSeconds);
            yield return null;
            Assert.AreEqual(1f, root.RainGain, 1e-4f);
            float full = root.RainSource.volume;

            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(menu.SetPaused(true), "pause");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(root.RainOn, "rain keeps sounding under the pause menu");
            Assert.AreEqual(full * AudioRules.DbToLinear(lib.pauseDuckDb), root.RainSource.volume, 1e-3f, "ducked with the music");
            float clock = root.Thunder.Clock;
            int strikes = root.ThunderStrikes;
            root.UpdateWeather(60f);
            Assert.AreEqual(clock, root.Thunder.Clock, 1e-4f, "schedule frozen while paused");
            Assert.AreEqual(strikes, root.ThunderStrikes, "no rumble while paused");

            Assert.IsTrue(menu.SetPaused(false), "resume");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(full, root.RainSource.volume, 1e-3f, "back to full");
            Assert.Greater(root.Thunder.Clock, clock, "schedule runs again");

            GameServices.Events.Publish(new TutorialHoldChanged(true));
            yield return null;
            clock = root.Thunder.Clock;
            root.UpdateWeather(60f);
            Assert.AreEqual(clock, root.Thunder.Clock, 1e-4f, "coach-mark hold freezes it too");
            GameServices.Events.Publish(new TutorialHoldChanged(false));
        }

        [UnityTest]
        public IEnumerator Rain_FollowsMasterVolume()
        {
            yield return LoadMain();
            var root = Root();
            root.ForceRainy = true;
            yield return null;
            root.UpdateWeather(root.Library.rainFadeInSeconds);
            var settings = GameServices.Get<IGameSettings>();
            settings.MasterVolume = 0.5f;
            yield return null;
            Assert.AreEqual(0.5f * root.Library.rainVolume, root.RainSource.volume, 1e-3f, "x master");
            settings.MasterVolume = 0f;
            yield return null;
            Assert.AreEqual(0f, root.RainSource.volume, 1e-6f, "muted");
        }

        [UnityTest]
        public IEnumerator ShopEvents_PlayTheirCues()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.2f);
            root.ClearRecords();
            var bus = GameServices.Events;
            var charm = new StickerId("talisman");

            bus.Publish(new ShopRerolled(10, 2, 15));
            bus.Publish(new TipsChanged(90, -10));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.ShopReroll, out var reroll), "reroll riffle");
            Assert.AreEqual("card-shuffle", reroll.Clip.name);
            Assert.AreEqual(0, CountOf(root, AudioCue.Tip), "the spend is silent");

            bus.Publish(new CharmBought(charm, 40));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.CharmBought, out var bell), "charm bell");
            Assert.AreEqual("charm_bell", bell.Clip.name);

            bus.Publish(new CharmTriggered(charm, new GuestId(5)));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.CharmShield, out var shield), "shield chime");
            Assert.AreEqual("charm_shield", shield.Clip.name);

            bus.Publish(new StickersExpired(0, 0));
            Assert.AreEqual(0, CountOf(root, AudioCue.StickersPeel), "nothing expired: silent");
            bus.Publish(new StickersExpired(2, 1));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.StickersPeel, out var peel), "peel");
            Assert.AreEqual("sticker_peel", peel.Clip.name);
            Assert.Less(peel.Volume, bell.Volume, "the peel is quiet");
            bool held = false;
            foreach (var s in root.SfxSources) held |= s.clip == peel.Clip;
            Assert.IsTrue(held, "an SFX voice holds the peel");
        }
    }
}
