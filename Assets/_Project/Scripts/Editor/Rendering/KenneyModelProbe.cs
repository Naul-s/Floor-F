using System.IO;
using System.Linq;
using System.Text;
using FloorF.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Inspection helpers for the Kenney models (not used by the game):
    ///  Dump    - FBX contents (meshes, materials, bones, clips) -> .harness/&lt;run&gt;/kenney_probe.txt
    ///  Preview - renders the raw characters, built guest prefabs and baked props from the camera side -> kenney_preview.png
    /// Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.KenneyModelProbe.Preview
    /// </summary>
    public static class KenneyModelProbe
    {
        static string OutDir()
        {
            var outDir = System.Environment.GetEnvironmentVariable("FLOORF_HARNESS_OUT");
            return string.IsNullOrEmpty(outDir) ? "Temp" : outDir;
        }

        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var dir in new[] { "MiniCharacters", "FurnitureKit", "MiniArcade" })
            {
                var folder = KenneyCatalogBuilder.ThirdParty + "/" + dir;
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                    var all = AssetDatabase.LoadAllAssetsAtPath(path);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    sb.AppendLine("## " + path + " anim=" + imp?.animationType + " scale=" + imp?.globalScale);
                    foreach (var clip in all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
                        sb.AppendLine("  clip " + clip.name + " len=" + clip.length.ToString("0.00"));
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        sb.AppendLine("  renderer " + r.name + " " + r.GetType().Name + " bounds=" + r.bounds.size.ToString("0.000") +
                                      " mats=[" + string.Join(",", r.sharedMaterials.Select(x => x == null ? "null" : x.name)) + "]");
                }
            }
            File.WriteAllText(Path.Combine(OutDir(), "kenney_probe.txt"), sb.ToString());
        }

        public static void Preview()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<KenneyCatalog>("Assets/_Project/Resources/" + KenneyCatalog.ResourcePath + ".asset");
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(30f, -20f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

            // row 1: raw characters with the original colormap
            var rawMat = new Material(catalog.Material);
            rawMat.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f)); // tile 0 = untouched colormap
            var names = new[] { "female-a", "female-b", "female-c", "female-d", "female-e", "female-f", "male-a", "male-b", "male-c", "male-d", "male-e", "male-f" };
            for (int i = 0; i < names.Length; i++)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyCatalogBuilder.ThirdParty + "/MiniCharacters/character-" + names[i] + ".fbx");
                var go = Object.Instantiate(src);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = rawMat;
                go.transform.localScale = Vector3.one * 0.018f;
                go.transform.position = new Vector3(-11f + i * 2f, 4.2f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }
            // row 2: built guest prefabs (tinted tiles)
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Guests.Length; i++)
                {
                    var g = catalog.Guests[i];
                    var go = Object.Instantiate(g.Prefab);
                    go.transform.localScale = Vector3.one * (1.4f / g.Bounds.size.y);
                    go.transform.position = new Vector3(-11f + i * 2f, 2.0f, 0f);
                    go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    if (catalog.Idle != null) catalog.Idle.SampleAnimation(go, 0.3f);
                }
                // row 3: props
                float x = -12f;
                foreach (var p in catalog.Props)
                {
                    var go = new GameObject(p.Name);
                    go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = catalog.Material;
                    float w = p.Mesh.bounds.size.x;
                    if (p.Mesh.bounds.size.y > 2.5f) { Object.DestroyImmediate(go); continue; }
                    if (x + w > 12f) break;
                    go.transform.position = new Vector3(x + w * 0.5f, -1.8f, 0f);
                    x += w + 0.15f;
                }
            }
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.3f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            camGo.transform.position = new Vector3(0f, 1.2f, 0f) - camGo.transform.forward * 20f;
            var rt = new RenderTexture(1600, 640, 24);
            cam.aspect = 1600f / 640f;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var path = Path.Combine(OutDir(), "kenney_preview.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[KenneyModelProbe] " + path);
        }

        public static void BuildAndPreview()
        {
            KenneyCatalogBuilder.Build();
            Preview();
        }
    }
}
