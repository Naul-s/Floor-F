using System.Collections.Generic;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Cheap pooled "particles": tiny self-lit cubes that pop out, twinkle and fall (1-2 low-res pixels each, so the
    /// pixelation turns them into dots). No ParticleSystem: deterministic, pause-friendly (Tick(0) freezes them), and
    /// one shared unlit material per colour.
    /// </summary>
    sealed class JuiceFx
    {
        const int PoolSize = 72;
        const float Gravity = -7f;

        sealed class Particle
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity;
            public float Age, Life, Size;
            public bool Active;
        }

        readonly RenderAssets assets;
        readonly List<Particle> pool = new List<Particle>(PoolSize);
        readonly System.Random random = new System.Random(4);
        int next;

        public int ActiveCount { get; private set; }

        public JuiceFx(Transform parent, RenderAssets assets)
        {
            this.assets = assets;
            var root = new GameObject("Juice Fx").transform;
            root.SetParent(parent, false);
            var mat = assets.Unlit(RenderingStyle.LuckyFlash);
            for (int i = 0; i < PoolSize; i++)
            {
                var go = assets.Box(root, "Spark", Vector3.zero, Vector3.one * 0.15f, mat, false);
                go.SetActive(false);
                pool.Add(new Particle { T = go.transform, R = go.GetComponent<MeshRenderer>() });
            }
        }

        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);

        /// <summary>Burst of <paramref name="count"/> sparks at a world position.</summary>
        public void Burst(Vector3 position, Color color, int count, float speed = 3.2f, float size = 0.17f)
        {
            var mat = assets.Unlit(color);
            for (int i = 0; i < count; i++)
            {
                var p = pool[next];
                next = (next + 1) % pool.Count;
                if (!p.Active) ActiveCount++;
                p.Active = true;
                p.Age = 0f;
                p.Life = Range(0.45f, 0.85f);
                p.Size = size * Range(0.7f, 1.25f);
                float a = Range(0f, Mathf.PI * 2f);
                float s = speed * Range(0.45f, 1f);
                p.Velocity = new Vector3(Mathf.Cos(a) * s, Mathf.Abs(Mathf.Sin(a)) * s * 1.1f + 1.2f, -0.6f);
                p.T.position = position + new Vector3(Range(-0.15f, 0.15f), Range(-0.1f, 0.1f), -0.3f);
                p.T.localScale = Vector3.one * p.Size;
                p.R.sharedMaterial = mat;
                p.T.gameObject.SetActive(true);
            }
        }

        public void Tick(float dt)
        {
            if (dt <= 0f || ActiveCount == 0) return;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (!p.Active) continue;
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.Active = false;
                    ActiveCount--;
                    p.T.gameObject.SetActive(false);
                    continue;
                }
                p.Velocity += new Vector3(-p.Velocity.x * 2.5f * dt, Gravity * dt, 0f);
                p.T.position += p.Velocity * dt;
                float k = 1f - p.Age / p.Life;
                bool twinkle = Twinkle(p.Age, i);
                p.T.localScale = Vector3.one * (p.Size * (0.4f + 0.6f * k) * (twinkle ? 1f : 0.55f));
            }
        }

        /// <summary>Spark twinkle cycles per second: 2 size changes per cycle, &lt;= 3 per second (photosensitivity).</summary>
        public const float TwinkleRate = 1.4f;

        /// <summary>Big (true) or small twinkle phase of spark <paramref name="index"/> at <paramref name="age"/> seconds.</summary>
        public static bool Twinkle(float age, int index) => Mathf.Repeat(age * TwinkleRate + index * 0.37f, 1f) < 0.5f;

        /// <summary>Position of the first active spark (tests: checks that sparks freeze while paused).</summary>
        public bool TryGetFirstActive(out Vector3 position)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Active) { position = pool[i].T.position; return true; }
            position = default;
            return false;
        }

        public void Clear()
        {
            foreach (var p in pool) { p.Active = false; p.T.gameObject.SetActive(false); }
            ActiveCount = 0;
        }
    }
}
