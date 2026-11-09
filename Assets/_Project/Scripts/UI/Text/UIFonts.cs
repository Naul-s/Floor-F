using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FloorF.UI
{
    /// <summary>
    /// The UI fonts (all SIL OFL, orchestrator imports under ThirdParty/*/Resources/Fonts), turned into DYNAMIC
    /// TMP_FontAssets at runtime - no font asset files, so play mode never touches Assets/TextMesh Pro:
    /// <list type="bullet">
    /// <item><see cref="Main"/> (body): 물마루 Mulmaru - every text by default.</item>
    /// <item><see cref="Heading"/>: Galmuri11 Bold - big titles only (ending/day/shop/collection/credits/pause titles,
    /// day banner). Wide: never for small labels.</item>
    /// <item><see cref="Fallback"/>: Galmuri11 - the only entry in both assets' fallbackFontAssetTable. Mulmaru and
    /// Galmuri11 Bold lack the hanja of guests/stickers (死 四 生 ...) and symbols like ↕ ≫; Galmuri11 draws them on the
    /// same 12px grid. Nothing may come from TMP's LiberationSans (EditMode/PlayMode glyph coverage tests).</item>
    /// </list>
    /// All three are drawn on a 12px grid (Galmuri 1200 upem / Mulmaru 192 upem; same 16/12 line height): every UI size is
    /// snapped to 12/24/36/48... WebGL-safe: no threads, no file IO (FontEngine reads the imported font data). If a face
    /// cannot be created the UI falls back (Body -> Galmuri11 -> TMP_Settings.defaultFontAsset) with one warning.
    /// </summary>
    static class UIFonts
    {
        public const string BodyPath = "Fonts/Mulmaru";
        public const string HeadingPath = "Fonts/Galmuri11-Bold";
        public const string FallbackPath = "Fonts/Galmuri11";
        public const string BodyFamily = "Mulmaru";
        public const string GalmuriFamily = "Galmuri11"; // Galmuri11 Bold shares the family, style "Bold"
        /// <summary>The fonts' pixel grid in TMP points (font size 12 = one font pixel per canvas unit).</summary>
        public const int Grid = 12;
        /// <summary>Texts at or above this size that are titles use <see cref="Heading"/>.</summary>
        public const int HeadingMinSize = 36;

        public enum Face { Body, Heading, Fallback }

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

        static readonly TMP_FontAsset[] faces = new TMP_FontAsset[3];
        static readonly bool[] tried = new bool[3];
        static bool warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < faces.Length; i++) { faces[i] = null; tried[i] = false; }
            warned = false;
            IsFallback = false;
            starGlyph = 0;
        }

        /// <summary>Test hook: forget the runtime assets, so the next UI build creates fresh ones with EMPTY atlases -
        /// what a fresh boot (WebGL page load) sees.</summary>
        internal static void ResetForTests() => ResetStatics();

        /// <summary>True when the body font could not be created (Galmuri11 or TMP's default font is used instead).</summary>
        public static bool IsFallback { get; private set; }

        /// <summary>Body font (Mulmaru with the Galmuri11 fallback).</summary>
        public static TMP_FontAsset Main => Get(Face.Body);

        /// <summary>Big-title font (Galmuri11 Bold with the Galmuri11 fallback); the body font if it cannot be created.</summary>
        public static TMP_FontAsset Heading => Get(Face.Heading);

        /// <summary>Galmuri11: fallback for the glyphs the other two lack.</summary>
        public static TMP_FontAsset Fallback => Get(Face.Fallback);

        /// <summary><see cref="Heading"/> for a title of at least <see cref="HeadingMinSize"/>, else <see cref="Main"/>.</summary>
        public static TMP_FontAsset ForTitle(float size) => Px(size) >= HeadingMinSize ? Heading : Main;

        static TMP_FontAsset Get(Face face)
        {
            int i = (int)face;
            if (faces[i] != null) return faces[i];
            if (!tried[i])
            {
                tried[i] = true;
                faces[i] = Create(face, DefaultMode);
            }
            if (faces[i] == null)
            {
                switch (face)
                {
                    case Face.Heading: return Main;
                    case Face.Body:
                        IsFallback = true;
                        faces[i] = Fallback != null ? Fallback : TMP_Settings.defaultFontAsset;
                        Warn("body font " + BodyPath + " could not be created - using " + (faces[i] != null ? faces[i].name : "<no font>"));
                        break;
                    default:
                        Warn("fallback font " + FallbackPath + " could not be created");
                        break;
                }
            }
            return faces[i];
        }

        static void Warn(string msg)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[UI] " + msg);
        }

        static string PathOf(Face face) => face == Face.Body ? BodyPath : face == Face.Heading ? HeadingPath : FallbackPath;

        /// <summary>A fresh dynamic asset of <paramref name="face"/> in <paramref name="mode"/> (null on failure, never
        /// throws). Body and Heading get Galmuri11 (<see cref="Fallback"/>) as their only fallback.</summary>
        public static TMP_FontAsset Create(Face face, Mode mode)
        {
            try
            {
                var font = Resources.Load<Font>(PathOf(face));
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
                fa.name = font.name + " (runtime " + mode + ")";
                fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (face != Face.Fallback)
                {
                    var galmuri = mode == DefaultMode ? Fallback : Create(Face.Fallback, mode);
                    if (galmuri != null) fa.fallbackFontAssetTable.Add(galmuri);
                }
                return fa;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UI] " + PathOf(face) + " " + mode + " font asset failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Puts every character of <paramref name="text"/> into the body and heading atlases (and what they lack into
        /// Galmuri11's) BEFORE any UI text is built or measured: glyphs rendered, metrics and font features read in one go.
        /// Without it the first layout of every text added its glyphs one by one in the middle of GetPreferredValues
        /// (TMP adds them lazily, font features are queued until the first render), while every later layout found the
        /// asset complete - the two did not have to agree. Returns the number of distinct characters requested.
        /// </summary>
        public static int Prewarm(string text)
        {
            var seen = new HashSet<char>();
            var sb = new System.Text.StringBuilder(text != null ? text.Length : 0);
            foreach (char c in text ?? "")
                if (!char.IsControl(c) && !char.IsSurrogate(c) && seen.Add(c)) sb.Append(c);
            if (sb.Length == 0) return 0;
            string chars = sb.ToString();
            var fallback = Fallback;
            foreach (var face in new[] { Face.Body, Face.Heading })
            {
                var fa = Get(face);
                if (fa == null) continue;
                fa.TryAddCharacters(chars, true);
                if (fallback == null || fa == fallback) continue;
                sb.Clear();
                foreach (char c in chars)
                    if (!char.IsWhiteSpace(c) && !fa.HasCharacter(c, false, false)) sb.Append(c);
                if (sb.Length > 0) fallback.TryAddCharacters(sb.ToString(), true);
            }
            return chars.Length;
        }

        /// <summary>Nearest multiple of the 12px grid (ties up), at least 12.</summary>
        public static float Px(float size) => Mathf.Max(1, Mathf.FloorToInt(size / Grid + 0.5f)) * Grid;

        /// <summary>The runtime body or heading asset (what every UI text must use).</summary>
        public static bool IsUiFont(TMP_FontAsset fa) => fa != null && (fa == Main || fa == Heading);

        /// <summary>Body = Mulmaru, Heading = Galmuri11 Bold, both with Galmuri11 as their fallback (no TMP default).</summary>
        public static bool AllFacesCreated
        {
            get
            {
                var b = Main; var h = Heading; var f = Fallback;
                return !IsFallback && b != null && h != null && f != null && b != h
                    && b.faceInfo.familyName == BodyFamily
                    && h.faceInfo.familyName == GalmuriFamily && h.faceInfo.styleName == "Bold"
                    && f.faceInfo.familyName == GalmuriFamily && f.faceInfo.styleName == "Regular"
                    && b.fallbackFontAssetTable.Count == 1 && b.fallbackFontAssetTable[0] == f
                    && h.fallbackFontAssetTable.Count == 1 && h.fallbackFontAssetTable[0] == f
                    && f.fallbackFontAssetTable.Count == 0;
            }
        }

        /// <summary>
        /// The asset that draws <paramref name="c"/> in a text using <paramref name="primary"/>: the primary itself or an
        /// entry of its own fallbackFontAssetTable - never TMP_Settings' global fallbacks / default font (LiberationSans).
        /// Null when none has it (TMP would then search LiberationSans or draw □).
        /// </summary>
        public static TMP_FontAsset Resolve(TMP_FontAsset primary, char c)
        {
            if (primary == null) return null;
            if (primary.HasCharacter(c, false, true)) return primary;
            var table = primary.fallbackFontAssetTable;
            if (table != null)
                foreach (var fb in table)
                    if (fb != null && fb.HasCharacter(c, false, true)) return fb;
            return null;
        }

        /// <summary>First character of <paramref name="s"/> neither the body font nor its Galmuri11 fallback can draw, or
        /// null when they have them all.</summary>
        public static string MissingGlyph(string s) => MissingGlyph(Main, s);

        /// <summary><see cref="MissingGlyph(string)"/> for texts in <paramref name="face"/> (Heading: Galmuri11 Bold + Galmuri11).
        /// No TMP types in the signature: the test assemblies do not reference TextMeshPro.</summary>
        public static string MissingGlyphIn(Face face, string s) => MissingGlyph(Get(face), s);

        static string MissingGlyph(TMP_FontAsset f, string s)
        {
            if (f == null) return s;
            foreach (char c in s ?? "")
                if (!char.IsWhiteSpace(c) && !char.IsControl(c) && !char.IsSurrogate(c) && Resolve(f, c) == null) return c.ToString();
            return null;
        }

        /// <summary>"family style" of the asset that draws <paramref name="c"/> in a <paramref name="face"/> text, or null
        /// when neither it nor its fallback has it.</summary>
        public static string DrawnBy(Face face, char c)
        {
            var fa = Resolve(Get(face), c);
            return fa == null ? null : fa.faceInfo.familyName + " " + fa.faceInfo.styleName;
        }

        static int starGlyph; // 0 = unknown, 1 = has ★, 2 = no

        /// <summary>"★" when the UI fonts have it (Mulmaru, Galmuri cover KS X 1001 symbols), else "*".</summary>
        public static string Star
        {
            get
            {
                if (starGlyph == 0) starGlyph = MissingGlyph("★") == null && MissingGlyphIn(Face.Heading, "★") == null ? 1 : 2;
                return starGlyph == 1 ? "★" : "*";
            }
        }
    }
}
