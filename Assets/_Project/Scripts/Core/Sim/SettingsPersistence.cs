using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// Key/value storage for settings (engine-free). Gameplay adapts PlayerPrefs (WebGL: IndexedDB); tests and batch
    /// runs use <see cref="MemorySettingsStore"/> so nothing leaks into the real player prefs.
    /// </summary>
    public interface ISettingsStore
    {
        bool TryGetInt(string key, out int value);
        bool TryGetFloat(string key, out float value);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        /// <summary>Flush to disk (PlayerPrefs.Save). Called rarely (throttled by the owner).</summary>
        void Save();
    }

    /// <summary>Storage key names (the PlayerPrefs adapter adds its own prefix in front).</summary>
    public static class SettingsKeys
    {
        public const string UseKorean = "settings.useKorean";
        public const string MasterVolume = "settings.masterVolume";
        public const string ShowTutorialHints = "settings.showTutorialHints";
    }

    /// <summary>In-memory <see cref="ISettingsStore"/> (tests, batch mode). Share one instance to simulate a relaunch.</summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        readonly Dictionary<string, int> ints = new Dictionary<string, int>();
        readonly Dictionary<string, float> floats = new Dictionary<string, float>();

        public int SaveCount { get; private set; }
        public int KeyCount => ints.Count + floats.Count;

        public bool TryGetInt(string key, out int value) => ints.TryGetValue(key, out value);
        public bool TryGetFloat(string key, out float value) => floats.TryGetValue(key, out value);
        public void SetInt(string key, int value) => ints[key] = value;
        public void SetFloat(string key, float value) => floats[key] = value;
        public void Save() => SaveCount++;
    }

    /// <summary>
    /// Loads <see cref="GameSettings"/> from an <see cref="ISettingsStore"/> once (silently - nobody has read the
    /// settings yet at boot) and writes every <see cref="SettingsChanged"/> back. Writes are cheap; the disk flush is
    /// deferred: the owner calls <see cref="Flush"/> (throttled) so a volume slider drag does not save every frame.
    /// </summary>
    public sealed class SettingsPersistence : IDisposable
    {
        readonly GameSettings settings;
        readonly ISettingsStore store;
        IDisposable subscription;

        public SettingsPersistence(GameSettings settings, ISettingsStore store, IEventBus bus)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            subscription = bus.Subscribe<SettingsChanged>(OnChanged);
        }

        /// <summary>True when values were written since the last <see cref="Flush"/>.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>Stored values -> settings, without publishing. Missing keys keep the defaults.</summary>
        public void Load()
        {
            bool korean = settings.UseKorean, hints = settings.ShowTutorialHints;
            float volume = settings.MasterVolume;
            if (store.TryGetInt(SettingsKeys.UseKorean, out int k)) korean = k != 0;
            if (store.TryGetFloat(SettingsKeys.MasterVolume, out float v)) volume = v;
            if (store.TryGetInt(SettingsKeys.ShowTutorialHints, out int h)) hints = h != 0;
            settings.SetSilently(korean, volume, hints);
        }

        void OnChanged(SettingsChanged e)
        {
            store.SetInt(SettingsKeys.UseKorean, e.UseKorean ? 1 : 0);
            store.SetFloat(SettingsKeys.MasterVolume, e.MasterVolume);
            store.SetInt(SettingsKeys.ShowTutorialHints, e.ShowTutorialHints ? 1 : 0);
            IsDirty = true;
        }

        /// <summary>Saves the store if anything changed. Returns true if it saved.</summary>
        public bool Flush()
        {
            if (!IsDirty) return false;
            IsDirty = false;
            store.Save();
            return true;
        }

        /// <summary>Flushes pending changes and stops listening.</summary>
        public void Dispose()
        {
            Flush();
            subscription?.Dispose();
            subscription = null;
        }
    }
}
