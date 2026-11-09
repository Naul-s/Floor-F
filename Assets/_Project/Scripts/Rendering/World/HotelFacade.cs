using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Exterior of the hotel. Lit masonry (merged per material, screen-plane pixel patterns - see
    /// <see cref="RenderAssets.PatternLit"/>): rusticated stone plinth, brick pilasters with stone quoins, floor bands with a
    /// shadow line, capitals, a dentil frieze under a projecting cornice, a stepped brick parapet with coping and corner piers,
    /// a paved roof with a water tower, chimney, AC units, vents, laundry poles, an antenna mast with a beacon, a brick
    /// stair hut and planters; the entrance annex (glass door, lamps, striped canopy) with an AC unit; AC boxes on the side.
    /// Self-lit parts (lamps, grilles, door glass) = one palette mesh. Pixel-exact details = one palette mesh PER FRAME ZOOM
    /// (like the city): vertical HOTEL blade sign, pigeons, satellite dishes, laundry, ladder and bracing, a drainpipe,
    /// wall sconces, marquee bulbs over the annex. Nothing covers the floors' cut-away (screen x in [-w/2, w/2],
    /// y in [0, roof slab]): every addition grows outward from the pilasters or sits above the roof slab.
    /// </summary>
    sealed class HotelFacade
    {
        static readonly Color Stone = new Color(0.34f, 0.31f, 0.30f);
        static readonly Color StoneDark = new Color(0.22f, 0.20f, 0.20f);
        static readonly Color Trim = new Color(0.56f, 0.48f, 0.38f);
        static readonly Color Pilaster = new Color(0.60f, 0.28f, 0.21f);
        static readonly Color Metal = new Color(0.22f, 0.22f, 0.25f);
        static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
        static readonly Color AcGrey = new Color(0.46f, 0.47f, 0.49f);
        static readonly Color Awning = new Color(0.62f, 0.13f, 0.12f);
        static readonly Color AwningLight = new Color(0.86f, 0.80f, 0.68f);
        static readonly Color RoofDeck = new Color(0.26f, 0.25f, 0.27f);

        /// <summary>Gap kept between dressing and the cut-away (pilaster inner faces sit at |x| = w/2 + this).</summary>
        public const float CutawayClearance = 0.1f;
        public const float PilasterWidth = 0.9f;
        /// <summary>Annex starts where the old (narrower) pilaster ended: door / pedestrians unchanged.</summary>
        const float AnnexOffset = 0.62f;

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly PaletteAtlas atlas;
        readonly LivingLights lights;
        readonly TexelLedger ledger = new TexelLedger();
        readonly Dictionary<int, Mesh> detailMeshes = new Dictionary<int, Mesh>();
        MeshFilter detailFilter;

        public Transform Root { get; }
        public readonly List<Renderer> Renderers = new List<Renderer>();
        /// <summary>Screen-plane x of the entrance door centre (pedestrians may turn in here).</summary>
        public float DoorX { get; private set; }
        public int BulbCount { get; private set; }
        /// <summary>Pixel-detail renderer (blade sign, pigeons, dishes...): one mesh per frame zoom.</summary>
        public MeshRenderer DetailRenderer { get; private set; }
        public int DetailPixelsPerUnit { get; private set; }
        public int DetailVariants => detailMeshes.Count;
        /// <summary>Lit boxes merged into the facade (tests / stats).</summary>
        public int LitParts { get; private set; }
        public int PigeonCount { get; private set; }
        /// <summary>Blade sign rectangle (target pixels, screen plane) of the detail mesh built last.</summary>
        public RectInt BladeSignPixels { get; private set; }

        // ---- geometry shared by the lit build and the per-zoom pixel details
        float w, d, sx, H, left, right, g, front, pz, roof;
        float pilL0, pilR1, cx0, cx1, stepIn, annex0, annex1, annexTop, annexZ;
        float dishX, lineX0, lineX1, towerX0, towerX1, hutX0, hutX1;
        const float LineZ = 1.6f, LineH = 0.95f, TowerZ = 0.5f, LegH = 0.6f, BarrelH = 1.15f, HutZ = 0.4f, HutH = 1.3f;
        readonly List<Vector3> pigeonSpots = new List<Vector3>();

        public HotelFacade(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, PaletteAtlas atlas, LivingLights lights)
        {
            this.assets = assets;
            this.layout = layout;
            this.building = building;
            this.atlas = atlas;
            this.lights = lights;
            Root = new GameObject("Facade").transform;
            Root.SetParent(parent, false);
        }

        public void Build(PixelGrid grid)
        {
            w = layout.FloorWidth; d = layout.Depth; sx = layout.ShaftX; H = layout.FloorCount * layout.FloorHeight;
            left = sx - w * 0.5f; right = sx + w * 0.5f;
            float wall = 0.3f;
            front = -d * 0.5f;
            g = RenderingStyle.GroundY;
            var lit = new LitBatch(assets) { ShearDepth = front };
            var self = new PaletteMeshBuilder(atlas);
            var stone = assets.PatternLit(Stone, WallPattern.Stone);
            var stoneDark = assets.Lit(StoneDark);
            var trim = assets.Lit(Trim);
            var brick = assets.PatternLit(Pilaster, WallPattern.Brick);
            var dentil = assets.PatternLit(Trim, WallPattern.Dentil);
            var deck = assets.PatternLit(RoofDeck, WallPattern.Gravel);
            var metal = assets.Lit(Metal);
            var ac = assets.Lit(AcGrey);
            var wood = assets.Lit(Wood);

            float pin = CutawayClearance, pw = PilasterWidth;
            pz = front - 0.3f;
            pilL0 = left - pin - pw;
            pilR1 = right + pin + pw;

            // ---- plinth: rusticated stone from the sidewalk up to the lobby floor (y = 0)
            float px0 = pilL0 - 0.1f, px1 = pilR1 + 0.1f;
            lit.BoxMinMax(new Vector3(px0, g, front - 0.25f), new Vector3(px1, 0f, d * 0.5f + wall * 0.5f), stone);
            lit.BoxMinMax(new Vector3(px0 - 0.05f, g, front - 0.32f), new Vector3(px1 + 0.05f, g + 0.35f, front - 0.25f), stoneDark);
            lit.BoxMinMax(new Vector3(px0 - 0.05f, -0.16f, front - 0.3f), new Vector3(px1 + 0.05f, 0f, front - 0.2f), trim);
            // shadow under the plinth cap
            lit.BoxMinMax(new Vector3(px0, -0.25f, front - 0.27f), new Vector3(px1, -0.16f, front - 0.25f), stoneDark);
            int vent = atlas.Static(new Color(0.06f, 0.06f, 0.07f));
            for (int i = 0; i < 4; i++)
            {
                float vx = left + 1.4f + i * (w - 2.8f) / 3f;
                self.Front(vx - 0.5f, g + 0.55f, vx + 0.5f, g + 1.05f, front - 0.26f, vent);
            }
            // lucky house number plaque (brass "8") left of the centre
            int brass = atlas.Static(new Color(0.78f, 0.62f, 0.28f));
            self.Front(sx - 0.45f, g + 0.45f, sx + 0.45f, g + 1.25f, front - 0.27f, brass);
            int brassInk = atlas.Static(new Color(0.20f, 0.13f, 0.05f));
            self.Front(sx - 0.15f, g + 0.6f, sx + 0.15f, g + 1.1f, front - 0.28f, brassInk);

            // ---- corner pilasters: brick, stone quoins on the outer edge, floor bands with a shadow line, base, capital
            float q = 4f / grid.PixelsPerUnit; // quoin course = 4 px of the gameplay frame
            for (int side = 0; side < 2; side++)
            {
                bool L = side == 0;
                float a = L ? pilL0 : right + pin;
                float b = a + pw;
                lit.BoxMinMax(new Vector3(a, 0f, pz), new Vector3(b, H, d * 0.5f + wall * 0.5f), brick);
                // quoins: alternating long / short light blocks up the outer corner (they only grow outward)
                int k = 0;
                for (float y = 0.45f; y + q <= H - 0.5f; y += q, k++)
                {
                    float qw = (k & 1) == 0 ? 0.36f : 0.2f;
                    if (L) lit.BoxMinMax(new Vector3(a - 0.03f, y + 0.02f, pz - 0.04f), new Vector3(a + qw, y + q - 0.02f, pz + 0.3f), trim);
                    else lit.BoxMinMax(new Vector3(b - qw, y + 0.02f, pz - 0.04f), new Vector3(b + 0.03f, y + q - 0.02f, pz + 0.3f), trim);
                }
                for (int f = 1; f < layout.FloorCount; f++)
                {
                    float y = f * layout.FloorHeight;
                    lit.BoxMinMax(new Vector3(L ? a - 0.08f : a, y - 0.3f, pz - 0.1f), new Vector3(L ? b : b + 0.08f, y, pz + 0.4f), trim);
                    lit.BoxMinMax(new Vector3(L ? a - 0.05f : a, y - 0.39f, pz - 0.06f), new Vector3(L ? b : b + 0.05f, y - 0.3f, pz + 0.4f), stoneDark);
                }
                // base and capital only grow outward: the inner face stays clear of the cut-away
                lit.BoxMinMax(new Vector3(L ? a - 0.1f : a, 0f, pz - 0.12f), new Vector3(L ? b : b + 0.1f, 0.45f, pz + 0.4f), stoneDark);
                lit.BoxMinMax(new Vector3(L ? a - 0.14f : a, H - 0.42f, pz - 0.14f), new Vector3(L ? b : b + 0.14f, H, pz + 0.4f), trim);
                lit.BoxMinMax(new Vector3(L ? a - 0.08f : a, H - 0.51f, pz - 0.08f), new Vector3(L ? b : b + 0.08f, H - 0.42f, pz + 0.4f), stoneDark);
            }

            // ---- entablature (on the roof slab, never over floor 9): dentil frieze, shadow line, projecting cornice
            cx0 = pilL0 - 0.25f; cx1 = pilR1 + 0.25f;
            lit.BoxMinMax(new Vector3(cx0, H, front - 0.4f), new Vector3(cx1, H + 0.3f, d * 0.5f + 0.25f), dentil);
            lit.BoxMinMax(new Vector3(cx0, H + 0.21f, front - 0.43f), new Vector3(cx1, H + 0.3f, front - 0.4f), stoneDark);
            lit.BoxMinMax(new Vector3(cx0 - 0.15f, H + 0.3f, front - 0.55f), new Vector3(cx1 + 0.15f, H + 0.46f, d * 0.5f + 0.35f), trim);
            roof = H + 0.46f;
            // paved roof deck (a thin slab inside the parapet)
            lit.BoxMinMax(new Vector3(cx0 + 0.1f, roof, front - 0.2f), new Vector3(cx1 - 0.1f, roof + 0.02f, d * 0.5f + 0.25f), deck);

            // ---- stepped parapet: low steps beside the HOTEL sign, full height outside, corner piers with finials
            float parapetZ = front - 0.45f, pzBack = front - 0.2f;
            float signHalf = building.HotelSignRoot != null ? building.HotelPlateSize.x * 0.5f + 3f * building.SignTexelBase : 3.3f;
            stepIn = sx - signHalf - 0.15f;
            float stepOut = stepIn - 0.9f;
            float parapetTop = roof + 0.42f, stepTop = roof + 0.24f, pierTop = roof + 0.72f;
            for (int side = 0; side < 2; side++)
            {
                float m = side == 0 ? 1f : -1f; // mirror around the shaft
                float o0 = sx + m * (cx0 - sx), o1 = sx + m * (stepOut - sx), i1 = sx + m * (stepIn - sx);
                float p0 = sx + m * (cx0 + 0.6f - sx);
                Wall(lit, brick, trim, Mathf.Min(p0, o1), Mathf.Max(p0, o1), roof, parapetTop, parapetZ, pzBack);
                Wall(lit, brick, trim, Mathf.Min(o1, i1), Mathf.Max(o1, i1), roof, stepTop, parapetZ, pzBack);
                // corner pier + finial
                float a = Mathf.Min(o0, p0), b = Mathf.Max(o0, p0);
                Wall(lit, brick, trim, a, b, roof, pierTop, parapetZ - 0.05f, pzBack + 0.05f);
                float fc = (a + b) * 0.5f;
                lit.BoxMinMax(new Vector3(fc - 0.12f, pierTop + 0.07f, parapetZ + 0.02f), new Vector3(fc + 0.12f, pierTop + 0.27f, parapetZ + 0.2f), trim);
            }
            // pigeons perch on the coping: right step, left parapet (two), the left step, right parapet
            pigeonSpots.Clear();
            pigeonSpots.Add(new Vector3(2f * sx - stepIn + 0.25f, stepTop + 0.07f, parapetZ - 0.05f));
            pigeonSpots.Add(new Vector3(cx0 + 1.4f, parapetTop + 0.07f, parapetZ - 0.05f));
            pigeonSpots.Add(new Vector3(cx0 + 1.85f, parapetTop + 0.07f, parapetZ - 0.05f));
            pigeonSpots.Add(new Vector3(stepIn - 0.55f, stepTop + 0.07f, parapetZ - 0.05f));
            pigeonSpots.Add(new Vector3(cx1 - 2.3f, parapetTop + 0.07f, parapetZ - 0.05f));

            // ---- roof, left: antenna mast + beacon, dish post, brick chimney, two AC units, laundry poles
            float mastX = cx0 + 0.55f;
            lit.BoxMinMax(new Vector3(mastX, roof, 0.9f), new Vector3(mastX + 0.12f, roof + 2.15f, 1.02f), metal);
            lit.BoxMinMax(new Vector3(mastX - 0.3f, roof + 1.3f, 0.9f), new Vector3(mastX + 0.42f, roof + 1.37f, 1.02f), metal);
            lit.BoxMinMax(new Vector3(mastX - 0.18f, roof + 1.7f, 0.9f), new Vector3(mastX + 0.3f, roof + 1.76f, 1.02f), metal);
            lit.BoxMinMax(new Vector3(mastX - 0.25f, roof, 0.75f), new Vector3(mastX + 0.37f, roof + 0.12f, 1.17f), metal);
            int beacon = lights.Add(LivingLights.Kind.Beacon, new Color(1f, 0.22f, 0.18f), new Color(0.32f, 0.08f, 0.07f));
            self.Box(new Vector3(mastX - 0.05f, roof + 2.15f, 0.88f), new Vector3(mastX + 0.17f, roof + 2.33f, 1.04f), beacon, beacon, beacon);

            dishX = mastX + 0.95f;
            lit.BoxMinMax(new Vector3(dishX, roof, -0.45f), new Vector3(dishX + 0.09f, roof + 0.55f, -0.36f), metal);
            lit.BoxMinMax(new Vector3(dishX - 0.2f, roof, -0.6f), new Vector3(dishX + 0.29f, roof + 0.08f, -0.2f), metal);

            float chimX = dishX + 0.6f;
            lit.BoxMinMax(new Vector3(chimX, roof, 1.0f), new Vector3(chimX + 0.5f, roof + 1.05f, 1.5f), brick);
            lit.BoxMinMax(new Vector3(chimX - 0.06f, roof + 1.05f, 0.94f), new Vector3(chimX + 0.56f, roof + 1.15f, 1.56f), trim);
            self.Flat(chimX + 0.08f, 1.08f, chimX + 0.42f, 1.42f, roof + 1.151f, atlas.Static(new Color(0.05f, 0.04f, 0.04f)));

            int grille = atlas.Static(new Color(0.12f, 0.12f, 0.14f));
            float ac0 = chimX + 0.75f;
            lit.BoxMinMax(new Vector3(ac0, roof, -0.75f), new Vector3(ac0 + 1.15f, roof + 0.68f, 0.4f), ac);
            self.Front(ac0 + 0.22f, roof + 0.12f, ac0 + 0.93f, roof + 0.56f, -0.76f, grille);
            lit.BoxMinMax(new Vector3(ac0 + 0.1f, roof + 0.68f, -0.6f), new Vector3(ac0 + 1.05f, roof + 0.74f, 0.25f), metal);
            float ac1 = ac0 + 1.35f;
            if (ac1 + 0.95f < stepIn - 0.1f)
            {
                lit.BoxMinMax(new Vector3(ac1, roof, -0.7f), new Vector3(ac1 + 0.95f, roof + 0.55f, 0.2f), ac);
                self.Front(ac1 + 0.2f, roof + 0.1f, ac1 + 0.75f, roof + 0.45f, -0.71f, grille);
            }
            lineX0 = chimX + 0.75f;
            lineX1 = Mathf.Min(stepIn - 0.2f, lineX0 + 2.5f);
            foreach (float lx in new[] { lineX0, lineX1 - 0.08f })
                lit.BoxMinMax(new Vector3(lx, roof, LineZ - 0.04f), new Vector3(lx + 0.08f, roof + LineH, LineZ + 0.04f), metal);

            // ---- roof, right: water tower (stilts, banded barrel, stepped cone), mushroom vents, brick stair hut, planters
            towerX0 = sx + signHalf + 0.55f;
            towerX1 = towerX0 + 1.4f;
            float tz0 = TowerZ, tz1 = TowerZ + 1.2f;
            foreach (float lx in new[] { towerX0 + 0.1f, towerX1 - 0.18f })
                foreach (float lz in new[] { tz0 + 0.1f, tz1 - 0.18f })
                    lit.BoxMinMax(new Vector3(lx, roof, lz), new Vector3(lx + 0.08f, roof + LegH, lz + 0.08f), metal);
            lit.BoxMinMax(new Vector3(towerX0 - 0.05f, roof + LegH - 0.06f, tz0 - 0.05f), new Vector3(towerX1 + 0.05f, roof + LegH, tz1 + 0.05f), metal);
            float by0 = roof + LegH, by1 = by0 + BarrelH;
            lit.BoxMinMax(new Vector3(towerX0, by0, tz0), new Vector3(towerX1, by1, tz1), wood);
            foreach (float hy in new[] { by0 + 0.3f, by0 + 0.8f })
                lit.BoxMinMax(new Vector3(towerX0 - 0.03f, hy, tz0 - 0.03f), new Vector3(towerX1 + 0.03f, hy + 0.06f, tz1 + 0.03f), metal);
            const float coneStep = 0.24f;
            for (int i = 0; i < 3; i++)
                lit.BoxMinMax(new Vector3(towerX0 - 0.06f + i * coneStep, by1 + i * 0.13f, tz0 - 0.06f + i * 0.2f),
                    new Vector3(towerX1 + 0.06f - i * coneStep, by1 + (i + 1) * 0.13f, tz1 + 0.06f - i * 0.2f), metal);
            float cone = (towerX0 + towerX1) * 0.5f;
            lit.BoxMinMax(new Vector3(cone - 0.04f, by1 + 0.39f, tz0 + 0.55f), new Vector3(cone + 0.04f, by1 + 0.6f, tz0 + 0.63f), metal);

            float vx0 = towerX1 + 0.35f;
            for (int i = 0; i < 2; i++)
            {
                float vxi = vx0 + i * 0.42f;
                lit.BoxMinMax(new Vector3(vxi, roof, -0.95f), new Vector3(vxi + 0.12f, roof + 0.3f, -0.83f), metal);
                lit.BoxMinMax(new Vector3(vxi - 0.1f, roof + 0.3f, -1.03f), new Vector3(vxi + 0.22f, roof + 0.42f, -0.75f), ac);
            }

            hutX1 = cx1 - 0.85f;
            hutX0 = hutX1 - 1.7f;
            lit.BoxMinMax(new Vector3(hutX0, roof, HutZ), new Vector3(hutX1, roof + HutH, HutZ + 1.5f), brick);
            lit.BoxMinMax(new Vector3(hutX0 - 0.08f, roof + HutH, HutZ - 0.08f), new Vector3(hutX1 + 0.08f, roof + HutH + 0.12f, HutZ + 1.58f), metal);
            self.Front(hutX0 + 0.3f, roof, hutX0 + 0.9f, roof + 1.05f, HutZ - 0.01f, atlas.Static(new Color(0.10f, 0.07f, 0.06f)));
            int hutLamp = lights.Add(LivingLights.Kind.Lamp, new Color(1f, 0.85f, 0.55f), new Color(0.3f, 0.25f, 0.18f));
            self.Front(hutX0 + 1.15f, roof + 0.95f, hutX0 + 1.4f, roof + 1.15f, HutZ - 0.01f, hutLamp);
            for (int i = 0; i < 2; i++)
            {
                float a = hutX0 + 0.1f + i * 0.85f;
                lit.BoxMinMax(new Vector3(a, roof, -0.95f), new Vector3(a + 0.7f, roof + 0.22f, -0.6f), wood);
            }

            // ---- entrance annex (right of the hotel): stone, glass door, lamps, striped canopy over the sidewalk, AC on top
            float ex0 = right + pin + AnnexOffset, ex1 = ex0 + 4.2f, ez = front + 0.3f, eTop = g + 3.2f;
            annex0 = ex0; annex1 = ex1; annexTop = eTop; annexZ = ez;
            lit.BoxMinMax(new Vector3(ex0, g, ez), new Vector3(ex1, eTop, d * 0.5f - 0.5f), stone);
            lit.BoxMinMax(new Vector3(ex0 - 0.05f, eTop, ez - 0.15f), new Vector3(ex1 + 0.1f, eTop + 0.25f, d * 0.5f - 0.4f), trim);
            lit.BoxMinMax(new Vector3(ex0 - 0.05f, eTop - 0.08f, ez - 0.12f), new Vector3(ex1 + 0.1f, eTop, ez), stoneDark);
            lit.BoxMinMax(new Vector3(ex1 - 1.7f, eTop + 0.25f, ez + 0.6f), new Vector3(ex1 - 0.7f, eTop + 0.8f, ez + 1.5f), ac);
            self.Front(ex1 - 1.5f, eTop + 0.35f, ex1 - 0.9f, eTop + 0.7f, ez + 0.59f, grille);
            float dx0 = ex0 + 1.2f, dx1 = ex1 - 1.0f;
            DoorX = (dx0 + dx1) * 0.5f + RenderingStyle.CameraShear.x * (ez - front);
            int glass = lights.Add(LivingLights.Kind.Lamp, new Color(0.98f, 0.82f, 0.52f), new Color(0.35f, 0.28f, 0.18f));
            self.Front(dx0, g, dx1, g + 2.2f, ez - 0.02f, glass);
            int frameC = atlas.Static(RenderingStyle.ShaftFrame * 0.9f);
            self.Front(dx0 - 0.12f, g, dx0, g + 2.32f, ez - 0.03f, frameC);
            self.Front(dx1, g, dx1 + 0.12f, g + 2.32f, ez - 0.03f, frameC);
            self.Front(dx0, g + 2.2f, dx1, g + 2.32f, ez - 0.03f, frameC);
            self.Front((dx0 + dx1) * 0.5f - 0.05f, g, (dx0 + dx1) * 0.5f + 0.05f, g + 2.2f, ez - 0.03f, frameC);
            for (int i = 0; i < 2; i++)
            {
                float lx = i == 0 ? dx0 - 0.55f : dx1 + 0.3f;
                int lamp = lights.Add(LivingLights.Kind.Lamp, new Color(1f, 0.9f, 0.62f), new Color(0.3f, 0.26f, 0.2f));
                self.Box(new Vector3(lx, g + 1.45f, ez - 0.2f), new Vector3(lx + 0.25f, g + 1.8f, ez), lamp, lamp, lamp);
            }
            var awning = assets.Lit(Awning);
            var awningLight = assets.Lit(AwningLight);
            float cz0 = RenderingStyle.CurbZ + 0.3f, cy = g + 2.45f;
            lit.BoxMinMax(new Vector3(dx0 - 0.6f, cy, cz0), new Vector3(dx1 + 0.6f, cy + 0.18f, ez), awning);
            int stripes = Mathf.Max(3, Mathf.RoundToInt((dx1 - dx0 + 1.2f) / 0.4f));
            float sw = (dx1 - dx0 + 1.2f) / stripes;
            for (int i = 0; i < stripes; i++)
                lit.BoxMinMax(new Vector3(dx0 - 0.6f + i * sw, cy - 0.28f, cz0 - 0.06f), new Vector3(dx0 - 0.6f + (i + 1) * sw, cy, cz0 + 0.04f), (i & 1) == 0 ? awning : awningLight);
            for (int i = 0; i < 2; i++)
            {
                float postX = i == 0 ? dx0 - 0.5f : dx1 + 0.38f;
                lit.BoxMinMax(new Vector3(postX, g, cz0 + 0.05f), new Vector3(postX + 0.12f, cy - 0.28f, cz0 + 0.17f), metal);
            }

            // ---- AC boxes hanging on the right side wall (seen on the +X face)
            foreach (int f in new[] { 1, 4, 6 })
            {
                float y = f * layout.FloorHeight + 0.9f;
                lit.BoxMinMax(new Vector3(pilR1, y, -1.3f), new Vector3(pilR1 + 0.32f, y + 0.42f, -0.7f), ac);
                lit.BoxMinMax(new Vector3(pilR1, y - 0.06f, -1.25f), new Vector3(pilR1 + 0.26f, y, -1.19f), metal);
            }

            // ---- sidewalk furniture (Kenney atlas, one merged mesh): planters by the door, bench + bin by the plinth
            var kenney = assets.Kenney;
            if (kenney != null)
            {
                var props = new List<CombineInstance>();
                void Prop(string name, float x, float z, float scale)
                {
                    var mesh = kenney.FindProp(name);
                    if (mesh != null) props.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(new Vector3(x, g, z), Quaternion.identity, Vector3.one * scale) });
                }
                Prop("pottedPlant", dx0 - 0.95f, front - 0.15f, 0.9f);
                Prop("pottedPlant", dx1 + 0.85f, front - 0.15f, 0.9f);
                Prop("bench", left - 3.2f, front - 0.25f, 1f);
                Prop("trashcan", left - 1.6f, front - 0.35f, 1f);
                if (props.Count > 0)
                {
                    var mesh = assets.Own(new Mesh { name = "Sidewalk Props" });
                    mesh.CombineMeshes(props.ToArray(), true, true);
                    var r = PaletteRenderer.Create(Root, "Sidewalk Props", mesh, kenney.Material);
                    Renderers.Add(r);
                }
            }

            // ---- HOTEL sign: dark frame + two interleaved sets of marquee bulbs + a pixel scaffold (children of the sign assembly)
            if (building.HotelSignRoot != null) BuildSignFrame();

            var selfMesh = self.Build("Facade Lights");
            Renderers.Add(PaletteRenderer.Create(Root, "Facade Lights", selfMesh, atlas.Material));
            LitParts = lit.Count;
            foreach (var r in lit.Build(Root, "Facade", castShadows: true)) Renderers.Add(r);

            // ---- pixel details, one mesh per frame zoom (built lazily, same lights / texels replayed)
            int key = Key(grid.PixelsPerUnit);
            var detail = BuildDetail(grid, replay: false);
            detailMeshes[key] = detail;
            DetailRenderer = PaletteRenderer.Create(Root, "Facade Details", detail, atlas.Material);
            detailFilter = DetailRenderer.GetComponent<MeshFilter>();
            DetailPixelsPerUnit = key;
            Renderers.Add(DetailRenderer);
        }

        /// <summary>Brick wall section with a stone coping (front face at z0, back at z1).</summary>
        static void Wall(LitBatch lit, Material brick, Material trim, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            if (x1 - x0 < 0.05f) return;
            lit.BoxMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), brick);
            lit.BoxMinMax(new Vector3(x0 - 0.04f, y1, z0 - 0.04f), new Vector3(x1 + 0.04f, y1 + 0.07f, z1 + 0.04f), trim);
        }

        static int Key(float ppu) => Mathf.Max(1, Mathf.RoundToInt(ppu));

        /// <summary>Active frame's pixel grid: the detail mesh built for that zoom (whole pixels in every frame).</summary>
        public void ApplyPixelGrid(PixelGrid g)
        {
            if (detailFilter == null) return;
            int key = Key(g.PixelsPerUnit);
            if (key == DetailPixelsPerUnit) return;
            if (!detailMeshes.TryGetValue(key, out var mesh))
            {
                mesh = BuildDetail(g, replay: true);
                detailMeshes[key] = mesh;
            }
            detailFilter.sharedMesh = mesh;
            DetailPixelsPerUnit = key;
        }

        void BuildSignFrame()
        {
            var sign = new PaletteMeshBuilder(atlas);
            float t = building.SignTexelBase;
            var plate = building.HotelPlateSize;
            float hw = plate.x * 0.5f + 2f * t;
            int frame = atlas.Static(new Color(0.07f, 0.06f, 0.07f));
            sign.Front(-hw, -2f * t, hw, plate.y + 2f * t, 0.03f, frame);
            sign.Flat(-hw, 0.03f, hw, 0.3f, plate.y + 2f * t, atlas.Static(new Color(0.16f, 0.15f, 0.17f)));
            var on = new Color(1f, 0.90f, 0.62f);
            var off = new Color(0.42f, 0.32f, 0.20f);
            int bulbA = lights.Add(LivingLights.Kind.BulbA, on, off);
            int bulbB = lights.Add(LivingLights.Kind.BulbB, on, off);
            int n = 0;
            // top and bottom rows: one-texel bulbs every other texel, in the frame margin
            for (float x = -hw + 0.5f * t; x + t <= hw - 0.5f * t + 1e-4f; x += 2f * t)
            {
                int tex = (n++ & 1) == 0 ? bulbA : bulbB;
                sign.Front(x, plate.y + 0.5f * t, x + t, plate.y + 1.5f * t, 0.02f, tex);
                sign.Front(x, -1.5f * t, x + t, -0.5f * t, 0.02f, tex == bulbA ? bulbB : bulbA);
            }
            BulbCount = n * 2;
            // scaffold between the legs: two rails and X bracing in 1-px steps (half a sign texel = 1 target pixel)
            float p = 0.5f * t, legX = Mathf.Round(plate.x * 0.35f / p) * p, low = -BuildingView.HotelSignLift + 0.06f, high = -2f * t - p;
            int iron = atlas.Static(new Color(0.20f, 0.20f, 0.23f));
            int ironLit = atlas.Static(new Color(0.34f, 0.33f, 0.36f));
            float rail = Mathf.Round((low + high) * 0.5f / p) * p;
            sign.Front(-legX, rail, legX, rail + p, 0.17f, ironLit);
            sign.Front(-legX, high - p, legX, high, 0.17f, iron);
            for (int i = 0; i < 2; i++)
            {
                float a = i == 0 ? -legX : 0f, b = i == 0 ? 0f : legX;
                int steps = Mathf.Max(1, Mathf.RoundToInt((b - a) / p));
                int rise = Mathf.Max(1, Mathf.RoundToInt((rail - low) / p));
                for (int s = 0; s < steps; s++)
                {
                    float x = a + s * p;
                    float k = s / (float)steps;
                    float up = low + Mathf.Floor(k * rise) * p, down = low + Mathf.Floor((1f - k) * rise) * p;
                    sign.Front(x, up, x + p, up + p, 0.18f, iron);
                    sign.Front(x, down, x + p, down + p, 0.18f, iron);
                }
            }
            var mesh = sign.Build("Hotel Sign Frame");
            var r = PaletteRenderer.Create(building.HotelSignRoot, "Sign Frame", mesh, atlas.Material);
            Renderers.Add(r);
        }

        // ------------------------------------------------------------------ pixel details (per frame zoom)

        static readonly string[] Pigeon =
        {
            ".##..",
            "#ww..",
            ".wwww",
            "..bb.",
        };

        static readonly string[] PigeonPeck =
        {
            ".....",
            "..www",
            "#wwww",
            ".#bb.",
        };

        static readonly string[] Dish =
        {
            "...ss..",
            ".sswwl.",
            "swwwwl.",
            "swwwl..",
            ".swl.#.",
            "..l.#..",
            "...#...",
        };

        static readonly string[] Bush =
        {
            ".gGg.p",
            "gGGgGg",
            "gggGgg",
        };

        static readonly string[] Shirt =
        {
            "ss.ss",
            "sssss",
            ".sss.",
            ".sss.",
        };

        Mesh BuildDetail(PixelGrid grid, bool replay)
        {
            if (replay) ledger.Replay();
            var b = new PaletteMeshBuilder(atlas);
            var p = new PixelPainter(b, grid.PixelsPerUnit, grid.Shear, grid.ShearDepth);

            // ---- vertical HOTEL blade sign on the left pilaster (1 font texel = 1 px), two iron brackets
            int bladeBack = atlas.Static(new Color(0.07f, 0.05f, 0.07f));
            int bladeRim = atlas.Static(new Color(0.55f, 0.14f, 0.16f));
            int bladeInk = atlas.Static(new Color(0.90f, 0.31f, 0.31f));
            int iron = atlas.Static(new Color(0.18f, 0.18f, 0.21f));
            float bz = pz - 0.2f;
            var anchor = p.At(new Vector3(pilL0, 4.5f * layout.FloorHeight, bz));
            const int bw = 11, letterGap = 3, pad = 4;
            string word = BuildingView.HotelText;
            int bh = word.Length * PixelFont.GlyphHeight + (word.Length - 1) * letterGap + pad * 2;
            int bx1 = anchor.x - 2, bx0 = bx1 - bw, by0 = anchor.y - bh / 2;
            p.Rect(bx0, by0, bw, bh, bz, bladeRim);
            p.Rect(bx0 + 1, by0 + 1, bw - 2, bh - 2, bz - 0.005f, bladeBack);
            for (int i = 0; i < word.Length; i++)
            {
                var mask = PixelFont.Rasterize(word[i].ToString(), 0, out var size);
                int ly = by0 + bh - pad - (i + 1) * PixelFont.GlyphHeight - i * letterGap;
                for (int row = 0; row < size.y; row++)
                    for (int col = 0; col < size.x;)
                    {
                        if (!mask[row * size.x + col]) { col++; continue; }
                        int end = col + 1;
                        while (end < size.x && mask[row * size.x + end]) end++;
                        p.Rect(bx0 + 3 + col, ly + row, end - col, 1, bz - 0.01f, bladeInk);
                        col = end;
                    }
            }
            foreach (int ay in new[] { by0 + 3, by0 + bh - 4 })
            {
                p.Rect(bx1, ay, anchor.x - bx1 + 1, 1, bz, iron);
                p.Rect(anchor.x, ay - 1, 1, 3, bz, iron);
            }
            BladeSignPixels = new RectInt(bx0, by0, bw, bh);

            // ---- wall sconces on the pilasters (one per floor) and a drainpipe down the right side wall
            int sconce = ledger.Take(() => lights.Add(LivingLights.Kind.Lamp, new Color(1.00f, 0.80f, 0.50f), new Color(0.32f, 0.26f, 0.18f)));
            int bracket = atlas.Static(new Color(0.16f, 0.15f, 0.16f));
            for (int f = 0; f < layout.FloorCount; f++)
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? pilL0 + PilasterWidth * 0.62f : right + CutawayClearance + PilasterWidth * 0.38f;
                    var s = p.At(new Vector3(x, f * layout.FloorHeight + 1.75f, pz - 0.05f));
                    p.Rect(s.x, s.y, 1, 2, pz - 0.06f, sconce);
                    p.Rect(s.x - 1, s.y - 1, 3, 1, pz - 0.06f, bracket);
                }
            int pipe = atlas.Static(new Color(0.30f, 0.40f, 0.36f));
            int pipeDark = atlas.Static(new Color(0.17f, 0.22f, 0.20f));
            const float pipeZ = -1.55f;
            var top = p.At(new Vector3(pilR1 + 0.03f, H - 0.1f, pipeZ));
            var bottom = p.At(new Vector3(pilR1 + 0.03f, g, pipeZ));
            p.Rect(top.x, bottom.y + 2, 1, top.y - bottom.y - 2, pipeZ, pipe);
            p.Rect(top.x - 1, top.y - 2, 3, 3, pipeZ - 0.005f, pipeDark);
            p.Rect(top.x, bottom.y, 3, 2, pipeZ, pipe);
            for (int f = 1; f < layout.FloorCount; f++)
            {
                var c = p.At(new Vector3(pilR1 + 0.03f, f * layout.FloorHeight - 1.2f, pipeZ));
                p.Rect(c.x - 1, c.y, 3, 1, pipeZ - 0.005f, pipeDark);
            }

            // ---- marquee bulbs along the annex cornice (same chase as the HOTEL sign)
            var on = new Color(1f, 0.90f, 0.62f);
            var off = new Color(0.42f, 0.32f, 0.20f);
            int bulbA = ledger.Take(() => lights.Add(LivingLights.Kind.BulbA, on, off));
            int bulbB = ledger.Take(() => lights.Add(LivingLights.Kind.BulbB, on, off));
            float mz = annexZ - 0.17f;
            var m0 = p.At(new Vector3(annex0 + 0.15f, annexTop + 0.125f, mz));
            var m1 = p.At(new Vector3(annex1, annexTop + 0.125f, mz));
            for (int x = m0.x, i = 0; x < m1.x; x += 2, i++) p.Rect(x, m0.y, 1, 1, mz, (i & 1) == 0 ? bulbA : bulbB);

            // ---- roof: satellite dishes, laundry, water tower ladder + bracing, planters' bushes, pigeons
            var dishKeys = new Dictionary<char, int>
            {
                ['w'] = atlas.Static(new Color(0.66f, 0.67f, 0.70f)),
                ['s'] = atlas.Static(new Color(0.80f, 0.81f, 0.84f)),
                ['l'] = atlas.Static(new Color(0.42f, 0.43f, 0.47f)),
                ['#'] = atlas.Static(new Color(0.20f, 0.20f, 0.23f)),
            };
            var d1 = p.At(new Vector3(dishX + 0.045f, roof + 0.5f, -0.47f));
            p.Sprite(d1.x - 3, d1.y, Dish, -0.47f, dishKeys);
            var d2 = p.At(new Vector3(hutX1 - 0.45f, roof + HutH + 0.12f, HutZ + 0.5f));
            p.Sprite(d2.x - 3, d2.y, Dish, HutZ + 0.49f, dishKeys);

            int line = atlas.Static(new Color(0.55f, 0.55f, 0.58f));
            var l0 = p.At(new Vector3(lineX0 + 0.08f, roof + LineH - 0.04f, LineZ - 0.05f));
            var l1 = p.At(new Vector3(lineX1 - 0.08f, roof + LineH - 0.04f, LineZ - 0.05f));
            int span = Mathf.Max(1, l1.x - l0.x);
            for (int x = l0.x; x < l1.x; x++)
            {
                float u = (x - l0.x) / (float)span;
                int sag = u > 0.3f && u < 0.7f ? 1 : 0;
                p.Rect(x, l0.y - sag, 1, 1, LineZ - 0.05f, line);
            }
            var cloth = new[]
            {
                atlas.Static(new Color(0.86f, 0.86f, 0.82f)), atlas.Static(new Color(0.72f, 0.20f, 0.22f)),
                atlas.Static(new Color(0.86f, 0.86f, 0.82f)), atlas.Static(new Color(0.30f, 0.42f, 0.72f)),
                atlas.Static(new Color(0.88f, 0.74f, 0.30f)),
            };
            int cx = l0.x + 2;
            for (int i = 0; i < cloth.Length && cx + 4 < l1.x; i++)
            {
                float u = (cx + 1 - l0.x) / (float)span;
                int ty = l0.y - (u > 0.3f && u < 0.7f ? 1 : 0);
                if (i == 3) p.Sprite(cx, ty - 4, Shirt, LineZ - 0.06f, new Dictionary<char, int> { ['s'] = cloth[i] });
                else
                {
                    int cw = i == 4 ? 2 : 3, chh = i == 1 ? 5 : 4;
                    p.Rect(cx, ty - chh, cw, chh, LineZ - 0.06f, cloth[i]);
                }
                cx += i == 3 ? 7 : 5;
            }

            int rail = atlas.Static(new Color(0.15f, 0.15f, 0.17f));
            float lz = TowerZ - 0.03f;
            var lb = p.At(new Vector3(towerX1 - 0.55f, roof, lz));
            var lt = p.At(new Vector3(towerX1 - 0.55f, roof + LegH + BarrelH + 0.1f, lz));
            p.Rect(lb.x, lb.y, 1, lt.y - lb.y, lz, rail);
            p.Rect(lb.x + 3, lb.y, 1, lt.y - lb.y, lz, rail);
            for (int y = lb.y + 1; y < lt.y; y += 2) p.Rect(lb.x + 1, y, 2, 1, lz, rail);
            var b0 = p.At(new Vector3(towerX0 + 0.18f, roof + 0.04f, TowerZ + 0.08f));
            var b1 = p.At(new Vector3(towerX1 - 0.18f, roof + LegH - 0.06f, TowerZ + 0.08f));
            p.Line(b0.x, b0.y, b1.x, b1.y, TowerZ + 0.07f, rail);
            p.Line(b0.x, b1.y, b1.x, b0.y, TowerZ + 0.07f, rail);

            var bushKeys = new Dictionary<char, int>
            {
                ['g'] = atlas.Static(new Color(0.15f, 0.34f, 0.18f)),
                ['G'] = atlas.Static(new Color(0.24f, 0.48f, 0.24f)),
                ['p'] = atlas.Static(new Color(0.90f, 0.46f, 0.62f)),
            };
            for (int i = 0; i < 2; i++)
            {
                var at = p.At(new Vector3(hutX0 + 0.2f + i * 0.85f, roof + 0.22f, -0.96f));
                p.Sprite(at.x, at.y, Bush, -0.97f, bushKeys);
            }

            var pigeonKeys = new Dictionary<char, int>
            {
                ['#'] = atlas.Static(new Color(0.20f, 0.21f, 0.25f)),
                ['w'] = atlas.Static(new Color(0.40f, 0.41f, 0.47f)),
                ['b'] = atlas.Static(new Color(0.30f, 0.31f, 0.36f)),
            };
            PigeonCount = 0;
            for (int i = 0; i < pigeonSpots.Count; i++)
            {
                var at = p.At(pigeonSpots[i]);
                p.Sprite(at.x, at.y, i == 2 ? PigeonPeck : Pigeon, pigeonSpots[i].z, pigeonKeys, (i & 1) == 1);
                PigeonCount++;
            }
            return b.Build("Facade Details " + Key(grid.PixelsPerUnit) + "ppu");
        }
    }

    /// <summary>
    /// Paints whole-pixel rectangles / sprites on the screen-plane pixel lattice of one frame zoom (a pixel = 1/ppu
    /// screen-plane units), at a given depth: front-facing palette quads that project exactly onto target pixels.
    /// </summary>
    sealed class PixelPainter
    {
        readonly PaletteMeshBuilder b;
        readonly float ppu;
        readonly Vector2 shear;
        readonly float z0;

        public PixelPainter(PaletteMeshBuilder b, float pixelsPerUnit, Vector2 shear, float shearDepth)
        {
            this.b = b;
            ppu = Mathf.Max(1f, pixelsPerUnit);
            this.shear = shear;
            z0 = shearDepth;
        }

        /// <summary>Screen-plane pixel corner nearest to where <paramref name="world"/> projects.</summary>
        public Vector2Int At(Vector3 world)
        {
            var s = PixelGrid.Project(world, shear, z0);
            return new Vector2Int(Mathf.RoundToInt(s.x * ppu), Mathf.RoundToInt(s.y * ppu));
        }

        public void Rect(int x, int y, int w, int h, float z, int texel)
        {
            if (w <= 0 || h <= 0 || texel < 0) return;
            float sx0 = x / ppu, sx1 = (x + w) / ppu, sy0 = y / ppu, sy1 = (y + h) / ppu;
            b.Front(PixelGrid.WorldX(sx0, z, shear, z0), PixelGrid.WorldY(sy0, z, shear, z0), PixelGrid.WorldX(sx1, z, shear, z0), PixelGrid.WorldY(sy1, z, shear, z0), z, texel);
        }

        /// <summary>Bitmap (rows top-down, '.' = empty) with its bottom-left pixel at (x, y); runs merged per row.</summary>
        public void Sprite(int x, int y, string[] rows, float z, Dictionary<char, int> keys, bool mirror = false)
        {
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            {
                string line = mirror ? Reverse(rows[r]) : rows[r];
                int yy = y + h - 1 - r;
                for (int c = 0; c < line.Length;)
                {
                    if (!keys.TryGetValue(line[c], out int texel)) { c++; continue; }
                    int end = c + 1;
                    while (end < line.Length && line[end] == line[c]) end++;
                    Rect(x + c, yy, end - c, 1, z, texel);
                    c = end;
                }
            }
        }

        static string Reverse(string s)
        {
            var a = s.ToCharArray();
            System.Array.Reverse(a);
            return new string(a);
        }

        /// <summary>1-px line (Bresenham).</summary>
        public void Line(int x0, int y0, int x1, int y1, float z, int texel)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sxs = x0 < x1 ? 1 : -1, sys = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                Rect(x0, y0, 1, 1, z, texel);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sxs; }
                if (e2 <= dx) { err += dx; y0 += sys; }
            }
        }
    }
}
