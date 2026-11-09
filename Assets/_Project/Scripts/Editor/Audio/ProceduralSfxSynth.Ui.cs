using System;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// UI / presentation cues (our own synthesis).
    ///
    /// <b>gold_tink.wav</b> (UiSound Tap, also the soft layer on the button click; 0.25 s file, audible ~0.12 s): a tiny
    /// lucky-gold coin "tink" - a 1.5 ms high-passed noise tick (the coin edge) + a D7 ping and an A7 ping 18 ms later,
    /// each with struck-metal partials 1 / 2.32 / 4.25 (upper ones decaying faster, tau 40 / 28 ms), 1.5 ms raised-cosine
    /// attack; a raised-cosine gate 0.08-0.13 s closes it, the rest is silence (WebGL needs clips >= 0.25 s).
    ///
    /// <b>menu_reveal.wav</b> (UiSound Reveal, 0.85 s): a magical rising shimmer - two sines a fifth apart gliding
    /// 520 -> 1560 Hz with a 7 Hz vibrato, swelling to 0.30 s, with a band-passed (2-7 kHz) "glitter" noise bed and six
    /// tiny pentatonic pings climbing G6..G7 - blooming at 0.30 s into a soft golden bell (C6 + G6 + E7, each a pair
    /// detuned +-1.5 Hz so it shimmers) that rings out.
    ///
    /// <b>scenic_whoosh.wav</b> (establishing shot zooming back in, 0.9 s): a soft air whoosh - white noise through a
    /// state-variable band-pass whose centre sweeps 250 -> 2200 Hz (exponential), swelling to 0.62 s and closing
    /// quickly after, plus a little of its low-pass for body. No tone.
    /// </summary>
    public static partial class ProceduralSfxSynth
    {
        public const string GoldTinkPath = Folder + "/gold_tink.wav";
        public const string MenuRevealPath = Folder + "/menu_reveal.wav";
        public const string ScenicWhooshPath = Folder + "/scenic_whoosh.wav";

        /// <summary>The tink is audible this long; the file is padded with silence to <see cref="UiClipSeconds"/>.</summary>
        public const float TinkAudibleSeconds = 0.13f;
        /// <summary>Length of the tink file: WebGL's AAC decode rejects clips of only a few frames (AudioLibraryBuilder.MinClipSeconds).</summary>
        public const float UiClipSeconds = 0.25f;
        public const float RevealSeconds = 0.85f;
        public const float WhooshSeconds = 0.9f;

        static void BuildUi()
        {
            Write(GoldTinkPath, GoldTink());
            Write(MenuRevealPath, MenuReveal());
            Write(ScenicWhooshPath, ScenicWhoosh());
        }

        static readonly float[] CoinMul = { 1f, 2.32f, 4.25f };
        static readonly float[] CoinLvl = { 1f, 0.32f, 0.12f };

        /// <summary>A struck-metal ping: <see cref="CoinMul"/> partials, partial p decaying with tau / mul^0.7.</summary>
        static void AddCoinPing(float[] o, float start, float freq, float amp, float tau, float attackSeconds)
        {
            int s0 = Mathf.RoundToInt(start * Rate);
            int attack = Mathf.Max(1, Mathf.RoundToInt(attackSeconds * Rate));
            for (int i = s0; i < o.Length; i++)
            {
                double t = (i - s0) / (double)Rate;
                double atk = i - s0 < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * (i - s0) / attack) : 1.0;
                double v = 0;
                for (int p = 0; p < CoinMul.Length; p++)
                {
                    if (freq * CoinMul[p] > Rate * 0.45f) continue;
                    v += CoinLvl[p] * Math.Exp(-t * Math.Pow(CoinMul[p], 0.7) / tau) * Math.Sin(2 * Math.PI * freq * CoinMul[p] * t);
                }
                o[i] += (float)(amp * atk * v);
            }
        }

        /// <summary>UiSound Tap: the gold "tink" (see class summary).</summary>
        public static float[] GoldTink()
        {
            var o = new float[Mathf.RoundToInt(UiClipSeconds * Rate)];
            // the coin edge: 1.5 ms of first-differenced (bright) noise under a raised-cosine window
            var rng = new Noise(0x60D71C4Bu);
            int tick = Mathf.RoundToInt(0.0015f * Rate);
            float prev = 0f;
            for (int i = 0; i < tick; i++)
            {
                float w = rng.Next();
                double win = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (tick - 1));
                o[i] += (float)(0.18 * win * (w - prev));
                prev = w;
            }
            AddCoinPing(o, 0.000f, 2349.32f, 1.00f, 0.040f, 0.0015f); // D7
            AddCoinPing(o, 0.018f, 3520.00f, 0.45f, 0.028f, 0.0015f); // A7: the "ti-ting" sparkle
            // close it: raised-cosine gate 0.08 -> TinkAudibleSeconds, silence after
            int g0 = Mathf.RoundToInt(0.08f * Rate), g1 = Mathf.RoundToInt(TinkAudibleSeconds * Rate);
            for (int i = g0; i < o.Length; i++)
                o[i] *= i >= g1 ? 0f : (float)(0.5 + 0.5 * Math.Cos(Math.PI * (i - g0) / (g1 - g0)));
            return Normalize(o, PeakDb);
        }

        static readonly float[] RevealPings = { 1567.98f, 1760.00f, 2093.00f, 2349.32f, 2637.02f, 3135.96f }; // G6 A6 C7 D7 E7 G7

        /// <summary>UiSound Reveal: rising shimmer blooming into a soft golden bell (see class summary).</summary>
        public static float[] MenuReveal()
        {
            var o = new float[Mathf.RoundToInt(RevealSeconds * Rate)];
            const double peakAt = 0.30;
            var rng = new Noise(0x2E7EA1u);
            double ph = 0, lp1 = 0, lp2 = 0, hp = 0, aLp = Lp(7000f), aHp = Lp(2000f);
            for (int i = 0; i < o.Length; i++)
            {
                double t = i / (double)Rate;
                double f = 520.0 + 1040.0 * SmoothStep(t / 0.40);
                f *= 1.0 + 0.01 * Math.Sin(2 * Math.PI * 7.0 * t);
                ph += 2 * Math.PI * f / Rate;
                // swell in, then a gaussian fall as the bell takes over
                double g = t < peakAt ? SmoothStep(t / peakAt) : Math.Exp(-Math.Pow((t - peakAt) / 0.13, 2));
                double tone = Math.Sin(ph) + 0.45 * Math.Sin(1.5 * ph);
                // glitter: band-passed noise riding the same swell
                double w = rng.Next();
                lp1 += aLp * (w - lp1);
                lp2 += aLp * (lp1 - lp2);
                hp += aHp * (lp2 - hp);
                o[i] += (float)(g * (0.20 * tone + 0.10 * (lp2 - hp)));
            }
            for (int j = 0; j < RevealPings.Length; j++)
                AddBell(o, 0.04f + j * 0.042f, RevealPings[j], 0.10f + j * 0.015f, 0.07f);
            // the golden bell burst
            float[] bell = { 1046.50f, 1567.98f, 2637.02f };
            float[] amp = { 0.42f, 0.26f, 0.10f };
            for (int j = 0; j < bell.Length; j++)
            {
                AddSoftBell(o, (float)peakAt, bell[j] - 1.5f, amp[j], 0.38f, 0.008f);
                AddSoftBell(o, (float)peakAt, bell[j] + 1.5f, amp[j], 0.38f, 0.008f);
            }
            FadeIn(o, 0.005f);
            FadeOut(o, 0.12f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Establishing shot zooming back in: a soft band-passed noise whoosh (see class summary).</summary>
        public static float[] ScenicWhoosh()
        {
            var o = new float[Mathf.RoundToInt(WhooshSeconds * Rate)];
            const double peakAt = 0.62, closeEnd = 0.86;
            var rng = new Noise(0x5CE71Cu);
            double low = 0, band = 0;
            const double damp = 1.1; // SVF damping (1/Q): broad, airy
            for (int i = 0; i < o.Length; i++)
            {
                double t = i / (double)Rate;
                double fc = 250.0 * Math.Pow(2200.0 / 250.0, Math.Min(1.0, t / peakAt));
                double fq = 2.0 * Math.Sin(Math.PI * fc / Rate);
                double w = rng.Next();
                low += fq * band;
                double high = w - low - damp * band;
                band += fq * high;
                double env = t < peakAt ? Math.Pow(SmoothStep(t / peakAt), 1.6)
                    : t < closeEnd ? 0.5 + 0.5 * Math.Cos(Math.PI * (t - peakAt) / (closeEnd - peakAt)) : 0.0;
                o[i] = (float)(env * (band + 0.35 * low));
            }
            FadeOut(o, 0.03f);
            return Normalize(o, PeakDb);
        }
    }
}
