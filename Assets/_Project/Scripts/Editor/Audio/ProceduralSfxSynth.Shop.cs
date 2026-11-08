using System;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Shop cues (our own synthesis; the reroll uses Kenney's card-shuffle instead).
    ///
    /// <b>charm_bell.wav</b> (CharmBought, 1.6 s): a soft temple-style bell - G5 then D6 0.11 s later; partials 1, 2, 2.76
    /// (bell-like inharmonic) and 5.4 at falling levels, upper ones decaying faster, 12 ms soft attack (no strike click).
    ///
    /// <b>charm_shield.wav</b> (CharmTriggered, 1.4 s): a protective "shield": a soft tonal swell gliding 330 -> 990 Hz
    /// (gaussian envelope, peak 0.12 s) that blooms into an E-major bell chord (E5 B5 E6 G#6), every note a pair detuned
    /// +-2 Hz so the chord shimmers (slow beating) as it rings out.
    ///
    /// <b>sticker_peel.wav</b> (StickersExpired, 0.42 s): a sticker peeling off - band-passed (1.5-6 kHz) noise crackle:
    /// 1-3 ms grains whose density rises 150 -> 700 per second over a quiet "ffff" bed, a smooth 0.3 s rise and a quick
    /// release, then a tiny soft 1.2 kHz "tk" as it comes free.
    /// </summary>
    public static partial class ProceduralSfxSynth
    {
        public const string CharmBellPath = Folder + "/charm_bell.wav";
        public const string ShieldChimePath = Folder + "/charm_shield.wav";
        public const string StickerPeelPath = Folder + "/sticker_peel.wav";

        static void BuildShop()
        {
            Write(CharmBellPath, CharmBell());
            Write(ShieldChimePath, ShieldChime());
            Write(StickerPeelPath, StickerPeel());
        }

        static readonly float[] SoftBellMul = { 1f, 2f, 2.76f, 5.4f };
        static readonly float[] SoftBellLvl = { 1f, 0.25f, 0.18f, 0.05f };

        /// <summary>A soft bell: <see cref="SoftBellMul"/> partials, upper ones decaying faster, raised-cosine attack.</summary>
        static void AddSoftBell(float[] o, float start, float freq, float amp, float tau, float attackSeconds)
        {
            int s0 = Mathf.RoundToInt(start * Rate);
            int attack = Mathf.Max(1, Mathf.RoundToInt(attackSeconds * Rate));
            for (int i = s0; i < o.Length; i++)
            {
                double t = (i - s0) / (double)Rate;
                double atk = i - s0 < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * (i - s0) / attack) : 1.0;
                double v = 0;
                for (int p = 0; p < SoftBellMul.Length; p++)
                {
                    if (freq * SoftBellMul[p] > Rate * 0.45f) continue;
                    v += SoftBellLvl[p] * Math.Exp(-t * Math.Sqrt(SoftBellMul[p]) / tau) * Math.Sin(2 * Math.PI * freq * SoftBellMul[p] * t);
                }
                o[i] += (float)(amp * atk * v);
            }
        }

        /// <summary>CharmBought: soft two-note bell (G5, D6).</summary>
        public static float[] CharmBell()
        {
            var o = new float[Mathf.RoundToInt(1.6f * Rate)];
            AddSoftBell(o, 0.005f, 783.99f, 1f, 0.55f, 0.012f);
            AddSoftBell(o, 0.115f, 1174.66f, 0.55f, 0.45f, 0.012f);
            FadeOut(o, 0.1f);
            return Normalize(o, PeakDb);
        }

        /// <summary>CharmTriggered: tonal swell blooming into a shimmering E-major chord.</summary>
        public static float[] ShieldChime()
        {
            var o = new float[Mathf.RoundToInt(1.4f * Rate)];
            double ph = 0;
            for (int i = 0; i < o.Length; i++)
            {
                double t = i / (double)Rate;
                double fr = 330.0 + 660.0 * SmoothStep(t / 0.22);
                ph += 2 * Math.PI * fr / Rate;
                double g = Math.Exp(-Math.Pow((t - 0.12) / 0.08, 2));
                o[i] += (float)(g * (0.30 * Math.Sin(ph) + 0.10 * Math.Sin(2 * ph)));
            }
            float[] chord = { 659.25f, 987.77f, 1318.51f, 1661.22f };
            for (int j = 0; j < chord.Length; j++)
            {
                float amp = 0.32f - j * 0.04f, at = 0.10f + j * 0.025f;
                AddSoftBell(o, at, chord[j] - 2f, amp, 0.5f, 0.02f);
                AddSoftBell(o, at, chord[j] + 2f, amp, 0.5f, 0.02f);
            }
            FadeIn(o, 0.01f);
            FadeOut(o, 0.12f);
            return Normalize(o, PeakDb);
        }

        /// <summary>StickersExpired: a quiet sticker peel (crackle + soft release tick).</summary>
        public static float[] StickerPeel()
        {
            const float len = 0.42f, riseEnd = 0.30f, releaseEnd = 0.36f;
            var o = new float[Mathf.RoundToInt(len * Rate)];
            var rng = new Noise(0x5717C4E5u);
            double lp1 = 0, lp2 = 0, hp = 0, aLp = Lp(6000f), aHp = Lp(1500f);
            int grainLeft = 0;
            float grainAmp = 0f;
            for (int i = 0; i < o.Length; i++)
            {
                float t = i / (float)Rate;
                double env = t < riseEnd ? SmoothStep(t / riseEnd)
                    : t < releaseEnd ? 0.5 + 0.5 * Math.Cos(Math.PI * (t - riseEnd) / (releaseEnd - riseEnd)) : 0.0;
                float density = Mathf.Lerp(150f, 700f, Mathf.Clamp01(t / riseEnd));
                if (grainLeft <= 0 && t < releaseEnd && rng.Range(0f, 1f) < density / Rate)
                {
                    grainLeft = Mathf.RoundToInt(rng.Range(0.001f, 0.003f) * Rate);
                    grainAmp = rng.Range(0.3f, 1f);
                }
                double w = rng.Next();
                double v = 0.15 * w;
                if (grainLeft > 0) { v += grainAmp * w; grainLeft--; }
                lp1 += aLp * (v - lp1);
                lp2 += aLp * (lp1 - lp2);
                hp += aHp * (lp2 - hp);
                o[i] = (float)((lp2 - hp) * env);
            }
            // the release "tk": 12 ms damped 1.2 kHz sine
            int s0 = Mathf.RoundToInt(0.355f * Rate), tk = Mathf.RoundToInt(0.012f * Rate), atk = Mathf.RoundToInt(0.001f * Rate);
            for (int k = 0; k < tk && s0 + k < o.Length; k++)
            {
                double t = k / (double)Rate;
                double a = k < atk ? 0.5 - 0.5 * Math.Cos(Math.PI * k / atk) : 1.0;
                o[s0 + k] += (float)(0.12 * a * Math.Exp(-t / 0.003) * Math.Sin(2 * Math.PI * 1200.0 * t));
            }
            FadeIn(o, 0.005f);
            FadeOut(o, 0.03f);
            return Normalize(o, PeakDb);
        }
    }
}
