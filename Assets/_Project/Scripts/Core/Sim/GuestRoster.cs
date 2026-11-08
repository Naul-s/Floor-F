using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>Mutable runtime state of one guest (internal to the simulation; others see <see cref="GuestInfo"/>).</summary>
    internal sealed class GuestRuntime
    {
        public GuestId Id;
        public GuestDef Def;
        public PartyId Party;
        /// <summary>First member of a party (or a solo guest): the one who speaks lines.</summary>
        public bool IsSpeaker;
        public GuestState State;
        public GuestMood Mood;
        public FloorIndex Floor;
        public DestinationId Destination;
        public FloorIndex DestinationFloor;
        public float Patience;
        public float PatienceMax;
        /// <summary>The guest's own numbers (what bubbles show).</summary>
        public LuckProfile Luck;
        /// <summary>Numbers verdicts use today: <see cref="Luck"/>, or <see cref="StarNightRules.Apply"/> of it on a star night.</summary>
        public LuckProfile JudgeLuck;
        public float TipMultiplier = 1f;
        public bool FloorTipBonusApplied;
        public bool OccupancyTipBonusApplied;
        /// <summary>Rule 3: floors the car moved with this guest inside (exact, detours and reversals included).</summary>
        public double Travelled;
        /// <summary>A guest reacts to each floor / each headcount at most once per ride (no open/close farming).</summary>
        public readonly HashSet<int> SeenFloors = new HashSet<int>();
        public readonly HashSet<int> SeenOccupancies = new HashSet<int>();
        public bool ImpatientSpoken;
        /// <summary>The "relabelled" line is said once per guest (no bubble spam on a stickered building).</summary>
        public bool RelabelledSpoken;
        // walking in/out (for IActorLocator)
        public int TransferFromSlot;
        public int TransferToRiderIndex;

        public GuestArchetypeId Archetype => Def.Id;
        public float Patience01 => PatienceMax <= 0 ? 0f : Math.Max(0f, Math.Min(1f, Patience / PatienceMax));

        /// <summary>Whole floors travelled since boarding (<see cref="LuckSource.RideDistance"/>); 0 while waiting.</summary>
        public int FloorsTravelled => (int)Math.Floor(Travelled + 1e-3);

        public GuestInfo ToInfo() => new GuestInfo(Id, Archetype, Party, State, Mood, Floor, Destination, DestinationFloor, Patience01, Luck, Def.hideNumbers, FloorsTravelled);
    }

    /// <summary><see cref="IGuestQuery"/>: active guests and per-floor waiting lines.</summary>
    public sealed class GuestRoster : IGuestQuery
    {
        readonly List<GuestId> activeIds = new List<GuestId>();
        internal readonly List<GuestRuntime> Active = new List<GuestRuntime>();
        readonly Dictionary<GuestId, GuestRuntime> byId = new Dictionary<GuestId, GuestRuntime>();
        readonly List<GuestRuntime>[] lines;

        public GuestRoster(int floorCount)
        {
            lines = new List<GuestRuntime>[floorCount];
            for (int i = 0; i < floorCount; i++) lines[i] = new List<GuestRuntime>();
        }

        public IReadOnlyList<GuestId> ActiveGuests => activeIds;

        public bool TryGet(GuestId guest, out GuestInfo info)
        {
            if (byId.TryGetValue(guest, out var g)) { info = g.ToInfo(); return true; }
            info = default;
            return false;
        }

        /// <summary>Clears <paramref name="results"/>, fills it with the floor's line (front first), returns the count.</summary>
        public int GetWaiting(FloorIndex floor, List<GuestId> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            if (floor.Value < 0 || floor.Value >= lines.Length) return 0;
            foreach (var g in lines[floor.Value]) results.Add(g.Id);
            return results.Count;
        }

        public int WaitingCount(FloorIndex floor) => floor.Value >= 0 && floor.Value < lines.Length ? lines[floor.Value].Count : 0;

        internal bool TryGetRuntime(GuestId id, out GuestRuntime g) => byId.TryGetValue(id, out g);

        internal List<GuestRuntime> Line(FloorIndex floor) => lines[floor.Value];

        internal int SlotOf(GuestRuntime g) => g.Floor.Value < lines.Length ? lines[g.Floor.Value].IndexOf(g) : -1;

        /// <summary>Adds a Waiting guest at the back of its floor's line; returns the line slot.</summary>
        internal int AddWaiting(GuestRuntime g)
        {
            Active.Add(g);
            activeIds.Add(g.Id);
            byId[g.Id] = g;
            var line = lines[g.Floor.Value];
            line.Add(g);
            return line.Count - 1;
        }

        internal void RemoveFromLine(GuestRuntime g)
        {
            foreach (var line in lines) if (line.Remove(g)) return;
        }

        /// <summary>Guest reached a terminal state (Delivered/Left).</summary>
        internal void Remove(GuestRuntime g)
        {
            RemoveFromLine(g);
            int i = Active.IndexOf(g);
            if (i >= 0) { Active.RemoveAt(i); activeIds.RemoveAt(i); }
            byId.Remove(g.Id);
        }

        internal void Clear()
        {
            Active.Clear();
            activeIds.Clear();
            byId.Clear();
            foreach (var line in lines) line.Clear();
        }
    }
}
