using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>How an <see cref="AutoPilot"/> drives.</summary>
    public enum AutoPilotStrategy
    {
        /// <summary>Default: nearest rider destination, else nearest waiting guest. Ignores luck entirely.</summary>
        LuckBlind = 0,
        /// <summary>
        /// Simple greedy luck player: scores every floor as the next stop by the label reactions of the riders inside,
        /// delivery/pickup value, waiting urgency and distance (so it detours to lucky labels and postpones floors that
        /// scare a rider); while guests board it closes early when the next boarder would make the headcount worse.
        /// Rule 3 (ride distance): a delivery stop also scores the distance the rider will have travelled there, so it
        /// prefers stop orders that avoid an unlucky distance (or hit a lucky one); when the best stop would still end an
        /// unlucky ride it takes a short detour (moving past floors without opening) if that fixes the distance cheaply.
        /// Patience first: no label-only stops while a waiting guest has used half their patience, and a delivery gains
        /// value with every floor the rider has already ridden (nobody is carried around forever).
        /// Star night: judges with 7 Lucky for everyone (<see cref="StarNightRules"/>) and wishes on every shooting star it
        /// sees (when the control is the <see cref="GameSession"/>). Same for the day's lucky-for-all number
        /// (<see cref="ISeasonQuery.LuckyForAll"/>, Christmas Eve's 12) when the control is also the ISeasonQuery.
        /// </summary>
        LuckAware = 1,
    }

    /// <summary>
    /// Elevator bot driving only the public service interfaces (tests, balance runs, attract/demo mode).
    /// <see cref="AutoPilotStrategy.LuckBlind"/> (default) delivers riders to the nearest destination, otherwise fetches the
    /// nearest waiting guest. <see cref="AutoPilotStrategy.LuckAware"/> plays for Luck (see the enum).
    /// Call <see cref="Step"/> once per frame/tick.
    /// </summary>
    public sealed class AutoPilot
    {
        // LuckAware heuristic weights (bot tuning, not game content)
        const float LabelLucky = 1f, LabelUnlucky = -4f, HonestRelabelled = -2.5f;
        const float HeadcountLucky = 1.5f, HeadcountUnlucky = -2.5f;
        const float DeliveryValue = 1f, PickupValue = 0.8f, UrgencyValue = 1f, PerFloorCost = 0.2f;
        const float DistanceLucky = 1f, DistanceUnlucky = -3f;
        /// <summary>Longest detour (extra floors) the bot takes to fix an unlucky ride distance.</summary>
        const int MaxDetourFloors = 6;
        /// <summary>No label-only stops while a waiting guest has used this much of their patience.</summary>
        const float HurryUrgency = 0.5f;
        /// <summary>Extra delivery value per floor a rider has already ridden.</summary>
        const float RideUrgencyPerFloor = 0.15f;

        readonly IElevatorQuery elevator;
        readonly IElevatorControl control;
        readonly IGuestQuery guests;
        readonly IBuildingQuery building;
        readonly List<GuestId> buffer = new List<GuestId>();
        int closedAtFloor = -1;

        // LuckAware memory: what each rider already reacted to (the session judges each floor/headcount once per ride)
        readonly Dictionary<GuestId, HashSet<int>> seenFloors = new Dictionary<GuestId, HashSet<int>>();
        readonly Dictionary<GuestId, HashSet<int>> seenHeadcounts = new Dictionary<GuestId, HashSet<int>>();
        readonly List<GuestInfo> riders = new List<GuestInfo>();
        readonly List<GuestInfo> group = new List<GuestInfo>();
        readonly List<GuestId> stale = new List<GuestId>();
        DoorState lastDoors = DoorState.Closed;

        // new-day detection (the control is usually the GameSession, which is also the IDayQuery)
        readonly IDayQuery dayQuery;
        /// <summary>Wish stars (LuckAware): only when the control is the session itself.</summary>
        readonly GameSession session;
        readonly List<int> stars = new List<int>();
        readonly ISeasonQuery seasonQuery;
        /// <summary>Star-night / lucky-for-all numbers per (own profile, star night, lucky number) (profiles are shared per archetype).</summary>
        readonly Dictionary<(LuckProfile, bool, int), LuckProfile> starProfiles = new Dictionary<(LuckProfile, bool, int), LuckProfile>();
        int lastDayNumber = -1;
        float lastElapsed = -1f;
        bool wasAccepting;

        public AutoPilot(IElevatorQuery elevator, IElevatorControl control, IGuestQuery guests, IBuildingQuery building)
            : this(elevator, control, guests, building, AutoPilotStrategy.LuckBlind) { }

        public AutoPilot(IElevatorQuery elevator, IElevatorControl control, IGuestQuery guests, IBuildingQuery building, AutoPilotStrategy strategy)
        {
            this.elevator = elevator ?? throw new ArgumentNullException(nameof(elevator));
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.guests = guests ?? throw new ArgumentNullException(nameof(guests));
            this.building = building ?? throw new ArgumentNullException(nameof(building));
            Strategy = strategy;
            dayQuery = control as IDayQuery;
            seasonQuery = control as ISeasonQuery;
            session = control as GameSession;
        }

        public AutoPilotStrategy Strategy { get; }

        /// <summary>Optional (LuckAware): lets the bot see guest traits (honest guests vs stickered floors). Null = numbers only.</summary>
        public GameData Data { get; set; }

        /// <summary>
        /// One decision. Never plays the "Day 0" tutorial shift (when the control is the session/IDayQuery): it is for
        /// people, has no clock, and a bot clearing it would skew balance runs - callers <c>SkipTutorial()</c> first.
        /// </summary>
        public void Step()
        {
            DetectNewDay();
            if (dayQuery != null && dayQuery.IsTutorial) return;
            if (Strategy == AutoPilotStrategy.LuckAware) { WishOnStars(); StepLuckAware(); }
            else StepLuckBlind();
        }

        /// <summary>Forgets everything about the current day (closed-on floor, LuckAware memory).</summary>
        public void Reset()
        {
            closedAtFloor = -1;
            seenFloors.Clear();
            seenHeadcounts.Clear();
            lastDoors = DoorState.Closed;
        }

        /// <summary>
        /// Per-day state must not leak into the next day (or a restart): reset when input was off since the last step,
        /// or when the day number changed / the day clock went back (works even if Step was not called between days).
        /// </summary>
        void DetectNewDay()
        {
            bool accepting = control.AcceptsInput;
            bool newDay = accepting && !wasAccepting;
            if (dayQuery != null)
            {
                int dayNumber = dayQuery.CurrentDay.Number;
                float elapsed = dayQuery.ElapsedSeconds;
                if (dayNumber != lastDayNumber || elapsed < lastElapsed) newDay = true;
                lastDayNumber = dayNumber;
                lastElapsed = elapsed;
            }
            wasAccepting = accepting;
            if (newDay || !accepting) Reset();
        }

        // ================================================================================================ LuckBlind

        void StepLuckBlind()
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

        // ================================================================================================ LuckAware

        /// <summary>Star night: a wish on every star crossing the sky right now.</summary>
        void WishOnStars()
        {
            if (session == null || !control.AcceptsInput || session.GetWishStarsInSky(stars) == 0) return;
            foreach (var id in stars) session.MakeWish(id);
        }

        /// <summary>The numbers a guest is judged by today (7 Lucky for all on a star night).</summary>
        LuckProfile Judging(GuestInfo g)
        {
            bool star = dayQuery != null && dayQuery.IsStarNight;
            int all = seasonQuery != null ? seasonQuery.LuckyForAll : 0;
            if (!star && all <= 0) return g.Luck;
            var key = (g.Luck, star, all);
            if (!starProfiles.TryGetValue(key, out var p))
            {
                p = star ? StarNightRules.Apply(g.Luck) : g.Luck;
                if (all > 0) p = LuckyForAllRules.Apply(p, all);
                starProfiles[key] = p;
            }
            return p;
        }

        void StepLuckAware()
        {
            if (!control.AcceptsInput) return; // DetectNewDay already forgot the day
            var doors = elevator.Doors;
            CollectRiders();
            // doors just finished closing: every rider reacted to this headcount
            if (doors == DoorState.Closed && lastDoors != DoorState.Closed)
                foreach (var r in riders) Seen(seenHeadcounts, r.Id).Add(elevator.Occupancy);
            lastDoors = doors;

            if (elevator.Motion != ElevatorMotion.Idle || doors == DoorState.Opening || doors == DoorState.Closing) return;
            var here = elevator.CurrentFloor;

            if (doors == DoorState.Open) { StepOpenLuckAware(here); return; }
            if (elevator.HasTarget) return;

            // every floor is a candidate stop: value of opening there minus travel
            int best = -1;
            float bestValue = float.NegativeInfinity;
            int free = elevator.Capacity - elevator.Occupancy;
            // label-only detours (doors opened just for a lucky sign) wait while someone is running out of patience
            bool hurry = MostUrgentWaiting() >= HurryUrgency;
            for (int f = 0; f < building.FloorCount; f++)
            {
                var floor = new FloorIndex(f);
                float value = 0f;
                bool errand = false;
                int travel = Math.Abs(f - here.Value);
                foreach (var r in riders)
                    if (r.DestinationFloor == floor)
                    {
                        // long rides get priority (nobody rides around forever, e.g. behind an honest guest's sticker aversion)
                        value += DeliveryValue + RideUrgencyPerFloor * r.FloorsTravelled + DistanceScore(r, r.FloorsTravelled + travel);
                        errand = true;
                    }
                if (free > 0 && f != closedAtFloor)
                {
                    int n = guests.GetWaiting(floor, buffer);
                    if (n > 0)
                    {
                        float urgency = 0f;
                        foreach (var id in buffer)
                            if (guests.TryGet(id, out var w)) urgency = Math.Max(urgency, 1f - w.Patience01);
                        value += PickupValue * Math.Min(n, free) + UrgencyValue * urgency;
                        errand = true;
                    }
                }
                float label = FloorScore(floor);
                if (!errand && (label <= 0f || f == here.Value || hurry)) continue; // nothing to gain there (or no time)
                value += label - PerFloorCost * Math.Abs(f - here.Value);
                if (value > bestValue) { bestValue = value; best = f; }
            }
            if (best < 0) return;
            if (best != here.Value && TryDetour(here, best, out int detour)) { Go(detour); return; }
            if (best == here.Value) OpenHere(here);
            else Go(best);
        }

        /// <summary>1 - patience of the most impatient waiting guest anywhere (0 = nobody waits).</summary>
        float MostUrgentWaiting()
        {
            float urgency = 0f;
            for (int f = 0; f < building.FloorCount; f++)
            {
                if (guests.GetWaiting(new FloorIndex(f), buffer) == 0) continue;
                foreach (var id in buffer)
                    if (guests.TryGet(id, out var w)) urgency = Math.Max(urgency, 1f - w.Patience01);
            }
            return urgency;
        }

        /// <summary>Rule 3 value of a ride of <paramref name="distance"/> floors for one rider (Friday the 13th aware).</summary>
        float DistanceScore(GuestInfo r, int distance)
        {
            var v = Judging(r).Judge(distance);
            if (dayQuery != null && dayQuery.IsFriday13 && distance == StoryRules.FridayDistance) v = LuckVerdict.Unlucky;
            return v == LuckVerdict.Lucky ? DistanceLucky : v == LuckVerdict.Unlucky ? DistanceUnlucky : 0f;
        }

        /// <summary>
        /// When going straight to <paramref name="stop"/> would end someone's ride on an unlucky distance, the cheapest
        /// floor to pass through first (no door opening there, so no label judgement) that makes the deliveries at the
        /// stop better, net of the extra travel.
        /// </summary>
        bool TryDetour(FloorIndex here, int stop, out int detour)
        {
            detour = -1;
            int direct = Math.Abs(stop - here.Value);
            float directScore = 0f;
            bool unlucky = false;
            foreach (var r in riders)
            {
                if (r.DestinationFloor.Value != stop) continue;
                float d = DistanceScore(r, r.FloorsTravelled + direct);
                directScore += d;
                if (d < 0f) unlucky = true;
            }
            if (!unlucky) return false;
            float bestGain = 0f;
            for (int x = 0; x < building.FloorCount; x++)
            {
                if (x == here.Value || x == stop) continue;
                int via = Math.Abs(x - here.Value) + Math.Abs(stop - x);
                int extra = via - direct;
                if (extra <= 0 || extra > MaxDetourFloors) continue;
                float score = 0f;
                foreach (var r in riders)
                    if (r.DestinationFloor.Value == stop) score += DistanceScore(r, r.FloorsTravelled + via);
                float gain = score - directScore - PerFloorCost * extra;
                if (gain > bestGain) { bestGain = gain; detour = x; }
            }
            return detour >= 0;
        }

        void OpenHere(FloorIndex here)
        {
            if (!control.OpenDoors()) return;
            foreach (var r in riders) Seen(seenFloors, r.Id).Add(here.Value);
        }

        void StepOpenLuckAware(FloorIndex here)
        {
            // the group once the current walker is done: riders staying + guests walking in
            group.Clear();
            bool boarding = false;
            foreach (var id in guests.ActiveGuests)
            {
                if (!guests.TryGet(id, out var info)) continue;
                if (info.State == GuestState.Exiting) return; // exits come first; nothing to decide yet
                if (info.State == GuestState.Boarding) { boarding = true; group.Add(info); }
            }
            foreach (var r in riders) group.Add(r);

            if (!boarding)
            {
                if (control.CloseDoors()) closedAtFloor = here.Value;
                return;
            }

            // who would board next: the front-most waiting unit that fits
            int count = group.Count;
            int line = guests.GetWaiting(here, buffer);
            for (int i = 0; i < line; i++)
            {
                if (!guests.TryGet(buffer[i], out var next) || next.DestinationFloor == here) continue;
                int size = 0;
                foreach (var id in buffer)
                    if (id == next.Id || (!next.Party.IsNone && guests.TryGet(id, out var m) && m.Party == next.Party)) size++;
                if (count + size > elevator.Capacity) continue;
                if (HasTrait(next, GuestTraits.Gambler) && !Judging(next).IsLucky(count + size)) continue;

                float now = HeadcountScore(count, default, 0);
                float then = HeadcountScore(count + size, next, size) + DeliveryValue * size;
                if (now > then && control.CloseDoors()) closedAtFloor = here.Value;
                return;
            }
        }

        void CollectRiders()
        {
            riders.Clear();
            foreach (var id in elevator.Riders)
                if (guests.TryGet(id, out var info)) riders.Add(info);
            // forget guests who are no longer on board
            Forget(seenFloors);
            Forget(seenHeadcounts);
        }

        void Forget(Dictionary<GuestId, HashSet<int>> map)
        {
            stale.Clear();
            foreach (var id in map.Keys)
                if (!guests.TryGet(id, out var info) || (info.State != GuestState.Riding && info.State != GuestState.Boarding && info.State != GuestState.Exiting))
                    stale.Add(id);
            foreach (var id in stale) map.Remove(id);
        }

        static HashSet<int> Seen(Dictionary<GuestId, HashSet<int>> map, GuestId id)
        {
            if (!map.TryGetValue(id, out var set)) { set = new HashSet<int>(); map[id] = set; }
            return set;
        }

        bool HasTrait(GuestInfo g, string trait)
            => Data != null && Data.TryGetGuest(g.Archetype, out var def) && def.HasTrait(trait);

        /// <summary>Label reactions of the current riders if the doors opened at <paramref name="floor"/>.</summary>
        float FloorScore(FloorIndex floor)
        {
            var label = building.GetLabel(floor);
            bool relabelled = building.IsRelabelled(floor);
            float score = 0f;
            bool star = dayQuery != null && dayQuery.IsStarNight;
            int all = seasonQuery != null ? seasonQuery.LuckyForAll : 0;
            foreach (var r in riders)
            {
                if (seenFloors.TryGetValue(r.Id, out var seen) && seen.Contains(floor.Value)) continue;
                var v = LuckyForAllRules.JudgeLabel(Judging(r), label, out _, star, all);
                if (v == LuckVerdict.Lucky) score += LabelLucky;
                else if (v == LuckVerdict.Unlucky) score += LabelUnlucky;
                if (relabelled && HasTrait(r, GuestTraits.Honest)) score += HonestRelabelled;
            }
            return score;
        }

        /// <summary>Headcount reactions of <see cref="group"/> (plus an extra unit of <paramref name="extraSize"/>) at <paramref name="headcount"/>.</summary>
        float HeadcountScore(int headcount, GuestInfo extra, int extraSize)
        {
            float score = 0f;
            foreach (var g in group) score += Headcount(g, headcount);
            if (extraSize > 0) score += Headcount(extra, headcount) * extraSize;
            return score;
        }

        float Headcount(GuestInfo g, int headcount)
        {
            if (seenHeadcounts.TryGetValue(g.Id, out var seen) && seen.Contains(headcount)) return 0f;
            var v = Judging(g).Judge(headcount);
            return v == LuckVerdict.Lucky ? HeadcountLucky : v == LuckVerdict.Unlucky ? HeadcountUnlucky : 0f;
        }
    }
}
