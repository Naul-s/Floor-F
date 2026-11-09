using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Personality for the neighbour buildings (all inside the city's ONE palette mesh - no extra draw calls): ground
    /// floor shops with neon signs that fit "Lucky Number" (LOTTO 로또, a 24h store, a fortune teller with a crystal ball,
    /// a ramen bar, a "777" pawn shop, a mahjong parlour, a laundromat), rooftop features (water tower, LUCKY 7 billboard,
    /// antenna + beacon, satellite dishes, a garden with string lights), fire escapes, balconies, AC units, awnings.
    /// Neon letters are whole pixels of the frame the mesh is built for (1 font texel = 1 target pixel; CityView builds
    /// one mesh per zoom, so the zoomed-out title frame is just as crisp) and each sign "hums": a slow
    /// breathing of its texels (&gt;= 3 s period, never a strobe), one sign also loses a letter now and then like HOTEL.
    /// </summary>
    sealed class CityDressing
    {
        public enum Shop { None, Lotto, Convenience, Fortune, Ramen, Pawn, Mahjong, Laundry, Flowers }
        public enum Roof { None, WaterTower, Billboard, Antenna, Dishes, Garden }

        public readonly struct Look
        {
            public readonly Shop Shop;
            public readonly Roof Roof;
            public readonly bool FireEscape, Balconies, AcUnits;
            public Look(Shop shop, Roof roof, bool fireEscape = false, bool balconies = false, bool acUnits = false)
            {
                Shop = shop; Roof = roof; FireEscape = fireEscape; Balconies = balconies; AcUnits = acUnits;
            }
        }

        /// <summary>One look per <see cref="CityView.Blocks"/> entry (same order). Back row: rooftop silhouettes only.</summary>
        public static readonly Look[] Looks =
        {
            new Look(Shop.Lotto, Roof.Garden),
            new Look(Shop.Fortune, Roof.Billboard, acUnits: true),
            new Look(Shop.Convenience, Roof.Antenna, fireEscape: true),
            new Look(Shop.Laundry, Roof.Dishes, balconies: true),
            new Look(Shop.Mahjong, Roof.WaterTower, acUnits: true),
            new Look(Shop.Ramen, Roof.Dishes, balconies: true),
            new Look(Shop.Pawn, Roof.WaterTower, fireEscape: true),
            new Look(Shop.Flowers, Roof.Antenna, acUnits: true),
            new Look(Shop.Convenience, Roof.Garden),
            new Look(Shop.Lotto, Roof.Billboard, balconies: true),
            new Look(Shop.None, Roof.Antenna),
            new Look(Shop.None, Roof.WaterTower),
            new Look(Shop.None, Roof.Dishes),
            new Look(Shop.None, Roof.Antenna),
            new Look(Shop.None, Roof.WaterTower),
            new Look(Shop.None, Roof.Antenna),
        };

        // ---- small pixel bitmaps (rows top-down; '.' = nothing, other chars = colour keys)
        static readonly string[] HangulLotto =
        {
            // 로 (7 wide) + gap + 또 (7 wide)
            "######..###.###",
            ".....#..#...#..",
            "######..#...#..",
            "#.......#...#..",
            "######..###.###",
            "...............",
            "...#.......#...",
            "#######.#######",
        };

        static readonly string[] CrystalBall =
        {
            "..#####..",
            ".#ooooo#.",
            "#o*ooooo#",
            "#o*ooooo#",
            "#ooooooo#",
            ".#ooooo#.",
            "..#####..",
            ".bbbbbbb.",
            "bbbbbbbbb",
        };

        static readonly string[] Clover =
        {
            ".##.##.",
            "###.###",
            ".#####.",
            "...#...",
            ".#####.",
            "###.###",
            ".##.##.",
        };

        static readonly string[] Bowl =
        {
            "...s.s...",
            "..s.s....",
            "#########",
            ".rrrrrrr.",
            "..rrrrr..",
            "...rrr...",
        };

        static readonly string[] Star =
        {
            "..#..",
            "..#..",
            "#####",
            ".###.",
            ".#.#.",
        };

        static readonly string[] Balls =
        {
            ".r...y...b.",
            "rrr.yyy.bbb",
            ".r...y...b.",
        };

        static readonly Color NeonYellow = new Color(1.00f, 0.92f, 0.30f);
        static readonly Color NeonPink = new Color(1.00f, 0.36f, 0.72f);
        static readonly Color NeonCyan = new Color(0.35f, 0.95f, 1.00f);
        static readonly Color NeonGreen = new Color(0.45f, 1.00f, 0.50f);
        static readonly Color NeonViolet = new Color(0.78f, 0.45f, 1.00f);
        static readonly Color NeonOrange = new Color(1.00f, 0.58f, 0.22f);
        static readonly Color NeonRed = new Color(1.00f, 0.25f, 0.24f);
        static readonly Color NeonGold = new Color(1.00f, 0.80f, 0.28f);
        static readonly Color NeonWhite = new Color(0.92f, 0.96f, 1.00f);
        static readonly Color Metal = new Color(0.14f, 0.15f, 0.17f);
        static readonly Color Wood = new Color(0.28f, 0.19f, 0.13f);
        static readonly Color Plant = new Color(0.13f, 0.30f, 0.16f);
        static readonly Color SignBack = new Color(0.06f, 0.055f, 0.07f);

        struct Neon
        {
            public int Texel;
            public Color On;
            public int Sign, Letter, Letters;
            public bool Stutter;
        }

        readonly PaletteAtlas atlas;
        readonly LivingLights lights;
        readonly TexelLedger ledger;
        readonly System.Func<float, float, float> wx, wy;
        readonly float px;
        readonly List<Neon> neons = new List<Neon>();
        int signs;

        /// <summary>Neon signs built (tests).</summary>
        public int SignCount => signs;
        public int NeonTexelCount => neons.Count;
        /// <summary>Palette texels of every neon tube / letter (tests: they sit on whole pixels of the active frame).</summary>
        public IEnumerable<int> NeonTexels { get { foreach (var n in neons) yield return n.Texel; } }
        /// <summary>Shop neon spilling onto the pavement in front of each shop (banded light pools).</summary>
        public readonly List<PixelGlow.LightPool> Spills = new List<PixelGlow.LightPool>();

        /// <param name="ledger">Records (first build) or replays (rebuild at another zoom) the lights / neon texels.</param>
        public CityDressing(PaletteAtlas atlas, LivingLights lights, TexelLedger ledger, System.Func<float, float, float> worldX, System.Func<float, float, float> worldY, float pixelsPerUnit)
        {
            this.atlas = atlas;
            this.lights = lights;
            this.ledger = ledger;
            wx = worldX;
            wy = worldY;
            px = 1f / Mathf.Max(1f, pixelsPerUnit);
        }

        int Light(LivingLights.Kind kind, Color on, Color off) => ledger.Take(() => lights.Add(kind, on, off));

        float Snap(float v) => Mathf.Round(v / px) * px;

        /// <summary>Front-facing rectangle in screen-plane units at depth z (on the pixel grid of the gameplay frame).</summary>
        void Rect(PaletteMeshBuilder b, float x0, float y0, float x1, float y1, float z, int texel)
            => b.Front(wx(x0, z), wy(y0, z), wx(x1, z), wy(y1, z), z, texel);

        /// <summary>Box whose front face is the screen-plane rectangle at depth z, <paramref name="depth"/> deep.</summary>
        void Box(PaletteMeshBuilder b, float x0, float y0, float x1, float y1, float z, float depth, Color c)
        {
            float wx0 = wx(x0, z), wx1 = wx(x1, z), wy0 = wy(y0, z), wy1 = wy(y1, z);
            b.Box(new Vector3(wx0, wy0, z), new Vector3(wx1, wy1, z + depth), c);
        }

        // ------------------------------------------------------------------ neon

        int NeonTexel(Color on, int letter, int letters, bool stutter)
        {
            int t = ledger.Take(() => atlas.Dynamic(on));
            if (!ledger.Replaying) neons.Add(new Neon { Texel = t, On = on, Sign = signs, Letter = letter, Letters = letters, Stutter = stutter });
            return t;
        }

        /// <summary>Text in the 5x7 pixel font, one target pixel of the frame this build is for per font texel (x <paramref name="scale"/>), left-bottom at (x, y).</summary>
        float Text(PaletteMeshBuilder b, string text, float x, float y, float z, Color on, int scale = 1, bool stutter = false)
        {
            var mask = PixelFont.Rasterize(text, 0, out var size);
            float s = px * scale;
            int letters = text.Length;
            var texels = new int[letters];
            for (int i = 0; i < letters; i++) texels[i] = NeonTexel(on, i, letters, stutter);
            for (int row = 0; row < size.y; row++)
                for (int col = 0; col < size.x;)
                {
                    if (!mask[row * size.x + col]) { col++; continue; }
                    int letter = col / (PixelFont.GlyphWidth + PixelFont.Spacing);
                    int end = col + 1;
                    while (end < size.x && mask[row * size.x + end] && end / (PixelFont.GlyphWidth + PixelFont.Spacing) == letter) end++;
                    Rect(b, x + col * s, y + row * s, x + end * s, y + (row + 1) * s, z, texels[letter]);
                    col = end;
                }
            signs++;
            return size.x * s;
        }

        /// <summary>Bitmap with a colour key map ('#' etc.); neon keys hum with the sign.</summary>
        void Bitmap(PaletteMeshBuilder b, string[] rows, float x, float y, float z, Dictionary<char, int> keys, int scale = 1)
        {
            int h = rows.Length;
            float s = px * scale;
            for (int r = 0; r < h; r++)
            {
                string line = rows[r];
                float yy = y + (h - 1 - r) * s;
                for (int c = 0; c < line.Length;)
                {
                    char k = line[c];
                    if (!keys.TryGetValue(k, out int texel)) { c++; continue; }
                    int end = c + 1;
                    while (end < line.Length && line[end] == k) end++;
                    Rect(b, x + c * s, yy, x + end * s, yy + s, z, texel);
                    c = end;
                }
            }
        }

        static float TextWidth(string text, int scale) => (text.Length * (PixelFont.GlyphWidth + PixelFont.Spacing) - PixelFont.Spacing) * scale;

        /// <summary>Dark sign board with a 1-px lit rim and the text centred on it.</summary>
        void SignBoard(PaletteMeshBuilder b, string text, float cx, float y, float z, Color neon, int scale = 1, bool stutter = false)
        {
            float tw = TextWidth(text, scale) * px, th = PixelFont.GlyphHeight * scale * px;
            float x0 = Snap(cx - tw * 0.5f - 3f * px), x1 = x0 + tw + 6f * px;
            float y0 = Snap(y), y1 = y0 + th + 6f * px;
            int back = atlas.Static(SignBack);
            int rim = NeonTexel(neon * 0.55f, 0, 1, false);
            Rect(b, x0, y0, x1, y1, z - 0.005f, rim);
            Rect(b, x0 + px, y0 + px, x1 - px, y1 - px, z - 0.01f, back);
            Text(b, text, x0 + 3f * px, y0 + 3f * px, z - 0.015f, neon, scale, stutter);
        }

        // ------------------------------------------------------------------ build

        /// <summary>Everything for one block: shop front, facade extras, rooftop. g = ground y, top = roof y (screen).</summary>
        public void Dress(PaletteMeshBuilder b, in CityView.Block blk, Look look, float g, float top, float depth, int index)
        {
            float z = blk.Z;
            bool back = blk.Z > 6f;
            bool shop = !back && look.Shop != Shop.None;
            if (shop) ShopFront(b, blk, look.Shop, g, z, index);
            // facade extras start above the shop sign (the zoomed-out title frame draws the signs larger in world units)
            float first = Mathf.Ceil(g) + (shop ? 5f : 3f);
            if (look.FireEscape) FireEscape(b, blk, first, top, z);
            if (look.Balconies) Balconies(b, blk, first, top, z);
            if (look.AcUnits) AcUnits(b, blk, first, top, z, index);
            Rooftop(b, blk, look.Roof, top, z, depth, back, index);
        }

        void ShopFront(PaletteMeshBuilder b, in CityView.Block blk, Shop shop, float g, float z, int index)
        {
            float x0 = blk.X0 + 0.5f, x1 = blk.X1 - 0.5f;
            // sign boards lean toward the hotel (wide blocks at the frame edge still show their whole sign)
            float lean = Mathf.Clamp((blk.X1 - blk.X0 - 6f) * 0.5f, 0f, 2f);
            float cx = (blk.X0 + blk.X1) * 0.5f + (blk.X1 <= 0 ? lean : -lean);
            float wz = z - 0.01f;
            Color glass;
            switch (shop)
            {
                case Shop.Convenience: glass = new Color(0.86f, 0.95f, 1.00f); break;
                case Shop.Fortune: glass = new Color(0.45f, 0.28f, 0.62f); break;
                case Shop.Ramen: glass = new Color(0.98f, 0.72f, 0.42f); break;
                case Shop.Laundry: glass = new Color(0.70f, 0.86f, 0.95f); break;
                case Shop.Mahjong: glass = new Color(0.62f, 0.20f, 0.18f); break;
                case Shop.Pawn: glass = new Color(0.85f, 0.68f, 0.36f); break;
                case Shop.Flowers: glass = new Color(0.90f, 0.82f, 0.62f); break;
                default: glass = new Color(0.95f, 0.80f, 0.45f); break;
            }
            int window = Light(LivingLights.Kind.Lamp, glass, glass * 0.3f);
            int frame = atlas.Static(Metal);
            float wy0 = g + 0.15f, wy1 = g + 1.55f;
            // door on the right third, display window on the rest
            float door = x1 - 1.0f;
            Rect(b, x0 - 0.1f, wy0 - 0.1f, x1 + 0.1f, wy1 + 0.1f, wz, frame);
            Rect(b, x0, wy0, door - 0.1f, wy1, wz - 0.002f, window);
            Rect(b, door, wy0, x1, wy1 - 0.1f, wz - 0.002f, atlas.Static(glass * 0.55f));
            float iz = wz - 0.004f;
            Color spill = NeonYellow;
            switch (shop)
            {
                case Shop.Convenience:
                {
                    // shelves + a green/teal stripe band with 24H
                    int shelf = atlas.Static(new Color(0.42f, 0.50f, 0.58f));
                    for (float sy = wy0 + 0.35f; sy < wy1 - 0.1f; sy += 0.4f) Rect(b, x0 + 0.1f, sy, door - 0.2f, sy + px, iz, shelf);
                    int stripe = atlas.Static(new Color(0.10f, 0.55f, 0.42f));
                    Rect(b, blk.X0, g + 1.7f, blk.X1, g + 1.7f + 2f * px, wz, stripe);
                    Rect(b, blk.X0, g + 2.55f, blk.X1, g + 2.55f + 2f * px, wz, atlas.Static(new Color(0.15f, 0.35f, 0.75f)));
                    SignBoard(b, "24H", cx, g + 1.82f, wz, NeonGreen);
                    spill = NeonGreen;
                    break;
                }
                case Shop.Lotto:
                {
                    Awning(b, blk, g, z, new Color(0.62f, 0.12f, 0.12f), new Color(0.75f, 0.70f, 0.62f));
                    SignBoard(b, "LOTTO", cx, g + 2.15f, wz, NeonYellow, 1, true);
                    var keys = new Dictionary<char, int>
                    {
                        ['#'] = NeonTexel(NeonPink, 0, 1, false),
                    };
                    float hw = HangulLotto[0].Length * px * 2f;
                    Bitmap(b, HangulLotto, Snap(cx - hw * 0.5f), Snap(g + 3.5f), wz - 0.015f, keys, 2);
                    var balls = new Dictionary<char, int>
                    {
                        ['r'] = atlas.Static(new Color(0.95f, 0.30f, 0.25f)),
                        ['y'] = atlas.Static(new Color(1.00f, 0.85f, 0.25f)),
                        ['b'] = atlas.Static(new Color(0.35f, 0.55f, 1.00f)),
                    };
                    Bitmap(b, Balls, Snap(x0 + 0.2f), Snap(wy0 + 0.25f), iz, balls);
                    var clover = new Dictionary<char, int> { ['#'] = atlas.Static(new Color(0.30f, 0.85f, 0.35f)) };
                    Bitmap(b, Clover, Snap(door - 0.9f), Snap(wy0 + 0.5f), iz, clover);
                    break;
                }
                case Shop.Fortune:
                {
                    var ball = new Dictionary<char, int>
                    {
                        ['#'] = NeonTexel(NeonViolet, 0, 1, false),
                        ['o'] = NeonTexel(new Color(0.45f, 0.75f, 1.00f), 0, 1, false),
                        ['*'] = atlas.Static(NeonWhite),
                        ['b'] = atlas.Static(new Color(0.45f, 0.30f, 0.20f)),
                    };
                    Bitmap(b, CrystalBall, Snap((x0 + door) * 0.5f - 4.5f * px), Snap(wy0 + 0.25f), iz, ball);
                    SignBoard(b, "FORTUNE", cx, g + 1.8f, wz, NeonViolet);
                    spill = NeonViolet;
                    // vertical blade sign with a big star
                    float bx = Snap(blk.X1 - 0.7f), bx1 = Snap(bx + 0.5f), by0 = Snap(g + 2.6f), by1 = Snap(g + 4.6f);
                    int violet = NeonTexel(NeonViolet, 0, 1, false);
                    Rect(b, bx, by0, bx1, by1, z - 0.35f, atlas.Static(SignBack));
                    Rect(b, bx, by0, bx + px, by1, z - 0.36f, violet);
                    Rect(b, bx1 - px, by0, bx1, by1, z - 0.36f, violet);
                    Bitmap(b, Star, Snap(bx + 0.25f - 2.5f * px), Snap(g + 3.3f), z - 0.37f, new Dictionary<char, int> { ['#'] = NeonTexel(NeonYellow, 0, 1, false) });
                    break;
                }
                case Shop.Ramen:
                {
                    // noren curtain strips + red lanterns + RAMEN sign + a bowl
                    int noren = atlas.Static(new Color(0.20f, 0.10f, 0.32f));
                    for (float nx = x0; nx < door - 0.2f; nx += 0.45f) Rect(b, nx, wy1 - 0.45f, nx + 0.38f, wy1, iz, noren);
                    SignBoard(b, "RAMEN", cx, g + 1.85f, wz, NeonOrange);
                    spill = NeonOrange;
                    var bowl = new Dictionary<char, int>
                    {
                        ['#'] = atlas.Static(new Color(0.95f, 0.92f, 0.85f)),
                        ['r'] = NeonTexel(NeonRed, 0, 1, false),
                        ['s'] = atlas.Static(new Color(0.75f, 0.75f, 0.80f)),
                    };
                    Bitmap(b, Bowl, Snap(x0 + 0.15f), Snap(wy0 + 0.2f), iz, bowl);
                    int lantern = Light(LivingLights.Kind.Lamp, new Color(1.00f, 0.32f, 0.22f), new Color(0.35f, 0.10f, 0.08f));
                    foreach (float lx in new[] { blk.X0 + 0.25f, blk.X1 - 0.55f })
                        Box(b, lx, g + 1.0f, lx + 0.3f, g + 1.45f, z - 0.4f, 0.3f, new Color(0.9f, 0.25f, 0.2f));
                    foreach (float lx in new[] { blk.X0 + 0.25f, blk.X1 - 0.55f })
                        Rect(b, lx + px, g + 1.0f + px, lx + 0.3f - px, g + 1.45f - px, z - 0.41f, lantern);
                    break;
                }
                case Shop.Pawn:
                {
                    Awning(b, blk, g, z, new Color(0.12f, 0.30f, 0.20f), new Color(0.12f, 0.30f, 0.20f) * 1.6f);
                    float w = TextWidth("777", 2) * px + 6f * px;
                    SignBoard(b, "777", cx, g + 2.15f, wz, NeonGold, 2);
                    spill = NeonGold;
                    Text(b, "PAWN", Snap(x0 + 0.15f), Snap(wy1 - 9f * px), iz, NeonGold);
                    // three golden balls hanging from a bracket (whole pixels of this build's frame)
                    float bx = Snap(cx + w * 0.5f + 0.25f);
                    int gold = NeonTexel(NeonGold, 0, 1, false);
                    Rect(b, bx, Snap(g + 3.4f), Snap(bx + 0.6f), Snap(g + 3.4f) + px, z - 0.3f, atlas.Static(Metal));
                    foreach (var o in new[] { new Vector2(0.05f, 3.0f), new Vector2(0.4f, 3.0f), new Vector2(0.22f, 2.7f) })
                    {
                        float ox = Snap(bx + o.x), oy = Snap(g + o.y);
                        Rect(b, ox, oy, ox + 3f * px, oy + 3f * px, z - 0.31f, gold);
                    }
                    break;
                }
                case Shop.Mahjong:
                {
                    SignBoard(b, "MAHJONG", cx, g + 1.85f, wz, NeonRed, 1, true);
                    spill = NeonRed;
                    int tile = atlas.Static(new Color(0.92f, 0.90f, 0.80f));
                    int ink = atlas.Static(new Color(0.20f, 0.45f, 0.30f));
                    for (int i = 0; i < 4; i++)
                    {
                        float tx = x0 + 0.2f + i * 0.4f;
                        Rect(b, tx, wy0 + 0.3f, tx + 0.3f, wy0 + 0.72f, iz, tile);
                        Rect(b, tx + 0.1f, wy0 + 0.45f, tx + 0.2f, wy0 + 0.6f, iz - 0.001f, ink);
                    }
                    break;
                }
                case Shop.Laundry:
                {
                    Awning(b, blk, g, z, new Color(0.15f, 0.35f, 0.62f), new Color(0.75f, 0.80f, 0.85f));
                    SignBoard(b, "LAUNDRY", cx, g + 2.15f, wz, NeonCyan);
                    spill = NeonCyan;
                    int washer = atlas.Static(new Color(0.85f, 0.88f, 0.90f));
                    int drum = atlas.Static(new Color(0.20f, 0.30f, 0.42f));
                    for (float tx = x0 + 0.15f; tx + 0.6f < door - 0.1f; tx += 0.75f)
                    {
                        Rect(b, tx, wy0, tx + 0.6f, wy0 + 0.75f, iz, washer);
                        Rect(b, tx + 0.15f, wy0 + 0.15f, tx + 0.45f, wy0 + 0.45f, iz - 0.001f, drum);
                    }
                    break;
                }
                case Shop.Flowers:
                {
                    Awning(b, blk, g, z, new Color(0.62f, 0.25f, 0.45f), new Color(0.85f, 0.80f, 0.75f));
                    SignBoard(b, "FLOWERS", cx, g + 2.15f, wz, NeonPink);
                    spill = NeonPink;
                    int[] petals = { atlas.Static(new Color(0.95f, 0.35f, 0.45f)), atlas.Static(new Color(1.0f, 0.85f, 0.3f)), atlas.Static(new Color(0.7f, 0.45f, 0.95f)) };
                    int leaf = atlas.Static(Plant * 1.4f);
                    for (int i = 0; x0 + 0.2f + i * 0.35f < door - 0.3f; i++)
                    {
                        float tx = x0 + 0.2f + i * 0.35f;
                        Rect(b, tx, wy0, tx + 0.25f, wy0 + 0.35f, iz, leaf);
                        Rect(b, tx + 0.05f, wy0 + 0.35f, tx + 0.2f, wy0 + 0.5f, iz, petals[i % petals.Length]);
                    }
                    break;
                }
            }
            // the shop's neon spills onto the pavement and the bottom of the facade (banded light pool)
            float sz = z - 0.6f;
            if (!ledger.Replaying) Spills.Add(new PixelGlow.LightPool(new Vector3(wx(cx, sz), wy(g + 0.5f, sz), sz), new Vector2(2.6f, 1.5f), spill * SpillIntensity));
        }

        /// <summary>Shop neon spill pool intensity (light units, see PixelGlow.Bands).</summary>
        public const float SpillIntensity = 0.24f;

        void Awning(PaletteMeshBuilder b, in CityView.Block blk, float g, float z, Color a, Color c)
        {
            int ta = atlas.Static(a), tc = atlas.Static(c);
            float az = z - 0.9f, sy = g + 1.65f;
            for (int i = 0; blk.X0 + 0.5f + i * 0.5f < blk.X1 - 0.5f; i++)
            {
                float ax = blk.X0 + 0.5f + i * 0.5f;
                int tex = (i & 1) == 0 ? ta : tc;
                b.Box(new Vector3(wx(ax, az), wy(sy, az), az), new Vector3(wx(ax + 0.5f, az), wy(sy + 0.4f, az), z), tex, tex, tex);
            }
        }

        void FireEscape(PaletteMeshBuilder b, in CityView.Block blk, float first, float top, float z)
        {
            int metal = atlas.Static(new Color(0.20f, 0.20f, 0.22f));
            int rail = atlas.Static(new Color(0.30f, 0.30f, 0.33f));
            float x0 = blk.X0 + 0.6f, x1 = Mathf.Min(blk.X1 - 0.6f, x0 + 3.2f);
            float fz = z - 0.45f;
            for (float y = first; y + 1f < top - 1f; y += 2f)
            {
                // platform (slab + its top), railing, ladder down to the next platform
                b.Box(new Vector3(wx(x0, fz), wy(y - 0.1f, fz), fz), new Vector3(wx(x1, fz), wy(y, fz), z), metal, rail, metal);
                Rect(b, x0, y + 0.55f, x1, y + 0.55f + px, fz - 0.01f, rail);
                for (float rx = x0; rx <= x1 + 0.01f; rx += 0.5f) Rect(b, rx, y, rx + px, y + 0.55f, fz - 0.01f, rail);
                // diagonal stairs to the platform above, pixel-stepped
                int steps = 10;
                for (int s = 0; s < steps; s++)
                {
                    float u = s / (float)steps;
                    float sx = Mathf.Lerp(x0 + 0.2f, x1 - 0.4f, u), sy = Mathf.Lerp(y + 0.05f, y + 1.9f, u);
                    Rect(b, sx, sy, sx + 0.3f, sy + px, fz + 0.2f, metal);
                }
            }
        }

        void Balconies(PaletteMeshBuilder b, in CityView.Block blk, float first, float top, float z)
        {
            int slab = atlas.Static(new Color(0.30f, 0.29f, 0.30f));
            int rail = atlas.Static(new Color(0.42f, 0.42f, 0.45f));
            float bz = z - 0.35f;
            for (float y = first; y + 1f <= top - 1f; y += 2f)
            {
                float x0 = blk.X0 + 0.7f, x1 = blk.X1 - 0.7f;
                b.Box(new Vector3(wx(x0, bz), wy(y - 0.12f, bz), bz), new Vector3(wx(x1, bz), wy(y, bz), z), slab, atlas.Static(PaletteMeshBuilder.Top(new Color(0.30f, 0.29f, 0.30f))), slab);
                Rect(b, x0, y + 0.42f, x1, y + 0.42f + px, bz - 0.01f, rail);
                for (float rx = x0; rx <= x1 + 0.01f; rx += 0.25f) Rect(b, rx, y, rx + px, y + 0.42f, bz - 0.01f, rail);
            }
        }

        void AcUnits(PaletteMeshBuilder b, in CityView.Block blk, float first, float top, float z, int index)
        {
            var body = new Color(0.50f, 0.52f, 0.55f);
            int grille = atlas.Static(new Color(0.25f, 0.26f, 0.28f));
            int k = 0;
            for (float y = first; y + 1f <= top - 1f; y += 2f)
                for (float x = blk.X0 + 1f; x + 1f <= blk.X1 - 1f; x += 2f, k++)
                {
                    if (LightSchedule.Hash(RenderingStyle.CitySeed, index, k, 140) > 0.3f) continue;
                    float az = z - 0.3f;
                    Box(b, x, y - 0.45f, x + 0.6f, y - 0.1f, az, 0.3f, body);
                    Rect(b, x + 0.1f, y - 0.35f, x + 0.5f, y - 0.35f + px, az - 0.005f, grille);
                    Rect(b, x + 0.1f, y - 0.25f, x + 0.5f, y - 0.25f + px, az - 0.005f, grille);
                }
        }

        void Rooftop(PaletteMeshBuilder b, in CityView.Block blk, Roof roof, float top, float z, float depth, bool back, int index)
        {
            float w = blk.X1 - blk.X0;
            float rz = z + Mathf.Min(1.2f, depth * 0.3f);
            var dark = back ? new Color(0.05f, 0.055f, 0.09f) : Metal;
            switch (roof)
            {
                case Roof.WaterTower:
                {
                    float tx = blk.X0 + w * (0.25f + 0.4f * LightSchedule.Hash(RenderingStyle.CitySeed, index, 141, 0));
                    var wood = back ? new Color(0.07f, 0.07f, 0.11f) : Wood;
                    foreach (float lx in new[] { tx + 0.1f, tx + 1.2f })
                        Box(b, lx, top, lx + 0.1f, top + 0.9f, rz, 0.1f, dark);
                    Box(b, tx, top + 0.9f, tx + 1.4f, top + 2.3f, rz, 1.2f, wood);
                    int band = atlas.Static(wood * 0.6f);
                    Rect(b, tx, top + 1.25f, tx + 1.4f, top + 1.25f + px, rz - 0.01f, band);
                    Rect(b, tx, top + 1.85f, tx + 1.4f, top + 1.85f + px, rz - 0.01f, band);
                    Box(b, tx + 0.15f, top + 2.3f, tx + 1.25f, top + 2.6f, rz + 0.1f, 1.0f, wood * 0.8f);
                    Box(b, tx + 0.45f, top + 2.6f, tx + 0.95f, top + 2.8f, rz + 0.3f, 0.6f, wood * 0.8f);
                    break;
                }
                case Roof.Billboard:
                {
                    float bx0 = blk.X0 + 0.5f, bx1 = Mathf.Min(blk.X1 - 0.5f, bx0 + 5.5f);
                    foreach (float lx in new[] { bx0 + 0.6f, bx1 - 0.7f })
                        Box(b, lx, top, lx + 0.1f, top + 0.8f, rz, 0.1f, dark);
                    Box(b, bx0, top + 0.8f, bx1, top + 2.8f, rz, 0.15f, new Color(0.10f, 0.09f, 0.14f));
                    int face = Light(LivingLights.Kind.Lamp, new Color(0.22f, 0.12f, 0.30f), new Color(0.10f, 0.07f, 0.13f));
                    Rect(b, bx0 + 0.1f, top + 0.9f, bx1 - 0.1f, top + 2.7f, rz - 0.01f, face);
                    float cx = (bx0 + bx1) * 0.5f;
                    float tw = TextWidth("LUCKY", 1) * px;
                    Text(b, "LUCKY", Snap(cx - tw * 0.5f - 0.6f), Snap(top + 1.45f), rz - 0.02f, NeonYellow);
                    Text(b, "7", Snap(cx + tw * 0.5f - 0.35f), Snap(top + 1.1f), rz - 0.02f, NeonRed, 2);
                    // two lamps on arms lighting it from below
                    int lamp = Light(LivingLights.Kind.Lamp, new Color(1.0f, 0.95f, 0.75f), new Color(0.3f, 0.28f, 0.2f));
                    foreach (float lx in new[] { bx0 + 1.0f, bx1 - 1.3f })
                    {
                        Rect(b, lx, top + 0.6f, lx + 0.3f, top + 0.75f, rz - 0.3f, lamp);
                        Rect(b, lx + 0.1f, top + 0.75f, lx + 0.1f + px, top + 0.9f, rz - 0.3f, atlas.Static(dark));
                    }
                    break;
                }
                case Roof.Antenna:
                {
                    float ax = blk.X0 + w * (0.3f + 0.4f * LightSchedule.Hash(RenderingStyle.CitySeed, index, 142, 0));
                    float h = back ? 4f : 3f;
                    int pole = atlas.Static(dark);
                    Rect(b, ax, top, ax + 2f * px, top + h, rz, pole);
                    for (int i = 1; i <= 3; i++)
                    {
                        float cw = 0.5f - i * 0.1f;
                        Rect(b, ax - cw, top + h * (0.35f + i * 0.17f), ax + cw + 2f * px, top + h * (0.35f + i * 0.17f) + px, rz, pole);
                    }
                    int beacon = Light(LivingLights.Kind.Beacon, new Color(1.0f, 0.18f, 0.15f), new Color(0.25f, 0.06f, 0.06f));
                    Rect(b, ax - px, top + h, ax + 3f * px, top + h + 3f * px, rz - 0.01f, beacon);
                    break;
                }
                case Roof.Dishes:
                {
                    var dish = back ? new Color(0.09f, 0.09f, 0.13f) : new Color(0.62f, 0.64f, 0.68f);
                    int rim = atlas.Static(dish * 0.7f);
                    for (int i = 0; i < 2; i++)
                    {
                        float dx = blk.X0 + 0.8f + i * Mathf.Max(1.4f, w * 0.4f);
                        if (dx + 0.7f > blk.X1 - 0.3f) break;
                        Rect(b, dx + 0.3f, top, dx + 0.3f + 2f * px, top + 0.4f, rz, atlas.Static(dark));
                        Box(b, dx, top + 0.35f, dx + 0.7f, top + 1.0f, rz - 0.1f, 0.12f, dish);
                        Rect(b, dx, top + 0.35f, dx + 0.7f, top + 0.35f + px, rz - 0.111f, rim);
                        Rect(b, dx + 0.3f, top + 0.6f, dx + 0.4f, top + 0.7f, rz - 0.112f, rim);
                    }
                    break;
                }
                case Roof.Garden:
                {
                    var planter = new Color(0.30f, 0.22f, 0.16f);
                    for (float gx = blk.X0 + 0.4f; gx + 0.8f < blk.X1 - 0.3f; gx += 1.1f)
                    {
                        Box(b, gx, top, gx + 0.8f, top + 0.3f, rz - 0.6f, 0.6f, planter);
                        Box(b, gx + 0.05f, top + 0.3f, gx + 0.75f, top + 0.75f + 0.2f * LightSchedule.Hash(RenderingStyle.CitySeed, index, (int)(gx * 10f), 143), rz - 0.55f, 0.5f, Plant);
                    }
                    // string lights: two interleaved sets that gently chase (the HOTEL marquee's schedule)
                    float sy = top + 1.3f;
                    int pole = atlas.Static(dark);
                    Rect(b, blk.X0 + 0.2f, top, blk.X0 + 0.2f + px, sy, rz - 0.7f, pole);
                    Rect(b, blk.X1 - 0.3f, top, blk.X1 - 0.3f + px, sy, rz - 0.7f, pole);
                    int a = Light(LivingLights.Kind.BulbA, new Color(1.0f, 0.85f, 0.45f), new Color(0.35f, 0.28f, 0.16f));
                    int c = Light(LivingLights.Kind.BulbB, new Color(1.0f, 0.55f, 0.65f), new Color(0.32f, 0.18f, 0.2f));
                    int k = 0;
                    for (float lx = blk.X0 + 0.4f; lx < blk.X1 - 0.4f; lx += 0.4f, k++)
                    {
                        float sag = 0.25f * Mathf.Sin(Mathf.PI * (lx - blk.X0) / w);
                        Rect(b, Snap(lx), Snap(sy - sag), Snap(lx) + 2f * px, Snap(sy - sag) + 2f * px, rz - 0.71f, (k & 1) == 0 ? a : c);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ hum

        public const float HumMinPeriod = 3.2f;
        public const float HumMaxPeriod = 5.5f;
        public const float HumDepth = 0.09f;

        /// <summary>
        /// Neon brightness 0..1 for sign <paramref name="sign"/>: a slow breathing (depth below the 10% photosensitivity
        /// threshold, so it never counts as a flash), plus (stutter signs) the HOTEL sign's gentle letter drop-outs.
        /// Pure (EditMode / photosensitivity tested).
        /// </summary>
        public static float NeonLevel(int seed, int sign, int letter, int letters, bool stutter, float time)
        {
            float period = Mathf.Lerp(HumMinPeriod, HumMaxPeriod, LightSchedule.Hash(seed, sign, 150, 0));
            float hum = 1f - HumDepth * (0.5f + 0.5f * Mathf.Sin((time / period + LightSchedule.Hash(seed, sign, 151, 0)) * Mathf.PI * 2f));
            if (stutter) hum *= LightSchedule.SignLetterLevel(seed + 31 * (sign + 1), letter, letters, time);
            return hum;
        }

        public void Tick(float time, int seed)
        {
            foreach (var n in neons)
                atlas.Set(n.Texel, n.On * NeonLevel(seed, n.Sign, n.Letter, n.Letters, n.Stutter, time));
        }
    }
}
