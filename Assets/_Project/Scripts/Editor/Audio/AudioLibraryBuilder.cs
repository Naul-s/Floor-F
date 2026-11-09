using System.Collections.Generic;
using System.IO;
using FloorF.Audio;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Writes the audio data table Resources/Audio/AudioLibrary.asset from the clips the orchestrator imported under
    /// ThirdParty/ (import settings are not touched here). Idempotent - this is the place to retune volumes/pitches.
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.AudioLibraryBuilder.Build
    /// </summary>
    public static class AudioLibraryBuilder
    {
        const string Folder = "Assets/_Project/Resources/Audio";
        const string AssetPath = Folder + "/AudioLibrary.asset";
        const string FS = "Assets/_Project/ThirdParty/Freesound/";
        const string UI = "Assets/_Project/ThirdParty/Kenney/InterfaceSounds/";
        const string Casino = "Assets/_Project/ThirdParty/Kenney/CasinoAudio/";
        const string KM = "Assets/_Project/ThirdParty/KevinMacLeod/";

        // Levels of the procedural clips (AudioLevelProbe, dBFS as played = file x volume; files peak at -3 dBFS).
        // Travel hum (file rms -10.8 / max100ms -8.1): 0.15 -> rms -27.3 / max100ms -24.6, ~7 dB under the ding (-17.8)
        // and the door clunk (-17.7) unweighted - and it is bass-heavy (110 Hz fundamental, A-weighted ~12.5 dB below its
        // rms), so by ear it sits ~5 dB under the music (rms -31.5, broadband) and far under every effect. It is faded out
        // before the ding anyway; on the title demo x titleSfxScale (-9 dB) like the other SFX.
        public const float TravelHumVolume = 0.15f;
        // wish star appears (max100ms -12.8): 0.3 -> -23.2, a soft cue around the Lucky twinkle (-24.8)
        public const float WishStarVolume = 0.3f;
        // wish made (max100ms -12.5): 0.5 -> -18.5, the reward - as bright as the ding
        public const float WishVolume = 0.5f;
        // star-night Lucky layer (max100ms -11.7, 2.6-4 kHz, sounds loud): 0.12 -> -30.1, like the Lucky glass layer (-29.2)
        public const float StarSparkleVolume = 0.12f;
        // Rainy day ambience (played = file x volume). Rain bed (file rms -19.9 / max100ms -17.9, droplets peak -6.6):
        // 0.12 -> rms -38.3 / max100ms -36.3, a steady bed ~7 dB under the music (rms -31.5) and far under every effect.
        public const float RainVolume = 0.12f;
        // Distant thunder (max100ms -10.7..-12.1, bass-heavy, 0.35-0.6 s rises): 0.25 -> max100ms -22.7..-24.2, under the
        // ding (-17.8) and, being mostly < 300 Hz, much softer by ear than those numbers. Fades with the rain.
        public const float ThunderVolume = 0.25f;
        // Shop. Reroll = Kenney card shuffle (3.1 s, quiet: max100ms -21.6 imported): 0.7 at pitch 1.1 -> ~-24.7, a riffle
        // next to the click. Charm bell (max100ms -11.0): 0.35 -> -20.1, soft, under the ding. Shield chime (-12.7):
        // 0.5 -> -18.7 - it plays next to the guest's Unlucky gasp (-24.1) and must read as "saved". Sticker peel (-15.8):
        // 0.2 -> -29.7, quiet (day end, under the summary).
        public const float RerollVolume = 0.7f;
        public const float RerollPitch = 1.1f;
        public const float CharmBellVolume = 0.35f;
        public const float ShieldVolume = 0.5f;
        public const float PeelVolume = 0.2f;
        // UI (procedural, ProceduralSfxSynth.Ui; files peak -3 dBFS). The button click (click_002, a 10 ms transient)
        // plays at max100ms -32.0. Gold tink (max100ms -14.6, bright 2.3-3.5 kHz): Tap 0.12 -> -33.0, just under the
        // click - frequent, so the quietest UI sound; under the click itself 0.5 x 0.2 = 0.1 -> -34.6, a light golden
        // ring under the click's snap. Menu reveal (-13.1): 0.3 -> -23.6, like the wish-star shimmer (-23.2), well under
        // the ding (-17.8); UI sounds are not title-scaled. Whoosh (-12.9, broadband noise, sounds louder than its
        // number): 0.14 -> -30.0, a breath under the music.
        public const float TapVolume = 0.12f;
        public const float TapMinInterval = 0.07f;
        public const float ClickTinkLayer = 0.2f;
        public const float RevealVolume = 0.3f;
        public const float WhooshVolume = 0.14f;
        public const float ScenicSfxDb = -4f;

        /// <summary>
        /// WebGL re-encodes every clip to AAC and the BROWSER decodes it (decodeAudioData). AAC works in 1024-sample
        /// frames; clips of a few ms (Kenney click_002 is 10 ms = 441 samples) become 1-2 frame streams that some
        /// decoders (e.g. Windows' Media Foundation behind Chrome/Edge) reject with "EncodingError: Unable to decode
        /// audio data" (user report, itch.io, 2026-11-09). Any library clip shorter than this is copied with trailing
        /// silence to this length - same samples, same level, sounds identical.
        /// </summary>
        public const float MinClipSeconds = 0.25f;
        public const string PaddedFolder = "Assets/_Project/Audio/Generated/Padded";

        static AudioClip PadIfShort(string path, AudioClip clip)
        {
            if (clip.length >= MinClipSeconds || path.StartsWith(PaddedFolder)) return clip;
            int channels = Mathf.Max(1, clip.channels), rate = clip.frequency;
            clip.LoadAudioData();
            var src = new float[clip.samples * channels];
            if (!clip.GetData(src, 0)) throw new IOException("[AudioLibraryBuilder] could not read samples of " + path);
            int frames = Mathf.CeilToInt(MinClipSeconds * rate);
            var padded = new float[frames * channels];
            System.Array.Copy(src, padded, src.Length);
            Directory.CreateDirectory(Path.GetFullPath(PaddedFolder));
            string outPath = PaddedFolder + "/" + Path.GetFileNameWithoutExtension(path) + "_pad.wav";
            File.WriteAllBytes(Path.GetFullPath(outPath), DoorClipTrimmer.WriteWav16(padded, channels, rate));
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var ai = (AudioImporter)AssetImporter.GetAtPath(outPath);
            ai.forceToMono = false;            // samples were taken from the imported (already mono/normalised) clip
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<AudioClip>(outPath);
            Debug.LogFormat("[AudioLibraryBuilder] padded {0} ({1:0.000}s) -> {2} ({3:0.000}s)", path, clip.length, outPath, result.length);
            return result;
        }

        static string[] ThunderClips()
        {
            var a = new string[ProceduralSfxSynth.ThunderVariants];
            for (int i = 0; i < a.Length; i++) a[i] = ProceduralSfxSynth.ThunderPath(i);
            return a;
        }

        /// <summary>One batch run: synthesize the procedural clips, rebuild the table, log the levels.</summary>
        public static void SynthBuildAndProbe()
        {
            ProceduralSfxSynth.Build();
            Build();
            AudioLevelProbe.Run();
        }

        [MenuItem("Floor F/Audio/Rebuild Audio Library")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var lib = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetPath);
            bool created = lib == null;
            if (created) lib = ScriptableObject.CreateInstance<AudioLibrary>();

            var missing = new List<string>();
            AudioClip C(string path)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) { missing.Add(path); return null; }
                return PadIfShort(path, clip);
            }
            CueDef Cue(AudioCue cue, string[] clips, float volume, float pitch, float jitter, float minInterval,
                string layer = null, float layerVolume = 0.4f)
            {
                var arr = new AudioClip[clips.Length];
                for (int i = 0; i < clips.Length; i++) arr[i] = C(clips[i]);
                return new CueDef
                {
                    cue = cue, clips = arr, volume = volume, pitch = pitch, pitchJitter = jitter, minInterval = minInterval,
                    layer = layer != null ? C(layer) : null, layerVolume = layerVolume,
                };
            }

            // only the latch clunk of PagDev's door recording (0-568 ms; the motor/machinery run after it is cut off by
            // DoorClipTrimmer). Door cues share one voice (AudioRoot: a new one cuts the previous) and play
            // doorUnderDingDb lower inside dingGuardSeconds after the arrival ding.
            const string door = DoorClipTrimmer.ClunkPath;
            lib.cues = new[]
            {
                // the ping is mastered near 0 dBFS (~20 dB hotter than the other cues): keep it low
                Cue(AudioCue.Ding, new[] { FS + "459349__lilmati__elevator-ping-01.wav" }, 0.15f, 1f, 0.05f, 0.25f),
                Cue(AudioCue.DoorsOpen, new[] { door }, 0.45f, 1f, 0.06f, 0.3f),
                // no closing sample: the same clunk slid lower and quieter reads as "heavier, shutting"
                Cue(AudioCue.DoorsClose, new[] { door }, 0.37f, 0.8f, 0.06f, 0.3f),
                Cue(AudioCue.Lucky, new[] { FS + "639429__laurenponder__crystal-twinkle.wav" }, 0.7f, 1f, 0.08f, 0.12f,
                    layer: UI + "glass_001.ogg", layerVolume: 0.3f),
                Cue(AudioCue.Unlucky, new[] { FS + "536757__egomassive__gaspm.ogg" }, 0.85f, 1f, 0.10f, 0.2f),
                Cue(AudioCue.Tip, new[] { Casino + "chips-stack-1.ogg", Casino + "chips-stack-2.ogg", Casino + "chips-stack-3.ogg" },
                    0.6f, 1f, 0.07f, 0.1f),
                Cue(AudioCue.Sticker, new[] { Casino + "chip-lay-1.ogg", Casino + "chip-lay-2.ogg" }, 0.8f, 1f, 0.05f, 0.2f,
                    layer: UI + "confirmation_001.ogg", layerVolume: 0.5f),
                Cue(AudioCue.GuestLeft, new[] { UI + "bong_001.ogg" }, 0.5f, 0.75f, 0.06f, 0.3f),
                Cue(AudioCue.Impatient, new[] { UI + "tick_001.ogg" }, 0.45f, 1f, 0.08f, 0.25f),
                Cue(AudioCue.DayStart, new[] { UI + "confirmation_002.ogg" }, 0.6f, 1f, 0.05f, 0.5f),
                Cue(AudioCue.Error, new[] { UI + "error_001.ogg" }, 0.6f, 1f, 0.05f, 0.2f),
                // button click: Kenney click + the gold tink lightly underneath, so buttons and taps feel like one family
                Cue(AudioCue.UiClick, new[] { UI + "click_002.ogg" }, 0.5f, 1f, 0.07f, 0.05f,
                    layer: ProceduralSfxSynth.GoldTinkPath, layerVolume: ClickTinkLayer),
                // a click anywhere: the tink alone, quiet, small pitch spread; TapGuard drops/fades spam (AudioLibrary.tap*)
                Cue(AudioCue.Tap, new[] { ProceduralSfxSynth.GoldTinkPath }, TapVolume, 1f, 0.06f, TapMinInterval),
                // title menu reveal: rising shimmer + soft golden bell
                Cue(AudioCue.Reveal, new[] { ProceduralSfxSynth.MenuRevealPath }, RevealVolume, 1f, 0.05f, 0.6f),
                // the day's establishing shot zooming back in
                Cue(AudioCue.ScenicWhoosh, new[] { ProceduralSfxSynth.ScenicWhooshPath }, WhooshVolume, 1f, 0.05f, 1f),
                // streak ladder: a tonal pluck (+ a little glass) on top of the Lucky twinkle; played at an exact pitch
                // (semitone steps), the jitter value here is not used for it
                Cue(AudioCue.StreakUp, new[] { UI + "pluck_001.ogg" }, 0.6f, 1f, 0.05f, 0.1f,
                    layer: UI + "glass_002.ogg", layerVolume: 0.35f),
                // streak lost: falling "minimize" swoosh slid lower + the chip stack scattering
                Cue(AudioCue.StreakBroken, new[] { UI + "minimize_001.ogg" }, 0.3f, 0.8f, 0.05f, 0.4f,
                    layer: Casino + "chips-collide-1.ogg", layerVolume: 1f),
                // last 10 s of a real day: soft click (quiet sample: 0.8 plays ~-28 dBFS/100 ms, a short transient over the
                // music), pitch 0.9 rising to 0.9 x 1.2 (exact pitch, no jitter)
                Cue(AudioCue.ClockTick, new[] { UI + "click_001.ogg" }, 0.8f, 0.9f, 0.05f, 0.25f),
                // star night (procedural clips, ProceduralSfxSynth): a soft shimmer when a wish star appears, a bright
                // bell arpeggio when the wish is made (nothing on fade), a quiet high sparkle layered on every Lucky
                Cue(AudioCue.WishStar, new[] { ProceduralSfxSynth.WishStarPath }, WishStarVolume, 1f, 0.05f, 0.5f),
                Cue(AudioCue.Wish, new[] { ProceduralSfxSynth.WishChimePath }, WishVolume, 1f, 0.05f, 0.3f),
                Cue(AudioCue.StarSparkle, new[] { ProceduralSfxSynth.StarSparklePath }, StarSparkleVolume, 1f, 0.08f, 0.15f),
                // shop: offers redrawn = a card riffle; a charm = a soft bell; the talisman eating a bad-luck verdict = a
                // shimmering shield chime (it plays next to the guest's Unlucky gasp); one-day purchases running out at
                // day end = a quiet peel
                Cue(AudioCue.ShopReroll, new[] { Casino + "card-shuffle.ogg" }, RerollVolume, RerollPitch, 0.05f, 0.3f),
                Cue(AudioCue.CharmBought, new[] { ProceduralSfxSynth.CharmBellPath }, CharmBellVolume, 1f, 0.05f, 0.3f),
                Cue(AudioCue.CharmShield, new[] { ProceduralSfxSynth.ShieldChimePath }, ShieldVolume, 1f, 0.05f, 0.4f),
                Cue(AudioCue.StickersPeel, new[] { ProceduralSfxSynth.StickerPeelPath }, PeelVolume, 1f, 0.06f, 0.5f),
                // rainy-day distant thunder: played by AudioRoot's own thunder voice on the seeded schedule (not an event)
                Cue(AudioCue.Thunder, ThunderClips(), ThunderVolume, 1f, 0.08f, 6f),
            };
            lib.tracks = new[]
            {
                // Levels (AudioLevelProbe, dBFS as played = clip x musicVolume x track volume, before phase level/master):
                // BlondPanda loop is dense (rms -17.6, loudest 100 ms -13.3). At 0.45 it plays at rms -31.5 / max100ms
                // -27.2: ~9 dB under the ping (-17.8) and the doors (-18.4), a few dB under the lucky twinkle / gasp
                // (~-24.5) - about where the old main song sat (MacLeod rms -33.2). Its loop seam is a sample step
                // (0.22 vs a largest natural step of 0.07 = click) inside sustained music: a short 0.12 s fade each side
                // removes the click without the long dip the mp3 needs.
                new TrackDef { track = MusicTrack.Elevator, clip = C(FS + "659889__blondpanda__short-elevator-music-loop.wav"), volume = 0.45f, seamFade = 0.12f },
                // mp3 -> Vorbis keeps the encoder padding at the loop point: fade 0.3 s each side to hide the click/gap
                new TrackDef { track = MusicTrack.ShopLoop, clip = C(KM + "Local Forecast - Elevator.mp3"), volume = 1f, seamFade = 0.3f },
                // mystery loop is ~10 dB hotter than the rest (rms -14.3): 0.6 -> rms -25.6, a step up for the ending, not a jump
                new TrackDef { track = MusicTrack.Mystery, clip = C(FS + "665215__danlucaz__mystery-loop-3.wav"), volume = 0.6f, seamFade = 0f },
            };
            lib.musicVolume = 0.45f;
            lib.pauseDuckDb = -12f;
            lib.holdDuckDb = -6f;
            lib.crossfadeSeconds = 1.2f;
            lib.titleSfxScale = 0.35f;
            lib.dipPitch = 0.75f;
            lib.dipDownSeconds = 0.15f;
            lib.dipUpSeconds = 0.6f;
            lib.streakSemitoneStep = 1f;
            lib.streakMaxSemitones = 7f;
            lib.clockTickSeconds = 10;
            lib.clockTickRise = 0.2f;
            lib.doorCutFadeSeconds = 0.03f;
            // the ping holds full level ~0.4 s and is ~-20 dB by 0.8 s; doors finish opening >= doorSeconds (0.35 s)
            // after arrival, i.e. inside that sustain: -6 dB keeps the clunk under the ding there
            lib.dingGuardSeconds = 0.6f;
            lib.doorUnderDingDb = -6f;

            // scenic view: SFX a little lower, the player's scenic mode keeps the music (no pause duck)
            lib.scenicSfxDb = ScenicSfxDb;
            lib.scenicMusicDb = 0f;
            // tap spam guard: rapid taps (< 0.35 s apart) 0.8x the previous, floor 0.45 (-7 dB); none right after a click
            lib.tapRepeatWindow = 0.35f;
            lib.tapRepeatStep = 0.8f;
            lib.tapRepeatFloor = 0.45f;
            lib.tapAfterClickSeconds = 0.08f;

            // travel hum (ProceduralSfxSynth): an ambience under the ding / doors / music - see TravelHumVolume
            lib.travelHum = C(ProceduralSfxSynth.HumPath);
            lib.travelHumVolume = TravelHumVolume;
            lib.travelFadeInSeconds = 0.15f;
            lib.travelFadeOutSeconds = 0.2f;
            lib.travelPitchStart = 0.93f;
            lib.travelSpinUpSeconds = 0.5f;
            lib.travelPitchArrive = 0.95f;
            lib.travelSpinDownSeconds = 0.4f;

            // rainy days (Day 5, Friday the 13th): rain bed + seeded distant thunder, see AudioLibrary
            lib.rainLoop = C(ProceduralSfxSynth.RainPath);
            lib.rainVolume = RainVolume;
            lib.rainFadeInSeconds = 3f;
            lib.rainFadeOutSeconds = 2.5f;
            lib.thunderMinGap = 8f;
            lib.thunderMaxGap = 20f;
            lib.thunderDelayMin = 1f;
            lib.thunderDelayMax = 3f;

            if (missing.Count > 0)
                throw new FileNotFoundException("[AudioLibraryBuilder] missing clips:\n" + string.Join("\n", missing));

            if (created) AssetDatabase.CreateAsset(lib, AssetPath);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioLibraryBuilder] wrote " + AssetPath + " (" + lib.cues.Length + " cues, " + lib.tracks.Length + " tracks)");
        }
    }
}
