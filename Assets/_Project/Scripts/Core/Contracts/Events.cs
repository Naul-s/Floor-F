namespace FloorF.Core
{
    // =====================================================================================================
    // Game events. Published on GameServices.Events (IEventBus) by the module that OWNS the fact
    // (almost always Gameplay); everyone else only subscribes. All are small readonly structs: no allocation.
    //
    // Ordering guarantees Gameplay must keep (presentation modules rely on them):
    //   ElevatorArrived -> DoorsOpened -> (LuckJudged for riders, FloorLabel source)* -> GuestExited* -> GuestBoarded*
    //   DoorsClosed -> (LuckJudged for riders, Occupancy source)* -> ElevatorDeparted
    //   Every LuckJudged that changes Luck is followed by LuckChanged; tips by TipsChanged.
    //   DayStarted comes after DayPhaseChanged(Intro->Running); DayEnded before DayPhaseChanged(->Summary).
    // =====================================================================================================

    /// <summary>New game (first boot or restart). Followed by Day 1's DayPhaseChanged/DayStarted.</summary>
    public readonly struct GameStarted
    {
        public readonly int DayCount;
        public readonly int StartLuck;
        public readonly int MaxLuck;
        public GameStarted(int dayCount, int startLuck, int maxLuck) { DayCount = dayCount; StartLuck = startLuck; MaxLuck = maxLuck; }
    }

    public readonly struct DayPhaseChanged
    {
        public readonly DayId Day;
        public readonly DayPhase Previous;
        public readonly DayPhase Current;
        public DayPhaseChanged(DayId day, DayPhase previous, DayPhase current) { Day = day; Previous = previous; Current = current; }
    }

    /// <summary>The clock starts running for <see cref="Day"/>.</summary>
    public readonly struct DayStarted
    {
        public readonly DayId Day;
        public readonly int DayCount;
        public readonly float DurationSeconds;
        public readonly bool IsTutorial;
        public DayStarted(DayId day, int dayCount, float durationSeconds, bool isTutorial)
        { Day = day; DayCount = dayCount; DurationSeconds = durationSeconds; IsTutorial = isTutorial; }
    }

    public readonly struct DaySummary
    {
        public readonly int Delivered;
        public readonly int Left;
        public readonly int TipsEarned;
        public readonly int LuckAtStart;
        public readonly int LuckAtEnd;
        public readonly int LuckyJudgements;
        public readonly int UnluckyJudgements;
        public DaySummary(int delivered, int left, int tipsEarned, int luckAtStart, int luckAtEnd, int luckyJudgements, int unluckyJudgements)
        {
            Delivered = delivered; Left = left; TipsEarned = tipsEarned; LuckAtStart = luckAtStart; LuckAtEnd = luckAtEnd;
            LuckyJudgements = luckyJudgements; UnluckyJudgements = unluckyJudgements;
        }
        public int LuckDelta => LuckAtEnd - LuckAtStart;
    }

    /// <summary>The clock ran out (or the day was ended early). Summary phase follows unless an ending is reached.</summary>
    public readonly struct DayEnded
    {
        public readonly DayId Day;
        public readonly DaySummary Summary;
        public DayEnded(DayId day, DaySummary summary) { Day = day; Summary = summary; }
    }

    // ---------------------------------------------------------------- guests

    public readonly struct GuestSpawned
    {
        public readonly GuestId Guest;
        public readonly GuestArchetypeId Archetype;
        public readonly PartyId Party;
        public readonly FloorIndex Floor;
        public readonly DestinationId Destination;
        public readonly FloorIndex DestinationFloor;
        /// <summary>Slot index in the floor's waiting line (0 = nearest the shaft). See IWorldLayout.GetWaitingSlot.</summary>
        public readonly int WaitingSlot;
        public GuestSpawned(GuestId guest, GuestArchetypeId archetype, PartyId party, FloorIndex floor, DestinationId destination, FloorIndex destinationFloor, int waitingSlot)
        {
            Guest = guest; Archetype = archetype; Party = party; Floor = floor; Destination = destination; DestinationFloor = destinationFloor; WaitingSlot = waitingSlot;
        }
    }

    public readonly struct GuestBoarded
    {
        public readonly GuestId Guest;
        public readonly FloorIndex Floor;
        /// <summary>Riders after this guest got in.</summary>
        public readonly int Occupancy;
        public GuestBoarded(GuestId guest, FloorIndex floor, int occupancy) { Guest = guest; Floor = floor; Occupancy = occupancy; }
    }

    public readonly struct GuestExited
    {
        public readonly GuestId Guest;
        public readonly FloorIndex Floor;
        public readonly bool AtDestination;
        /// <summary>Riders after this guest got out.</summary>
        public readonly int Occupancy;
        public GuestExited(GuestId guest, FloorIndex floor, bool atDestination, int occupancy) { Guest = guest; Floor = floor; AtDestination = atDestination; Occupancy = occupancy; }
    }

    /// <summary>Guest reached their destination and paid. Comes right after GuestExited(AtDestination=true).</summary>
    public readonly struct GuestDelivered
    {
        public readonly GuestId Guest;
        public readonly FloorIndex Floor;
        public readonly int Tip;
        public GuestDelivered(GuestId guest, FloorIndex floor, int tip) { Guest = guest; Floor = floor; Tip = tip; }
    }

    /// <summary>Guest gave up (stairs) or refused. Terminal for that guest.</summary>
    public readonly struct GuestLeft
    {
        public readonly GuestId Guest;
        public readonly FloorIndex Floor;
        public readonly GuestLeaveReason Reason;
        public GuestLeft(GuestId guest, FloorIndex floor, GuestLeaveReason reason) { Guest = guest; Floor = floor; Reason = reason; }
    }

    public readonly struct GuestMoodChanged
    {
        public readonly GuestId Guest;
        public readonly GuestMood Previous;
        public readonly GuestMood Current;
        public GuestMoodChanged(GuestId guest, GuestMood previous, GuestMood current) { Guest = guest; Previous = previous; Current = current; }
    }

    /// <summary>A guest says a line (speech bubble). <see cref="LineTrigger"/> is one of GuestLineTriggers; Text is already localized.</summary>
    public readonly struct GuestSpoke
    {
        public readonly GuestId Guest;
        public readonly string LineTrigger;
        public readonly string Text;
        public GuestSpoke(GuestId guest, string lineTrigger, string text) { Guest = guest; LineTrigger = lineTrigger; Text = text; }
    }

    // ---------------------------------------------------------------- elevator

    public readonly struct ElevatorDeparted
    {
        public readonly FloorIndex From;
        public readonly FloorIndex To;
        public ElevatorDeparted(FloorIndex from, FloorIndex to) { From = from; To = to; }
    }

    /// <summary>Car passed a floor without stopping (LED tick). No luck judgement happens on pass.</summary>
    public readonly struct ElevatorPassedFloor
    {
        public readonly FloorIndex Floor;
        public readonly FloorLabel Label;
        public ElevatorPassedFloor(FloorIndex floor, FloorLabel label) { Floor = floor; Label = label; }
    }

    /// <summary>Car stopped at a floor ("ding"). Doors are still closed.</summary>
    public readonly struct ElevatorArrived
    {
        public readonly FloorIndex Floor;
        public readonly FloorLabel Label;
        public ElevatorArrived(FloorIndex floor, FloorLabel label) { Floor = floor; Label = label; }
    }

    /// <summary>
    /// Doors finished opening. GDD rule 1: every rider (guests inside at this moment) is judged on <see cref="Label"/>.
    /// <see cref="Occupancy"/> = riders inside when the doors opened (before anyone exits/boards).
    /// </summary>
    public readonly struct DoorsOpened
    {
        public readonly FloorIndex Floor;
        public readonly FloorLabel Label;
        public readonly int Occupancy;
        public DoorsOpened(FloorIndex floor, FloorLabel label, int occupancy) { Floor = floor; Label = label; Occupancy = occupancy; }
    }

    /// <summary>
    /// Doors finished closing. GDD rule 2: every rider is judged on <see cref="Occupancy"/> (riders inside now).
    /// </summary>
    public readonly struct DoorsClosed
    {
        public readonly FloorIndex Floor;
        public readonly FloorLabel Label;
        public readonly int Occupancy;
        public DoorsClosed(FloorIndex floor, FloorLabel label, int occupancy) { Floor = floor; Label = label; Occupancy = occupancy; }
    }

    // ---------------------------------------------------------------- luck & money

    /// <summary>
    /// One guest reacted to one number. Presentation hooks: sparkle/scream, number popup, red light, music pitch dip.
    /// Neutral judgements are NOT published.
    /// </summary>
    public readonly struct LuckJudged
    {
        public readonly GuestId Guest;
        public readonly LuckSource Source;
        /// <summary>The number that triggered it (label number or occupancy). -1 if the source has no number.</summary>
        public readonly int Number;
        public readonly LuckVerdict Verdict;
        public readonly FloorIndex Floor;
        public LuckJudged(GuestId guest, LuckSource source, int number, LuckVerdict verdict, FloorIndex floor)
        { Guest = guest; Source = source; Number = number; Verdict = verdict; Floor = floor; }
    }

    /// <summary>Hotel Luck meter changed (GDD 4: health + score; 0 = haunted ending).</summary>
    public readonly struct LuckChanged
    {
        public readonly int Previous;
        public readonly int Current;
        public readonly int Max;
        public readonly LuckSource Source;
        public LuckChanged(int previous, int current, int max, LuckSource source) { Previous = previous; Current = current; Max = max; Source = source; }
        public int Delta => Current - Previous;
    }

    public readonly struct TipsChanged
    {
        public readonly int Previous;
        public readonly int Current;
        public TipsChanged(int previous, int current) { Previous = previous; Current = current; }
        public int Delta => Current - Previous;
    }

    // ---------------------------------------------------------------- building & shop

    /// <summary>A floor's displayed label changed (sticker, reset, story). Panel buttons, LED, floor signs update on this.</summary>
    public readonly struct FloorLabelChanged
    {
        public readonly FloorIndex Floor;
        public readonly FloorLabel Previous;
        public readonly FloorLabel Current;
        public FloorLabelChanged(FloorIndex floor, FloorLabel previous, FloorLabel current) { Floor = floor; Previous = previous; Current = current; }
    }

    /// <summary>A sticker was bought and stuck on a floor. Followed by FloorLabelChanged and TipsChanged.</summary>
    public readonly struct StickerApplied
    {
        public readonly StickerId Sticker;
        public readonly FloorIndex Floor;
        public readonly FloorLabel Previous;
        public readonly FloorLabel Current;
        public readonly int Price;
        public StickerApplied(StickerId sticker, FloorIndex floor, FloorLabel previous, FloorLabel current, int price)
        { Sticker = sticker; Floor = floor; Previous = previous; Current = current; Price = price; }
    }

    /// <summary>The game is over. Phase becomes Ending.</summary>
    public readonly struct EndingReached
    {
        public readonly EndingKind Ending;
        public readonly DayId Day;
        public readonly int FinalLuck;
        public readonly int TotalTips;
        public EndingReached(EndingKind ending, DayId day, int finalLuck, int totalTips) { Ending = ending; Day = day; FinalLuck = finalLuck; TotalTips = totalTips; }
    }
}

namespace FloorF.Core
{
    /// <summary>An IGameSettings value changed (language toggle, volume, hints). Read the new values from IGameSettings.</summary>
    public readonly struct SettingsChanged
    {
        public readonly bool UseKorean;
        public readonly float MasterVolume;
        public readonly bool ShowTutorialHints;
        public SettingsChanged(bool useKorean, float masterVolume, bool showTutorialHints)
        { UseKorean = useKorean; MasterVolume = masterVolume; ShowTutorialHints = showTutorialHints; }
    }
}

namespace FloorF.Core
{
    /// <summary>IMenuFlow pause state changed.</summary>
    public readonly struct PauseChanged
    {
        public readonly bool Paused;
        public PauseChanged(bool paused) { Paused = paused; }
    }
}
