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

        /// <summary>Street lamps along the curb (screen-plane x).</summary>
        public static readonly int[] LampXs = { -45, -33, -19, 17, 30, 42 };

        static readonly Color Asphalt = new Color(0.085f, 0.085f, 0.095f);
        static readonly Color Dash = new Color(0.36f, 0.33f, 0.22f);
        static readonly Color Curb = new Color(0.25f, 0.25f, 0.26f);
        static readonly Color Pavement = new Color(0.19f, 0.185f, 0.19f);
        static readonly Color LampPool = new Color(0.29f, 0.26f, 0.21f);
        static readonly Color Backlot = new Color(0.11f, 0.11f, 0.12f);
        static readonly Color PoleColor = new Color(0.16f, 0.17f, 0.19f);
        static readonly Color LampOn = new Color(1.00f, 0.88f, 0.60f);
        static readonly Color LampOff = new Color(0.32f, 0.28f, 0.22f);

        static readonly Color[] WindowWarm = { new Color(0.82f, 0.65f, 0.36f), new Color(0.88f, 0.74f, 0.44f), new Color(0.70f, 0.78f, 0.92f) * 0.8f, new Color(0.42f, 0.55f, 0.95f) * 0.7f };

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
        readonly float pixelsPerUnit;

        /// <param name="pixelsPerUnit">Gameplay frame's pixels per unit: neon letters are whole pixels of it.</param>
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

        /// <summary>Neon hum + sign stutters (juice clock + prewarm).</summary>
        public void Tick(float time)
        {
            Dressing?.Tick(time, RenderingStyle.CitySeed);
        }

        float WX(float screenX, float z) => PixelGrid.WorldX(screenX, z, shear, z0);
        float WY(float screenY, float z) => PixelGrid.WorldY(screenY, z, shear, z0);

        public void Build(float hotelFrontZ)
        {
            var b = new PaletteMeshBuilder(atlas);
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

            // street lamps: pool of light on the pavement, pole, arm, lamp head (a living light)
            int pole = atlas.Static(PoleColor);
            float lampZ = curb + 0.25f;
            foreach (int sx in LampXs)
            {
                float x = WX(sx, lampZ);
                b.Flat(x - 1.3f, curb + 0.02f, x + 1.3f, hotelFrontZ - 0.05f, g + 0.003f, atlas.Static(LampPool));
                b.Box(new Vector3(x - 0.06f, g, lampZ - 0.06f), new Vector3(x + 0.06f, g + 3.4f, lampZ + 0.06f), pole, pole, pole);
                b.Box(new Vector3(x - 0.06f, g + 3.3f, lampZ - 0.06f), new Vector3(x + 0.6f, g + 3.42f, lampZ + 0.06f), pole, pole, pole);
                int head = lights.Add(LivingLights.Kind.Lamp, LampOn, LampOff);
                b.Box(new Vector3(x + 0.3f, g + 3.12f, lampZ - 0.12f), new Vector3(x + 0.72f, g + 3.3f, lampZ + 0.12f), head, pole, head);
            }

            Dressing = new CityDressing(atlas, lights, WX, WY, pixelsPerUnit);
            for (int i = 0; i < Blocks.Length; i++) BuildBlock(b, Blocks[i], i);

            var mesh = b.Build("City");
            Renderer = PaletteRenderer.Create(Root, "City Mesh", mesh, atlas.Material);
        }

        void BuildBlock(PaletteMeshBuilder b, in Block blk, int index)
        {
            var look = index < CityDressing.Looks.Length ? CityDressing.Looks[index] : default;
            float g = RenderingStyle.GroundY;
            float z = blk.Z;
            float depth = blk.Z > 6f ? 6f : 4.5f;
            float top = Mathf.Round(g + blk.Height);
            // the front face sits on whole screen-plane units: x and y both (y: top edge; the bottom meets the ground)
            float x0 = WX(blk.X0, z), x1 = WX(blk.X1, z);
            float yTop = WY(top, z);
            b.Box(new Vector3(x0, g, z), new Vector3(x1, yTop, z + depth), blk.Color);
            BuildingRects.Add(Rect.MinMaxRect(blk.X0, g + shear.y * (z - z0), blk.X1 + shear.x * depth, top + shear.y * depth));
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
            float px = 1f / Mathf.Max(1f, pixelsPerUnit);
            for (int y = Mathf.CeilToInt(g) + firstRow; y + 1 <= top - 1; y += 2)
                for (int x = blk.X0 + 1; x + winW <= blk.X1 - 1; x += pitch)
                {
                    float hue = LightSchedule.Hash(RenderingStyle.CitySeed, x * 97 + y, 13, 0);
                    var on = WindowWarm[hue < 0.55f ? 0 : hue < 0.8f ? 1 : hue < 0.93f ? 2 : 3] * (back ? 0.8f : 1f);
                    int texel = lights.Add(LivingLights.Kind.Window, on, off, onRatio);
                    b.Front(WX(x, wz), WY(y, wz), WX(x + winW, wz), WY(y + 1, wz), wz, texel);
                    // near row: a 1-px sill under each window, a mullion splitting the wide ones
                    if (sill >= 0) b.Front(WX(x - px, wz - 0.002f), WY(y - px, wz - 0.002f), WX(x + winW + px, wz - 0.002f), WY(y, wz - 0.002f), wz - 0.002f, sill);
                    if (winW == 2 && !back) b.Front(WX(x + 1 - px * 0.5f, wz - 0.002f), WY(y, wz - 0.002f), WX(x + 1 + px * 0.5f, wz - 0.002f), WY(y + 1, wz - 0.002f), wz - 0.002f, mullion);
                    WindowCount++;
                }
            Dressing.Dress(b, blk, look, g, top, depth, index);
        }
    }
}
