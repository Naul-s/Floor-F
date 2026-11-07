using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Derives the door "clunk" from PagDev's elevator door recording (Freesound 562714, CC0): keeps only the latch
    /// clunk at the start and drops the motor/machinery run that follows, with a short raised-cosine fade at each end
    /// (the source starts mid-waveform at -0.026, i.e. a click, and the cut would click too).
    ///
    /// Where the cut is (envelope, 10 ms windows, channel average): 0-100 ms soft pre-roll, 100-560 ms the clunk
    /// (two low-heavy hits, &lt;250 Hz band -14..-20 dB, &gt;2 kHz band only -28..-34), last hit at ~547 ms; at ~560 ms the
    /// level drops 5 dB into a flat low rumble (motor starting), and from ~800 ms the bright band rises to the level of
    /// the whole signal (the machinery whine), building to the loud run 1.6-3.3 s; a second thud (door stop) at 3.3 s and
    /// the file ends abruptly at 3.58 s while still at -20 dB. Kept: 0..568 ms, fade-in 3 ms, fade-out 16 ms (552-568 ms).
    ///
    /// Output: 16-bit PCM, same channels/rate. Idempotent; the source file is never modified.
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.DoorClipTrimmer.Build
    /// then ProjectSetupTool.ConfigureThirdPartyImporters (mono Vorbis like the other SFX) and AudioLibraryBuilder.Build.
    /// </summary>
    public static class DoorClipTrimmer
    {
        public const string SourcePath = "Assets/_Project/ThirdParty/Freesound/562714__pagdev__elevator_door_opening.wav";
        public const string ClunkPath = "Assets/_Project/ThirdParty/Freesound/562714__pagdev__elevator_door_clunk.wav";
        public const float CutEndSeconds = 0.568f;
        public const float FadeInSeconds = 0.003f;
        public const float FadeOutSeconds = 0.016f;

        [MenuItem("Floor F/Audio/Rebuild Door Clunk Clip")]
        public static void Build()
        {
            string src = Path.GetFullPath(SourcePath);
            if (!File.Exists(src)) throw new FileNotFoundException("[DoorClipTrimmer] missing " + SourcePath);
            if (!TryReadWav(File.ReadAllBytes(src), out var data, out int channels, out int rate, out string error))
                throw new InvalidDataException("[DoorClipTrimmer] " + SourcePath + ": " + error);
            var clunk = Trim(data, channels, rate, CutEndSeconds, FadeInSeconds, FadeOutSeconds);
            File.WriteAllBytes(Path.GetFullPath(ClunkPath), WriteWav16(clunk, channels, rate));
            AssetDatabase.ImportAsset(ClunkPath, ImportAssetOptions.ForceUpdate);

            var full = AudioLevelProbe.Measure(data, channels, rate);
            var cut = AudioLevelProbe.Measure(clunk, channels, rate);
            Debug.LogFormat("[DoorClipTrimmer] wrote {0}: {1:0.000}s of {2:0.000}s, {3} ch {4} Hz. " +
                            "dBFS peak/max100ms/rms source {5:0.0}/{6:0.0}/{7:0.0} -> clunk {8:0.0}/{9:0.0}/{10:0.0}",
                ClunkPath, clunk.Length / channels / (float)rate, data.Length / channels / (float)rate, channels, rate,
                full.PeakDb, full.Max100msDb, full.RmsDb, cut.PeakDb, cut.Max100msDb, cut.RmsDb);
        }

        /// <summary>
        /// Interleaved samples 0..<paramref name="endSeconds"/>, raised-cosine fade-in over the first
        /// <paramref name="fadeIn"/> s (first frame exactly 0) and fade-out over the last <paramref name="fadeOut"/> s
        /// (last frame exactly 0).
        /// </summary>
        public static float[] Trim(float[] interleaved, int channels, int rate, float endSeconds, float fadeIn, float fadeOut)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            int frames = interleaved.Length / channels;
            int keep = Mathf.Clamp(Mathf.RoundToInt(endSeconds * rate), 1, frames);
            int fin = Mathf.Clamp(Mathf.RoundToInt(fadeIn * rate), 0, keep);
            int fout = Mathf.Clamp(Mathf.RoundToInt(fadeOut * rate), 0, keep);
            var o = new float[keep * channels];
            for (int f = 0; f < keep; f++)
            {
                float g = 1f;
                if (fin > 1 && f < fin) g *= Ease(f / (float)(fin - 1));
                int fromEnd = keep - 1 - f;
                if (fout > 1 && fromEnd < fout) g *= Ease(fromEnd / (float)(fout - 1));
                for (int c = 0; c < channels; c++) o[f * channels + c] = interleaved[f * channels + c] * g;
            }
            return o;
        }

        /// <summary>0 -> 0, 1 -> 1, zero slope at both ends (no click at the fade edges).</summary>
        static float Ease(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(t));

        /// <summary>RIFF/WAVE: PCM 16/24/32-bit integer or 32-bit float (plain or WAVE_FORMAT_EXTENSIBLE).</summary>
        public static bool TryReadWav(byte[] b, out float[] data, out int channels, out int rate, out string error)
        {
            data = null; channels = 0; rate = 0; error = null;
            if (b.Length < 12 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE") { error = "not RIFF/WAVE"; return false; }
            int format = 0, bits = 0, pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Ascii(b, pos);
                int size = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > b.Length) size = b.Length - body; // truncated last chunk: read what is there
                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(b, body + 24); // sub-format GUID head
                }
                else if (id == "data")
                {
                    if (channels < 1 || rate < 1) { error = "data before fmt"; return false; }
                    int bytes = bits / 8;
                    if (!(format == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(format == 3 && bits == 32))
                    { error = "unsupported format " + format + "/" + bits + "-bit"; return false; }
                    int n = size / bytes / channels * channels;
                    data = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int p = body + i * bytes;
                        data[i] = format == 3 ? BitConverter.ToSingle(b, p)
                            : bits == 16 ? BitConverter.ToInt16(b, p) / 32768f
                            : bits == 24 ? ((b[p] | (b[p + 1] << 8) | ((sbyte)b[p + 2] << 16)) / 8388608f)
                            : (float)(BitConverter.ToInt32(b, p) / 2147483648.0);
                    }
                    return true;
                }
                pos = body + size + (size & 1);
            }
            error = "no data chunk";
            return false;
        }

        /// <summary>Interleaved floats -> 16-bit PCM RIFF/WAVE bytes (clamped to -1..1, rounded).</summary>
        public static byte[] WriteWav16(float[] interleaved, int channels, int rate)
        {
            int dataBytes = interleaved.Length * 2;
            using (var ms = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                foreach (float v in interleaved)
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
                w.Flush();
                return ms.ToArray();
            }
        }

        static string Ascii(byte[] b, int at) => at + 4 <= b.Length ? System.Text.Encoding.ASCII.GetString(b, at, 4) : "";
    }
}
