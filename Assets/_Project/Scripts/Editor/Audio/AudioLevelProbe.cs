using System.IO;
using System.Text;
using FloorF.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FloorF.EditorTools
{
    /// <summary>
    /// "Ear proxy" for balancing: decodes every clip in the AudioLibrary from its source file and logs peak, loudest
    /// 100 ms window and overall RMS (dBFS, channels averaged), plus the level the game actually plays it at
    /// (x cue volume, or x musicVolume x track volume for music). Read-only - changes nothing.
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.AudioLevelProbe.Run  (look for "[AudioLevelProbe]")
    /// </summary>
    public static class AudioLevelProbe
    {
        public readonly struct Level
        {
            public readonly float PeakDb, Max100msDb, RmsDb, Seconds;
            public Level(float peak, float max100, float rms, float seconds) { PeakDb = peak; Max100msDb = max100; RmsDb = rms; Seconds = seconds; }
        }

        [MenuItem("Floor F/Audio/Log Clip Levels")]
        public static void Run()
        {
            var lib = AudioLibrary.Load();
            if (lib == null) { Debug.LogError("[AudioLevelProbe] no AudioLibrary"); return; }
            var sb = new StringBuilder("[AudioLevelProbe] dBFS: peak / max100ms / rms -> played max100ms / played rms\n");
            foreach (var c in lib.cues)
            {
                if (c == null || c.clips == null) continue;
                foreach (var clip in c.clips) Line(sb, c.cue.ToString(), clip, c.volume);
                if (c.layer != null) Line(sb, c.cue + " (layer)", c.layer, c.volume * c.layerVolume);
            }
            foreach (var t in lib.tracks)
                if (t != null) Line(sb, "music " + t.track, t.clip, lib.musicVolume * t.volume);
            Debug.Log(sb.ToString());
        }

        static void Line(StringBuilder sb, string name, AudioClip clip, float gain)
        {
            if (clip == null) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!TryMeasure(path, out var l) && !TryMeasureImported(clip, out l))
            { sb.AppendLine(name + ": " + path + " (could not decode)"); return; }
            float g = 20f * Mathf.Log10(Mathf.Max(1e-6f, gain));
            sb.AppendFormat("{0,-22} {1,6:0.0} / {2,6:0.0} / {3,6:0.0}  -> {4,6:0.0} / {5,6:0.0}  (x{6:0.###}, {7:0.0}s) {8}\n",
                name, l.PeakDb, l.Max100msDb, l.RmsDb, l.Max100msDb + g, l.RmsDb + g, gain, l.Seconds, Path.GetFileName(path));
        }

        /// <summary>Decodes the source file (not the imported/compressed clip) and measures it.</summary>
        public static bool TryMeasure(string assetPath, out Level level)
        {
            level = default;
            string full = Path.GetFullPath(assetPath).Replace('\\', '/');
            string ext = Path.GetExtension(full).ToLowerInvariant();
            var type = ext == ".wav" ? AudioType.WAV : ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
            if (type == AudioType.UNKNOWN) return false;
            using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + full, type))
            {
                var dh = (DownloadHandlerAudioClip)req.downloadHandler;
                dh.streamAudio = false;
                dh.compressed = false;
                var op = req.SendWebRequest();
                var until = System.DateTime.UtcNow.AddSeconds(30);
                while (!op.isDone && System.DateTime.UtcNow < until) { }
                if (req.result != UnityWebRequest.Result.Success) return false;
                var clip = dh.audioClip;
                if (clip == null) return false;
                // the decode may still be loading
                until = System.DateTime.UtcNow.AddSeconds(30);
                while (clip.loadState == AudioDataLoadState.Loading && System.DateTime.UtcNow < until) { }
                int ch = Mathf.Max(1, clip.channels), n = clip.samples;
                var data = new float[n * ch];
                if (!clip.GetData(data, 0)) { Object.DestroyImmediate(clip); return false; }
                level = Measure(data, ch, clip.frequency);
                Object.DestroyImmediate(clip);
                return true;
            }
        }

        /// <summary>Fallback: the imported clip itself (works for Decompress On Load clips).</summary>
        static bool TryMeasureImported(AudioClip clip, out Level level)
        {
            level = default;
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return false;
            int ch = Mathf.Max(1, clip.channels);
            var data = new float[clip.samples * ch];
            if (!clip.GetData(data, 0)) return false;
            level = Measure(data, ch, clip.frequency);
            return true;
        }

        /// <summary>Interleaved samples -> peak / loudest 100 ms window (50% hop) / RMS of the channel average, in dBFS.</summary>
        public static Level Measure(float[] interleaved, int channels, int rate)
        {
            int n = interleaved.Length / channels;
            var mono = new float[n];
            float peak = 0f;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                float v = 0f;
                for (int c = 0; c < channels; c++) v += interleaved[i * channels + c];
                v /= channels;
                mono[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
                sum += v * v;
            }
            int win = Mathf.Max(1, rate / 10);
            double best = 0;
            for (int s = 0; s + win <= n; s += Mathf.Max(1, win / 2))
            {
                double w = 0;
                for (int i = s; i < s + win; i++) w += mono[i] * mono[i];
                best = System.Math.Max(best, w / win);
            }
            if (n < win) best = sum / Mathf.Max(1, n);
            return new Level(Db(peak), Db((float)System.Math.Sqrt(best)), Db((float)System.Math.Sqrt(sum / Mathf.Max(1, n))), n / (float)rate);
        }

        static float Db(float x) => 20f * Mathf.Log10(Mathf.Max(1e-9f, x));
    }
}
