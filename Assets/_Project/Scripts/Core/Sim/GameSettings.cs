using System;

namespace FloorF.Core
{
    /// <summary>
    /// <see cref="IGameSettings"/>: player-facing settings. Every setter that actually changes a value publishes one
    /// <see cref="SettingsChanged"/> with the full new state; setting the same value again publishes nothing.
    /// <see cref="GameSession"/> reads <see cref="UseKorean"/> when it localizes GuestSpoke text.
    /// </summary>
    public sealed class GameSettings : IGameSettings
    {
        readonly IEventBus bus;
        bool useKorean;
        float masterVolume = 1f;
        bool showTutorialHints = true;

        public GameSettings(IEventBus bus)
        {
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public bool UseKorean
        {
            get => useKorean;
            set { if (useKorean == value) return; useKorean = value; Publish(); }
        }

        /// <summary>Clamped to 0..1; NaN is ignored.</summary>
        public float MasterVolume
        {
            get => masterVolume;
            set
            {
                if (float.IsNaN(value)) return;
                float v = Math.Max(0f, Math.Min(1f, value));
                if (v == masterVolume) return;
                masterVolume = v;
                Publish();
            }
        }

        public bool ShowTutorialHints
        {
            get => showTutorialHints;
            set { if (showTutorialHints == value) return; showTutorialHints = value; Publish(); }
        }

        void Publish() => bus.Publish(new SettingsChanged(useKorean, masterVolume, showTutorialHints));
    }
}
