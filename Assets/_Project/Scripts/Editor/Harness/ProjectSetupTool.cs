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
    }
}
