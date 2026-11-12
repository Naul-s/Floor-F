using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Night city around the hotel, built once as ONE palette mesh (one draw call): road with lane dashes, curb,
    /// sidewalk with lamp pools, street lamps, a near row of neighbouring buildings and a darker back row, every window
    /// a living light (<see cref="LivingLights"/>). Front faces are placed on whole world units of the screen plane, so
    /// with an integer pixels-per-unit frame (gameplay and title) every window is an exact pixel rectangle.
    /// Nothing here may overlap the hotel's screen area: buildings keep |screen x| &gt;= <see cref="HotelClearX"/>.
    /// </summary>
    sealed class CityView
    {
        /// <summary>Screen-plane x the neighbours keep clear of on each side (hotel + entrance annex).</summary>
        public const float HotelClearLeft = -9.6f;
        public const float HotelClearRight = 13f;

        public readonly struct Block
        {
            public readonly int X0, X1;
            public readonly float Height;
            public readonly float Z;
            public readonly Color Color;
            public readonly int Style;
            public Block(int x0, int x1, float height, float z, Color color, int style)
            {
                X0 = x0; X1 = x1; Height = height; Z = z; Color = color; Style = style;
            }
        }

        static readonly Color Brick = new Color(0.17f, 0.11f, 0.10f);
        static readonly Color Grey = new Color(0.12f, 0.13f, 0.16f);
        static readonly Color Olive = new Color(0.12f, 0.13f, 0.11f);
        static readonly Color Navy = new Color(0.09f, 0.10f, 0.15f);
        static readonly Color Far = new Color(0.065f, 0.07f, 0.115f);

        /// <summary>Near row (front z ~0.5-2) and back row (z ~10-12). Screen-plane x in whole units, height above ground.</summary>
        public static readonly Block[] Blocks =
        {
            new Block(-15, -10, 8f, 0.6f, Brick, 2),
            new Block(-23, -16, 13f, 1.4f, Grey, 0),
            new Block(-32, -24, 18f, 2.2f, Navy, 1),
            new Block(-41, -33, 11f, 1.0f, Olive, 0),
            new Block(-50, -42, 16f, 1.8f, Grey, 1),
            new Block(14, 19, 10f, 0.8f, Grey, 0),
            new Block(20, 27, 15f, 1.6f, Brick, 0),
            new Block(28, 37, 21f, 2.4f, Navy, 1),
            new Block(38, 44, 11f, 1.2f, Olive, 0),
            new Block(45, 53, 17f, 2.0f, Grey, 1),
            // back row (taller, darker, behind the gaps)
            new Block(-45, -34, 24f, 10f, Far, 1),
            new Block(-31, -20, 27f, 11f, Far, 0),
            new Block(-19, -11, 20f, 12f, Far, 1),
            new Block(12, 20, 23f, 12f, Far, 0),
            new Block(22, 31, 30f, 10.5f, Far, 1),
            new Block(34, 45, 25f, 11f, Far, 0),
        };

        /// <summary>Street lamps along the curb (screen-plane x): in the gaps between the shop signs, so no pole or lamp
        /// head ever crosses a sign's letters (title frame sign widths included).</summary>
        public static readonly float[] LampXs = { -41f, -32.5f, -23.5f, -15.5f, 19.5f, 27f, 36f, 44f };

        static readonly Color Asphalt = new Color(0.085f, 0.085f, 0.095f);
        static readonly Color Dash = new Color(0.36f, 0.33f, 0.22f);
        static readonly Color Curb = new Color(0.25f, 0.25f, 0.26f);
        static readonly Color Pavement = new Color(0.19f, 0.185f, 0.19f);
        static readonly Color Backlot = new Color(0.11f, 0.11f, 0.12f);
        static readonly Color PoleColor = new Color(0.16f, 0.17f, 0.19f);
        static readonly Color LampOn = new Color(1.00f, 0.88f, 0.60f);
        static readonly Color LampOff = new Color(0.32f, 0.28f, 0.22f);

        // lit windows stay under the lighting's strong range (PixelGlow.Strong, gamma 0.8): a calm facade, no halo per window
        static readonly Color[] WindowWarm = { new Color(0.78f, 0.62f, 0.34f), new Color(0.79f, 0.67f, 0.40f), new Color(0.70f, 0.78f, 0.92f) * 0.8f, new Color(0.42f, 0.55f, 0.95f) * 0.7f };

        readonly PaletteAtlas atlas;
        readonly LivingLights lights;
        readonly Vector2 shear;
        readonly float z0;

        public Transform Root { get; }
        public MeshRenderer Renderer { get; private set; }
        /// <summary>Screen-plane rectangles of the buildings (tests: no overlap with the hotel).</summary>
        public readonly List<Rect> BuildingRects = new List<Rect>();
        public int WindowCount { get; private set; }
        /// <summary>Shops, signs, rooftops, fire escapes... (null before Build).</summary>
        public CityDressing Dressing { get; private set; }
        /// <summary>Authored light pools: street lamps on the sidewalk, shop neon on the pavement (after Build).</summary>
        public readonly List<PixelGlow.LightPool> LightPools = new List<PixelGlow.LightPool>();
        readonly float pixelsPerUnit;
        readonly TexelLedger ledger = new TexelLedger();
        readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        MeshFilter filter;
        float hotelFrontZ;

        /// <summary>Pixels per unit the active city mesh was built for (one mesh per frame zoom, built on first use).</summary>
        public int ActivePixelsPerUnit { get; private set; }
        public int MeshVariants => meshes.Count;
        public Mesh ActiveMesh => filter != null ? filter.sharedMesh : null;

        /// <param name="pixelsPerUnit">Gameplay frame's pixels per unit (first build); other zooms rebuild on <see cref="ApplyPixelGrid"/>.</param>
        public CityView(Transform parent, PaletteAtlas atlas, LivingLights lights, Vector2 shear, float shearDepth, float pixelsPerUnit = 11f)
        {
            this.atlas = atlas;
            this.lights = lights;
            this.shear = shear;
            this.pixelsPerUnit = pixelsPerUnit;
            z0 = shearDepth;
            Root = new GameObject("City").transform;
            Root.SetParent(parent, false);
        }

        static int Key(float ppu) => Mathf.Max(1, Mathf.RoundToInt(ppu));

        /// <summary>
        /// The active frame's pixel grid (gameplay or zoomed-out title): the city mesh built for that many pixels per unit
        /// (1-px details and every neon font texel are whole pixels of THIS frame). Same palette texels and lights - the
        /// rebuild replays the first build's allocations - so switching is a mesh swap, no new lights.
        /// </summary>
        public void ApplyPixelGrid(PixelGrid g)
        {
            if (filter == null) return;
            int key = Key(g.PixelsPerUnit);
            if (key == ActivePixelsPerUnit) return;
            if (!meshes.TryGetValue(key, out var mesh))
            {
                mesh = BuildMesh(key, replay: true);
                meshes[key] = mesh;
            }
            filter.sharedMesh = mesh;
            ActivePixelsPerUnit = key;
        }

        /// <summary>Neon hum + sign stutters (juice clock + prewarm).</summary>
        public void Tick(float time)
        {
            Dressing?.Tick(time, RenderingStyle.CitySeed);
        }

        float WX(float screenX, float z) => PixelGrid.WorldX(screenX, z, shear, z0);
        float WY(float screenY, float z) => PixelGrid.WorldY(screenY, z, shear, z0);

        public void Build(float hotelFrontZ)
        {
            this.hotelFrontZ = hotelFrontZ;
            int key = Key(pixelsPerUnit);
            var mesh = BuildMesh(key, replay: false);
            meshes[key] = mesh;
            Renderer = PaletteRenderer.Create(Root, "City Mesh", mesh, atlas.Material);
            filter = Renderer.GetComponent<MeshFilter>();
            ActivePixelsPerUnit = key;
        }

        /// <summary>
        /// One city mesh for <paramref name="ppu"/> pixels per unit. The first build allocates the lights / neon texels
        /// and records them; a replay build gets the same texels back in the same order (nothing allocated, counted twice).
        /// </summary>
        Mesh BuildMesh(int ppu, bool replay)
        {
            if (replay) ledger.Replay();
            var b = new PaletteMeshBuilder(atlas);
            float px = 1f / ppu;
            float g = RenderingStyle.GroundY, road = RenderingStyle.RoadY, curb = RenderingStyle.CurbZ;
            float half = RenderingStyle.StreetHalfLength + 12f;

            // ground behind / under the buildings, sidewalk, curb, road (top faces + the curb's front face)
            b.Flat(-half, hotelFrontZ, half, 30f, g, atlas.Static(Backlot));
            b.Flat(-half, curb, half, hotelFrontZ, g, atlas.Static(Pavement));
            b.Front(-half, road, half, g, curb, atlas.Static(Curb));
            b.Flat(-half, curb - 0.18f, half, curb, g + 0.002f, atlas.Static(PaletteMeshBuilder.Top(Curb)));
            b.Flat(-half, -40f, half, curb, road, atlas.Static(Asphalt));
            float dashZ = (RenderingStyle.FarLaneZ + RenderingStyle.NearLaneZ) * 0.5f;
            int dash = atlas.Static(Dash);
            for (float x = -half; x < half; x += 4f) b.Flat(x, dashZ - 0.3f, x + 1.6f, dashZ + 0.3f, road + 0.004f, dash);

            // street lamps: pole, arm, lamp head (a living light); the pool on the sidewalk is a banded light (PixelGlow)
            int pole = atlas.Static(PoleColor);
            float lampZ = curb + 0.25f;
            foreach (float sx in LampXs)
            {
                float x = WX(sx, lampZ);
                b.Box(new Vector3(x - 0.06f, g, lampZ - 0.06f), new Vector3(x + 0.06f, g + 3.4f, lampZ + 0.06f), pole, pole, pole);
                b.Box(new Vector3(x - 0.06f, g + 3.3f, lampZ - 0.06f), new Vector3(x + 0.6f, g + 3.42f, lampZ + 0.06f), pole, pole, pole);
                int head = ledger.Take(() => lights.Add(LivingLights.Kind.Lamp, LampOn, LampOff));
                b.Box(new Vector3(x + 0.3f, g + 3.12f, lampZ - 0.12f), new Vector3(x + 0.72f, g + 3.3f, lampZ + 0.12f), head, pole, head);
                // warm pool under the head: the sidewalk, the curb and the shop fronts behind the lamp
                if (!replay) LightPools.Add(new PixelGlow.LightPool(new Vector3(x + 0.5f, g + 0.15f, lampZ), new Vector2(2.6f, 1.25f), LampPoolLight));
            }

            var dressing = new CityDressing(atlas, lights, ledger, WX, WY, ppu);
            if (!replay) Dressing = dressing;
            for (int i = 0; i < Blocks.Length; i++) BuildBlock(b, Blocks[i], i, dressing, px, replay);
            if (!replay) LightPools.AddRange(dressing.Spills);
            return b.Build("City " + ppu + "ppu");
        }

        /// <summary>
        /// The near row's windows exactly as <see cref="BuildBlock"/> lays them out: block index, screen-plane x / y of the
        /// window's bottom-left (whole units), width (units) and the window depth. Seasonal dressing (snow on the sills,
        /// candles) follows the same grid.
        /// </summary>
        public static IEnumerable<(int Block, int X, int Y, int W, float Z)> NearWindows()
        {
            float g = RenderingStyle.GroundY;
            for (int i = 0; i < Blocks.Length; i++)
            {
                var blk = Blocks[i];
                if (blk.Z > 6f) continue;
                var look = i < CityDressing.Looks.Length ? CityDressing.Looks[i] : default;
                float top = Mathf.Round(g + blk.Height);
                int winW = blk.Style == 1 ? 2 : 1, pitch = blk.Style == 1 ? 3 : 2;
                bool shop = look.Shop != CityDressing.Shop.None;
                int firstRow = !shop ? 1 : look.Shop == CityDressing.Shop.Lotto ? 5 : 3;
                for (int y = Mathf.CeilToInt(g) + firstRow; y + 1 <= top - 1; y += 2)
                    for (int x = blk.X0 + 1; x + winW <= blk.X1 - 1; x += pitch)
                        yield return (i, x, y, winW, blk.Z - 0.01f);
            }
        }

        /// <summary>Wall pattern of a block (by its colour): brick, concrete panels, glazed tiles, stone; far towers get floor lines.</summary>
        public static WallPattern PatternOf(in Block blk)
        {
            if (blk.Z > 6f) return WallPattern.Bands;
            if (blk.Color == Brick) return WallPattern.Brick;
            if (blk.Color == Grey) return WallPattern.Panel;
            if (blk.Color == Navy) return WallPattern.Tile;
            return WallPattern.Stone;
        }

        /// <summary>Light of a street lamp's pool (hue x intensity, see PixelGlow.LightPool).</summary>
        static readonly Color LampPoolLight = new Color(1.00f, 0.80f, 0.48f) * 0.45f;

        void BuildBlock(PaletteMeshBuilder b, in Block blk, int index, CityDressing dressing, float px, bool replay)
        {
            var look = index < CityDressing.Looks.Length ? CityDressing.Looks[index] : default;
            float g = RenderingStyle.GroundY;
            float z = blk.Z;
            float depth = blk.Z > 6f ? 6f : 4.5f;
            float top = Mathf.Round(g + blk.Height);
            // the front face sits on whole screen-plane units: x and y both (y: top edge; the bottom meets the ground)
            float x0 = WX(blk.X0, z), x1 = WX(blk.X1, z);
            float yTop = WY(top, z);
            // walls: top + side flat-shaded, the front face a pixel pattern (brick, panels, tiles...) in the block colour
            b.Box(new Vector3(x0, g, z), new Vector3(x1, yTop, z + depth), -1, atlas.Static(PaletteMeshBuilder.Top(blk.Color)), atlas.Static(PaletteMeshBuilder.Side(blk.Color)));
            b.PatternFront(blk.X0, g + shear.y * (z - z0), blk.X1, top, z, atlas.Tile(PatternOf(blk), blk.Color), new PixelGrid(1f / px, shear, z0));
            if (!replay) BuildingRects.Add(Rect.MinMaxRect(blk.X0, g + shear.y * (z - z0), blk.X1 + shear.x * depth, top + shear.y * depth));
            // roof cap strip (lighter) and a few rooftop boxes
            var cap = PaletteMeshBuilder.Top(blk.Color);
            b.Front(x0, yTop - 0.2f, x1, yTop, z - 0.01f, atlas.Static(cap));
            int seed = blk.X0 * 31 + blk.X1;
            if (look.Roof == CityDressing.Roof.None && LightSchedule.Hash(RenderingStyle.CitySeed, seed, 11, 0) < 0.6f)
            {
                float tx = Mathf.Lerp(x0 + 1f, x1 - 2f, LightSchedule.Hash(RenderingStyle.CitySeed, seed, 12, 0));
                b.Box(new Vector3(tx, yTop, z + 1f), new Vector3(tx + 1.2f, yTop + 1.1f, z + 2.2f), blk.Color * 1.15f);
            }

            bool back = blk.Z > 6f;
            float onRatio = back ? 0.28f : 0.36f;
            var off = back ? new Color(0.10f, 0.11f, 0.18f) : Color.Lerp(blk.Color, new Color(0.15f, 0.16f, 0.25f), 0.7f);
            float wz = z - 0.01f;
            int winW = blk.Style == 1 ? 2 : 1, pitch = blk.Style == 1 ? 3 : 2;
            bool shop = !back && look.Shop != CityDressing.Shop.None;
            int firstRow = !shop ? 1 : look.Shop == CityDressing.Shop.Lotto ? 5 : 3; // shop front (+ the 로또 neon) on the ground floor
            int sill = back ? -1 : atlas.Static(PaletteMeshBuilder.Top(blk.Color));
            int mullion = atlas.Static(blk.Color * 0.8f);
            for (int y = Mathf.CeilToInt(g) + firstRow; y + 1 <= top - 1; y += 2)
                for (int x = blk.X0 + 1; x + winW <= blk.X1 - 1; x += pitch)
                {
                    float hue = LightSchedule.Hash(RenderingStyle.CitySeed, x * 97 + y, 13, 0);
                    var on = WindowWarm[hue < 0.55f ? 0 : hue < 0.8f ? 1 : hue < 0.93f ? 2 : 3] * (back ? 0.8f : 1f);
                    // the blue ones are TVs: their light wavers softly while someone watches (LightSchedule.TvLevel)
                    var kind = hue >= 0.93f ? LivingLights.Kind.Tv : LivingLights.Kind.Window;
                    int texel = ledger.Take(() => lights.Add(kind, on, off, onRatio));
                    b.Front(WX(x, wz), WY(y, wz), WX(x + winW, wz), WY(y + 1, wz), wz, texel);
                    // near row: a 1-px sill under each window, a mullion splitting the wide ones
                    if (sill >= 0) b.Front(WX(x - px, wz - 0.002f), WY(y - px, wz - 0.002f), WX(x + winW + px, wz - 0.002f), WY(y, wz - 0.002f), wz - 0.002f, sill);
                    if (winW == 2 && !back) b.Front(WX(x + 1, wz - 0.002f), WY(y, wz - 0.002f), WX(x + 1 + px, wz - 0.002f), WY(y + 1, wz - 0.002f), wz - 0.002f, mullion);
                    if (!replay) WindowCount++;
                }
            dressing.Dress(b, blk, look, g, top, depth, index);
        }
    }

    /// <summary>
    /// Records the palette texels a build allocates (lights, neon) so a rebuild of the same geometry at another zoom gets
    /// exactly the same texels back in the same order instead of allocating new ones.
    /// </summary>
    sealed class TexelLedger
    {
        readonly List<int> ids = new List<int>();
        int cursor = -1;

        public bool Replaying => cursor >= 0;
        public int Count => ids.Count;

        public int Take(System.Func<int> allocate)
        {
            if (cursor < 0)
            {
                int t = allocate();
                ids.Add(t);
                return t;
            }
            if (cursor >= ids.Count) throw new System.InvalidOperationException("rebuild allocates more texels than the first build");
            return ids[cursor++];
        }

        /// <summary>Next Take calls return the recorded texels from the start.</summary>
        public void Replay() => cursor = 0;
    }
}
