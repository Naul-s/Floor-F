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
