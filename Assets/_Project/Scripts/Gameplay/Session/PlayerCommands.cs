using FloorF.Core;

namespace FloorF.Gameplay
{
    /// <summary>Abstract player intents (keyboard today; UI buttons call the services directly).</summary>
    public enum PlayerIntent
    {
        None = 0,
        /// <summary>W / Up arrow: one floor above the current target (or the car).</summary>
        Up = 1,
        /// <summary>S / Down arrow: one floor below.</summary>
        Down = 2,
        /// <summary>Space: open if closed/closing, close if open/opening.</summary>
        ToggleDoors = 3,
        /// <summary>Enter: advance the flow (Intro -> Running, Summary -> Shop/next, Shop -> next day, Ending -> restart).</summary>
        Advance = 4,
    }

    /// <summary>Maps an intent to service calls. Pure (no Unity) so it is EditMode-testable.</summary>
    public static class PlayerCommands
    {
        public static bool Apply(PlayerIntent intent, IElevatorQuery elevator, IElevatorControl control, IBuildingQuery building, IDayQuery day, IGameFlow flow)
        {
            switch (intent)
            {
                case PlayerIntent.Up:
                case PlayerIntent.Down:
                {
                    if (!control.AcceptsInput) return false;
                    int from = elevator.HasTarget ? elevator.TargetFloor.Value : elevator.CurrentFloor.Value;
                    int to = from + (intent == PlayerIntent.Up ? 1 : -1);
                    if (to < 0 || to >= building.FloorCount) return false;
                    return control.RequestFloor(new FloorIndex(to));
                }
                case PlayerIntent.ToggleDoors:
                    if (!control.AcceptsInput) return false;
                    return elevator.Doors == DoorState.Closed || elevator.Doors == DoorState.Closing
                        ? control.OpenDoors()
                        : control.CloseDoors();
                case PlayerIntent.Advance:
                    switch (day.Phase)
                    {
                        case DayPhase.Intro: return flow.BeginDay();
                        case DayPhase.Summary: return flow.ContinueFromSummary();
                        case DayPhase.Shop: return flow.FinishShopping();
                        case DayPhase.Ending: flow.RestartGame(); return true;
                        default: return false;
                    }
                default:
                    return false;
            }
        }
    }
}
