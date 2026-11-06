using UnityEngine;

namespace FloorF.Rendering
{
    readonly struct CameraFrame
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float OrthographicSize;
        /// <summary>Low-res target pixels per world unit (integer after <see cref="CameraFraming.SnapToWholePixels"/>).</summary>
        public readonly float PixelsPerUnit;

        public CameraFrame(Vector3 position, Quaternion rotation, float orthographicSize, int targetHeight)
        {
            Position = position;
            Rotation = rotation;
            OrthographicSize = orthographicSize;
            PixelsPerUnit = targetHeight / (2f * orthographicSize);
        }
    }

    /// <summary>Pure math: fit an orthographic camera with a fixed rotation around world bounds (EditMode-tested).</summary>
    static class CameraFraming
    {
        public static CameraFrame Fit(Bounds bounds, Quaternion rotation, float aspect, float margin, float distance, int targetHeight)
        {
            var inv = Quaternion.Inverse(rotation);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var c = bounds.center;
            var e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = inv * corner;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            float halfH = (max.y - min.y) * 0.5f;
            float halfW = (max.x - min.x) * 0.5f;
            float size = Mathf.Max(halfH, halfW / Mathf.Max(0.01f, aspect)) * (1f + Mathf.Max(0f, margin));
            var localPos = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, min.z - distance);
            return new CameraFrame(rotation * localPos, rotation, size, targetHeight);
        }

        /// <summary>
        /// Grows the orthographic size so one world unit is a whole number of target pixels (stable texel grid for
        /// signs; never shrinks, so the fit still holds).
        /// </summary>
        public static CameraFrame SnapToWholePixels(CameraFrame f, int targetHeight)
        {
            float ppu = Mathf.Max(1f, Mathf.Floor(targetHeight / (2f * f.OrthographicSize)));
            return new CameraFrame(f.Position, f.Rotation, targetHeight / (2f * ppu), targetHeight);
        }
    }
}
