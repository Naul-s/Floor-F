using System;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Rainy-day ambience (our own noise synthesis, deterministic seeds - same files every build).
    ///
    /// <b>rain_loop.wav</b> (8 s, seamless): a pink-noise bed (Paul Kellet's filter) + 0.6 brown noise for body, low-passed
    /// twice at 4.2 kHz (no hiss / harshness) and high-passed at 90 Hz (no rumble). The tail of a longer render is
    /// equal-power crossfaded into the head (0.5 s), so the seam is an ordinary sample step. A slow swell (+-10 % at
    /// 1/8 Hz, +-5 % at 3/8 Hz) is periodic in the loop length. Bed normalized to <see cref="RainRmsDb"/>; on top
    /// ~5.5 soft droplet "plips" per second: 30 ms damped sines at 1.8-4.2 kHz that chirp up 25 % (a bubble's
    /// resonance rising), 1.5 ms soft attack, 0.8-2.2x the bed rms, wrapped around the loop end (seamless).
    ///
    /// <b>thunder_rumble_1..3.wav</b> (4.2-5 s): brown noise low-passed (2-pole) at 150 Hz plus half of its 200-600 Hz band
    /// for texture on small speakers, high-passed at 35 Hz; rolled by 2-3 overlapping swells with slow raised-cosine rises
    /// (0.35-0.6 s - no crack, never startling) and exponential decays, and a slow random (3 Hz) amplitude roll.
    /// Peak -3 dBFS; played quietly (AudioLibraryBuilder.ThunderVolume).
    /// </summary>
    public static partial class ProceduralSfxSynth
    {
        public const string RainPath = Folder + "/rain_loop.wav";
        public const float RainSeconds = 8f;
        public const float RainCrossfadeSeconds = 0.5f;
        /// <summary>RMS (dBFS) of the noise bed in the rain file, before the droplets.</summary>
        public const float RainRmsDb = -20f;
        public const float RainLowpassHz = 4200f;
        public const int ThunderVariants = 3;

        public static string ThunderPath(int variant) => Folder + "/thunder_rumble_" + (variant + 1) + ".wav";

        static void BuildWeather()
        {
            Write(RainPath, Rain());
            for (int v = 0; v < ThunderVariants; v++) Write(ThunderPath(v), Thunder(v));
        }

        /// <summary>Small deterministic white-noise source (xorshift32), -1..1.</summary>
        internal sealed class Noise
        {
            uint s;
            public Noise(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }

            public float Next()
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return (s >> 8) * (2f / 16777216f) - 1f;
            }

            /// <summary>Uniform lo..hi.</summary>
            public float Range(float lo, float hi) => lo + (Next() * 0.5f + 0.5f) * (hi - lo);
        }

        /// <summary>One-pole low-pass coefficient for <paramref name="hz"/>.</summary>
        static double Lp(float hz) => 1.0 - Math.Exp(-2.0 * Math.PI * hz / Rate);

        // ------------------------------------------------------------------ rain

        /// <summary>The seamless rain loop (see class summary): exactly <see cref="RainSeconds"/> x <see cref="Rate"/> samples.</summary>
        public static float[] Rain()
        {
            int n = Mathf.RoundToInt(RainSeconds * Rate);
            int f = Mathf.RoundToInt(RainCrossfadeSeconds * Rate);
            var rng = new Noise(0x5EED4A1Fu);
            var x = new float[n + f];
            double b0 = 0, b1 = 0, b2 = 0, br = 0, lp1 = 0, lp2 = 0, hp = 0;
            double aLp = Lp(RainLowpassHz), aHp = Lp(90f);
            for (int i = -Rate; i < x.Length; i++) // 1 s warm-up: the filters start settled
            {
                double w = rng.Next();
                b0 = 0.99765 * b0 + w * 0.0990460;
                b1 = 0.96300 * b1 + w * 0.2965164;
                b2 = 0.57000 * b2 + w * 1.0526913;
                double pink = (b0 + b1 + b2 + w * 0.1848) * 0.11;
                br = 0.997 * br + w * 0.04;
                double v = pink + 0.6 * br;
                lp1 += aLp * (v - lp1);
                lp2 += aLp * (lp1 - lp2);
                hp += aHp * (lp2 - hp);
                if (i >= 0) x[i] = (float)(lp2 - hp);
            }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < f)
                {
                    double th = 0.5 * Math.PI * i / f;
                    o[i] = (float)(x[n + i] * Math.Cos(th) + x[i] * Math.Sin(th));
                }
                else o[i] = x[i];
                double t = i / (double)n;
                o[i] *= (float)(1.0 + 0.10 * Math.Sin(2 * Math.PI * t) + 0.05 * Math.Sin(2 * Math.PI * 3 * t + 1.3));
            }
            NormalizeRms(o, RainRmsDb);
            float bed = Mathf.Pow(10f, RainRmsDb / 20f);
            int drops = Mathf.RoundToInt(5.5f * RainSeconds);
            for (int d = 0; d < drops; d++)
            {
                int start = Mathf.FloorToInt(rng.Range(0f, n - 1));
                float f0 = rng.Range(1800f, 4200f);
                float tau = rng.Range(0.005f, 0.012f);
                float amp = bed * rng.Range(0.8f, 2.2f);
                AddDroplet(o, start, f0, tau, amp);
            }
            float peak = 0f;
            foreach (float v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            float maxPeak = Mathf.Pow(10f, -1f / 20f);
            if (peak > maxPeak) for (int i = 0; i < n; i++) o[i] *= maxPeak / peak;
            return o;
        }

        /// <summary>A 30 ms droplet plip at <paramref name="start"/>, wrapped around the loop end.</summary>
        static void AddDroplet(float[] o, int start, float f0, float tau, float amp)
        {
            int len = Mathf.RoundToInt(0.03f * Rate);
            int attack = Mathf.RoundToInt(0.0015f * Rate);
            double ph = 0;
            for (int k = 0; k < len; k++)
            {
                double t = k / (double)Rate;
                double fr = f0 * (1.0 + 0.25 * k / len);
                ph += 2 * Math.PI * fr / Rate;
                double atk = k < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * k / attack) : 1.0;
                double tail = k > len - attack ? 0.5 - 0.5 * Math.Cos(Math.PI * (len - k) / attack) : 1.0;
                o[(start + k) % o.Length] += (float)(amp * atk * tail * Math.Exp(-t / tau) * Math.Sin(ph));
            }
        }

        // ------------------------------------------------------------------ thunder

        /// <summary>(peak time s, rise s, decay s, level) of each swell, per variant.</summary>
        static readonly float[][] ThunderSwells =
        {
            new[] { 0.55f, 0.45f, 0.9f, 1.0f, 1.45f, 0.40f, 1.2f, 0.75f, 2.6f, 0.6f, 1.0f, 0.40f },
            new[] { 0.65f, 0.50f, 1.4f, 1.0f, 1.95f, 0.50f, 1.3f, 0.60f },
            new[] { 0.45f, 0.40f, 0.7f, 0.8f, 1.05f, 0.35f, 0.9f, 1.0f, 2.05f, 0.5f, 0.9f, 0.45f },
        };
        static readonly float[] ThunderLengths = { 4.6f, 5.0f, 4.2f };

        /// <summary>One distant rumble (see class summary), variant 0..<see cref="ThunderVariants"/>-1.</summary>
        public static float[] Thunder(int variant)
        {
            variant = Mathf.Clamp(variant, 0, ThunderVariants - 1);
            int n = Mathf.RoundToInt(ThunderLengths[variant] * Rate);
            var o = new float[n];
            var rollBuf = new float[n];
            var rng = new Noise(0x7A0D3E11u + (uint)variant * 7919u);
            double br = 0, b1 = 0, b2 = 0, t1 = 0, t2 = 0, u1 = 0, u2 = 0, hp = 0, roll = 0, roll2 = 0;
            double aBody = Lp(150f), aTexHi = Lp(600f), aTexLo = Lp(200f), aHp = Lp(35f), aRoll = Lp(3f);
            for (int i = -Rate; i < n; i++)
            {
                double w = rng.Next();
                br = 0.998 * br + w * 0.05;
                b1 += aBody * (br - b1); b2 += aBody * (b1 - b2);
                t1 += aTexHi * (br - t1); t2 += aTexHi * (t1 - t2);
                u1 += aTexLo * (br - u1); u2 += aTexLo * (u1 - u2);
                double v = b2 + 0.5 * (t2 - u2);
                hp += aHp * (v - hp);
                roll += aRoll * (rng.Next() - roll); roll2 += aRoll * (roll - roll2);
                if (i < 0) continue;
                double t = i / (double)Rate;
                double env = 0;
                var sw = ThunderSwells[variant];
                for (int k = 0; k + 3 < sw.Length; k += 4)
                {
                    double peakT = sw[k], rise = sw[k + 1], decay = sw[k + 2], lvl = sw[k + 3];
                    if (t < peakT - rise) continue;
                    env += lvl * (t < peakT ? 0.5 - 0.5 * Math.Cos(Math.PI * (t - (peakT - rise)) / rise) : Math.Exp(-(t - peakT) / decay));
                }
                o[i] = (float)((v - hp) * env);
                rollBuf[i] = (float)roll2;
            }
            // the roll: 3 Hz-filtered noise scaled to unit rms, +-30 % around 1 (never below 0.4)
            NormalizeRms(rollBuf, 0f);
            for (int i = 0; i < n; i++) o[i] *= Mathf.Max(0.4f, 1f + 0.3f * rollBuf[i]);
            FadeIn(o, 0.25f);
            FadeOut(o, 0.4f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Raised-cosine fade over the first <paramref name="seconds"/>; the first sample is exactly 0.</summary>
        internal static void FadeIn(float[] o, float seconds)
        {
            int f = Mathf.Clamp(Mathf.RoundToInt(seconds * Rate), 2, o.Length);
            for (int i = 0; i < f; i++) o[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / (f - 1)));
        }

        internal static void NormalizeRms(float[] o, float rmsDb)
        {
            double sum = 0;
            foreach (float v in o) sum += v * (double)v;
            double rms = Math.Sqrt(sum / Math.Max(1, o.Length));
            if (rms <= 0) return;
            float g = (float)(Mathf.Pow(10f, rmsDb / 20f) / rms);
            for (int i = 0; i < o.Length; i++) o[i] *= g;
        }
    }
}
