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
        /// <summary>WishStarAppeared (star night): soft rising shimmer + climbing pentatonic pings (procedural clip).</summary>
        WishStar = 16,
        /// <summary>WishMade: bright C-major bell arpeggio (procedural clip). WishStarFaded is silent.</summary>
        Wish = 17,
        /// <summary>Star night only: a quiet high sparkle layered on every Lucky judgement (the wish-star twinkle, pitched up).</summary>
        StarSparkle = 18,
        /// <summary>ShopRerolled: Kenney card shuffle (offers redrawn). The TipsChanged spend that follows is silent.</summary>
        ShopReroll = 19,
        /// <summary>CharmBought: soft two-note bell (procedural).</summary>
        CharmBought = 20,
        /// <summary>CharmTriggered (e.g. the talisman ate an Unlucky verdict): protective shimmering "shield" chime (procedural).</summary>
        CharmShield = 21,
        /// <summary>StickersExpired with at least one label/charm: quiet sticker peel at day end (procedural).</summary>
        StickersPeel = 22,
        /// <summary>
        /// Rainy days: a distant thunder rumble (procedural variants) on its own voice, on a seeded schedule
        /// (<see cref="ThunderSchedule"/>), never two at once. Not tied to an event.
        /// </summary>
        Thunder = 23,
        /// <summary>
        /// UiSound(Tap): a pointer click anywhere that is not a button (the golden click effect) - a tiny lucky-gold
        /// "tink" (procedural). Frequent: <see cref="TapGuard"/> drops spam, fades rapid repeats and skips a tap right
        /// after a button click (the click already carries the tink as its layer).
        /// </summary>
        Tap = 24,
        /// <summary>UiSound(Reveal): title "press any key" -> the menu appears: rising shimmer + soft bell (procedural).</summary>
        Reveal = 25,
        /// <summary>ScenicViewChanged(On = false, Establishing = true): soft whoosh as the day's establishing shot zooms back in.</summary>
        ScenicWhoosh = 26,
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

        /// <summary>
        /// The player's scenic mode (ScenicViewChanged, not Establishing): every SFX plays this much lower. It pauses the
        /// game, but the music (and rain) is NOT pause-ducked then - it plays at <see cref="scenicMusicDb"/> (a "look at
        /// the city" moment). The day's establishing shot leaves the levels alone.
        /// </summary>
        public float scenicSfxDb = -4f;
        public float scenicMusicDb = 0f;
        /// <summary>
        /// Tap spam guard (<see cref="TapGuard"/>): a tap within <see cref="tapRepeatWindow"/> s of the previous one plays
        /// <see cref="tapRepeatStep"/> x the previous one's gain (never below <see cref="tapRepeatFloor"/>), so a burst
        /// of clicks gets softer, never louder; a tap within <see cref="tapAfterClickSeconds"/> after a button click is
        /// dropped. The minimum gap between taps is the Tap row's minInterval.
        /// </summary>
        public float tapRepeatWindow = 0.35f;
        public float tapRepeatStep = 0.8f;
        public float tapRepeatFloor = 0.45f;
        public float tapAfterClickSeconds = 0.08f;

        /// <summary>
        /// Elevator travel loop ("wing-wing", procedural, Audio/Generated): one dedicated looping voice that fades in when
        /// the car departs and out so it is silent at arrival (before the ding). Never stacks: a re-departure keeps it going.
        /// </summary>
        public AudioClip travelHum;
        /// <summary>Level of the hum at full fade (x MasterVolume, x titleSfxScale on the title demo). An ambience: under the SFX.</summary>
        public float travelHumVolume = 0.3f;
        public float travelFadeInSeconds = 0.15f;
        /// <summary>Fade-out length; it starts this long before the predicted arrival, so the hum is out when the ding plays.</summary>
        public float travelFadeOutSeconds = 0.2f;
        /// <summary>Pitch at departure, rising (smoothstep) to 1 over <see cref="travelSpinUpSeconds"/> ("accelerating").</summary>
        public float travelPitchStart = 0.93f;
        public float travelSpinUpSeconds = 0.5f;
        /// <summary>Pitch at arrival, dipping (smoothstep) over the last <see cref="travelSpinDownSeconds"/> ("braking").</summary>
        public float travelPitchArrive = 0.95f;
        public float travelSpinDownSeconds = 0.4f;

        /// <summary>
        /// Rainy days (IDayQuery.IsRainy, Intro/Running/Summary): one dedicated looping voice with the procedural rain bed,
        /// fading in/out with the day, ducked with the music on pause / coach-mark hold.
        /// </summary>
        public AudioClip rainLoop;
        /// <summary>Rain level at full fade (x MasterVolume): an ambience under the music.</summary>
        public float rainVolume = 0.12f;
        public float rainFadeInSeconds = 3f;
        public float rainFadeOutSeconds = 2.5f;
        /// <summary>Implied lightning flashes every min..max seconds (uniform, seeded per day) ...</summary>
        public float thunderMinGap = 8f;
        public float thunderMaxGap = 20f;
        /// <summary>... each heard as a rumble this many seconds after its flash (sound travels slower than light).</summary>
        public float thunderDelayMin = 1f;
        public float thunderDelayMax = 3f;

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
