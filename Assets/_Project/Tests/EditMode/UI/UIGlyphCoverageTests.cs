using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>
    /// Every character the game can show is drawn by a UI font: the body font (Mulmaru) or the heading font (Galmuri11
    /// Bold), each with Galmuri11 as its only fallback - never TMP's LiberationSans (smooth, breaks the pixel look) or □.
    /// Sources: UIStrings (both languages), every string literal in Scripts/UI, every character of Resources/Data/*.json.
    /// </summary>
    public class UIGlyphCoverageTests
    {
        [Test]
        public void Faces_BodyMulmaru_HeadingGalmuriBold_OnlyGalmuri11Fallback()
        {
            Assert.IsTrue(UIFonts.AllFacesCreated, "Mulmaru + Galmuri11 Bold runtime assets, each with exactly [Galmuri11] as fallback");
            Assert.AreEqual("Mulmaru regular", UIFonts.DrawnBy(UIFonts.Face.Body, 'A').Replace("Regular", "regular"));
            Assert.AreEqual("Galmuri11 Bold", UIFonts.DrawnBy(UIFonts.Face.Heading, 'A'));
            Assert.AreEqual("Mulmaru regular", UIFonts.DrawnBy(UIFonts.Face.Body, '가').Replace("Regular", "regular"));
        }

        /// <summary>The glyphs the two primary fonts lack come from Galmuri11 (same 12px grid).</summary>
        [Test]
        public void Hanja_And_Symbols_ComeFromGalmuri11()
        {
            foreach (char c in "死四生久苦發順六大雙好事成紅包喜≫↕")
            {
                // Mulmaru has ↕ itself; Galmuri11 Bold lacks it
                foreach (var face in c == '↕' ? new[] { UIFonts.Face.Heading } : new[] { UIFonts.Face.Body, UIFonts.Face.Heading })
                {
                    string by = UIFonts.DrawnBy(face, c);
                    Assert.IsNotNull(by, face + ": " + c + " drawn by a UI font");
                    StringAssert.StartsWith("Galmuri11", by, face + ": " + c);
                }
            }
        }

        [Test]
        public void EveryUiAndDataCharacter_IsInBodyAndHeadingFonts()
        {
            var chars = new SortedDictionary<char, string>();
            foreach (var kv in UiStringValues()) Add(chars, kv.Value, kv.Key);
            foreach (var file in Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/Scripts/UI"), "*.cs", SearchOption.AllDirectories))
                foreach (var lit in StringLiterals(File.ReadAllText(file))) Add(chars, lit, Path.GetFileName(file));
            string data = Path.Combine(Application.dataPath, "_Project/Resources/Data");
            var jsons = Directory.GetFiles(data, "*.json");
            Assert.GreaterOrEqual(jsons.Length, 4, "guests, stickers, days, hotel");
            foreach (var file in jsons) Add(chars, File.ReadAllText(file), Path.GetFileName(file));
            Assert.Greater(chars.Count, 400, "Hangul, hanja, symbols, ASCII collected");

            var miss = new StringBuilder();
            foreach (var kv in chars)
            {
                string s = kv.Key.ToString();
                foreach (var face in new[] { UIFonts.Face.Body, UIFonts.Face.Heading })
                    if (UIFonts.MissingGlyphIn(face, s) != null)
                        miss.Append(face).Append(' ').Append(s).Append(" U+").Append(((int)kv.Key).ToString("X4")).Append(" (").Append(kv.Value).Append("); ");
            }
            Assert.AreEqual("", miss.ToString(), chars.Count + " distinct characters; these would come from LiberationSans or draw □");
        }

        static void Add(SortedDictionary<char, string> chars, string s, string source)
        {
            foreach (char c in s ?? "")
                if (!char.IsWhiteSpace(c) && !char.IsControl(c) && !char.IsSurrogate(c) && c != '﻿' && !chars.ContainsKey(c)) chars[c] = source;
        }

        /// <summary>Every UIStrings static string / Loc (En and Ko) / array of them.</summary>
        static IEnumerable<KeyValuePair<string, string>> UiStringValues()
        {
            var type = typeof(UIFonts).Assembly.GetType("FloorF.UI.UIStrings");
            Assert.IsNotNull(type, "UIStrings");
            int n = 0;
            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
                foreach (var s in Strings(v)) { n++; yield return new KeyValuePair<string, string>("UIStrings." + f.Name, s); }
            }
            Assert.Greater(n, 100, "UIStrings values found");
        }

        static IEnumerable<string> Strings(object v)
        {
            if (v == null) yield break;
            if (v is string s) { yield return s; yield break; }
            if (v is System.Collections.IEnumerable list)
            {
                foreach (var e in list) foreach (var x in Strings(e)) yield return x;
                yield break;
            }
            var t = v.GetType();
            if (t.Name == "Loc")
                foreach (var name in new[] { "En", "Ko" })
                    if (t.GetField(name)?.GetValue(v) is string ls) yield return ls;
        }

        static readonly Regex Literal = new Regex("@\"(?:[^\"]|\"\")*\"|\"(?:[^\"\\\\\\n]|\\\\.)*\"|'(?:[^'\\\\\\n]|\\\\.)'");

        /// <summary>String and char literal contents outside // comments (escapes decoded).</summary>
        static IEnumerable<string> StringLiterals(string code)
        {
            foreach (var raw in code.Split('\n'))
            {
                string line = StripComment(raw);
                foreach (Match m in Literal.Matches(line))
                {
                    string v = m.Value;
                    if (v.StartsWith("@")) yield return v.Substring(2, v.Length - 3).Replace("\"\"", "\"");
                    else yield return Unescape(v.Substring(1, v.Length - 2));
                }
            }
        }

        static string Unescape(string s)
        {
            try { return Regex.Unescape(s.Replace("\\'", "'")); }
            catch (System.ArgumentException) { return s; } // escapes C# has and Regex does not: the raw text still has every glyph
        }

        static string StripComment(string line)
        {
            bool inStr = false, inChar = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if ((inStr || inChar) && c == '\\') { i++; continue; }
                if (!inChar && c == '"') inStr = !inStr;
                else if (!inStr && c == '\'') inChar = !inChar;
                else if (!inStr && !inChar && c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line.Substring(0, i);
            }
            return line;
        }
    }
}
