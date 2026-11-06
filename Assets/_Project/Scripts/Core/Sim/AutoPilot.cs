using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// Naive elevator bot driving only the public service interfaces (tests, attract/demo mode).
    /// Delivers riders to the nearest destination, otherwise fetches the nearest waiting guest. Ignores luck.
    /// Call <see cref="Step"/> once per frame/tick.
    /// </summary>
    public sealed class AutoPilot
    {
        readonly IElevatorQuery elevator;
        readonly IElevatorControl control;
        readonly IGuestQuery guests;
        readonly IBuildingQuery building;
        readonly List<GuestId> buffer = new List<GuestId>();
        int closedAtFloor = -1;

        public AutoPilot(IElevatorQuery elevator, IElevatorControl control, IGuestQuery guests, IBuildingQuery building)
        {
            this.elevator = elevator ?? throw new ArgumentNullException(nameof(elevator));
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.guests = guests ?? throw new ArgumentNullException(nameof(guests));
            this.building = building ?? throw new ArgumentNullException(nameof(building));
        }

        public void Step()
        {
            if (!control.AcceptsInput || elevator.Motion != ElevatorMotion.Idle) return;
            var doors = elevator.Doors;
            if (doors == DoorState.Opening || doors == DoorState.Closing) return;
            var here = elevator.CurrentFloor;

            if (doors == DoorState.Open)
            {
                foreach (var id in guests.ActiveGuests)
                    if (guests.TryGet(id, out var info) && (info.State == GuestState.Boarding || info.State == GuestState.Exiting)) return;
                if (control.CloseDoors()) closedAtFloor = here.Value;
                return;
            }
            if (elevator.HasTarget) return;

            // 1) a rider wants to get off here
            int bestRiderFloor = -1, bestDist = int.MaxValue;
            foreach (var id in elevator.Riders)
            {
                if (!guests.TryGet(id, out var info)) continue;
                if (info.DestinationFloor == here) { Open(); return; }
                int d = Math.Abs(info.DestinationFloor.Value - here.Value);
                if (d < bestDist) { bestDist = d; bestRiderFloor = info.DestinationFloor.Value; }
            }

            // 2) someone waits here (and we did not just close on them)
            if (!elevator.IsFull && closedAtFloor != here.Value && guests.GetWaiting(here, buffer) > 0) { Open(); return; }

            // 3) deliver riders
            if (bestRiderFloor >= 0) { Go(bestRiderFloor); return; }

            // 4) fetch the nearest waiting guest elsewhere
            int bestWait = -1; bestDist = int.MaxValue;
            for (int f = 0; f < building.FloorCount; f++)
            {
                if (f == here.Value) continue;
                if (guests.GetWaiting(new FloorIndex(f), buffer) == 0) continue;
                int d = Math.Abs(f - here.Value);
                if (d < bestDist) { bestDist = d; bestWait = f; }
            }
            if (bestWait >= 0) Go(bestWait);
        }

        void Open() => control.OpenDoors();

        void Go(int floor)
        {
            if (control.RequestFloor(new FloorIndex(floor))) closedAtFloor = -1;
        }
    }
}
