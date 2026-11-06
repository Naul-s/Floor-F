using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary><see cref="IElevatorQuery"/> state of the single car. Mutated only by <see cref="GameSession"/>.</summary>
    public sealed class Elevator : IElevatorQuery
    {
        readonly List<GuestId> riderIds = new List<GuestId>();
        internal readonly List<GuestRuntime> RiderGuests = new List<GuestRuntime>();

        public Elevator(int capacity) { Capacity = capacity; }

        public FloorIndex CurrentFloor { get; internal set; }
        public bool HasTarget { get; internal set; }
        public FloorIndex TargetFloor { get; internal set; }
        public float Position { get; internal set; }
        public ElevatorMotion Motion { get; internal set; }
        public DoorState Doors { get; internal set; }
        public int Occupancy => riderIds.Count;
        public int Capacity { get; }
        public bool IsFull => riderIds.Count >= Capacity;
        public IReadOnlyList<GuestId> Riders => riderIds;

        internal void AddRider(GuestRuntime g)
        {
            RiderGuests.Add(g);
            riderIds.Add(g.Id);
        }

        internal void RemoveRider(GuestRuntime g)
        {
            int i = RiderGuests.IndexOf(g);
            if (i < 0) return;
            RiderGuests.RemoveAt(i);
            riderIds.RemoveAt(i);
        }

        internal int IndexOfRider(GuestRuntime g) => RiderGuests.IndexOf(g);

        /// <summary>Empty car parked at the ground floor, doors closed.</summary>
        internal void Reset()
        {
            RiderGuests.Clear();
            riderIds.Clear();
            CurrentFloor = FloorIndex.Ground;
            HasTarget = false;
            TargetFloor = FloorIndex.Ground;
            Position = 0f;
            Motion = ElevatorMotion.Idle;
            Doors = DoorState.Closed;
        }
    }
}
