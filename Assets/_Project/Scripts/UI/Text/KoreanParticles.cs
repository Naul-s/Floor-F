using System;

namespace FloorF.UI
{
    /// <summary>
    /// Korean particles chosen by how the word is READ (pure C#, EditMode-tested). Numbers are read Sino-Korean:
    /// 0 영, 1 일, 2 이, 3 삼, 4 사, 5 오, 6 육, 7 칠, 8 팔, 9 구 by the last digit; trailing zeros read as the unit
    /// (10 십, 100 백, 1000 천, 10000 만), all of which end in a final consonant. Hangul uses its own final consonant,
    /// Latin letters their Korean names (F 에프, L 엘, M 엠, N 엔, R 알 ...).
    /// <para>In UIStrings Korean templates write the particle as a format specifier: "{0:은} 행운 숫자" -> "3은", "2는".
    /// Supported: 은/는, 이/가, 을/를, 과/와, 으로/로 (either spelling of the pair works). Use
    /// <see cref="Format(string, object[])"/> (or UIContext.F) - English templates have no specifiers and pass through.</para>
    /// </summary>
    static class KoreanParticles
    {
        enum Final { None, Rieul, Other }

        sealed class Provider : IFormatProvider, ICustomFormatter
        {
            public object GetFormat(Type formatType) => formatType == typeof(ICustomFormatter) ? this : null;

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                string s = arg is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : arg?.ToString() ?? "";
                if (string.IsNullOrEmpty(format)) return s;
                return TryParticle(format, out var withFinal, out var without, out var ro) ? s + Pick(s, withFinal, without, ro) : s;
            }
        }

        static readonly Provider provider = new Provider();

        /// <summary>string.Format with particle specifiers ("{0:이}").</summary>
        public static string Format(string template, params object[] args) => string.Format(provider, template ?? "", args);

        /// <summary>The particle for <paramref name="word"/>: Particle("3", "은") -> "은", Particle("2", "은") -> "는".</summary>
        public static string Particle(string word, string particle) =>
            TryParticle(particle, out var a, out var b, out var ro) ? Pick(word, a, b, ro) : particle;

        /// <summary>Word + particle: Attach("3", "는") -> "3은".</summary>
        public static string Attach(string word, string particle) => (word ?? "") + Particle(word, particle);

        /// <summary>True when the reading of <paramref name="word"/> ends in a final consonant (받침).</summary>
        public static bool HasFinalConsonant(string word) => FinalOf(word) != Final.None;

        static bool TryParticle(string p, out string withFinal, out string without, out bool ro)
        {
            ro = false;
            switch (p)
            {
                case "은": case "는": withFinal = "은"; without = "는"; return true;
                case "이": case "가": withFinal = "이"; without = "가"; return true;
                case "을": case "를": withFinal = "을"; without = "를"; return true;
                case "과": case "와": withFinal = "과"; without = "와"; return true;
                case "으로": case "로": withFinal = "으로"; without = "로"; ro = true; return true;
                default: withFinal = without = null; return false;
            }
        }

        static string Pick(string word, string withFinal, string without, bool ro)
        {
            var f = FinalOf(word);
            if (ro) return f == Final.None || f == Final.Rieul ? without : withFinal; // ㄹ받침 + 로 (칠로, 일로)
            return f == Final.None ? without : withFinal;
        }

        static Final FinalOf(string word)
        {
            if (string.IsNullOrEmpty(word)) return Final.None;
            // the last readable character decides: skip closing punctuation / spaces / rich-text-free symbols
            int i = word.Length - 1;
            while (i >= 0 && !char.IsLetterOrDigit(word[i])) i--;
            if (i < 0) return Final.None;
            char c = word[i];
            if (c >= '0' && c <= '9') return NumberFinal(word, i);
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                int jong = (c - 0xAC00) % 28;
                return jong == 0 ? Final.None : jong == 8 ? Final.Rieul : Final.Other;
            }
            return LetterFinal(char.ToUpperInvariant(c));
        }

        static Final NumberFinal(string word, int last)
        {
            int zeros = 0, j = last;
            while (j >= 0 && word[j] == '0') { zeros++; j--; }
            bool allZero = j < 0 || !char.IsDigit(word[j]);
            if (allZero) return Final.Other; // 0 영 (00 영영)
            if (zeros > 0) return Final.Other; // 십 백 천 만 (and 억 at 8): every unit ends in a consonant
            switch (word[last])
            {
                case '1': case '7': case '8': return Final.Rieul; // 일 칠 팔
                case '3': case '6': return Final.Other;            // 삼 육
                default: return Final.None;                        // 이 사 오 구
            }
        }

        /// <summary>Korean names of Latin letters: L 엘, R 알 (ㄹ); M 엠, N 엔 (consonant); the rest end in a vowel.</summary>
        static Final LetterFinal(char c)
        {
            switch (c)
            {
                case 'L': case 'R': return Final.Rieul;
                case 'M': case 'N': return Final.Other;
                default: return Final.None;
            }
        }
    }
}
