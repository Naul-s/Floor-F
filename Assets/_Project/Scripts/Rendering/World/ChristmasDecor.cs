using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Christmas Eve dressing (ISeasonQuery.IsChristmas, day 10), built on first use, shown only that night (and in the
    /// winter endings):
    ///   Lobby     a big decorated tree (lit tiers, ornaments, a glowing star) with presents, in the lobby's front corner.
    ///   Floors    a wreath with a red bow beside the elevator doors and on both pilasters, on every floor.
    ///   Facade    a garland swagged under the cornice, strings of 2x2 bulbs down both pilasters and along the canopy.
    ///   Street    bulb swags along the neighbours' roof edges and over their sign boards, holly + a red bow over every
    ///             shop window, candles in some of the neighbours' windows, the FORTUNE sign turned into MERRY XMAS, a
    ///             snowman and a little lit fir on the sidewalk by the hotel entrance (seen in the gameplay frame).
    ///   Roof      a snowman by the HOTEL sign, Santa's sleigh (with its sack of presents) parked on the stair hut - Santa's
    ///             destination is the rooftop - and a Santa hat on the HOTEL sign's H.
    /// Bulbs and ornaments share <see cref="WinterSchedule.BulbGroups"/> palette texels that fade SLOWLY in and out of
    /// phase (<see cref="WinterSchedule.BulbLevel"/>: a 3.6 s raised cosine, never on/off) - photosensitivity-safe.
    /// Cost while shown: tree/presents (lit, ~5 draws), one palette mesh for the roof + ornaments, one pixel-detail mesh per
    /// frame zoom for wreaths and bulbs, the sign hat.
    /// </summary>
    sealed class ChristmasDecor
    {
        // peaks at 0.88: at full brightness a bulb reaches into the pixel glow's strong range (a small halo that breathes
        // with the 3.6 s fade), at the low end (x0.42) it is far below it - no permanent bloom over the whole street
        static readonly Color[] BulbColors =
        {
            new Color(0.88f, 0.20f, 0.16f),
            new Color(0.88f, 0.70f, 0.26f),
            new Color(0.30f, 0.84f, 0.38f),
            new Color(0.36f, 0.54f, 0.88f),
        };
        static readonly Color XmasRed = new Color(0.95f, 0.22f, 0.20f);
        static readonly Color XmasGreen = new Color(0.32f, 0.92f, 0.42f);
        static readonly Color StarColor = new Color(1.00f, 0.88f, 0.40f);

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly HotelFacade facade;
        readonly CityView city;
        readonly PaletteAtlas atlas;
        readonly FloorIndex lobby;
        readonly int seed;
        readonly int[] bulbs = new int[WinterSchedule.BulbGroups];
        readonly Dictionary<int, Mesh> detailMeshes = new Dictionary<int, Mesh>();
        readonly List<Renderer> renderers = new List<Renderer>();
        int star = -1;
        MeshFilter detailFilter;
        int detailKey;

        public Transform Root { get; }
        public bool Built { get; private set; }
        public bool Shown { get; private set; }
        /// <summary>Tests: force the decorations on/off (null = follow ISeasonQuery.IsChristmas).</summary>
        public bool? Force { get; set; }
        /// <summary>Endings keep (or bring) the decorations on.</summary>
        public bool EndingShow { get; set; }
        public Bounds TreeBounds { get; private set; }
        public Vector3 SleighCentre { get; private set; }
        public Vector3 SnowmanCentre { get; private set; }
        public int WreathCount { get; private set; }
        public int BulbCount { get; private set; }
        public int CandleCount { get; private set; }
        public int HollyCount { get; private set; }
        public Vector3 SidewalkSnowman { get; private set; }
        /// <summary>Screen-plane rectangle of the MERRY XMAS sign (zero when the city has no FORTUNE sign).</summary>
        public Rect XmasSign { get; private set; }
        public MeshRenderer DetailRenderer { get; private set; }
        public MeshRenderer HatRenderer { get; private set; }
        public IReadOnlyList<Renderer> Renderers => renderers;

        public ChristmasDecor(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, HotelFacade facade, PaletteAtlas atlas, FloorIndex lobby, int seed, CityView city = null)
        {
            this.city = city;
            this.assets = assets;
            this.layout = layout;
            this.building = building;
            this.facade = facade;
            this.atlas = atlas;
            this.lobby = lobby;
            this.seed = seed;
            Root = new GameObject("Christmas").transform;
            Root.SetParent(parent, false);
            Root.gameObject.SetActive(false);
        }

        /// <summary>Texel of bulb group <paramref name="g"/> (tests sample its brightness over time).</summary>
        public int BulbTexel(int g) => bulbs[g];
        public int StarTexel => star;

        public void Tick(float time, bool christmas, PixelGrid grid)
        {
            bool show = Force ?? (christmas || EndingShow);
            if (show && !Built) Build();
            if (Built)
            {
                if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
                if (HatRenderer != null) HatRenderer.enabled = show;
            }
            Shown = show && Built;
            if (!Shown) return;
            ApplyDetail(grid);
            for (int g = 0; g < bulbs.Length; g++) atlas.Set(bulbs[g], BulbColors[g] * WinterSchedule.BulbLevel(g, time));
            atlas.Set(star, StarColor * WinterSchedule.StarLevel(time));
            atlas.Flush();
        }

        void Build()
        {
            Built = true;
            for (int g = 0; g < bulbs.Length; g++) bulbs[g] = atlas.Dynamic(BulbColors[g]);
            star = atlas.Dynamic(StarColor);
            var pal = new PaletteMeshBuilder(atlas);
            BuildTree(pal);
            BuildRoof(pal);
            BuildSidewalk(pal);
            var r = PaletteRenderer.Create(Root, "Christmas Palette", assets.Own(pal.Build("Christmas Palette")), atlas.Material);
            renderers.Add(r);
            BuildHat();
        }

        // ------------------------------------------------------------------ lobby tree + presents

        void BuildTree(PaletteMeshBuilder pal)
        {
            float y0 = lobby.Value * layout.FloorHeight;
            float xMin = building.PropZoneMinX;
            // front-left corner of the lobby, in front of the sofa: the camera sees all of it
            float cx = xMin + 0.9f, cz = 0.05f;
            var lit = new LitBatch(assets);
            var needle = assets.Lit(new Color(0.10f, 0.40f, 0.18f));
            var needleLight = assets.Lit(new Color(0.18f, 0.54f, 0.26f));
            var trunk = assets.Lit(new Color(0.32f, 0.20f, 0.12f));
            var pot = assets.Lit(new Color(0.66f, 0.14f, 0.14f));
            lit.BoxMinMax(new Vector3(cx - 0.32f, y0, cz - 0.32f), new Vector3(cx + 0.32f, y0 + 0.24f, cz + 0.32f), pot);
            lit.BoxMinMax(new Vector3(cx - 0.09f, y0 + 0.24f, cz - 0.09f), new Vector3(cx + 0.09f, y0 + 0.4f, cz + 0.09f), trunk);
            // five tiers, each overlapping the one below, alternating shades so the steps read (top under the ceiling slab)
            float[] w = { 1.70f, 1.36f, 1.02f, 0.68f, 0.38f };
            float[] yb = { 0.30f, 0.66f, 1.00f, 1.32f, 1.60f };
            float[] yt = { 0.80f, 1.14f, 1.45f, 1.72f, 1.95f };
            float top = 0f;
            var bounds = new Bounds(new Vector3(cx, y0 + 1f, cz), Vector3.zero);
            const float o = 0.17f; // ornament size: ~2 px in the gameplay frame
            for (int i = 0; i < w.Length; i++)
            {
                float hw = w[i] * 0.5f, hd = w[i] * 0.36f;
                var min = new Vector3(cx - hw, y0 + yb[i], cz - hd);
                var max = new Vector3(cx + hw, y0 + yt[i], cz + hd);
                lit.BoxMinMax(min, max, (i & 1) == 0 ? needle : needleLight);
                bounds.Encapsulate(min); bounds.Encapsulate(max);
                top = y0 + yt[i];
                // ornaments on the tier's front face (colour groups cycling), a gold tinsel line under them
                int n = Mathf.Max(1, Mathf.RoundToInt(w[i] / 0.34f));
                for (int k = 0; k < n; k++)
                {
                    float u = (k + 0.5f) / n;
                    float ox = cx - hw + 0.1f + u * (w[i] - 0.2f);
                    float oy = y0 + Mathf.Lerp(yb[i] + 0.05f, yt[i] - o - 0.06f, LightSchedule.Hash(seed, i, k, 260));
                    pal.Front(ox - o * 0.5f, oy, ox + o * 0.5f, oy + o, cz - hd - 0.012f, bulbs[(i + k) % bulbs.Length]);
                }
                pal.Front(cx - hw + 0.05f, y0 + yb[i] + 0.02f, cx + hw - 0.05f, y0 + yb[i] + 0.08f, cz - hd - 0.011f, atlas.Static(new Color(0.78f, 0.62f, 0.22f)));
            }
            // star on top: a plus with a bright core
            float sy = top + 0.02f, sz = cz - 0.1f;
            pal.Front(cx - 0.2f, sy + 0.1f, cx + 0.2f, sy + 0.2f, sz, star);
            pal.Front(cx - 0.07f, sy - 0.02f, cx + 0.07f, sy + 0.32f, sz, star);
            bounds.Encapsulate(new Vector3(cx, sy + 0.32f, sz));
            TreeBounds = bounds;

            // presents in front of the tree: boxes with a ribbon cross on the front and a bow on top
            var ribbonGold = atlas.Static(new Color(0.88f, 0.74f, 0.32f));
            var ribbonWhite = atlas.Static(new Color(0.74f, 0.76f, 0.78f));
            void Present(float x, float wdt, float h, float z, Color c, int ribbon)
            {
                var m = assets.Lit(c);
                var min = new Vector3(x - wdt * 0.5f, y0, z - wdt * 0.4f);
                var max = new Vector3(x + wdt * 0.5f, y0 + h, z + wdt * 0.4f);
                lit.BoxMinMax(min, max, m);
                pal.Front(x - 0.045f, y0, x + 0.045f, y0 + h, min.z - 0.01f, ribbon);
                pal.Front(min.x, y0 + h * 0.45f, max.x, y0 + h * 0.45f + 0.09f, min.z - 0.01f, ribbon);
                pal.Front(x - 0.12f, y0 + h, x + 0.12f, y0 + h + 0.1f, min.z + 0.05f, ribbon);
            }
            Present(cx - 0.62f, 0.42f, 0.36f, cz - 0.72f, new Color(0.74f, 0.14f, 0.16f), ribbonGold);
            Present(cx - 0.08f, 0.32f, 0.26f, cz - 0.86f, new Color(0.24f, 0.38f, 0.78f), ribbonWhite);
            Present(cx + 0.46f, 0.46f, 0.3f, cz - 0.7f, new Color(0.86f, 0.70f, 0.28f), atlas.Static(new Color(0.74f, 0.14f, 0.16f)));
            foreach (var r in lit.Build(Root, "Christmas Tree", castShadows: false)) renderers.Add(r);
        }

        // ------------------------------------------------------------------ sidewalk: snowman + a little lit fir by the entrance

        void BuildSidewalk(PaletteMeshBuilder b)
        {
            if (facade == null) return;
            float g = RenderingStyle.GroundY, front = facade.FrontZ;
            int snowF = atlas.Static(WinterView.SnowFront), snowT = atlas.Static(WinterView.SnowTop), snowS = atlas.Static(WinterView.SnowShade);
            void Ball(float cx, float y, float w, float h, float cz)
                => b.Box(new Vector3(cx - w * 0.5f, y, cz - w * 0.4f), new Vector3(cx + w * 0.5f, y + h, cz + w * 0.4f), snowF, snowT, snowS);
            // snowman right of the entrance annex, against the wall line (pedestrian lanes stay clear)
            float sx = facade.AnnexRight + 0.85f, sz = front - 0.32f;
            Ball(sx, g, 0.8f, 0.6f, sz);
            Ball(sx, g + 0.55f, 0.6f, 0.48f, sz);
            Ball(sx, g + 0.98f, 0.44f, 0.4f, sz);
            int coal = atlas.Static(new Color(0.06f, 0.06f, 0.07f));
            float hz = sz - 0.44f * 0.4f - 0.01f, mz = sz - 0.6f * 0.4f - 0.01f;
            b.Front(sx - 0.13f, g + 1.18f, sx - 0.04f, g + 1.27f, hz, coal);
            b.Front(sx + 0.04f, g + 1.18f, sx + 0.13f, g + 1.27f, hz, coal);
            b.Box(new Vector3(sx - 0.03f, g + 1.08f, hz - 0.24f), new Vector3(sx + 0.2f, g + 1.15f, hz), atlas.Static(new Color(0.92f, 0.46f, 0.12f)), atlas.Static(new Color(0.98f, 0.58f, 0.2f)), atlas.Static(new Color(0.7f, 0.32f, 0.08f)));
            for (int i = 0; i < 3; i++) b.Front(sx - 0.04f, g + 0.64f + i * 0.13f, sx + 0.04f, g + 0.72f + i * 0.13f, mz, coal);
            var red = new Color(0.74f, 0.14f, 0.14f);
            b.Box(new Vector3(sx - 0.27f, g + 0.96f, sz - 0.26f), new Vector3(sx + 0.27f, g + 1.06f, sz + 0.22f), red);
            b.Box(new Vector3(sx + 0.1f, g + 0.7f, sz - 0.27f), new Vector3(sx + 0.2f, g + 0.98f, sz - 0.25f), red);
            var hat = new Color(0.08f, 0.08f, 0.09f);
            b.Box(new Vector3(sx - 0.3f, g + 1.38f, sz - 0.25f), new Vector3(sx + 0.3f, g + 1.43f, sz + 0.25f), hat);
            b.Box(new Vector3(sx - 0.19f, g + 1.43f, sz - 0.16f), new Vector3(sx + 0.19f, g + 1.72f, sz + 0.16f), hat);
            b.Box(new Vector3(sx - 0.19f, g + 1.47f, sz - 0.17f), new Vector3(sx + 0.19f, g + 1.53f, sz - 0.16f), red);
            int stick = atlas.Static(new Color(0.34f, 0.22f, 0.12f));
            b.Front(sx - 0.66f, g + 0.82f, sx - 0.28f, g + 0.88f, mz, stick);
            b.Front(sx - 0.7f, g + 0.88f, sx - 0.62f, g + 1.02f, mz, stick);
            b.Front(sx + 0.28f, g + 0.78f, sx + 0.64f, g + 0.84f, mz, stick);
            SidewalkSnowman = new Vector3(sx, g + 0.9f, sz);

            // a little fir in a tub left of the annex door, with bulbs
            float fx = facade.AnnexLeft + 0.25f, fz = front - 0.4f;
            b.Box(new Vector3(fx - 0.28f, g, fz - 0.22f), new Vector3(fx + 0.28f, g + 0.3f, fz + 0.22f), new Color(0.40f, 0.26f, 0.16f));
            float[] fw = { 0.9f, 0.68f, 0.44f, 0.22f };
            for (int i = 0; i < fw.Length; i++)
            {
                float y = g + 0.28f + i * 0.3f;
                b.Box(new Vector3(fx - fw[i] * 0.5f, y, fz - fw[i] * 0.3f), new Vector3(fx + fw[i] * 0.5f, y + 0.36f, fz + fw[i] * 0.3f), (i & 1) == 0 ? new Color(0.10f, 0.32f, 0.16f) : new Color(0.15f, 0.42f, 0.22f));
                for (int k = 0; k < 3 - i / 2; k++)
                {
                    float bx = fx - fw[i] * 0.4f + (k + 0.5f) * fw[i] * 0.8f / (3 - i / 2);
                    b.Front(bx - 0.08f, y + 0.12f, bx + 0.08f, y + 0.28f, fz - fw[i] * 0.3f - 0.01f, bulbs[(i + k) % bulbs.Length]);
                }
            }
            b.Front(fx - 0.1f, g + 1.48f, fx + 0.1f, g + 1.68f, fz - 0.1f, star);
        }

        // ------------------------------------------------------------------ roof: snowman + Santa's sleigh

        void BuildRoof(PaletteMeshBuilder b)
        {
            if (facade == null) return;
            float roof = facade.RoofY;
            var snow = WinterView.SnowFront;
            void Ball(float cx, float y, float w, float h, float cz)
                => b.Box(new Vector3(cx - w * 0.5f, y, cz - w * 0.4f), new Vector3(cx + w * 0.5f, y + h, cz + w * 0.4f),
                    atlas.Static(snow), atlas.Static(WinterView.SnowTop), atlas.Static(WinterView.SnowShade));

            // snowman on the left low step, beside the sign
            float sx = facade.StepInX - 0.55f, sz = -1.2f;
            Ball(sx, roof, 0.64f, 0.5f, sz);
            Ball(sx, roof + 0.46f, 0.48f, 0.4f, sz);
            Ball(sx, roof + 0.82f, 0.36f, 0.32f, sz);
            int coal = atlas.Static(new Color(0.06f, 0.06f, 0.07f));
            float hz = sz - 0.36f * 0.4f - 0.005f;
            b.Front(sx - 0.1f, roof + 0.98f, sx - 0.04f, roof + 1.05f, hz, coal);
            b.Front(sx + 0.04f, roof + 0.98f, sx + 0.1f, roof + 1.05f, hz, coal);
            b.Box(new Vector3(sx - 0.02f, roof + 0.9f, hz - 0.2f), new Vector3(sx + 0.16f, roof + 0.95f, hz), atlas.Static(new Color(0.92f, 0.46f, 0.12f)), atlas.Static(new Color(1f, 0.6f, 0.2f)), atlas.Static(new Color(0.7f, 0.32f, 0.08f)));
            float mz = sz - 0.48f * 0.4f - 0.005f;
            for (int i = 0; i < 2; i++) b.Front(sx - 0.03f, roof + 0.58f + i * 0.13f, sx + 0.03f, roof + 0.64f + i * 0.13f, mz, coal);
            // scarf + hat
            b.Box(new Vector3(sx - 0.22f, roof + 0.8f, sz - 0.2f), new Vector3(sx + 0.22f, roof + 0.88f, sz + 0.18f), new Color(0.72f, 0.14f, 0.14f));
            b.Box(new Vector3(sx + 0.1f, roof + 0.6f, sz - 0.21f), new Vector3(sx + 0.18f, roof + 0.82f, sz - 0.19f), new Color(0.72f, 0.14f, 0.14f));
            b.Box(new Vector3(sx - 0.24f, roof + 1.13f, sz - 0.2f), new Vector3(sx + 0.24f, roof + 1.17f, sz + 0.2f), new Color(0.08f, 0.08f, 0.09f));
            b.Box(new Vector3(sx - 0.15f, roof + 1.17f, sz - 0.13f), new Vector3(sx + 0.15f, roof + 1.42f, sz + 0.13f), new Color(0.08f, 0.08f, 0.09f));
            // stick arms
            int stick = atlas.Static(new Color(0.34f, 0.22f, 0.12f));
            b.Front(sx - 0.52f, roof + 0.7f, sx - 0.22f, roof + 0.74f, mz, stick);
            b.Front(sx - 0.56f, roof + 0.74f, sx - 0.5f, roof + 0.86f, mz, stick);
            b.Front(sx + 0.22f, roof + 0.66f, sx + 0.5f, roof + 0.7f, mz, stick);
            SnowmanCentre = new Vector3(sx, roof + 0.7f, sz);

            // Santa's sleigh parked on the stair hut roof (facing left, toward the sign)
            float top = facade.HutTop;
            float x0 = facade.HutLeft + 0.06f, x1 = x0 + 1.22f, z0 = 0.62f, z1 = 1.12f;
            var red = new Color(0.72f, 0.12f, 0.12f);
            var redDark = new Color(0.46f, 0.07f, 0.07f);
            var gold = new Color(0.88f, 0.70f, 0.26f);
            // runners + struts
            b.Box(new Vector3(x0 - 0.12f, top, z0), new Vector3(x1 + 0.02f, top + 0.05f, z1), gold);
            b.Box(new Vector3(x0 - 0.18f, top + 0.05f, z0), new Vector3(x0 - 0.1f, top + 0.2f, z1), gold);
            for (int i = 0; i < 2; i++)
            {
                float px = Mathf.Lerp(x0 + 0.15f, x1 - 0.2f, i);
                b.Box(new Vector3(px, top + 0.05f, z0), new Vector3(px + 0.05f, top + 0.14f, z1), gold);
            }
            // body: low front (left), high seat back (right), gold trim line, curl at the front
            b.Box(new Vector3(x0, top + 0.14f, z0), new Vector3(x1, top + 0.5f, z1), red);
            b.Box(new Vector3(x1 - 0.3f, top + 0.5f, z0), new Vector3(x1, top + 0.78f, z1), redDark);
            b.Front(x0, top + 0.42f, x1, top + 0.46f, z0 - 0.005f, atlas.Static(gold));
            b.Box(new Vector3(x0 - 0.12f, top + 0.3f, z0 + 0.05f), new Vector3(x0 + 0.02f, top + 0.62f, z1 - 0.05f), red);
            b.Box(new Vector3(x0 - 0.16f, top + 0.62f, z0 + 0.05f), new Vector3(x0 + 0.06f, top + 0.7f, z1 - 0.05f), gold);
            // the sack of presents on the back seat, two presents peeking out
            b.Box(new Vector3(x1 - 0.62f, top + 0.5f, z0 + 0.12f), new Vector3(x1 - 0.28f, top + 0.92f, z1 - 0.02f), new Color(0.46f, 0.30f, 0.18f));
            b.Box(new Vector3(x1 - 0.58f, top + 0.92f, z0 + 0.2f), new Vector3(x1 - 0.44f, top + 1.04f, z0 + 0.36f), new Color(0.24f, 0.56f, 0.30f));
            b.Box(new Vector3(x1 - 0.42f, top + 0.92f, z0 + 0.24f), new Vector3(x1 - 0.3f, top + 1.0f, z0 + 0.38f), new Color(0.82f, 0.24f, 0.30f));
            SleighCentre = new Vector3((x0 + x1) * 0.5f, top + 0.4f, (z0 + z1) * 0.5f);
        }

        // ------------------------------------------------------------------ Santa hat on the HOTEL sign

        void BuildHat()
        {
            if (building?.HotelSignRoot == null) return;
            var b = new PaletteMeshBuilder(atlas);
            float t = building.SignTexelBase, p = t * 0.5f;
            var plate = building.HotelPlateSize;
            float hw = plate.x * 0.5f + 2f * t, top = plate.y + 2f * t;
            int red = atlas.Static(new Color(0.80f, 0.12f, 0.12f)), redDark = atlas.Static(new Color(0.52f, 0.07f, 0.08f));
            int white = atlas.Static(new Color(0.68f, 0.68f, 0.68f));
            float z = -0.09f;
            float left = -hw - p;
            // the gameplay frame leaves ~3 px of sky above the sign: a low hat pulled over the frame's top-left corner,
            // its tip flopping down the left side (in the sky beside the sign) with the pompom
            int[] x0s = { 1, 3, 5 }, x1s = { 13, 10, 8 };
            for (int r = 0; r < x0s.Length; r++)
            {
                float y = top + r * p;
                b.Front(left + x0s[r] * p, y, left + x1s[r] * p, y + p, z, red);
                b.Front(left + (x1s[r] - 1) * p, y, left + x1s[r] * p, y + p, z - 0.001f, redDark);
            }
            b.Front(left - p, top + 2f * p, left + 5f * p, top + 3f * p, z, red);
            b.Front(left - 2f * p, top, left, top + 2f * p, z, red);
            b.Front(left - 4f * p, top - 3f * p, left - p, top, z - 0.002f, white);
            // fluffy brim over the top-left of the frame (covers the corner bulbs, never the H)
            b.Front(left, top - 3f * p, left + 14f * p, top, z - 0.002f, white);
            HatRenderer = PaletteRenderer.Create(building.HotelSignRoot, "Santa Hat", assets.Own(b.Build("Santa Hat")), atlas.Material);
            HatRenderer.enabled = false;
        }

        // ------------------------------------------------------------------ pixel details: wreaths, garland, bulb strings

        void ApplyDetail(PixelGrid grid)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(grid.PixelsPerUnit));
            if (DetailRenderer != null && key == detailKey) return;
            if (!detailMeshes.TryGetValue(key, out var mesh))
            {
                mesh = assets.Own(BuildDetail(grid, key));
                detailMeshes[key] = mesh;
            }
            if (DetailRenderer == null)
            {
                DetailRenderer = PaletteRenderer.Create(Root, "Christmas Details", mesh, atlas.Material);
                detailFilter = DetailRenderer.GetComponent<MeshFilter>();
                renderers.Add(DetailRenderer);
            }
            detailFilter.sharedMesh = mesh;
            detailKey = key;
        }

        static readonly string[] Wreath =
        {
            "...GGGGG...",
            "..GgGbGgG..",
            ".GgG...GgG.",
            "GgG.....GgG",
            "Gb.......bG",
            "Gg.......gG",
            "GgG.....GgG",
            ".GgG...GgG.",
            "..GgGrGgG..",
            "...rrRrr...",
            "..rr.R.rr..",
            "....r.r....",
        };

        static readonly string[] Bow =
        {
            "rr.rr",
            "rrRrr",
            ".r.r.",
        };

        Dictionary<char, int> WreathKeys() => new Dictionary<char, int>
        {
            ['G'] = atlas.Static(new Color(0.10f, 0.36f, 0.16f)),
            ['g'] = atlas.Static(new Color(0.20f, 0.54f, 0.26f)),
            ['b'] = bulbs[1],
            ['r'] = atlas.Static(new Color(0.84f, 0.14f, 0.14f)),
            ['R'] = atlas.Static(new Color(0.56f, 0.08f, 0.08f)),
        };

        Mesh BuildDetail(PixelGrid grid, int key)
        {
            var b = new PaletteMeshBuilder(atlas);
            var p = new PixelPainter(b, key, grid.Shear, grid.ShearDepth);
            int bulbN = 0, wreaths = 0;
            var wreathKeys = WreathKeys();
            void Bulb(int x, int y, float z, int i) { p.Rect(x, y, 2, 2, z, bulbs[((i % bulbs.Length) + bulbs.Length) % bulbs.Length]); bulbN++; }

            // a wreath beside the elevator doors on every floor (right shaft post, above the queue's heads)
            float d = layout.Depth;
            for (int f = 0; f < layout.FloorCount; f++)
            {
                var at = p.At(new Vector3(layout.ShaftX + building.ShaftHalfWidth + 0.5f, f * layout.FloorHeight + 2.1f, -d * 0.5f - 0.04f));
                p.Sprite(at.x - 5, at.y - 6, Wreath, -d * 0.5f - 0.04f, wreathKeys);
                wreaths++;
            }
            if (facade != null)
            {
                int leaf = atlas.Static(new Color(0.12f, 0.40f, 0.18f)), leafLight = atlas.Static(new Color(0.22f, 0.56f, 0.28f));
                int wire = atlas.Static(new Color(0.10f, 0.20f, 0.12f));
                // ... and on both pilasters of every floor (outside), under the sconces
                float pw = HotelFacade.PilasterWidth;
                float rightIn = layout.ShaftX + layout.FloorWidth * 0.5f + HotelFacade.CutawayClearance;
                float pz = facade.PilasterZ - 0.07f;
                for (int f = 0; f < layout.FloorCount; f++)
                    foreach (float wx in new[] { facade.PilasterLeft + pw * 0.5f, rightIn + pw * 0.5f })
                    {
                        var at = p.At(new Vector3(wx, f * layout.FloorHeight + 1.05f, pz));
                        p.Sprite(at.x - 5, at.y - 6, Wreath, pz, wreathKeys);
                        wreaths++;
                    }
                // garland swags under the cornice: 3 px thick, dipping between red bows, a 2x2 bulb every 4 px
                float gz = facade.FrontZ - 0.59f;
                var a = p.At(new Vector3(facade.CorniceLeft - 0.1f, facade.TopY + 0.28f, gz));
                var c = p.At(new Vector3(facade.CorniceRight + 0.1f, facade.TopY + 0.28f, gz));
                const int swag = 18;
                for (int x = a.x; x < c.x; x++)
                {
                    float u = ((x - a.x) % swag) / (float)swag;
                    int dip = Mathf.RoundToInt(Mathf.Sin(u * Mathf.PI) * 4f);
                    int y = a.y - dip;
                    p.Rect(x, y - 2, 1, 3, gz, ((x >> 1) & 1) == 0 ? leaf : leafLight);
                    if ((x - a.x) % 4 == 2) Bulb(x, y - 4, gz - 0.002f, (x - a.x) / 4);
                    if ((x - a.x) % swag == 0) p.Sprite(x - 2, y - 1, Bow, gz - 0.003f, wreathKeys);
                }
                // bulb strings down the outer edges of both pilasters
                foreach (float ex in new[] { facade.PilasterLeft - 0.02f, facade.PilasterRight + 0.02f })
                {
                    var t0 = p.At(new Vector3(ex, facade.TopY - 0.5f, pz));
                    var b0 = p.At(new Vector3(ex, 0.4f, pz));
                    bool leftSide = ex < layout.ShaftX;
                    p.Rect(t0.x, b0.y, 1, t0.y - b0.y, pz + 0.001f, wire);
                    for (int y = t0.y - 2, i = 0; y > b0.y; y -= 5, i++) Bulb(leftSide ? t0.x - 2 : t0.x + 1, y, pz - 0.002f, i);
                }
                // along the canopy's front edge and the annex cornice
                float cz = facade.CanopyFrontZ - 0.08f;
                var ca = p.At(new Vector3(facade.CanopyLeft, facade.CanopyTop - 0.5f, cz));
                var cb = p.At(new Vector3(facade.CanopyRight, facade.CanopyTop - 0.5f, cz));
                for (int x = ca.x + 1, i = 0; x < cb.x - 1; x += 4, i++) Bulb(x, ca.y - 2 - (i & 1), cz, i + 2);
                float az = facade.AnnexZ - 0.18f;
                var aa = p.At(new Vector3(facade.AnnexLeft, facade.AnnexTop - 0.12f, az));
                var ab = p.At(new Vector3(facade.AnnexRight, facade.AnnexTop - 0.12f, az));
                for (int x = aa.x + 1, i = 0; x < ab.x - 1; x += 4, i++) Bulb(x, aa.y, az, i + 1);
            }
            WreathCount = wreaths;
            BuildStreet(p, key, grid, Bulb);
            BulbCount = bulbN;
            return b.Build("Christmas Details " + key + "ppu");
        }

        /// <summary>The neighbours: roof-edge bulb swags, bulbs over sign boards, holly over shop windows, candles, MERRY XMAS.</summary>
        void BuildStreet(PixelPainter p, int key, PixelGrid grid, System.Action<int, int, float, int> bulb)
        {
            var sh = grid.Shear;
            float z0 = grid.ShearDepth, spx = 1f / key;
            Vector2Int Screen(float sx, float sy, float z) => p.At(new Vector3(PixelGrid.WorldX(sx, z, sh, z0), PixelGrid.WorldY(sy, z, sh, z0), z));
            int wire = atlas.Static(new Color(0.10f, 0.12f, 0.12f));
            float g = RenderingStyle.GroundY;

            // roof edges of the near row: swags of bulbs between little posts
            foreach (var blk in CityView.Blocks)
            {
                if (blk.Z > 6f) continue;
                float top = Mathf.Round(g + blk.Height);
                float z = blk.Z - 0.08f;
                var a = Screen(blk.X0 + 0.15f, top + 0.05f, z);
                var c = Screen(blk.X1 - 0.15f, top + 0.05f, z);
                int span = Mathf.Max(12, (c.x - a.x) / Mathf.Max(1, Mathf.RoundToInt((c.x - a.x) / 26f)));
                for (int x = a.x, i = 0; x <= c.x; x++)
                {
                    float u = ((x - a.x) % span) / (float)span;
                    int y = a.y - Mathf.RoundToInt(Mathf.Sin(u * Mathf.PI) * 4f);
                    p.Rect(x, y, 1, 1, z + 0.001f, wire);
                    if ((x - a.x) % 5 == 2) bulb(x, y - 2, z, i++ + blk.X0);
                }
            }

            var d = city?.Dressing;
            if (d != null)
            {
                // a short string of bulbs along the top of every sign board (except the MERRY XMAS one, which glows itself)
                foreach (var sb in d.SignBoards)
                {
                    if (sb.Text == "FORTUNE") continue;
                    var a = Screen(sb.Screen.xMin, sb.Screen.yMax, sb.Z - 0.03f);
                    var c = Screen(sb.Screen.xMax, sb.Screen.yMax, sb.Z - 0.03f);
                    for (int x = a.x, i = 0; x < c.x - 1; x += 4, i++) bulb(x, a.y, sb.Z - 0.03f, i + sb.Text.Length);
                }
                // holly over every shop window: a 2-px green band with red berries, a red bow in the middle
                int leaf = atlas.Static(new Color(0.12f, 0.42f, 0.18f)), leafLight = atlas.Static(new Color(0.22f, 0.58f, 0.28f));
                int berry = atlas.Static(new Color(0.86f, 0.14f, 0.14f));
                var bowKeys = WreathKeys();
                int holly = 0;
                foreach (var shop in d.ShopFronts)
                {
                    float z = shop.Z - 0.012f;
                    var a = Screen(shop.X0 + 0.4f, shop.WindowY1 + 0.1f, z);
                    var c = Screen(shop.X1 - 0.4f, shop.WindowY1 + 0.1f, z);
                    for (int x = a.x; x < c.x; x++)
                    {
                        int sag = ((x - a.x) % 10) is int m && m > 2 && m < 8 ? 1 : 0;
                        p.Rect(x, a.y - 1 - sag, 1, 2, z, ((x >> 1) & 1) == 0 ? leaf : leafLight);
                        if ((x - a.x) % 5 == 3) p.Rect(x, a.y - 2 - sag, 1, 1, z - 0.001f, berry);
                    }
                    p.Sprite((a.x + c.x) / 2 - 2, a.y - 2, Bow, z - 0.002f, bowKeys);
                    holly++;
                }
                HollyCount = holly;
                // FORTUNE -> MERRY XMAS on Christmas Eve: a new board over the old one, red and green letters
                foreach (var sb in d.SignBoards)
                {
                    if (sb.Text != "FORTUNE") continue;
                    MerryXmas(p, Screen((sb.Screen.xMin + sb.Screen.xMax) * 0.5f, sb.Screen.yMin, sb.Z - 0.03f), sb.Z - 0.03f);
                    XmasSign = sb.Screen;
                }
            }

            // candles in some of the neighbours' windows (lit or not): a cream stick and a warm flame
            int wax = atlas.Static(new Color(0.72f, 0.70f, 0.64f)), flame = atlas.Static(new Color(0.80f, 0.62f, 0.28f));
            int candles = 0, n = 0;
            foreach (var w in CityView.NearWindows())
            {
                if (LightSchedule.Hash(seed, w.X * 31 + w.Y, n++, 270) > 0.3f) continue;
                float z = w.Z - 0.005f;
                var at = Screen(w.X + w.W * 0.5f, w.Y, z);
                p.Rect(at.x, at.y + 1, 1, 2, z, wax);
                p.Rect(at.x, at.y + 3, 1, 1, z, flame);
                candles++;
            }
            CandleCount = candles;
        }

        int xmasRed = -1, xmasGreen = -1;

        void MerryXmas(PixelPainter p, Vector2Int bottomCentre, float z)
        {
            const string text = "MERRY XMAS";
            if (xmasRed < 0) { xmasRed = atlas.Static(XmasRed); xmasGreen = atlas.Static(XmasGreen); }
            int tw = text.Length * (PixelFont.GlyphWidth + PixelFont.Spacing) - PixelFont.Spacing;
            int w = tw + 6, h = PixelFont.GlyphHeight + 6;
            int x0 = bottomCentre.x - w / 2, y0 = bottomCentre.y;
            p.Rect(x0, y0, w, h, z, atlas.Static(new Color(0.42f, 0.10f, 0.10f)));
            p.Rect(x0 + 1, y0 + 1, w - 2, h - 2, z - 0.002f, atlas.Static(new Color(0.05f, 0.05f, 0.06f)));
            int col = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    var mask = PixelFont.Rasterize(text[i].ToString(), 0, out var size);
                    int tex = (col++ & 1) == 0 ? xmasRed : xmasGreen;
                    for (int row = 0; row < size.y; row++)
                        for (int cx = 0; cx < size.x; cx++)
                            if (mask[row * size.x + cx]) p.Rect(x0 + 3 + i * (PixelFont.GlyphWidth + PixelFont.Spacing) + cx, y0 + 3 + row, 1, 1, z - 0.004f, tex);
                }
            }
        }
    }
}
