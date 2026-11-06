namespace FloorF.Core
{
    /// <summary>
    /// <see cref="IMenuFlow"/>: title screen with an attract-mode demo, and pause.
    ///
    /// Title: the visible phase is <see cref="DayPhase.Title"/>. Behind it the session really plays Day 1 with a
    /// LuckAware <see cref="AutoPilot"/> (fresh run state; it loops when the demo day ends). Player input is refused
    /// (<see cref="AcceptsInput"/> false, every IElevatorControl/IGameFlow command returns false). Gameplay events
    /// (guests, elevator, luck, tips, speech) are published so presentation can show the demo; FLOW events
    /// (GameStarted, DayStarted, DayEnded, DayPhaseChanged, EndingReached) are not - the only flow event of the title
    /// is DayPhaseChanged(-> Title) when entering it. <see cref="StartGameFromTitle"/> = <see cref="RestartGame"/>.
    ///
    /// Pause: allowed in Intro/Running/Summary/Shop (not Title/Ending). Freezes the simulation clock (Tick is a no-op,
    /// so patience, spawns, motion and doors stop) and refuses player commands (elevator, flow, shop). Unity timeScale is
    /// untouched. RestartGame / ReturnToTitle unpause. Changes publish <see cref="PauseChanged"/>.
    /// </summary>
    public sealed partial class GameSession
    {
        bool onTitle;
        bool paused;
        AutoPilot demoPilot;

        // =========================================================================================== IMenuFlow

        public bool IsOnTitle => onTitle;
        public bool IsPaused => paused;

        public bool StartGameFromTitle()
        {
            if (!onTitle) return false;
            RestartGame();
            return true;
        }

        /// <summary>Boot and "back to title": fresh run, DayPhaseChanged(-> Title), demo day starts.</summary>
        public void ReturnToTitle()
        {
            var shown = Phase;
            Unpause();
            onTitle = true;
            ResetRun();
            if (shown != DayPhase.Title) bus.Publish(new DayPhaseChanged(day, shown, DayPhase.Title));
            BeginDemoDay();
        }

        public bool SetPaused(bool value)
        {
            if (!value) { Unpause(); return true; }
            if (paused) return true;
            if (onTitle || !CanPauseIn(phase)) return false;
            paused = true;
            bus.Publish(new PauseChanged(true));
            return true;
        }

        static bool CanPauseIn(DayPhase p)
            => p == DayPhase.Running || p == DayPhase.Intro || p == DayPhase.Summary || p == DayPhase.Shop;

        void Unpause()
        {
            if (!paused) return;
            paused = false;
            bus.Publish(new PauseChanged(false));
        }

        /// <summary>Flow events stay silent while the title demo plays.</summary>
        void PublishFlow<T>(T evt)
        {
            if (!onTitle) bus.Publish(evt);
        }

        // =========================================================================================== attract demo

        /// <summary>The demo bot (created on first use). Tests read it; null before the first title.</summary>
        internal AutoPilot DemoPilot => demoPilot;

        void BeginDemoDay()
        {
            if (demoPilot == null)
                demoPilot = new AutoPilot(Elevator, new DemoControl(this), Guests, Building, AutoPilotStrategy.LuckAware) { Data = data };
            demoPilot.Reset();
            BeginDayCore();
        }

        /// <summary>The demo day ended (Summary or an ending): play it again from scratch.</summary>
        void RestartDemo()
        {
            ResetRun();
            BeginDemoDay();
        }

        void StepDemoPilot() => demoPilot?.Step();

        /// <summary>Elevator control for the demo bot only: bypasses the title gate that blocks the player.</summary>
        sealed class DemoControl : IElevatorControl
        {
            readonly GameSession s;
            public DemoControl(GameSession s) { this.s = s; }
            public bool AcceptsInput => s.onTitle && s.SimAcceptsInput;
            public bool RequestFloor(FloorIndex floor) => s.onTitle && s.RequestFloorCore(floor);
            public bool OpenDoors() => s.onTitle && s.OpenDoorsCore();
            public bool CloseDoors() => s.onTitle && s.CloseDoorsCore();
        }
    }
}
