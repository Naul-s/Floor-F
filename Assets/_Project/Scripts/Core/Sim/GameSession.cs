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
    /// (IGuestQuery), <see cref="Score"/> (IScoreQuery); this object is IElevatorControl, IDayQuery, IGameFlow, IShopService,
    /// IMenuFlow, ISeasonQuery, ISeasonFlow, IRunHistory (winter season, lottery night, rare mystery guest: GameSession.Season.cs).
    ///
    /// Door cycle: OpenDoors -> Opening (doorSeconds) -> Open: DoorsOpened, riders judged on the label, then riders for
    /// this floor exit (boardSeconds per guest/party), then waiting guests board while the doors stay open.
    /// CloseDoors (or RequestFloor) stops new boarding; once the current walker and all exits finish -> Closing
    /// (doorSeconds) -> DoorsClosed, riders judged on occupancy -> depart if a floor was requested.
    ///
    /// Tutorial days ("Day 0" training shift, <c>DayDef.tutorial</c>): no time-out (RemainingSeconds stays full), spawns
    /// on a wave clock that only runs while the hotel is empty, no patience loss, no Haunted ending. Luck/tips still move
    /// (the UI explains verdicts) and are reset to the run's start values when the player leaves its summary; no shop.
    /// Coach marks can freeze a tutorial day with <see cref="SetTutorialHold"/> (the next accepted elevator command resumes it).
    ///
    /// Lucky streak (every day, Day 0 included): each LuckJudged is followed by LuckStreakChanged when the streak moved;
    /// tips are multiplied by <see cref="ScoreKeeper.StreakTipMultiplier"/> at payment (guest steps out).
    ///
    /// Rule 3, ride distance (<see cref="LuckSource.RideDistance"/>): every rider accumulates the floors the car moves with
    /// them inside (detours and reversals included; <see cref="GuestInfo.FloorsTravelled"/>). When a rider steps out at
    /// the destination the distance is judged once against the same lucky/unlucky numbers as rules 1 and 2, BEFORE the tip:
    ///   GuestExited -> LuckJudged(RideDistance, distance, exit floor) -> LuckStreakChanged? -> LuckChanged? ->
    ///   GuestMoodChanged? -> GuestSpoke(lucky_distance | unlucky_distance)? -> GuestDelivered(tip) -> CharmTriggered(tips)? -> LuckChanged(Delivery)? ->
    ///   TipsChanged -> GuestSpoke(delivered | special)
    /// Lucky: tip x rules.luckyDistanceTipMultiplier (the streak step it adds counts at this payment), Luck +luckyDistanceLuckBonus.
    /// Unlucky: Luck -unluckyDistanceLuckPenalty; if that empties the meter on a real day the hotel is Haunted at once (the
    /// guest is not paid). A distance line replaces the delivered line (one bubble per exit; the 4th-floor "special" wins).
    /// Parties are judged per member (same distance). Neutral distances publish nothing.
    ///
    /// Lottery night jackpot (<see cref="IsLotteryNight"/>): a rider stepping out at the destination on a floor whose label
    /// is the current <see cref="LotteryNumber"/> - judged AFTER the ride distance (a Haunted distance pays nothing):
    ///   GuestExited -> LuckJudged(RideDistance)... -> LotteryWon -> GuestDelivered(tip) -> CharmTriggered(tips)? ->
    ///   LuckChanged(Delivery)? -> LuckChanged(Lottery)? -> TipsChanged -> GuestSpoke
    /// Tip = baseTip x the guest's lucky floor/occupancy/distance multipliers x streak x rules.lotteryTipMultiplier
    /// x tips charm, rounded once at the end. Luck +rules.lotteryLuckBonus. Parties: per member.
    ///
    /// Unlucky penalties (every Unlucky LuckJudged: floor label, occupancy, ride distance, honest sticker):
    ///   base x <see cref="StoryRules.DevoutPenaltyFactor"/> when a devout guest's number is <see cref="StoryRules.DevoutNumber"/>
    ///   ("666"), x rules.friday13PenaltyMultiplier on a Friday-the-13th day, rounded half away from zero.
    /// Shield charm (one day, see GameSession.Shop.cs for the shop, one-day purchases and charms): a shielded Unlucky
    /// verdict publishes CharmTriggered(shield, guest) IMMEDIATELY BEFORE its LuckJudged(Unlucky), then no LuckChanged:
    ///   CharmTriggered -> LuckJudged(Unlucky) -> LuckStreakChanged? -> GuestMoodChanged? -> GuestSpoke?
    /// Friday the 13th (<see cref="IsFriday13"/>): a ride distance of <see cref="StoryRules.FridayDistance"/> is Unlucky for
    /// every guest, even one whose own lucky number is 13 (no own-number line is said for that override). Patience
    /// penalties are not verdicts and are not multiplied.
    /// </summary>
    public sealed partial class GameSession : IElevatorControl, IDayQuery, IGameFlow, IShopService, IMenuFlow
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
        /// <summary>Tutorial days: spawn clock that only runs while the hotel is empty (waves). Real days spawn on <see cref="elapsed"/>.</summary>
        float waveClock;
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

        readonly List<GuestRuntime> scratch = new List<GuestRuntime>();

        // ---- speech throttle: "archetype|trigger" -> day time it was last said
        readonly Dictionary<string, float> lastLineAt = new Dictionary<string, float>();
        // ---- line variants: "archetype|trigger" -> lines said this run (picks the next variant; reset per run, not per day)
        readonly Dictionary<string, int> lineTurn = new Dictionary<string, int>();

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
        /// <summary>Days of the run: the base run (GameData.DayCount), or every day once the season was extended.</summary>
        public int DayCount => seasonExtended ? data.AllDayCount : data.DayCount;
        public int DisplayDay => data.DisplayDayNumber(day);
        /// <summary>"/7", or "/10" once the season was extended (GameData.SeasonPlayableDayCount).</summary>
        public int PlayableDayCount => seasonExtended ? data.SeasonPlayableDayCount : data.PlayableDayCount;
        /// <summary>Visible phase: <see cref="DayPhase.Title"/> while the title/demo runs (the demo's own day phases stay internal).</summary>
        public DayPhase Phase => onTitle ? DayPhase.Title : phase;
        public bool IsTutorial => dayDef != null && dayDef.tutorial;
        /// <summary>Friday the 13th event day: Unlucky penalties x rules.friday13PenaltyMultiplier, ride distance 13 Unlucky for all.</summary>
        public bool IsFriday13 => dayDef != null && dayDef.friday13;
        public float DurationSeconds => dayDef != null ? dayDef.durationSeconds : 0f;
        public float ElapsedSeconds => elapsed;
        /// <summary>Tutorial days have no clock: stays at <see cref="DurationSeconds"/>.</summary>
        public float RemainingSeconds => IsTutorial ? DurationSeconds : Math.Max(0f, DurationSeconds - elapsed);
        /// <summary>Tutorial wave clock (spawn times of a tutorial day are on it); 0 on real days.</summary>
        internal float WaveClockSeconds => waveClock;
        public EndingKind Ending => ending;
        public DaySummary LastSummary => lastSummary;
        public DayDef CurrentDayDef => dayDef;

        // =========================================================================================== IGameFlow

        /// <summary>First boot: GameStarted + DayPhaseChanged(None -> Intro). Same as <see cref="RestartGame"/>.</summary>
        public void StartNewGame() => RestartGame();

        public void RestartGame()
        {
            var shown = Phase;
            onTitle = false;
            Unpause();
            ResetRun();
            DecideMysteryTonight();
            bus.Publish(new GameStarted(data.DayCount, Score.Luck, Score.MaxLuck));
            SetPhase(DayPhase.Intro, shown);
        }

        /// <summary>Fresh run state (day 1, start Luck/tips, original labels, empty building) without flow events.</summary>
        void ResetRun()
        {
            ReleaseTutorialHold();
            if (started)
            {
                ResetWishStars();
                DropAllGuests();
                Building.ResetLabels();
                Score.ResetForNewGame();
            }
            started = true;
            lineTurn.Clear();
            ending = EndingKind.None;
            ResetSeason();
            lastSummary = default;
            day = DayId.First;
            CloseShop();
            ClearCharms();
            ResetElevator();
            dayDef = data.GetDay(day);
            elapsed = 0f;
            phase = DayPhase.Intro;
        }

        public bool BeginDay()
        {
            if (paused || onTitle) return false;
            return BeginDayCore();
        }

        bool BeginDayCore()
        {
            if (phase != DayPhase.Intro) return false;
            dayDef = data.GetDay(day);
            spawns = TonightsSpawns(dayDef); // without the mystery guest unless it comes tonight
            // stable sort by time (Array.Sort is unstable)
            var times = new float[spawns.Length];
            for (int i = 0; i < times.Length; i++) times[i] = spawns[i].time + i * 1e-6f;
            Array.Sort(times, spawns);
            nextSpawn = 0;
            elapsed = 0f;
            waveClock = 0f;
            emptyFor = 0f;
            lastLineAt.Clear();
            luckAtStart = Score.Luck;
            dayDelivered = dayLeft = dayLucky = dayUnlucky = 0;
            Score.BeginDay();
            ScheduleWishStars();
            ClearLottery();
            SetPhase(DayPhase.Running);
            PublishFlow(new DayStarted(day, DayCount, dayDef.durationSeconds, dayDef.tutorial));
            SettleMysteryNight(); // RunHistoryChanged when the pity counter moved
            StepLottery();        // lottery night: the first winning number right away
            SpawnDue();
            return true;
        }

        /// <summary>
        /// Tutorial Intro -> first real day's Intro (DayPhaseChanged Intro -> Intro). Luck/tips untouched (nothing was
        /// played yet). False anywhere else, or when no real day follows.
        /// </summary>
        public bool SkipTutorial()
        {
            if (paused || onTitle || phase != DayPhase.Intro || !IsTutorial) return false;
            if (!TryFirstRealDayFrom(day, out var real)) return false;
            day = real;
            dayDef = data.GetDay(day);
            elapsed = 0f;
            waveClock = 0f;
            ResetElevator();
            DecideMysteryTonight();
            SetPhase(DayPhase.Intro);
            return true;
        }

        /// <summary>First non-tutorial day at or after <paramref name="from"/>.</summary>
        bool TryFirstRealDayFrom(DayId from, out DayId real)
        {
            for (var d = from; ; d = d.Next)
            {
                if (!data.GetDay(d).tutorial) { real = d; return true; }
                if (data.IsLastDay(d)) { real = from; return false; }
            }
        }

        // ---- Day 0 coach marks: tutorial hold
        bool tutorialHold;

        /// <summary>
        /// Coach-mark hold: <see cref="Tick"/> advances nothing while held (motion, doors, transfers, spawns, wave clock,
        /// elapsed). Elevator commands stay accepted; the first ACCEPTED one releases the hold (TutorialHoldChanged(false))
        /// and then takes effect, so its events follow the release. A refused command keeps the hold.
        /// </summary>
        public bool TutorialHold => tutorialHold;

        public bool SetTutorialHold(bool held)
        {
            if (!held) { ReleaseTutorialHold(); return true; }
            if (tutorialHold) return true;
            if (onTitle || paused || phase != DayPhase.Running || !IsTutorial) return false;
            tutorialHold = true;
            bus.Publish(new TutorialHoldChanged(true));
            return true;
        }

        void ReleaseTutorialHold()
        {
            if (!tutorialHold) return;
            tutorialHold = false;
            bus.Publish(new TutorialHoldChanged(false));
        }

        public bool ContinueFromSummary()
        {
            if (phase != DayPhase.Summary || paused || onTitle) return false;
            if (IsTutorial && !data.IsLastDay(day))
            {
                // the training shift does not count: back to the run's start Luck/tips, no shop
                Score.ResetForNewGame();
                GoToNextDayIntro();
                return true;
            }
            if (data.IsLastDay(day))
            {
                ReachEnding(Score.Luck >= rules.prosperousEndingLuck ? EndingKind.Prosperous : EndingKind.Modest);
                return true;
            }
            if (TryReachWinterEnding()) return true; // last winter day: WinterMiracle / SilentNight
            if (OpenShop()) SetPhase(DayPhase.Shop);
            else GoToNextDayIntro();
            return true;
        }

        public bool FinishShopping()
        {
            if (phase != DayPhase.Shop || paused || onTitle) return false;
            CloseShop();
            GoToNextDayIntro();
            return true;
        }

        void GoToNextDayIntro()
        {
            day = day.Next;
            dayDef = data.GetDay(day);
            elapsed = 0f;
            waveClock = 0f;
            ResetElevator();
            DecideMysteryTonight();
            SetPhase(DayPhase.Intro);
        }

        void SetPhase(DayPhase next) => SetPhase(next, Phase);

        /// <summary>Sets the internal phase; publishes DayPhaseChanged(<paramref name="shownBefore"/> -> next) unless the title demo runs.</summary>
        void SetPhase(DayPhase next, DayPhase shownBefore)
        {
            if (next != DayPhase.Running) ReleaseTutorialHold();
            phase = next;
            if (!onTitle) bus.Publish(new DayPhaseChanged(day, shownBefore, next));
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
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || paused || tutorialHold) return;
            float remaining = deltaSeconds;
            while (remaining > 0f && phase == DayPhase.Running)
            {
                float step = Math.Min(remaining, MaxStepSeconds);
                remaining -= step;
                if (onTitle) StepDemoPilot();
                Step(step);
                if (onTitle && phase != DayPhase.Running) RestartDemo();
            }
        }

        void Step(float dt)
        {
            elapsed += dt;
            if (dayDef.tutorial && Guests.Active.Count == 0) waveClock += dt;
            StepLottery();
            StepWishStars(dt);
            SpawnDue();
            StepPatience(dt);
            if (phase != DayPhase.Running) return;
            StepMotion(dt);
            if (phase != DayPhase.Running) return;
            StepDoors(dt);
            if (phase != DayPhase.Running) return;
            if (!dayDef.tutorial && elapsed >= dayDef.durationSeconds) { EndDay(EndingKind.None); return; }
            StepEarlyEnd(dt);
        }

        /// <summary>True once the day had scheduled spawns, all of them appeared, and nobody is active.</summary>
        internal bool AllGuestsDone => spawns.Length > 0 && nextSpawn >= spawns.Length && Guests.Active.Count == 0;

        void StepEarlyEnd(float dt)
        {
            if (!AllGuestsDone || WishStarsRemaining) { emptyFor = 0f; return; }
            emptyFor += dt;
            if (emptyFor >= EarlyEndDelaySeconds - 1e-4f) EndDay(EndingKind.None);
        }

        // =========================================================================================== guests

        /// <summary>
        /// Spawns every due entry. Real days: due on the day clock. Tutorial days: due on the wave clock, which only
        /// runs while the hotel is empty - so each wave (entries with equal times spawn together) waits for the last one.
        /// </summary>
        void SpawnDue()
        {
            float clock = dayDef.tutorial ? waveClock : elapsed;
            while (phase == DayPhase.Running && nextSpawn < spawns.Length && spawns[nextSpawn].time <= clock)
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
            if (def.HasTrait(GuestTraits.Mystery)) OnMysteryArrives(); // the pity counter resets when it really comes
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
                    // patience charm: x (1 + amount) for guests appearing while it is active
                    Patience = def.patienceSeconds * PatienceFactor,
                    PatienceMax = def.patienceSeconds * PatienceFactor,
                    Luck = data.GetLuckProfile(def.Id),
                };
                g.JudgeLuck = JudgingProfile(g.Luck);
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
            if (dayDef.tutorial) return; // training shift: nobody runs out of patience
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

        /// <summary>
        /// Publishes the guest's line for <paramref name="trigger"/>; false when nothing was said (no line, not the speaker, throttled).
        /// Variants (the same trigger more than once in guests.json) take turns per archetype+trigger: each line actually
        /// said advances <see cref="lineTurn"/>, so a run cycles 0, 1, 2, 0 ... (deterministic, no RNG). One line = always it.
        /// </summary>
        bool Speak(GuestRuntime g, string trigger)
        {
            if (!g.IsSpeaker) return false;
            var key = g.Def.id + "|" + trigger;
            lineTurn.TryGetValue(key, out int turn);
            var line = g.Def.FindLine(trigger, turn);
            if (line == null) return false;
            var text = line.Get(UseKorean);
            if (string.IsNullOrEmpty(text)) return false;
            if (trigger != GuestLineTriggers.Special)
            {
                if (lastLineAt.TryGetValue(key, out var at) && elapsed - at < SameLineCooldownSeconds - 1e-4f) return false;
                lastLineAt[key] = elapsed;
            }
            lineTurn[key] = turn + 1;
            bus.Publish(new GuestSpoke(g.Id, trigger, text));
            return true;
        }

        // =========================================================================================== elevator control

        /// <summary>Player input: Running, not paused, not the title demo.</summary>
        public bool AcceptsInput => phase == DayPhase.Running && !paused && !onTitle;

        /// <summary>Simulation-side gate (the demo pilot drives through this).</summary>
        bool SimAcceptsInput => phase == DayPhase.Running && !paused;

        public bool RequestFloor(FloorIndex floor) => AcceptsInput && ReleaseHoldIf(CanRequestFloor(floor)) && RequestFloorCore(floor);
        public bool OpenDoors() => AcceptsInput && ReleaseHoldIf(CanOpenDoors()) && OpenDoorsCore();
        public bool CloseDoors() => AcceptsInput && ReleaseHoldIf(CanCloseDoors()) && CloseDoorsCore();

        /// <summary>Tutorial hold: an accepted command releases the hold BEFORE it applies. Returns <paramref name="accepted"/>.</summary>
        bool ReleaseHoldIf(bool accepted)
        {
            if (accepted) ReleaseTutorialHold();
            return accepted;
        }

        bool CanRequestFloor(FloorIndex floor)
        {
            if (!SimAcceptsInput || !Building.IsValid(floor)) return false;
            if (Elevator.Motion != ElevatorMotion.Idle) return Elevator.TargetFloor != floor;
            if (floor == Elevator.CurrentFloor) return Elevator.HasTarget; // cancels a pending departure
            return !(Elevator.HasTarget && Elevator.TargetFloor == floor);
        }

        bool CanOpenDoors()
        {
            if (!SimAcceptsInput || Elevator.Motion != ElevatorMotion.Idle) return false;
            if (Elevator.Doors == DoorState.Closed || Elevator.Doors == DoorState.Closing) return true;
            return closeRequested && !Elevator.HasTarget; // cancels a pending close
        }

        bool CanCloseDoors()
            => SimAcceptsInput && (Elevator.Doors == DoorState.Open || Elevator.Doors == DoorState.Opening) && !closeRequested;

        bool RequestFloorCore(FloorIndex floor)
        {
            if (!CanRequestFloor(floor)) return false;
            if (Elevator.Motion != ElevatorMotion.Idle)
            {
                Elevator.TargetFloor = floor;
                return true;
            }
            if (floor == Elevator.CurrentFloor)
            {
                Elevator.HasTarget = false; // cancel a pending departure
                return true;
            }
            Elevator.HasTarget = true;
            Elevator.TargetFloor = floor;
            if (Elevator.Doors == DoorState.Closed) Depart();
            else if (Elevator.Doors == DoorState.Open || Elevator.Doors == DoorState.Opening) closeRequested = true;
            return true;
        }

        bool OpenDoorsCore()
        {
            if (!CanOpenDoors()) return false;
            switch (Elevator.Doors)
            {
                case DoorState.Closed:
                    Elevator.Doors = DoorState.Opening;
                    doorTimer = DoorSeconds;
                    closeRequested = false;
                    return true;
                case DoorState.Closing:
                    Elevator.Doors = DoorState.Opening;
                    doorTimer = Math.Max(0f, DoorSeconds - doorTimer);
                    closeRequested = false;
                    return true;
                default:
                    closeRequested = false;
                    return true;
            }
        }

        bool CloseDoorsCore()
        {
            if (!CanCloseDoors()) return false;
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
            float move = dt / Math.Max(0.0001f, SecondsPerFloor);
            float dist = Math.Abs(target - pos);
            bool up = target > pos;
            Elevator.Motion = up ? ElevatorMotion.MovingUp : ElevatorMotion.MovingDown;
            bool arrive = move >= dist - 1e-5f;
            float next = arrive ? target : pos + (up ? move : -move);

            // rule 3: everyone inside rides these floors (doors are closed while moving: every rider is Riding)
            double moved = Math.Abs(next - pos);
            foreach (var g in Elevator.RiderGuests) g.Travelled += moved;

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
            // a lucky-for-all whole number (star night 7, Christmas Eve 12) beats a feared digit ("12" vs an unlucky 2)
            var verdict = LuckyForAllRules.JudgeLabel(g.JudgeLuck, label, out int number, IsStarNight, LuckyForAll);
            if (verdict == LuckVerdict.Lucky)
            {
                dayLucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.FloorLabel, number, verdict, floor));
                Score.OnVerdict(verdict);
                Score.AddLuck(rules.luckyFloorLuckBonus, LuckSource.FloorLabel);
                if (!g.FloorTipBonusApplied) { g.FloorTipBonusApplied = true; g.TipMultiplier *= rules.luckyFloorTipMultiplier; }
                SetMood(g, g.Mood == GuestMood.Happy || g.Mood == GuestMood.Delighted ? GuestMood.Delighted : GuestMood.Happy);
                if (!IsStarOverride(LabelNumbers.Judge(g.Luck, label, out _), verdict)) Speak(g, GuestLineTriggers.LuckyFloor);
            }
            else if (verdict == LuckVerdict.Unlucky)
            {
                dayUnlucky++;
                int penalty = UnluckyPenalty(rules.unluckyFloorLuckPenalty, g, number);
                bool shielded = TryShield(g, penalty); // CharmTriggered BEFORE the verdict it protects
                bus.Publish(new LuckJudged(g.Id, LuckSource.FloorLabel, number, verdict, floor));
                Score.OnVerdict(verdict);
                if (!shielded) Score.AddLuck(-penalty, LuckSource.FloorLabel);
                SetMood(g, GuestMood.Scared);
                Speak(g, GuestLineTriggers.UnluckyFloor);
            }
            if (!Building.IsRelabelled(floor)) return;
            if (g.Def.HasTrait(GuestTraits.Honest))
            {
                // GDD 5 counterweight: honest guests hate stickers (once per floor per ride, like every floor reaction)
                dayUnlucky++;
                int penalty = UnluckyPenalty(rules.honestGuestLuckPenalty, g, -1);
                bool shielded = TryShield(g, penalty); // CharmTriggered BEFORE the verdict it protects
                bus.Publish(new LuckJudged(g.Id, LuckSource.Sticker, -1, LuckVerdict.Unlucky, floor));
                Score.OnVerdict(LuckVerdict.Unlucky);
                if (!shielded) Score.AddLuck(-penalty, LuckSource.Sticker);
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
            var verdict = g.JudgeLuck.Judge(occupancy);
            if (verdict == LuckVerdict.Lucky)
            {
                dayLucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.Occupancy, occupancy, verdict, floor));
                Score.OnVerdict(verdict);
                Score.AddLuck(rules.luckyOccupancyLuckBonus, LuckSource.Occupancy);
                if (!g.OccupancyTipBonusApplied) { g.OccupancyTipBonusApplied = true; g.TipMultiplier *= rules.luckyOccupancyTipMultiplier; }
                SetMood(g, g.Mood == GuestMood.Happy || g.Mood == GuestMood.Delighted ? GuestMood.Delighted : GuestMood.Happy);
                if (!IsStarOverride(g.Luck.Judge(occupancy), verdict)) Speak(g, GuestLineTriggers.LuckyOccupancy);
            }
            else if (verdict == LuckVerdict.Unlucky)
            {
                dayUnlucky++;
                int penalty = UnluckyPenalty(rules.unluckyOccupancyLuckPenalty, g, occupancy);
                bool shielded = TryShield(g, penalty); // CharmTriggered BEFORE the verdict it protects
                bus.Publish(new LuckJudged(g.Id, LuckSource.Occupancy, occupancy, verdict, floor));
                Score.OnVerdict(verdict);
                if (!shielded) Score.AddLuck(-penalty, LuckSource.Occupancy);
                SetMood(g, GuestMood.Scared);
                Speak(g, GuestLineTriggers.UnluckyOccupancy);
            }
        }

        /// <summary>
        /// Luck lost for one Unlucky verdict: <paramref name="basePenalty"/> x2 for a devout guest's 6 ("666"),
        /// x rules.friday13PenaltyMultiplier on Friday the 13th (NaN/negative = 1), rounded half away from zero.
        /// </summary>
        internal int UnluckyPenalty(int basePenalty, GuestRuntime g, int number)
        {
            double p = basePenalty;
            if (number == StoryRules.DevoutNumber && g.Def.HasTrait(GuestTraits.Devout)) p *= StoryRules.DevoutPenaltyFactor;
            if (IsFriday13)
            {
                float m = rules.friday13PenaltyMultiplier;
                p *= m >= 0f ? m : 1f; // NaN fails the comparison -> 1
            }
            return (int)Math.Round(p, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Rule 3 verdict for a ride of <paramref name="distance"/> floors (star night: 7 is Lucky for all; Friday the 13th:
        /// 13 is Unlucky for all). <paramref name="globalOverride"/> = the verdict is not the guest's own superstition.
        /// </summary>
        LuckVerdict JudgeDistanceVerdict(GuestRuntime g, int distance, out bool globalOverride)
        {
            var own = g.Luck.Judge(distance);
            var judged = g.JudgeLuck.Judge(distance);
            bool friday = IsFriday13 && distance == StoryRules.FridayDistance && judged != LuckVerdict.Unlucky;
            if (friday) judged = LuckVerdict.Unlucky;
            globalOverride = judged != own;
            return judged;
        }

        /// <summary>
        /// Rule 3: judged once when the rider steps out at the destination, before payment. Returns true when a distance
        /// line was said (it replaces the delivered line).
        /// </summary>
        bool JudgeDistance(GuestRuntime g, FloorIndex floor)
        {
            int distance = g.FloorsTravelled;
            var verdict = JudgeDistanceVerdict(g, distance, out bool globalOverride);
            if (verdict == LuckVerdict.Lucky)
            {
                dayLucky++;
                bus.Publish(new LuckJudged(g.Id, LuckSource.RideDistance, distance, verdict, floor));
                Score.OnVerdict(verdict);
                Score.AddLuck(rules.luckyDistanceLuckBonus, LuckSource.RideDistance);
                g.TipMultiplier *= rules.luckyDistanceTipMultiplier;
                SetMood(g, g.Mood == GuestMood.Happy || g.Mood == GuestMood.Delighted ? GuestMood.Delighted : GuestMood.Happy);
                return !globalOverride && Speak(g, GuestLineTriggers.LuckyDistance);
            }
            if (verdict == LuckVerdict.Unlucky)
            {
                dayUnlucky++;
                int penalty = UnluckyPenalty(rules.unluckyDistanceLuckPenalty, g, distance);
                bool shielded = TryShield(g, penalty); // CharmTriggered BEFORE the verdict it protects
                bus.Publish(new LuckJudged(g.Id, LuckSource.RideDistance, distance, verdict, floor));
                Score.OnVerdict(verdict);
                if (!shielded) Score.AddLuck(-penalty, LuckSource.RideDistance);
                SetMood(g, GuestMood.Scared);
                // the Friday override is not the guest's own superstition: no own-number line (presentation shows Friday 13)
                return !globalOverride && Speak(g, GuestLineTriggers.UnluckyDistance);
            }
            return false;
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
                    doorTimer = DoorSeconds;
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
                if (g.Def.HasTrait(GuestTraits.Gambler) && !g.JudgeLuck.IsLucky(after)) continue;

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
                    // rule 3 before the payment: a lucky distance multiplies this tip, an unlucky one may haunt the hotel
                    bool distanceLine = JudgeDistance(g, floor);
                    if (CheckHaunted()) return; // EndDay dropped everyone (this guest included): no payment
                    // lottery night: stepped out on the floor showing the winning number
                    bool jackpot = IsLotteryWin(floor);
                    if (jackpot) bus.Publish(new LotteryWon(g.Id, floor, lotteryNumber));
                    // lucky streak bonus taken at payment time (see ScoreKeeper)
                    double raw = (double)g.Def.baseTip * g.TipMultiplier * Score.StreakTipMultiplier;
                    if (jackpot) raw *= LotteryTipFactor;
                    int plainTip = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
                    // tips charm (one day): x (1 + amount), feedback when it added something
                    int tip = (int)Math.Round(raw * TipFactor, MidpointRounding.AwayFromZero);
                    bus.Publish(new GuestDelivered(g.Id, floor, tip));
                    if (tip > plainTip && TipsCharm() is StickerDef tipsCharm) bus.Publish(new CharmTriggered(tipsCharm.Id, g.Id));
                    dayDelivered++;
                    Score.AddLuck(rules.deliveryLuckBonus, LuckSource.Delivery);
                    if (jackpot) Score.AddLuck(Math.Max(0, rules.lotteryLuckBonus), LuckSource.Lottery);
                    Score.AddTips(tip, earned: true);
                    // 4th-floor ending: the mystery guest gets off at the real 4th floor while a sticker hides it
                    bool fourth = g.Def.HasTrait(GuestTraits.Mystery) && floor == StoryRules.RealFourthFloor && Building.IsRelabelled(floor);
                    if (fourth) Speak(g, GuestLineTriggers.Special);
                    else if (!distanceLine) Speak(g, GuestLineTriggers.Delivered);
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
                    // picked up at last: an impatient guest stops stomping (patience does not run in the car)
                    if (g.Mood == GuestMood.Impatient) SetMood(g, GuestMood.Neutral);
                    Speak(g, GuestLineTriggers.Board);
                }
            }
        }

        // =========================================================================================== day end / endings

        bool CheckHaunted()
        {
            if (phase != DayPhase.Running || dayDef.tutorial || Score.Luck > 0) return false; // no Haunted on the training shift
            EndDay(EndingKind.Haunted);
            return true;
        }

        /// <summary>
        /// Clock ran out (or an ending cut the day short): everyone still around leaves, DayEnded, the one-day purchases
        /// expire (FloorLabelChanged..., StickersExpired), then Summary or Ending.
        /// </summary>
        void EndDay(EndingKind endingNow)
        {
            if (phase != DayPhase.Running) return;
            CloseOutDay();
            if (endingNow != EndingKind.None) ReachEnding(endingNow);
            else SetPhase(DayPhase.Summary);
        }

        /// <summary>The running day stops: everyone leaves, stars fade, DayEnded, one-day purchases expire (phase unchanged).</summary>
        void CloseOutDay()
        {
            ClearLottery(); // the winning number is only good for its night
            DropAllGuests();
            FadeAllWishStars();
            lastSummary = new DaySummary(dayDelivered, dayLeft, Score.TipsEarnedToday, luckAtStart, Score.Luck, dayLucky, dayUnlucky, Score.BestStreakToday);
            PublishFlow(new DayEnded(day, lastSummary));
            ExpirePurchases(); // one-day stickers/charms: FloorLabelChanged per floor, StickersExpired
        }

        void ReachEnding(EndingKind kind)
        {
            ending = kind;
            CloseShop();
            SetPhase(DayPhase.Ending);
            PublishFlow(new EndingReached(kind, day, Score.Luck, Score.Tips));
            CountFinishedRun(); // the run's first ending: RunHistoryChanged
        }

        /// <summary>Ends the running day now (debug key / tests).</summary>
        internal void EndDayNow() => EndDay(EndingKind.None);
    }
}
