using System.IO;
using FloorF.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Main.unity from scratch and makes it the only scene in Build Settings.
    /// The scene is deliberately minimal (camera + light): every module builds its own objects at runtime
    /// (see FloorF.Gameplay.GameplayBootstrap), so nobody needs to edit this scene.
    /// Orchestrator-owned. Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.MainSceneBuilder.Build
    /// </summary>
    public static class MainSceneBuilder
    {
        [MenuItem("Floor F/Scenes/Rebuild Main Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // World camera: orthographic, slightly looking down at the 9-floor cut-away (GDD 8).
            // render-dev re-targets it at runtime (low-res RenderTexture) - do not rely on these exact numbers.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 15f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.10f, 1f);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 20f, -40f), Quaternion.Euler(10f, 0f, 0f));
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.AddComponent<UniversalAdditionalLightData>();

            Directory.CreateDirectory(Path.GetDirectoryName(GameScenes.MainPath));
            if (!EditorSceneManager.SaveScene(scene, GameScenes.MainPath))
                throw new IOException("MainSceneBuilder: could not save " + GameScenes.MainPath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScenes.MainPath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[MainSceneBuilder] saved " + GameScenes.MainPath + " and set it as the only build scene");
        }
    }
}
