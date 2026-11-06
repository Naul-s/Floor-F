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

    /// <summary>
    /// UI module root - EXAMPLE to extend (ui-dev owns it). Build canvases here at runtime: Screen Space - Camera only
    /// (Overlay is invisible to L2 captures), TextMeshPro text, EventSystem with InputSystemUIInputModule.
    /// Player commands go through IElevatorControl / IGameFlow / IShopService - never into Gameplay classes.
    /// </summary>
    sealed class UIRoot : MonoBehaviour
    {
        readonly Subscriptions subs = new Subscriptions();

        void OnEnable()
        {
            subs.Add(GameServices.Events.Subscribe<DayStarted>(OnDayStarted));
            subs.Add(GameServices.Events.Subscribe<LuckChanged>(OnLuckChanged));
        }

        void OnDisable() => subs.Clear();

        void OnDayStarted(DayStarted e)
        {
            // e.g. show "Day {e.Day.Number}/{e.DayCount}" banner
        }

        void OnLuckChanged(LuckChanged e)
        {
            // e.g. animate the Luck meter from e.Previous to e.Current (of e.Max)
        }
    }
}
