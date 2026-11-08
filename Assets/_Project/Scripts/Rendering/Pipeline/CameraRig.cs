using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// World camera motion on top of the fitted frame: unlucky screen shake and the title-screen pan. Every offset is a
    /// whole number of low-res pixels along the camera's right/up axes, so the 640x360 image shifts by whole pixels
    /// (no texel shimmer on signs) and returns exactly to the base frame afterwards.
    /// </summary>
    sealed class CameraRig
    {
        public const float ShakeSeconds = 0.32f;
        public const float ShakePixels = 3f;
        /// <summary>Title: orthographic size grows by this factor (whole building plus sky/street around it).</summary>
        // 1.18 left the HOTEL sign under the UI's top logo at a real 16:9 canvas (batch captures letterbox the world
        // view 0.75x and hid it); 1.34 keeps ~30% sky above the sign
        public const float TitleZoomOut = 1.34f;
        public const float TitlePanPixels = 26f;
        public const float TitlePanPeriod = 18f;
        /// <summary>Title: world units of extra street below the gameplay frame's bottom edge.</summary>
        public const float TitleExtraRoad = 0.6f;

        readonly PixelPipeline pipeline;
        readonly int targetHeight;
        CameraFrame baseFrame, titleFrame;
        float shakeElapsed = float.MaxValue, shakeDuration = ShakeSeconds, shakeAmplitude = ShakePixels;
        float titleTime;

        public CameraFrame BaseFrame => baseFrame;
        public CameraFrame TitleFrame => titleFrame;
        public bool TitleMode { get; private set; }
        public bool IsShaking => shakeElapsed < shakeDuration;
        public float ShakeElapsed => shakeElapsed;
        /// <summary>Current offset from the active frame, in low-res pixels (x = camera right, y = camera up).</summary>
        public Vector2Int OffsetPixels { get; private set; }
        /// <summary>The frame applied to the camera in the last Tick.</summary>
        public CameraFrame Current { get; private set; }

        public CameraRig(PixelPipeline pipeline, CameraFrame baseFrame, int targetHeight)
        {
            this.pipeline = pipeline;
            this.targetHeight = targetHeight;
            this.baseFrame = baseFrame;
            titleFrame = MakeTitleFrame(baseFrame, targetHeight);
            Current = baseFrame;
            pipeline.ApplyFrame(baseFrame);
        }

        /// <summary>
        /// Zoomed-out frame, still whole pixels per unit: same street line at the bottom (a little more road), the extra
        /// room goes to sky and neighbouring buildings.
        /// </summary>
        public static CameraFrame MakeTitleFrame(CameraFrame f, int targetHeight)
        {
            var wide = CameraFraming.SnapToWholePixels(f.WithSize(f.OrthographicSize * TitleZoomOut, targetHeight), targetHeight);
            return CameraFraming.AnchorBottom(wide, CameraFraming.Bottom(f) - TitleExtraRoad, targetHeight);
        }

        public void Shake(float seconds = ShakeSeconds, float amplitudePixels = ShakePixels)
        {
            float carry = IsShaking ? shakeAmplitude * (1f - shakeElapsed / shakeDuration) : 0f;
            shakeElapsed = 0f;
            shakeDuration = Mathf.Max(0.05f, seconds);
            shakeAmplitude = Mathf.Max(carry, amplitudePixels);
        }

        public void SetTitle(bool on)
        {
            if (on && !TitleMode) titleTime = 0f;
            TitleMode = on;
        }

        /// <summary>Pure: shake offset in whole pixels at <paramref name="elapsed"/> (zero once finished).</summary>
        public static Vector2Int ShakeOffset(float elapsed, float duration, float amplitude)
        {
            if (elapsed >= duration || elapsed < 0f) return Vector2Int.zero;
            float k = 1f - elapsed / duration;
            float a = amplitude * k;
            int x = Mathf.RoundToInt(Mathf.Sin(elapsed * 61f) * a);
            int y = Mathf.RoundToInt(Mathf.Cos(elapsed * 47f + 1.3f) * a * 0.7f);
            return new Vector2Int(x, y);
        }

        public static int TitlePanOffset(float time) => Mathf.RoundToInt(Mathf.Sin(time * Mathf.PI * 2f / TitlePanPeriod) * TitlePanPixels);

        /// <summary>Advance on the juice clock (dt = 0 while paused holds the current offset).</summary>
        public void Tick(float dt)
        {
            if (shakeElapsed < shakeDuration)
            {
                shakeElapsed += dt;
                if (shakeElapsed >= shakeDuration) shakeAmplitude = 0f;
            }
            var shake = ShakeOffset(shakeElapsed, shakeDuration, shakeAmplitude);
            var frame = baseFrame;
            int pan = 0;
            if (TitleMode)
            {
                titleTime += dt;
                frame = titleFrame;
                pan = TitlePanOffset(titleTime);
            }
            OffsetPixels = new Vector2Int(shake.x + pan, shake.y);
            float unit = 1f / frame.PixelsPerUnit;
            var right = frame.Rotation * Vector3.right;
            var up = frame.Rotation * Vector3.up;
            var pos = frame.Position + right * (OffsetPixels.x * unit) + up * (OffsetPixels.y * unit);
            Current = frame.WithPosition(pos, targetHeight);
            pipeline.ApplyFrame(Current);
        }
    }
}
