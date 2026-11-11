using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>
    /// Winter season (ISeasonQuery, days 8-10). Without the service (Gameplay not providing it yet) all of it is silent.
    /// - Snow days (SnowAmount &gt; 0): a soft wind loop on its own voice instead of rain, same screens as the rain
    ///   (<see cref="WinterRules.WindAudible"/>), level rising with the snow, ducked with the music on pause / hold.
    /// - Christmas Eve: one bar of sleigh bells looped on its own voice ON the elevator song - started at the song's
    ///   position mod one bar, same pitch (so the unlucky dip bends both), the song's level / crossfade / seam fade,
    ///   re-synced if it drifts (<see cref="WinterRules.BellsResync"/>). Santa jingles on spawn / boarding, the
    ///   carolers ring a handbell run when they board.
    /// - LotteryDrawn: roll + ding; LotteryWon: jackpot shower; SeasonExtended: the winter stinger.
    /// - Endings WinterMiracle / SilentNight: procedural tracks via <see cref="AudioRules.MusicFor"/>.
    /// Clips come from <see cref="WinterBank"/>: synthesized once, one per frame outside Running after the season is
    /// extended (so no winter day pays for them), or on first use.
    /// </summary>
    sealed partial class AudioRoot
    {
        /// <summary>The bell layer is checked against the song this often (s) and jumps back if more than this (s) off.</summary>
        internal const float BellsSyncInterval = 0.5f;
        internal const float BellsSyncTolerance = 0.04f;

        readonly RainBed wind = new RainBed();
        AudioSource windSource;
        bool windOn;
        /// <summary>WinterRules.WindLevel of the snow while wanted (kept while it fades out).</summary>
        float windLevel;
        AudioSource bellsSource;
        bool bellsOn;
        float bellsGain;
        float bellsNextSync;
        bool prewarm;
        /// <summary>Santa / carolers by guest (from GuestSpawned) in case IGuestQuery no longer knows them at boarding.</summary>
        readonly Dictionary<GuestId, GuestArchetypeId> specialGuests = new Dictionary<GuestId, GuestArchetypeId>();

        // ------------------------------------------------------------------ test hooks (internal)

        /// <summary>Test override of the ISeasonQuery service (null = ask GameServices).</summary>
        internal ISeasonQuery SeasonOverride { get; set; }
        internal ISeasonQuery Season => SeasonOverride ?? (GameServices.TryGet<ISeasonQuery>(out var s) ? s : null);
        internal AudioSource WindSource => windSource;
        internal bool WindOn => windOn;
        internal float WindGain => wind.Gain;
        internal bool WindWanted => wind.Wanted;
        /// <summary>Wind level without the fade: master x wind row volume x snow level x music duck.</summary>
        internal float WindFullVolume => master * WinterBank.WindVolume * windLevel * duck;
        internal AudioSource BellsSource => bellsSource;
        internal bool BellsOn => bellsOn;
        internal float BellsGain => bellsGain;
        /// <summary>Times the bell layer jumped back onto the song's grid (since the last ClearRecords).</summary>
        internal int BellsResyncs { get; private set; }
        internal bool Prewarming => prewarm;

        // ------------------------------------------------------------------ setup

        void AwakeWinter()
        {
            var windGo = new GameObject("Winter Wind");
            windGo.transform.SetParent(transform, false);
            windSource = MakeSource(windGo, loop: true, priority: 64);
            var bellsGo = new GameObject("Christmas Bells");
            bellsGo.transform.SetParent(transform, false);
            bellsSource = MakeSource(bellsGo, loop: true, priority: 16);
        }

        void SubscribeWinter(IEventBus bus)
        {
            subs.Add(bus.Subscribe<SeasonExtended>(OnSeasonExtended));
            subs.Add(bus.Subscribe<LotteryDrawn>(OnLotteryDrawn));
            subs.Add(bus.Subscribe<LotteryWon>(OnLotteryWon));
            subs.Add(bus.Subscribe<GuestSpawned>(OnGuestSpawned));
            subs.Add(bus.Subscribe<GuestBoarded>(OnGuestBoarded));
            subs.Add(bus.Subscribe<DayStarted>(_ => specialGuests.Clear()));
        }

        void StartWinter()
        {
            var season = Season;
            if (season != null && season.IsSeasonExtended) prewarm = true;
        }

        // handlers are internal: tests call them directly (publishing fake guests / draws would reach Gameplay / UI too)

        internal void OnSeasonExtended(SeasonExtended e)
        {
            prewarm = true;
            Play(AudioEventMap.For(e));
        }

        internal void OnLotteryDrawn(LotteryDrawn e) => Play(AudioEventMap.For(e));

        internal void OnLotteryWon(LotteryWon e) => Play(AudioEventMap.For(e));

        internal void OnGuestSpawned(GuestSpawned e)
        {
            var cue = AudioEventMap.ForSpawn(e.Archetype);
            if (AudioEventMap.ForBoarding(e.Archetype) != AudioCue.None) specialGuests[e.Guest] = e.Archetype;
            Play(cue);
        }

        internal void OnGuestBoarded(GuestBoarded e)
        {
            GuestArchetypeId archetype = default;
            if (GameServices.TryGet<IGuestQuery>(out var guests) && guests.TryGet(e.Guest, out var info)) archetype = info.Archetype;
            else specialGuests.TryGetValue(e.Guest, out archetype);
            Play(AudioEventMap.ForBoarding(archetype));
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Wind, the Christmas bell layer and clip prewarming. Call after UpdateMusic (uses its duck / channels).</summary>
        internal void UpdateWinter(float dt)
        {
            var season = Season;
            if (season != null && (season.IsSeasonExtended || season.IsExtendedDay)) prewarm = true;
            // one clip per frame, never during a running day (a lazy first use covers anything still missing)
            if (prewarm && phase != DayPhase.Running && !WinterBank.PrewarmOne()) prewarm = false;
            UpdateWind(dt, season);
            UpdateBells(dt, season);
        }

        void UpdateWind(float dt, ISeasonQuery season)
        {
            float snow = season != null ? season.SnowAmount : 0f;
            bool want = WinterRules.WindAudible(snow, phase) && unlock.IsUnlocked;
            if (want) windLevel = WinterRules.WindLevel(snow);
            wind.Step(dt, want, WinterBank.WindFadeInSeconds, WinterBank.WindFadeOutSeconds);
            if (wind.Active && !windOn)
            {
                windSource.clip = WinterBank.WindLoop;
                windSource.time = 0f;
                windSource.Play();
                windOn = true;
            }
            else if (!wind.Active && windOn)
            {
                windSource.Stop();
                windOn = false;
            }
            ApplyWindVolume();
        }

        /// <summary>The music channel playing the elevator song (the active one first), -1 if none.</summary>
        int ElevatorChannel()
        {
            if (music[activeMusic].Track == MusicTrack.Elevator && music[activeMusic].Started) return activeMusic;
            int other = activeMusic ^ 1;
            return music[other].Track == MusicTrack.Elevator && music[other].Started ? other : -1;
        }

        void UpdateBells(float dt, ISeasonQuery season)
        {
            int ch = ElevatorChannel();
            bool christmas = season != null && season.IsChristmas;
            bool want = unlock.IsUnlocked && ch == activeMusic && ch >= 0
                        && WinterRules.ChristmasBellsAudible(christmas, phase, music[activeMusic].Track);
            bellsGain = Mathf.MoveTowards(bellsGain, want ? 1f : 0f, dt / Mathf.Max(0.01f, WinterBank.BellsFadeSeconds));
            if (want && !bellsOn)
            {
                var song = music[ch].Source;
                bellsSource.clip = WinterBank.ChristmasBells;
                bellsSource.pitch = song.pitch;
                bellsSource.time = SongLoaded(song) ? Mathf.Repeat(song.time, bellsSource.clip.length) : 0f;
                bellsSource.Play();
                bellsOn = true;
                bellsNextSync = Time.unscaledTime + BellsSyncInterval;
            }
            else if (bellsOn && (ch < 0 || (!want && bellsGain <= 0f)))
            {
                bellsSource.Stop();
                bellsOn = false;
                bellsGain = 0f;
            }
            if (!bellsOn) { bellsSource.volume = 0f; return; }

            var src = music[ch].Source;
            bellsSource.pitch = src.pitch;
            if (Time.unscaledTime >= bellsNextSync)
            {
                bellsNextSync = Time.unscaledTime + BellsSyncInterval;
                if (SongLoaded(src) && src.isPlaying && bellsSource.isPlaying)
                {
                    float len = bellsSource.clip.length;
                    if (WinterRules.BellsResync(src.time, bellsSource.time, len, BellsSyncTolerance) != 0f)
                    {
                        bellsSource.time = Mathf.Repeat(src.time, len);
                        BellsResyncs++;
                    }
                }
            }
            ApplyBellsVolume(ch);
        }

        static bool SongLoaded(AudioSource s) => s.clip != null && s.clip.loadState == AudioDataLoadState.Loaded;

        void ApplyWindVolume()
        {
            if (windSource == null) return;
            windSource.volume = windOn ? WindFullVolume * wind.Gain : 0f;
        }

        /// <summary>The layer follows the song it rides on: its level, crossfade, loop-seam fade and the music duck.</summary>
        void ApplyBellsVolume(int ch)
        {
            var c = music[ch];
            var def = lib.FindAny(c.Track);
            float seam = def != null && SongLoaded(c.Source) ? AudioRules.SeamGain(c.Source.time, c.Source.clip.length, def.seamFade) : 1f;
            bellsSource.volume = master * WinterBank.ChristmasBellsVolume * c.Level * c.Fade * duck * seam * bellsGain;
        }

        void ApplyWinterVolumes()
        {
            ApplyWindVolume();
            if (bellsSource == null) return;
            int ch = ElevatorChannel();
            if (bellsOn && ch >= 0) ApplyBellsVolume(ch);
        }
    }
}
