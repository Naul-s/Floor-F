using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Winter season (ISeasonQuery: IsExtendedDay, SnowAmount; days 8-10):
    ///   Snowfall   (1 additive palette mesh in viewport pixels, like the rain): chunky 1-px / 2x2 flakes swaying on a
    ///              light wind, count proportional to SnowAmount (<= <see cref="WinterSchedule.MaxFlakes"/> quads);
    ///              over the hotel they are drawn at half strength so the floors stay readable.
    ///   Cover      (world palette meshes, built on first use): heavy (SnowAmount >= 0.7, Christmas Eve) = snow caps on the
    ///              parapet, piers, cornice lip, every floor band of the pilasters, the annex, canopy, roof props, lamp
    ///              heads, the neighbours' roofs, the HOTEL sign, a snowy sidewalk with trampled lanes and banks along the
    ///              curb + icicles (one pixel-exact mesh per frame zoom); dust (first snow 0.3) = thin top caps only.
    ///   Mood       (<see cref="Mood"/> 0..1, eased): colder ambient / moonlight / sky while the season runs - the warm
    ///              hotel floors and windows read against it (applied by RenderingRoot).
    ///   Sleigh     (endings): Santa's sleigh + two reindeer crossing the moon (viewport pixels behind the city).
    /// Nothing is drawn and nothing is built while there is no season service (or it reports no snow / not winter).
    /// Snow colours stay below the pixel glow's weak threshold (max channel &lt; 0.7): white without halos.
    /// </summary>
    sealed class WinterView
    {
        public static readonly Color SnowTop = new Color(0.64f, 0.66f, 0.69f);
        public static readonly Color SnowFront = new Color(0.52f, 0.55f, 0.63f);
        public static readonly Color SnowShade = new Color(0.38f, 0.42f, 0.52f);
        static readonly Color Slush = new Color(0.27f, 0.29f, 0.34f);
        static readonly Color Ice = new Color(0.50f, 0.62f, 0.69f);
        static readonly Color IceTip = new Color(0.36f, 0.46f, 0.56f);
        static readonly Color FlakeColor = new Color(0.72f, 0.74f, 0.78f);
        /// <summary>Winter mood targets (Mood = 1).</summary>
        public static readonly Color WinterAmbient = new Color(0.22f, 0.25f, 0.36f);
        public static readonly Color WinterMoon = new Color(0.52f, 0.66f, 1.00f);
        public static readonly Color WinterSky = new Color(0.050f, 0.066f, 0.125f);
        public const float HotelSnowFactor = 0.5f;
        public const float SleighDepth = SkyView.StarDepth - 0.4f;
        const int RampSteps = 6;

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly HotelFacade facade;
        readonly int seed;
        readonly PixelQuads snowQuads = new PixelQuads();
        readonly PixelQuads sleighQuads = new PixelQuads();
        readonly Dictionary<int, Mesh> icicleMeshes = new Dictionary<int, Mesh>();
        int[] snowRamp;
        Mesh snowMesh, sleighMesh;
        MeshFilter icicleFilter;
        Transform snowLayer, sleighLayer;
        int icicleKey;
        float sleighClock;

        public PaletteAtlas Atlas { get; }
        public Transform Root { get; }
        public MeshRenderer SnowRenderer { get; private set; }
        public MeshRenderer HeavyRenderer { get; private set; }
        public MeshRenderer DustRenderer { get; private set; }
        public MeshRenderer SignCapRenderer { get; private set; }
        public MeshRenderer IcicleRenderer { get; private set; }
        public MeshRenderer SleighRenderer { get; private set; }

        /// <summary>Tests: force the snow amount (null = follow ISeasonQuery.SnowAmount).</summary>
        public float? ForceSnow { get; set; }
        /// <summary>Tests: force the winter mood (null = follow ISeasonQuery.IsExtendedDay).</summary>
        public bool? ForceWinter { get; set; }
        /// <summary>Ending override: snow amount while a winter ending shows (null = none).</summary>
        public float? EndingSnow { get; set; }
        /// <summary>Silent Night: slower, calmer flakes (0..1).</summary>
        public float Calm { get; set; }
        /// <summary>Winter Miracle: Santa's sleigh flies across the moon.</summary>
        public bool SleighFlight { get; set; }

        public float SnowAmount { get; private set; }
        public bool Snowing => SnowAmount > 0.001f;
        public bool HeavyCover => HeavyRenderer != null && HeavyRenderer.enabled;
        public bool DustCover => DustRenderer != null && DustRenderer.enabled;
        public int FlakeQuadCount => snowQuads.Count;
        public int SleighQuadCount => sleighQuads.Count;
        public bool SleighVisible => SleighRenderer != null && SleighRenderer.enabled;
        public Vector2 SleighPixels { get; private set; }
        /// <summary>Eased 0..1 winter colour grading (1 = full winter).</summary>
        public float Mood { get; private set; }
        public bool Winter { get; private set; }
        public Rect HotelPixels { get; private set; }

        public WinterView(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, HotelFacade facade, int seed)
        {
            this.assets = assets;
            this.layout = layout;
            this.building = building;
            this.facade = facade;
            this.seed = seed;
            Root = new GameObject("Winter").transform;
            Root.SetParent(parent, false);
            Atlas = new PaletteAtlas(assets, "Winter Palette");
        }

        public void Build()
        {
            var additive = assets.NewAdditive(Atlas.Texture, "Snow (additive)");
            var from = additive != null ? Color.black : RenderingStyle.Background;
            snowRamp = new int[RampSteps];
            for (int i = 0; i < RampSteps; i++) snowRamp[i] = Atlas.Static(Color.Lerp(from, FlakeColor, i / (RampSteps - 1f)));
            snowMesh = assets.Own(new Mesh { name = "Snowfall" });
            snowMesh.MarkDynamic();
            SnowRenderer = PaletteRenderer.Create(Root, "Snowfall", snowMesh, additive != null ? additive : Atlas.Material);
            SnowRenderer.allowOcclusionWhenDynamic = false;
            snowLayer = SnowRenderer.transform;
            SnowRenderer.enabled = false;

            sleighMesh = assets.Own(new Mesh { name = "Sleigh Flight" });
            sleighMesh.MarkDynamic();
            SleighRenderer = PaletteRenderer.Create(Root, "Sleigh Flight", sleighMesh, Atlas.Material);
            SleighRenderer.allowOcclusionWhenDynamic = false;
            sleighLayer = SleighRenderer.transform;
            SleighRenderer.enabled = false;
            SleighKeys();
            Atlas.Flush();
        }

        // ------------------------------------------------------------------ per frame

        /// <param name="time">Ambient clock (+ prewarm): snow freezes with the pause like the rain.</param>
        /// <param name="endingDt">Juice clock step of the ending animation (sleigh).</param>
        public void Tick(float time, float endingDt, CameraFrame frame, ISeasonQuery season, bool raining)
        {
            if (snowMesh == null) return;
            bool winter = ForceWinter ?? (season != null && season.IsExtendedDay);
            float amount = EndingSnow ?? ForceSnow ?? (season != null ? season.SnowAmount : 0f);
            if (raining) amount = 0f;
            Winter = winter || amount > 0f;
            SnowAmount = Mathf.Clamp01(amount);
            float moodTarget = Winter ? 1f : 0f;
            Mood = Mathf.MoveTowards(Mood, moodTarget, Mathf.Max(0f, Time.unscaledDeltaTime) * 0.8f);

            UpdateCover(frame);
            UpdateSnow(time, frame);
            UpdateSleigh(endingDt, frame);
            Atlas.Flush();
        }

        /// <summary>Mood applied immediately (no easing) - first frame / tests.</summary>
        public void SnapMood() => Mood = Winter ? 1f : 0f;

        public static Color MoodAmbient(float mood) => Color.Lerp(RenderingStyle.Ambient, WinterAmbient, mood);
        public static Color MoodMoon(float mood) => Color.Lerp(RenderingStyle.MoonColor, WinterMoon, mood);
        public static Color MoodSky(float mood) => Color.Lerp(RenderingStyle.Background, WinterSky, mood);

        void UpdateSnow(float time, CameraFrame frame)
        {
            int count = WinterSchedule.FlakeCount(SnowAmount);
            if (count == 0)
            {
                if (snowQuads.Count > 0) { snowQuads.Clear(); snowQuads.Apply(snowMesh); }
                SnowRenderer.enabled = false;
                return;
            }
            Place(snowLayer, frame, SkyView.FxDepth);
            HotelPixels = HotelRect(frame);
            var hotel = HotelPixels;
            snowQuads.Clear();
            for (int i = 0; i < count; i++)
            {
                var f = WinterSchedule.SnowFlake(seed, i, time, Calm);
                int x = Mathf.FloorToInt(f.Pos.x), y = Mathf.FloorToInt(f.Pos.y);
                bool overHotel = hotel.Contains(f.Pos);
                float level = f.Level * (overHotel ? HotelSnowFactor : 1f);
                int s = overHotel ? 1 : f.Size;
                int r = Mathf.Clamp(Mathf.RoundToInt(level * (RampSteps - 1)), 0, RampSteps - 1);
                if (r <= 0 || x + s <= 0 || y + s <= 0 || x >= StarField.Width || y >= StarField.Height) continue;
                snowQuads.Rect(x, y, s, s, Atlas.UV(snowRamp[r]));
            }
            snowQuads.Apply(snowMesh);
            SnowRenderer.enabled = snowQuads.Count > 0;
        }

        static void Place(Transform layer, CameraFrame f, float z)
        {
            layer.position = SkyView.LayerOrigin(f, z);
            float s = 1f / f.PixelsPerUnit;
            layer.localScale = new Vector3(s, s, 1f);
        }

        Rect HotelRect(CameraFrame f)
        {
            var b = BuildingView.FramingBounds(layout);
            var lo = SkyView.WorldToPixels(f, new Vector3(b.min.x, b.min.y, b.min.z));
            var hi = SkyView.WorldToPixels(f, new Vector3(b.max.x, b.max.y, b.min.z));
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        // ------------------------------------------------------------------ cover

        void UpdateCover(CameraFrame frame)
        {
            bool heavy = SnowAmount >= WinterSchedule.HeavyCover;
            bool dust = !heavy && SnowAmount >= WinterSchedule.DustCover;
            if (heavy && HeavyRenderer == null) BuildHeavy();
            if (dust && DustRenderer == null) BuildDust();
            if (HeavyRenderer != null) HeavyRenderer.enabled = heavy;
            if (SignCapRenderer != null) SignCapRenderer.enabled = heavy;
            if (DustRenderer != null) DustRenderer.enabled = dust;
            if (heavy) ApplyIcicles(frame.Grid);
            if (IcicleRenderer != null) IcicleRenderer.enabled = heavy;
        }

        int snowTop, snowFront, snowShade, slush;

        void Colours()
        {
            snowTop = Atlas.Static(SnowTop);
            snowFront = Atlas.Static(SnowFront);
            snowShade = Atlas.Static(SnowShade);
            slush = Atlas.Static(Slush);
        }

        /// <summary>Snow cap on top of a box whose top is at <paramref name="y"/>: overhangs by <paramref name="lip"/>.</summary>
        void Cap(PaletteMeshBuilder b, float x0, float x1, float y, float z0, float z1, float thick, float lip = 0.03f)
            => b.Box(new Vector3(x0 - lip, y, z0 - lip), new Vector3(x1 + lip, y + thick, z1 + lip), snowFront, snowTop, snowShade);

        /// <summary>Thin top-only dusting.</summary>
        void Dust(PaletteMeshBuilder b, float x0, float x1, float y, float z0, float z1)
            => b.Flat(x0, z0, x1, z1, y + 0.012f, snowTop);

        void BuildHeavy()
        {
            Colours();
            var b = new PaletteMeshBuilder(Atlas);
            HotelCaps(b, heavy: true);
            StreetSnow(b, heavy: true);
            NeighbourRoofs(b, heavy: true);
            HeavyRenderer = PaletteRenderer.Create(Root, "Snow Cover", assets.Own(b.Build("Snow Cover")), Atlas.Material);
            BuildSignCap();
        }

        void BuildDust()
        {
            Colours();
            var b = new PaletteMeshBuilder(Atlas);
            HotelCaps(b, heavy: false);
            StreetSnow(b, heavy: false);
            NeighbourRoofs(b, heavy: false);
            DustRenderer = PaletteRenderer.Create(Root, "Snow Dust", assets.Own(b.Build("Snow Dust")), Atlas.Material);
        }

        void HotelCaps(PaletteMeshBuilder b, bool heavy)
        {
            if (facade == null) return;
            float sx = layout.ShaftX, roof = facade.RoofY, front = facade.FrontZ;
            float pz = facade.ParapetZ, pzBack = facade.ParapetBackZ;
            float cx0 = facade.CorniceLeft, cx1 = facade.CorniceRight;
            float t = heavy ? 0.13f : 0f;
            void Top(float x0, float x1, float y, float z0, float z1, float thick)
            {
                if (heavy) Cap(b, x0, x1, y, z0, z1, thick);
                else Dust(b, x0, x1, y, z0, z1);
            }

            // parapet: coping tops of the full walls, the low steps beside the sign and the corner piers + finials
            for (int side = 0; side < 2; side++)
            {
                float m = side == 0 ? 1f : -1f;
                float o0 = sx + m * (cx0 - sx), o1 = sx + m * (facade.StepOutX - sx), i1 = sx + m * (facade.StepInX - sx);
                float p0 = sx + m * (cx0 + 0.6f - sx);
                Top(Mathf.Min(p0, o1) - 0.04f, Mathf.Max(p0, o1) + 0.04f, facade.ParapetTop + 0.07f, pz - 0.04f, pzBack + 0.04f, t);
                Top(Mathf.Min(o1, i1) - 0.04f, Mathf.Max(o1, i1) + 0.04f, facade.StepTop + 0.07f, pz - 0.04f, pzBack + 0.04f, t);
                float a = Mathf.Min(o0, p0), c = Mathf.Max(o0, p0);
                Top(a - 0.04f, c + 0.04f, facade.PierTop + 0.07f, pz - 0.09f, pzBack + 0.09f, t);
                float fc = (a + c) * 0.5f;
                Top(fc - 0.12f, fc + 0.12f, facade.PierTop + 0.27f, pz + 0.02f, pz + 0.2f, heavy ? 0.09f : 0f);
            }
            // cornice lip in front of the parapet
            Top(cx0 - 0.15f, cx1 + 0.15f, roof, front - 0.55f, pz - 0.04f, heavy ? 0.08f : 0f);
            if (!heavy)
            {
                Dust(b, facade.AnnexLeft - 0.05f, facade.AnnexRight + 0.1f, facade.AnnexTop, facade.AnnexZ - 0.15f, layout.Depth * 0.5f - 0.4f);
                Dust(b, facade.CanopyLeft, facade.CanopyRight, facade.CanopyTop, facade.CanopyFrontZ, facade.AnnexZ);
                return;
            }
            // the roof deck (seen between the props), roof props
            b.Flat(cx0 + 0.1f, front - 0.2f, cx1 - 0.1f, layout.Depth * 0.5f + 0.25f, roof + 0.03f, snowTop);
            float chimX = cx0 + 2.1f, ac0 = chimX + 0.75f;
            Cap(b, chimX - 0.06f, chimX + 0.56f, roof + 1.15f, 0.94f, 1.56f, 0.1f);
            Cap(b, ac0 + 0.1f, ac0 + 1.05f, roof + 0.74f, -0.6f, 0.25f, 0.1f);
            Cap(b, facade.HutLeft - 0.08f, facade.HutRight + 0.08f, facade.HutTop, 0.32f, 1.98f, 0.14f);
            const float tz = 0.5f, coneStep = 0.24f;
            Cap(b, facade.TowerLeft - 0.06f + 2 * coneStep, facade.TowerRight + 0.06f - 2 * coneStep, facade.TowerTop, tz - 0.06f + 0.4f, tz + 1.26f - 0.4f, 0.08f);
            // pilasters: every floor band (window ledges of the facade) and the capitals
            float pw = HotelFacade.PilasterWidth;
            float rightIn = layout.ShaftX + layout.FloorWidth * 0.5f + HotelFacade.CutawayClearance;
            float ppz = facade.PilasterZ;
            for (int f = 1; f < layout.FloorCount; f++)
            {
                float y = f * layout.FloorHeight;
                Cap(b, facade.PilasterLeft - 0.08f, facade.PilasterLeft + pw, y, ppz - 0.1f, ppz + 0.4f, 0.06f, 0.01f);
                Cap(b, rightIn, facade.PilasterRight + 0.08f, y, ppz - 0.1f, ppz + 0.4f, 0.06f, 0.01f);
            }
            // entrance annex roof, its AC box, the striped canopy
            Cap(b, facade.AnnexLeft - 0.05f, facade.AnnexRight + 0.1f, facade.AnnexTop, facade.AnnexZ - 0.15f, layout.Depth * 0.5f - 0.4f, 0.14f);
            Cap(b, facade.AnnexRight - 1.7f, facade.AnnexRight - 0.7f, facade.AnnexTop + 0.55f, facade.AnnexZ + 0.6f, facade.AnnexZ + 1.5f, 0.08f);
            Cap(b, facade.CanopyLeft, facade.CanopyRight, facade.CanopyTop, facade.CanopyFrontZ, facade.AnnexZ, 0.1f);
            // plinth cap (front ledge at the lobby floor)
            Cap(b, facade.PilasterLeft - 0.15f, facade.PilasterRight + 0.15f, -0.045f, front - 0.3f, front - 0.2f, 0.04f, 0f);
        }

        void StreetSnow(PaletteMeshBuilder b, bool heavy)
        {
            float g = RenderingStyle.GroundY, road = RenderingStyle.RoadY, curb = RenderingStyle.CurbZ;
            float half = RenderingStyle.StreetHalfLength + 12f;
            float hotelFront = -layout.Depth * 0.5f - 0.15f;
            if (!heavy)
            {
                // first snow: a white curb and loose patches along the walls and the curb
                b.Flat(-half, curb - 0.18f, half, curb, g + 0.006f, snowShade);
                for (int i = 0; i < 70; i++)
                {
                    float x = Mathf.Lerp(-half, half, LightSchedule.Hash(seed, i, 220, 0));
                    bool wall = LightSchedule.Hash(seed, i, 221, 0) < 0.5f;
                    float z = wall ? hotelFront - 0.05f - 0.3f * LightSchedule.Hash(seed, i, 222, 0) : curb + 0.05f + 0.25f * LightSchedule.Hash(seed, i, 222, 0);
                    float w = Mathf.Lerp(0.3f, 1.4f, LightSchedule.Hash(seed, i, 223, 0));
                    b.Flat(x, z - 0.12f, x + w, z + 0.12f, g + 0.005f, LightSchedule.Hash(seed, i, 224, 0) < 0.5f ? snowShade : snowTop);
                }
                return;
            }
            // sidewalk under snow, two trampled lanes where the pedestrians walk
            b.Flat(-half, curb, half, hotelFront, g + 0.004f, snowTop);
            foreach (float lane in RenderingStyle.WalkLanes)
                b.Flat(-half, lane - 0.16f, half, lane + 0.16f, g + 0.006f, Atlas.Static(Color.Lerp(SnowShade, Slush, 0.35f)));
            // curb: snow on top and down its face, a bank along the gutter with a ragged edge
            b.Flat(-half, curb - 0.18f, half, curb, g + 0.006f, snowTop);
            b.Front(-half, road + 0.05f, half, g + 0.006f, curb - 0.004f, snowFront);
            float bankZ = curb - 0.5f;
            b.Flat(-half, bankZ, half, curb, road + 0.004f, snowShade);
            for (int i = 0; i < 90; i++)
            {
                float x = Mathf.Lerp(-half, half, LightSchedule.Hash(seed, i, 225, 0));
                float w = Mathf.Lerp(0.3f, 1.1f, LightSchedule.Hash(seed, i, 226, 0));
                float d = Mathf.Lerp(0.08f, 0.3f, LightSchedule.Hash(seed, i, 227, 0));
                b.Flat(x, bankZ - d, x + w, bankZ, road + 0.004f, snowShade);
            }
            // slush tracks in the lanes, snow along the far road edge (the bottom of the screen)
            float dashZ = (RenderingStyle.FarLaneZ + RenderingStyle.NearLaneZ) * 0.5f;
            b.Flat(-half, dashZ - 0.25f, half, dashZ + 0.25f, road + 0.006f, slush);
            foreach (float lane in new[] { RenderingStyle.FarLaneZ, RenderingStyle.NearLaneZ })
                b.Flat(-half, lane - 0.08f, half, lane + 0.08f, road + 0.005f, slush);
            b.Flat(-half, -40f, half, RenderingStyle.NearLaneZ - 0.95f, road + 0.004f, snowShade);
            // street lamp heads
            float z0 = -layout.Depth * 0.5f, lampZ = curb + 0.25f;
            foreach (float sx in CityView.LampXs)
            {
                float x = PixelGrid.WorldX(sx, lampZ, RenderingStyle.CameraShear, z0);
                Cap(b, x - 0.06f, x + 0.6f, g + 3.42f, lampZ - 0.06f, lampZ + 0.06f, 0.06f, 0.02f);
            }
        }

        void NeighbourRoofs(PaletteMeshBuilder b, bool heavy)
        {
            var shear = RenderingStyle.CameraShear;
            float z0 = -layout.Depth * 0.5f, g = RenderingStyle.GroundY;
            foreach (var blk in CityView.Blocks)
            {
                float z = blk.Z;
                float depth = blk.Z > 6f ? 6f : 4.5f;
                float top = Mathf.Round(g + blk.Height);
                float x0 = PixelGrid.WorldX(blk.X0, z, shear, z0), x1 = PixelGrid.WorldX(blk.X1, z, shear, z0);
                float yTop = PixelGrid.WorldY(top, z, shear, z0);
                bool back = blk.Z > 6f;
                var topC = back ? Atlas.Static(SnowShade * 0.8f) : snowTop;
                var frontC = back ? Atlas.Static(SnowShade * 0.65f) : snowFront;
                if (heavy)
                {
                    b.Box(new Vector3(x0 - 0.05f, yTop - 0.05f, z - 0.03f), new Vector3(x1 + 0.05f, yTop + 0.18f, z + depth), frontC, topC, snowShade);
                }
                else b.Flat(x0, z, x1, z + depth, yTop + 0.01f, topC);
            }
        }

        /// <summary>Snow on top of the HOTEL sign frame (child of the sign assembly: scales with its texels).</summary>
        void BuildSignCap()
        {
            if (building?.HotelSignRoot == null) return;
            var b = new PaletteMeshBuilder(Atlas);
            float t = building.SignTexelBase, p = t * 0.5f;
            var plate = building.HotelPlateSize;
            float hw = plate.x * 0.5f + 2f * t, top = plate.y + 2f * t;
            b.Box(new Vector3(-hw - p, top, -0.05f), new Vector3(hw + p, top + 3f * p, 0.32f), snowFront, snowTop, snowShade);
            // a few one-pixel drips over the frame edge (never down to the bulbs)
            for (int i = 0; i < 9; i++)
            {
                float x = -hw + (i + 0.5f) * (2f * hw / 9f) + (LightSchedule.Hash(seed, i, 230, 0) - 0.5f) * 4f * p;
                x = Mathf.Round(x / p) * p;
                b.Front(x, top - p, x + p, top, -0.051f, snowFront);
            }
            SignCapRenderer = PaletteRenderer.Create(building.HotelSignRoot, "Sign Snow", assets.Own(b.Build("Sign Snow")), Atlas.Material);
        }

        // ------------------------------------------------------------------ icicles (pixel-exact per frame zoom)

        void ApplyIcicles(PixelGrid grid)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(grid.PixelsPerUnit));
            if (IcicleRenderer != null && key == icicleKey) return;
            if (!icicleMeshes.TryGetValue(key, out var mesh))
            {
                mesh = assets.Own(BuildIcicles(grid, key));
                icicleMeshes[key] = mesh;
            }
            if (IcicleRenderer == null)
            {
                IcicleRenderer = PaletteRenderer.Create(Root, "Icicles", mesh, Atlas.Material);
                icicleFilter = IcicleRenderer.GetComponent<MeshFilter>();
            }
            icicleFilter.sharedMesh = mesh;
            icicleKey = key;
        }

        public int IcicleCount { get; private set; }

        Mesh BuildIcicles(PixelGrid grid, int key)
        {
            var b = new PaletteMeshBuilder(Atlas);
            var p = new PixelPainter(b, key, grid.Shear, grid.ShearDepth);
            int ice = Atlas.Static(Ice), tip = Atlas.Static(IceTip), drip = Atlas.Static(SnowFront);
            int count = 0;
            void Row(float x0, float x1, float y, float z, int salt)
            {
                var a = p.At(new Vector3(x0, y, z));
                var c = p.At(new Vector3(x1, y, z));
                // a ragged 1-px snow lip under the edge, icicles every few pixels
                for (int x = a.x, i = 0; x < c.x; i++)
                {
                    float h = LightSchedule.Hash(seed, salt, i, 240);
                    int len = h < 0.35f ? 0 : h < 0.7f ? 1 : h < 0.9f ? 2 : 3;
                    if (len > 0)
                    {
                        p.Rect(x, a.y - len, 1, len, z, ice);
                        p.Rect(x, a.y - len - 1, 1, 1, z, tip);
                        count++;
                    }
                    else if (h < 0.15f) p.Rect(x, a.y - 1, 1, 1, z, drip);
                    x += 2 + (int)(LightSchedule.Hash(seed, salt, i, 241) * 3f);
                }
            }
            if (facade != null)
            {
                float front = facade.FrontZ;
                Row(facade.CorniceLeft - 0.15f, facade.CorniceRight + 0.15f, facade.TopY + 0.3f, front - 0.56f, 1);
                Row(facade.AnnexLeft - 0.05f, facade.AnnexRight + 0.1f, facade.AnnexTop - 0.33f, facade.AnnexZ - 0.16f, 2);
                Row(facade.CanopyLeft, facade.CanopyRight, facade.CanopyTop - 0.46f, facade.CanopyFrontZ - 0.07f, 3);
                // under the floor bands' shadow lines on both pilasters
                float pw = HotelFacade.PilasterWidth;
                float rightIn = layout.ShaftX + layout.FloorWidth * 0.5f + HotelFacade.CutawayClearance;
                for (int f = 1; f < layout.FloorCount; f++)
                {
                    float y = f * layout.FloorHeight - 0.39f;
                    Row(facade.PilasterLeft - 0.05f, facade.PilasterLeft + pw * 0.6f, y, facade.PilasterZ - 0.07f, 10 + f);
                    Row(rightIn + pw * 0.4f, facade.PilasterRight + 0.05f, y, facade.PilasterZ - 0.07f, 30 + f);
                }
            }
            IcicleCount = count;
            return b.Build("Icicles " + key + "ppu");
        }

        // ------------------------------------------------------------------ Santa's sleigh across the moon

        static readonly string[] SleighSprite =
        {
            ".BB...........",
            "BBBB..rw......",
            "BBBB.rrr......",
            "BBBBrwsw......",
            "rrrrrrwwrrr.gg",
            "Rrrrrrrrrrrrg.",
            ".Rrrrrrrrrrg..",
            "g..........g..",
            ".gggggggggggg.",
        };

        static readonly string[] ReindeerA =
        {
            ".....y.y",
            "......y.",
            ".....bbn",
            "bbbbbbb.",
            ".bbbbb..",
            ".b...b..",
            "b.....b.",
        };

        static readonly string[] ReindeerB =
        {
            ".....y.y",
            "......y.",
            ".....bbn",
            "bbbbbbb.",
            ".bbbbb..",
            "..b.b...",
            "..b.b...",
        };

        Dictionary<char, int> sleighKeys, deerKeys, leaderKeys;
        int rein, sparkle;

        void SleighKeys()
        {
            sleighKeys = new Dictionary<char, int>
            {
                ['B'] = Atlas.Static(new Color(0.42f, 0.27f, 0.16f)),
                ['r'] = Atlas.Static(new Color(0.78f, 0.13f, 0.12f)),
                ['R'] = Atlas.Static(new Color(0.48f, 0.07f, 0.07f)),
                ['w'] = Atlas.Static(new Color(0.92f, 0.92f, 0.90f)),
                ['s'] = Atlas.Static(new Color(0.96f, 0.76f, 0.60f)),
                ['g'] = Atlas.Static(new Color(0.88f, 0.70f, 0.26f)),
            };
            int brown = Atlas.Static(new Color(0.36f, 0.24f, 0.15f)), antler = Atlas.Static(new Color(0.62f, 0.50f, 0.34f));
            deerKeys = new Dictionary<char, int> { ['b'] = brown, ['y'] = antler, ['n'] = brown };
            leaderKeys = new Dictionary<char, int> { ['b'] = brown, ['y'] = antler, ['n'] = Atlas.Static(new Color(1f, 0.2f, 0.16f)) };
            rein = Atlas.Static(new Color(0.70f, 0.55f, 0.22f));
            sparkle = Atlas.Static(new Color(1f, 0.88f, 0.5f));
        }

        public void ResetSleigh()
        {
            sleighClock = 0f;
            SleighFlight = false;
            if (sleighMesh != null) { sleighQuads.Clear(); sleighQuads.Apply(sleighMesh); }
            if (SleighRenderer != null) SleighRenderer.enabled = false;
        }

        void UpdateSleigh(float dt, CameraFrame frame)
        {
            if (!SleighFlight)
            {
                if (SleighRenderer.enabled) ResetSleigh();
                return;
            }
            sleighClock += Mathf.Max(0f, dt);
            var moon = new Vector2Int(Mathf.RoundToInt(SkyView.MoonViewport.x * StarField.Width), Mathf.RoundToInt(SkyView.MoonViewport.y * StarField.Height));
            sleighQuads.Clear();
            if (WinterSchedule.Sleigh(sleighClock, moon, out var at, out _))
            {
                SleighPixels = at;
                int x = Mathf.FloorToInt(at.x), y = Mathf.FloorToInt(at.y);
                bool stride = Mathf.Repeat(sleighClock, 0.7f) < 0.35f; // gallop: 2 leg changes per 0.7 s
                Sprite(x, y, SleighSprite, sleighKeys);
                // reins from the sleigh's curl to the leader, two reindeer
                for (int i = 0; i < 12; i++) Px(x + 13 + i, y + 6 + (i > 3 && i < 9 ? 1 : 0), rein);
                Sprite(x + 15, y + 4, stride ? ReindeerA : ReindeerB, deerKeys);
                Sprite(x + 24, y + 5, stride ? ReindeerB : ReindeerA, leaderKeys);
                // a short trail of gold sparkles behind the sleigh (fixed per flight, slowly drifting down)
                for (int i = 0; i < 6; i++)
                {
                    float h = LightSchedule.Hash(seed, i, 250, 0);
                    int tx = x - 3 - i * 4 - (int)(h * 3f);
                    int ty = y + 3 - i / 2 + (int)(h * 3f) - 1;
                    if (((i + Mathf.FloorToInt(sleighClock * 2f)) & 1) == 0 || i < 2) Px(tx, ty, sparkle);
                }
            }
            Place(sleighLayer, frame, SleighDepth);
            sleighQuads.Apply(sleighMesh);
            SleighRenderer.enabled = sleighQuads.Count > 0;
        }

        void Px(int x, int y, int texel)
        {
            if (x < 0 || y < 0 || x >= StarField.Width || y >= StarField.Height) return;
            sleighQuads.Rect(x, y, 1, 1, Atlas.UV(texel));
        }

        void Sprite(int x, int y, string[] rows, Dictionary<char, int> keys)
        {
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            {
                string line = rows[r];
                int yy = y + h - 1 - r;
                if (yy < 0 || yy >= StarField.Height) continue;
                for (int c = 0; c < line.Length;)
                {
                    if (!keys.TryGetValue(line[c], out int texel)) { c++; continue; }
                    int end = c + 1;
                    while (end < line.Length && line[end] == line[c]) end++;
                    int x0 = Mathf.Max(0, x + c), x1 = Mathf.Min(StarField.Width, x + end);
                    if (x1 > x0) sleighQuads.Rect(x0, yy, x1 - x0, 1, Atlas.UV(texel));
                    c = end;
                }
            }
        }
    }
}
