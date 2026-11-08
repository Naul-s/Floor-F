using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorF.Rendering
{
    /// <summary>
    /// Night sky, anchored to the VIEWPORT: built in low-res target pixels (0..640 x 0..360, origin bottom-left) on layers
    /// far behind the city, re-placed every frame from the applied camera frame so one layer unit = one target pixel and
    /// the layer origin sits on the viewport's bottom-left pixel corner. Stars therefore never shimmer (whole pixels in
    /// every frame, the title pan and the shake only move the world in front of them) and a viewport coordinate - the
    /// wish-star contract - maps to exactly the same spot in the gameplay and title frames.
    ///   Stars layer (1 palette mesh): moon with craters + dithered halo, steady / twinkling stars (twinkle = shared
    ///                                  palette texels rewritten each frame).
    ///   Band layer  (1 textured quad, star night only): baked dithered gradient sky + Milky Way + dense faint stars.
    ///   Fx layer    (1 palette mesh rebuilt while something moves): glints, meteors (behind the city, like real sky).
    ///   Wish layer  (1 palette mesh, ADDITIVE, ZTest Always - drawn over the city and the hotel): wish stars with a
    ///                dithered glow, their sparkles and the wish burst. They are magical and the player must click them,
    ///                so they never hide behind a building; still placed on the same viewport mapping (event coords).
    /// Everything shares the city palette texture except the band (own baked texture).
    /// </summary>
    sealed class SkyView
    {
        public const float StarDepth = 60f;
        public const float BandDepth = 61f;
        public const float FxDepth = 59f;
        /// <summary>
        /// Top left corner: above the left neighbours in the gameplay frame (their roofs reach ~0.88), left of the HUD
        /// panels (from ~0.14 at 16:9) and of the title logo (top centre), never behind the hotel.
        /// </summary>
        // between the HUD day box (ends ~0.22) and the hotel / title logo (start ~0.27) at a real 16:9 canvas;
        // 0.07 hid it under the day box in play (batch captures letterbox the world and didn't show that)
        public static readonly Vector2 MoonViewport = new Vector2(0.245f, 0.925f);
        public const int MoonRadius = 10;
        public const int HaloRadius = MoonRadius + 7;
        const int RampSteps = 12;

        static readonly Color[] SteadyTones =
        {
            new Color(0.36f, 0.40f, 0.56f),
            new Color(0.60f, 0.64f, 0.80f),
            new Color(0.93f, 0.94f, 1.00f),
            new Color(0.98f, 0.85f, 0.62f),
        };
        static readonly Color TwinkleCool = new Color(0.88f, 0.92f, 1.00f);
        static readonly Color TwinkleWarm = new Color(1.00f, 0.90f, 0.72f);
        static readonly Color GlintColor = new Color(0.95f, 0.97f, 1.00f);
        /// <summary>Hot-to-cold ramps (level 1 first): pixel-art tails cool down through saturated colours, not grey.</summary>
        static readonly Color[] MeteorStops =
        {
            new Color(0.96f, 0.98f, 1.00f), new Color(0.62f, 0.82f, 1.00f), new Color(0.36f, 0.50f, 0.92f), new Color(0.22f, 0.22f, 0.52f), new Color(0.11f, 0.11f, 0.26f),
        };
        static readonly Color[] WishStops =
        {
            new Color(1.00f, 0.98f, 0.80f), new Color(1.00f, 0.85f, 0.38f), new Color(0.98f, 0.58f, 0.24f), new Color(0.66f, 0.28f, 0.36f), new Color(0.24f, 0.12f, 0.26f),
        };
        static readonly Color WishCore = new Color(1.00f, 1.00f, 0.94f);
        /// <summary>Soft dithered glow around a wish star's head (added on top of whatever is behind it).</summary>
        static readonly Color WishGlow = new Color(1.00f, 0.80f, 0.42f);
        public const int WishGlowRadius = 6;

        static readonly Color MoonLight = new Color(0.95f, 0.93f, 0.82f);
        static readonly Color MoonBright = new Color(1.00f, 0.99f, 0.92f);
        static readonly Color MoonShade = new Color(0.74f, 0.73f, 0.68f);
        static readonly Color MoonRim = new Color(0.56f, 0.57f, 0.60f);
        static readonly Color MoonCrater = new Color(0.78f, 0.76f, 0.66f);
        static readonly Color MoonCraterDark = new Color(0.62f, 0.61f, 0.57f);
        static readonly Color HaloNear = new Color(0.15f, 0.16f, 0.25f);
        static readonly Color HaloFar = new Color(0.10f, 0.11f, 0.19f);

        /// <summary>Craters: offset from the moon centre (pixels) and radius.</summary>
        static readonly Vector3[] Craters =
        {
            new Vector3(-3f, 2f, 2.3f), new Vector3(3f, -3f, 1.7f), new Vector3(2f, 5f, 1.2f),
            new Vector3(-3f, -5f, 1.3f), new Vector3(6f, 1f, 1.1f), new Vector3(-6f, -1f, 0.9f),
        };

        sealed class Wish
        {
            public int Id;
            public Vector2 From, To;
            public float Seconds, Age, EndAge = -1f;
            public bool Wished;
            public Vector2 BurstAt;
        }

        readonly RenderAssets assets;
        readonly PaletteAtlas atlas;
        readonly int seed;
        readonly PixelQuads fxQuads = new PixelQuads();
        readonly PixelQuads wishQuads = new PixelQuads();
        PixelQuads target;
        readonly List<Wish> wishes = new List<Wish>();
        readonly int[] twinkleCentre = new int[StarField.TwinkleGroups];
        readonly int[] twinkleArm = new int[StarField.TwinkleGroups];
        int[] glintRamp, meteorRamp, wishRamp, coreRamp, glowRamp;
        List<SkyStar> stars;
        readonly List<SkyStar> glints = new List<SkyStar>();
        Transform starLayer, bandLayer, fxLayer, wishLayer;
        MeshRenderer starRenderer, bandRenderer, fxRenderer, wishRenderer;
        Mesh fxMesh, wishMesh;
        bool wishDirty;
        Texture2D bandTexture;
        bool fxDirty;
        float time;

        public Transform Root { get; }
        public IReadOnlyList<SkyStar> Stars => stars;
        public Vector2Int MoonCentre { get; }
        public MeshRenderer StarRenderer => starRenderer;
        public MeshRenderer BandRenderer => bandRenderer;
        public MeshRenderer FxRenderer => fxRenderer;
        /// <summary>Wish stars + burst: additive, drawn over the city and the hotel.</summary>
        public MeshRenderer WishRenderer => wishRenderer;
        /// <summary>False only when the additive shader is missing (then wish stars fall back to the sky's fx layer).</summary>
        public bool WishInFront { get; private set; }
        public int WishQuadCount => wishQuads.Count;
        public bool StarNight { get; private set; }
        public bool BandVisible => bandRenderer != null && bandRenderer.enabled;
        /// <summary>Tests: force star night on/off (null = follow IDayQuery.IsStarNight).</summary>
        public bool? ForceStarNight { get; set; }
        /// <summary>Rainy night: clouds hide the moon, the stars and the glints (set by the weather every frame).</summary>
        public bool Overcast { get; set; }
        public bool MeteorActive { get; private set; }
        /// <summary>The meteor in flight (valid while <see cref="MeteorActive"/>).</summary>
        public SkySchedule.MeteorPose Meteor { get; private set; }
        public int ActiveGlint { get; private set; } = -1;
        public int FxQuadCount => fxQuads.Count;
        public int WishCount => wishes.Count;
        public int MeteorsSeen { get; private set; }
        int lastMeteorSlot = int.MinValue;

        public SkyView(Transform parent, RenderAssets assets, PaletteAtlas atlas, int seed)
        {
            this.assets = assets;
            this.atlas = atlas;
            this.seed = seed;
            MoonCentre = new Vector2Int(Mathf.RoundToInt(MoonViewport.x * StarField.Width), Mathf.RoundToInt(MoonViewport.y * StarField.Height));
            Root = new GameObject("Sky").transform;
            Root.SetParent(parent, false);
        }

        public int CountOf(StarKind kind)
        {
            int n = 0;
            foreach (var s in stars) if (s.Kind == kind) n++;
            return n;
        }

        // ------------------------------------------------------------------ placement (pure)

        static float Aspect => (float)RenderingStyle.PixelWidth / RenderingStyle.PixelHeight;

        /// <summary>World point at depth <paramref name="z"/> that projects onto the viewport's bottom-left corner.</summary>
        public static Vector3 LayerOrigin(CameraFrame f, float z)
        {
            float halfH = f.OrthographicSize, halfW = halfH * Aspect;
            float d = z - f.ShearDepth;
            return new Vector3(f.Position.x - halfW - f.Shear.x * d, f.Position.y - halfH - f.Shear.y * d, z);
        }

        /// <summary>World point at depth <paramref name="z"/> that projects onto viewport point <paramref name="viewport"/>.</summary>
        public static Vector3 ViewportToWorld(CameraFrame f, Vector2 viewport, float z)
        {
            float h = 2f * f.OrthographicSize;
            return LayerOrigin(f, z) + new Vector3(viewport.x * h * Aspect, viewport.y * h, 0f);
        }

        /// <summary>Viewport pixel (0..640 x 0..360, float) that world point <paramref name="w"/> projects onto.</summary>
        public static Vector2 WorldToPixels(CameraFrame f, Vector3 w)
        {
            var o = f.ToScreenPlane(LayerOrigin(f, w.z));
            return (f.ToScreenPlane(w) - o) * f.PixelsPerUnit;
        }

        static void Place(Transform layer, CameraFrame f, float z)
        {
            if (layer == null) return;
            layer.position = LayerOrigin(f, z);
            float s = 1f / f.PixelsPerUnit;
            layer.localScale = new Vector3(s, s, 1f);
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            stars = StarField.Generate(seed, MoonCentre, HaloRadius + 3);
            glints.Clear();
            foreach (var s in stars) if (s.Kind == StarKind.Glint) glints.Add(s);

            var front = assets.NewAdditive(atlas.Texture, "Wish Stars (additive)");
            WishInFront = front != null;
            // additive ramps fade to black (adds nothing), opaque ones to the sky colour
            var wishBase = WishInFront ? Color.black : RenderingStyle.Background;
            glintRamp = Ramp(GlintColor, RenderingStyle.Background);
            meteorRamp = Ramp(MeteorStops, RenderingStyle.Background);
            wishRamp = Ramp(WishStops, wishBase);
            coreRamp = Ramp(WishCore, wishBase);
            glowRamp = Ramp(WishGlow, wishBase);
            for (int g = 0; g < StarField.TwinkleGroups; g++)
            {
                twinkleCentre[g] = atlas.Dynamic(GroupColor(g));
                twinkleArm[g] = atlas.Dynamic(GroupColor(g) * 0.4f);
            }

            var q = new PixelQuads();
            BuildMoon(q);
            foreach (var s in stars)
            {
                if (s.Kind == StarKind.Glint) continue;
                int centre, arm;
                if (s.Kind == StarKind.Twinkle) { centre = twinkleCentre[s.Group]; arm = twinkleArm[s.Group]; }
                else
                {
                    centre = atlas.Static(SteadyTones[s.Tone]);
                    arm = atlas.Static(Color.Lerp(RenderingStyle.Background, SteadyTones[s.Tone], 0.42f));
                }
                if (s.Cross)
                {
                    q.Rect(s.X - 1, s.Y, 1, 1, atlas.UV(arm));
                    q.Rect(s.X + 1, s.Y, 1, 1, atlas.UV(arm));
                    q.Rect(s.X, s.Y - 1, 1, 1, atlas.UV(arm));
                    q.Rect(s.X, s.Y + 1, 1, 1, atlas.UV(arm));
                }
                q.Rect(s.X, s.Y, 1, 1, atlas.UV(centre));
            }
            var mesh = assets.Own(new Mesh { name = "Sky Stars" });
            q.Apply(mesh);
            starLayer = Layer("Stars", mesh, atlas.Material, out starRenderer);

            fxMesh = assets.Own(new Mesh { name = "Sky Fx" });
            fxMesh.MarkDynamic();
            fxLayer = Layer("Sky Fx", fxMesh, atlas.Material, out fxRenderer);
            fxRenderer.enabled = false;

            wishMesh = assets.Own(new Mesh { name = "Wish Stars" });
            wishMesh.MarkDynamic();
            wishLayer = Layer("Wish Stars", wishMesh, front != null ? front : atlas.Material, out wishRenderer);
            wishRenderer.enabled = false;
        }

        Transform Layer(string name, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            renderer = PaletteRenderer.Create(Root, name, mesh, material);
            renderer.allowOcclusionWhenDynamic = false;
            return renderer.transform;
        }

        static Color GroupColor(int g) => (g & 1) == 0 ? TwinkleCool : TwinkleWarm;

        int[] Ramp(Color c, Color from)
        {
            var r = new int[RampSteps];
            for (int i = 0; i < RampSteps; i++) r[i] = atlas.Static(Color.Lerp(from, c, i / (RampSteps - 1f)));
            return r;
        }

        /// <summary>Ramp through <paramref name="stops"/> (stops[0] = full level, last = faintest, then <paramref name="sky"/>).</summary>
        int[] Ramp(Color[] stops, Color sky)
        {
            var r = new int[RampSteps];
            int n = stops.Length;
            for (int i = 0; i < RampSteps; i++)
            {
                float level = i / (RampSteps - 1f);
                float f = (1f - level) * n; // 0 = first stop ... n = sky
                int k = Mathf.Min(n - 1, Mathf.FloorToInt(f));
                var next = k + 1 < n ? stops[k + 1] : sky;
                r[i] = atlas.Static(Color.Lerp(stops[k], next, f - k));
            }
            return r;
        }

        /// <summary>Pixel-art moon: shaded disc (lit from the upper left) with craters, two dithered halo rings.</summary>
        void BuildMoon(PixelQuads q)
        {
            int size = HaloRadius * 2 + 1;
            var grid = new int[size * size];
            int light = atlas.Static(MoonLight), bright = atlas.Static(MoonBright), shade = atlas.Static(MoonShade), rim = atlas.Static(MoonRim);
            int crater = atlas.Static(MoonCrater), craterDark = atlas.Static(MoonCraterDark);
            int haloNear = atlas.Static(HaloNear), haloFar = atlas.Static(HaloFar);
            float r = MoonRadius;
            for (int dy = -HaloRadius; dy <= HaloRadius; dy++)
                for (int dx = -HaloRadius; dx <= HaloRadius; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    int texel = -1;
                    if (d <= r + 0.35f)
                    {
                        // terminator towards the lower right, highlight upper left
                        float lit = (-dx * 0.62f + dy * 0.78f) / r;
                        texel = lit < -0.78f ? rim : lit < -0.45f ? shade : lit > 0.55f && d < r - 1f ? bright : light;
                        foreach (var c in Craters)
                        {
                            float cx = dx - c.x, cy = dy - c.y;
                            if (cx * cx + cy * cy > c.z * c.z) continue;
                            // crater: darker lower-right inner wall
                            texel = cx - cy > 0.3f * c.z ? craterDark : crater;
                            break;
                        }
                    }
                    // halo: 50% checker, then a 25% grid, then a sparse 12.5% grid (classic pixel-art dither falloff)
                    else if (d <= r + 3.5f) { if (((dx + dy) & 1) == 0) texel = haloNear; }
                    else if (d <= r + 5.5f) { if ((dx & 1) == 0 && (dy & 1) == 0) texel = haloFar; }
                    else if (d <= HaloRadius + 0.5f) { if ((dy & 1) == 0 && ((dx + (dy & 2)) & 3) == 0) texel = haloFar; }
                    grid[(dy + HaloRadius) * size + dx + HaloRadius] = texel;
                }
            // one quad per horizontal run of the same texel
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size;)
                {
                    int t = grid[y * size + x];
                    int end = x + 1;
                    while (end < size && grid[y * size + end] == t) end++;
                    if (t >= 0) q.Rect(MoonCentre.x - HaloRadius + x, MoonCentre.y - HaloRadius + y, end - x, 1, atlas.UV(t));
                    x = end;
                }
        }

        // ------------------------------------------------------------------ star night band (baked once, lazily)

        static readonly float[] Bayer4 =
        {
            0f, 8f, 2f, 10f,
            12f, 4f, 14f, 6f,
            3f, 11f, 1f, 9f,
            15f, 7f, 13f, 5f,
        };

        static float Dither(int x, int y) => (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

        static float ValueNoise(int seed, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = LightSchedule.Hash(seed, ix, iy, 90), b = LightSchedule.Hash(seed, ix + 1, iy, 90);
            float c = LightSchedule.Hash(seed, ix, iy + 1, 90), d = LightSchedule.Hash(seed, ix + 1, iy + 1, 90);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Milky Way centre line (pixel y) at pixel column <paramref name="x"/>: low on the left, high on the right.</summary>
        public static float BandCentre(float x) => 190f + 0.235f * (x + 20f) + 7f * Mathf.Sin(x * 0.011f + 0.7f) + 3f * Mathf.Sin(x * 0.037f + 2.1f);

        public const float BandHalfWidth = 30f;

        public static readonly Color StarNightTop = new Color(0.030f, 0.028f, 0.095f);
        public static readonly Color StarNightHorizon = new Color(0.085f, 0.065f, 0.165f);

        /// <summary>
        /// Star-night sky (pure, seeded): dithered indigo-to-violet gradient, a Milky Way band of dithered glow with a dark
        /// rift and clumps, dense faint stars inside it and a few across the sky. 640x360, row-major from the bottom.
        /// </summary>
        public static Color32[] BakeStarNight(int seed)
        {
            int w = StarField.Width, h = StarField.Height;
            var px = new Color32[w * h];
            const int gradientSteps = 5;
            var white = new Color(0.90f, 0.92f, 1f);
            var glowTint = new[] { Color.black, new Color(0.040f, 0.036f, 0.070f), new Color(0.090f, 0.078f, 0.135f), new Color(0.165f, 0.145f, 0.215f) };
            float cos = Mathf.Cos(Mathf.Atan(0.235f));
            for (int y = 0; y < h; y++)
            {
                float g = 1f - y / (h - 1f);
                for (int x = 0; x < w; x++)
                {
                    float dither = Dither(x, y);
                    int gi = Mathf.Clamp(Mathf.FloorToInt(g * g * (gradientSteps - 1) + dither), 0, gradientSteps - 1);
                    var c = Color.Lerp(StarNightTop, StarNightHorizon, gi / (gradientSteps - 1f));

                    float hw = BandHalfWidth + 6f * Mathf.Sin(x * 0.017f + 1.3f);
                    float u = (y - BandCentre(x)) * cos / hw;
                    float glow = 0f;
                    if (u > -2.2f && u < 2.2f)
                    {
                        glow = Mathf.Exp(-2.2f * u * u) * (0.6f + 0.4f * ValueNoise(seed, x / 9f, y / 7f));
                        float riftU = (u - 0.1f - 0.18f * Mathf.Sin(x * 0.021f)) / 0.17f;
                        float rift = Mathf.Exp(-riftU * riftU) * (0.45f + 0.55f * ValueNoise(seed + 1, x / 22f, y / 18f));
                        glow *= 1f - 0.65f * rift;
                        int bi = Mathf.Clamp(Mathf.FloorToInt(glow * 3.3f + dither - 0.35f), 0, 3);
                        c += glowTint[bi];
                    }
                    float hs = LightSchedule.Hash(seed, x, y, 80);
                    if (hs < 0.0011f + 0.04f * glow * glow)
                    {
                        float b = 0.25f + 0.55f * LightSchedule.Hash(seed, x, y, 81) * (0.55f + glow);
                        c = Color.Lerp(c, white, Mathf.Clamp01(b));
                    }
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            return px;
        }

        void EnsureBand()
        {
            if (bandRenderer != null) return;
            bandTexture = assets.Own(new Texture2D(StarField.Width, StarField.Height, TextureFormat.RGBA32, false)
            {
                name = "Star Night Sky",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            });
            bandTexture.SetPixels32(BakeStarNight(seed));
            bandTexture.Apply(false, true);
            var mesh = assets.Own(new Mesh { name = "Star Night Sky" });
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, StarField.Height, 0f),
                new Vector3(StarField.Width, StarField.Height, 0f), new Vector3(StarField.Width, 0f, 0f),
            });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            var material = assets.NewUnlit(Color.white, bandTexture);
            material.name = "Star Night Sky";
            bandLayer = Layer("Star Night Sky", mesh, material, out bandRenderer);
        }

        // ------------------------------------------------------------------ wish stars (events)

        public void WishAppeared(int id, float fromX, float fromY, float toX, float toY, float seconds)
        {
            Remove(id);
            wishes.Add(new Wish { Id = id, From = new Vector2(fromX, fromY), To = new Vector2(toX, toY), Seconds = Mathf.Max(0.05f, seconds) });
            fxDirty = true;
        }

        public void WishMade(int id)
        {
            var w = Find(id);
            if (w == null || w.EndAge >= 0f) return;
            w.Wished = true;
            w.EndAge = w.Age;
            w.BurstAt = HeadPixels(w);
            fxDirty = true;
        }

        public void WishFaded(int id)
        {
            var w = Find(id);
            if (w == null || w.EndAge >= 0f) return;
            w.EndAge = w.Age;
            fxDirty = true;
        }

        public void ClearWishes()
        {
            wishes.Clear();
            fxDirty = true;
        }

        Wish Find(int id)
        {
            foreach (var w in wishes) if (w.Id == id) return w;
            return null;
        }

        void Remove(int id)
        {
            for (int i = wishes.Count - 1; i >= 0; i--) if (wishes[i].Id == id) wishes.RemoveAt(i);
        }

        static Vector2 HeadViewport(Wish w, float age) => Vector2.Lerp(w.From, w.To, Mathf.Clamp01(age / w.Seconds));
        static Vector2 ToPixels(Vector2 viewport) => new Vector2(viewport.x * StarField.Width, viewport.y * StarField.Height);
        static Vector2 HeadPixels(Wish w) => ToPixels(HeadViewport(w, w.EndAge >= 0f ? w.EndAge : w.Age));

        /// <summary>Viewport position of a wish star's head (exact, before pixel rounding).</summary>
        public bool TryGetWishHead(int id, out Vector2 viewport)
        {
            var w = Find(id);
            viewport = w != null ? HeadViewport(w, w.EndAge >= 0f ? w.EndAge : w.Age) : default;
            return w != null;
        }

        public bool IsWishEnded(int id)
        {
            var w = Find(id);
            return w == null || w.EndAge >= 0f;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>
        /// <paramref name="skyTime"/> = juice clock (twinkles, glints, meteors), <paramref name="wishDt"/> = juice dt that
        /// also stops during the tutorial hold (wish stars freeze with the simulation).
        /// </summary>
        public void Tick(float skyTime, float wishDt, CameraFrame frame, bool starNight)
        {
            if (stars == null) return;
            time = skyTime;
            StarNight = ForceStarNight ?? starNight;
            if (StarNight) EnsureBand();
            if (bandRenderer != null) bandRenderer.enabled = StarNight;
            starRenderer.enabled = !Overcast;

            Place(starLayer, frame, StarDepth);
            Place(bandLayer, frame, BandDepth);
            Place(fxLayer, frame, FxDepth);
            Place(wishLayer, frame, FxDepth);

            for (int g = 0; g < StarField.TwinkleGroups; g++)
            {
                float l = SkySchedule.TwinkleLevel(seed, g, skyTime);
                atlas.Set(twinkleCentre[g], Color.Lerp(RenderingStyle.Background, GroupColor(g), l));
                atlas.Set(twinkleArm[g], Color.Lerp(RenderingStyle.Background, GroupColor(g), l * 0.42f));
            }

            for (int i = wishes.Count - 1; i >= 0; i--)
            {
                var w = wishes[i];
                w.Age += Mathf.Max(0f, wishDt);
                // safety: the gameplay always ends a star, but never let one hang around forever
                if (w.EndAge < 0f && w.Age > w.Seconds + 6f) w.EndAge = w.Age;
                if (w.EndAge >= 0f && w.Age - w.EndAge > Mathf.Max(SkySchedule.WishFadeOut, Mathf.Max(SkySchedule.SparkleLife, SkySchedule.BurstLife)) + 0.05f)
                    wishes.RemoveAt(i);
            }
            RebuildFx();
        }

        void RebuildFx()
        {
            fxQuads.Clear();
            wishQuads.Clear();
            target = fxQuads;
            int gi = -1;
            float glint = Overcast ? 0f : SkySchedule.Glint(seed, glints.Count, time, out gi);
            ActiveGlint = gi;
            if (gi >= 0)
            {
                var s = glints[gi];
                Sparkle(s.X, s.Y, glint, glintRamp, glint > 0.75f ? 2 : glint > 0.4f ? 1 : 0, false);
            }
            MeteorActive = false;
            if (StarNight && SkySchedule.Meteor(seed, time, out var m))
            {
                MeteorActive = true;
                Meteor = m;
                if (m.Slot != lastMeteorSlot) { lastMeteorSlot = m.Slot; MeteorsSeen++; }
                Streak(m.Head, m.Dir, m.Tail, m.Level, meteorRamp, false);
            }
            // wish stars: their own layer drawn over the world (or the fx layer if the additive shader is missing)
            if (WishInFront) target = wishQuads;
            foreach (var w in wishes) DrawWish(w);

            bool any = fxQuads.Count > 0;
            if (any || fxDirty)
            {
                fxQuads.Apply(fxMesh);
                fxDirty = any;
            }
            fxRenderer.enabled = any;

            bool anyWish = wishQuads.Count > 0;
            if (anyWish || wishDirty)
            {
                wishQuads.Apply(wishMesh);
                wishDirty = anyWish;
            }
            wishRenderer.enabled = anyWish;
        }

        void Px(int x, int y, float level, int[] ramp)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(level * (RampSteps - 1)), 0, RampSteps - 1);
            if (i <= 0) return;
            if (x < 0 || y < 0 || x >= StarField.Width || y >= StarField.Height) return;
            target.Rect(x, y, 1, 1, atlas.UV(ramp[i]));
        }

        /// <summary>"+" sparkle: dim parts first, the bright core last (same depth: later quads win).</summary>
        void Sparkle(int x, int y, float level, int[] ramp, int arm, bool diagonals)
        {
            for (int r = arm; r >= 1; r--)
            {
                float l = level * (r == 1 ? 0.62f : r == 2 ? 0.32f : 0.16f);
                Px(x - r, y, l, ramp); Px(x + r, y, l, ramp);
                Px(x, y - r, l, ramp); Px(x, y + r, l, ramp);
            }
            if (diagonals)
            {
                float l = level * 0.3f;
                Px(x - 1, y - 1, l, ramp); Px(x + 1, y - 1, l, ramp);
                Px(x - 1, y + 1, l, ramp); Px(x + 1, y + 1, l, ramp);
            }
            Px(x, y, level, ramp);
        }

        /// <summary>Streak from <paramref name="head"/> back against <paramref name="dir"/>, fading along its tail.</summary>
        void Streak(Vector2 head, Vector2 dir, int tail, float level, int[] ramp, bool thick)
        {
            int lastX = int.MinValue, lastY = int.MinValue;
            var perp = new Vector2(-dir.y, dir.x);
            for (int i = tail; i >= 1; i--)
            {
                var p = head - dir * i;
                int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
                if (x == lastX && y == lastY) continue;
                lastX = x; lastY = y;
                float k = 1f - i / (tail + 1f);
                float l = level * Mathf.Pow(k, 1.3f);
                if (thick && i < tail * 0.4f)
                {
                    var q = p + perp;
                    var r = p - perp;
                    Px(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y), l * 0.35f, ramp);
                    Px(Mathf.FloorToInt(r.x), Mathf.FloorToInt(r.y), l * 0.35f, ramp);
                }
                Px(x, y, l, ramp);
            }
            Px(Mathf.FloorToInt(head.x), Mathf.FloorToInt(head.y), level, ramp);
        }

        void DrawWish(Wish w)
        {
            float headLevel = SkySchedule.WishHeadLevel(w.Age, w.EndAge);
            var dirPx = ToPixels(w.To - w.From);
            var dir = dirPx.sqrMagnitude > 1e-6f ? dirPx.normalized : Vector2.right;
            var perp = new Vector2(-dir.y, dir.x);

            // sparkles shed along the path (stateless: emission j at star time j * SparkleEvery)
            float emitEnd = w.EndAge >= 0f ? w.EndAge : w.Age;
            int j0 = Mathf.Max(0, Mathf.FloorToInt((w.Age - SkySchedule.SparkleLife) / SkySchedule.SparkleEvery) + 1);
            int j1 = Mathf.FloorToInt(emitEnd / SkySchedule.SparkleEvery);
            for (int j = j0; j <= j1; j++)
            {
                float tj = j * SkySchedule.SparkleEvery;
                float age = w.Age - tj;
                float l = SkySchedule.SparkleLevel(age) * 0.95f;
                if (l <= 0f) continue;
                var at = ToPixels(HeadViewport(w, tj)) + perp * ((LightSchedule.Hash(w.Id, j, 75, 0) - 0.5f) * 6f) + new Vector2(0f, -7f * age);
                int x = Mathf.FloorToInt(at.x), y = Mathf.FloorToInt(at.y);
                if (l > 0.55f && LightSchedule.Hash(w.Id, j, 76, 0) < 0.3f) Sparkle(x, y, l, wishRamp, 1, false);
                else Px(x, y, l, wishRamp);
            }

            if (headLevel > 0f)
            {
                var head = HeadPixels(w);
                if (WishInFront) Glow(Mathf.FloorToInt(head.x), Mathf.FloorToInt(head.y), headLevel);
                int tail = Mathf.RoundToInt(SkySchedule.WishTail * Mathf.Clamp01(w.Age / 0.5f));
                if (tail > 0) Streak(head, dir, tail, headLevel * 0.9f, wishRamp, true);
                int hx = Mathf.FloorToInt(head.x), hy = Mathf.FloorToInt(head.y);
                Sparkle(hx, hy, headLevel, wishRamp, (SkySchedule.WishPulse(w.Age) ? 3 : 2) + (WishInFront ? 1 : 0), true);
                // white-hot core
                Px(hx, hy, headLevel, coreRamp);
                Px(hx - 1, hy, headLevel * 0.85f, coreRamp); Px(hx + 1, hy, headLevel * 0.85f, coreRamp);
                Px(hx, hy - 1, headLevel * 0.85f, coreRamp); Px(hx, hy + 1, headLevel * 0.85f, coreRamp);
            }

            if (w.Wished)
            {
                float age = w.Age - w.EndAge;
                for (int i = 0; i < SkySchedule.BurstCount; i++)
                {
                    float l = SkySchedule.BurstLevel(age, i);
                    if (l <= 0f) continue;
                    var at = w.BurstAt + SkySchedule.BurstOffset(age, i);
                    int x = Mathf.FloorToInt(at.x), y = Mathf.FloorToInt(at.y);
                    if (i % 3 == 0 && l > 0.5f) Sparkle(x, y, l, wishRamp, 1, false);
                    else Px(x, y, l, i % 2 == 0 ? wishRamp : coreRamp);
                }
            }
        }

        /// <summary>
        /// Soft pixel-art glow around a wish star's head: dithered rings (50% checker inside, sparse grid outside) of a warm
        /// tint ADDED to whatever is behind - over a dark wall or the sky it reads as a halo, never as a box.
        /// </summary>
        void Glow(int cx, int cy, float level)
        {
            int r = WishGlowRadius;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 < 9 || d2 > r * r) continue; // the head + its sparkle arms cover the middle
                    float d = Mathf.Sqrt(d2);
                    bool on = d <= 4.5f ? ((cx + dx + cy + dy) & 1) == 0 : ((cx + dx) & 1) == 0 && ((cy + dy) & 1) == 0;
                    if (!on) continue;
                    Px(cx + dx, cy + dy, level * Mathf.Lerp(0.62f, 0.24f, (d - 3f) / (r - 3f)), glowRamp);
                }
        }

        /// <summary>Determinism fingerprint: star field + band bake.</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            foreach (var s in stars) sb.Append(s.X).Append(',').Append(s.Y).Append(',').Append((int)s.Kind).Append(s.Cross ? '+' : '.').Append(';');
            return sb.ToString();
        }
    }

    /// <summary>Whole-pixel quads (layer units = target pixels) pointing at one palette texel each.</summary>
    sealed class PixelQuads
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public int Count => v.Count / 4;

        public void Clear()
        {
            v.Clear(); uv.Clear(); t.Clear();
        }

        public void Rect(int x, int y, int w, int h, Vector2 texelUv)
        {
            int i = v.Count;
            v.Add(new Vector3(x, y, 0f)); v.Add(new Vector3(x, y + h, 0f));
            v.Add(new Vector3(x + w, y + h, 0f)); v.Add(new Vector3(x + w, y, 0f));
            uv.Add(texelUv); uv.Add(texelUv); uv.Add(texelUv); uv.Add(texelUv);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Apply(Mesh m)
        {
            m.Clear();
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }
    }
}
