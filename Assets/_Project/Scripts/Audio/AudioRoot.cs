using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>One SFX request that actually started (test hook: batch mode may have no audio device, so tests check this).</summary>
    readonly struct CueRecord
    {
        public readonly AudioCue Cue;
        public readonly AudioClip Clip;
        public readonly float Pitch;
        public readonly float Volume;
        public readonly float Time;
        public CueRecord(AudioCue cue, AudioClip clip, float pitch, float volume, float time)
        { Cue = cue; Clip = clip; Pitch = pitch; Volume = volume; Time = time; }
    }

    /// <summary>
    /// Audio module root (one per Main scene, see AudioBootstrap). Subscribes to Contracts events only:
    /// SFX pool (2D, random pitch, per-cue spam guard) + two music sources (crossfade) whose level/pitch follow the phase,
    /// the unlucky pitch dip, pause / Day 0 coach-mark ducking, the lucky-streak ladder, the last-seconds clock ticks,
    /// MasterVolume and the WebGL gesture lock. All timing uses unscaled time (the clock ticks follow the day clock).
    /// The AudioListener is on the scene's Main Camera.
    /// </summary>
    sealed class AudioRoot : MonoBehaviour
    {
        internal const string RootName = "[Audio]";
        internal const int SfxVoices = 12;
        internal const int MaxRecords = 128;

        sealed class MusicChannel
        {
            public AudioSource Source;
            public MusicTrack Track;
            public bool Started;
            public float Fade;
            public float FadeTarget;
            public float Level;
        }

        readonly Subscriptions subs = new Subscriptions();
        readonly CueThrottle throttle = new CueThrottle();
        readonly System.Random rng = new System.Random();
        readonly List<CueRecord> played = new List<CueRecord>();
        readonly MusicChannel[] music = new MusicChannel[2];
        readonly CountdownTicker countdown = new CountdownTicker();

        AudioLibrary lib;
        AudioSource[] sfx;
        float[] sfxBaseVolume;
        int nextVoice;
        int activeMusic;
        MusicSpec spec = new MusicSpec(MusicTrack.None, 0f, 1f);
        float basePitch = 1f;
        float duck = 1f;
        PitchDip dip;
        AudioUnlock unlock;
        float master = 1f;
        bool paused;
        bool held;
        /// <summary>Highest streak reached since the last StreakUp note (same-frame increases = one note, the top one).</summary>
        int pendingStreak;
        DayPhase phase = DayPhase.None;
        EndingKind ending = EndingKind.None;

        // ------------------------------------------------------------------ test hooks (internal)
        internal AudioLibrary Library => lib;
        internal IReadOnlyList<CueRecord> Played => played;
        internal int SuppressedCount { get; private set; }
        internal bool IsUnlocked => unlock != null && unlock.IsUnlocked;
        internal AudioSource MusicSource => music[activeMusic]?.Source;
        internal MusicTrack CurrentTrack => music[activeMusic] != null ? music[activeMusic].Track : MusicTrack.None;
        /// <summary>True once the active track was actually started (Play called) - false while the gesture lock holds it.</summary>
        internal bool MusicStarted => music[activeMusic] != null && music[activeMusic].Started;
        internal MusicSpec Music => spec;
        internal float DuckGain => duck;
        internal float MasterVolume => master;
        internal bool Paused => paused;
        internal bool TutorialHeld => held;
        internal bool DipActive => dip.Active;
        internal IReadOnlyList<AudioSource> SfxSources => sfx;
        internal void ClearRecords() { played.Clear(); SuppressedCount = 0; throttle.Clear(); pendingStreak = 0; }

        internal bool TryGetLastPlayed(AudioCue cue, out CueRecord record)
        {
            for (int i = played.Count - 1; i >= 0; i--)
                if (played[i].Cue == cue) { record = played[i]; return true; }
            record = default;
            return false;
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            lib = AudioLibrary.Load();
            if (lib == null)
            {
                Debug.LogError("[Audio] Resources/" + AudioLibrary.ResourcePath + " missing - run FloorF.EditorTools.AudioLibraryBuilder.Build");
                lib = ScriptableObject.CreateInstance<AudioLibrary>();
            }
            unlock = new AudioUnlock(AudioUnlock.DefaultRequiresGesture);

            var sfxRoot = new GameObject("SFX").transform;
            sfxRoot.SetParent(transform, false);
            sfx = new AudioSource[SfxVoices];
            sfxBaseVolume = new float[SfxVoices];
            for (int i = 0; i < SfxVoices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(sfxRoot, false);
                sfx[i] = MakeSource(go, loop: false, priority: 128);
            }
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "Music A" : "Music B");
                go.transform.SetParent(transform, false);
                music[i] = new MusicChannel { Source = MakeSource(go, loop: true, priority: 0) };
            }
        }

        static AudioSource MakeSource(GameObject go, bool loop, int priority)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.priority = priority;
            s.volume = 0f;
            return s;
        }

        void OnEnable()
        {
            var bus = GameServices.Events;
            subs.Add(bus.Subscribe<ElevatorArrived>(_ => Play(AudioCue.Ding)));
            subs.Add(bus.Subscribe<DoorsOpened>(_ => Play(AudioCue.DoorsOpen)));
            subs.Add(bus.Subscribe<DoorsClosed>(_ => Play(AudioCue.DoorsClose)));
            subs.Add(bus.Subscribe<LuckJudged>(OnLuckJudged));
            subs.Add(bus.Subscribe<TipsChanged>(e => Play(AudioEventMap.For(e))));
            subs.Add(bus.Subscribe<GuestDelivered>(_ => Play(AudioCue.Tip)));
            subs.Add(bus.Subscribe<StickerApplied>(_ => Play(AudioCue.Sticker)));
            subs.Add(bus.Subscribe<GuestLeft>(e => Play(AudioEventMap.For(e))));
            subs.Add(bus.Subscribe<GuestMoodChanged>(e => Play(AudioEventMap.For(e))));
            subs.Add(bus.Subscribe<DayStarted>(_ => Play(AudioCue.DayStart)));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnPhaseChanged));
            subs.Add(bus.Subscribe<EndingReached>(OnEnding));
            subs.Add(bus.Subscribe<PauseChanged>(OnPause));
            subs.Add(bus.Subscribe<LuckStreakChanged>(OnStreak));
            subs.Add(bus.Subscribe<TutorialHoldChanged>(e => held = e.Held));
            subs.Add(bus.Subscribe<SettingsChanged>(e => SetMaster(e.MasterVolume)));
            subs.Add(bus.Subscribe<UiSound>(e => Play(e.Kind == UiSoundKind.Error ? AudioCue.Error : e.Kind == UiSoundKind.Confirm ? AudioCue.DayStart : AudioCue.UiClick)));
        }

        void OnDisable() => subs.Clear();

        void Start()
        {
            if (GameServices.TryGet<IGameSettings>(out var settings)) SetMaster(settings.MasterVolume);
            if (GameServices.TryGet<IMenuFlow>(out var menu)) paused = menu.IsPaused;
            if (GameServices.TryGet<IGameFlow>(out var flow)) held = flow.TutorialHold;
            if (GameServices.TryGet<IDayQuery>(out var day)) { phase = day.Phase; ending = day.Ending; }
            if (spec.Track == MusicTrack.None) SetMusic(AudioRules.MusicFor(phase, ending));
        }

        void Update()
        {
            if (unlock.Poll()) OnUnlocked();
            if (GameServices.TryGet<IDayQuery>(out var day)) TickCountdown(day.Phase, day.IsTutorial, day.RemainingSeconds);
            UpdateMusic(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>After every Update (Core ticks in Gameplay's Update): one StreakUp note for this frame's increases.</summary>
        void LateUpdate() => FlushStreak();

        internal void FlushStreak()
        {
            if (pendingStreak <= 0) return;
            int streak = pendingStreak;
            pendingStreak = 0;
            Play(AudioCue.StreakUp, 1f, AudioRules.StreakPitch(streak, lib.streakSemitoneStep, lib.streakMaxSemitones));
        }

        /// <summary>Last-seconds ticks: real days only (no tutorial, no title demo - its visible phase is Title), not paused.</summary>
        internal void TickCountdown(DayPhase visiblePhase, bool tutorial, float remaining)
        {
            int window = lib.clockTickSeconds;
            int sec = countdown.Step(visiblePhase == DayPhase.Running, paused || tutorial, remaining, window);
            if (sec > 0) Play(AudioCue.ClockTick, 1f, AudioRules.ClockTickPitch(sec, window, lib.clockTickRise));
        }

        // ------------------------------------------------------------------ events

        void OnLuckJudged(LuckJudged e)
        {
            var cue = AudioEventMap.For(e);
            Play(cue);
            if (cue == AudioCue.Unlucky && !paused) dip.Trigger(Time.unscaledTime);
        }

        void OnStreak(LuckStreakChanged e)
        {
            switch (AudioEventMap.For(e))
            {
                case AudioCue.StreakUp:
                    pendingStreak = Mathf.Max(pendingStreak, e.Streak);
                    break;
                case AudioCue.StreakBroken:
                    pendingStreak = 0;
                    Play(AudioCue.StreakBroken);
                    break;
            }
        }

        void OnPhaseChanged(DayPhaseChanged e)
        {
            if (AudioRules.IsPlayerDrivenTransition(e.Previous, e.Current)) Unlock();
            phase = e.Current;
            if (phase != DayPhase.Running) held = false; // leaving Running always releases the coach-mark hold
            if (phase == DayPhase.Ending && GameServices.TryGet<IDayQuery>(out var day)) ending = day.Ending;
            else if (phase != DayPhase.Ending) ending = EndingKind.None;
            SetMusic(AudioRules.MusicFor(phase, ending));
        }

        void OnEnding(EndingReached e)
        {
            ending = e.Ending;
            phase = DayPhase.Ending;
            SetMusic(AudioRules.MusicFor(phase, ending));
        }

        void OnPause(PauseChanged e)
        {
            Unlock(); // pausing is always the player's doing
            paused = e.Paused;
            if (!paused) return;
            foreach (var s in sfx) s.Stop();
            dip.Clear();
        }

        void SetMaster(float v)
        {
            master = Mathf.Clamp01(v);
            for (int i = 0; i < sfx.Length; i++) sfx[i].volume = sfxBaseVolume[i] * master;
            UpdateMusic(Time.unscaledTime, 0f);
        }

        void Unlock()
        {
            if (unlock.Unlock()) OnUnlocked();
        }

        void OnUnlocked()
        {
            var ch = music[activeMusic];
            if (!ch.Started) StartChannel(ch);
        }

        // ------------------------------------------------------------------ SFX

        /// <param name="exactPitch">
        /// &gt; 0: play at def.pitch x this, WITHOUT the random jitter (pitch ladders: streak, clock ticks). 0 = jittered.
        /// </param>
        internal void Play(AudioCue cue, float scale = 1f, float exactPitch = 0f)
        {
            if (cue == AudioCue.None) return;
            if (!unlock.IsUnlocked || paused) { SuppressedCount++; return; }
            var def = lib.Find(cue);
            if (def == null || def.clips == null || def.clips.Length == 0) return;
            float now = Time.unscaledTime;
            if (!throttle.TryConsume(cue, now, def.minInterval)) return;
            var clip = def.clips[rng.Next(def.clips.Length)];
            if (clip == null) return;
            float pitch = exactPitch > 0f ? def.pitch * exactPitch : AudioRules.PickPitch(def.pitch, def.pitchJitter, rng);
            float vol = def.volume * scale * (phase == DayPhase.Title ? lib.titleSfxScale : 1f);
            PlayVoice(clip, vol, pitch);
            if (def.layer != null) PlayVoice(def.layer, vol * def.layerVolume, pitch);
            if (played.Count >= MaxRecords) played.RemoveAt(0);
            played.Add(new CueRecord(cue, clip, pitch, vol * master, now));
        }

        void PlayVoice(AudioClip clip, float volume, float pitch)
        {
            int pick = -1;
            for (int k = 0; k < sfx.Length; k++)
            {
                int i = (nextVoice + k) % sfx.Length;
                if (!sfx[i].isPlaying) { pick = i; break; }
            }
            if (pick < 0) pick = nextVoice;
            nextVoice = (pick + 1) % sfx.Length;
            var s = sfx[pick];
            s.Stop();
            s.clip = clip;
            s.pitch = pitch;
            sfxBaseVolume[pick] = volume;
            s.volume = volume * master;
            s.Play();
        }

        // ------------------------------------------------------------------ music

        void SetMusic(MusicSpec next)
        {
            spec = next;
            var act = music[activeMusic];
            if (act.Track == next.Track) return; // same song keeps playing; level/pitch glide in UpdateMusic
            if (act.Started)
            {
                act.FadeTarget = 0f;
                activeMusic ^= 1;
            }
            var ch = music[activeMusic];
            ch.Source.Stop();
            ch.Started = false;
            ch.Track = next.Track;
            ch.Source.clip = lib.Find(next.Track)?.clip;
            ch.Fade = 0f;
            ch.FadeTarget = 1f;
            ch.Level = next.Volume;
            if (unlock.IsUnlocked) StartChannel(ch);
        }

        static void StartChannel(MusicChannel ch)
        {
            if (ch.Source.clip == null || ch.Track == MusicTrack.None) return;
            ch.Source.time = 0f;
            ch.Source.Play();
            ch.Started = true;
        }

        void UpdateMusic(float now, float dt)
        {
            duck = Mathf.MoveTowards(duck, AudioRules.DuckTarget(paused, held, lib.pauseDuckDb, lib.holdDuckDb), dt * 4f);
            basePitch = Mathf.MoveTowards(basePitch, spec.Pitch, dt * 0.5f);
            float pitch = basePitch * dip.Evaluate(now, lib.dipPitch, lib.dipDownSeconds, lib.dipUpSeconds);
            float fadeStep = dt / Mathf.Max(0.01f, lib.crossfadeSeconds);
            for (int i = 0; i < music.Length; i++)
            {
                var ch = music[i];
                var src = ch.Source;
                ch.Fade = Mathf.MoveTowards(ch.Fade, ch.FadeTarget, fadeStep);
                if (i == activeMusic) ch.Level = Mathf.MoveTowards(ch.Level, spec.Volume, dt);
                if (i != activeMusic && ch.Started && ch.Fade <= 0f)
                {
                    src.Stop();
                    ch.Started = false;
                    ch.Track = MusicTrack.None;
                }
                var def = lib.Find(ch.Track);
                float seam = def != null && src.clip != null ? AudioRules.SeamGain(src.time, src.clip.length, def.seamFade) : 1f;
                float trackVol = def != null ? def.volume : 1f;
                src.volume = master * lib.musicVolume * trackVol * ch.Level * ch.Fade * duck * seam;
                src.pitch = pitch;
            }
        }

        /// <summary>Music volume the active source would have without pause ducking and the loop-seam fade (tests).</summary>
        internal float UnduckedMusicVolume
        {
            get
            {
                var ch = music[activeMusic];
                var def = lib.Find(ch.Track);
                return master * lib.musicVolume * (def != null ? def.volume : 1f) * ch.Level * ch.Fade;
            }
        }
    }
}
