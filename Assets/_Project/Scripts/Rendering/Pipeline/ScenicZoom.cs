using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Pure math of the scenic zoom (gameplay frame &lt;-&gt; wide frame) - EditMode-tested.
    /// The zoom never renders at a fractional pixels-per-unit: it walks the INTEGER ladder between the two frames
    /// (gameplay 11 -&gt; 10 -&gt; 9 -&gt; 8 wide), every rung a real crisp frame (signs, facade details and the city mesh are
    /// rebuilt per whole ppu, the camera centre sits on that rung's pixel grid). The rung is picked from an eased,
    /// continuous orthographic size, so the holds are longest at both ends (ease-in-out) and the camera centre moves
    /// linearly with the size - every rung keeps the same fixed point on screen, like one continuous zoom.
    /// PixelGlow cross-dissolves rung to rung for a few frames (ordered pixel dissolve), so the steps read as a
    /// deliberate pixel zoom rather than cuts.
    /// </summary>
    static class ScenicZoom
    {
        /// <summary>Ease-in-out (smoothstep) on 0..1.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static int Ppu(CameraFrame f) => Mathf.Max(1, Mathf.RoundToInt(f.PixelsPerUnit));

        /// <summary>Orthographic size of a whole-ppu rung.</summary>
        public static float SizeOf(int ppu, int targetHeight) => targetHeight / (2f * Mathf.Max(1, ppu));

        /// <summary>
        /// Rung (whole pixels per unit) for eased progress <paramref name="eased"/> (0 = <paramref name="near"/>,
        /// 1 = <paramref name="far"/>): the one whose size is nearest to the interpolated size.
        /// </summary>
        public static int Level(CameraFrame near, CameraFrame far, float eased, int targetHeight)
        {
            int a = Ppu(near), b = Ppu(far);
            if (eased <= 0f) return a;
            if (eased >= 1f) return b;
            float size = Mathf.Lerp(near.OrthographicSize, far.OrthographicSize, eased);
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b), best = a;
            float bestErr = float.MaxValue;
            for (int k = lo; k <= hi; k++)
            {
                float err = Mathf.Abs(SizeOf(k, targetHeight) - size);
                if (err < bestErr) { bestErr = err; best = k; }
            }
            return best;
        }

        /// <summary>Every rung from <paramref name="near"/> to <paramref name="far"/> inclusive, in zoom order.</summary>
        public static int[] Levels(CameraFrame near, CameraFrame far)
        {
            int a = Ppu(near), b = Ppu(far), n = Mathf.Abs(a - b) + 1, step = b >= a ? 1 : -1;
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = a + i * step;
            return r;
        }

        /// <summary>
        /// The frame of rung <paramref name="ppu"/>: exact <paramref name="near"/> / <paramref name="far"/> at the ends,
        /// otherwise that rung's size with the centre interpolated by size (fixed point preserved) and snapped to the
        /// rung's pixel grid.
        /// </summary>
        public static CameraFrame LevelFrame(CameraFrame near, CameraFrame far, int ppu, int targetHeight)
        {
            if (ppu == Ppu(near)) return near;
            if (ppu == Ppu(far)) return far;
            float size = SizeOf(ppu, targetHeight);
            float span = far.OrthographicSize - near.OrthographicSize;
            float s = Mathf.Abs(span) < 1e-5f ? 0f : Mathf.Clamp01((size - near.OrthographicSize) / span);
            var p = Vector3.Lerp(near.Position, far.Position, s);
            p = new Vector3(Mathf.Round(p.x * ppu) / ppu, Mathf.Round(p.y * ppu) / ppu, near.Position.z);
            return new CameraFrame(p, near.Rotation, size, targetHeight, near.Shear, near.ShearDepth);
        }

        /// <summary>Pure: slow scenic pan in whole pixels, scaled by the zoom (0 in the gameplay frame).</summary>
        public static int PanOffset(float time, float eased, float pixels, float period)
            => Mathf.RoundToInt(Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.01f, period)) * pixels * Mathf.Clamp01(eased));
    }
}
