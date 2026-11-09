using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pure, seeded rain + lightning schedules (EditMode-tested, PhotosensitivityTests). Like <see cref="SkySchedule"/>
    /// everything is a function of (seed, index, time): no RNG state, a frozen clock freezes the weather.
    /// Lightning: exactly one strike per <see cref="StrikeSlot"/> s slot, placed so consecutive strikes are
    /// <see cref="MinGap"/>..<see cref="MaxGap"/> s apart; each strike is ONE flash (a fast rise, a short hold and a
    /// monotonic fade = 2 bright/dark transitions) - never a strobe.
    /// </summary>
    static class WeatherSchedule
    {
        public const int DropCount = 210;
        public const float DropMinSpeed = 250f;
        public const float DropMaxSpeed = 340f;
        /// <summary>Horizontal pixels per vertical pixel (wind from the right).</summary>
        public const float Slant = 0.3f;
        public const int SplashSlots = 28;
        public const float SplashLife = 0.18f;

        public const float StrikeSlot = 14f;
        public const float MinGap = 8f;
        public const float MaxGap = 20f;
        /// <summary>Strike offset inside its slot: [StrikeEarliest, StrikeEarliest + StrikeSpread] -> gaps 8..20 s.</summary>
        const float StrikeEarliest = 3f;
        const float StrikeSpread = 6f;
        public const float FlashRise = 0.05f;
        public const float FlashHold = 0.1f;
        public const float FlashFall = 0.6f;
        public const float FlashSeconds = FlashRise + FlashHold + FlashFall;

        public struct Drop
        {
            /// <summary>Head (lowest, brightest pixel) in viewport pixels.</summary>
            public Vector2 Head;
            public int Length;
            public float Level;
        }

        /// <summary>Rain drop <paramref name="i"/> at <paramref name="time"/>: falls through the viewport and wraps.</summary>
        public static Drop RainDrop(int seed, int i, float time)
        {
            float speed = Mathf.Lerp(DropMinSpeed, DropMaxSpeed, LightSchedule.Hash(seed, i, 101, 0));
            int len = 6 + (int)(LightSchedule.Hash(seed, i, 102, 0) * 6f);
            float span = StarField.Height + len + 24f;
            float phase = LightSchedule.Hash(seed, i, 103, 0);
            float fall = Mathf.Repeat(phase * span + Mathf.Max(0f, time) * speed, span);
            float y = StarField.Height + len - fall;
            // spread over the width plus the slant overhang so the left edge is rained on too
            float x0 = LightSchedule.Hash(seed, i, 104, 0) * (StarField.Width + span * Slant);
            float x = x0 - fall * Slant;
            return new Drop
            {
                Head = new Vector2(x, y),
                Length = len,
                Level = Mathf.Lerp(0.55f, 1f, LightSchedule.Hash(seed, i, 105, 0)),
            };
        }

        /// <summary>
        /// Splash slot <paramref name="i"/>: active for <see cref="SplashLife"/> s once per cycle, at a fresh spot each
        /// cycle. <paramref name="u"/> = viewport x 0..1, <paramref name="depth"/> = 0 (curb) .. 1 (far side), age 0..1.
        /// </summary>
        public static bool Splash(int seed, int i, float time, out float u, out float depth, out float age)
        {
            float period = Mathf.Lerp(0.35f, 0.7f, LightSchedule.Hash(seed, i, 110, 0));
            float t = Mathf.Max(0f, time) + LightSchedule.Hash(seed, i, 111, 0) * period;
            int k = Mathf.FloorToInt(t / period);
            float r = t - k * period;
            u = LightSchedule.Hash(seed, i, 112, k);
            depth = LightSchedule.Hash(seed, i, 113, k);
            age = r / SplashLife;
            return r < SplashLife;
        }

        /// <summary>Start time of the strike in slot <paramref name="k"/>.</summary>
        public static float StrikeStart(int seed, int k) => k * StrikeSlot + StrikeEarliest + LightSchedule.Hash(seed, 120, k, 0) * StrikeSpread;

        static float Envelope(float r)
        {
            if (r < 0f || r >= FlashSeconds) return 0f;
            if (r < FlashRise) return r / FlashRise;
            if (r < FlashRise + FlashHold) return 1f;
            float k = 1f - (r - FlashRise - FlashHold) / FlashFall;
            return k * k;
        }

        /// <summary>Lightning brightness 0..1 at <paramref name="time"/> and the strike's slot (-1 = none).</summary>
        public static float Lightning(int seed, float time, out int slot)
        {
            float t = Mathf.Max(0f, time);
            int k = Mathf.FloorToInt(t / StrikeSlot);
            float level = Envelope(t - StrikeStart(seed, k));
            slot = level > 0f ? k : -1;
            return level;
        }

        public static float LightningLevel(int seed, float time) => Lightning(seed, time, out _);

        /// <summary>
        /// Jagged bolt for strike slot <paramref name="k"/> in viewport pixels: main channel from the top of the sky down
        /// to the skyline (the city hides its foot), plus one shorter branch. Points are consecutive line ends.
        /// </summary>
        public static void Bolt(int seed, int k, List<Vector2Int> main, List<Vector2Int> branch)
        {
            main.Clear();
            branch.Clear();
            // aim at the open sky of the gameplay frame: the gap left of the hotel, or above the right-hand roofs
            float hx = LightSchedule.Hash(seed, 121, k, 0);
            float vx = hx < 0.55f ? Mathf.Lerp(0.18f, 0.33f, hx / 0.55f) : Mathf.Lerp(0.70f, 0.86f, (hx - 0.55f) / 0.45f);
            int x = Mathf.RoundToInt(vx * StarField.Width);
            int y = StarField.Height + 2;
            int bottom = 150 + (int)(LightSchedule.Hash(seed, 121, k, 1) * 40f);
            int lean = LightSchedule.Hash(seed, 121, k, 2) < 0.5f ? -1 : 1;
            main.Add(new Vector2Int(x, y));
            int step = 0;
            while (y > bottom)
            {
                y -= 7 + (int)(LightSchedule.Hash(seed, 122, k, step) * 10f);
                x = Mathf.Clamp(x + lean * 2 + (int)((LightSchedule.Hash(seed, 123, k, step) - 0.5f) * 14f), 8, StarField.Width - 8);
                main.Add(new Vector2Int(x, y));
                step++;
            }
            int from = Mathf.Clamp(1 + (int)(LightSchedule.Hash(seed, 124, k, 0) * (main.Count / 2)), 1, main.Count - 1);
            var p = main[from];
            branch.Add(p);
            for (int j = 0; j < 4; j++)
            {
                p += new Vector2Int(-lean * (4 + (int)(LightSchedule.Hash(seed, 125, k, j) * 6f)), -(5 + (int)(LightSchedule.Hash(seed, 126, k, j) * 6f)));
                branch.Add(p);
            }
        }
    }

    /// <summary>
    /// Rainy night (IDayQuery.IsRainy, Day 5): overcast sky (SkyView hides moon and stars), rain streaks over the whole
    /// view, splashes on the street, wet street (puddles + lamp reflections), distant lightning behind the skyline.
    ///   Rain layer   (1 additive palette mesh, viewport pixels like the sky): slanted 1-px streaks; over the hotel they
    ///                are drawn at less than half strength so the floors stay readable. Splashes: tiny "^" on the street.
    ///   Bolt layer   (1 palette mesh behind the city, only during a strike): jagged bolt + branch.
    ///   Puddles      (1 world-space palette mesh on the street): puddles and lamp reflections, slow shimmer, they catch
    ///                the lightning.
    ///   Sky flash    the world camera's clear colour (= the sky between and above the buildings, nothing else).
    /// Cost on a rainy day: 2 draws (+1 during a strike), ~350 quads rebuilt per frame. Nothing exists on other days.
    /// </summary>
    sealed class WeatherView
    {
        public const float BoltDepth = SkyView.StarDepth - 0.5f;
        public static readonly Color RainSky = new Color(0.065f, 0.07f, 0.10f);
        /// <summary>Sky colour at the peak of a flash: a moderate blue-grey, never white.</summary>
        public static readonly Color FlashSky = new Color(0.30f, 0.31f, 0.42f);
        static readonly Color RainColor = new Color(0.36f, 0.40f, 0.52f);
        static readonly Color SplashColor = new Color(0.55f, 0.60f, 0.72f);
        static readonly Color BoltCore = new Color(0.86f, 0.88f, 1.00f);
        static readonly Color BoltGlow = new Color(0.46f, 0.48f, 0.70f);
        static readonly Color PuddleDark = new Color(0.10f, 0.115f, 0.16f);
        static readonly Color PuddleLit = new Color(0.30f, 0.33f, 0.43f);
        static readonly Color LampReflection = new Color(0.50f, 0.41f, 0.24f);
        /// <summary>Rain over the hotel's rectangle is drawn at this fraction (readable floors).</summary>
        public const float HotelRainFactor = 0.35f;
        const int RampSteps = 8;

        readonly RenderAssets assets;
        readonly PaletteAtlas atlas;
        readonly FloorF.Core.IWorldLayout layout;
        readonly int seed;
        readonly PixelQuads rainQuads = new PixelQuads();
        readonly PixelQuads boltQuads = new PixelQuads();
        readonly List<Vector2Int> boltMain = new List<Vector2Int>();
        readonly List<Vector2Int> boltBranch = new List<Vector2Int>();
        int[] rainRamp, splashRamp;
        int boltCore, boltGlow, puddleTexel, reflectionTexel;
        Mesh rainMesh, boltMesh;
        Transform rainLayer, boltLayer;
        int boltSlot = int.MinValue;
        bool rainAdditive;

        public Transform Root { get; }
        public MeshRenderer RainRenderer { get; private set; }
        public MeshRenderer BoltRenderer { get; private set; }
        public MeshRenderer PuddleRenderer { get; private set; }
        /// <summary>Tests: force rain on/off (null = follow IDayQuery.IsRainy).</summary>
        public bool? ForceRain { get; set; }
        public bool Raining { get; private set; }
        public float LightningLevel { get; private set; }
        public int StrikesSeen { get; private set; }
        public int RainQuadCount => rainQuads.Count;
        public int BoltQuadCount => boltQuads.Count;
        /// <summary>Clear colour the weather wants for the world camera (sky).</summary>
        public Color SkyColor { get; private set; } = RenderingStyle.Background;
        /// <summary>Hotel rectangle in viewport pixels (rain is lighter in front of it).</summary>
        public Rect HotelPixels { get; private set; }

        public WeatherView(Transform parent, RenderAssets assets, PaletteAtlas atlas, FloorF.Core.IWorldLayout layout, int seed)
        {
            this.assets = assets;
            this.atlas = atlas;
            this.layout = layout;
            this.seed = seed;
            Root = new GameObject("Weather").transform;
            Root.SetParent(parent, false);
        }

        public void Build()
        {
            var additive = assets.NewAdditive(atlas.Texture, "Rain (additive)");
            rainAdditive = additive != null;
            var from = rainAdditive ? Color.black : RenderingStyle.Background;
            rainRamp = Ramp(RainColor, from);
            splashRamp = Ramp(SplashColor, from);
            boltCore = atlas.Dynamic(BoltCore);
            boltGlow = atlas.Dynamic(BoltGlow);
            puddleTexel = atlas.Dynamic(PuddleDark);
            reflectionTexel = atlas.Dynamic(LampReflection);

            rainMesh = assets.Own(new Mesh { name = "Rain" });
            rainMesh.MarkDynamic();
            RainRenderer = PaletteRenderer.Create(Root, "Rain", rainMesh, rainAdditive ? additive : atlas.Material);
            RainRenderer.allowOcclusionWhenDynamic = false;
            rainLayer = RainRenderer.transform;
            RainRenderer.enabled = false;

            boltMesh = assets.Own(new Mesh { name = "Lightning Bolt" });
            boltMesh.MarkDynamic();
            BoltRenderer = PaletteRenderer.Create(Root, "Lightning Bolt", boltMesh, atlas.Material);
            BoltRenderer.allowOcclusionWhenDynamic = false;
            boltLayer = BoltRenderer.transform;
            BoltRenderer.enabled = false;

            BuildPuddles();
        }

        int[] Ramp(Color c, Color from)
        {
            var r = new int[RampSteps];
            for (int i = 0; i < RampSteps; i++) r[i] = atlas.Static(Color.Lerp(from, c, i / (RampSteps - 1f)));
            return r;
        }

        /// <summary>Wet street: puddles on the road and the sidewalk, warm streaks of lamp light on the asphalt.</summary>
        void BuildPuddles()
        {
            var b = new PaletteMeshBuilder(atlas);
            float road = RenderingStyle.RoadY + 0.006f, ground = RenderingStyle.GroundY + 0.006f;
            float half = RenderingStyle.StreetHalfLength;
            float z0 = -layout.Depth * 0.5f;
            for (int i = 0; i < 26; i++)
            {
                float x = Mathf.Lerp(-half, half, LightSchedule.Hash(seed, i, 130, 0));
                bool sidewalk = LightSchedule.Hash(seed, i, 131, 0) < 0.35f;
                float z = sidewalk
                    ? Mathf.Lerp(RenderingStyle.CurbZ + 0.3f, z0 - 0.5f, LightSchedule.Hash(seed, i, 132, 0))
                    : Mathf.Lerp(RenderingStyle.NearLaneZ - 0.9f, RenderingStyle.CurbZ - 0.4f, LightSchedule.Hash(seed, i, 132, 0));
                float w = Mathf.Lerp(0.8f, 2.2f, LightSchedule.Hash(seed, i, 133, 0));
                float d = Mathf.Lerp(0.18f, 0.4f, LightSchedule.Hash(seed, i, 134, 0));
                float y = sidewalk ? ground : road;
                // rounded blob: a wide core and a narrower rim row in front and behind
                b.Flat(x - w * 0.5f, z - d * 0.5f, x + w * 0.5f, z + d * 0.5f, y, puddleTexel);
                b.Flat(x - w * 0.32f, z + d * 0.5f, x + w * 0.32f, z + d * 0.85f, y, puddleTexel);
                b.Flat(x - w * 0.36f, z - d * 0.85f, x + w * 0.36f, z - d * 0.5f, y, puddleTexel);
            }
            // street lamps mirrored on the wet asphalt: broken vertical streaks from the curb into the road
            foreach (float sx in CityView.LampXs)
            {
                float x = PixelGrid.WorldX(sx, RenderingStyle.CurbZ, RenderingStyle.CameraShear, z0) + 0.5f;
                for (int j = 0; j < 4; j++)
                {
                    float za = RenderingStyle.CurbZ - 0.15f - j * 0.62f;
                    float hw = 0.16f - j * 0.025f;
                    b.Flat(x - hw, za - 0.4f, x + hw, za, road + 0.001f, reflectionTexel);
                }
            }
            var mesh = assets.Own(b.Build("Puddles"));
            PuddleRenderer = PaletteRenderer.Create(Root, "Puddles", mesh, atlas.Material);
            PuddleRenderer.enabled = false;
        }

        static void Place(Transform layer, CameraFrame f, float z)
        {
            layer.position = SkyView.LayerOrigin(f, z);
            float s = 1f / f.PixelsPerUnit;
            layer.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>
        /// <paramref name="time"/> = juice clock (+ prewarm): rain, splashes and lightning freeze while paused.
        /// </summary>
        public void Tick(float time, CameraFrame frame, bool rainy)
        {
            if (rainMesh == null) return;
            bool was = Raining;
            Raining = ForceRain ?? rainy;
            PuddleRenderer.enabled = Raining;
            if (!Raining)
            {
                if (was)
                {
                    rainQuads.Clear(); rainQuads.Apply(rainMesh);
                    boltQuads.Clear(); boltQuads.Apply(boltMesh);
                }
                RainRenderer.enabled = BoltRenderer.enabled = false;
                LightningLevel = 0f;
                SkyColor = RenderingStyle.Background;
                return;
            }

            Place(rainLayer, frame, SkyView.FxDepth);
            Place(boltLayer, frame, BoltDepth);
            HotelPixels = HotelRect(frame);

            // ---- lightning: sky flash (clear colour), bolt behind the skyline, puddles catch it
            float flash = WeatherSchedule.Lightning(seed, time, out int slot);
            LightningLevel = flash;
            SkyColor = Color.Lerp(RainSky, FlashSky, flash);
            float shimmer = 0.5f + 0.5f * Mathf.Sin(time * 2.1f);
            atlas.Set(puddleTexel, Color.Lerp(Color.Lerp(PuddleDark, PuddleLit, 0.35f + 0.25f * shimmer), FlashSky * 1.25f, flash * 0.6f));
            atlas.Set(reflectionTexel, Color.Lerp(LampReflection * (0.9f + 0.1f * shimmer), FlashSky * 1.3f, flash * 0.5f));
            atlas.Set(boltCore, Color.Lerp(SkyColor, BoltCore, flash));
            atlas.Set(boltGlow, Color.Lerp(SkyColor, BoltGlow, flash));
            if (slot >= 0 && slot != boltSlot)
            {
                boltSlot = slot;
                StrikesSeen++;
                WeatherSchedule.Bolt(seed, slot, boltMain, boltBranch);
                boltQuads.Clear();
                Polyline(boltBranch, boltGlow, false);
                Polyline(boltMain, boltCore, true);
                boltQuads.Apply(boltMesh);
            }
            BoltRenderer.enabled = slot >= 0 && flash > 0.08f;

            // ---- rain + splashes
            rainQuads.Clear();
            var hotel = HotelPixels;
            for (int i = 0; i < WeatherSchedule.DropCount; i++)
            {
                var d = WeatherSchedule.RainDrop(seed, i, time);
                float k = hotel.Contains(d.Head) ? HotelRainFactor : 1f;
                Streak(d, k);
            }
            float curbY = SkyView.WorldToPixels(frame, new Vector3(frame.Position.x, RenderingStyle.RoadY, RenderingStyle.CurbZ)).y;
            float farY = SkyView.WorldToPixels(frame, new Vector3(frame.Position.x, RenderingStyle.RoadY, RenderingStyle.NearLaneZ - 1.2f)).y;
            float walkY = SkyView.WorldToPixels(frame, new Vector3(frame.Position.x, RenderingStyle.GroundY, RenderingStyle.CurbZ + 0.6f)).y;
            for (int i = 0; i < WeatherSchedule.SplashSlots; i++)
            {
                if (!WeatherSchedule.Splash(seed, i, time, out float u, out float depth, out float age)) continue;
                // two thirds on the road, a third on the sidewalk
                float y = i % 3 == 0 ? walkY + 1f + depth * 3f : Mathf.Lerp(curbY - 2f, farY, depth);
                Splash(Mathf.FloorToInt(u * StarField.Width), Mathf.FloorToInt(y), age);
            }
            rainQuads.Apply(rainMesh);
            RainRenderer.enabled = rainQuads.Count > 0;
        }

        /// <summary>The hotel's front rectangle (plinth to sign) in viewport pixels.</summary>
        Rect HotelRect(CameraFrame f)
        {
            var b = BuildingView.FramingBounds(layout);
            var lo = SkyView.WorldToPixels(f, new Vector3(b.min.x, b.min.y, b.min.z));
            var hi = SkyView.WorldToPixels(f, new Vector3(b.max.x, b.max.y, b.min.z));
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        void Px(int x, int y, float level, int[] ramp)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(level * (RampSteps - 1)), 0, RampSteps - 1);
            if (i <= 0 || x < 0 || y < 0 || x >= StarField.Width || y >= StarField.Height) return;
            rainQuads.Rect(x, y, 1, 1, atlas.UV(ramp[i]));
        }

        /// <summary>Slanted 1-px streak, head (bottom) brightest; vertical runs of one column merge into one quad.</summary>
        void Streak(in WeatherSchedule.Drop d, float k)
        {
            int hx = Mathf.FloorToInt(d.Head.x), hy = Mathf.FloorToInt(d.Head.y);
            int runX = int.MinValue, runY0 = 0, runLen = 0;
            float runLevel = 0f;
            for (int j = 0; j < d.Length; j++)
            {
                int x = hx + Mathf.FloorToInt(j * WeatherSchedule.Slant);
                int y = hy + j;
                float level = d.Level * k * (j == 0 ? 1f : j < d.Length / 2 ? 0.75f : 0.45f);
                if (x == runX && Mathf.Approximately(level, runLevel)) { runLen++; continue; }
                FlushRun(runX, runY0, runLen, runLevel);
                runX = x; runY0 = y; runLen = 1; runLevel = level;
            }
            FlushRun(runX, runY0, runLen, runLevel);
        }

        void FlushRun(int x, int y0, int len, float level)
        {
            if (len <= 0 || x < 0 || x >= StarField.Width) return;
            int i = Mathf.Clamp(Mathf.RoundToInt(level * (RampSteps - 1)), 0, RampSteps - 1);
            if (i <= 0) return;
            int y1 = Mathf.Min(StarField.Height, y0 + len);
            y0 = Mathf.Max(0, y0);
            if (y1 <= y0) return;
            rainQuads.Rect(x, y0, 1, y1 - y0, atlas.UV(rainRamp[i]));
        }

        /// <summary>Tiny splash: a dot, then a "^" crown spreading and fading.</summary>
        void Splash(int x, int y, float age)
        {
            float l = 1f - age;
            if (age < 0.35f) { Px(x, y, l, splashRamp); Px(x, y + 1, l * 0.6f, splashRamp); return; }
            int s = age < 0.7f ? 1 : 2;
            Px(x - s, y + 1, l, splashRamp);
            Px(x + s, y + 1, l, splashRamp);
            if (s == 2) { Px(x - 1, y, l * 0.7f, splashRamp); Px(x + 1, y, l * 0.7f, splashRamp); }
        }

        /// <summary>Pixel-stepped polyline of 1-px quads (core one pixel wider to the right for the main channel).</summary>
        void Polyline(List<Vector2Int> pts, int texel, bool thick)
        {
            var uv = atlas.UV(texel);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                for (int s = 0; s <= steps; s++)
                {
                    int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, steps == 0 ? 0f : s / (float)steps));
                    int y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, steps == 0 ? 0f : s / (float)steps));
                    if (y < 0 || y >= StarField.Height + 4) continue;
                    boltQuads.Rect(x, y, thick && i < pts.Count / 2 ? 2 : 1, 1, uv);
                }
            }
        }
    }
}
