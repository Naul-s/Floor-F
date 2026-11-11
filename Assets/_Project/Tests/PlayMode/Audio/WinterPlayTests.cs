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
    /// Winter season audio on the real Main scene with a fake ISeasonQuery (AudioRoot.SeasonOverride - Gameplay owns the
    /// real one): snow wind, the Christmas bell layer on the elevator song, Santa / carolers / lottery / season cues and
    /// the winter endings. Guest / lottery / season events go straight to AudioRoot's handlers (fake guests published on
    /// the bus would reach Gameplay and the UI too). Batch mode may have no audio device: voice state + request log.
    /// </summary>
    public class WinterPlayTests
    {
        sealed class FakeSeason : ISeasonQuery
        {
            public bool IsSeasonExtended { get; set; } = true;
            public bool IsExtendedDay { get; set; } = true;
            public bool MysteryTonight { get; set; }
            public bool IsLotteryNight { get; set; }
            public int LotteryNumber { get; set; }
            public bool IsChristmas { get; set; }
            public int LuckyForAll { get; set; }
            public float SnowAmount { get; set; }
        }

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

        static void AssertPlayed(AudioRoot root, AudioCue cue)
        {
            Assert.IsTrue(root.TryGetLastPlayed(cue, out var rec), cue + " requested");
            var def = root.Library.FindAny(cue);
            CollectionAssert.Contains(def.clips, rec.Clip, cue + " clip from the winter table");
            bool held = false;
            foreach (var s in root.SfxSources) held |= s.clip == rec.Clip;
            Assert.IsTrue(held, cue + ": an SFX voice holds the clip");
        }

        static GuestSpawned Spawn(int id, string archetype)
            => new GuestSpawned(new GuestId(id), new GuestArchetypeId(archetype), new PartyId(id), new FloorIndex(0),
                new DestinationId("room"), new FloorIndex(5), 0);

        [UnityTest]
        public IEnumerator NoSeasonService_NoWindNoBells()
        {
            yield return LoadMain();
            var root = Root();
            if (GameServices.Has<ISeasonQuery>()) Assert.Ignore("Gameplay registers ISeasonQuery now - covered by the fake tests");
            root.UpdateWinter(10f);
            Assert.IsFalse(root.WindOn);
            Assert.IsFalse(root.BellsOn);
        }

        [UnityTest]
        public IEnumerator SnowDay_WindFadesIn_OwnVoice_LevelFollowsTheSnow_NoRain()
        {
            yield return LoadMain();
            var root = Root();
            var season = new FakeSeason { SnowAmount = 0.3f };
            root.SeasonOverride = season;
            root.ForceRainy = false;
            yield return new WaitForSecondsRealtime(0.5f);
            var src = root.WindSource;
            Assert.IsNotNull(src);
            CollectionAssert.DoesNotContain(root.SfxSources, src, "own voice");
            Assert.AreNotSame(root.RainSource, src);
            Assert.IsTrue(root.WindOn, "wind on the snowy day's intro");
            Assert.IsTrue(src.loop);
            Assert.AreSame(WinterBank.WindLoop, src.clip);
            Assert.That(root.WindGain, Is.InRange(0.05f, 0.5f), "fading in");
            Assert.IsFalse(root.RainOn, "snow is not rain");

            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            root.UpdateWinter(WinterBank.WindFadeInSeconds);
            yield return null;
            Assert.AreEqual(1f, root.WindGain, 1e-4f);
            float light = src.volume;
            Assert.AreEqual(WinterBank.WindVolume * WinterRules.WindLevel(0.3f), light, 1e-3f, "light snow level");

            season.SnowAmount = 1f; // Christmas Eve's snow
            yield return null;
            Assert.Greater(src.volume, light, "more snow, more wind");
            Assert.AreEqual(WinterBank.WindVolume, src.volume, 1e-3f, "full level at full snow");

            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(menu.SetPaused(true));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(root.WindOn, "keeps blowing under the pause menu");
            Assert.AreEqual(WinterBank.WindVolume * AudioRules.DbToLinear(root.Library.pauseDuckDb), src.volume, 1e-3f, "ducked with the music");
            Assert.IsTrue(menu.SetPaused(false));
            yield return null;

            GameServices.Get<IGameSettings>().MasterVolume = 0.5f;
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(0.5f * WinterBank.WindVolume, src.volume, 1e-3f, "x master");

            season.SnowAmount = 0f; // the snow stops
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(root.WindGain, Is.InRange(0.5f, 0.99f), "fading out");
            root.UpdateWinter(WinterBank.WindFadeOutSeconds);
            yield return null;
            Assert.IsFalse(root.WindOn, "stopped once silent");
            Assert.IsFalse(src.isPlaying);
        }

        [UnityTest]
        public IEnumerator TitleDemo_NoWindNoBells()
        {
            yield return LoadMain(title: true);
            var root = Root();
            root.SeasonOverride = new FakeSeason { SnowAmount = 1f, IsChristmas = true };
            yield return new WaitForSecondsRealtime(0.3f);
            root.UpdateWinter(10f);
            Assert.IsFalse(root.WindOn, "no wind on the title");
            Assert.IsFalse(root.BellsOn, "no bells on the title");
        }

        [UnityTest]
        public IEnumerator ChristmasEve_BellsRideTheElevatorSong_InSync_ThenLeaveWithIt()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(MusicTrack.Elevator, root.CurrentTrack);
            var season = new FakeSeason();
            root.SeasonOverride = season;
            yield return null;
            Assert.IsFalse(root.BellsOn, "not Christmas: no bells");

            season.IsChristmas = true;
            yield return null;
            var bells = root.BellsSource;
            Assert.IsTrue(root.BellsOn, "bells on Christmas Eve");
            CollectionAssert.DoesNotContain(root.SfxSources, bells, "own voice");
            Assert.AreNotSame(root.MusicSource, bells);
            Assert.AreSame(WinterBank.ChristmasBells, bells.clip);
            Assert.IsTrue(bells.loop);
            root.UpdateWinter(WinterBank.BellsFadeSeconds);
            yield return null;
            Assert.AreEqual(1f, root.BellsGain, 1e-4f, "faded in");
            Assert.Greater(bells.volume, 0f);
            Assert.Less(bells.volume, root.MusicSource.volume, "a layer under the song");
            Assert.AreEqual(root.MusicSource.pitch, bells.pitch, 1e-4f, "same pitch as the song");
            if (AudioSettings.outputSampleRate > 0 && root.MusicSource.isPlaying)
            {
                yield return new WaitForSecondsRealtime(AudioRoot.BellsSyncInterval + 0.1f);
                float d = WinterRules.BellsResync(root.MusicSource.time, bells.time, bells.clip.length, 0.06f);
                Assert.AreEqual(0f, d, "on the song's grid");
            }

            // the unlucky dip bends both
            GameServices.Events.Publish(new LuckJudged(new GuestId(3), LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, new FloorIndex(3)));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.Less(root.MusicSource.pitch, 1f, "dip");
            Assert.AreEqual(root.MusicSource.pitch, bells.pitch, 1e-4f, "bells dip with the song");

            // pause: ducked with the music
            float full = bells.volume;
            var menu = GameServices.Get<IMenuFlow>();
            yield return new WaitForSecondsRealtime(0.8f);
            full = bells.volume;
            Assert.IsTrue(menu.SetPaused(true));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsTrue(root.BellsOn, "keeps ringing under the pause menu");
            Assert.Less(bells.volume, full * 0.5f, "ducked");
            Assert.IsTrue(menu.SetPaused(false));
            yield return new WaitForSecondsRealtime(0.6f);

            // a winter ending takes the music away: the bells leave with the song
            var day = GameServices.Get<IDayQuery>();
            GameServices.Events.Publish(new EndingReached(EndingKind.WinterMiracle, day.CurrentDay, 90, 500));
            yield return null;
            Assert.AreEqual(MusicTrack.WinterBells, root.CurrentTrack);
            yield return new WaitForSecondsRealtime(root.Library.crossfadeSeconds + 0.3f);
            root.UpdateWinter(WinterBank.BellsFadeSeconds);
            yield return null;
            Assert.IsFalse(root.BellsOn, "no bell layer on the ending music");
        }

        [UnityTest]
        public IEnumerator WinterEndings_PlayTheirProceduralLoops()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.3f);
            var day = GameServices.Get<IDayQuery>();
            var elevator = root.MusicSource;
            GameServices.Events.Publish(new EndingReached(EndingKind.WinterMiracle, day.CurrentDay, 90, 500));
            yield return null;
            Assert.AreEqual(MusicTrack.WinterBells, root.CurrentTrack);
            Assert.AreSame(root.Library.FindAny(MusicTrack.WinterBells).clip, root.MusicSource.clip);
            Assert.AreNotSame(elevator, root.MusicSource, "crossfade");
            Assert.IsTrue(root.MusicStarted);
            Assert.IsTrue(root.MusicSource.loop);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(root.MusicSource.volume, 0.02f, "faded in");
            Assert.AreEqual(1f, root.MusicSource.pitch, 0.02f);

            GameServices.Events.Publish(new EndingReached(EndingKind.SilentNight, day.CurrentDay, 50, 300));
            yield return null;
            Assert.AreEqual(MusicTrack.MusicBox, root.CurrentTrack);
            Assert.AreSame(root.Library.FindAny(MusicTrack.MusicBox).clip, root.MusicSource.clip);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(root.MusicSource.volume, 0.01f, "music box faded in");
            Assert.AreEqual(root.UnduckedMusicVolume, root.MusicSource.volume, 1e-3f, "level from the winter table");
        }

        [UnityTest]
        public IEnumerator SantaCarolersLotterySeason_PlayTheirCues()
        {
            yield return LoadMain();
            var root = Root();
            yield return new WaitForSecondsRealtime(0.2f);
            root.ClearRecords();

            // Santa: sleigh bells on arrival and again on boarding (IGuestQuery does not know the fake guest: remembered)
            root.OnGuestSpawned(Spawn(901, "santa"));
            AssertPlayed(root, AudioCue.SleighBells);
            yield return new WaitForSecondsRealtime(root.Library.FindAny(AudioCue.SleighBells).minInterval + 0.05f);
            root.OnGuestBoarded(new GuestBoarded(new GuestId(901), new FloorIndex(0), 1));
            Assert.AreEqual(2, CountOf(root, AudioCue.SleighBells), "boarding jingles again");

            // carolers: silent arrival, ONE handbell run for the party of three boarding
            for (int i = 0; i < 3; i++) root.OnGuestSpawned(Spawn(910 + i, "carolers"));
            Assert.AreEqual(0, CountOf(root, AudioCue.CarolChime));
            for (int i = 0; i < 3; i++)
            {
                root.OnGuestBoarded(new GuestBoarded(new GuestId(910 + i), new FloorIndex(0), 2 + i));
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Assert.AreEqual(1, CountOf(root, AudioCue.CarolChime), "one run per party");
            AssertPlayed(root, AudioCue.CarolChime);

            // anyone else: silent
            root.OnGuestSpawned(Spawn(920, "solo_traveler"));
            root.OnGuestBoarded(new GuestBoarded(new GuestId(920), new FloorIndex(0), 5));
            root.OnGuestBoarded(new GuestBoarded(new GuestId(4242), new FloorIndex(0), 5));
            Assert.AreEqual(2, CountOf(root, AudioCue.SleighBells));
            Assert.AreEqual(1, CountOf(root, AudioCue.CarolChime));

            // lottery: roll + ding per draw; a party winning in the same frame = one jackpot shower
            root.OnLotteryDrawn(new LotteryDrawn(7, 0, 2));
            AssertPlayed(root, AudioCue.LotteryDraw);
            for (int i = 0; i < 3; i++) root.OnLotteryWon(new LotteryWon(new GuestId(930 + i), new FloorIndex(6), 7));
            Assert.AreEqual(1, CountOf(root, AudioCue.LotteryJackpot), "one shower for the party");
            AssertPlayed(root, AudioCue.LotteryJackpot);
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.LotteryJackpot, out var jack));
            GameServices.Events.Publish(new GuestDelivered(new GuestId(930), new FloorIndex(6), 120));
            Assert.IsTrue(root.TryGetLastPlayed(AudioCue.Tip, out var tip), "the tip still plays after it");
            Assert.AreNotSame(tip.Clip, jack.Clip);

            // the season goes on: stinger + the remaining clips get synthesized outside Running
            root.OnSeasonExtended(new SeasonExtended(new DayId(9), 3));
            AssertPlayed(root, AudioCue.WinterStart);
            Assert.IsTrue(root.Prewarming || WinterBank.Pending == 0);
        }

        [UnityTest]
        public IEnumerator Paused_WinterCuesAreSuppressed()
        {
            yield return LoadMain();
            var root = Root();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            yield return null;
            var menu = GameServices.Get<IMenuFlow>();
            Assert.IsTrue(menu.SetPaused(true));
            yield return null;
            root.ClearRecords();
            root.OnLotteryDrawn(new LotteryDrawn(8, 1, 2));
            root.OnGuestSpawned(Spawn(950, "santa"));
            Assert.AreEqual(0, CountOf(root, AudioCue.LotteryDraw));
            Assert.AreEqual(0, CountOf(root, AudioCue.SleighBells));
            Assert.IsTrue(menu.SetPaused(false));
        }
    }
}
