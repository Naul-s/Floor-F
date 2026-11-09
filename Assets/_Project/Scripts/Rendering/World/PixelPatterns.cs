using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>Wall surface patterns (pixel-art masonry, cladding, trim).</summary>
    enum WallPattern { None, Brick, Stone, Panel, Tile, Bands, Dentil, Gravel }

    /// <summary>
    /// Pure, tiling pixel patterns (EditMode-tested): a shade multiplier per tile pixel. One tile pixel is drawn as one
    /// low-res target pixel in every camera frame (gameplay and zoomed-out title), anchored on the screen-plane pixel
    /// grid, so courses and joints are crisp and continue across neighbouring surfaces:
    ///   palette meshes (city walls)  - <see cref="PaletteAtlas.Tile"/> + <see cref="PaletteMeshBuilder.PatternFront"/>;
    ///   lit meshes (the hotel)       - <see cref="RenderAssets.PatternLit"/> (_BaseMap, screen-plane UVs, tiling = ppu).
    /// Flat bands, no dither or per-pixel noise (the lighting tests forbid speckle): tone varies per brick / block only.
    /// </summary>
    static class PixelPatterns
    {
        public const float Joint = 0.66f;

        public static Vector2Int Size(WallPattern p)
        {
            switch (p)
            {
                case WallPattern.Brick: return new Vector2Int(32, 8);
                case WallPattern.Stone: return new Vector2Int(24, 12);
                case WallPattern.Panel: return new Vector2Int(32, 12);
                case WallPattern.Tile: return new Vector2Int(16, 8);
                case WallPattern.Bands: return new Vector2Int(32, 9);
                case WallPattern.Dentil: return new Vector2Int(8, 4);
                case WallPattern.Gravel: return new Vector2Int(16, 8);
                default: return new Vector2Int(1, 1);
            }
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>Small stable integer hash (0..3) for per-brick tones.</summary>
        static int Tone(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h & 3u);
            }
        }

        static readonly float[] Tones = { 0.90f, 0.96f, 1.00f, 1.05f };

        /// <summary>Shade multiplier of tile pixel (x, y) - y counts up from the tile's bottom row; wraps.</summary>
        public static float Shade(WallPattern p, int x, int y)
        {
            var s = Size(p);
            x = Mod(x, s.x);
            y = Mod(y, s.y);
            switch (p)
            {
                case WallPattern.Brick:
                {
                    // running bond: 3-px brick + 1-px bed joint, 8-px bricks (incl. the head joint) offset half a brick
                    int course = y / 4, yy = y % 4;
                    if (yy == 0) return Joint;
                    int bx = x + (course & 1) * 4;
                    if (bx % 8 == 0) return Joint;
                    float t = Tones[Tone(bx / 8 % 4, course, 1)];
                    return yy == 3 ? t * 1.12f : t;
                }
                case WallPattern.Stone:
                {
                    // rusticated blocks 12x6 with a bevel: light top/left edge, shaded bottom/right edge
                    int course = y / 6, yy = y % 6;
                    int bx = x + (course & 1) * 6, xx = bx % 12;
                    if (yy == 0 || xx == 0) return 0.58f;
                    float t = Tones[Tone(bx / 12 % 2, course, 2)];
                    if (yy == 5) return t * 1.14f;
                    if (xx == 1) return t * 1.06f;
                    if (yy == 1 || xx == 11) return t * 0.84f;
                    return t;
                }
                case WallPattern.Panel:
                {
                    // concrete panels 16x12: dark joint, a lit lip above each horizontal joint, two tie-hole dots
                    if (x % 16 == 0 || y == 0) return Joint;
                    if (y == 1) return 1.1f;
                    if (y == 7 && (x % 16 == 4 || x % 16 == 12)) return 0.8f;
                    return x >= 16 ? 0.97f : 1f;
                }
                case WallPattern.Tile:
                {
                    // small glazed tiles 4x4 with grout, a lit top-left corner pixel
                    int tx = x / 4, ty = y / 4;
                    if (x % 4 == 0 || y % 4 == 0) return 0.78f;
                    float t = Tones[Tone(tx, ty, 3)];
                    return x % 4 == 1 && y % 4 == 3 ? t * 1.1f : t;
                }
                case WallPattern.Bands:
                    // far towers: floor lines (a shaded line under a lit lip) every 9 px
                    return y == 0 ? 0.74f : y == 1 ? 1.08f : 1f;
                case WallPattern.Dentil:
                    // a row of dentil blocks: 2 px block, 2 px gap (uniform in y, so any band height works)
                    return x % 4 < 2 ? 1.1f : 0.64f;
                case WallPattern.Gravel:
                {
                    // roof pavers: 4x2 slabs, offset rows
                    int row = y / 2;
                    int bx = x + (row & 1) * 2;
                    if (y % 2 == 0) return 0.8f;
                    if (bx % 4 == 0) return 0.86f;
                    return Tones[Tone(bx / 4, row, 4)];
                }
                default:
                    return 1f;
            }
        }

        /// <summary>Largest shade of the pattern (lit textures store shade / max and scale the material colour by max).</summary>
        public static float Max(WallPattern p)
        {
            var s = Size(p);
            float m = 0f;
            for (int y = 0; y < s.y; y++)
                for (int x = 0; x < s.x; x++) m = Mathf.Max(m, Shade(p, x, y));
            return m;
        }

        /// <summary>Tile pixels (row-major, bottom row first) of <paramref name="c"/> times the shade.</summary>
        public static Color32[] Bake(WallPattern p, Color c, float scale = 1f)
        {
            var s = Size(p);
            var px = new Color32[s.x * s.y];
            for (int y = 0; y < s.y; y++)
                for (int x = 0; x < s.x; x++)
                {
                    float k = Shade(p, x, y) * scale;
                    px[y * s.x + x] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
                }
            return px;
        }

        /// <summary>Distinct shades in one tile (tests: a pattern, not a flat colour; few levels, not noise).</summary>
        public static int Levels(WallPattern p)
        {
            var s = Size(p);
            var set = new System.Collections.Generic.HashSet<int>();
            for (int y = 0; y < s.y; y++)
                for (int x = 0; x < s.x; x++) set.Add(Mathf.RoundToInt(Shade(p, x, y) * 1000f));
            return set.Count;
        }
    }
}
