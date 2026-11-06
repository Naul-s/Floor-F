using System.IO;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Creates the material templates FloorF.Rendering instantiates at runtime, under Resources/Rendering/Materials.
    /// Living in Resources is what keeps URP Simple Lit / Unlit in WebGL builds (code-only Shader.Find would be stripped).
    /// Idempotent. Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.RenderingAssetBuilder.Build
    /// </summary>
    public static class RenderingAssetBuilder
    {
        const string Folder = "Assets/_Project/Resources/Rendering/Materials";

        [MenuItem("Floor F/Rendering/Rebuild Material Templates")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            Make("GreyboxLit", "Universal Render Pipeline/Simple Lit", m =>
            {
                SetFloat(m, "_Smoothness", 0.15f);
                SetFloat(m, "_SpecularHighlights", 0f);
                m.DisableKeyword("_SPECULAR_COLOR");
                SetFloat(m, "_EnvironmentReflections", 0f);
            });
            Make("GreyboxUnlit", "Universal Render Pipeline/Unlit", m => { });
            AssetDatabase.SaveAssets();
            Debug.Log("[RenderingAssetBuilder] material templates written to " + Folder);
        }

        static void SetFloat(Material m, string name, float v)
        {
            if (m.HasProperty(name)) m.SetFloat(name, v);
        }

        static void Make(string name, string shaderName, System.Action<Material> setup)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("RenderingAssetBuilder: shader not found: " + shaderName);
            var path = Folder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(shader) { name = name };
            else mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.enableInstancing = false;
            setup(mat);
            if (create) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
        }
    }
}
