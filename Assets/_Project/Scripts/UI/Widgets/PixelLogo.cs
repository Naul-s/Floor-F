using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// The "FLOOR F" title logo as procedural pixel art (no asset, no shader): chunky 3-pixel-stroke block letters
    /// "FLOOR" with a gold-to-amber gradient, a bevel (light top edges, shaded bottom edges), a 2-row brown extrusion and a
    /// dark outline, then the "F" as an elevator floor indicator: a gold-framed panel with a red 7-segment LED "F"
    /// (unlit segments glow faintly, like the in-game LED). Point-filtered RawImages, so it stays crisp at any canvas
    /// scale and needs no font (language-independent).
    /// Layers: Shadow (stays put) | Float (bobs): Face, Led (lit segments + halo, flickers), Glint (a sweep across the letters).
    /// Animation (<see cref="Tick"/>, unscaled time): a 3-unit bob, a diagonal glint every few seconds, and an occasional
    /// LED flicker of two short dips (photosensitivity: at most 2 flashes in any second).
    /// </summary>
    sealed class PixelLogo
    {
        public const int LetterH = 13, Extrude = 2, LetterGap = 2, PanelGap = 4;
        /// <summary>Indicator panel: frame 1 + padding 1 around the 8x13 digit.</summary>
        public const int DigitW = 8, DigitH = 13, PanelW = DigitW + 4, PanelH = DigitH + 4;
        /// <summary>Bob amplitude (canvas units, snapped to whole units) and period.</summary>
        public const float Bob = 3f, BobPeriod = 3.4f;
        /// <summary>Glint: one sweep every <see cref="GlintEvery"/> s, taking <see cref="GlintTime"/> s.</summary>
        public const float GlintEvery = 6.5f, GlintTime = 0.7f;
        /// <summary>LED flicker: a burst of two dips (off 0.08 s, on 0.12 s, off 0.06 s) every 7-11 s.</summary>
        public const float FlickerMin = 7f, FlickerMax = 11f;

        // 3-pixel strokes, 13 rows. '#' = face.
        static readonly string[] GlyphF =
        {
            "#########", "#########", "#########", "###......", "###......", "#######..", "#######..", "#######..",
            "###......", "###......", "###......", "###......", "###......",
        };
        static readonly string[] GlyphL =
        {
            "###......", "###......", "###......", "###......", "###......", "###......", "###......", "###......",
            "###......", "###......", "#########", "#########", "#########",
        };
        static readonly string[] GlyphO =
        {
            ".########.", "##########", "##########", "###....###", "###....###", "###....###", "###....###", "###....###",
            "###....###", "###....###", "##########", "##########", ".########.",
        };
        static readonly string[] GlyphR =
        {
            "#########.", "##########", "##########", "###....###", "###....###", "##########", "##########", "#########.",
            "###..###..", "###...###.", "###...###.", "###....###", "###....###",
        };

        enum Px : byte { Empty, Face, Extrude1, Extrude2, Outline, Frame, Display, Ghost, PanelExtrude1, PanelExtrude2 }

        public readonly RectTransform Root, Float;
        public readonly RawImage Face, Led, Glint, Shadow;
        public readonly int W, H;
        public readonly float Pixel;
        readonly Texture2D baseTex, ledTex, glintTex;
        readonly bool[] face;
        readonly Color32[] glintPixels;
        float nextGlint, nextFlicker = -1f, flickerStart = -100f;
        int glintBand = int.MinValue;
        uint rng = 0x9E3779B9u;

        public Vector2 Size => new Vector2(W * Pixel, H * Pixel);
        public bool LedLit => Led.enabled;

        public PixelLogo(string name, Transform parent, float pixel, float glintPhase = 0f)
        {
            Pixel = pixel;
            int lettersW = 9 + 9 + 10 + 10 + 10 + 4 * LetterGap;
            W = 1 + lettersW + PanelGap + PanelW + 1;
            int top = 1;                       // outline row
            int panelY = top, lettersY = top + 2;
            H = top + PanelH + Extrude + 1;     // panel body + its extrusion + outline
            var map = new Px[W * H];
            var row = new int[W * H];          // face row inside its letter (gradient)
            face = new bool[W * H];

            int x = 1;
            foreach (var g in new[] { GlyphF, GlyphL, GlyphO, GlyphO, GlyphR })
            {
                for (int gy = 0; gy < g.Length; gy++)
                    for (int gx = 0; gx < g[gy].Length; gx++)
                        if (g[gy][gx] == '#') { int i = (lettersY + gy) * W + x + gx; map[i] = Px.Face; row[i] = gy; face[i] = true; }
                x += g[0].Length + LetterGap;
            }
            int panelX = x - LetterGap + PanelGap;
            for (int py = 0; py < PanelH; py++)
                for (int px = 0; px < PanelW; px++)
                {
                    bool frame = px == 0 || py == 0 || px == PanelW - 1 || py == PanelH - 1;
                    map[(panelY + py) * W + panelX + px] = frame ? Px.Frame : Px.Display;
                }
            int digitX = panelX + 2, digitY = panelY + 2;
            var lit = new bool[W * H];
            for (int s = 0; s < 7; s++)
            {
                Segment(s, out int sx, out int sy, out int sw, out int sh);
                bool on = s == 0 || s == 4 || s == 5 || s == 6; // a, e, f, g = "F"
                for (int yy = 0; yy < sh; yy++)
                    for (int xx = 0; xx < sw; xx++)
                    {
                        int i = (digitY + sy + yy) * W + digitX + sx + xx;
                        map[i] = Px.Ghost;
                        lit[i] = on;
                    }
            }
            // extrusion straight down (letters 2 rows, the panel too), then a 1-pixel outline around everything
            for (int yy = H - 1; yy >= 0; yy--)
                for (int xx = 0; xx < W; xx++)
                {
                    int i = yy * W + xx;
                    if (map[i] != Px.Empty) continue;
                    for (int k = 1; k <= Extrude; k++)
                    {
                        if (yy - k < 0) break;
                        var above = map[(yy - k) * W + xx];
                        if (above == Px.Face) { map[i] = k == 1 ? Px.Extrude1 : Px.Extrude2; break; }
                        if (above == Px.Frame || above == Px.Display || above == Px.Ghost) { map[i] = k == 1 ? Px.PanelExtrude1 : Px.PanelExtrude2; break; }
                    }
                }
            var solid = (Px[])map.Clone();
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                {
                    if (solid[yy * W + xx] != Px.Empty) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = xx + dx, ny = yy + dy;
                            near = nx >= 0 && ny >= 0 && nx < W && ny < H && solid[ny * W + nx] != Px.Empty;
                        }
                    if (near) map[yy * W + xx] = Px.Outline;
                }

            // ---- colours (texture rows run bottom-up: flip y)
            var basePixels = new Color32[W * H];
            var ledPixels = new Color32[W * H];
            glintPixels = new Color32[W * H];
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                {
                    int i = yy * W + xx, t = (H - 1 - yy) * W + xx;
                    basePixels[t] = ColorOf(map, row, W, i, xx, yy, panelX, panelY);
                    if (lit[i])
                    {
                        bool topEdge = yy == 0 || !lit[i - W];
                        ledPixels[t] = topEdge ? new Color32(255, 160, 120, 255) : (Color32)UIStyle.Led;
                    }
                    else if (map[i] == Px.Display || map[i] == Px.Ghost)
                    {
                        // halo: display pixels next to a lit segment
                        bool next = (xx > 0 && lit[i - 1]) || (xx < W - 1 && lit[i + 1]) || (yy > 0 && lit[i - W]) || (yy < H - 1 && lit[i + W]);
                        if (next) ledPixels[t] = new Color32(255, 80, 50, 96);
                    }
                }
            baseTex = MakeTexture(name + "Base", basePixels);
            ledTex = MakeTexture(name + "Led", ledPixels);
            glintTex = MakeTexture(name + "Glint", glintPixels);

            Root = UIBuild.Rect(name, parent);
            Root.sizeDelta = Size;
            Shadow = Raw("Shadow", Root, baseTex, new Color(0f, 0f, 0.03f, 0.55f));
            Shadow.rectTransform.anchoredPosition = new Vector2(0f, -2f * pixel);
            Float = UIBuild.Stretch(UIBuild.Rect("Float", Root));
            Face = Raw("Face", Float, baseTex, Color.white);
            Led = Raw("Led", Float, ledTex, Color.white);
            Glint = Raw("Glint", Float, glintTex, Color.white);
            Glint.enabled = false;
            nextGlint = 1.2f + glintPhase;
        }

        static RawImage Raw(string name, Transform parent, Texture2D tex, Color tint)
        {
            var rt = UIBuild.Stretch(UIBuild.Rect(name, parent));
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        Texture2D MakeTexture(string name, Color32[] pixels)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>7-segment layout in the 8x13 digit: a, b, c, d, e, f, g (2-pixel strokes, open corners).</summary>
        static void Segment(int s, out int x, out int y, out int w, out int h)
        {
            switch (s)
            {
                case 0: x = 2; y = 0; w = 4; h = 2; return;   // a
                case 1: x = 6; y = 2; w = 2; h = 3; return;   // b
                case 2: x = 6; y = 7; w = 2; h = 4; return;   // c
                case 3: x = 2; y = 11; w = 4; h = 2; return;  // d
                case 4: x = 0; y = 7; w = 2; h = 4; return;   // e
                case 5: x = 0; y = 2; w = 2; h = 3; return;   // f
                default: x = 2; y = 5; w = 4; h = 2; return;  // g
            }
        }

        static readonly Color GoldTop = UIStyle.Hex("#FFE066"), GoldBottom = UIStyle.Hex("#E8801C"), Highlight = UIStyle.Hex("#FFF7D1");
        static readonly Color Extrude1C = UIStyle.Hex("#9A4616"), Extrude2C = UIStyle.Hex("#5C260F");
        static readonly Color PanelExtrude1C = UIStyle.Hex("#5A4126"), PanelExtrude2C = UIStyle.Hex("#33241A");
        static readonly Color OutlineC = UIStyle.Hex("#110A12"), DisplayC = UIStyle.Hex("#1A0909");
        static readonly Color FrameTop = UIStyle.Hex("#FFE9A6"), FrameMid = UIStyle.Hex("#D9A441"), FrameBottom = UIStyle.Hex("#8A5A1E");

        /// <summary>Colour of map pixel <paramref name="i"/> (x, y; y down; <paramref name="width"/> = map row length).</summary>
        static Color32 ColorOf(Px[] map, int[] row, int width, int i, int x, int y, int panelX, int panelY)
        {
            switch (map[i])
            {
                case Px.Face:
                {
                    // letters never touch the map border (outline + panel margin), so the neighbours exist
                    int r = row[i];
                    var c = Color.Lerp(GoldTop, GoldBottom, Mathf.Pow(r / (float)(LetterH - 1), 1.3f));
                    bool topEdge = map[i - width] != Px.Face;
                    bool bottomEdge = map[i + width] != Px.Face;
                    bool leftEdge = map[i - 1] != Px.Face;
                    bool rightEdge = map[i + 1] != Px.Face;
                    if (topEdge) c = Color.Lerp(c, Highlight, 0.75f);
                    else if (bottomEdge) c *= 0.8f;
                    else if (leftEdge) c = Color.Lerp(c, Highlight, 0.22f);
                    else if (rightEdge) c *= 0.9f;
                    c.a = 1f;
                    return c;
                }
                case Px.Extrude1: return Extrude1C;
                case Px.Extrude2: return Extrude2C;
                case Px.PanelExtrude1: return PanelExtrude1C;
                case Px.PanelExtrude2: return PanelExtrude2C;
                case Px.Outline: return OutlineC;
                case Px.Display: return DisplayC;
                case Px.Ghost: return UIStyle.LedGhost;
                case Px.Frame:
                {
                    int py = y - panelY, px = x - panelX;
                    if (py == 0) return FrameTop;
                    if (py == PanelH - 1) return FrameBottom;
                    return px == 0 ? Color.Lerp(FrameMid, FrameTop, 0.35f) : FrameMid * new Color(0.85f, 0.85f, 0.85f, 1f);
                }
                default: return new Color32(0, 0, 0, 0);
            }
        }

        /// <summary>Bob + glint + flicker. <paramref name="time"/> = Time.unscaledTime (the pause menu animates too).</summary>
        public void Tick(float time)
        {
            Float.anchoredPosition = new Vector2(0f, Mathf.Round(Bob * Mathf.Sin(time * (2f * Mathf.PI / BobPeriod))));

            // glint: a 3-pixel "/" band crossing the letter faces
            if (time >= nextGlint)
            {
                float k = (time - nextGlint) / GlintTime;
                if (k >= 1f)
                {
                    Glint.enabled = false;
                    glintBand = int.MinValue;
                    nextGlint = time + GlintEvery;
                }
                else
                {
                    int band = Mathf.FloorToInt(Mathf.Lerp(-H, W + 2, k));
                    if (band != glintBand) { glintBand = band; PaintGlint(band); }
                    Glint.enabled = true;
                }
            }

            // LED flicker: off 0.08, on 0.12, off 0.06 - two dips, then steady for 7-11 s
            if (nextFlicker < 0f) nextFlicker = time + 3f;
            if (time >= nextFlicker) { flickerStart = time; nextFlicker = time + Mathf.Lerp(FlickerMin, FlickerMax, Next01()); }
            float f = time - flickerStart;
            bool off = (f >= 0f && f < 0.08f) || (f >= 0.2f && f < 0.26f);
            if (Led.enabled == off) Led.enabled = !off;
        }

        void PaintGlint(int band)
        {
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = x + y - band;
                    bool on = face[y * W + x] && d >= 0 && d < 3;
                    glintPixels[(H - 1 - y) * W + x] = on ? new Color32(255, 255, 240, (byte)(d == 1 ? 170 : 110)) : clear;
                }
            glintTex.SetPixels32(glintPixels);
            glintTex.Apply(false, false);
        }

        float Next01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Test hooks: forces the flicker / glint state.</summary>
        public void ForceLed(bool on) { Led.enabled = on; flickerStart = -100f; }
        /// <summary>Test hook: the animation clock starts over (next Tick time is "0").</summary>
        public void RestartAnimation() { nextFlicker = -1f; flickerStart = -100f; nextGlint = 1.2f; glintBand = int.MinValue; Glint.enabled = false; }
        public int FacePixelCount { get { int n = 0; foreach (bool b in face) if (b) n++; return n; } }
    }
}
