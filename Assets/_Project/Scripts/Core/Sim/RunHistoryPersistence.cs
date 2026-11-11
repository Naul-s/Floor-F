using System;

namespace FloorF.Core
{
    /// <summary>Storage key names of the run history (the PlayerPrefs adapter adds its own prefix in front).</summary>
    public static class ProgressKeys
    {
        public const string FinishedRuns = "progress.finishedRuns";
        public const string MysteryMisses = "progress.mysteryMisses";
    }

    /// <summary>
    /// Saves <see cref="IRunHistory"/> (finished runs, mystery pity counter) in an <see cref="ISettingsStore"/>.
    /// <see cref="Load"/> restores it silently (call it before the first run); every <see cref="RunHistoryChanged"/> is
    /// written and flushed at once (changes are rare: a day start or an ending), so closing the tab cannot lose it.
    /// </summary>
    public sealed class RunHistoryPersistence : IDisposable
    {
        readonly IRunHistory history;
        readonly ISettingsStore store;
        IDisposable subscription;

        public RunHistoryPersistence(IRunHistory history, ISettingsStore store, IEventBus bus)
        {
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            subscription = bus.Subscribe<RunHistoryChanged>(OnChanged);
        }

        /// <summary>Stored values -> history (no event). Missing keys = 0 (a brand-new player).</summary>
        public void Load()
        {
            store.TryGetInt(ProgressKeys.FinishedRuns, out int runs);
            store.TryGetInt(ProgressKeys.MysteryMisses, out int misses);
            history.LoadRunHistory(runs, misses);
        }

        void OnChanged(RunHistoryChanged e)
        {
            store.SetInt(ProgressKeys.FinishedRuns, e.FinishedRuns);
            store.SetInt(ProgressKeys.MysteryMisses, e.MysteryMisses);
            store.Save();
        }

        public void Dispose()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
