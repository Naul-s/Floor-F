using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FloorF.Rendering
{
    /// <summary>
    /// Banded pixel lighting for emissive things (neon, the HOTEL sign, street lamps, lit windows, the moon, car lights,
    /// wish stars) inside the 640x360 pipeline - pixel-art light, not bloom: every halo is a few FLAT colour bands
    /// (posterised steps; no dither, no noise) in the source's hue, screen-blended so it tints the walls behind.
    /// Right after the world camera filled the target (same render context, so before the UI camera):
    ///   prefilter (161x91, world-anchored 4x4 cells) -> blur H -> blur V -> composite target + light into <see cref="LitTexture"/>
    /// (Resources/Rendering/Shaders/FloorF_PixelGlow). The pixel screen shows the lit texture while on and the raw
    /// target while off. Authored radial <see cref="LightPool"/>s (street lamps on the sidewalk, shop neon spilling on
    /// the pavement, the HOTEL sign on the roof) are added before the banding, so they get the same steps.
    /// Stable: the light grid moves with the world (camera pixel position mod 4), pools are projected on whole pixels and
    /// the brightness weight saturates below the neon's slow hum, so bands do not crawl or breathe.
    /// Cost: 3 draws on 14,651 pixels + 1 on 230,400 (&lt;= 24 pools each) - WebGL2-safe.
    /// Off switch: <see cref="DefaultOn"/> (const) or <see cref="Enabled"/> at runtime (tests capture both).
    /// </summary>
    sealed class PixelGlow : IDisposable
    {
        /// <summary>Flip to false to ship without the lighting (e.g. if a low-end WebGL device struggles).</summary>
        public const bool DefaultOn = true;
        public const int Downsample = 4;
        public const int MaxPools = 24;
        public const int BandCount = 4;
        public const string GlowShaderPath = "Rendering/Shaders/FloorF_PixelGlow";

        public const int PassPrefilter = 0, PassBlurH = 1, PassBlurV = 2, PassComposite = 3, PassLightOnly = 4, PassDissolve = 5;
        static readonly int PrevTexId = Shader.PropertyToID("_PrevTex");
        static readonly int MixId = Shader.PropertyToID("_Mix");

        /// <summary>Source weight from the max channel (gamma): lit windows (x..y) count z, bright emitters (Strong) fully.</summary>
        public static readonly Vector4 Weak = new Vector4(0.70f, 0.78f, 0.05f, 0f);
        /// <summary>Saturates below the neon hum's low point (0.91), so humming signs keep the same halo.</summary>
        public static readonly Vector4 Strong = new Vector4(0.80f, 0.90f, 0f, 0f);
        public const float Gain = 5f;
        /// <summary>Light thresholds (max channel) of the bands, faint to bright.</summary>
        public static readonly Vector4 Bands = new Vector4(0.10f, 0.22f, 0.40f, 0.65f);
        /// <summary>Brightness (gamma, screen-blended) of each band.</summary>
        public static readonly Vector4 Levels = new Vector4(0.07f, 0.12f, 0.18f, 0.26f);

        /// <summary>Authored radial light: an ellipse (world units, screen-plane radii) around a world point.</summary>
        public readonly struct LightPool
        {
            public readonly Vector3 World;
            public readonly Vector2 Radius;
            /// <summary>Hue times intensity (light units; the bands start at <see cref="Bands"/>.x).</summary>
            public readonly Color Color;
            public LightPool(Vector3 world, Vector2 radius, Color color) { World = world; Radius = radius; Color = color; }
        }

        static readonly int GlowTexId = Shader.PropertyToID("_GlowTex");
        static readonly int OffsetId = Shader.PropertyToID("_Offset");
        static readonly int WeakId = Shader.PropertyToID("_Weak");
        static readonly int StrongId = Shader.PropertyToID("_Strong");
        static readonly int BandsId = Shader.PropertyToID("_Bands");
        static readonly int LevelsId = Shader.PropertyToID("_Levels");
        static readonly int GainId = Shader.PropertyToID("_Gain");
        static readonly int LinearId = Shader.PropertyToID("_Linear");
        static readonly int PoolCountId = Shader.PropertyToID("_PoolCount");
        static readonly int PoolRectId = Shader.PropertyToID("_PoolRect");
        static readonly int PoolColorId = Shader.PropertyToID("_PoolColor");

        readonly RenderTexture source;
        readonly Camera worldCamera;
        readonly PixelScreen screen;
        readonly Material glow;
        readonly CommandBuffer cmd;
        readonly List<LightPool> pools = new List<LightPool>();
        readonly Vector4[] poolRects = new Vector4[MaxPools];
        readonly Vector4[] poolColors = new Vector4[MaxPools];
        RenderTexture a, b, lit;
        bool enabled;
        // ordered dissolve between two scenic zoom rungs: snapshot of what was shown + the mixed output
        RenderTexture prev, mixed;
        bool snapshotPending;
        float dissolveElapsed, dissolveSeconds;

        /// <summary>An ordered pixel dissolve from the previous zoom rung is on screen (<see cref="BeginDissolve"/>).</summary>
        public bool Dissolving { get; private set; }
        /// <summary>0..1: share of target pixels already showing the new rung.</summary>
        public float DissolveMix => dissolveSeconds > 0f ? Mathf.Clamp01(dissolveElapsed / dissolveSeconds) : 1f;
        /// <summary>Dissolves started so far (tests).</summary>
        public int DissolvesStarted { get; private set; }
        /// <summary>Output of the dissolve (null before the first one).</summary>
        public RenderTexture DissolveTexture => mixed;

        /// <summary>
        /// Pure, mirrors the shader's pass 5: target pixel (x, y) already shows the new rung at <paramref name="mix"/>
        /// (4x4 Bayer order: 1/16 of the pixels per step, evenly spread, no noise).
        /// </summary>
        public static bool DissolveShowsNew(int x, int y, float mix) => (BayerIndex(x, y) + 0.5f) / 16f < mix;

        public static int BayerIndex(int x, int y)
        {
            int Bayer2(int u, int v) => 2 * System.Math.Abs(u - v) + v;
            return 4 * Bayer2(x & 1, y & 1) + Bayer2((x >> 1) & 1, (y >> 1) & 1);
        }

        /// <summary>Blurred light buffer (161x91, bilinear) - before gain, pools and banding.</summary>
        public RenderTexture GlowTexture => a;
        /// <summary>Target + banded light (640x360, point): what the pixel screen shows while on.</summary>
        public RenderTexture LitTexture => lit;
        public Material Material => glow;
        /// <summary>Lighting passes executed so far (tests: it runs every frame while on, never while off).</summary>
        public int Passes { get; private set; }
        /// <summary>False when the shader is missing / unsupported: the pixel screen then shows the raw target.</summary>
        public bool Available => glow != null;
        /// <summary>Light grid offset (target pixels, 0..3) of the frame applied last.</summary>
        public Vector2Int GridOffset { get; private set; }
        /// <summary>Pools projected into the current frame (tests).</summary>
        public int VisiblePools { get; private set; }
        public IReadOnlyList<LightPool> Pools => pools;

        public bool Enabled
        {
            get => enabled && Available;
            set
            {
                enabled = value;
                if (!Enabled) StopDissolve();
                screen.SetTexture(Shown, source.width, source.height);
            }
        }

        Texture Shown => !Enabled ? source : Dissolving && mixed != null ? mixed : (Texture)lit;

        /// <summary>
        /// Scenic zoom stepped to another rung: the next render snapshots what is on screen now and the new rung
        /// replaces it in Bayer order over <paramref name="seconds"/> (unscaled). No-op while the lighting is off
        /// (the rung then simply snaps).
        /// </summary>
        public void BeginDissolve(float seconds)
        {
            if (!Enabled || seconds <= 0f) return;
            if (prev == null) prev = NewTarget("FloorF Dissolve From", source.width, source.height, FilterMode.Point);
            if (mixed == null) mixed = NewTarget("FloorF Dissolve", source.width, source.height, FilterMode.Point);
            // snapshot BEFORE switching the screen: Record copies the texture shown so far (lit, or a dissolve in flight)
            snapshotPending = true;
            snapshotFrom = Shown;
            Dissolving = true;
            dissolveElapsed = 0f;
            dissolveSeconds = seconds;
            DissolvesStarted++;
            glow.SetFloat(MixId, 0f);
            screen.SetTexture(Shown, source.width, source.height);
        }

        Texture snapshotFrom;

        /// <summary>Unscaled clock: advances the dissolve, back to the plain lit target once it is complete.</summary>
        public void TickDissolve(float dt)
        {
            if (!Dissolving) return;
            if (!Enabled) { StopDissolve(); return; }
            dissolveElapsed += Mathf.Max(0f, dt);
            glow.SetFloat(MixId, DissolveMix);
            if (dissolveElapsed >= dissolveSeconds && !snapshotPending) StopDissolve();
        }

        void StopDissolve()
        {
            if (!Dissolving) return;
            Dissolving = false;
            snapshotPending = false;
            if (screen != null && source != null) screen.SetTexture(Shown, source.width, source.height);
        }

        public PixelGlow(RenderTexture source, Camera worldCamera, PixelScreen screen)
        {
            this.source = source;
            this.worldCamera = worldCamera;
            this.screen = screen;
            var shader = Resources.Load<Shader>(GlowShaderPath);
            if (shader != null && shader.isSupported)
            {
                glow = new Material(shader) { name = "Pixel Light", hideFlags = HideFlags.DontSave };
                glow.SetVector(WeakId, Weak);
                glow.SetVector(StrongId, Strong);
                glow.SetVector(BandsId, Bands);
                glow.SetVector(LevelsId, Levels);
                glow.SetFloat(GainId, Gain);
                glow.SetFloat(LinearId, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
                glow.SetVectorArray(PoolRectId, poolRects);
                glow.SetVectorArray(PoolColorId, poolColors);
                glow.SetFloat(PoolCountId, 0f);
                int w = source.width / Downsample + 1, h = source.height / Downsample + 1;
                a = NewTarget("FloorF Light A", w, h, FilterMode.Bilinear);
                b = NewTarget("FloorF Light B", w, h, FilterMode.Bilinear);
                lit = NewTarget("FloorF Lit Target", source.width, source.height, FilterMode.Point);
                glow.SetTexture(GlowTexId, a);
                glow.SetVector(OffsetId, new Vector4(0f, 0f, w, h));
                cmd = new CommandBuffer { name = "FloorF Pixel Light" };
                RenderPipelineManager.endCameraRendering += OnEndCamera;
            }
            else Debug.LogWarning("[Rendering] light shader missing under Resources/" + GlowShaderPath + " - lighting off");
            Enabled = DefaultOn;
        }

        static RenderTexture NewTarget(string name, int w, int h, FilterMode filter)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
            {
                msaaSamples = 1,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = false,
                autoGenerateMips = false,
            })
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        /// <summary>Authored pools (replaces the previous set). Only the ones on screen are drawn.</summary>
        public void SetPools(IEnumerable<LightPool> list)
        {
            pools.Clear();
            if (list != null) pools.AddRange(list);
        }

        /// <summary>Pure: light grid offset (0..3 per axis) for a camera centred on screen-plane pixel <paramref name="cameraPixels"/>.</summary>
        public static Vector2Int GridOffsetFor(Vector2 cameraPixels)
        {
            int x = Mathf.RoundToInt(cameraPixels.x), y = Mathf.RoundToInt(cameraPixels.y);
            return new Vector2Int(((x % Downsample) + Downsample) % Downsample, ((y % Downsample) + Downsample) % Downsample);
        }

        /// <summary>Pure: target pixel (continuous, origin bottom-left) a world point projects onto in frame <paramref name="f"/>.</summary>
        public static Vector2 ToTargetPixels(CameraFrame f, Vector3 world, int width, int height)
            => (f.ToScreenPlane(world) - new Vector2(f.Position.x, f.Position.y)) * f.PixelsPerUnit + new Vector2(width * 0.5f, height * 0.5f);

        /// <summary>Called with every applied camera frame: grid offset + pool projection (whole pixels).</summary>
        public void SetFrame(CameraFrame f)
        {
            if (!Available) return;
            float ppu = f.PixelsPerUnit;
            GridOffset = GridOffsetFor(new Vector2(f.Position.x * ppu, f.Position.y * ppu));
            glow.SetVector(OffsetId, new Vector4(GridOffset.x, GridOffset.y, a.width, a.height));
            int n = 0, w = source.width, h = source.height;
            foreach (var p in pools)
            {
                if (n >= MaxPools) break;
                var c = ToTargetPixels(f, p.World, w, h);
                c = new Vector2(Mathf.Round(c.x), Mathf.Round(c.y));
                float rx = Mathf.Max(1f, p.Radius.x * ppu), ry = Mathf.Max(1f, p.Radius.y * ppu);
                if (c.x + rx < 0f || c.x - rx > w || c.y + ry < 0f || c.y - ry > h) continue;
                poolRects[n] = new Vector4(c.x, c.y, 1f / rx, 1f / ry);
                poolColors[n] = new Vector4(p.Color.r, p.Color.g, p.Color.b, 0f);
                n++;
            }
            VisiblePools = n;
            glow.SetVectorArray(PoolRectId, poolRects);
            glow.SetVectorArray(PoolColorId, poolColors);
            glow.SetFloat(PoolCountId, n);
        }

        void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != worldCamera || !Enabled || source == null) return;
            Record();
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
            Passes++;
        }

        void Record()
        {
            cmd.Clear();
            if (snapshotPending && snapshotFrom != null && prev != null)
            {
                // what the player saw last frame becomes the dissolve's "from" image (before lit is overwritten)
                cmd.Blit(snapshotFrom, prev);
                snapshotPending = false;
                glow.SetTexture(PrevTexId, prev);
            }
            cmd.Blit(source, a, glow, PassPrefilter);
            cmd.Blit(a, b, glow, PassBlurH);
            cmd.Blit(b, a, glow, PassBlurV);
            cmd.Blit(source, lit, glow, PassComposite);
            if (Dissolving && mixed != null && prev != null) cmd.Blit(lit, mixed, glow, PassDissolve);
        }

        /// <summary>All passes right now (e.g. after an off-frame render of the world camera).</summary>
        public void Run()
        {
            if (!Available || source == null) return;
            Record();
            Graphics.ExecuteCommandBuffer(cmd);
            Passes++;
        }

        /// <summary>Tests: the banded light alone (current light buffer + pools) into <paramref name="dst"/> (target size).</summary>
        public void RenderLightOnly(RenderTexture dst)
        {
            if (Available) Graphics.Blit(source, dst, glow, PassLightOnly);
        }

        public void Dispose()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (screen != null) screen.SetTexture(source, source.width, source.height);
            cmd?.Release();
            Release(ref a);
            Release(ref b);
            Release(ref lit);
            Release(ref prev);
            Release(ref mixed);
            if (glow != null) Object.Destroy(glow);
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }
    }
}
