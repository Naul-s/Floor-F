using FloorF.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorF.Audio
{
    /// <summary>Audio module entry point. Same pattern as FloorF.Gameplay.GameplayBootstrap (see its doc comment).</summary>
    static class AudioBootstrap
    {
        static AudioRoot root;

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
            var go = new GameObject("[Audio]");
            SceneManager.MoveGameObjectToScene(go, scene);
            root = go.AddComponent<AudioRoot>();
        }
    }

    /// <summary>
    /// Audio module root - EXAMPLE to extend (audio-dev owns it). AudioSources live under this object;
    /// the AudioListener is on the scene's Main Camera. Clips go in Resources/Audio/.
    /// </summary>
    sealed class AudioRoot : MonoBehaviour
    {
        readonly Subscriptions subs = new Subscriptions();

        void OnEnable()
        {
            subs.Add(GameServices.Events.Subscribe<ElevatorArrived>(OnArrived));
            subs.Add(GameServices.Events.Subscribe<LuckJudged>(OnLuckJudged));
        }

        void OnDisable() => subs.Clear();

        void OnArrived(ElevatorArrived e)
        {
            // e.g. play the "ding"
        }

        void OnLuckJudged(LuckJudged e)
        {
            // e.g. sparkle on Lucky, scream + music pitch dip on Unlucky
        }
    }
}
