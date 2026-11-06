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
    }
}
