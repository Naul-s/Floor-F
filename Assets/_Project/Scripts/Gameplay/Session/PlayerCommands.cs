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
        /// <summary>Space: open if closed/closing, close if open/opening (pressed again while that close waits: hold the doors).</summary>
        ToggleDoors = 3,
        /// <summary>Enter: advance the flow (Title -> new game, Intro -> Running, Summary -> Shop/next, Shop -> next day, Ending -> restart).</summary>
        Advance = 4,
        /// <summary>Esc: pause / resume (IMenuFlow.SetPaused; refused on Title/Ending).</summary>
        TogglePause = 5,
    }

    /// <summary>Physical keys the keyboard binding knows (engine-free, so the mapping is EditMode-testable).</summary>
    public enum KeyPress
    {
        None = 0,
        W, S, UpArrow, DownArrow, Space, Enter, NumpadEnter, Escape,
    }

    /// <summary>Maps an intent to service calls. Pure (no Unity) so it is EditMode-testable.</summary>
    public static class PlayerCommands
    {
        /// <summary>Key -> intent (KeyboardInput). Enter is the flow key, Esc the pause key.</summary>
        public static PlayerIntent IntentForKey(KeyPress key)
        {
            switch (key)
            {
                case KeyPress.W: case KeyPress.UpArrow: return PlayerIntent.Up;
                case KeyPress.S: case KeyPress.DownArrow: return PlayerIntent.Down;
                case KeyPress.Space: return PlayerIntent.ToggleDoors;
                case KeyPress.Enter: case KeyPress.NumpadEnter: return PlayerIntent.Advance;
                case KeyPress.Escape: return PlayerIntent.TogglePause;
                default: return PlayerIntent.None;
            }
        }

        /// <summary>Without a menu: Advance does nothing on Title, TogglePause is refused.</summary>
        public static bool Apply(PlayerIntent intent, IElevatorQuery elevator, IElevatorControl control, IBuildingQuery building, IDayQuery day, IGameFlow flow)
            => Apply(intent, elevator, control, building, day, flow, null);

        public static bool Apply(PlayerIntent intent, IElevatorQuery elevator, IElevatorControl control, IBuildingQuery building, IDayQuery day, IGameFlow flow, IMenuFlow menu)
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
                    if (elevator.Doors == DoorState.Closed || elevator.Doors == DoorState.Closing) return control.OpenDoors();
                    // open/opening: close - or, when a close is already pending (someone is still walking in), a second
                    // press holds the doors again (OpenDoors cancels a pending close; refused while a floor is requested)
                    return control.CloseDoors() || control.OpenDoors();
                case PlayerIntent.Advance:
                    switch (day.Phase)
                    {
                        case DayPhase.Title: return menu != null && menu.StartGameFromTitle();
                        case DayPhase.Intro: return flow.BeginDay();
                        case DayPhase.Summary: return flow.ContinueFromSummary();
                        case DayPhase.Shop: return flow.FinishShopping();
                        case DayPhase.Ending: flow.RestartGame(); return true;
                        default: return false;
                    }
                case PlayerIntent.TogglePause:
                    return menu != null && menu.SetPaused(!menu.IsPaused);
                default:
                    return false;
            }
        }
    }
}
