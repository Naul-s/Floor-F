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

        // ---- scenic view (ScenicViewChanged): integer-ppu zoom ladder to the wide frame, see ScenicZoom
        /// <summary>Gameplay -&gt; wide (player's scenic mode), seconds on the unscaled clock.</summary>
        public const float ScenicOutSeconds = 1.2f;
        /// <summary>Wide -&gt; gameplay (scenic off, the establishing shot's "zoom into the hotel").</summary>
        public const float ScenicInSeconds = 0.9f;
        /// <summary>Scenic pan: gentler and slower than the title's (the player is looking, not being attracted).</summary>
        public const float ScenicPanPixels = 22f;
        public const float ScenicPanPeriod = 36f;
        /// <summary>Star night: the wide frame rises this many pixels (less road, more Milky Way); the curb stays in view.</summary>
        public const int StarNightLiftPixels = 16;
        /// <summary>Ordered pixel dissolve between two zoom rungs (PixelGlow).</summary>
        public const float StepDissolveSeconds = 0.12f;
        /// <summary>Unscaled frame time cap (a hitch never skips the whole zoom).</summary>
        public const float MaxCameraStep = 0.1f;

        readonly PixelPipeline pipeline;
        readonly int targetHeight;
        CameraFrame baseFrame, titleFrame, scenicFrame, scenicStarFrame;
        float shakeElapsed = float.MaxValue, shakeDuration = ShakeSeconds, shakeAmplitude = ShakePixels;
        float titleTime;
        float zoomT, scenicTime;
        bool wideStar, prevTitle;
        int prevLevel;

        public CameraFrame BaseFrame => baseFrame;
        public CameraFrame TitleFrame => titleFrame;
        /// <summary>Wide frame of the scenic view (title framing; star night: <see cref="StarNightLiftPixels"/> higher).</summary>
        public CameraFrame ScenicFrame => scenicFrame;
        public CameraFrame ScenicStarFrame => scenicStarFrame;
        /// <summary>The wide frame the current / next scenic zoom heads for.</summary>
        public CameraFrame WideFrame => wideStar ? scenicStarFrame : scenicFrame;
        public bool TitleMode { get; private set; }
        /// <summary>Last ScenicViewChanged said On (the camera may still be on its way).</summary>
        public bool ScenicOn { get; private set; }
        public bool Establishing { get; private set; }
        /// <summary>Zoom progress, linear in (unscaled) time: 0 = gameplay frame, 1 = wide frame.</summary>
        public float ZoomProgress => zoomT;
        public float ZoomEased => ScenicZoom.Ease(zoomT);
        /// <summary>Anywhere off the gameplay frame because of the scenic view (wide, or zooming either way).</summary>
        public bool InScenicView => zoomT > 0f || ScenicOn;
        public bool IsZooming => ScenicOn ? zoomT < 1f : zoomT > 0f;
        /// <summary>Whole pixels per unit of the frame applied last.</summary>
        public int Level { get; private set; }
        /// <summary>The last Tick moved to another zoom rung (scenic; not the title cut): PixelGlow dissolves it.</summary>
        public bool LevelStepped { get; private set; }
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
            scenicFrame = titleFrame;
            scenicStarFrame = MakeStarNightFrame(titleFrame, targetHeight);
            Current = baseFrame;
            Level = prevLevel = ScenicZoom.Ppu(baseFrame);
            pipeline?.ApplyFrame(baseFrame);
        }

        /// <summary>Star-night wide frame: the title framing lifted <see cref="StarNightLiftPixels"/> whole pixels.</summary>
        public static CameraFrame MakeStarNightFrame(CameraFrame wide, int targetHeight)
        {
            var p = wide.Position;
            p.y += StarNightLiftPixels / wide.PixelsPerUnit;
            return wide.WithPosition(p, targetHeight);
        }

        /// <summary>
        /// ScenicViewChanged. Player's scenic mode (On, !Establishing): zoom out to the wide frame and pan slowly; the
        /// establishing shot (On, Establishing) cuts straight to the wide frame. Off: zoom back into the gameplay frame.
        /// <paramref name="starNight"/> picks the wide frame when a zoom starts from the gameplay frame. Ignored on the title.
        /// </summary>
        public void SetScenic(bool on, bool establishing, bool starNight)
        {
            if (TitleMode) return;
            if (on)
            {
                if (zoomT <= 0f) { scenicTime = 0f; wideStar = starNight; }
                ScenicOn = true;
                Establishing = establishing;
                if (establishing) zoomT = 1f;
            }
            else
            {
                ScenicOn = false;
                Establishing = false;
            }
        }

        /// <summary>Drops the scenic view at once (title, scene reset): back on the gameplay frame next Tick.</summary>
        public void ClearScenic()
        {
            ScenicOn = false;
            Establishing = false;
            zoomT = 0f;
            scenicTime = 0f;
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
            if (on && !TitleMode) { titleTime = 0f; ClearScenic(); }
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

        /// <summary>Juice clock only (shake / title pan); the scenic zoom does not move.</summary>
        public void Tick(float dt) => Tick(dt, 0f);

        /// <summary>
        /// Advance: shake and title pan on the juice clock <paramref name="dt"/> (0 while paused holds them), the scenic
        /// zoom and its pan on the UNSCALED clock <paramref name="cameraDt"/> (the scenic view pauses the game).
        /// </summary>
        public void Tick(float dt, float cameraDt)
        {
            cameraDt = Mathf.Clamp(cameraDt, 0f, MaxCameraStep);
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
            else
            {
                if (ScenicOn && zoomT < 1f) zoomT = Mathf.Min(1f, zoomT + cameraDt / ScenicOutSeconds);
                else if (!ScenicOn && zoomT > 0f) zoomT = Mathf.Max(0f, zoomT - cameraDt / ScenicInSeconds);
                if (zoomT > 0f)
                {
                    scenicTime += cameraDt;
                    float e = ScenicZoom.Ease(zoomT);
                    var wide = WideFrame;
                    frame = ScenicZoom.LevelFrame(baseFrame, wide, ScenicZoom.Level(baseFrame, wide, e, targetHeight), targetHeight);
                    pan = ScenicZoom.PanOffset(scenicTime, e, ScenicPanPixels, ScenicPanPeriod);
                }
                else scenicTime = 0f;
            }
            Level = ScenicZoom.Ppu(frame);
            LevelStepped = !TitleMode && !prevTitle && Level != prevLevel;
            prevLevel = Level;
            prevTitle = TitleMode;
            OffsetPixels = new Vector2Int(shake.x + pan, shake.y);
            float unit = 1f / frame.PixelsPerUnit;
            var right = frame.Rotation * Vector3.right;
            var up = frame.Rotation * Vector3.up;
            var pos = frame.Position + right * (OffsetPixels.x * unit) + up * (OffsetPixels.y * unit);
            Current = frame.WithPosition(pos, targetHeight);
            pipeline?.ApplyFrame(Current);
        }
    }
}
