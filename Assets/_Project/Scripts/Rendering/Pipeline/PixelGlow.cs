using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pixel-art glow for emissive things (neon, lit windows, lamps, the HOTEL sign, car lights) inside the 640x360
    /// pipeline: right after the world camera filled the target (same render context, so it is ordered after the world
    /// and before the UI camera), the target is thresholded + averaged down to 160x90 and blurred once at that size
    /// (two tiny blits); the pixel screen's material adds it back quantised and
    /// Bayer-dithered per low-res pixel (Resources/Rendering/Shaders/FloorF_PixelGlow + FloorF_PixelScreenGlow).
    /// Cost: 2 draws on 14,400 pixels + a few ALU in the screen draw that already existed. WebGL2-safe.
    /// Off switch: <see cref="DefaultOn"/> (const) or <see cref="Enabled"/> at runtime (tests capture both).
    /// </summary>
    sealed class PixelGlow : IDisposable
    {
        /// <summary>Flip to false to ship without the glow (e.g. if a low-end WebGL device struggles).</summary>
        public const bool DefaultOn = true;
        public const int Downsample = 4;
        public const string GlowShaderPath = "Rendering/Shaders/FloorF_PixelGlow";
        public const string ScreenShaderPath = "Rendering/Shaders/FloorF_PixelScreenGlow";
        public const float Threshold = 0.75f;
        public const float Strength = 0.95f;
        public const int Steps = 10;

        static readonly int GlowTexId = Shader.PropertyToID("_GlowTex");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int StrengthId = Shader.PropertyToID("_GlowStrength");
        static readonly int StepsId = Shader.PropertyToID("_Steps");
        static readonly int ResId = Shader.PropertyToID("_Res");

        readonly RenderTexture source;
        readonly Camera worldCamera;
        readonly PixelScreen screen;
        readonly Material glow;
        readonly CommandBuffer cmd;
        RenderTexture a, b;
        bool enabled;

        /// <summary>Material of the pixel screen while the glow is on.</summary>
        public Material ScreenMaterial { get; }
        public RenderTexture GlowTexture => b;
        /// <summary>Glow passes executed so far (tests: it runs every frame while on, never while off).</summary>
        public int Passes { get; private set; }
        /// <summary>False when a shader is missing / unsupported: the pixel screen then stays on the default UI material.</summary>
        public bool Available => glow != null && ScreenMaterial != null;

        public bool Enabled
        {
            get => enabled && Available;
            set
            {
                enabled = value;
                screen.SetMaterial(Enabled ? ScreenMaterial : null);
            }
        }

        public PixelGlow(RenderTexture source, Camera worldCamera, PixelScreen screen)
        {
            this.source = source;
            this.worldCamera = worldCamera;
            this.screen = screen;
            var glowShader = Resources.Load<Shader>(GlowShaderPath);
            var screenShader = Resources.Load<Shader>(ScreenShaderPath);
            if (glowShader != null && glowShader.isSupported && screenShader != null && screenShader.isSupported)
            {
                glow = new Material(glowShader) { name = "Pixel Glow", hideFlags = HideFlags.DontSave };
                glow.SetFloat(ThresholdId, Threshold);
                ScreenMaterial = new Material(screenShader) { name = "Pixel Screen Glow", hideFlags = HideFlags.DontSave };
                ScreenMaterial.SetFloat(StrengthId, Strength);
                ScreenMaterial.SetFloat(StepsId, Steps);
                ScreenMaterial.SetVector(ResId, new Vector4(source.width, source.height, 0f, 0f));
                int w = Mathf.Max(1, source.width / Downsample), h = Mathf.Max(1, source.height / Downsample);
                a = NewTarget("FloorF Glow A", w, h);
                b = NewTarget("FloorF Glow B", w, h);
                ScreenMaterial.SetTexture(GlowTexId, b);
                cmd = new CommandBuffer { name = "FloorF Pixel Glow" };
                RenderPipelineManager.endCameraRendering += OnEndCamera;
            }
            else Debug.LogWarning("[Rendering] glow shaders missing under Resources/" + GlowShaderPath + " - glow off");
            Enabled = DefaultOn;
        }

        static RenderTexture NewTarget(string name, int w, int h)
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
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
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
            cmd.Blit(source, a, glow, 0);
            cmd.Blit(a, b, glow, 1);
        }

        /// <summary>Bright pass + blur right now (e.g. after an off-frame render of the world camera).</summary>
        public void Run()
        {
            if (!Available || source == null) return;
            Record();
            Graphics.ExecuteCommandBuffer(cmd);
            Passes++;
        }

        public void Dispose()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (screen != null) screen.SetMaterial(null);
            cmd?.Release();
            Release(ref a);
            Release(ref b);
            if (glow != null) Object.Destroy(glow);
            if (ScreenMaterial != null) Object.Destroy(ScreenMaterial);
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
