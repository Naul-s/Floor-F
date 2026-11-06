using System;
using System.Collections.Generic;

namespace FloorF.Core
{
    /// <summary>
    /// The whole game rules engine (GDD 3-5, 7), engine-free and deterministic: time comes from <see cref="Tick"/>,
    /// randomness from the injected <see cref="Random"/>. Publishes every Events.cs event on the injected bus in the
    /// documented order. Gameplay (Unity) only feeds Tick(Time.deltaTime) and player commands.
    ///
    /// Services: <see cref="Building"/> (IBuildingQuery), <see cref="Elevator"/> (IElevatorQuery), <see cref="Guests"/>
    /// (IGuestQuery), <see cref="Score"/> (IScoreQuery); this object is IElevatorControl, IDayQuery, IGameFlow, IShopService.
    ///
    /// Door cycle: OpenDoors -> Opening (doorSeconds) -> Open: DoorsOpened, riders judged on the label, then riders for
    /// this floor exit (boardSeconds per guest/party), then waiting guests board while the doors stay open.
    /// CloseDoors (or RequestFloor) stops new boarding; once the current walker and all exits finish -> Closing
    /// (doorSeconds) -> DoorsClosed, riders judged on occupancy -> depart if a floor was requested.
    /// </summary>
    public sealed class GameSession : IElevatorControl, IDayQuery, IGameFlow, IShopService
    {
        /// <summary>Longest simulated slice per internal step (keeps big frame deltas stable).</summary>
        public const float MaxStepSeconds = 0.05f;

        readonly GameData data;
        readonly RulesDef rules;
        readonly IEventBus bus;
        readonly Random random;

        public Building Building { get; }
        public Elevator Elevator { get; }
        public GuestRoster Guests { get; }
        public ScoreKeeper Score { get; }

        /// <summary>IGameSettings (owner Gameplay registers it). GuestSpoke text follows <see cref="GameSettings.UseKorean"/>.</summary>
        public GameSettings Settings { get; }

        /// <summary>GuestSpoke texts in Korean (textKo) when available. Same value as <c>Settings.UseKorean</c>.</summary>
        public bool UseKorean { get => Settings.UseKorean; set => Settings.UseKorean = value; }

        /// <summary>
        /// Early day end: once every scheduled spawn of the day has appeared and no guest is active any more, the day
        /// ends after this many seconds (no bonus for the time left). A guest appearing in between cancels the wait.
        /// Data: <c>rules.earlyEndDelaySeconds</c> (negative/NaN = 0).
        /// </summary>
        public float EarlyEndDelaySeconds => rules.earlyEndDelaySeconds > 0f ? rules.earlyEndDelaySeconds : 0f;

        /// <summary>
        /// Speech spam guard: the same archetype does not repeat the same line trigger within this many seconds of day
        /// time (e.g. two identical guests spawning together show one bubble). <see cref="GuestLineTriggers.Special"/>
        /// is never throttled. Presentation tuning, not game content.
        /// </summary>
        public const float SameLineCooldownSeconds = 5f;

        // ---- day flow
        DayId day = DayId.First;
        DayPhase phase = DayPhase.None;
        EndingKind ending = EndingKind.None;
        DaySummary lastSummary;
        DayDef dayDef;
        SpawnDef[] spawns = Array.Empty<SpawnDef>();
        int nextSpawn;
        float elapsed;
        float emptyFor;
        bool started;
        int luckAtStart, dayDelivered, dayLeft, dayLucky, dayUnlucky;
        int nextGuestId = 1, nextPartyId = 1;

        // ---- doors / transfers
        float doorTimer;
        bool closeRequested;
        readonly List<GuestRuntime> transfer = new List<GuestRuntime>();
        bool transferIsExit;
        float transferTimer;

        // ---- shop
        readonly List<ShopOffer> offers = new List<ShopOffer>();
        readonly List<int> offerRemaining = new List<int>();

        readonly List<GuestRuntime> scratch = new List<GuestRuntime>();

        // ---- speech throttle: "archetype|trigger" -> day time it was last said
        readonly Dictionary<string, float> lastLineAt = new Dictionary<string, float>();

        public GameSession(GameData data, IEventBus bus, Random random)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            this.random = random ?? new Random(0);
            rules = data.Rules;
            Building = new Building(data, bus);
            Elevator = new Elevator(data.Capacity);
            Guests = new GuestRoster(data.FloorCount);
            Score = new ScoreKeeper(rules, bus);
            Settings = new GameSettings(bus);
            Elevator.Reset();
            dayDef = data.GetDay(day);
        }

        public GameData Data => data;
        public bool HasStarted => started;

        // =========================================================================================== IDayQuery

        public DayId CurrentDay => day;
        public int DayCount => data.DayCount;
        public DayPhase Phase => phase;
        public bool IsTutorial => dayDef != null && dayDef.tutorial;
        public float DurationSeconds => dayDef != null ? dayDef.durationSeconds : 0f;
        public float ElapsedSeconds => elapsed;
        public float RemainingSeconds => Math.Max(0f, DurationSeconds - elapsed);
        public EndingKind Ending => ending;
        public DaySummary LastSummary => lastSummary;
        public DayDef CurrentDayDef => dayDef;

        // =========================================================================================== IGameFlow

        /// <summary>First boot: GameStarted + DayPhaseChanged(None -> Intro). Same as <see cref="RestartGame"/>.</summary>
        public void StartNewGame() => RestartGame();

        public void RestartGame()
        {
            if (started)
            {
                DropAllGuests();
                Building.ResetLabels();
                Score.ResetForNewGame();
            }
            started = true;
            ending = EndingKind.None;
            lastSummary = default;
            day = DayId.First;
            CloseShop();
            ResetElevator();
            dayDef = data.GetDay(day);
            elapsed = 0f;
            bus.Publish(new GameStarted(data.DayCount, Score.Luck, Score.MaxLuck));
            SetPhase(DayPhase.Intro);
        }

        public bool BeginDay()
        {
            if (phase != DayPhase.Intro) return false;
            dayDef = data.GetDay(day);
            spawns = (SpawnDef[])dayDef.spawns.Clone();
            // stable sort by time (Array.Sort is unstable)
            var times = new float[spawns.Length];
            for (int i = 0; i < times.Length; i++) times[i] = spawns[i].time + i * 1e-6f;
            Array.Sort(times, spawns);
            nextSpawn = 0;
            elapsed = 0f;
            emptyFor = 0f;
            lastLineAt.Clear();
            luckAtStart = Score.Luck;
            dayDelivered = dayLeft = dayLucky = dayUnlucky = 0;
            Score.BeginDay();
            SetPhase(DayPhase.Running);
            bus.Publish(new DayStarted(day, data.DayCount, dayDef.durationSeconds, dayDef.tutorial));
            SpawnDue();
            return true;
        }

        public bool ContinueFromSummary()
        {
            if (phase != DayPhase.Summary) return false;
            if (data.IsLastDay(day))
            {
                ReachEnding(Score.Luck >= rules.prosperousEndingLuck ? EndingKind.Prosperous : EndingKind.Modest);
                return true;
            }
            if (OpenShop()) SetPhase(DayPhase.Shop);
            else GoToNextDayIntro();
            return true;
        }

        public bool FinishShopping()
        {
            if (phase != DayPhase.Shop) return false;
            CloseShop();
            GoToNextDayIntro();
            return true;
        }

        void GoToNextDayIntro()
        {
            day = day.Next;
            dayDef = data.GetDay(day);
            elapsed = 0f;
            ResetElevator();
            SetPhase(DayPhase.Intro);
        }

        void SetPhase(DayPhase next)
        {
            var prev = phase;
            phase = next;
            bus.Publish(new DayPhaseChanged(day, prev, next));
        }

        void ResetElevator()
        {
            Elevator.Reset();
            transfer.Clear();
            closeRequested = false;
            doorTimer = 0f;
            transferTimer = 0f;
        }

        // =========================================================================================== tick

        /// <summary>Advances the running day by <paramref name="deltaSeconds"/> (no-op outside Running).</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return;
            float remaining = deltaSeconds;
            while (remaining > 0f && phase == DayPhase.Running)
            {
                float step = Math.Min(remaining, MaxStepSeconds);
                remaining -= step;
                Step(step);
            }
        }

        void Step(float dt)
        {
            elapsed += dt;
            SpawnDue();
            StepPatience(dt);
            if (phase != DayPhase.Running) return;
            StepMotion(dt);
            if (phase != DayPhase.Running) return;
            StepDoors(dt);
            if (phase != DayPhase.Running) return;
            if (elapsed >= dayDef.durationSeconds) { EndDay(EndingKind.None); return; }
            StepEarlyEnd(dt);
        }

        /// <summary>True once the day had scheduled spawns, all of them appeared, and nobody is active.</summary>
        internal bool AllGuestsDone => spawns.Length > 0 && nextSpawn >= spawns.Length && Guests.Active.Count == 0;

        void StepEarlyEnd(float dt)
        {
            if (!AllGuestsDone) { emptyFor = 0f; return; }
            emptyFor += dt;
            if (emptyFor >= EarlyEndDelaySeconds - 1e-4f) EndDay(EndingKind.None);
        }

        // =========================================================================================== guests

        void SpawnDue()
        {
            while (phase == DayPhase.Running && nextSpawn < spawns.Length && spawns[nextSpawn].time <= elapsed)
                SpawnEntry(spawns[nextSpawn++]);
        }

        void SpawnEntry(SpawnDef s)
        {
            if (!data.TryGetGuest(s.Guest, out var def)) return;
            var floor = s.Floor;
            if (!Building.IsValid(floor)) return;
            FloorIndex destFloor;
            DestinationId dest;
            if (s.HasDestinationFloor)
            {
                destFloor = s.DestinationFloor;
                dest = Building.GetDestination(destFloor);
            }
            else if (!string.IsNullOrWhiteSpace(s.destination))
            {
                dest = new DestinationId(s.destination);
                if (!Building.TryFindFloor(dest, out destFloor)) return;
            }
            else
            {
                if (!TryPickDestination(def, floor, out dest, out destFloor)) return;
            }
            if (!Building.IsValid(destFloor) || destFloor == floor) return; // data error: nowhere to go
            SpawnParty(def, floor, dest, destFloor);
        }

        bool TryPickDestination(GuestDef def, FloorIndex from, out DestinationId dest, out FloorIndex destFloor)
        {
            var candidates = new List<FloorIndex>();
            var ids = new List<DestinationId>();
            foreach (var d in def.destinations)
            {
                var id = new DestinationId(d);
                if (Building.TryFindFloor(id, out var f) && f != from) { candidates.Add(f); ids.Add(id); }
            }
            if (candidates.Count == 0) { dest = default; destFloor = default; return false; }
            int k = random.Next(candidates.Count);
            dest = ids[k];
            destFloor = candidates[k];
            return true;
        }

        /// <summary>Spawns <c>def.partySize</c> guests waiting at <paramref name="floor"/>. Returns the first id. (Tests call this directly.)</summary>
        internal GuestId SpawnParty(GuestDef def, FloorIndex floor, DestinationId dest, FloorIndex destFloor)
        {
            int size = Math.Max(1, def.partySize);
            var party = size > 1 ? new PartyId(nextPartyId++) : PartyId.None;
            GuestId first = GuestId.None;
            for (int i = 0; i < size; i++)
            {
                var g = new GuestRuntime
                {
                    Id = new GuestId(nextGuestId++),
                    Def = def,
                    Party = party,
                    IsSpeaker = i == 0,
                    State = GuestState.Waiting,
                    Mood = GuestMood.Neutral,
                    Floor = floor,
                    Destination = dest,
                    DestinationFloor = destFloor,
                    Patience = def.patienceSeconds,
                    PatienceMax = def.patienceSeconds,
                    Luck = data.GetLuckProfile(def.Id),
                };
                if (i == 0) first = g.Id;
                int slot = Guests.AddWaiting(g);
                bus.Publish(new GuestSpawned(g.Id, def.Id, party, floor, dest, destFloor, slot));
                Speak(g, GuestLineTriggers.Spawn);
            }
            return first;
        }

        /// <summary>Spawns by archetype id (tests / debug).</summary>
        internal GuestId SpawnGuest(string archetype, int physicalFloor, int destinationPhysicalFloor)
        {
            var def = data.GetGuest(new GuestArchetypeId(archetype));
            var dest = FloorIndex.FromPhysicalNumber(destinationPhysicalFloor);
            return SpawnParty(def, FloorIndex.FromPhysicalNumber(physicalFloor), Building.GetDestination(dest), dest);
        }

        void StepPatience(float dt)
        {
            scratch.Clear();
            foreach (var g in Guests.Active) if (g.State == GuestState.Waiting) scratch.Add(g);
            foreach (var g in scratch)
            {
                g.Patience -= dt;
                if (g.Patience <= 0f)
                {
                    g.Patience = 0f;
                    Leave(g, GuestLeaveReason.PatienceRanOut);
                    if (CheckHaunted()) return;
                }
                else if (g.Patience01 < rules.impatientPatienceFraction && !g.ImpatientSpoken)
                {
                    g.ImpatientSpoken = true;
                    SetMood(g, GuestMood.Impatient);
                    Speak(g, GuestLineTriggers.Impatient);
                }
            }
        }

        void Leave(GuestRuntime g, GuestLeaveReason reason)
        {
            Speak(g, GuestLineTriggers.Left);
            g.State = GuestState.Left;
            Guests.Remove(g);
            bus.Publish(new GuestLeft(g.Id, g.Floor, reason));
            if (reason == GuestLeaveReason.PatienceRanOut || reason == GuestLeaveReason.Refused)
            {
                dayLeft++;
                Score.AddLuck(-rules.patienceOutLuckPenalty, LuckSource.Patience);
            }
        }

        void DropAllGuests()
        {
            scratch.Clear();
            scratch.AddRange(Guests.Active);
            foreach (var g in scratch)
            {
                g.State = GuestState.Left;
                bus.Publish(new GuestLeft(g.Id, g.Floor, GuestLeaveReason.DayEnded));
            }
            Guests.Clear();
            ResetElevator();
        }

        void SetMood(GuestRuntime g, GuestMood mood)
        {
            if (g.Mood == mood) return;
            var prev = g.Mood;
            g.Mood = mood;
            bus.Publish(new GuestMoodChanged(g.Id, prev, mood));
        }

        void Speak(GuestRuntime g, string trigger)
        {
            if (!g.IsSpeaker) return;
            var line = g.Def.FindLine(trigger);
            if (line == null) return;
            var text = line.Get(UseKorean);
            if (string.IsNullOrEmpty(text)) return;
            if (trigger != GuestLineTriggers.Special)
            {
                var key = g.Def.id + "|" + trigger;
                if (lastLineAt.TryGetValue(key, out var at) && elapsed - at < SameLineCooldownSeconds - 1e-4f) return;
                lastLineAt[key] = elapsed;
            }
            bus.Publish(new GuestSpoke(g.Id, trigger, text));
        }

        // =========================================================================================== elevator control

        public bool AcceptsInput => phase == DayPhase.Running;

        public bool RequestFloor(FloorIndex floor)
        {
            if (!AcceptsInput || !Building.IsValid(floor)) return false;
            if (Elevator.Motion != ElevatorMotion.Idle)
            {
                if (Elevator.TargetFloor == floor) return false;
                Elevator.TargetFloor = floor;
                return true;
            }
            if (floor == Elevator.CurrentFloor)
            {
                if (!Elevator.HasTarget) return false;
                Elevator.HasTarget = false; // cancel a pending departure
                return true;
            }
            if (Elevator.HasTarget && Elevator.TargetFloor == floor) return false;
            Elevator.HasTarget = true;
            Elevator.TargetFloor = floor;
            if (Elevator.Doors == DoorState.Closed) Depart();
            else if (Elevator.Doors == DoorState.Open || Elevator.Doors == DoorState.Opening) closeRequested = true;
            return true;
        }

        public bool OpenDoors()
        {
            if (!AcceptsInput || Elevator.Motion != ElevatorMotion.Idle) return false;
            switch (Elevator.Doors)
            {
                case DoorState.Closed:
                    Elevator.Doors = DoorState.Opening;
                    doorTimer = rules.doorSeconds;
                    closeRequested = false;
                    return true;
                case DoorState.Closing:
                    Elevator.Doors = DoorState.Opening;
                    doorTimer = Math.Max(0f, rules.doorSeconds - doorTimer);
                    closeRequested = false;
                    return true;
                default:
                    if (closeRequested && !Elevator.HasTarget) { closeRequested = false; return true; }
                    return false;
            }
        }

        public bool CloseDoors()
        {
            if (!AcceptsInput) return false;
            if (Elevator.Doors != DoorState.Open && Elevator.Doors != DoorState.Opening) return false;
            if (closeRequested) return false;
            closeRequested = true;
            return true;
        }

        void Depart()
        {
            Elevator.HasTarget = true;
            Elevator.Motion = Elevator.TargetFloor.Value > Elevator.Position ? ElevatorMotion.MovingUp : ElevatorMotion.MovingDown;
            bus.Publish(new ElevatorDeparted(Elevator.CurrentFloor, Elevator.TargetFloor));
        }

        void StepMotion(float dt)
        {
            if (Elevator.Motion == ElevatorMotion.Idle) return;
            float pos = Elevator.Position;
            float target = Elevator.TargetFloor.Value;
            float move = dt / Math.Max(0.0001f, rules.elevatorSecondsPerFloor);
            float dist = Math.Abs(target - pos);
            bool up = target > pos;
            Elevator.Motion = up ? ElevatorMotion.MovingUp : ElevatorMotion.MovingDown;
            bool arrive = move >= dist - 1e-5f;
            float next = arrive ? target : pos + (up ? move : -move);

            // floors strictly crossed (not the target): LED tick, no judgement (GDD rule 1: passing is fine)
            if (up)
            {
                for (int f = (int)Math.Floor(pos + 1e-4f) + 1; f <= (int)Math.Floor(next + 1e-4f) && f < target; f++) Pass(f);
            }
            else
            {
                for (int f = (int)Math.Ceiling(pos - 1e-4f) - 1; f >= (int)Math.Ceiling(next - 1e-4f) && f > target; f--) Pass(f);
            }
            Elevator.Position = next;

            if (arrive)
            {
                var floor = Elevator.TargetFloor;
                Elevator.Position = floor.Value;
                Elevator.CurrentFloor = floor;
                Elevator.Motion = ElevatorMotion.Idle;
                Elevator.HasTarget = false;
                foreach (var g in Elevator.RiderGuests) g.Floor = floor;
                bus.Publish(new ElevatorArrived(floor, Building.GetLabel(floor)));
            }
        }

        void Pass(int f)
        {
            var floor = new FloorIndex(f);
            Elevator.CurrentFloor = floor;
            foreach (var g in Elevator.RiderGuests) g.Floor = floor;
            bus.Publish(new ElevatorPassedFloor(floor, Building.GetLabel(floor)));
        }

        // =========================================================================================== doors

        void StepDoors(float dt)
        {
            if (Elevator.Doors == DoorState.Opening)
            {
                doorTimer -= dt;
                dt = 0f;
                if (doorTimer <= 0f)
                {
                    Elevator.Doors = DoorState.Open;
                    OnDoorsOpened();
                    if (phase != DayPhase.Running) return;
                }
            }
            if (Elevator.Doors == DoorState.Open)
            {
                StepTransfers(dt);
                if (phase != DayPhase.Running) return;
                dt = 0f;
            }
            if (Elevator.Doors == DoorState.Closing)
            {
                doorTimer -= dt;
                if (doorTimer <= 0f)
                {
                    Elevator.Doors = DoorState.Closed;
                    OnDoorsClosed();
                }
            }
        }

        void OnDoorsOpened()
        {
            var floor = Elevator.CurrentFloor;
            var label = Building.GetLabel(floor);
            bus.Publish(new DoorsOpened(floor, label, Elevator.Occupancy));
            scratch.Clear();
            scratch.AddRange(Elevator.RiderGuests);
            foreach (var g in scratch) JudgeFloor(g, floor, label);
            CheckHaunted();
        }

        /// <summary>GDD rule 1: label of a floor whose doors open while the guest is inside.</summary>
        void JudgeFloor(GuestRuntime g, FloorIndex floor, FloorLabel label)
        {
            if (!g.SeenFloors.Add(floor.Value)) return;
            var verdict = LabelNumbers.Judge(g.Luck, label, out int number);
            if (verdict == LuckVerdict.Lucky)
            {
                dayLucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.FloorLabel, number, verdict, floor));
                Score.AddLuck(rules.luckyFloorLuckBonus, LuckSource.FloorLabel);
                if (!g.FloorTipBonusApplied) { g.FloorTipBonusApplied = true; g.TipMultiplier *= rules.luckyFloorTipMultiplier; }
                SetMood(g, g.Mood == GuestMood.Happy || g.Mood == GuestMood.Delighted ? GuestMood.Delighted : GuestMood.Happy);
                Speak(g, GuestLineTriggers.LuckyFloor);
            }
            else if (verdict == LuckVerdict.Unlucky)
            {
                dayUnlucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.FloorLabel, number, verdict, floor));
                Score.AddLuck(-rules.unluckyFloorLuckPenalty, LuckSource.FloorLabel);
                SetMood(g, GuestMood.Scared);
                Speak(g, GuestLineTriggers.UnluckyFloor);
            }
            if (!Building.IsRelabelled(floor)) return;
            if (g.Def.HasTrait(GuestTraits.Honest))
            {
                // GDD 5 counterweight: honest guests hate stickers (once per floor per ride, like every floor reaction)
                dayUnlucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.Sticker, -1, LuckVerdict.Unlucky, floor));
                Score.AddLuck(-rules.honestGuestLuckPenalty, LuckSource.Sticker);
                SetMood(g, GuestMood.Angry);
            }
            // everyone comments on the first sticker they see (once per guest, after the luck line; no luck effect)
            if (!g.RelabelledSpoken)
            {
                g.RelabelledSpoken = true;
                Speak(g, GuestLineTriggers.Relabelled);
            }
        }

        void OnDoorsClosed()
        {
            var floor = Elevator.CurrentFloor;
            int occupancy = Elevator.Occupancy;
            bus.Publish(new DoorsClosed(floor, Building.GetLabel(floor), occupancy));
            scratch.Clear();
            scratch.AddRange(Elevator.RiderGuests);
            foreach (var g in scratch) JudgeOccupancy(g, occupancy, floor);
            if (CheckHaunted()) return;
            if (Elevator.HasTarget && Elevator.TargetFloor != Elevator.CurrentFloor) Depart();
            else Elevator.HasTarget = false;
        }

        /// <summary>GDD rule 2: riders inside when the doors close.</summary>
        void JudgeOccupancy(GuestRuntime g, int occupancy, FloorIndex floor)
        {
            if (!g.SeenOccupancies.Add(occupancy)) return;
            var verdict = g.Luck.Judge(occupancy);
            if (verdict == LuckVerdict.Lucky)
            {
                dayLucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.Occupancy, occupancy, verdict, floor));
                Score.AddLuck(rules.luckyOccupancyLuckBonus, LuckSource.Occupancy);
                if (!g.OccupancyTipBonusApplied) { g.OccupancyTipBonusApplied = true; g.TipMultiplier *= rules.luckyOccupancyTipMultiplier; }
                SetMood(g, g.Mood == GuestMood.Happy || g.Mood == GuestMood.Delighted ? GuestMood.Delighted : GuestMood.Happy);
                Speak(g, GuestLineTriggers.LuckyOccupancy);
            }
            else if (verdict == LuckVerdict.Unlucky)
            {
                dayUnlucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.Occupancy, occupancy, verdict, floor));
                Score.AddLuck(-rules.unluckyOccupancyLuckPenalty, LuckSource.Occupancy);
                SetMood(g, GuestMood.Scared);
                Speak(g, GuestLineTriggers.UnluckyOccupancy);
            }
        }

        // =========================================================================================== boarding / exiting

        /// <summary>0..1 progress of the guest(s) currently walking in/out (1 when nobody is walking).</summary>
        internal float TransferProgress01 => transfer.Count == 0 || rules.boardSeconds <= 0f ? 1f : 1f - Math.Max(0f, transferTimer) / rules.boardSeconds;

        /// <summary>True while someone is walking in or out, or riders still have to get off here.</summary>
        public bool IsTransferring => transfer.Count > 0;

        void StepTransfers(float dt)
        {
            float budget = dt;
            for (int guard = 0; guard < 256; guard++)
            {
                if (transfer.Count > 0)
                {
                    transferTimer -= budget;
                    budget = 0f;
                    if (transferTimer > 0f) return;
                    CompleteTransfer();
                    if (phase != DayPhase.Running) return;
                    continue;
                }
                if (StartNextExit()) continue;
                if (!closeRequested && StartNextBoard()) continue;
                if (closeRequested)
                {
                    closeRequested = false;
                    Elevator.Doors = DoorState.Closing;
                    doorTimer = rules.doorSeconds;
                }
                return;
            }
        }

        bool StartNextExit()
        {
            var floor = Elevator.CurrentFloor;
            foreach (var g in Elevator.RiderGuests)
            {
                if (g.State != GuestState.Riding || g.DestinationFloor != floor) continue;
                transfer.Clear();
                foreach (var m in Elevator.RiderGuests)
                    if (m.State == GuestState.Riding && m.DestinationFloor == floor && (m == g || (!g.Party.IsNone && m.Party == g.Party)))
                        transfer.Add(m);
                foreach (var m in transfer) { m.State = GuestState.Exiting; m.TransferToRiderIndex = Elevator.IndexOfRider(m); }
                transferIsExit = true;
                transferTimer = rules.boardSeconds;
                return true;
            }
            return false;
        }

        bool StartNextBoard()
        {
            var floor = Elevator.CurrentFloor;
            var line = Guests.Line(floor);
            for (int i = 0; i < line.Count; i++)
            {
                var g = line[i];
                if (g.State != GuestState.Waiting || g.DestinationFloor == floor) continue;
                int size = 0;
                foreach (var m in line) if (m == g || (!g.Party.IsNone && m.Party == g.Party)) size++;
                int after = Elevator.Occupancy + size;
                if (after > Elevator.Capacity) continue;
                if (g.Def.HasTrait(GuestTraits.Gambler) && !g.Luck.IsLucky(after)) continue;

                transfer.Clear();
                foreach (var m in line) if (m == g || (!g.Party.IsNone && m.Party == g.Party)) transfer.Add(m);
                int riderIndex = Elevator.Occupancy;
                foreach (var m in transfer)
                {
                    m.TransferFromSlot = line.IndexOf(m);
                    m.TransferToRiderIndex = riderIndex++;
                }
                foreach (var m in transfer) { Guests.RemoveFromLine(m); m.State = GuestState.Boarding; }
                transferIsExit = false;
                transferTimer = rules.boardSeconds;
                return true;
            }
            return false;
        }

        void CompleteTransfer()
        {
            var floor = Elevator.CurrentFloor;
            scratch.Clear();
            scratch.AddRange(transfer);
            transfer.Clear();
            if (transferIsExit)
            {
                bool fourthFloorEnding = false;
                foreach (var g in scratch)
                {
                    Elevator.RemoveRider(g);
                    g.State = GuestState.Delivered;
                    g.Floor = floor;
                    bus.Publish(new GuestExited(g.Id, floor, true, Elevator.Occupancy));
                    int tip = (int)Math.Round(g.Def.baseTip * g.TipMultiplier, MidpointRounding.AwayFromZero);
                    bus.Publish(new GuestDelivered(g.Id, floor, tip));
                    dayDelivered++;
                    Score.AddLuck(rules.deliveryLuckBonus, LuckSource.Delivery);
                    Score.AddTips(tip, earned: true);
                    // 4th-floor ending: the mystery guest gets off at the real 4th floor while a sticker hides it
                    bool fourth = g.Def.HasTrait(GuestTraits.Mystery) && floor == StoryRules.RealFourthFloor && Building.IsRelabelled(floor);
                    Speak(g, fourth ? GuestLineTriggers.Special : GuestLineTriggers.Delivered);
                    Guests.Remove(g);
                    if (fourth) fourthFloorEnding = true;
                }
                if (fourthFloorEnding) EndDay(EndingKind.FourthFloor);
            }
            else
            {
                foreach (var g in scratch)
                {
                    g.State = GuestState.Riding;
                    g.Floor = floor;
                    Elevator.AddRider(g);
                    bus.Publish(new GuestBoarded(g.Id, floor, Elevator.Occupancy));
                    Speak(g, GuestLineTriggers.Board);
                }
            }
        }

        // =========================================================================================== day end / endings

        bool CheckHaunted()
        {
            if (phase != DayPhase.Running || Score.Luck > 0) return false;
            EndDay(EndingKind.Haunted);
            return true;
        }

        /// <summary>Clock ran out (or an ending cut the day short): everyone still around leaves, DayEnded, then Summary or Ending.</summary>
        void EndDay(EndingKind endingNow)
        {
            if (phase != DayPhase.Running) return;
            DropAllGuests();
            lastSummary = new DaySummary(dayDelivered, dayLeft, Score.TipsEarnedToday, luckAtStart, Score.Luck, dayLucky, dayUnlucky);
            bus.Publish(new DayEnded(day, lastSummary));
            if (endingNow != EndingKind.None) ReachEnding(endingNow);
            else SetPhase(DayPhase.Summary);
        }

        void ReachEnding(EndingKind kind)
        {
            ending = kind;
            CloseShop();
            SetPhase(DayPhase.Ending);
            bus.Publish(new EndingReached(kind, day, Score.Luck, Score.Tips));
        }

        /// <summary>Ends the running day now (debug key / tests).</summary>
        internal void EndDayNow() => EndDay(EndingKind.None);

        // =========================================================================================== shop

        public bool IsOpen => phase == DayPhase.Shop;
        /// <summary>Snapshot: replaced (not mutated) on purchase, so buying while iterating it is safe.</summary>
        public IReadOnlyList<ShopOffer> Offers => offersView;
        ShopOffer[] offersView = Array.Empty<ShopOffer>();

        bool OpenShop()
        {
            CloseShop();
            if (dayDef == null) return false;
            foreach (var idText in dayDef.shopStickers)
            {
                if (!data.TryGetSticker(new StickerId(idText), out var s)) continue;
                bool dup = false;
                foreach (var o in offers) if (o.Id == s.Id) dup = true;
                if (dup) continue;
                offers.Add(new ShopOffer(s, s.price, s.stock));
                offerRemaining.Add(s.stock);
            }
            offersView = offers.ToArray();
            return offers.Count > 0;
        }

        void CloseShop()
        {
            offers.Clear();
            offersView = Array.Empty<ShopOffer>();
            offerRemaining.Clear();
        }

        int IndexOfOffer(StickerId id)
        {
            for (int i = 0; i < offers.Count; i++) if (offers[i].Id == id) return i;
            return -1;
        }

        public StickerApplyResult CanBuyAndApply(StickerId sticker, FloorIndex floor) => Evaluate(sticker, floor, out _, out _);

        StickerApplyResult Evaluate(StickerId sticker, FloorIndex floor, out StickerDef def, out int offerIndex)
        {
            def = null;
            offerIndex = -1;
            if (phase != DayPhase.Shop) return StickerApplyResult.ShopClosed;
            if (!data.TryGetSticker(sticker, out def)) return StickerApplyResult.UnknownSticker;
            offerIndex = IndexOfOffer(sticker);
            if (offerIndex < 0) return StickerApplyResult.NotInShop;
            if (offerRemaining[offerIndex] == 0) return StickerApplyResult.OutOfStock;
            if (!Building.IsValid(floor) || !def.CanApplyTo(Building.GetLabel(floor))) return StickerApplyResult.NotApplicable;
            if (Score.Tips < offers[offerIndex].Price) return StickerApplyResult.NotEnoughTips;
            return StickerApplyResult.Applied;
        }

        public StickerApplyResult TryBuyAndApply(StickerId sticker, FloorIndex floor)
        {
            var result = Evaluate(sticker, floor, out var def, out int idx);
            if (result != StickerApplyResult.Applied) return result;
            int price = offers[idx].Price;
            if (offerRemaining[idx] > 0)
            {
                offerRemaining[idx]--;
                offers[idx] = new ShopOffer(def, price, offerRemaining[idx]);
                offersView = offers.ToArray();
            }
            var prev = Building.GetLabel(floor);
            var next = def.ResultLabel;
            bus.Publish(new StickerApplied(def.Id, floor, prev, next, price));
            Building.SetLabel(floor, next);
            Score.AddTips(-price, earned: false);
            return StickerApplyResult.Applied;
        }
    }
}
