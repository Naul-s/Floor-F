using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FloorF.UI
{
    /// <summary>
    /// The one UI font: Galmuri11 (SIL OFL, orchestrator import, Resources "Fonts/Galmuri11") turned into a DYNAMIC
    /// TMP_FontAsset at runtime - no font asset file, so play mode never touches Assets/TextMesh Pro (LiberationSans and
    /// its dynamic fallback are never rendered). Covers English + Hangul + the symbols the UI uses (▲▼→•…).
    /// Galmuri11 is drawn on a 12px grid (1200 upem, 100 units per pixel): every UI size is snapped to 12/24/36/48...
    /// WebGL-safe: no threads, no file IO (FontEngine reads the imported font data). If creation fails the UI falls back
    /// to TMP_Settings.defaultFontAsset with one warning.
    /// </summary>
    static class UIFonts
    {
        public const string ResourcePath = "Fonts/Galmuri11";
        public const string FamilyName = "Galmuri11";
        /// <summary>Galmuri11's pixel grid in TMP points (font size 12 = one font pixel per canvas unit).</summary>
        public const int Grid = 12;

        public enum Mode
        {
            /// <summary>Signed distance field sampled at 4x the grid (48pt) - crisp at any canvas scale, uses the
            /// Mobile SDF shader that TMP's own Resources materials already pull into the WebGL build.</summary>
            Sdf,
            /// <summary>Hinted bitmap at the native 12pt + Point filtering - exact pixels at integer scales only, and
            /// needs "TextMeshPro/Mobile/Bitmap" which nothing in the build references.</summary>
            Raster,
        }

        /// <summary>Chosen from L2 capture ui_font_compare.png (see Docs/Notes/ui-dev).</summary>
        public const Mode DefaultMode = Mode.Sdf;

        static TMP_FontAsset main;
        static bool tried, warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            main = null;
            tried = warned = false;
            IsFallback = false;
        }

        /// <summary>True when Galmuri could not be created and the UI uses TMP's default font.</summary>
        public static bool IsFallback { get; private set; }

        public static TMP_FontAsset Main
        {
            get
            {
                if (main != null) return main;
                if (!tried)
                {
                    tried = true;
                    main = Create(DefaultMode);
                }
                if (main == null)
                {
                    IsFallback = true;
                    main = TMP_Settings.defaultFontAsset;
                    if (!warned)
                    {
                        warned = true;
                        Debug.LogWarning("[UI] Galmuri11 font asset could not be created - falling back to " + (main != null ? main.name : "<no TMP default font>"));
                    }
                }
                return main;
            }
        }

        /// <summary>A fresh dynamic Galmuri11 asset in <paramref name="mode"/> (null on failure, never throws).</summary>
        public static TMP_FontAsset Create(Mode mode)
        {
            try
            {
                var font = Resources.Load<Font>(ResourcePath);
                if (font == null) return null;
                TMP_FontAsset fa;
                if (mode == Mode.Raster)
                {
                    if (Shader.Find("TextMeshPro/Mobile/Bitmap") == null) return null;
                    fa = TMP_FontAsset.CreateFontAsset(font, Grid, 2, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                    if (fa != null && fa.atlasTexture != null) fa.atlasTexture.filterMode = FilterMode.Point;
                }
                else
                {
                    fa = TMP_FontAsset.CreateFontAsset(font, Grid * 4, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                }
                if (fa == null) return null;
                fa.name = FamilyName + " (runtime " + mode + ")";
                fa.fallbackFontAssetTable = new List<TMP_FontAsset>(); // never fall through to another asset
                return fa;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UI] Galmuri11 " + mode + " font asset failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Nearest multiple of the 12px grid (ties up), at least 12.</summary>
        public static float Px(float size) => Mathf.Max(1, Mathf.FloorToInt(size / Grid + 0.5f)) * Grid;

        public static bool IsGalmuri(TMP_FontAsset fa) => fa != null && fa.faceInfo.familyName == FamilyName;
    }
}
