using System;
using System.Collections.Generic;
using System.Text;
using FloorF.Audio;
using FloorF.Core;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Objective mix audit (nobody can listen): renders every clip the game plays - the AudioLibrary asset rows (imported
    /// and editor-procedural), the music tracks per ending, the hum / rain beds and every runtime winter clip
    /// (<see cref="WinterBank"/>) - at the level it is played at, and measures sample peak, DC offset, rms, loudest
    /// 100 ms, BS.1770 K-weighted loudness ("LUFS-ish", mono: momentary = loudest 400 ms, short-term = loudest 3 s,
    /// integrated = gated) and, for loops, the wrap-around seam (sample step and level jump). Read-only.
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.AudioMixAudit.Run  (look for "[AudioMixAudit]")
    /// </summary>
    public static class AudioMixAudit
    {
        public readonly struct Stats
        {
            public readonly float PeakDb, DcDb, RmsDb, Max100msDb, MomentaryLufs, ShortTermLufs, IntegratedLufs;
            /// <summary>Loops: |last - first| sample step / the clip's 99.9th percentile step (&lt;= 1 = no click).</summary>
            public readonly float SeamRatio;
            /// <summary>Loops: rms of the last 20 ms vs the first 20 ms (dB, 0 = no level jump at the seam).</summary>
            public readonly float SeamJumpDb;
            public readonly float Seconds;

            public Stats(float peak, float dc, float rms, float max100, float mom, float st, float integ, float seam, float jump, float sec)
            {
                PeakDb = peak; DcDb = dc; RmsDb = rms; Max100msDb = max100; MomentaryLufs = mom; ShortTermLufs = st;
                IntegratedLufs = integ; SeamRatio = seam; SeamJumpDb = jump; Seconds = sec;
            }
        }

        public sealed class Row
        {
            public string Name;
            public string Group;
            public bool Loop;
            /// <summary>Level it is played at (cue volume / musicVolume x track volume x phase level / bed volume).</summary>
            public float Gain;
            /// <summary>How the samples were read (imported clip / source file + mono-normalize model / runtime).</summary>
            public string Source;
            /// <summary>Asset path ("" for runtime clips).</summary>
            public string Path;
            /// <summary>Music: the track's loop-seam fade (s) - a seam the game fades over, not a click.</summary>
            public float SeamFade;
            public Stats File;
            public Stats Played;
        }

        [MenuItem("Floor F/Audio/Log Mix Audit")]
        public static void Run()
        {
            var lib = AudioLibrary.Load();
            if (lib == null) { Debug.LogError("[AudioMixAudit] no AudioLibrary"); return; }
            Debug.Log(Format(Collect(lib)));
        }

        public static string Format(List<Row> rows)
        {
            var sb = new StringBuilder("[AudioMixAudit] as played, dBFS / LUFS-ish (mono K-weighted): peak | dc | rms | max100 | momentary | short-term | integrated | seam\n");
            foreach (var r in rows)
            {
                var p = r.Played;
                sb.AppendFormat("{0,-34} {1,6:0.0} {2,6:0} {3,6:0.0} {4,6:0.0} | M {5,6:0.0} S {6,6:0.0} I {7,6:0.0} {8} (x{9:0.###}, {10:0.0}s, {11})\n",
                    r.Group + "/" + r.Name, p.PeakDb, p.DcDb, p.RmsDb, p.Max100msDb, p.MomentaryLufs, p.ShortTermLufs, p.IntegratedLufs,
                    r.Loop ? string.Format("seam {0:0.00} jump {1:+0.0;-0.0}dB", p.SeamRatio, p.SeamJumpDb) : "",
                    r.Gain, p.Seconds, r.Source);
            }
            return sb.ToString();
        }

        /// <summary>Every clip the game plays, measured at its played level.</summary>
        public static List<Row> Collect(AudioLibrary lib)
        {
            var rows = new List<Row>();
            foreach (var c in lib.cues)
            {
                if (c == null || c.clips == null) continue;
                for (int i = 0; i < c.clips.Length; i++) Add(rows, "sfx", c.cue + (c.clips.Length > 1 ? "#" + i : ""), c.clips[i], c.volume, false);
                if (c.layer != null) Add(rows, "sfx", c.cue + " layer", c.layer, c.volume * c.layerVolume, false);
            }
            foreach (DayPhase ph in new[] { DayPhase.Running, DayPhase.Shop })
            {
                var s = AudioRules.MusicFor(ph, EndingKind.None);
                var t = lib.FindAny(s.Track);
                if (t != null) Add(rows, "music", ph + " " + s.Track, t.clip, lib.musicVolume * t.volume * s.Volume, true, t.seamFade);
            }
            foreach (EndingKind e in Enum.GetValues(typeof(EndingKind)))
            {
                if (e == EndingKind.None) continue;
                var s = AudioRules.MusicFor(DayPhase.Ending, e);
                var t = lib.FindAny(s.Track);
                if (t != null) Add(rows, "ending", e + " " + s.Track, t.clip, lib.musicVolume * t.volume * s.Volume, true, t.seamFade);
            }
            Add(rows, "bed", "travel hum", lib.travelHum, lib.travelHumVolume, true);
            Add(rows, "bed", "rain loop", lib.rainLoop, lib.rainVolume, true);
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                if (!WinterBank.Has(cue)) continue;
                var d = WinterBank.Find(cue);
                Add(rows, "winter", cue.ToString(), d.clips[0], d.volume, false);
            }
            Add(rows, "winter", "wind (full snow)", WinterBank.WindLoop, WinterBank.WindVolume, true);
            Add(rows, "winter", "christmas bells layer", WinterBank.ChristmasBells, WinterBank.ChristmasBellsVolume, true);
            return rows;
        }

        /// <summary>The row named <paramref name="nameStart"/> (whole words: "Wish" is not "WishStar"; "Tip" = its first variant).</summary>
        public static Row Find(List<Row> rows, string group, string nameStart)
        {
            foreach (var r in rows)
                if (r.Group == group && (r.Name == nameStart || r.Name.StartsWith(nameStart + " ", StringComparison.Ordinal)
                                         || r.Name.StartsWith(nameStart + "#", StringComparison.Ordinal))) return r;
            return null;
        }

        static void Add(List<Row> rows, string group, string name, AudioClip clip, float gain, bool loop, float seamFade = 0f)
        {
            if (clip == null) return;
            if (!TryMono(clip, out var mono, out int rate, out string how)) { rows.Add(new Row { Name = name, Group = group, Source = "unreadable", Gain = gain, Loop = loop }); return; }
            var file = Analyze(mono, rate, 1f, loop);
            rows.Add(new Row { Name = name, Group = group, Loop = loop, Gain = gain, Source = how, Path = AssetDatabase.GetAssetPath(clip), SeamFade = seamFade, File = file, Played = Analyze(mono, rate, gain, loop) });
        }

        /// <summary>
        /// The clip as the game hears it, mono. Decompressed / runtime clips: their own samples. Compressed-in-memory clips
        /// (music) cannot be read back: the source file, downmixed and - when the importer forces a multi-channel file to
        /// mono with Normalize, as ProjectSetupTool sets the third-party files - peak-normalized like Unity's downmix.
        /// </summary>
        public static bool TryMono(AudioClip clip, out float[] mono, out int rate, out string how)
        {
            mono = null; rate = clip.frequency; how = "";
            string path = AssetDatabase.GetAssetPath(clip);
            bool runtime = string.IsNullOrEmpty(path);
            if (runtime || clip.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var d = new float[clip.samples * Mathf.Max(1, clip.channels)];
                if (clip.GetData(d, 0))
                {
                    mono = Downmix(d, Mathf.Max(1, clip.channels));
                    how = runtime ? "runtime" : "imported";
                    return true;
                }
            }
            return TrySourceModel(path, out mono, out rate, out how);
        }

        /// <summary>The source file of <paramref name="path"/> as the importer would hand it to the game (see <see cref="TryMono"/>).</summary>
        public static bool TrySourceModel(string path, out float[] mono, out int rate, out string how)
        {
            mono = null; rate = 0; how = "";
            if (!AudioLevelProbe.TryDecode(path, out var src, out int ch, out int srcRate)) return false;
            mono = Downmix(src, ch);
            rate = srcRate;
            how = "source " + ch + "ch";
            // Unity normalizes only while mixing several channels down (a mono file keeps its level - checked against
            // the decompressed third-party SFX: imported = this model within ~1 dB, Vorbis)
            if (ch > 1 && AssetImporter.GetAtPath(path) is AudioImporter ai && ai.forceToMono)
            {
                var norm = new SerializedObject(ai).FindProperty("m_Normalize");
                if (norm == null || norm.boolValue)
                {
                    float peak = 0f;
                    foreach (float v in mono) peak = Mathf.Max(peak, Mathf.Abs(v));
                    if (peak > 0f) for (int i = 0; i < mono.Length; i++) mono[i] /= peak;
                    how += "+mono-normalize";
                }
            }
            return true;
        }

        static float[] Downmix(float[] d, int ch)
        {
            if (ch == 1) return d;
            int n = d.Length / ch;
            var m = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = 0f;
                for (int c = 0; c < ch; c++) v += d[i * ch + c];
                m[i] = v / ch;
            }
            return m;
        }

        // ------------------------------------------------------------------ measures

        static float Db(double x) => (float)(20.0 * Math.Log10(Math.Max(x, 1e-9)));
        static float Lufs(double meanSquare) => (float)(-0.691 + 10.0 * Math.Log10(Math.Max(meanSquare, 1e-18)));

        /// <summary>BS.1770 K-weighting (pre-filter shelf + RLB high-pass) for any rate. Loops: filtered twice, the second pass kept (settled state).</summary>
        public static double[] KWeight(float[] x, int rate, float gain, bool loop)
        {
            double K = Math.Tan(Math.PI * 1681.974450955533 / rate), Q = 0.7071752369554196;
            double Vh = Math.Pow(10, 3.999843853973347 / 20), Vb = Math.Pow(Vh, 0.4996667741545416);
            double a0 = 1 + K / Q + K * K;
            double sb0 = (Vh + Vb * K / Q + K * K) / a0, sb1 = 2 * (K * K - Vh) / a0, sb2 = (Vh - Vb * K / Q + K * K) / a0;
            double sa1 = 2 * (K * K - 1) / a0, sa2 = (1 - K / Q + K * K) / a0;
            double K2 = Math.Tan(Math.PI * 38.13547087602444 / rate), Q2 = 0.5003270373238773, a02 = 1 + K2 / Q2 + K2 * K2;
            double ha1 = 2 * (K2 * K2 - 1) / a02, ha2 = (1 - K2 / Q2 + K2 * K2) / a02;
            var y = new double[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, z1 = 0, z2 = 0;
            for (int pass = 0; pass < (loop ? 2 : 1); pass++)
                for (int i = 0; i < x.Length; i++)
                {
                    double v = x[i] * (double)gain;
                    double s = sb0 * v + sb1 * x1 + sb2 * x2 - sa1 * y1 - sa2 * y2;
                    x2 = x1; x1 = v; y2 = y1; y1 = s;
                    double h = s - 2 * u1 + u2 - ha1 * z1 - ha2 * z2;
                    u2 = u1; u1 = s; z2 = z1; z1 = h;
                    y[i] = h;
                }
            return y;
        }

        /// <summary>Loudest mean square over a window (hop = window / 4); loops wrap around, one-shots are zero-padded.</summary>
        static double MaxWindow(double[] sq, int win, bool loop)
        {
            int n = sq.Length;
            if (n == 0) return 0;
            win = Math.Max(1, win);
            int hop = Math.Max(1, win / 4);
            // prefix sums over [0, n + win) (wrapped for loops, zeros after the end for one-shots)
            int len = n + win;
            var pre = new double[len + 1];
            for (int i = 0; i < len; i++) pre[i + 1] = pre[i] + (i < n ? sq[i] : loop ? sq[i % n] : 0.0);
            int last = loop ? n - 1 : Math.Max(0, n - win);
            double best = 0;
            for (int s = 0; s <= last; s += hop) best = Math.Max(best, (pre[s + win] - pre[s]) / win);
            return best;
        }

        /// <summary>BS.1770 gated integrated loudness from 400 ms blocks (75 % overlap): -70 LUFS absolute, -10 LU relative.</summary>
        static float Integrated(double[] sq, int rate, bool loop)
        {
            int win = (int)(0.4 * rate), hop = Math.Max(1, win / 4), n = sq.Length;
            var blocks = new List<double>();
            if (n < win) blocks.Add(Sum(sq, 0, n, loop) / win);
            else for (int s = 0; s + (loop ? 0 : win) <= n && s < n; s += hop) blocks.Add(Sum(sq, s, win, loop) / win);
            double abs = Math.Pow(10, (-70 + 0.691) / 10), m = 0; int k = 0;
            foreach (var b in blocks) if (b > abs) { m += b; k++; }
            if (k == 0) return -70f;
            double rel = m / k * Math.Pow(10, -1.0);
            m = 0; k = 0;
            foreach (var b in blocks) if (b > abs && b > rel) { m += b; k++; }
            return k == 0 ? -70f : Lufs(m / k);
        }

        static double Sum(double[] sq, int s, int len, bool loop)
        {
            double t = 0;
            for (int i = 0; i < len; i++)
            {
                int j = s + i;
                if (j >= sq.Length) { if (!loop) break; j %= sq.Length; }
                t += sq[j];
            }
            return t;
        }

        public static Stats Analyze(float[] x, int rate, float gain, bool loop)
        {
            int n = x.Length;
            double peak = 0, sum = 0, all = 0;
            var sq = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = x[i] * (double)gain;
                peak = Math.Max(peak, Math.Abs(v));
                sum += v;
                sq[i] = v * v;
                all += sq[i];
            }
            double max100 = MaxWindow(sq, rate / 10, loop);
            var k = KWeight(x, rate, gain, loop);
            var ksq = new double[n];
            for (int i = 0; i < n; i++) ksq[i] = k[i] * k[i];
            float mom = Lufs(MaxWindow(ksq, (int)(0.4 * rate), loop));
            float st = Lufs(MaxWindow(ksq, 3 * rate, loop));
            float integ = Integrated(ksq, rate, loop);
            float seam = 0f, jump = 0f;
            if (loop && n > 2)
            {
                var steps = new float[n - 1];
                for (int i = 0; i + 1 < n; i++) steps[i] = Mathf.Abs(x[i + 1] - x[i]);
                Array.Sort(steps);
                float p999 = Mathf.Max(1e-6f, steps[(int)((steps.Length - 1) * 0.999)]);
                seam = Mathf.Abs(x[0] - x[n - 1]) / p999;
                int w = Math.Max(1, Math.Min(n / 2, rate / 50));
                double head = 0, tail = 0;
                for (int i = 0; i < w; i++) { head += x[i] * (double)x[i]; tail += x[n - 1 - i] * (double)x[n - 1 - i]; }
                jump = Db(Math.Sqrt(head / w)) - Db(Math.Sqrt(tail / w));
            }
            return new Stats(Db(peak), Db(Math.Abs(sum / Math.Max(1, n))), Db(Math.Sqrt(all / Math.Max(1, n))), Db(Math.Sqrt(max100)),
                mom, st, integ, seam, jump, n / (float)rate);
        }
    }
}
