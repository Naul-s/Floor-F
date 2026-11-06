using FloorF.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorF.Rendering
{
    /// <summary>Rendering module entry point. Same pattern as FloorF.Gameplay.GameplayBootstrap (see its doc comment).</summary>
    static class RenderingBootstrap
    {
        static RenderingRoot root;

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
            var go = new GameObject("[Rendering]");
            SceneManager.MoveGameObjectToScene(go, scene);
            root = go.AddComponent<RenderingRoot>();
        }
    }

    /// <summary>
    /// Rendering module root - EXAMPLE to extend (render-dev owns it). The scene only has "Main Camera" (tag MainCamera)
    /// and a directional light; configure/replace them at runtime from here (pixel RT, UI camera, greybox from IWorldLayout).
    /// </summary>
    sealed class RenderingRoot : MonoBehaviour
    {
        readonly Subscriptions subs = new Subscriptions();
        IWorldLayout layout;

        void OnEnable()
        {
            subs.Add(GameServices.Events.Subscribe<DoorsOpened>(OnDoorsOpened));
        }

        void OnDisable() => subs.Clear();

        void Start()
        {
            GameServices.TryGet(out layout); // services are safe to resolve from Start
        }

        void OnDoorsOpened(DoorsOpened e)
        {
            // e.g. flash the floor light red when e.Label is someone's unlucky number (react to LuckJudged for per-guest results)
        }
    }
}
