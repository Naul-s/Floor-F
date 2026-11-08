using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// One world-camera setup. The camera looks straight down +Z (identity rotation) through an OBLIQUE orthographic
    /// projection: a point at depth z is shifted by <see cref="Shear"/> * (z - <see cref="ShearDepth"/>) on screen
    /// (further = up and to the right), which shows top and right-hand side faces like a 3/4 view while every face
    /// parallel to the XY plane stays an exact axis-aligned rectangle (a rotated camera skews them by a fraction of a
    /// pixel per pixel, which rasterises as stray 1-pixel steps on long edges at 640x360).
    /// </summary>
    readonly struct CameraFrame
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float OrthographicSize;
        /// <summary>Low-res target pixels per world unit (integer after <see cref="CameraFraming.SnapToWholePixels"/>).</summary>
        public readonly float PixelsPerUnit;
        /// <summary>Screen offset (world units) per unit of depth behind <see cref="ShearDepth"/>: x = right, y = up.</summary>
        public readonly Vector2 Shear;
        /// <summary>Depth (world z) whose plane maps 1:1 onto the screen (the hotel's front).</summary>
        public readonly float ShearDepth;

        public CameraFrame(Vector3 position, Quaternion rotation, float orthographicSize, int targetHeight)
            : this(position, rotation, orthographicSize, targetHeight, Vector2.zero, 0f) { }

        public CameraFrame(Vector3 position, Quaternion rotation, float orthographicSize, int targetHeight, Vector2 shear, float shearDepth)
        {
            Position = position;
            Rotation = rotation;
            OrthographicSize = orthographicSize;
            PixelsPerUnit = targetHeight / (2f * orthographicSize);
            Shear = shear;
            ShearDepth = shearDepth;
        }

        public CameraFrame WithPosition(Vector3 p, int targetHeight) => new CameraFrame(p, Rotation, OrthographicSize, targetHeight, Shear, ShearDepth);
        public CameraFrame WithSize(float size, int targetHeight) => new CameraFrame(Position, Rotation, size, targetHeight, Shear, ShearDepth);

        /// <summary>Screen-plane (sheared) coordinates of a world point, in world units (camera rotation is identity).</summary>
        public Vector2 ToScreenPlane(Vector3 world) => PixelGrid.Project(world, Shear, ShearDepth);

        public PixelGrid Grid => new PixelGrid(PixelsPerUnit, Shear, ShearDepth);
    }

    /// <summary>
    /// The low-res pixel lattice in screen-plane coordinates: with a whole number of pixels per unit and the camera centre
    /// on a multiple of one pixel, pixel edges sit at k / PixelsPerUnit. Geometry snapped with <see cref="SnapCorner"/>
    /// rasterises to exact pixel rectangles.
    /// </summary>
    readonly struct PixelGrid
    {
        public readonly float PixelsPerUnit;
        public readonly Vector2 Shear;
        public readonly float ShearDepth;

        public PixelGrid(float pixelsPerUnit, Vector2 shear, float shearDepth)
        {
            PixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            Shear = shear;
            ShearDepth = shearDepth;
        }

        public float Pixel => 1f / PixelsPerUnit;

        public static Vector2 Project(Vector3 w, Vector2 shear, float shearDepth)
            => new Vector2(w.x + shear.x * (w.z - shearDepth), w.y + shear.y * (w.z - shearDepth));

        public Vector2 Project(Vector3 w) => Project(w, Shear, ShearDepth);

        /// <summary>Rounds a length to whole pixels (at least one).</summary>
        public float SnapLength(float worldUnits) => Mathf.Max(1f, Mathf.Round(worldUnits * PixelsPerUnit)) / PixelsPerUnit;

        /// <summary>Rounds a screen-plane coordinate to the nearest pixel edge.</summary>
        public float SnapScreen(float v) => Mathf.Round(v * PixelsPerUnit) / PixelsPerUnit;

        /// <summary>Moves <paramref name="world"/> (same depth) so it projects onto the nearest pixel corner.</summary>
        public Vector3 SnapCorner(Vector3 world)
        {
            var s = Project(world);
            return new Vector3(world.x + (SnapScreen(s.x) - s.x), world.y + (SnapScreen(s.y) - s.y), world.z);
        }

        /// <summary>Pure: world x/y that projects onto screen-plane x/y at depth <paramref name="z"/>.</summary>
        public static float WorldX(float screenX, float z, Vector2 shear, float shearDepth) => screenX - shear.x * (z - shearDepth);
        public static float WorldY(float screenY, float z, Vector2 shear, float shearDepth) => screenY - shear.y * (z - shearDepth);
    }

    /// <summary>Pure math: fit the oblique orthographic camera around the hotel (EditMode-tested).</summary>
    static class CameraFraming
    {
        /// <summary>
        /// Oblique fit: <paramref name="screenRect"/> is the screen-plane rectangle (see <see cref="PixelGrid.Project"/>)
        /// that must stay in view; the camera sits <paramref name="distance"/> in front of <paramref name="nearZ"/>.
        /// </summary>
        public static CameraFrame FitOblique(Rect screenRect, Vector2 shear, float shearDepth, float nearZ, float aspect, float margin, float distance, int targetHeight)
        {
            float halfH = screenRect.height * 0.5f, halfW = screenRect.width * 0.5f;
            float size = Mathf.Max(halfH, halfW / Mathf.Max(0.01f, aspect)) * (1f + Mathf.Max(0f, margin));
            var pos = new Vector3(screenRect.center.x, screenRect.center.y, nearZ - distance);
            return new CameraFrame(pos, Quaternion.identity, size, targetHeight, shear, shearDepth);
        }

        /// <summary>Screen-plane rectangle covering the 8 corners of <paramref name="b"/>.</summary>
        public static Rect ScreenRect(Bounds b, Vector2 shear, float shearDepth)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var c = b.center; var e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var s = PixelGrid.Project(corner, shear, shearDepth);
                min = Vector2.Min(min, s);
                max = Vector2.Max(max, s);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// Grows the orthographic size so one world unit is a whole number of target pixels (stable texel grid for
        /// signs; never shrinks, so the fit still holds) and puts the camera centre on a whole pixel.
        /// </summary>
        public static CameraFrame SnapToWholePixels(CameraFrame f, int targetHeight)
        {
            float ppu = Mathf.Max(1f, Mathf.Floor(targetHeight / (2f * f.OrthographicSize) + 1e-4f));
            return f.WithSize(targetHeight / (2f * ppu), targetHeight).WithPosition(SnapCentre(f.Position, ppu), targetHeight);
        }

        static Vector3 SnapCentre(Vector3 p, float ppu) => new Vector3(Mathf.Round(p.x * ppu) / ppu, Mathf.Round(p.y * ppu) / ppu, p.z);

        /// <summary>Moves the frame vertically so its top edge is at screen-plane y <paramref name="top"/> (snapped).</summary>
        public static CameraFrame AnchorTop(CameraFrame f, float top, int targetHeight)
        {
            var p = f.Position;
            p.y = top - f.OrthographicSize;
            return f.WithPosition(SnapCentre(p, f.PixelsPerUnit), targetHeight);
        }

        /// <summary>Moves the frame vertically so its bottom edge is at screen-plane y <paramref name="bottom"/> (snapped).</summary>
        public static CameraFrame AnchorBottom(CameraFrame f, float bottom, int targetHeight)
        {
            var p = f.Position;
            p.y = bottom + f.OrthographicSize;
            return f.WithPosition(SnapCentre(p, f.PixelsPerUnit), targetHeight);
        }

        public static float Top(CameraFrame f) => f.Position.y + f.OrthographicSize;
        public static float Bottom(CameraFrame f) => f.Position.y - f.OrthographicSize;

        /// <summary>
        /// GL-style projection for <paramref name="f"/>: orthographic box times the depth shear (view space looks down -Z,
        /// so a point's depth behind the shear plane is Position.z - ShearDepth - z_view).
        /// </summary>
        public static Matrix4x4 ProjectionMatrix(CameraFrame f, float aspect, float near, float far)
        {
            float hh = f.OrthographicSize, hw = hh * aspect;
            var ortho = Matrix4x4.Ortho(-hw, hw, -hh, hh, near, far);
            var s = Matrix4x4.identity;
            float d = f.Position.z - f.ShearDepth;
            s.m02 = -f.Shear.x; s.m03 = f.Shear.x * d;
            s.m12 = -f.Shear.y; s.m13 = f.Shear.y * d;
            return ortho * s;
        }
    }
}
