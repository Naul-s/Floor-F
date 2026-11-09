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
    /// Door cues have their own voice pair (never stacked: a new door cue fades the previous one out in
    /// <see cref="AudioLibrary.doorCutFadeSeconds"/>) and are played lower right after the arrival ding.
    /// The elevator travel hum has one dedicated looping voice (<see cref="TravelHum"/>): in on ElevatorDeparted, out
    /// before the predicted arrival (speed measured from IElevatorQuery.Position), paused with the game / coach-mark hold.
    /// Rainy days (<see cref="WeatherRules.RainAudible"/>): a looping rain bed on its own voice fading with the day, plus
    /// distant thunder on another own voice on a seeded schedule (<see cref="ThunderSchedule"/>, never two at once);
    /// both are ducked with the music on pause / coach-mark hold and no new rumble is scheduled while paused.
    /// UI feedback (UiSound) opens the gesture lock, plays while paused and is not title-scaled; taps go through a
    /// <see cref="TapGuard"/>. Scenic view (ScenicViewChanged): the player's scenic mode pauses the game but keeps the
    /// music/rain unducked and plays SFX <see cref="AudioLibrary.scenicSfxDb"/> lower; the day's establishing shot
    /// leaves the levels alone and whooshes softly as it zooms back in.
    /// The AudioListener is on the scene's Main Camera.
    /// </summary>
    sealed class AudioRoot : MonoBehaviour
    {
        internal const string RootName = "[Audio]";
        /// <summary>Pooled SFX voices (round-robin); the door voices come after them in <see cref="SfxSources"/>.</summary>
        internal const int SfxVoices = 12;
        /// <summary>Door voice pair: the new door cue starts on one while the cut previous one fades out on the other.</summary>
        internal const int DoorVoices = 2;
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
        /// <summary>Per voice: 1, or the remaining gain while a cut voice fades out (sfxCut).</summary>
        float[] sfxFade;
        bool[] sfxCut;
        /// <summary>Per voice: holds a UI feedback sound (keeps ringing when the game pauses).</summary>
        bool[] sfxUi;
        int nextVoice;
        /// <summary>Index (in sfx) of the door voice that played last, -1 before the first door cue.</summary>
        int doorVoice = -1;
        /// <summary>Unscaled time the last door cue's clip ends (length / pitch): "still sounding" even without an audio device.</summary>
        float doorEndsAt = float.NegativeInfinity;
        float lastDingTime = float.NegativeInfinity;
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
        /// <summary>ScenicViewChanged: the player's scenic mode (game paused, music kept) / the day's establishing shot.</summary>
        bool scenic;
        bool establishing;
        readonly TapGuard tapGuard = new TapGuard();

        readonly TravelHum hum = new TravelHum();
        AudioSource humSource;
        /// <summary>Play() was called on the hum voice and it was not stopped since (isPlaying is false without a device).</summary>
        bool humOn;
        bool humPaused;
        /// <summary>Car speed (floors per unscaled second) measured from IElevatorQuery.Position; 0 = not measured yet.</summary>
        float carSpeed;
        float lastCarPos;
        bool haveCarPos;
        /// <summary>IElevatorQuery reported the car moving during the current trip (so Idle afterwards = arrived/reset).</summary>
        bool carSeenMoving;
        float humEta = -1f;

        readonly RainBed rain = new RainBed();
        readonly ThunderSchedule thunder = new ThunderSchedule();
        AudioSource rainSource;
        AudioSource thunderSource;
        bool rainOn;
        /// <summary>Volume of the current rumble before master / duck / rain fade (its cue volume).</summary>
        float thunderLevel;
        /// <summary>Unscaled time the current rumble ends (length / pitch): "still sounding" even without an audio device.</summary>
        float thunderEndsAt = float.NegativeInfinity;

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
        /// <summary>The door voice holding the latest door cue (null before the first).</summary>
        internal AudioSource DoorSource => doorVoice >= 0 ? sfx[doorVoice] : null;
        /// <summary>Door cues that cut a still-sounding previous door cue (since the last ClearRecords).</summary>
        internal int DoorCutCount { get; private set; }
        internal bool IsCutFading(AudioSource s)
        {
            int i = System.Array.IndexOf(sfx, s);
            return i >= 0 && sfxCut[i];
        }
        internal void ClearRecords()
        {
            played.Clear(); SuppressedCount = 0; throttle.Clear(); pendingStreak = 0;
            DoorCutCount = 0; lastDingTime = float.NegativeInfinity; HumStarts = 0;
            ThunderStrikes = 0; ThunderSkipped = 0;
            tapGuard.Clear(); TapsDropped = 0;
        }

        /// <summary>The player's scenic mode is on (music not pause-ducked, SFX lowered).</summary>
        internal bool Scenic => scenic;
        /// <summary>The day's establishing shot is on (levels untouched; its end whooshes).</summary>
        internal bool Establishing => establishing;
        /// <summary>UiSound(Tap)s dropped by the spam guard (since the last ClearRecords).</summary>
        internal int TapsDropped { get; private set; }
        /// <summary>The voice holds a UI feedback sound (pause does not stop it).</summary>
        internal bool IsUiVoice(AudioSource s)
        {
            int i = System.Array.IndexOf(sfx, s);
            return i >= 0 && sfxUi[i];
        }
        /// <summary>Current SFX level multiplier (title demo / scenic) for a game (ui = false) or UI sound.</summary>
        internal float SfxScale(bool ui)
            => AudioRules.SfxScale(phase == DayPhase.Title, ui, scenic, lib.titleSfxScale, lib.scenicSfxDb);

        /// <summary>The travel hum's own looping voice (never one of <see cref="SfxSources"/>).</summary>
        internal AudioSource HumSource => humSource;
        internal bool HumMoving => hum.Moving;
        internal float HumGain => hum.Gain;
        internal float HumPitch => hum.Pitch;
        internal bool HumOn => humOn;
        internal bool HumPaused => humPaused;
        /// <summary>Seconds to the predicted arrival (-1 = unknown).</summary>
        internal float HumEta => humEta;
        /// <summary>Play() calls on the hum voice (since the last ClearRecords): a re-departure while it sounds adds none.</summary>
        internal int HumStarts { get; private set; }
        /// <summary>Hum level without the fade: master x travelHumVolume x title scale.</summary>
        internal float HumFullVolume => master * lib.travelHumVolume * (phase == DayPhase.Title ? lib.titleSfxScale : 1f);

        /// <summary>Test override of IDayQuery.IsRainy (null = ask the day).</summary>
        internal bool? ForceRainy { get; set; }
        internal AudioSource RainSource => rainSource;
        internal AudioSource ThunderSource => thunderSource;
        internal float RainGain => rain.Gain;
        internal bool RainWanted => rain.Wanted;
        /// <summary>Play() was called on the rain voice and it was not stopped since.</summary>
        internal bool RainOn => rainOn;
        /// <summary>Rain level without the fade: master x rainVolume x music duck.</summary>
        internal float RainFullVolume => master * lib.rainVolume * duck;
        internal ThunderSchedule Thunder => thunder;
        /// <summary>Rumbles started / rumbles dropped because one was still sounding (since the last ClearRecords).</summary>
        internal int ThunderStrikes { get; private set; }
        internal int ThunderSkipped { get; private set; }
        internal bool ThunderSounding => Time.unscaledTime < thunderEndsAt;

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
            int voices = SfxVoices + DoorVoices;
            sfx = new AudioSource[voices];
            sfxBaseVolume = new float[voices];
            sfxFade = new float[voices];
            sfxCut = new bool[voices];
            sfxUi = new bool[voices];
            for (int i = 0; i < voices; i++)
            {
                sfxFade[i] = 1f;
                var go = new GameObject(i < SfxVoices ? "Voice " + i : "Door Voice " + (i - SfxVoices));
                go.transform.SetParent(sfxRoot, false);
                sfx[i] = MakeSource(go, loop: false, priority: 128);
            }
            var humGo = new GameObject("Travel Hum");
            humGo.transform.SetParent(transform, false);
            humSource = MakeSource(humGo, loop: true, priority: 64);
            humSource.clip = lib.travelHum;
            var rainGo = new GameObject("Rain");
            rainGo.transform.SetParent(transform, false);
            rainSource = MakeSource(rainGo, loop: true, priority: 64);
            rainSource.clip = lib.rainLoop;
            var thunderGo = new GameObject("Thunder");
            thunderGo.transform.SetParent(transform, false);
            thunderSource = MakeSource(thunderGo, loop: false, priority: 96);
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
            subs.Add(bus.Subscribe<ElevatorArrived>(_ => { hum.Arrive(); Play(AudioCue.Ding); }));
            subs.Add(bus.Subscribe<ElevatorDeparted>(_ => OnDeparted()));
            subs.Add(bus.Subscribe<WishStarAppeared>(_ => Play(AudioCue.WishStar)));
            subs.Add(bus.Subscribe<WishMade>(_ => Play(AudioCue.Wish)));
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
            subs.Add(bus.Subscribe<ShopRerolled>(_ => Play(AudioCue.ShopReroll)));
            subs.Add(bus.Subscribe<CharmBought>(_ => Play(AudioCue.CharmBought)));
            subs.Add(bus.Subscribe<CharmTriggered>(_ => Play(AudioCue.CharmShield)));
            subs.Add(bus.Subscribe<StickersExpired>(e => Play(AudioEventMap.For(e))));
            subs.Add(bus.Subscribe<UiSound>(OnUiSound));
            subs.Add(bus.Subscribe<ScenicViewChanged>(OnScenic));
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
            StepCutFades(Time.unscaledDeltaTime);
            UpdateHum(Time.unscaledDeltaTime);
            UpdateWeather(Time.unscaledDeltaTime);
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
            bool starNight = GameServices.TryGet<IDayQuery>(out var day) && day.IsStarNight;
            Play(AudioEventMap.StarNightLayer(cue, starNight));
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
            hum.Arrive(); // a new phase starts with the car parked (day start / end, title <-> game): fade the hum out
            if (phase != DayPhase.Running) held = false; // leaving Running always releases the coach-mark hold
            if (phase == DayPhase.Title) scenic = establishing = false; // back to the title: no scenic view survives
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
            ApplyHumFreeze();
            if (!paused) return;
            // game sounds stop; a UI sound (the pause button's own click + tink) rings out
            for (int i = 0; i < sfx.Length; i++)
            {
                if (sfxUi[i]) continue;
                sfx[i].Stop(); sfxCut[i] = false; sfxFade[i] = 1f;
            }
            doorEndsAt = float.NegativeInfinity;
            dip.Clear();
        }

        /// <summary>
        /// UI feedback: always the player's doing (opens the WebGL lock), audible while paused (pause menu, scenic mode).
        /// Taps go through the <see cref="TapGuard"/>; a button click tells the guard so a tap in the same gesture is dropped.
        /// </summary>
        void OnUiSound(UiSound e)
        {
            Unlock();
            var cue = AudioEventMap.For(e.Kind);
            float now = Time.unscaledTime, gain = 1f;
            if (cue == AudioCue.UiClick) tapGuard.NoteClick(now);
            else if (cue == AudioCue.Tap)
            {
                gain = tapGuard.TryTap(now, TapSettings.From(lib, lib.Find(AudioCue.Tap)));
                if (gain <= 0f) { TapsDropped++; return; }
            }
            Play(cue, gain, 0f, ui: true);
        }

        void OnScenic(ScenicViewChanged e)
        {
            if (e.Establishing) establishing = e.On;
            else scenic = e.On;
            Play(AudioEventMap.For(e)); // establishing shot zooming back in: soft whoosh (after the flag cleared: full level)
        }

        void SetMaster(float v)
        {
            master = Mathf.Clamp01(v);
            for (int i = 0; i < sfx.Length; i++) sfx[i].volume = sfxBaseVolume[i] * sfxFade[i] * master;
            UpdateMusic(Time.unscaledTime, 0f);
            ApplyWeatherVolumes();
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
        /// <param name="ui">UI feedback (UiSound): plays while paused, no title demo scale, its voice survives a pause.</param>
        internal void Play(AudioCue cue, float scale = 1f, float exactPitch = 0f, bool ui = false)
        {
            if (cue == AudioCue.None) return;
            if (!unlock.IsUnlocked || (paused && !ui)) { SuppressedCount++; return; }
            var def = lib.Find(cue);
            if (def == null || def.clips == null || def.clips.Length == 0) return;
            float now = Time.unscaledTime;
            if (!throttle.TryConsume(cue, now, def.minInterval)) return;
            var clip = def.clips[rng.Next(def.clips.Length)];
            if (clip == null) return;
            float pitch = exactPitch > 0f ? def.pitch * exactPitch : AudioRules.PickPitch(def.pitch, def.pitchJitter, rng);
            float vol = def.volume * scale * SfxScale(ui);
            if (cue == AudioCue.Ding) lastDingTime = now;
            if (AudioRules.IsDoorCue(cue))
            {
                vol *= AudioRules.DoorGainAfterDing(now - lastDingTime, lib.dingGuardSeconds, lib.doorUnderDingDb);
                PlayDoor(clip, vol, pitch, now);
            }
            else PlayVoice(clip, vol, pitch, ui);
            if (def.layer != null) PlayVoice(def.layer, vol * def.layerVolume, pitch, ui);
            if (played.Count >= MaxRecords) played.RemoveAt(0);
            played.Add(new CueRecord(cue, clip, pitch, vol * master, now));
        }

        void PlayVoice(AudioClip clip, float volume, float pitch, bool ui)
        {
            int pick = -1;
            for (int k = 0; k < SfxVoices; k++)
            {
                int i = (nextVoice + k) % SfxVoices;
                if (!sfx[i].isPlaying) { pick = i; break; }
            }
            if (pick < 0) pick = nextVoice;
            nextVoice = (pick + 1) % SfxVoices;
            StartVoice(pick, clip, volume, pitch);
            sfxUi[pick] = ui;
        }

        /// <summary>
        /// One door sound at a time: if the previous door cue is still sounding it is cut (fades out on its voice) while
        /// the new one starts on the other door voice.
        /// </summary>
        void PlayDoor(AudioClip clip, float volume, float pitch, float now)
        {
            int prev = doorVoice;
            int next = prev == SfxVoices ? SfxVoices + 1 : SfxVoices;
            if (prev >= 0 && now < doorEndsAt)
            {
                sfxCut[prev] = true;
                DoorCutCount++;
            }
            StartVoice(next, clip, volume, pitch);
            doorVoice = next;
            doorEndsAt = now + clip.length / Mathf.Max(0.01f, pitch);
        }

        void StartVoice(int i, AudioClip clip, float volume, float pitch)
        {
            var s = sfx[i];
            s.Stop();
            s.clip = clip;
            s.pitch = pitch;
            sfxBaseVolume[i] = volume;
            sfxFade[i] = 1f;
            sfxCut[i] = false;
            sfxUi[i] = false;
            s.volume = volume * master;
            s.Play();
        }

        /// <summary>Cut voices glide to 0 over doorCutFadeSeconds, then stop (no click, no stacking).</summary>
        internal void StepCutFades(float dt)
        {
            for (int i = 0; i < sfx.Length; i++)
            {
                if (!sfxCut[i]) continue;
                sfxFade[i] = AudioRules.StepCutFade(sfxFade[i], dt, lib.doorCutFadeSeconds);
                sfx[i].volume = sfxBaseVolume[i] * sfxFade[i] * master;
                if (sfxFade[i] > 0f) continue;
                sfx[i].Stop();
                sfxCut[i] = false;
                sfxFade[i] = 1f;
            }
        }

        // ------------------------------------------------------------------ travel hum

        void OnDeparted()
        {
            hum.Depart();
            carSeenMoving = false;
            haveCarPos = false;
        }

        /// <summary>Paused or coach-mark hold: the sim is frozen, so the hum voice pauses (state kept) and resumes after.</summary>
        void ApplyHumFreeze()
        {
            bool frozen = paused || held;
            if (frozen && humOn && !humPaused) { humSource.Pause(); humPaused = true; }
            else if (!frozen && humPaused) { humPaused = false; if (humOn) humSource.UnPause(); }
        }

        internal void UpdateHum(float dt)
        {
            ApplyHumFreeze();
            if (paused || held) { haveCarPos = false; return; }
            humEta = MeasureEta(dt);
            hum.Step(dt, humEta, unlock.IsUnlocked && lib.travelHum != null, TravelHumSettings.From(lib));
            if (hum.Active && !humOn)
            {
                humSource.clip = lib.travelHum;
                humSource.time = 0f;
                humSource.Play();
                humOn = true;
                HumStarts++;
            }
            else if (!hum.Active && humOn)
            {
                humSource.Stop();
                humOn = false;
            }
            humSource.volume = HumFullVolume * hum.Gain;
            humSource.pitch = hum.Pitch;
        }

        /// <summary>
        /// Seconds until the car arrives, from IElevatorQuery (distance to target / measured speed); -1 = unknown (no
        /// query, idle car, speed not measured yet). Also ends the trip if the car stopped without an ElevatorArrived
        /// (run reset, title demo restart).
        /// </summary>
        float MeasureEta(float dt)
        {
            if (!GameServices.TryGet<IElevatorQuery>(out var q)) return -1f;
            if (q.Motion == ElevatorMotion.Idle)
            {
                haveCarPos = false;
                if (carSeenMoving && hum.Moving) hum.Arrive();
                carSeenMoving = false;
                return -1f;
            }
            carSeenMoving = true;
            float pos = q.Position;
            if (haveCarPos && dt > 0f)
            {
                float d = Mathf.Abs(pos - lastCarPos);
                if (d > 1e-5f) carSpeed = carSpeed <= 0f ? d / dt : Mathf.Lerp(carSpeed, d / dt, 0.3f);
            }
            lastCarPos = pos;
            haveCarPos = true;
            if (carSpeed <= 0f || !q.HasTarget) return -1f;
            return Mathf.Abs(q.TargetFloor.Value - pos) / carSpeed;
        }

        // ------------------------------------------------------------------ rain & thunder

        /// <summary>
        /// Rain fades with the day (<see cref="WeatherRules.RainAudible"/>), thunder rumbles on the day's seeded schedule
        /// while the rain is wanted. Paused / held: both keep sounding under the music duck, the schedule waits.
        /// Call after UpdateMusic (uses its duck gain).
        /// </summary>
        internal void UpdateWeather(float dt)
        {
            bool rainy = ForceRainy ?? (GameServices.TryGet<IDayQuery>(out var day) && day.IsRainy);
            bool want = WeatherRules.RainAudible(rainy, phase) && unlock.IsUnlocked && lib.rainLoop != null;
            rain.Step(dt, want, lib.rainFadeInSeconds, lib.rainFadeOutSeconds);
            if (rain.Active && !rainOn)
            {
                rainSource.clip = lib.rainLoop;
                rainSource.time = 0f;
                rainSource.Play();
                rainOn = true;
            }
            else if (!rain.Active && rainOn)
            {
                rainSource.Stop();
                rainOn = false;
            }

            var ts = ThunderSettings.From(lib);
            if (want && !thunder.Armed)
            {
                int dayNumber = GameServices.TryGet<IDayQuery>(out var q) ? q.CurrentDay.Number : 0;
                thunder.Reset(ThunderSchedule.SeedFor(dayNumber), ts); // a new rainy stretch: the day's schedule from the top
            }
            else if (!want && rain.Gain <= 0f) thunder.Disarm();
            if (want && !paused && !held && thunder.Step(dt, ts)) StrikeThunder();
            ApplyWeatherVolumes();
        }

        /// <summary>One rumble on the thunder voice - dropped (counted in ThunderSkipped) while the previous still sounds.</summary>
        internal void StrikeThunder()
        {
            float now = Time.unscaledTime;
            if (now < thunderEndsAt) { ThunderSkipped++; return; }
            var def = lib.Find(AudioCue.Thunder);
            if (def == null || def.clips == null || def.clips.Length == 0) return;
            var clip = def.clips[rng.Next(def.clips.Length)];
            if (clip == null) return;
            float pitch = AudioRules.PickPitch(def.pitch, def.pitchJitter, rng);
            thunderSource.Stop();
            thunderSource.clip = clip;
            thunderSource.pitch = pitch;
            thunderLevel = def.volume;
            thunderEndsAt = now + clip.length / Mathf.Max(0.01f, pitch);
            ApplyWeatherVolumes();
            thunderSource.Play();
            ThunderStrikes++;
            if (played.Count >= MaxRecords) played.RemoveAt(0);
            played.Add(new CueRecord(AudioCue.Thunder, clip, pitch, thunderSource.volume, now));
        }

        void ApplyWeatherVolumes()
        {
            if (rainSource == null) return;
            rainSource.volume = RainFullVolume * rain.Gain;
            // the rumble belongs to the rain: it fades with it (and with the pause duck)
            thunderSource.volume = master * thunderLevel * duck * rain.Gain;
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
            duck = Mathf.MoveTowards(duck,
                AudioRules.DuckTarget(paused, held, scenic, lib.pauseDuckDb, lib.holdDuckDb, lib.scenicMusicDb), dt * 4f);
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
                // WebGL streams music clips: length/time are unknown until loaded ("Trying to get length of sound which
                // is not loaded yet" every frame) - no seam fade until then
                float seam = def != null && src.clip != null && src.clip.loadState == AudioDataLoadState.Loaded
                    ? AudioRules.SeamGain(src.time, src.clip.length, def.seamFade) : 1f;
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
