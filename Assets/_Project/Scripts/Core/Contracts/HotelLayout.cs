using System;

namespace FloorF.Core
{
    /// <summary>
    /// Default <see cref="IWorldLayout"/>, computed from hotel.json "layout". Coordinate convention (world units):
    ///   +Y up, ground floor slab top at Y=0, floor n slab top at Y = n * floorHeight;
    ///   building spans X = shaftX - floorWidth/2 .. shaftX + floorWidth/2; the cut-away front faces -Z (camera side),
    ///   the back wall is at Z = +depth/2. Guests wait to the RIGHT of the shaft (+X), exit further right.
    /// </summary>
    public sealed class HotelLayout : IWorldLayout
    {
        readonly HotelLayoutDef def;
        readonly int capacity;

        public HotelLayout(HotelLayoutDef def, int floorCount, int capacity)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            if (floorCount < 1) throw new ArgumentOutOfRangeException(nameof(floorCount));
            FloorCount = floorCount;
            this.capacity = Math.Max(1, capacity);
        }

        public static HotelLayout From(HotelDef hotel) => new HotelLayout(hotel.layout, hotel.floorCount, hotel.capacity);

        public int FloorCount { get; }
        public float FloorHeight => def.floorHeight;
        public float FloorWidth => def.floorWidth;
        public float Depth => def.depth;
        public float ShaftX => def.shaftX;
        public float CarWidth => def.carWidth;
        public float BuildingHeight => FloorCount * def.floorHeight;

        public WorldPoint GetFloorAnchor(FloorIndex floor) => new WorldPoint(def.shaftX, floor.Value * def.floorHeight, 0f);

        public WorldPoint GetCarPosition(float floorPosition) => new WorldPoint(def.shaftX, floorPosition * def.floorHeight, 0f);

        public WorldPoint GetWaitingSlot(FloorIndex floor, int slot)
            => new WorldPoint(def.shaftX + def.waitingStartOffsetX + Math.Max(0, slot) * def.waitingSlotSpacing, floor.Value * def.floorHeight, 0f);

        /// <summary>Riders stand in two rows of capacity/2 across the car width (back row first).</summary>
        public WorldPoint GetCarSlotOffset(int riderIndex)
        {
            int perRow = Math.Max(1, (capacity + 1) / 2);
            int i = Math.Max(0, riderIndex) % (perRow * 2);
            int row = i / perRow;
            int col = i % perRow;
            float usable = def.carWidth * 0.8f;
            float x = perRow == 1 ? 0f : -usable / 2f + usable * col / (perRow - 1);
            float z = row == 0 ? def.depth * 0.15f : -def.depth * 0.1f;
            return new WorldPoint(x, 0f, z);
        }

        public WorldPoint GetExitPoint(FloorIndex floor) => new WorldPoint(def.shaftX + def.exitOffsetX, floor.Value * def.floorHeight, 0f);
    }
}
