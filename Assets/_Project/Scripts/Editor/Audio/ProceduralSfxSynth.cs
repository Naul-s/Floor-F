using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Our own procedural clips (no third-party source, no AI): written as 16-bit mono WAV under
    /// <see cref="Folder"/> and imported like the other SFX (Vorbis, Decompress On Load). Idempotent.
    ///
    /// <b>elevator_hum_loop.wav</b> - the car's travel sound ("wing-wing, wee-wing"): additive synthesis only (no noise,
    /// band-limited by construction - top partial 880 Hz), so it is clean. 110 Hz fundamental + harmonics 2,3,4,6,8 with a
    /// falling spectrum; a 2.5 Hz "wing" wobble moves level, brightness (upper partials follow it harder) and pitch
    /// (+-0.6 %), and a 1.25 Hz half-rate term makes every other wing a little higher/brighter ("wee-wing").
    /// 3.2 s = 352 fundamental cycles = 8 wings = 4 wee-wing pairs: every component is periodic in the clip length, and
    /// all partials are sines starting at phase 0, so the loop seam is a zero crossing with matching slope (no click).
    ///
    /// <b>wish_star_twinkle.wav</b> - wish star appears: soft rising glissando shimmer under six small pentatonic bell
    /// pings climbing C6..C7. <b>wish_chime.wav</b> - wish made: a bright C-major bell arpeggio (C6 E6 G6 C7) with a
    /// short high sparkle on top, ringing out over ~1.8 s. <b>star_sparkle.wav</b> - star-night layer on Lucky: three
    /// tiny high pings (E7 G7 B7) in 0.6 s.
    ///
    /// Rain loop + thunder rumbles: ProceduralSfxSynth.Weather.cs. Shop cues (charm bell, shield chime, sticker peel):
    /// ProceduralSfxSynth.Shop.cs. UI cues (gold tink, menu reveal, scenic whoosh): ProceduralSfxSynth.Ui.cs.
    ///
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.ProceduralSfxSynth.Build
    /// then AudioLibraryBuilder.Build.
    /// </summary>
    public static partial class ProceduralSfxSynth
    {
        public const string Folder = "Assets/_Project/Audio/Generated";
        public const string HumPath = Folder + "/elevator_hum_loop.wav";
        public const string WishStarPath = Folder + "/wish_star_twinkle.wav";
        public const string WishChimePath = Folder + "/wish_chime.wav";
        public const string StarSparklePath = Folder + "/star_sparkle.wav";

        public const int Rate = 44100;
        public const float HumFundamental = 110f;
        public const float HumWingHz = 2.5f;
        public const float HumSeconds = 3.2f;
        /// <summary>File peak (dBFS) of every generated clip; the playing level is set by the cue/hum volume.</summary>
        public const float PeakDb = -3f;

        [MenuItem("Floor F/Audio/Synthesize Procedural Clips")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetFullPath(Folder));
            Write(HumPath, Hum());
            Write(WishStarPath, WishStarTwinkle());
            Write(WishChimePath, WishChime());
            Write(StarSparklePath, StarSparkle());
            BuildWeather();
            BuildShop();
            BuildUi();
        }

        static void Write(string assetPath, float[] mono)
        {
            File.WriteAllBytes(Path.GetFullPath(assetPath), DoorClipTrimmer.WriteWav16(mono, 1, Rate));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(assetPath);
            var l = AudioLevelProbe.Measure(mono, 1, Rate);
            Debug.LogFormat("[ProceduralSfxSynth] wrote {0}: {1:0.000}s mono {2} Hz, dBFS peak/max100ms/rms {3:0.0}/{4:0.0}/{5:0.0}",
                assetPath, mono.Length / (float)Rate, Rate, l.PeakDb, l.Max100msDb, l.RmsDb);
        }

        /// <summary>Mono already: no downmix (forceToMono would also normalize and change the level we set).</summary>
        static void ConfigureImporter(string assetPath)
        {
            var ai = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (ai == null) throw new InvalidOperationException("[ProceduralSfxSynth] no AudioImporter for " + assetPath);
            ai.forceToMono = false;
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }

        // ------------------------------------------------------------------ hum

        /// <summary>The travel loop (see class summary). Exactly <see cref="HumSeconds"/> x <see cref="Rate"/> samples.</summary>
        public static float[] Hum()
        {
            int n = Mathf.RoundToInt(HumSeconds * Rate);
            var o = new float[n];
            // partial number, relative level, how hard its level follows the wing wobble (brighter on each wing)
            int[] k = { 1, 2, 3, 4, 6, 8 };
            float[] a = { 1f, 0.55f, 0.30f, 0.18f, 0.08f, 0.045f };
            float[] d = { 0.10f, 0.25f, 0.40f, 0.55f, 0.70f, 0.85f };
            const float wee = HumWingHz * 0.5f;
            const float pitchWing = 0.006f, pitchWee = 0.004f;
            double w1 = 2.0 * Math.PI * HumWingHz, w2 = 2.0 * Math.PI * wee, f0 = 2.0 * Math.PI * HumFundamental;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                // wobble -1..1: the wing plus the half-rate "wee" accent
                double s = 0.75 * Math.Cos(w1 * t) + 0.25 * Math.Cos(w2 * t);
                // phase of the fundamental with the FM integrated exactly (sin terms are 0 at 0 and at the clip end)
                double ph = f0 * t + HumFundamental * pitchWing / HumWingHz * Math.Sin(w1 * t)
                                   + HumFundamental * pitchWee / wee * Math.Sin(w2 * t);
                double env = 0.80 + 0.20 * s;
                double v = 0;
                for (int p = 0; p < k.Length; p++) v += a[p] * (1.0 + d[p] * s) * Math.Sin(k[p] * ph);
                o[i] = (float)(env * v);
            }
            return Normalize(o, PeakDb);
        }

        // ------------------------------------------------------------------ wish clips

        static readonly float[] Pentatonic = { 1046.50f, 1174.66f, 1318.51f, 1567.98f, 1760.00f, 2093.00f }; // C6 D6 E6 G6 A6 C7

        /// <summary>Wish star appears (~1.5 s): soft rising shimmer + six climbing pentatonic bell pings.</summary>
        public static float[] WishStarTwinkle()
        {
            const float len = 1.5f;
            var o = new float[Mathf.RoundToInt(len * Rate)];
            // shimmer: one sine gliding 700 -> 1500 Hz with a light vibrato, bell-shaped envelope (peak 0.45 s)
            double ph = 0;
            for (int i = 0; i < o.Length; i++)
            {
                double t = i / (double)Rate;
                double f = 700.0 + 800.0 * SmoothStep(t / 0.9) + 12.0 * Math.Sin(2 * Math.PI * 6.0 * t);
                ph += 2 * Math.PI * f / Rate;
                double g = Math.Exp(-Math.Pow((t - 0.45) / 0.22, 2));
                o[i] += (float)(0.22 * g * Math.Sin(ph));
            }
            for (int j = 0; j < Pentatonic.Length; j++)
                AddBell(o, 0.06f + j * 0.11f, Pentatonic[j], 0.55f - j * 0.04f, 0.22f);
            FadeOut(o, 0.05f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Wish made (~1.8 s): bright C-major bell arpeggio + a short high sparkle.</summary>
        public static float[] WishChime()
        {
            const float len = 1.8f;
            var o = new float[Mathf.RoundToInt(len * Rate)];
            float[] chord = { 1046.50f, 1318.51f, 1567.98f, 2093.00f };
            for (int j = 0; j < chord.Length; j++) AddBell(o, 0.005f + j * 0.06f, chord[j], 0.8f, 0.55f);
            AddBell(o, 0.30f, 2637.02f, 0.25f, 0.18f); // E7
            AddBell(o, 0.38f, 3135.96f, 0.20f, 0.15f); // G7
            FadeOut(o, 0.08f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Star-night Lucky layer (0.6 s): three tiny high pings.</summary>
        public static float[] StarSparkle()
        {
            var o = new float[Mathf.RoundToInt(0.6f * Rate)];
            AddBell(o, 0.005f, 2637.02f, 0.6f, 0.12f);
            AddBell(o, 0.075f, 3135.96f, 0.5f, 0.10f);
            AddBell(o, 0.145f, 3951.07f, 0.35f, 0.08f);
            FadeOut(o, 0.04f);
            return Normalize(o, PeakDb);
        }

        /// <summary>
        /// A small bell at <paramref name="start"/> s: fundamental + 2.0x / 3.0x harmonics and a 4.2x inharmonic shimmer,
        /// upper partials decaying faster; 4 ms raised-cosine attack (no click).
        /// </summary>
        internal static void AddBell(float[] o, float start, float freq, float amp, float tau)
        {
            float[] mul = { 1f, 2f, 3f, 4.2f };
            float[] lvl = { 1f, 0.35f, 0.12f, 0.08f };
            int s0 = Mathf.RoundToInt(start * Rate);
            int attack = Mathf.RoundToInt(0.004f * Rate);
            for (int i = s0; i < o.Length; i++)
            {
                double t = (i - s0) / (double)Rate;
                double atk = i - s0 < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * (i - s0) / attack) : 1.0;
                double v = 0;
                for (int p = 0; p < mul.Length; p++)
                {
                    if (freq * mul[p] > Rate * 0.45f) continue;
                    v += lvl[p] * Math.Exp(-t * mul[p] / tau) * Math.Sin(2 * Math.PI * freq * mul[p] * t);
                }
                o[i] += (float)(amp * atk * v);
            }
        }

        internal static double SmoothStep(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        /// <summary>Raised-cosine fade over the last <paramref name="seconds"/>; the last sample is exactly 0.</summary>
        internal static void FadeOut(float[] o, float seconds)
        {
            int f = Mathf.Clamp(Mathf.RoundToInt(seconds * Rate), 2, o.Length);
            for (int i = 0; i < f; i++)
            {
                int idx = o.Length - 1 - i;
                o[idx] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / (f - 1)));
            }
        }

        internal static float[] Normalize(float[] o, float peakDb)
        {
            float peak = 0f;
            foreach (float v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak <= 0f) return o;
            float g = Mathf.Pow(10f, peakDb / 20f) / peak;
            for (int i = 0; i < o.Length; i++) o[i] *= g;
            return o;
        }
    }
}
