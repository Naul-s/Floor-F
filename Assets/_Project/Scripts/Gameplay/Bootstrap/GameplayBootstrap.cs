using FloorF.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorF.Gameplay
{
    /// <summary>
    /// MODULE BOOTSTRAP PATTERN (copy this shape for every module; no module edits the scene or a shared file):
    ///   1. SubsystemRegistration  - reset every static of the module (domain reload is OFF).
    ///   2. BeforeSceneLoad        - hook SceneManager.sceneLoaded.
    ///   3. sceneLoaded / AfterSceneLoad - when the scene named GameScenes.Main loads, create ONE root GameObject
    ///      "[Module]" inside that scene and add the module's root component. Being a scene object, it is destroyed
    ///      when Main unloads and rebuilt when Main loads again (= clean restart).
    ///   4. The root component: Awake = build own objects + register own services (Gameplay only);
    ///      OnEnable = subscribe events; Start or later = GameServices.Get other services; OnDestroy = unregister.
    ///
    /// Gameplay is special in one way: it also resets the Core statics (GameServices), because Core has
    /// noEngineReferences and cannot carry RuntimeInitializeOnLoadMethod. Keep that call here.
    /// </summary>
    static class GameplayBootstrap
    {
        static GameplayRoot root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            root = null;
            GameServices.Reset();
            GameServices.HandlerErrorSink = Debug.LogException;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => SceneManager.sceneLoaded += OnSceneLoaded;

        // Safety net for the very first scene in case sceneLoaded already fired; TryInstall is idempotent.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForStartScene() => TryInstall(SceneManager.GetActiveScene());

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInstall(scene);

        static void TryInstall(Scene scene)
        {
            if (!scene.IsValid() || scene.name != GameScenes.Main || root != null) return;
            var go = new GameObject("[Gameplay]");
            SceneManager.MoveGameObjectToScene(go, scene);
            root = go.AddComponent<GameplayRoot>();
        }
    }

    /// <summary>
    /// Gameplay module root - EXAMPLE to extend (gameplay-dev owns it). Today it only loads + registers the data
    /// and the world layout so every other module can already read them.
    /// </summary>
    sealed class GameplayRoot : MonoBehaviour
    {
        GameData data;
        HotelLayout layout;

        void Awake()
        {
            try
            {
                data = GameDataResources.Load();
            }
            catch (GameDataException ex)
            {
                Debug.LogError("[Gameplay] " + ex.Message);
                return;
            }
            layout = HotelLayout.From(data.Hotel);
            GameServices.Register(data);
            GameServices.Register<IWorldLayout>(layout);
            Debug.Log($"[Gameplay] installed: {data.FloorCount} floors, {data.Guests.Count} guests, {data.DayCount} days, {data.Stickers.Count} stickers");
        }

        void OnDestroy()
        {
            GameServices.Unregister(data);
            GameServices.Unregister<IWorldLayout>(layout);
        }
    }
}
