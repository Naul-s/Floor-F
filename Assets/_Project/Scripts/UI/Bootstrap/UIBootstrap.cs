using FloorF.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorF.UI
{
    /// <summary>UI module entry point. Same pattern as FloorF.Gameplay.GameplayBootstrap (see its doc comment).</summary>
    static class UIBootstrap
    {
        static UIRoot root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            root = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => SceneManager.sceneLoaded += OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForStartScene() => TryInstall(SceneManager.GetActiveScene());

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInstall(scene);

        static void TryInstall(Scene scene)
        {
            if (!scene.IsValid() || scene.name != GameScenes.Main || root != null) return;
            var go = new GameObject("[UI]");
            SceneManager.MoveGameObjectToScene(go, scene);
            root = go.AddComponent<UIRoot>();
        }
    }
}
