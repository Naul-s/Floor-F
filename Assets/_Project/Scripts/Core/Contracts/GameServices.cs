using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// Process-wide access point: the event bus + a type-keyed service registry. This is the ONLY global in the game;
    /// modules never reference each other's classes for communication, only these interfaces and events.
    ///
    /// Lifecycle (see Docs/ARCHITECTURE.md "Runtime assembly"):
    ///  - <see cref="Reset"/> runs at RuntimeInitializeLoadType.SubsystemRegistration (called by GameplayBootstrap,
    ///    because Core has noEngineReferences and cannot carry Unity attributes). Domain reload is OFF, so this is
    ///    what clears state between play sessions.
    ///  - The owner registers its services while installing (during the Main scene's load) and unregisters in OnDestroy.
    ///  - Consumers call <see cref="Get{T}"/> from Start() or later (or from event handlers) - never from Awake/OnEnable,
    ///    because module install order is undefined. Subscribing to <see cref="Events"/> in OnEnable is fine.
    /// </summary>
    public static class GameServices
    {
        static readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        static EventBus events = new EventBus();
        static Action<Exception> errorSink;

        public static IEventBus Events => events;

        /// <summary>Where exceptions thrown by event handlers go (Gameplay sets Debug.LogException). Cleared by <see cref="Reset"/> - set it again after.</summary>
        public static Action<Exception> HandlerErrorSink
        {
            get => errorSink;
            set { errorSink = value; events.HandlerErrorSink = value; }
        }

        /// <summary>Clears every service and replaces the bus (drops all subscriptions) and the error sink.</summary>
        public static void Reset()
        {
            services.Clear();
            events = new EventBus();
            errorSink = null;
        }

        /// <summary>Registers (or replaces - last one wins) the service for <typeparamref name="T"/>.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            services[typeof(T)] = service;
        }

        /// <summary>Removes the registration only if it is still <paramref name="service"/> (safe across scene reloads).</summary>
        public static bool Unregister<T>(T service) where T : class
        {
            if (service == null) return false;
            if (services.TryGetValue(typeof(T), out var cur) && ReferenceEquals(cur, service))
            {
                services.Remove(typeof(T));
                return true;
            }
            return false;
        }

        public static bool Has<T>() where T : class => services.ContainsKey(typeof(T));

        public static bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var o)) { service = (T)o; return true; }
            service = null;
            return false;
        }

        /// <summary>Throws with a hint when missing (usually: called from Awake, or the owner module is not installed).</summary>
        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var s)) return s;
            throw new InvalidOperationException(
                "GameServices: no " + typeof(T).Name + " registered. Resolve services in Start() or later, " +
                "and check that the owning module installed for the Main scene. Registered: " + string.Join(", ", RegisteredNames()));
        }

        public static IEnumerable<string> RegisteredNames()
        {
            foreach (var t in services.Keys) yield return t.Name;
        }
    }

    /// <summary>Scene names/paths shared by bootstraps, tests and editor builders.</summary>
    public static class GameScenes
    {
        /// <summary>Module bootstraps install only when a scene with this name loads.</summary>
        public const string Main = "Main";
        public const string MainPath = "Assets/_Project/Scenes/Main.unity";
    }
}
