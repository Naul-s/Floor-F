using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Orchestrator-owned project setting tweaks, applied by code so nobody hand-edits YAML.
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.ProjectSetupTool.ApplyWebRenderSettings
    /// </summary>
    public static class ProjectSetupTool
    {
        const string MobileRpAsset = "Assets/Settings/Mobile_RPAsset.asset";

        /// <summary>WebGL uses the "Mobile" quality level -> Mobile_RPAsset. renderScale 0.8 blurs the native-resolution HUD.</summary>
        [MenuItem("Floor F/Setup/Apply Web Render Settings")]
        public static void ApplyWebRenderSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAsset);
            if (asset == null) throw new System.IO.FileNotFoundException("ProjectSetupTool: missing " + MobileRpAsset);
            asset.renderScale = 1f;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetupTool] " + MobileRpAsset + " renderScale = 1");
        }

        const string TmpSettingsAsset = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>
        /// QA Phase 3c MUST-FIX: (1) WebGL Gzip + decompression fallback, so the build loads even when the host (itch.io)
        /// sends no Content-Encoding header; (2) default web canvas 1280x720 = UI reference resolution;
        /// (3) TMP modern Hangul line breaking (Korean wraps between words, not mid-word).
        /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.ProjectSetupTool.ApplyWebBuildSettings
        /// </summary>
        [MenuItem("Floor F/Setup/Apply Web Build Settings")]
        public static void ApplyWebBuildSettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            var tmp = AssetDatabase.LoadMainAssetAtPath(TmpSettingsAsset);
            if (tmp == null) throw new System.IO.FileNotFoundException("ProjectSetupTool: missing " + TmpSettingsAsset);
            var so = new SerializedObject(tmp);
            var prop = so.FindProperty("m_UseModernHangulLineBreakingRules");
            if (prop == null) throw new System.InvalidOperationException("ProjectSetupTool: TMP Settings has no m_UseModernHangulLineBreakingRules");
            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tmp);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetupTool] WebGL Gzip+fallback, web canvas 1280x720, TMP modern Hangul line breaking on");
        }

        const string TmpEssentials = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        /// <summary>
        /// Imports TextMesh Pro Essential Resources (default font asset, shaders, TMP Settings) into Assets/TextMesh Pro.
        /// Same as Window > TextMeshPro > Import TMP Essential Resources, non-interactive. EDITOR ONLY: the import is
        /// queued and runs on a later editor update, so it does not complete under batch -executeMethod ... -quit.
        /// (Already committed - only needed if Assets/TextMesh Pro is ever deleted.)
        /// </summary>
        [MenuItem("Floor F/Setup/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                Debug.Log("[ProjectSetupTool] TMP Essentials already present");
                return;
            }
#pragma warning disable CS0618 // AssetPackage.Package.Import replacement is not available on every 6000.x
            AssetDatabase.ImportPackage(TmpEssentials, false);
#pragma warning restore CS0618
            Debug.Log("[ProjectSetupTool] queued TMP Essential Resources import");
        }

        const string GalmuriTtf = "Assets/_Project/ThirdParty/Galmuri/Resources/Fonts/Galmuri11.ttf";

        /// <summary>
        /// Re-imports the Galmuri TTF so Unity writes a full, current TrueTypeFontImporter .meta (the hand-made stub was
        /// version 1). Include Font Data must stay ON: UI builds a dynamic TMP font asset from it at runtime (WebGL too).
        /// </summary>
        [MenuItem("Floor F/Setup/Reimport Fonts")]
        public static void ReimportFonts()
        {
            var importer = AssetImporter.GetAtPath(GalmuriTtf) as TrueTypeFontImporter;
            if (importer == null) throw new System.IO.FileNotFoundException("ProjectSetupTool: missing font importer for " + GalmuriTtf);
            importer.includeFontData = true;
            importer.fontTextureCase = FontTextureCase.Dynamic;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            Debug.Log("[ProjectSetupTool] reimported " + GalmuriTtf + " (includeFontData=true)");
        }

        const string ThirdParty = "Assets/_Project/ThirdParty";

        /// <summary>
        /// WebGL-friendly import settings for the approved third-party assets (icons as sprites, mono Vorbis audio,
        /// point-filtered colormaps). Idempotent; re-run after adding files under ThirdParty.
        /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.ProjectSetupTool.ConfigureThirdPartyImporters
        /// </summary>
        [MenuItem("Floor F/Setup/Configure Third-Party Importers")]
        public static void ConfigureThirdPartyImporters()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ThirdParty + "/MaterialSymbols" }))
            {
                var ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.textureShape = TextureImporterShape.Texture2D; // ensure-meta stubs imported as Cube
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 128;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport(); n++;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ThirdParty + "/Kenney" }))
            {
                var ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                if (ti == null) continue;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 512;
                ti.SaveAndReimport(); n++;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { ThirdParty }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai == null) continue;
                bool music = path.Contains("/KevinMacLeod/") || path.ToLowerInvariant().Contains("loop");
                ai.forceToMono = true;
                ai.loadInBackground = music;
                var s = ai.defaultSampleSettings;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = music ? 0.45f : 0.6f;
                s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport(); n++;
            }
            Debug.Log("[ProjectSetupTool] configured " + n + " third-party importers");
        }
    }
}
