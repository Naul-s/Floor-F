using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Exterior dressing of the hotel (all static, merged): stone plinth down to the street, corner pilasters with floor
    /// bands, a cornice, rooftop props (water tank, AC units, stair hut, antenna with a beacon), the entrance annex on
    /// the right with a glass door, wall lamps and a striped canopy, sidewalk planters/bench/bin, and the HOTEL sign's
    /// frame with marquee bulbs. Lit parts = one mesh per material; self-lit parts = one palette mesh whose lamps are
    /// living lights. Nothing covers the floors' cut-away (screen x in [-w/2, w/2], y in [0, roof slab]).
    /// </summary>
    sealed class HotelFacade
    {
        static readonly Color Stone = new Color(0.34f, 0.31f, 0.30f);
        static readonly Color StoneDark = new Color(0.22f, 0.20f, 0.20f);
        static readonly Color Trim = new Color(0.56f, 0.48f, 0.38f);
        static readonly Color Pilaster = new Color(0.36f, 0.25f, 0.22f);
        static readonly Color Metal = new Color(0.22f, 0.22f, 0.25f);
        static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
        static readonly Color AcGrey = new Color(0.46f, 0.47f, 0.49f);
        static readonly Color Awning = new Color(0.62f, 0.13f, 0.12f);
        static readonly Color AwningLight = new Color(0.86f, 0.80f, 0.68f);

        /// <summary>Gap kept between dressing and the cut-away (pilaster inner faces sit at |x| = w/2 + this).</summary>
        public const float CutawayClearance = 0.1f;

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly PaletteAtlas atlas;
        readonly LivingLights lights;

        public Transform Root { get; }
        public readonly List<Renderer> Renderers = new List<Renderer>();
        /// <summary>Screen-plane x of the entrance door centre (pedestrians may turn in here).</summary>
        public float DoorX { get; private set; }
        public int BulbCount { get; private set; }

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

        public void Build()
        {
            float w = layout.FloorWidth, d = layout.Depth, sx = layout.ShaftX, H = layout.FloorCount * layout.FloorHeight;
            float left = sx - w * 0.5f, right = sx + w * 0.5f;
            float wall = 0.3f, front = -d * 0.5f, g = RenderingStyle.GroundY;
            var lit = new LitBatch(assets);
            var self = new PaletteMeshBuilder(atlas);
            var stone = assets.Lit(Stone);
            var stoneDark = assets.Lit(StoneDark);
            var trim = assets.Lit(Trim);
            var pil = assets.Lit(Pilaster);
            var metal = assets.Lit(Metal);

            // ---- plinth: the hotel stands on stone from the sidewalk up to the lobby floor (y = 0)
            float px0 = left - CutawayClearance - 0.72f, px1 = right + CutawayClearance + 0.72f;
            lit.BoxMinMax(new Vector3(px0, g, front - 0.25f), new Vector3(px1, 0f, d * 0.5f + wall * 0.5f), stone);
            lit.BoxMinMax(new Vector3(px0 - 0.05f, g, front - 0.32f), new Vector3(px1 + 0.05f, g + 0.35f, front - 0.25f), stoneDark);
            lit.BoxMinMax(new Vector3(px0 - 0.05f, -0.16f, front - 0.3f), new Vector3(px1 + 0.05f, 0f, front - 0.2f), trim);
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

            // ---- corner pilasters with floor bands, capitals and bases
            float pin = CutawayClearance, pw = 0.62f, pz = front - 0.3f;
            for (int side = 0; side < 2; side++)
            {
                float a = side == 0 ? left - pin - pw : right + pin;
                float b = a + pw;
                lit.BoxMinMax(new Vector3(a, 0f, pz), new Vector3(b, H, d * 0.5f + wall * 0.5f), pil);
                for (int f = 1; f < layout.FloorCount; f++)
                {
                    float y = f * layout.FloorHeight;
                    lit.BoxMinMax(new Vector3(side == 0 ? a - 0.06f : a, y - 0.3f, pz - 0.1f), new Vector3(side == 0 ? b : b + 0.06f, y, pz + 0.4f), trim);
                }
                // bands / base only grow outward: the inner face stays clear of the cut-away
                lit.BoxMinMax(new Vector3(side == 0 ? a - 0.1f : a, 0f, pz - 0.12f), new Vector3(side == 0 ? b : b + 0.1f, 0.45f, pz + 0.4f), stoneDark);
            }

            // ---- cornice (sits on the roof slab, so it never covers floor 9)
            lit.BoxMinMax(new Vector3(left - wall - 0.55f, H, front - 0.45f), new Vector3(right + wall + 0.55f, H + 0.35f, d * 0.5f + 0.25f), trim);
            lit.BoxMinMax(new Vector3(left - wall - 0.7f, H + 0.35f, front - 0.55f), new Vector3(right + wall + 0.7f, H + 0.55f, d * 0.5f + 0.35f), metal);
            float roof = H + 0.55f;

            // ---- roof: AC units (left), antenna with beacon (far left), water tank + stair hut (right)
            var ac = assets.Lit(AcGrey);
            int grille = atlas.Static(new Color(0.12f, 0.12f, 0.14f));
            for (int i = 0; i < 2; i++)
            {
                float ax = left + 1.6f + i * 1.6f;
                lit.BoxMinMax(new Vector3(ax, roof, -0.8f), new Vector3(ax + 1.2f, roof + 0.7f, 0.5f), ac);
                self.Front(ax + 0.25f, roof + 0.12f, ax + 0.95f, roof + 0.58f, -0.81f, grille);
            }
            float mx = left + 0.7f;
            lit.BoxMinMax(new Vector3(mx, roof, 0.9f), new Vector3(mx + 0.12f, roof + 1.85f, 1.02f), metal);
            lit.BoxMinMax(new Vector3(mx - 0.25f, roof + 1.2f, 0.9f), new Vector3(mx + 0.37f, roof + 1.28f, 1.02f), metal);
            int beacon = lights.Add(LivingLights.Kind.Beacon, new Color(1f, 0.22f, 0.18f), new Color(0.32f, 0.08f, 0.07f));
            self.Box(new Vector3(mx - 0.05f, roof + 1.85f, 0.88f), new Vector3(mx + 0.17f, roof + 2.03f, 1.04f), beacon, beacon, beacon);

            var wood = assets.Lit(Wood);
            float tx = right - 4.4f;
            for (int i = 0; i < 2; i++)
                lit.BoxMinMax(new Vector3(tx + 0.15f + i * 1.15f, roof, 0.4f), new Vector3(tx + 0.3f + i * 1.15f, roof + 0.55f, 0.55f), metal);
            lit.BoxMinMax(new Vector3(tx, roof + 0.55f, 0.2f), new Vector3(tx + 1.6f, roof + 1.55f, 1.6f), wood);
            lit.BoxMinMax(new Vector3(tx - 0.05f, roof + 0.85f, 0.15f), new Vector3(tx + 1.65f, roof + 0.93f, 1.65f), metal);
            lit.BoxMinMax(new Vector3(tx + 0.2f, roof + 1.55f, 0.4f), new Vector3(tx + 1.4f, roof + 1.75f, 1.4f), metal);
            float hx = right - 2.2f;
            lit.BoxMinMax(new Vector3(hx, roof, 0.4f), new Vector3(hx + 1.7f, roof + 1.3f, 1.9f), stone);
            lit.BoxMinMax(new Vector3(hx - 0.08f, roof + 1.3f, 0.32f), new Vector3(hx + 1.78f, roof + 1.42f, 1.98f), metal);
            self.Front(hx + 0.3f, roof, hx + 0.9f, roof + 1.05f, 0.39f, atlas.Static(new Color(0.10f, 0.07f, 0.06f)));
            int hutLamp = lights.Add(LivingLights.Kind.Lamp, new Color(1f, 0.85f, 0.55f), new Color(0.3f, 0.25f, 0.18f));
            self.Front(hx + 1.15f, roof + 0.95f, hx + 1.4f, roof + 1.15f, 0.39f, hutLamp);

            // ---- entrance annex (right of the hotel): glass door, lamps, striped canopy over the sidewalk
            float ex0 = right + pin + pw, ex1 = ex0 + 4.2f, ez = front + 0.3f, eTop = g + 3.2f;
            lit.BoxMinMax(new Vector3(ex0, g, ez), new Vector3(ex1, eTop, d * 0.5f - 0.5f), stone);
            lit.BoxMinMax(new Vector3(ex0 - 0.05f, eTop, ez - 0.15f), new Vector3(ex1 + 0.1f, eTop + 0.25f, d * 0.5f - 0.4f), trim);
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

            // ---- HOTEL sign: dark frame + two interleaved sets of marquee bulbs (children of the sign assembly)
            if (building.HotelSignRoot != null)
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
                var mesh = sign.Build("Hotel Sign Frame");
                var r = PaletteRenderer.Create(building.HotelSignRoot, "Sign Frame", mesh, atlas.Material);
                Renderers.Add(r);
            }

            var selfMesh = self.Build("Facade Lights");
            Renderers.Add(PaletteRenderer.Create(Root, "Facade Lights", selfMesh, atlas.Material));
            foreach (var r in lit.Build(Root, "Facade", castShadows: true)) Renderers.Add(r);
        }
    }
}
