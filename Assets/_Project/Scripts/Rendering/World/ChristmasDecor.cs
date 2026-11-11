using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Christmas Eve dressing (ISeasonQuery.IsChristmas, day 10), built on first use, shown only that night (and in the
    /// winter endings):
    ///   Lobby     a decorated tree (lit tiers, ornaments, a glowing star) with presents under it, in the lobby's back corner.
    ///   Floors    a wreath with a red bow beside the elevator doors on every floor.
    ///   Facade    a garland swagged under the cornice and strings of bulbs down both pilasters and along the canopy.
    ///   Roof      a snowman by the HOTEL sign, Santa's sleigh (with its sack of presents) parked on the stair hut - Santa's
    ///             destination is the rooftop - and a Santa hat on the HOTEL sign's H.
    /// Bulbs and ornaments share <see cref="WinterSchedule.BulbGroups"/> palette texels that fade SLOWLY in and out of
    /// phase (<see cref="WinterSchedule.BulbLevel"/>: a 3.6 s raised cosine, never on/off) - photosensitivity-safe.
    /// Cost while shown: tree/presents (lit, ~5 draws), one palette mesh for the roof + ornaments, one pixel-detail mesh per
    /// frame zoom for wreaths and bulbs, the sign hat.
    /// </summary>
    sealed class ChristmasDecor
    {
        static readonly Color[] BulbColors =
        {
            new Color(1.00f, 0.26f, 0.20f),
            new Color(1.00f, 0.80f, 0.32f),
            new Color(0.36f, 0.95f, 0.45f),
            new Color(0.42f, 0.62f, 1.00f),
        };
        static readonly Color StarColor = new Color(1.00f, 0.88f, 0.40f);

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly HotelFacade facade;
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
        public MeshRenderer DetailRenderer { get; private set; }
        public MeshRenderer HatRenderer { get; private set; }
        public IReadOnlyList<Renderer> Renderers => renderers;

        public ChristmasDecor(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, HotelFacade facade, PaletteAtlas atlas, FloorIndex lobby, int seed)
        {
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
            var r = PaletteRenderer.Create(Root, "Christmas Palette", assets.Own(pal.Build("Christmas Palette")), atlas.Material);
            renderers.Add(r);
            BuildHat();
        }

        // ------------------------------------------------------------------ lobby tree + presents

        void BuildTree(PaletteMeshBuilder pal)
        {
            float y0 = lobby.Value * layout.FloorHeight;
            float xMin = building.PropZoneMinX;
            float cx = xMin + 0.68f, cz = 1.2f;
            var lit = new LitBatch(assets);
            var needle = assets.Lit(new Color(0.10f, 0.36f, 0.18f));
            var needleLight = assets.Lit(new Color(0.16f, 0.48f, 0.24f));
            var trunk = assets.Lit(new Color(0.32f, 0.20f, 0.12f));
            var pot = assets.Lit(new Color(0.62f, 0.14f, 0.14f));
            lit.BoxMinMax(new Vector3(cx - 0.28f, y0, cz - 0.28f), new Vector3(cx + 0.28f, y0 + 0.24f, cz + 0.28f), pot);
            lit.BoxMinMax(new Vector3(cx - 0.08f, y0 + 0.24f, cz - 0.08f), new Vector3(cx + 0.08f, y0 + 0.42f, cz + 0.08f), trunk);
            // four tiers, each overlapping the one below, alternating shades so the steps read
            float[] w = { 1.30f, 1.02f, 0.74f, 0.44f };
            float[] yb = { 0.36f, 0.78f, 1.15f, 1.48f };
            float[] yt = { 0.90f, 1.26f, 1.58f, 1.86f };
            float top = 0f;
            var bounds = new Bounds(new Vector3(cx, y0 + 1f, cz), Vector3.zero);
            for (int i = 0; i < w.Length; i++)
            {
                float hw = w[i] * 0.5f, hd = w[i] * 0.38f;
                var min = new Vector3(cx - hw, y0 + yb[i], cz - hd);
                var max = new Vector3(cx + hw, y0 + yt[i], cz + hd);
                lit.BoxMinMax(min, max, (i & 1) == 0 ? needle : needleLight);
                bounds.Encapsulate(min); bounds.Encapsulate(max);
                top = y0 + yt[i];
                // ornaments on the tier's front face, one per ~0.28 units, colour groups cycling
                int n = Mathf.Max(2, Mathf.RoundToInt(w[i] / 0.28f));
                for (int k = 0; k < n; k++)
                {
                    float u = (k + 0.5f) / n;
                    float ox = cx - hw + 0.08f + u * (w[i] - 0.16f);
                    float oy = y0 + Mathf.Lerp(yb[i] + 0.06f, yt[i] - 0.12f, LightSchedule.Hash(seed, i, k, 260));
                    pal.Front(ox - 0.055f, oy, ox + 0.055f, oy + 0.11f, cz - hd - 0.012f, bulbs[(i + k) % bulbs.Length]);
                }
            }
            // star on top: a plus with a bright core
            float sy = top + 0.02f, sz = cz - 0.05f;
            pal.Front(cx - 0.17f, sy + 0.08f, cx + 0.17f, sy + 0.18f, sz, star);
            pal.Front(cx - 0.06f, sy - 0.02f, cx + 0.06f, sy + 0.30f, sz, star);
            bounds.Encapsulate(new Vector3(cx, sy + 0.3f, sz));
            TreeBounds = bounds;

            // presents in front of the tree: boxes with a ribbon cross on the front and a bow on top
            var ribbonGold = atlas.Static(new Color(0.95f, 0.80f, 0.36f));
            var ribbonWhite = atlas.Static(new Color(0.66f, 0.68f, 0.70f));
            void Present(float x, float wdt, float h, float z, Color c, int ribbon)
            {
                var m = assets.Lit(c);
                var min = new Vector3(x - wdt * 0.5f, y0, z - wdt * 0.4f);
                var max = new Vector3(x + wdt * 0.5f, y0 + h, z + wdt * 0.4f);
                lit.BoxMinMax(min, max, m);
                pal.Front(x - 0.03f, y0, x + 0.03f, y0 + h, min.z - 0.01f, ribbon);
                pal.Front(min.x, y0 + h * 0.45f, max.x, y0 + h * 0.45f + 0.06f, min.z - 0.01f, ribbon);
                pal.Front(x - 0.09f, y0 + h, x + 0.09f, y0 + h + 0.07f, min.z + 0.05f, ribbon);
            }
            Present(cx - 0.42f, 0.36f, 0.30f, cz - 0.62f, new Color(0.70f, 0.14f, 0.16f), ribbonGold);
            Present(cx + 0.02f, 0.28f, 0.22f, cz - 0.78f, new Color(0.24f, 0.36f, 0.72f), ribbonWhite);
            Present(cx + 0.42f, 0.40f, 0.26f, cz - 0.58f, new Color(0.86f, 0.70f, 0.28f), atlas.Static(new Color(0.70f, 0.14f, 0.16f)));
            foreach (var r in lit.Build(Root, "Christmas Tree", castShadows: false)) renderers.Add(r);
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
            "..GGG..",
            ".GgbgG.",
            "Gg...gG",
            "Gb...bG",
            "Gg...gG",
            ".GgrgG.",
            "..rRr..",
            ".r.R.r.",
        };

        Mesh BuildDetail(PixelGrid grid, int key)
        {
            var b = new PaletteMeshBuilder(atlas);
            var p = new PixelPainter(b, key, grid.Shear, grid.ShearDepth);
            int bulbN = 0, wreaths = 0;
            var wreathKeys = new Dictionary<char, int>
            {
                ['G'] = atlas.Static(new Color(0.10f, 0.34f, 0.16f)),
                ['g'] = atlas.Static(new Color(0.18f, 0.50f, 0.24f)),
                ['b'] = bulbs[1],
                ['r'] = atlas.Static(new Color(0.82f, 0.14f, 0.14f)),
                ['R'] = atlas.Static(new Color(0.55f, 0.08f, 0.08f)),
            };
            // a wreath beside the elevator doors on every floor (right shaft post, above the queue's heads)
            float d = layout.Depth;
            for (int f = 0; f < layout.FloorCount; f++)
            {
                var at = p.At(new Vector3(layout.ShaftX + building.ShaftHalfWidth + 0.42f, f * layout.FloorHeight + 2.02f, -d * 0.5f - 0.04f));
                p.Sprite(at.x - 3, at.y - 4, Wreath, -d * 0.5f - 0.04f, wreathKeys);
                wreaths++;
            }
            WreathCount = wreaths;
            if (facade != null)
            {
                int leaf = atlas.Static(new Color(0.12f, 0.38f, 0.18f)), leafLight = atlas.Static(new Color(0.20f, 0.52f, 0.26f));
                int wire = atlas.Static(new Color(0.10f, 0.20f, 0.12f));
                // garland swags under the cornice: 2-px thick, dipping between nails, a bulb every 3 px
                float gz = facade.FrontZ - 0.59f;
                var a = p.At(new Vector3(facade.CorniceLeft - 0.1f, facade.TopY + 0.28f, gz));
                var c = p.At(new Vector3(facade.CorniceRight + 0.1f, facade.TopY + 0.28f, gz));
                const int swag = 16;
                for (int x = a.x; x < c.x; x++)
                {
                    float u = ((x - a.x) % swag) / (float)swag;
                    int dip = Mathf.RoundToInt(Mathf.Sin(u * Mathf.PI) * 3f);
                    int y = a.y - dip;
                    p.Rect(x, y - 1, 1, 2, gz, ((x >> 1) & 1) == 0 ? leaf : leafLight);
                    if ((x - a.x) % 3 == 1) { p.Rect(x, y - 2, 1, 1, gz - 0.002f, bulbs[((x - a.x) / 3) % bulbs.Length]); bulbN++; }
                    if ((x - a.x) % swag == 0) p.Rect(x, y, 1, 2, gz - 0.003f, atlas.Static(new Color(0.75f, 0.12f, 0.12f)));
                }
                // bulb strings down the outer edges of both pilasters
                float pz = facade.PilasterZ - 0.06f;
                foreach (float ex in new[] { facade.PilasterLeft - 0.02f, facade.PilasterRight + 0.02f })
                {
                    var t0 = p.At(new Vector3(ex, facade.TopY - 0.55f, pz));
                    var b0 = p.At(new Vector3(ex, 0.55f, pz));
                    p.Rect(t0.x, b0.y, 1, t0.y - b0.y, pz + 0.001f, wire);
                    for (int y = t0.y - 1, i = 0; y > b0.y; y -= 4, i++)
                    {
                        p.Rect(t0.x + (ex < layout.ShaftX ? -1 : 1), y, 1, 1, pz - 0.002f, bulbs[i % bulbs.Length]);
                        bulbN++;
                    }
                }
                // along the canopy's front edge
                float cz = facade.CanopyFrontZ - 0.08f;
                var ca = p.At(new Vector3(facade.CanopyLeft, facade.CanopyTop - 0.5f, cz));
                var cb = p.At(new Vector3(facade.CanopyRight, facade.CanopyTop - 0.5f, cz));
                for (int x = ca.x + 1, i = 0; x < cb.x; x += 3, i++)
                {
                    p.Rect(x, ca.y - (i & 1), 1, 1, cz, bulbs[(i + 2) % bulbs.Length]);
                    bulbN++;
                }
            }
            BulbCount = bulbN;
            return b.Build("Christmas Details " + key + "ppu");
        }
    }
}
