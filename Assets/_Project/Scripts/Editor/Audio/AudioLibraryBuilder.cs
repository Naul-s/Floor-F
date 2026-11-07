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
            };
            lib.tracks = new[]
            {
                // mp3 -> Vorbis keeps the encoder padding at the loop point: fade 0.3 s each side to hide the click/gap
                new TrackDef { track = MusicTrack.Elevator, clip = C(KM + "Local Forecast - Elevator.mp3"), volume = 1f, seamFade = 0.3f },
                new TrackDef { track = MusicTrack.ShopLoop, clip = C(FS + "659889__blondpanda__short-elevator-music-loop.wav"), volume = 0.9f, seamFade = 0f },
                new TrackDef { track = MusicTrack.Mystery, clip = C(FS + "665215__danlucaz__mystery-loop-3.wav"), volume = 1f, seamFade = 0f },
            };
            lib.musicVolume = 0.45f;
            lib.pauseDuckDb = -12f;
            lib.crossfadeSeconds = 1.2f;
            lib.titleSfxScale = 0.35f;
            lib.dipPitch = 0.75f;
            lib.dipDownSeconds = 0.15f;
            lib.dipUpSeconds = 0.6f;

            if (missing.Count > 0)
                throw new FileNotFoundException("[AudioLibraryBuilder] missing clips:\n" + string.Join("\n", missing));

            if (created) AssetDatabase.CreateAsset(lib, AssetPath);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioLibraryBuilder] wrote " + AssetPath + " (" + lib.cues.Length + " cues, " + lib.tracks.Length + " tracks)");
        }
    }
}
