using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>Object names other code (tests, ui-dev via a proposed contract constant) can rely on.</summary>
    static class RenderingNames
    {
        public const string Root = "[Rendering]";
        /// <summary>Alias of PresentationContract.UiCameraName (HUD canvases, Screen Space - Camera, go on it).</summary>
        public const string UiCamera = PresentationContract.UiCameraName;
        public const string ScreenCanvas = "[Rendering] Pixel Screen Canvas";
        public const string World = "World";
        public const string FloorSignPrefix = "Sign ";
        public const string Car = "Elevator Car";
    }

    /// <summary>
    /// Visual style knobs (not game rules - those live in JSON). Kept in one place so the look can be tuned quickly.
    /// </summary>
    static class RenderingStyle
    {
        // ---- pipeline
        public const int PixelWidth = PresentationContract.PixelWidth;
        public const int PixelHeight = PresentationContract.PixelHeight;
        /// <summary>Screen-pixels per sign texel in the low-res target (2 = chunky but readable 5x7 glyphs).</summary>
        public const int SignPixelsPerTexel = 2;
        /// <summary>Canvas sorting order of the pixel screen. HUD canvases must sort above (>= 0).</summary>
        public const int ScreenCanvasSortingOrder = PresentationContract.PixelScreenSortingOrder;
        public const float ScreenCanvasPlaneDistance = 10f;

        // ---- camera: oblique orthographic (see CameraFrame) - depth shifts the image right/up so top and right-hand
        // side faces read as 3D (~ the old 10 deg pitch / -8 deg yaw), while front faces stay exact pixel rectangles
        public static readonly Vector2 CameraShear = new Vector2(0.14f, 0.175f);
        public const float CameraMargin = 0.05f;
        public const float CameraDistance = 20f;
        /// <summary>Sky above the HOTEL sign in the gameplay frame (world units); the rest of the slack shows the street.</summary>
        public const float CameraTopHeadroom = 0.3f;

        // ---- street (world units; hotel front at z = -depth/2, floor 1 at y = 0, camera side = -Z)
        /// <summary>Sidewalk top; the hotel stands on a plinth from here up to floor 1.</summary>
        public const float GroundY = -1.6f;
        public const float RoadY = -1.75f;
        /// <summary>Sidewalk from the hotel front (z = -depth/2 - 0.15) to the curb.</summary>
        public const float CurbZ = -3.7f;
        /// <summary>Pedestrian walking lines (z) on the sidewalk.</summary>
        public static readonly float[] WalkLanes = { -2.65f, -3.05f };
        /// <summary>Car lanes (z): far lane drives to +X, near lane to -X.</summary>
        public const float FarLaneZ = -4.3f;
        public const float NearLaneZ = -5.55f;
        public const int MaxCars = 8;
        public const int MaxPedestrians = 12;
        public const int CitySeed = 1408;
        /// <summary>Half the street length that is simulated (covers the zoomed-out title frame + its pan).</summary>
        public const float StreetHalfLength = 44f;

        public static readonly Color Background = new Color(0.055f, 0.06f, 0.11f);
        public static readonly Color Ambient = new Color(0.26f, 0.25f, 0.33f);
        public static readonly Color MoonColor = new Color(0.62f, 0.68f, 0.90f);
        public const float MoonIntensity = 0.45f;

        // ---- building materials
        public static readonly Color Slab = new Color(0.36f, 0.33f, 0.31f);
        public static readonly Color SlabEdge = new Color(0.55f, 0.42f, 0.30f);
        public static readonly Color OuterWall = new Color(0.42f, 0.30f, 0.26f);
        public static readonly Color ShaftBack = new Color(0.10f, 0.10f, 0.12f);
        public static readonly Color ShaftFrame = new Color(0.62f, 0.52f, 0.30f);
        public static readonly Color Car = new Color(0.92f, 0.74f, 0.36f);
        public static readonly Color CarInside = new Color(0.62f, 0.46f, 0.28f);
        public static readonly Color CarDoor = new Color(0.70f, 0.70f, 0.74f);
        public static readonly Color Ground = new Color(0.16f, 0.16f, 0.18f);
        public static readonly Color Sidewalk = new Color(0.30f, 0.29f, 0.30f);
        public static readonly Color Skyline = new Color(0.09f, 0.10f, 0.17f);
        public static readonly Color WindowGlow = new Color(0.95f, 0.78f, 0.42f);

        // ---- signs
        public static readonly Color SignPlate = new Color(0.08f, 0.07f, 0.06f);
        public static readonly Color SignInk = new Color(1.00f, 0.86f, 0.55f);
        public static readonly Color StickerPlate = new Color(0.96f, 0.84f, 0.20f);
        public static readonly Color StickerInk = new Color(0.12f, 0.08f, 0.04f);
        public static readonly Color HotelSign = new Color(1.00f, 0.32f, 0.30f);

        // ---- lighting moods
        public static readonly Color UnluckyFlash = new Color(1.00f, 0.08f, 0.06f);
        public static readonly Color LuckyFlash = new Color(1.00f, 0.85f, 0.30f);
        public const float UnluckyFlashSeconds = 1.1f;
        public const float LuckyFlashSeconds = 0.6f;
        /// <summary>"666" (unlucky 6 from a devout guest): one red pulse on every floor.</summary>
        public const float CurseFlashSeconds = 1.3f;
        public const float CurseFlashStrength = 0.75f;
        public const float FloorLightIntensity = 6f;
        public const float FloorLightRange = 6.5f;
        public static readonly Color CarLight = new Color(1.00f, 0.90f, 0.70f);
        public const float CarLightIntensity = 4f;
        public static readonly Color IndicatorIdle = new Color(0.25f, 0.18f, 0.08f);
        public static readonly Color IndicatorHere = new Color(1.00f, 0.62f, 0.15f);
        public static readonly Color IndicatorOpen = new Color(0.35f, 1.00f, 0.45f);

        /// <summary>Label that gets the flickering fluorescent tube (GDD 8: "F층의 깜빡이는 형광등").</summary>
        public const string FlickerLabel = "F";

        /// <summary>Per-destination light + wallpaper colour so floors read apart at a glance.</summary>
        public static Color FloorLightColor(DestinationId destination, int floorIndex)
        {
            switch (destination.Value)
            {
                case "lobby": return new Color(1.00f, 0.86f, 0.62f);
                case "restaurant": return new Color(1.00f, 0.66f, 0.36f);
                case "gym": return new Color(0.78f, 0.92f, 1.00f);
                case "karaoke": return new Color(1.00f, 0.42f, 0.86f);
                case "sauna": return new Color(1.00f, 0.55f, 0.30f);
                case "rooms": return new Color(1.00f, 0.92f, 0.72f);
                case "bar": return new Color(0.62f, 0.45f, 1.00f);
                case "casino": return new Color(1.00f, 0.30f, 0.30f);
                case "rooftop": return new Color(0.55f, 0.80f, 1.00f);
                default: return Color.HSVToRGB((floorIndex * 0.13f) % 1f, 0.35f, 1f);
            }
        }

        public static Color Wallpaper(Color lightColor) => Color.Lerp(new Color(0.55f, 0.50f, 0.46f), lightColor, 0.35f);
    }
}
