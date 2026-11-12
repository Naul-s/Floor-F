using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pure, seeded chimney smoke schedule (EditMode-tested): a steady stream of soft puffs, each one rising from the
    /// mouth, leaning on the wind (from the right, like the snow) and growing while it fades. Everything is a function of
    /// (seed, index, time): a frozen clock freezes the smoke. Smooth fades only - nothing switches on/off.
    /// </summary>
    static class SmokeSchedule
    {
        public const int Puffs = 14;
        /// <summary>Seconds a puff lives (the stream is continuous: one puff born every Life / Puffs s).</summary>
        public const float Life = 6f;
        /// <summary>Rise, gameplay-frame pixels per second.</summary>
        public const float Rise = 4.2f;
        public const float FadeIn = 0.5f;

        public struct Puff
        {
            /// <summary>Offset from the chimney mouth in gameplay-frame pixels (x right, y up).</summary>
            public Vector2 Offset;
            /// <summary>1, 2 or 3 px across.</summary>
            public int Size;
            /// <summary>0..1 brightness.</summary>
            public float Level;
        }

        public static Puff At(int seed, int i, float time)
        {
            float t = Mathf.Max(0f, time);
            float age = Mathf.Repeat(t / Life + i / (float)Puffs + LightSchedule.Hash(seed, i, 301, 0) * 0.04f, 1f) * Life;
            // which generation of this puff (its wobble changes per life)
            int gen = Mathf.FloorToInt(t / Life + i / (float)Puffs);
            float h = LightSchedule.Hash(seed, i, 302, gen);
            float y = age * Rise * Mathf.Lerp(0.85f, 1.15f, h);
            float sway = Mathf.Sin(age * 1.3f + h * 6.28f) * Mathf.Min(1f, age * 0.6f) * 1.4f;
            float x = -0.42f * age * age - age * 0.6f + sway;
            float fadeOut = 1f - age / Life;
            return new Puff
            {
                Offset = new Vector2(x, y + 1f),
                Size = age < 0.8f ? 1 : age < 2.6f ? 2 : 3,
                Level = Mathf.Clamp01(age / FadeIn) * Mathf.Pow(fadeOut, 1.3f),
            };
        }
    }

    /// <summary>
    /// A small detail that makes the hotel feel lived in: grey smoke curling up from the roof chimney (whiter and thicker
    /// in the winter season). One additive palette mesh in viewport pixels, depth-tested at the chimney's depth, so the
    /// HOTEL sign and the roof props in front of it stay in front; scaled with the frame's pixels per unit (smaller in
    /// the wide scenic frame). Rebuilt only when the ambient clock or the camera moved.
    /// </summary>
    sealed class ChimneySmoke
    {
        static readonly Color SmokeColor = new Color(0.26f, 0.26f, 0.29f);
        static readonly Color WinterSmoke = new Color(0.34f, 0.35f, 0.39f);
        const int RampSteps = 6;
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        readonly PaletteAtlas atlas;
        readonly Vector3 mouth;
        readonly int seed;
        readonly float basePpu;
        readonly PixelQuads quads = new PixelQuads();
        readonly Mesh mesh;
        readonly Transform layer;
        int[] ramp, winterRamp;
        float builtTime = float.NaN;
        Vector3 builtCamera;
        bool builtWinter;

        public MeshRenderer Renderer { get; }
        public int QuadCount => quads.Count;
        public int Rebuilds { get; private set; }
        /// <summary>Viewport pixel of the chimney mouth in the last Tick.</summary>
        public Vector2 MouthPixels { get; private set; }

        public ChimneySmoke(Transform parent, RenderAssets assets, PaletteAtlas atlas, Vector3 mouth, float basePixelsPerUnit, int seed)
        {
            this.atlas = atlas;
            this.mouth = mouth;
            this.seed = seed;
            basePpu = Mathf.Max(1f, basePixelsPerUnit);
            var additive = assets.NewAdditive(atlas.Texture, "Chimney Smoke (additive, depth-tested)");
            if (additive != null) additive.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            var from = additive != null ? Color.black : RenderingStyle.Background;
            ramp = Ramp(from, SmokeColor);
            winterRamp = Ramp(from, WinterSmoke);
            mesh = assets.Own(new Mesh { name = "Chimney Smoke" });
            mesh.MarkDynamic();
            Renderer = PaletteRenderer.Create(parent, "Chimney Smoke", mesh, additive != null ? additive : atlas.Material);
            Renderer.allowOcclusionWhenDynamic = false;
            Renderer.enabled = false;
            layer = Renderer.transform;
        }

        int[] Ramp(Color from, Color to)
        {
            var r = new int[RampSteps];
            for (int i = 0; i < RampSteps; i++) r[i] = atlas.Static(Color.Lerp(from, to, i / (RampSteps - 1f)));
            return r;
        }

        /// <param name="time">Ambient clock (+ prewarm): the smoke freezes with the pause.</param>
        public void Tick(float time, CameraFrame frame, bool winter)
        {
            layer.position = SkyView.LayerOrigin(frame, mouth.z);
            float s = 1f / frame.PixelsPerUnit;
            layer.localScale = new Vector3(s, s, 1f);
            if (time == builtTime && frame.Position == builtCamera && winter == builtWinter && Renderer.enabled) return;
            builtTime = time;
            builtCamera = frame.Position;
            builtWinter = winter;
            Rebuilds++;

            var m = SkyView.WorldToPixels(frame, mouth);
            MouthPixels = m;
            int mx = Mathf.FloorToInt(m.x), my = Mathf.FloorToInt(m.y);
            float k = frame.PixelsPerUnit / basePpu; // the plume keeps its world size across zoom rungs
            var r = winter ? winterRamp : ramp;
            quads.Clear();
            for (int i = 0; i < SmokeSchedule.Puffs; i++)
            {
                var p = SmokeSchedule.At(seed, i, time);
                int step = Mathf.Clamp(Mathf.RoundToInt(p.Level * (RampSteps - 1)), 0, RampSteps - 1);
                if (step <= 0) continue;
                int size = Mathf.Max(1, Mathf.RoundToInt(p.Size * Mathf.Min(1f, k)));
                int x = mx + Mathf.RoundToInt(p.Offset.x * k) - size / 2, y = my + Mathf.RoundToInt(p.Offset.y * k);
                var uv = atlas.UV(r[step]);
                if (size >= 3)
                {
                    // a round-ish puff: plus shape (no square corners), a dimmer core-less ring would read as noise
                    quads.Rect(x + 1, y, size - 2, size, uv);
                    quads.Rect(x, y + 1, 1, size - 2, uv);
                    quads.Rect(x + size - 1, y + 1, 1, size - 2, uv);
                }
                else quads.Rect(x, y, size, size, uv);
            }
            quads.Apply(mesh);
            Renderer.enabled = quads.Count > 0;
        }
    }
}
