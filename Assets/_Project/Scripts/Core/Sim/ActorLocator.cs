using System;

namespace FloorF.Core
{
    /// <summary>
    /// <see cref="IActorLocator"/> computed from the simulation state + <see cref="IWorldLayout"/>:
    /// waiting guests stand in their line slot, riders at their car slot, walkers are interpolated between the two.
    /// Presentation may smooth on top (guests "snap" one slot forward when someone ahead leaves the line).
    /// </summary>
    public sealed class ActorLocator : IActorLocator
    {
        readonly GameSession session;
        readonly IWorldLayout layout;

        public ActorLocator(GameSession session, IWorldLayout layout)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public WorldPoint CarPosition => layout.GetCarPosition(session.Elevator.Position);

        public bool TryGetGuestPosition(GuestId guest, out WorldPoint position)
        {
            position = WorldPoint.Zero;
            if (!session.Guests.TryGetRuntime(guest, out var g)) return false;
            var car = CarPosition;
            switch (g.State)
            {
                case GuestState.Waiting:
                    position = layout.GetWaitingSlot(g.Floor, Math.Max(0, session.Guests.SlotOf(g)));
                    return true;
                case GuestState.Riding:
                    position = Add(car, layout.GetCarSlotOffset(Math.Max(0, session.Elevator.IndexOfRider(g))));
                    return true;
                case GuestState.Boarding:
                {
                    var from = layout.GetWaitingSlot(g.Floor, g.TransferFromSlot);
                    var to = Add(car, layout.GetCarSlotOffset(g.TransferToRiderIndex));
                    position = Lerp(from, to, session.TransferProgress01);
                    return true;
                }
                case GuestState.Exiting:
                {
                    var from = Add(car, layout.GetCarSlotOffset(Math.Max(0, g.TransferToRiderIndex)));
                    var to = layout.GetWaitingSlot(g.DestinationFloor, 0);
                    position = Lerp(from, to, session.TransferProgress01);
                    return true;
                }
                default:
                    return false;
            }
        }

        static WorldPoint Add(WorldPoint a, WorldPoint b) => new WorldPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        static WorldPoint Lerp(WorldPoint a, WorldPoint b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return new WorldPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }
    }
}
