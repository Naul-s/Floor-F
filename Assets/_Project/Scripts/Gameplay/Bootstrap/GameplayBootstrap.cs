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
        /// Tests: with <see cref="SkipTitleForTests"/>, also skip the "Day 0" training shift at boot (IGameFlow.SkipTutorial),
        /// so the first Intro every module sees is the first real day. Same lifetime rules as SkipTitleForTests.
        /// </summary>
        internal static bool SkipTutorialForTests;

        /// <summary>
        /// Tests: settings storage for the next Main loads (e.g. a shared <see cref="MemorySettingsStore"/> to simulate a
        /// relaunch). Null = PlayerPrefs, except in batch mode (CI/agents), which always uses a fresh in-memory store so
        /// test runs never write the real player prefs.
        /// </summary>
        internal static ISettingsStore SettingsStoreOverride;

        /// <summary>
        /// Tests: let OnApplicationFocus / OnApplicationPause pause the game in the Editor and in batch mode too (normally
        /// only players - the WebGL tab - do: in the Editor the Game view loses focus to every other window, which would
        /// pause play sessions and Editor test runs at random).
        /// </summary>
        internal static bool ForceFocusPauseForTests;

        /// <summary>Seconds between settings flushes to disk (PlayerPrefs.Save) while values keep changing.</summary>
        const float SettingsFlushInterval = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SeedOverride = null;
            SkipTitleForTests = false;
            SkipTutorialForTests = false;
            SettingsStoreOverride = null;
            ForceFocusPauseForTests = false;
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
            // WebGL: keep rendering while the frame has no focus (an embedding page's fullscreen button takes it):
            // otherwise the resized canvas stays black until a click. A running day still pauses (PauseForFocusLoss).
            Application.runInBackground = true;
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
            // every build (WebGL release too): inert until debug mode is toggled on deliberately (DebugCheats)
            gameObject.AddComponent<DebugOverlay>().Bind(session);
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
                if (SkipTitleForTests)
                {
                    session.StartNewGame();
                    if (SkipTutorialForTests) session.SkipTutorial();
                }
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
            if (!pausing) return;
            settingsPersistence?.Flush();
            if (FocusPauseActive) PauseForFocusLoss();
        }

        /// <summary>WebGL: the tab or the window lost focus.</summary>
        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && FocusPauseActive) PauseForFocusLoss();
        }

        static bool FocusPauseActive => ForceFocusPauseForTests || (!Application.isEditor && !Application.isBatchMode);

        /// <summary>
        /// Focus lost: a running day pauses (IMenuFlow.SetPaused(true) - the pause menu shows; no auto-resume). Nothing
        /// happens on the title, outside Running, when already paused, or while a Day 0 coach mark holds the day (it is
        /// frozen anyway, and the menu pause would release the hold). Returns true when it paused.
        /// </summary>
        /// <summary>
        /// WebGL page (WebGLTemplates/FloorF/index.html, SendMessage): the player left fullscreen - with Esc, which the
        /// browser takes for itself - so show the pause menu, same rules as losing focus.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public void WebPause() => PauseForFocusLoss();

        internal bool PauseForFocusLoss() => ShouldPauseOnFocusLoss(session) && session.SetPaused(true);

        internal static bool ShouldPauseOnFocusLoss(GameSession s)
            => s != null && s.HasStarted && !s.IsOnTitle && s.Phase == DayPhase.Running && !s.IsPaused && !s.TutorialHold;

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
