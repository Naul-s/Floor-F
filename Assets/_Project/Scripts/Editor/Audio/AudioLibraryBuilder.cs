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
                if (clip == null) missing.Add(path);
                return clip;
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

            const string door = FS + "562714__pagdev__elevator_door_opening.wav";
            lib.cues = new[]
            {
                // the ping is mastered near 0 dBFS (~20 dB hotter than the other cues): keep it low
                Cue(AudioCue.Ding, new[] { FS + "459349__lilmati__elevator-ping-01.wav" }, 0.15f, 1f, 0.05f, 0.25f),
                Cue(AudioCue.DoorsOpen, new[] { door }, 0.55f, 1f, 0.06f, 0.3f),
                // no closing sample: the same door slid lower and quieter reads as "heavier, shutting"
                Cue(AudioCue.DoorsClose, new[] { door }, 0.45f, 0.8f, 0.06f, 0.3f),
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
                Cue(AudioCue.UiClick, new[] { UI + "click_002.ogg" }, 0.5f, 1f, 0.07f, 0.05f),
                // streak ladder: a tonal pluck (+ a little glass) on top of the Lucky twinkle; played at an exact pitch
                // (semitone steps), the jitter value here is not used for it
                Cue(AudioCue.StreakUp, new[] { UI + "pluck_001.ogg" }, 0.5f, 1f, 0.05f, 0.1f,
                    layer: UI + "glass_002.ogg", layerVolume: 0.35f),
                // streak lost: falling "minimize" swoosh slid lower + the chip stack scattering
                Cue(AudioCue.StreakBroken, new[] { UI + "minimize_001.ogg" }, 0.5f, 0.8f, 0.05f, 0.4f,
                    layer: Casino + "chips-collide-1.ogg", layerVolume: 0.4f),
                // last 10 s of a real day: soft click, pitch 0.9 rising to 0.9 x 1.2 (exact pitch, no jitter)
                Cue(AudioCue.ClockTick, new[] { UI + "click_001.ogg" }, 0.3f, 0.9f, 0.05f, 0.25f),
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

            if (missing.Count > 0)
                throw new FileNotFoundException("[AudioLibraryBuilder] missing clips:\n" + string.Join("\n", missing));

            if (created) AssetDatabase.CreateAsset(lib, AssetPath);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioLibraryBuilder] wrote " + AssetPath + " (" + lib.cues.Length + " cues, " + lib.tracks.Length + " tracks)");
        }
    }
}
