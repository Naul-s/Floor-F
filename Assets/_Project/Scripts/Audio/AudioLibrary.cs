using System;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>Every sound effect the game can play. Event -> cue mapping: <see cref="AudioEventMap"/>.</summary>
    public enum AudioCue
    {
        None = 0,
        /// <summary>ElevatorArrived.</summary>
        Ding = 1,
        /// <summary>DoorsOpened: the door latch clunk (motor run trimmed off). Door cues share one voice (no stacking).</summary>
        DoorsOpen = 2,
        /// <summary>DoorsClosed: the same clunk pitched down (no dedicated closing sample).</summary>
        DoorsClose = 3,
        /// <summary>LuckJudged Lucky.</summary>
        Lucky = 4,
        /// <summary>LuckJudged Unlucky (+ music pitch dip).</summary>
        Unlucky = 5,
        /// <summary>TipsChanged (increase) / GuestDelivered.</summary>
        Tip = 6,
        /// <summary>StickerApplied.</summary>
        Sticker = 7,
        /// <summary>GuestLeft (not DayEnded).</summary>
        GuestLeft = 8,
        /// <summary>GuestMoodChanged -> Impatient.</summary>
        Impatient = 9,
        /// <summary>DayStarted.</summary>
        DayStart = 10,
        /// <summary>UiSound(Error): shop purchase rejected etc.</summary>
        Error = 11,
        /// <summary>UiSound(Click).</summary>
        UiClick = 12,
        /// <summary>
        /// LuckStreakChanged increase: short pluck layered on the Lucky chime, one semitone higher per streak step
        /// (<see cref="AudioLibrary.streakSemitoneStep"/>, capped at <see cref="AudioLibrary.streakMaxSemitones"/>) and
        /// without random jitter, so a streak audibly climbs. Same-frame increases collapse into one note (the highest).
        /// </summary>
        StreakUp = 13,
        /// <summary>LuckStreakChanged.Broken: short deflating "lost it" sound (on top of the Unlucky gasp).</summary>
        StreakBroken = 14,
        /// <summary>Soft clock tick once per second over the last <see cref="AudioLibrary.clockTickSeconds"/> s of a real day (rising pitch).</summary>
        ClockTick = 15,
    }

    public enum MusicTrack
    {
        None = 0,
        /// <summary>
        /// Main background music (title, days, summary, prosperous/modest endings): BlondPanda "short elevator music loop"
        /// (Freesound 659889, CC0).
        /// </summary>
        Elevator = 1,
        /// <summary>Sticker shop: Kevin MacLeod "Local Forecast - Elevator" (CC-BY), so the shop sounds different from the days.</summary>
        ShopLoop = 2,
        /// <summary>danlucaz mystery loop (4th-floor / haunted endings).</summary>
        Mystery = 3,
    }

    /// <summary>One row of the sound table: clip variants (random pick), optional layer, volume, pitch range, spam guard.</summary>
    [Serializable]
    public sealed class CueDef
    {
        public AudioCue cue;
        public AudioClip[] clips = new AudioClip[0];
        /// <summary>Played together with the main clip (same pitch) at <see cref="layerVolume"/> x volume.</summary>
        public AudioClip layer;
        public float layerVolume = 0.4f;
        public float volume = 1f;
        public float pitch = 1f;
        /// <summary>Random pitch = pitch x (1 +- jitter). Kept in 0.05..0.10.</summary>
        public float pitchJitter = 0.07f;
        /// <summary>Seconds (unscaled) before the same cue may play again.</summary>
        public float minInterval = 0.1f;
    }

    [Serializable]
    public sealed class TrackDef
    {
        public MusicTrack track;
        public AudioClip clip;
        public float volume = 1f;
        /// <summary>Fade (s) at the loop start/end to hide an encoder gap at the seam. 0 = seamless loop.</summary>
        public float seamFade;
    }

    /// <summary>
    /// The audio data table (one asset: Resources/Audio/AudioLibrary). Built by FloorF.EditorTools.AudioLibraryBuilder from
    /// the clips under ThirdParty/ - rebuild instead of editing by hand.
    /// </summary>
    public sealed class AudioLibrary : ScriptableObject
    {
        public const string ResourcePath = "Audio/AudioLibrary";

        public CueDef[] cues = new CueDef[0];
        public TrackDef[] tracks = new TrackDef[0];
        /// <summary>Music bus level under MasterVolume (SFX are at 1).</summary>
        public float musicVolume = 0.45f;
        public float pauseDuckDb = -12f;
        /// <summary>Lighter music duck while the Day 0 coach marks hold the game (TutorialHoldChanged).</summary>
        public float holdDuckDb = -6f;
        public float crossfadeSeconds = 1.2f;
        /// <summary>SFX level while the title's attract demo plays behind the menu.</summary>
        public float titleSfxScale = 0.35f;
        /// <summary>Unlucky music dip: pitch multiplier at the bottom, seconds down, seconds back up.</summary>
        public float dipPitch = 0.75f;
        public float dipDownSeconds = 0.15f;
        public float dipUpSeconds = 0.6f;
        /// <summary>StreakUp: semitones added per streak step above 1, and the cap.</summary>
        public float streakSemitoneStep = 1f;
        public float streakMaxSemitones = 7f;
        /// <summary>ClockTick: ticks over the last N whole seconds of a real day; pitch rises to 1 + rise on the last one.</summary>
        public int clockTickSeconds = 10;
        public float clockTickRise = 0.2f;
        /// <summary>
        /// Door cues (DoorsOpen/DoorsClose) share one voice: a new door cue fades the still-sounding previous one out over
        /// this many seconds instead of stacking on it.
        /// </summary>
        public float doorCutFadeSeconds = 0.03f;
        /// <summary>A door cue starting within this many seconds after the arrival Ding is played <see cref="doorUnderDingDb"/> lower.</summary>
        public float dingGuardSeconds = 0.6f;
        public float doorUnderDingDb = -6f;

        public CueDef Find(AudioCue cue)
        {
            if (cues == null) return null;
            foreach (var c in cues) if (c != null && c.cue == cue) return c;
            return null;
        }

        public TrackDef Find(MusicTrack track)
        {
            if (tracks == null) return null;
            foreach (var t in tracks) if (t != null && t.track == track) return t;
            return null;
        }

        public static AudioLibrary Load() => Resources.Load<AudioLibrary>(ResourcePath);
    }
}
