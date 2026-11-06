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
    /// Gameplay module root. Awake: load data, build the Core <see cref="GameSession"/> (rules engine), load the saved
    /// settings and register every Gameplay-owned service. First LateUpdate (after every module's Start and Update):
    /// boot to the title (DayPhaseChanged(None -> Title) + attract demo), or with <see cref="SkipTitleForTests"/>
    /// straight to Day 1 Intro (GameStarted + DayPhaseChanged). Every Update after that: Tick(Time.deltaTime).
    /// </summary>
    sealed class GameplayRoot : MonoBehaviour
    {
        /// <summary>Tests may fix the random seed (destination picks). Null = time-based.</summary>
        internal static int? SeedOverride;

        /// <summary>
        /// Tests: boot straight into Day 1 Intro (the pre-title behaviour) instead of the title + demo. Set it BEFORE
        /// loading Main; it stays set for later loads until you clear it (and is reset when play mode starts).
        /// </summary>
        internal static bool SkipTitleForTests;

        /// <summary>
        /// Tests: settings storage for the next Main loads (e.g. a shared <see cref="MemorySettingsStore"/> to simulate a
        /// relaunch). Null = PlayerPrefs, except in batch mode (CI/agents), which always uses a fresh in-memory store so
        /// test runs never write the real player prefs.
        /// </summary>
        internal static ISettingsStore SettingsStoreOverride;

        /// <summary>Seconds between settings flushes to disk (PlayerPrefs.Save) while values keep changing.</summary>
        const float SettingsFlushInterval = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SeedOverride = null;
            SkipTitleForTests = false;
            SettingsStoreOverride = null;
        }

        GameData data;
        HotelLayout layout;
        GameSession session;
        ActorLocator locator;
        SettingsPersistence settingsPersistence;
        float nextSettingsFlush;

        internal GameSession Session => session;

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
            int seed = SeedOverride ?? System.Environment.TickCount;
            session = new GameSession(data, GameServices.Events, new System.Random(seed));
            locator = new ActorLocator(session, layout);
            settingsPersistence = new SettingsPersistence(session.Settings, CreateSettingsStore(), GameServices.Events);
            settingsPersistence.Load();

            GameServices.Register(data);
            GameServices.Register<IWorldLayout>(layout);
            GameServices.Register<IBuildingQuery>(session.Building);
            GameServices.Register<IElevatorQuery>(session.Elevator);
            GameServices.Register<IElevatorControl>(session);
            GameServices.Register<IGuestQuery>(session.Guests);
            GameServices.Register<IScoreQuery>(session.Score);
            GameServices.Register<IDayQuery>(session);
            GameServices.Register<IGameFlow>(session);
            GameServices.Register<IShopService>(session);
            GameServices.Register<IActorLocator>(locator);
            GameServices.Register<IGameSettings>(session.Settings);
            GameServices.Register<IMenuFlow>(session);

            gameObject.AddComponent<KeyboardInput>().Bind(session);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<DebugOverlay>().Bind(session);
#endif
            Debug.Log($"[Gameplay] installed: {data.FloorCount} floors, {data.Guests.Count} guests, {data.DayCount} days, {data.Stickers.Count} stickers (seed {seed})");
        }

        internal static ISettingsStore CreateSettingsStore()
        {
            if (SettingsStoreOverride != null) return SettingsStoreOverride;
            if (Application.isBatchMode) return new MemorySettingsStore();
            return new PlayerPrefsSettingsStore();
        }

        void Update()
        {
            if (session != null && session.HasStarted) session.Tick(Time.deltaTime);
        }

        // First game event only after every module's Start and first Update (ARCHITECTURE "runtime assembly").
        void LateUpdate()
        {
            if (session == null) return;
            if (!session.HasStarted)
            {
                if (SkipTitleForTests) session.StartNewGame();
                else session.ReturnToTitle();
            }
            if (settingsPersistence != null && settingsPersistence.IsDirty && Time.unscaledTime >= nextSettingsFlush)
            {
                settingsPersistence.Flush();
                nextSettingsFlush = Time.unscaledTime + SettingsFlushInterval;
            }
        }

        void OnApplicationPause(bool pausing)
        {
            if (pausing) settingsPersistence?.Flush();
        }

        void OnApplicationQuit() => settingsPersistence?.Flush();

        void OnDestroy()
        {
            settingsPersistence?.Dispose();
            settingsPersistence = null;
            if (data == null) return;
            GameServices.Unregister(data);
            GameServices.Unregister<IWorldLayout>(layout);
            if (session == null) return;
            GameServices.Unregister<IBuildingQuery>(session.Building);
            GameServices.Unregister<IElevatorQuery>(session.Elevator);
            GameServices.Unregister<IElevatorControl>(session);
            GameServices.Unregister<IGuestQuery>(session.Guests);
            GameServices.Unregister<IScoreQuery>(session.Score);
            GameServices.Unregister<IDayQuery>(session);
            GameServices.Unregister<IGameFlow>(session);
            GameServices.Unregister<IShopService>(session);
            GameServices.Unregister<IActorLocator>(locator);
            GameServices.Unregister<IGameSettings>(session.Settings);
            GameServices.Unregister<IMenuFlow>(session);
        }
    }
}
