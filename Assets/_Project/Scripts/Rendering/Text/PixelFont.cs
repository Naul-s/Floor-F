using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Tiny built-in 5x7 bitmap font for floor signs ("1".."9", "F", "12A", "88", "HOTEL").
    /// Rasterised into a point-filtered texture, so the sign is crisp pixel art inside the 640x360 target and needs no
    /// font asset, TextMeshPro reference or shader beyond URP Unlit. Lower case maps to upper case; unknown chars -> '?'.
    /// </summary>
    static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;
        public const int Spacing = 1;

        static readonly Dictionary<char, string[]> glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
            ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
        };

        public static bool HasGlyph(char c) => glyphs.ContainsKey(char.ToUpperInvariant(c));

        // ---- the struck-out ORIGINAL floor number beside a sticker: the same 5x7 glyphs at 1 texel = 1 target pixel
        // (half the size of the label's 2-pixel texels; a 3x5 font was unreadable under the slash)
        public const int TinyWidth = GlyphWidth;
        public const int TinyHeight = GlyphHeight;

        /// <summary>Hi-res sign: 1 texel = 1 target pixel (the big label is drawn 2x2 per font texel).</summary>
        public const int MarkedTexelScale = 2;
        /// <summary>Left strip of a marked sign: the old plate, the original number in tiny glyphs, a red slash.</summary>
        public static int MarkStripWidth(string original) => (string.IsNullOrEmpty(original) ? 1 : original.Length) * (TinyWidth + 1) - 1 + 5;

        /// <summary>
        /// Sticker sign that still shows the floor's ORIGINAL number: the old dark plate peeks out on the left with the
        /// original in tiny glyphs struck through by a red diagonal, the sticker (label) fills the rest. Texels are
        /// target pixels (<see cref="MarkedTexelScale"/> x the plain sign's), so the plain label keeps its size.
        /// </summary>
        public static Texture2D CreateMarkedTexture(string text, Color ink, Color plate, string original, Color oldInk, Color oldPlate, Color strike)
        {
            if (string.IsNullOrEmpty(original)) original = "?";
            const int s = MarkedTexelScale;
            var mask = Rasterize(text, 1, out var big);
            int strip = MarkStripWidth(original);
            int w = strip + big.x * s, h = big.y * s;
            var px = new Color32[w * h];
            var rim = plate * 0.6f; rim.a = 1f;
            var oldRim = oldPlate * 0.6f; oldRim.a = 1f;
            // sticker part: exactly the plain sign, every font texel 2x2 (rim = the outer font texel ring)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < big.x * s; x++)
                {
                    int fx = x / s, fy = y / s;
                    bool edge = fx == 0 || fy == 0 || fx == big.x - 1 || fy == big.y - 1;
                    px[y * w + strip + x] = mask[fy * big.x + fx] ? ink : edge ? rim : plate;
                }
            // old plate strip (same 2-pixel rim on its outer sides)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < strip; x++)
                {
                    bool edge = x < s || y < s || y >= h - s;
                    px[y * w + x] = edge ? oldRim : oldPlate;
                }
            // small original, vertically centred
            int gy0 = (h - TinyHeight) / 2;
            for (int i = 0; i < original.Length; i++)
            {
                var g = Glyph(original[i]);
                int ox = 3 + i * (TinyWidth + 1);
                for (int row = 0; row < TinyHeight; row++)
                    for (int col = 0; col < TinyWidth; col++)
                        if (g[row][col] == '#') px[(gy0 + TinyHeight - 1 - row) * w + ox + col] = oldInk;
            }
            // red slash over it, from below-left to above-right (one pixel wide, pixel-stepped)
            int x0 = 2, y0 = gy0 - 2, x1 = strip - 1, y1 = gy0 + TinyHeight + 1;
            int steps = Mathf.Max(x1 - x0, y1 - y0);
            for (int k = 0; k <= steps; k++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, k / (float)steps));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, k / (float)steps));
                if (x >= 0 && x < strip && y >= 0 && y < h) px[y * w + x] = strike;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Sign_" + text + "_was_" + original,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static string[] Glyph(char c) => glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : glyphs['?'];

        /// <summary>Texture size for <paramref name="text"/> with <paramref name="padding"/> texels of plate on every side.</summary>
        public static Vector2Int Measure(string text, int padding)
        {
            int n = string.IsNullOrEmpty(text) ? 1 : text.Length;
            return new Vector2Int(n * (GlyphWidth + Spacing) - Spacing + padding * 2, GlyphHeight + padding * 2);
        }

        /// <summary>
        /// Ink mask, row-major with row 0 at the BOTTOM (Unity texture order). Length = size.x * size.y.
        /// Empty text renders as a single '?' so a sign is never blank.
        /// </summary>
        public static bool[] Rasterize(string text, int padding, out Vector2Int size)
        {
            if (string.IsNullOrEmpty(text)) text = "?";
            size = Measure(text, padding);
            var mask = new bool[size.x * size.y];
            for (int i = 0; i < text.Length; i++)
            {
                var g = Glyph(text[i]);
                int ox = padding + i * (GlyphWidth + Spacing);
                for (int row = 0; row < GlyphHeight; row++)
                {
                    int y = padding + (GlyphHeight - 1 - row); // glyph rows are written top-down
                    string line = g[row];
                    for (int col = 0; col < GlyphWidth; col++)
                        if (line[col] == '#') mask[y * size.x + ox + col] = true;
                }
            }
            return mask;
        }

        /// <summary>New point-filtered texture (caller owns it). One-texel darker rim makes the plate read as a frame.</summary>
        public static Texture2D CreateTexture(string text, Color ink, Color plate, int padding = 1)
        {
            var mask = Rasterize(text, padding, out var size);
            var tex = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false)
            {
                name = "Sign_" + text,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var rim = plate * 0.6f;
            rim.a = 1f;
            var px = new Color32[mask.Length];
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    int i = y * size.x + x;
                    bool edge = x == 0 || y == 0 || x == size.x - 1 || y == size.y - 1;
                    px[i] = mask[i] ? ink : edge ? rim : plate;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
