using System;

namespace FloorF.Core
{
    /// <summary><see cref="IBuildingQuery"/>: floors, their current (stickered) labels and destinations.</summary>
    public sealed class Building : IBuildingQuery
    {
        readonly FloorLabel[] original;
        readonly FloorLabel[] current;
        readonly DestinationId[] destinations;
        readonly IEventBus bus;

        public Building(GameData data, IEventBus bus)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            int n = data.FloorCount;
            original = new FloorLabel[n];
            current = new FloorLabel[n];
            destinations = new DestinationId[n];
            for (int i = 0; i < n; i++)
            {
                var f = data.GetFloor(new FloorIndex(i));
                original[i] = current[i] = f.InitialLabel;
                destinations[i] = f.Destination;
            }
        }

        public int FloorCount => original.Length;

        public bool IsValid(FloorIndex floor) => floor.Value >= 0 && floor.Value < original.Length;

        public FloorLabel GetLabel(FloorIndex floor) => IsValid(floor) ? current[floor.Value] : default;

        public FloorLabel GetOriginalLabel(FloorIndex floor) => IsValid(floor) ? original[floor.Value] : default;

        public bool IsRelabelled(FloorIndex floor) => IsValid(floor) && current[floor.Value] != original[floor.Value];

        public DestinationId GetDestination(FloorIndex floor) => IsValid(floor) ? destinations[floor.Value] : default;

        public bool TryFindFloor(DestinationId destination, out FloorIndex floor)
        {
            for (int i = 0; i < destinations.Length; i++)
                if (destinations[i] == destination) { floor = new FloorIndex(i); return true; }
            floor = default;
            return false;
        }

        public bool TryFindFloorByLabel(FloorLabel label, out FloorIndex floor)
        {
            for (int i = 0; i < current.Length; i++)
                if (current[i] == label) { floor = new FloorIndex(i); return true; }
            floor = default;
            return false;
        }

        /// <summary>Changes a label and publishes <see cref="FloorLabelChanged"/>. False if unchanged/invalid.</summary>
        internal bool SetLabel(FloorIndex floor, FloorLabel label)
        {
            if (!IsValid(floor) || label.IsEmpty || current[floor.Value] == label) return false;
            var prev = current[floor.Value];
            current[floor.Value] = label;
            bus.Publish(new FloorLabelChanged(floor, prev, label));
            return true;
        }

        /// <summary>Back to hotel.json labels (new game), publishing a change per relabelled floor.</summary>
        internal void ResetLabels()
        {
            for (int i = 0; i < current.Length; i++) SetLabel(new FloorIndex(i), original[i]);
        }
    }
}
