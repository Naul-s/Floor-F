namespace FloorF.Core
{
    /// <summary>
    /// Shared constants between presentation modules (Rendering, UI, Audio may not reference each other).
    /// Owner of each object is named per constant; others only look it up, from Start() or later.
    /// </summary>
    public static class PresentationContract
    {
        /// <summary>
        /// Screen-space camera created by Rendering in its root's Awake. UI canvases: RenderMode.ScreenSpaceCamera,
        /// worldCamera = this camera, planeDistance &lt; 10 (e.g. 1), sortingOrder &gt;= 0, every object on <see cref="UiLayer"/>.
        /// Find with GameObject.Find(UiCameraName).GetComponent&lt;Camera&gt;().
        /// </summary>
        public const string UiCameraName = "[Rendering] UI Camera";

        /// <summary>Low-res world render target size (the world camera renders into it; it is aspect-fit to the screen).</summary>
        public const int PixelWidth = 640;
        public const int PixelHeight = 360;

        /// <summary>Unity's built-in "UI" layer. The UI camera culls everything else; the world camera excludes it.</summary>
        public const int UiLayer = 5;

        /// <summary>Sorting order of Rendering's upscaled pixel-screen canvas. HUD canvases must sort above it (>= 0).</summary>
        public const int PixelScreenSortingOrder = -100;

        /// <summary>
        /// Maps a world-camera viewport point (Camera.main.WorldToViewportPoint) to a screen pixel position inside the
        /// aspect-fit pixel screen, for speech bubbles / number popups. Returns false when the point is behind the camera.
        /// </summary>
        public static bool ViewportToScreen(float vx, float vy, float vz, float screenWidth, float screenHeight, out float sx, out float sy)
        {
            float scale = System.Math.Min(screenWidth / PixelWidth, screenHeight / PixelHeight);
            float w = PixelWidth * scale, h = PixelHeight * scale;
            float ox = (screenWidth - w) * 0.5f, oy = (screenHeight - h) * 0.5f;
            sx = ox + vx * w;
            sy = oy + vy * h;
            return vz > 0f;
        }
    }
}
