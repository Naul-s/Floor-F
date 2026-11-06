using FloorF.Core;
using UnityEngine;

namespace FloorF.Gameplay
{
    /// <summary>
    /// <see cref="ISettingsStore"/> on PlayerPrefs (WebGL: IndexedDB, flushed by <see cref="Save"/>). Every key is
    /// <see cref="Prefix"/> + a <see cref="SettingsKeys"/> name, so tests can use a private prefix and delete it after.
    /// </summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        /// <summary>Real game prefix.</summary>
        public const string DefaultPrefix = "FloorF.";

        public string Prefix { get; }

        public PlayerPrefsSettingsStore(string prefix = DefaultPrefix) { Prefix = prefix ?? DefaultPrefix; }

        public bool TryGetInt(string key, out int value)
        {
            var k = Prefix + key;
            bool has = PlayerPrefs.HasKey(k);
            value = has ? PlayerPrefs.GetInt(k) : 0;
            return has;
        }

        public bool TryGetFloat(string key, out float value)
        {
            var k = Prefix + key;
            bool has = PlayerPrefs.HasKey(k);
            value = has ? PlayerPrefs.GetFloat(k) : 0f;
            return has;
        }

        public void SetInt(string key, int value) => PlayerPrefs.SetInt(Prefix + key, value);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(Prefix + key, value);
        public void Save() => PlayerPrefs.Save();

        /// <summary>Removes this prefix's settings keys (tests clean up after themselves).</summary>
        public void DeleteAll()
        {
            PlayerPrefs.DeleteKey(Prefix + SettingsKeys.UseKorean);
            PlayerPrefs.DeleteKey(Prefix + SettingsKeys.MasterVolume);
            PlayerPrefs.DeleteKey(Prefix + SettingsKeys.ShowTutorialHints);
            PlayerPrefs.Save();
        }
    }
}
