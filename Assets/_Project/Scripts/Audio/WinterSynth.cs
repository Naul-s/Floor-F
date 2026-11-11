using System;

namespace FloorF.Audio
{
    /// <summary>
    /// Winter-season sounds, synthesized at runtime (our own synthesis - no third-party source, no AI): pure,
    /// deterministic functions returning mono samples. <see cref="WinterBank"/> turns them into AudioClips once (lazily,
    /// cached) and sets every level from the measured file level, so the balance does not depend on these amplitudes.
    /// Oscillators are rotating phasors with multiplicative decay (no per-sample sin/exp: cheap on WebGL). "Loop" clips
    /// wrap every note's tail around the end, so they repeat seamlessly (what an endless repeat of the loop contains).
    ///
    /// Clips: soft winter wind loop (snow days), sleigh-bell 8ths for the elevator song (Christmas Eve), Santa's sleigh
    /// shake, the carolers' handbell run, lottery drum roll + ding, jackpot coin shower, the winter-season stinger,
    /// "A Christmas Miracle" warm bell loop and the "Silent Night" music box (Gruber, 1818 - public domain).
    /// </summary>
    static class WinterSynth
    {
        public const int Rate = 44100;
        /// <summary>Dark/low clips (wind, music box): half rate - half the work and memory.</summary>
        public const int LowRate = 22050;
        /// <summary>The warm bell loop: bells reach ~8 kHz.</summary>
        public const int MidRate = 32000;
        public const float PeakDb = -3f;

        /// <summary>
        /// The elevator song's grid (BlondPanda 659889: 1192563 samples at 44.1 kHz = 27.042 s = 8 bars of 4 x 845 ms;
        /// onsets on a 211 ms pulse from the loop start). The Christmas bell layer is one such bar, looped in sync.
        /// </summary>
        public const double ElevatorLoopSeconds = 1192563.0 / 44100.0;
        public const int ElevatorBars = 8;
        public const double BarSeconds = ElevatorLoopSeconds / ElevatorBars;
        /// <summary>Bell pulses per bar (the song's 211 ms pulse).</summary>
        public const int BarPulses = 16;
        public const double GridOffsetSeconds = 0.015;

        public const float WindSeconds = 10f;
        public const float WindRmsDb = -20f;

        // ------------------------------------------------------------------ building blocks

        /// <summary>Small deterministic white noise (xorshift32), -1..1.</summary>
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

            public float Range(float lo, float hi) => lo + (Next() * 0.5f + 0.5f) * (hi - lo);
        }

        /// <summary>
        /// One exponentially decaying sine partial at <paramref name="start"/> s (raised-cosine attack), summed into
        /// <paramref name="o"/> until it is 70 dB down. <paramref name="wrap"/>: the tail wraps around the end (loops).
        /// </summary>
        internal static void AddPartial(float[] o, int rate, double start, double freq, double amp, double tau,
            double attack = 0.004, bool wrap = false)
        {
            if (amp == 0 || freq <= 0 || freq >= rate * 0.45 || tau <= 0) return;
            int s0 = (int)Math.Round(start * rate);
            if (wrap) s0 = ((s0 % o.Length) + o.Length) % o.Length;
            else if (s0 < 0 || s0 >= o.Length) return;
            int atk = Math.Max(1, (int)(attack * rate));
            long len = (long)((tau * 8.1 + attack) * rate);
            if (!wrap) len = Math.Min(len, o.Length - s0);
            double w = 2 * Math.PI * freq / rate, cw = Math.Cos(w), sw = Math.Sin(w);
            double re = 1, im = 0, env = amp, d = Math.Exp(-1.0 / (tau * rate));
            int idx = s0;
            for (long i = 0; i < len; i++)
            {
                double a = i < atk ? 0.5 - 0.5 * Math.Cos(Math.PI * i / atk) : 1.0;
                o[idx] += (float)(env * a * im);
                if (++idx == o.Length) idx = 0;
                double nre = re * cw - im * sw;
                im = re * sw + im * cw;
                re = nre;
                if (i >= atk) env *= d;
                if ((i & 1023) == 1023) { double m = 1.0 / Math.Sqrt(re * re + im * im); re *= m; im *= m; }
            }
        }

        /// <summary>Small bright bell (as the wish chime): 1, 2, 3, 4.2 x, upper partials decaying faster.</summary>
        internal static void AddBell(float[] o, int rate, double start, double f, double amp, double tau, bool wrap = false)
        {
            AddPartial(o, rate, start, f, amp, tau, 0.004, wrap);
            AddPartial(o, rate, start, f * 2, amp * 0.35, tau / 2, 0.004, wrap);
            AddPartial(o, rate, start, f * 3, amp * 0.12, tau / 3, 0.004, wrap);
            AddPartial(o, rate, start, f * 4.2, amp * 0.08, tau / 4.2, 0.004, wrap);
        }

        static readonly double[] ChurchRatio = { 0.5, 1.0, 1.19, 1.5, 2.0, 2.52, 3.0 };
        static readonly double[] ChurchLevel = { 0.40, 1.0, 0.30, 0.22, 0.28, 0.10, 0.06 };
        static readonly double[] ChurchTau = { 1.5, 1.0, 0.7, 0.6, 0.5, 0.35, 0.25 };

        /// <summary>Warm tower bell: hum (0.5x), prime, a soft minor tierce, quint, nominal; the hum rings longest.</summary>
        internal static void AddChurchBell(float[] o, int rate, double start, double f, double amp, double tau, bool wrap = false)
        {
            for (int p = 0; p < ChurchRatio.Length; p++)
                AddPartial(o, rate, start, f * ChurchRatio[p], amp * ChurchLevel[p], tau * ChurchTau[p], 0.006, wrap);
        }

        /// <summary>Handbell: fundamental + the twelfth (3x) it is tuned to + a faint 5x.</summary>
        internal static void AddHandbell(float[] o, int rate, double start, double f, double amp, double tau, bool wrap = false)
        {
            AddPartial(o, rate, start, f, amp, tau, 0.003, wrap);
            AddPartial(o, rate, start, f * 3, amp * 0.42, tau * 0.45, 0.003, wrap);
            AddPartial(o, rate, start, f * 5.04, amp * 0.12, tau * 0.22, 0.003, wrap);
        }

        /// <summary>Music-box tine: fundamental, a little octave and the short metallic "ping" partial (5.4x).</summary>
        internal static void AddTine(float[] o, int rate, double start, double f, double amp, bool wrap = false)
        {
            double tau = 1.0 * Math.Pow(523.25 / f, 0.3);
            AddPartial(o, rate, start, f, amp, tau, 0.002, wrap);
            AddPartial(o, rate, start, f * 2, amp * 0.16, tau * 0.35, 0.002, wrap);
            AddPartial(o, rate, start, f * 3, amp * 0.06, tau * 0.15, 0.002, wrap);
            AddPartial(o, rate, start, f * 5.4, amp * 0.22, 0.035, 0.001, wrap);
        }

        /// <summary>
        /// Soft sustained pad note: two slightly detuned sines + a faint octave, raised-cosine attack and release.
        /// </summary>
        internal static void AddPad(float[] o, int rate, double start, double dur, double f, double amp,
            double attack, double release, bool wrap = false)
        {
            int s0 = (int)Math.Round(start * rate);
            if (wrap) s0 = ((s0 % o.Length) + o.Length) % o.Length;
            else if (s0 < 0 || s0 >= o.Length) return;
            int n = (int)((dur + release) * rate), a = Math.Max(1, (int)(attack * rate)), hold = (int)(dur * rate);
            int r = Math.Max(1, n - hold);
            if (!wrap) n = Math.Min(n, o.Length - s0);
            double[] mul = { 0.9985, 1.0015, 2.0 };
            double[] lvl = { 0.5, 0.5, 0.12 };
            var re = new double[3];
            var im = new double[3];
            var cw = new double[3];
            var sw = new double[3];
            for (int p = 0; p < 3; p++)
            {
                double w = 2 * Math.PI * f * mul[p] / rate;
                cw[p] = Math.Cos(w); sw[p] = Math.Sin(w); re[p] = 1; im[p] = 0;
            }
            int idx = s0;
            for (int i = 0; i < n; i++)
            {
                double e = i < a ? 0.5 - 0.5 * Math.Cos(Math.PI * i / a) : 1.0;
                if (i >= hold) e *= 0.5 + 0.5 * Math.Cos(Math.PI * Math.Min(1.0, (i - hold) / (double)r));
                double v = 0;
                for (int p = 0; p < 3; p++)
                {
                    if (f * mul[p] >= rate * 0.45) continue;
                    v += lvl[p] * im[p];
                    double nre = re[p] * cw[p] - im[p] * sw[p];
                    im[p] = re[p] * sw[p] + im[p] * cw[p];
                    re[p] = nre;
                }
                o[idx] += (float)(amp * e * v);
                if (++idx == o.Length) idx = 0;
                if ((i & 1023) == 1023)
                    for (int p = 0; p < 3; p++) { double m = 1.0 / Math.Sqrt(re[p] * re[p] + im[p] * im[p]); re[p] *= m; im[p] *= m; }
            }
        }

        /// <summary>
        /// One sleigh-bell hit: a cluster of tiny jingle bells (inharmonic 2.6-8 kHz partials, 30-110 ms) striking within
        /// 35 ms, over a short bright noise rattle.
        /// </summary>
        internal static void AddJingle(float[] o, int rate, double start, double amp, Noise rng, bool wrap = false)
        {
            int bells = 5;
            for (int b = 0; b < bells; b++)
            {
                double t = start + rng.Range(0f, 0.035f);
                double f = rng.Range(2600f, 4300f);
                double a = amp * rng.Range(0.5f, 1f) / bells * 2.2;
                double tau = rng.Range(0.05f, 0.11f);
                AddPartial(o, rate, t, f, a, tau, 0.0015, wrap);
                AddPartial(o, rate, t, f * rng.Range(1.48f, 1.62f), a * 0.55, tau * 0.7, 0.0015, wrap);
                AddPartial(o, rate, t, f * rng.Range(2.3f, 2.6f), a * 0.3, tau * 0.5, 0.0015, wrap);
            }
            // rattle: high-passed noise (first difference), 6 ms decay
            int s0 = (int)Math.Round(start * rate), n = (int)(0.05 * rate);
            double env = amp * 0.35, d = Math.Exp(-1.0 / (0.006 * rate)), prev = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (wrap) idx = ((idx % o.Length) + o.Length) % o.Length;
                else if (idx < 0 || idx >= o.Length) break;
                double w = rng.Next();
                o[idx] += (float)(env * (w - prev) * 0.5);
                prev = w;
                env *= d;
            }
        }

        internal static double SmoothStep(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        /// <summary>Raised-cosine fade over the last <paramref name="seconds"/>; the last sample is exactly 0.</summary>
        internal static void FadeOut(float[] o, int rate, float seconds)
        {
            int f = Math.Max(2, Math.Min(o.Length, (int)Math.Round(seconds * rate)));
            for (int i = 0; i < f; i++)
                o[o.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / (f - 1)));
        }

        internal static float[] Normalize(float[] o, float peakDb)
        {
            float peak = 0f;
            foreach (float v in o) peak = Math.Max(peak, Math.Abs(v));
            if (peak <= 0f) return o;
            float g = (float)Math.Pow(10, peakDb / 20.0) / peak;
            for (int i = 0; i < o.Length; i++) o[i] *= g;
            return o;
        }

        static float[] Buffer(int rate, double seconds) => new float[(int)Math.Round(seconds * rate)];

        /// <summary>Level of a clip (dBFS): sample peak, loudest 100 ms window (rms, 25 ms hop) and overall rms.</summary>
        internal readonly struct Levels
        {
            public readonly float PeakDb, Max100msDb, RmsDb;
            public Levels(float peak, float max100, float rms) { PeakDb = peak; Max100msDb = max100; RmsDb = rms; }
            public override string ToString() => $"peak {PeakDb:0.0} / max100ms {Max100msDb:0.0} / rms {RmsDb:0.0} dBFS";
        }

        internal static float Db(double linear) => (float)(20.0 * Math.Log10(Math.Max(linear, 1e-9)));

        internal static Levels Measure(float[] d, int rate)
        {
            double peak = 0, all = 0;
            foreach (float v in d) { peak = Math.Max(peak, Math.Abs(v)); all += v * (double)v; }
            int win = Math.Max(1, rate / 10), hop = Math.Max(1, rate / 40);
            double best = 0;
            for (int s = 0; s + win <= Math.Max(win, d.Length); s += hop)
            {
                double sum = 0;
                int end = Math.Min(d.Length, s + win);
                for (int i = s; i < end; i++) sum += d[i] * (double)d[i];
                best = Math.Max(best, sum / win);
                if (end == d.Length) break;
            }
            return new Levels(Db(peak), Db(Math.Sqrt(best)), Db(Math.Sqrt(all / Math.Max(1, d.Length))));
        }

        // ------------------------------------------------------------------ wind (snow days)

        /// <summary>
        /// Soft winter wind (seamless <see cref="WindSeconds"/> s loop at <see cref="LowRate"/>): pink-ish noise through a
        /// gentle resonant band-pass (state-variable, Q ~1.4) whose centre drifts 300-700 Hz with the gusts, plus a dark
        /// low-passed body. Gusts = slow swells periodic in the loop length; the tail of a longer render is equal-power
        /// crossfaded into the head (1 s). Normalized to <see cref="WindRmsDb"/> rms (peak kept under -3 dBFS).
        /// </summary>
        public static float[] Wind()
        {
            int rate = LowRate;
            int n = (int)Math.Round(WindSeconds * rate), f = rate;
            var rng = new Noise(0x51A0F1D7u);
            var x = new float[n + f];
            double b0 = 0, b1 = 0, b2 = 0, low = 0, band = 0, body = 0, hp = 0;
            double aBody = 1 - Math.Exp(-2 * Math.PI * 220.0 / rate), aHp = 1 - Math.Exp(-2 * Math.PI * 70.0 / rate);
            const double q = 0.7; // damping (1/Q)
            for (int i = -rate; i < x.Length; i++) // 1 s warm-up: the filters start settled
            {
                double t = (i < 0 ? i + n : i) / (double)rate;
                double ph = 2 * Math.PI * t / WindSeconds; // gusts: periodic in the loop length
                double gust = 0.5 + 0.28 * Math.Sin(ph + 0.6) + 0.14 * Math.Sin(3 * ph + 2.1) + 0.08 * Math.Sin(7 * ph + 0.3);
                double w = rng.Next();
                b0 = 0.99765 * b0 + w * 0.0990460;
                b1 = 0.96300 * b1 + w * 0.2965164;
                b2 = 0.57000 * b2 + w * 1.0526913;
                double pink = (b0 + b1 + b2 + w * 0.1848) * 0.11;
                double fc = 300 + 400 * gust;
                double k = 2 * Math.Sin(Math.PI * fc / rate);
                low += k * band;
                double high = pink - low - q * band;
                band += k * high;
                body += aBody * (pink - body);
                double v = (0.75 * band + 0.6 * body) * (0.35 + 0.65 * gust);
                hp += aHp * (v - hp);
                if (i >= 0) x[i] = (float)(v - hp);
            }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < f)
                {
                    double th = 0.5 * Math.PI * i / f;
                    o[i] = (float)(x[i] * Math.Sin(th) + x[n + i] * Math.Cos(th));
                }
                else o[i] = x[i];
            }
            double s = 0;
            float peak = 0f;
            foreach (float v in o) { s += v * (double)v; peak = Math.Max(peak, Math.Abs(v)); }
            double g = Math.Pow(10, WindRmsDb / 20.0) / Math.Sqrt(s / n);
            g = Math.Min(g, Math.Pow(10, PeakDb / 20.0) / Math.Max(1e-9, peak));
            for (int i = 0; i < n; i++) o[i] *= (float)g;
            return o;
        }

        // ------------------------------------------------------------------ Christmas Eve

        /// <summary>
        /// One bar of the elevator song (<see cref="BarSeconds"/>, looped in sync with it): sleigh bells on its 211 ms
        /// pulse - lighter off-beats, accents on every other pulse and a fuller double shake on the backbeats.
        /// </summary>
        public static float[] ChristmasBellsBar()
        {
            var o = Buffer(Rate, BarSeconds);
            var rng = new Noise(0xC4815E11u);
            double p = BarSeconds / BarPulses;
            for (int k = 0; k < BarPulses; k++)
            {
                double t = GridOffsetSeconds + k * p;
                bool backbeat = k % 8 == 2 || k % 8 == 6;
                double amp = backbeat ? 1.0 : k % 2 == 0 ? 0.7 : 0.4;
                AddJingle(o, Rate, t, amp, rng, wrap: true);
                if (backbeat) AddJingle(o, Rate, t + 0.045, 0.45, rng, wrap: true);
            }
            return Normalize(o, PeakDb);
        }

        /// <summary>Santa (spawn / boarding): a quick sleigh-bell shake, ~1.2 s.</summary>
        public static float[] SleighShake()
        {
            var o = Buffer(Rate, 1.2);
            var rng = new Noise(0x5A17A001u);
            double[] at = { 0.0, 0.085, 0.16, 0.225, 0.285, 0.34, 0.39, 0.44 };
            for (int i = 0; i < at.Length; i++) AddJingle(o, Rate, 0.005 + at[i], i % 2 == 0 ? 1.0 : 0.75 - i * 0.03, rng);
            AddJingle(o, Rate, 0.56, 0.55, rng);
            FadeOut(o, Rate, 0.08f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Carolers boarding: a handbell run C6 B5 A5 G5 ("Joy to the World") resolving on a soft C-major chord.</summary>
        public static float[] CarolChime()
        {
            var o = Buffer(Rate, 1.7);
            double[] run = { 1046.50, 987.77, 880.00, 783.99 };
            for (int i = 0; i < run.Length; i++) AddHandbell(o, Rate, 0.005 + i * 0.13, run[i], i == 0 ? 0.8 : 0.65, 0.55);
            foreach (double f in new[] { 523.25, 659.26, 783.99 }) AddHandbell(o, Rate, 0.56, f, 0.38, 0.8);
            FadeOut(o, Rate, 0.12f);
            return Normalize(o, PeakDb);
        }

        // ------------------------------------------------------------------ lottery

        /// <summary>
        /// LotteryDrawn: a 0.8 s snare roll (32 strokes/s, crescendo) ending on an accent, then the reveal "ding"
        /// (E6 bell + B6 and a little E7 sparkle) ringing out - ~1.9 s.
        /// </summary>
        public static float[] LotteryDraw()
        {
            var o = Buffer(Rate, 1.9);
            var rng = new Noise(0x10770DEEu);
            const double roll = 0.8, rate32 = 1.0 / 32;
            int strokes = (int)(roll / rate32);
            for (int s = 0; s <= strokes; s++)
            {
                double t = 0.005 + s * rate32 + rng.Range(-0.002f, 0.002f);
                bool last = s == strokes;
                double amp = last ? 1.0 : (0.18 + 0.6 * SmoothStep(s / (double)strokes)) * (s % 2 == 0 ? 1.0 : 0.85);
                AddSnare(o, Rate, t, amp, rng, last ? 0.05 : 0.02);
            }
            double ding = roll + 0.04;
            AddBell(o, Rate, ding, 1318.51, 1.1, 0.55);
            AddBell(o, Rate, ding + 0.01, 1975.53, 0.45, 0.4);
            AddPartial(o, Rate, ding + 0.03, 2637.02, 0.25, 0.25);
            FadeOut(o, Rate, 0.1f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Snare stroke: band-limited noise (first difference, one-pole low-pass ~6 kHz) + a 190 Hz body.</summary>
        static void AddSnare(float[] o, int rate, double start, double amp, Noise rng, double tau)
        {
            int s0 = (int)Math.Round(start * rate), n = (int)(tau * 7 * rate);
            double env = amp, d = Math.Exp(-1.0 / (tau * rate)), prev = 0, lp = 0, aLp = 1 - Math.Exp(-2 * Math.PI * 6000.0 / rate);
            for (int i = 0; i < n && s0 + i < o.Length; i++)
            {
                if (s0 + i < 0) continue;
                double w = rng.Next();
                lp += aLp * ((w - prev) - lp);
                prev = w;
                o[s0 + i] += (float)(env * 0.5 * lp);
                env *= d;
            }
            AddPartial(o, rate, start, 190, amp * 0.12, tau * 1.2, 0.001);
        }

        /// <summary>
        /// LotteryWon: a bright "ka-ching" arpeggio (C6 E6 G6 C7 E7) over a ~1.2 s shower of coins (short inharmonic
        /// clinks, dense then thinning, some bouncing once) - flashier than a tip, ~1.6 s.
        /// </summary>
        public static float[] Jackpot()
        {
            var o = Buffer(Rate, 1.6);
            var rng = new Noise(0x7AC4B077u);
            double[] arp = { 1046.50, 1318.51, 1567.98, 2093.00, 2637.02 };
            for (int i = 0; i < arp.Length; i++) AddBell(o, Rate, 0.005 + i * 0.045, arp[i], 0.55, 0.32);
            const int coins = 40;
            for (int i = 0; i < coins; i++)
            {
                double u = i / (double)coins;
                double t = 0.06 + 1.1 * Math.Pow(u, 1.5) + rng.Range(-0.015f, 0.015f);
                double amp = 0.5 * (1 - 0.55 * u) * rng.Range(0.6f, 1f);
                AddCoin(o, t, amp, rng);
                if (rng.Next() > 0.4f) AddCoin(o, t + rng.Range(0.04f, 0.075f), amp * 0.45, rng);
            }
            FadeOut(o, Rate, 0.12f);
            return Normalize(o, PeakDb);
        }

        static void AddCoin(float[] o, double t, double amp, Noise rng)
        {
            double f = rng.Range(3000f, 5600f), tau = rng.Range(0.02f, 0.05f);
            AddPartial(o, Rate, t, f, amp, tau, 0.0008);
            AddPartial(o, Rate, t, f * rng.Range(1.38f, 1.7f), amp * 0.6, tau * 0.7, 0.0008);
            AddPartial(o, Rate, t, f * 0.53, amp * 0.25, tau * 1.4, 0.0008);
        }

        // ------------------------------------------------------------------ season / endings

        /// <summary>
        /// SeasonExtended: a sleigh-bell shake, a warm tower bell (C5) and a soft C-major pad swelling under three
        /// celesta notes G5 C6 E6 - ~2.8 s.
        /// </summary>
        public static float[] WinterStart()
        {
            var o = Buffer(Rate, 2.8);
            var rng = new Noise(0x0E1E5EA5u);
            double[] shake = { 0.0, 0.08, 0.15, 0.21, 0.27, 0.33 };
            for (int i = 0; i < shake.Length; i++) AddJingle(o, Rate, 0.005 + shake[i], 0.55 - i * 0.05, rng);
            foreach (double f in new[] { 261.63, 329.63, 392.00, 523.25 }) AddPad(o, Rate, 0.05, 1.3, f, 0.16, 0.35, 1.2);
            AddChurchBell(o, Rate, 0.01, 523.25, 0.5, 1.1);
            double[] cel = { 783.99, 1046.50, 1318.51 };
            for (int i = 0; i < cel.Length; i++) AddBell(o, Rate, 0.35 + i * 0.2, cel[i], 0.42, 0.7);
            FadeOut(o, Rate, 0.25f);
            return Normalize(o, PeakDb);
        }

        /// <summary>Seconds of the warm bell loop: four chords of 4 beats at 100 bpm.</summary>
        public const double MiracleSeconds = 9.6;

        /// <summary>
        /// "A Christmas Miracle" ending loop (<see cref="MiracleSeconds"/> s at <see cref="MidRate"/>, seamless): C - Am -
        /// F - G, a warm pad, a tower bell on each chord's downbeat, a celesta arpeggio in eighths and a light jingle on
        /// the backbeats.
        /// </summary>
        public static float[] WinterMiracleLoop()
        {
            int rate = MidRate;
            var o = Buffer(rate, MiracleSeconds);
            var rng = new Noise(0x3F1AC1E5u);
            const double chord = 2.4, beat = 0.6, eighth = 0.3;
            double[][] tones =
            {
                new[] { 261.63, 329.63, 392.00 }, // C
                new[] { 220.00, 261.63, 329.63 }, // Am
                new[] { 174.61, 220.00, 261.63 }, // F
                new[] { 196.00, 246.94, 293.66 }, // G
            };
            double[] bell = { 261.63, 220.00, 349.23, 196.00 };
            int[] arp = { 0, 1, 2, 3, 2, 1, 2, 1 };
            for (int c = 0; c < 4; c++)
            {
                double t0 = c * chord;
                var tn = tones[c];
                AddPad(o, rate, t0, chord - 0.3, tn[0] * 0.5, 0.14, 0.5, 0.9, true);
                foreach (double f in tn) AddPad(o, rate, t0, chord - 0.3, f, 0.1, 0.5, 0.9, true);
                AddChurchBell(o, rate, t0 + 0.005, bell[c], 0.42, 1.1, true);
                for (int e = 0; e < 8; e++)
                {
                    int k = arp[e];
                    double f = k == 3 ? tn[0] * 4 : tn[k] * 2;
                    AddBell(o, rate, t0 + e * eighth + 0.005, f, e == 0 ? 0.26 : 0.18, 0.45, true);
                }
                AddJingle(o, rate, t0 + beat, 0.12, rng, true);
                AddJingle(o, rate, t0 + 3 * beat, 0.12, rng, true);
            }
            return Normalize(o, PeakDb);
        }

        /// <summary>Music box tempo: one 6/8 eighth (dotted quarter = 54 bpm).</summary>
        public const double BoxEighth = 60.0 / 54.0 / 3.0;
        public const int BoxBars = 16;
        public static double SilentNightSeconds => BoxBars * 6 * BoxEighth;

        // Silent Night (Franz Xaver Gruber, 1818 - public domain), C major, 6/8: bars 1-8 and 17-24 of the song
        // ("Silent night ... all is bright", "Sleep in heavenly peace ..."). Notes as (MIDI, eighths); 0 = rest.
        static readonly int[][] Melody =
        {
            new[] { 79, 3, 81, 1, 79, 2 }, new[] { 76, 6 }, new[] { 79, 3, 81, 1, 79, 2 }, new[] { 76, 6 },
            new[] { 86, 4, 86, 2 }, new[] { 83, 6 }, new[] { 84, 4, 84, 2 }, new[] { 79, 6 },
            new[] { 86, 4, 86, 2 }, new[] { 89, 3, 86, 1, 83, 2 }, new[] { 84, 6 }, new[] { 88, 6 },
            new[] { 84, 3, 79, 1, 76, 2 }, new[] { 79, 3, 77, 1, 74, 2 }, new[] { 72, 6 }, new[] { 0, 6 },
        };
        // per bar: bass on the downbeat, a dyad on the second dotted beat
        static readonly int[] Bass = { 48, 48, 48, 48, 43, 43, 48, 48, 43, 43, 48, 48, 48, 43, 48, 48 };
        static readonly int[][] Dyad =
        {
            new[] { 64, 67 }, new[] { 64, 67 }, new[] { 64, 67 }, new[] { 64, 67 }, new[] { 59, 62 }, new[] { 59, 62 },
            new[] { 64, 67 }, new[] { 64, 67 }, new[] { 59, 65 }, new[] { 59, 65 }, new[] { 64, 67 }, new[] { 64, 67 },
            new[] { 64, 67 }, new[] { 59, 65 }, new[] { 64, 67 }, new[] { 0, 0 },
        };

        internal static double Midi(int note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

        /// <summary>
        /// "Silent Night" ending loop: the melody on a music box (<see cref="LowRate"/>, seamless, ~35.6 s) in octaves 5-6,
        /// over a quiet bass and dyad (octave 4), slightly humanized (seeded timing / velocity) like a real comb.
        /// </summary>
        public static float[] SilentNightLoop()
        {
            int rate = LowRate;
            var o = Buffer(rate, SilentNightSeconds);
            var rng = new Noise(0x51E27719u);
            double bar = 6 * BoxEighth;
            for (int b = 0; b < BoxBars; b++)
            {
                double t0 = b * bar;
                var m = Melody[b];
                double at = 0;
                for (int i = 0; i < m.Length; i += 2)
                {
                    if (m[i] > 0)
                        AddTine(o, rate, t0 + at + 0.004 + rng.Range(-0.006f, 0.006f), Midi(m[i]), 0.8 * rng.Range(0.9f, 1.05f), true);
                    at += m[i + 1] * BoxEighth;
                }
                AddTine(o, rate, t0 + 0.004 + rng.Range(0f, 0.008f), Midi(Bass[b] + 12), 0.42, true);
                if (Dyad[b][0] > 0)
                    foreach (int n in Dyad[b])
                        AddTine(o, rate, t0 + 3 * BoxEighth + 0.004 + rng.Range(-0.005f, 0.005f), Midi(n), 0.24, true);
            }
            return Normalize(o, PeakDb);
        }
    }
}
