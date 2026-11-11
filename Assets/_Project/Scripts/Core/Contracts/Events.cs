namespace FloorF.Core
{
    // =====================================================================================================
    // Game events. Published on GameServices.Events (IEventBus) by the module that OWNS the fact
    // (almost always Gameplay); everyone else only subscribes. All are small readonly structs: no allocation.
    //
    // Ordering guarantees Gameplay must keep (presentation modules rely on them):
    //   ElevatorArrived -> DoorsOpened -> (LuckJudged for riders, FloorLabel source)* -> GuestExited* -> GuestBoarded*
    //   per rider stepping out at their destination:
    //     GuestExited -> (LuckJudged RideDistance, if not Neutral) -> GuestDelivered (tip) -> TipsChanged
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
        /// <summary>Longest lucky streak of the day.</summary>
        public readonly int BestStreak;
        public DaySummary(int delivered, int left, int tipsEarned, int luckAtStart, int luckAtEnd, int luckyJudgements, int unluckyJudgements)
            : this(delivered, left, tipsEarned, luckAtStart, luckAtEnd, luckyJudgements, unluckyJudgements, 0) { }
        public DaySummary(int delivered, int left, int tipsEarned, int luckAtStart, int luckAtEnd, int luckyJudgements, int unluckyJudgements, int bestStreak)
        {
            Delivered = delivered; Left = left; TipsEarned = tipsEarned; LuckAtStart = luckAtStart; LuckAtEnd = luckAtEnd;
            LuckyJudgements = luckyJudgements; UnluckyJudgements = unluckyJudgements; BestStreak = bestStreak;
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

    /// <summary>
    /// Guest reached their destination and paid. Comes after GuestExited(AtDestination=true) and that guest's
    /// LuckJudged(RideDistance) (+ its LuckChanged / streak / speech), if any. Not published if that verdict hit Haunted.
    /// </summary>
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

    /// <summary>Shop offers were redrawn for a price.</summary>
    public readonly struct ShopRerolled
    {
        public readonly int Cost;
        public readonly int RerollsLeft;
        public readonly int NextCost;
        public ShopRerolled(int cost, int rerollsLeft, int nextCost) { Cost = cost; RerollsLeft = rerollsLeft; NextCost = nextCost; }
    }

    /// <summary>A charm was bought (active the next day). Followed by TipsChanged.</summary>
    public readonly struct CharmBought
    {
        public readonly StickerId Charm;
        public readonly int Price;
        public CharmBought(StickerId charm, int price) { Charm = charm; Price = price; }
    }

    /// <summary>
    /// The one-day purchases ran out at the end of the day they were active: labels went back to their originals
    /// (each also publishes FloorLabelChanged) and charms ended. Published after DayEnded, before Summary/Ending.
    /// </summary>
    public readonly struct StickersExpired
    {
        public readonly int Labels;
        public readonly int Charms;
        public StickersExpired(int labels, int charms) { Labels = labels; Charms = charms; }
    }

    /// <summary>A charm's effect actually helped (e.g. the shield ate an Unlucky verdict) — for feedback.</summary>
    public readonly struct CharmTriggered
    {
        public readonly StickerId Charm;
        public readonly GuestId Guest;
        public CharmTriggered(StickerId charm, GuestId guest) { Charm = charm; Guest = guest; }
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

    /// <summary>
    /// Lucky streak changed: consecutive Lucky judgements today. Each Lucky verdict +1, an Unlucky verdict resets to 0
    /// (published with Streak 0 only if it was above 0), Neutral keeps it; DayStarted starts at 0 silently.
    /// Published right after the LuckJudged that caused it. <see cref="TipMultiplier"/> = what the next tips get.
    /// </summary>
    public readonly struct LuckStreakChanged
    {
        public readonly int Streak;
        public readonly int Previous;
        public readonly float TipMultiplier;
        public LuckStreakChanged(int streak, int previous, float tipMultiplier) { Streak = streak; Previous = previous; TipMultiplier = tipMultiplier; }
        public bool Broken => Streak == 0 && Previous > 0;
    }

    /// <summary>
    /// Star night: a wish star starts crossing the sky. Positions are world-camera VIEWPORT coordinates (0..1, origin
    /// bottom-left; keep to the upper sky, y >= 0.6) so Rendering draws the streak and UI places the click target in the
    /// same spot (PresentationContract.ViewportToScreen). It travels From -> To linearly in <see cref="Seconds"/>
    /// (unscaled by pause: it freezes while paused, like the day clock). Ends with WishMade or WishStarFaded.
    /// </summary>
    public readonly struct WishStarAppeared
    {
        public readonly int Id;
        public readonly float FromX, FromY, ToX, ToY;
        public readonly float Seconds;
        public WishStarAppeared(int id, float fromX, float fromY, float toX, float toY, float seconds)
        { Id = id; FromX = fromX; FromY = fromY; ToX = toX; ToY = toY; Seconds = seconds; }
    }

    /// <summary>The player wished on a star in time (IGameFlow.MakeWish). Followed by LuckChanged(Story) if Luck changed.</summary>
    public readonly struct WishMade
    {
        public readonly int Id;
        public readonly int LuckGain;
        public WishMade(int id, int luckGain) { Id = id; LuckGain = luckGain; }
    }

    /// <summary>A wish star finished crossing without a wish (or the day ended under it).</summary>
    public readonly struct WishStarFaded
    {
        public readonly int Id;
        public WishStarFaded(int id) { Id = id; }
    }

    /// <summary>The run went on into the winter season (ISeasonFlow.ExtendSeason). Next: DayPhaseChanged (Ending -> Shop or Intro).</summary>
    public readonly struct SeasonExtended
    {
        /// <summary>First winter-season day.</summary>
        public readonly DayId FirstDay;
        /// <summary>How many winter days follow (GameData.ExtendedDayCount).</summary>
        public readonly int ExtraDays;
        public SeasonExtended(DayId firstDay, int extraDays) { FirstDay = firstDay; ExtraDays = extraDays; }
    }

    /// <summary>
    /// Lottery night: a winning number was drawn (Running; the first draw right at the day start). Drawn among the numbers
    /// currently shown on the floor buttons, so stickers can help. <see cref="DrawIndex"/> 0-based, of <see cref="DrawCount"/>.
    /// </summary>
    public readonly struct LotteryDrawn
    {
        public readonly int Number;
        public readonly int DrawIndex;
        public readonly int DrawCount;
        public LotteryDrawn(int number, int drawIndex, int drawCount) { Number = number; DrawIndex = drawIndex; DrawCount = drawCount; }
    }

    /// <summary>
    /// Lottery jackpot: a guest (each party member) stepped out at their destination on a floor showing the winning number.
    /// Published right before that guest's GuestDelivered (whose tip already includes x rules.lotteryTipMultiplier),
    /// followed by LuckChanged(Lottery) for rules.lotteryLuckBonus.
    /// </summary>
    public readonly struct LotteryWon
    {
        public readonly GuestId Guest;
        public readonly FloorIndex Floor;
        public readonly int Number;
        public LotteryWon(GuestId guest, FloorIndex floor, int number) { Guest = guest; Floor = floor; Number = number; }
    }

    /// <summary>IRunHistory changed (owner saves it).</summary>
    public readonly struct RunHistoryChanged
    {
        public readonly int FinishedRuns;
        public readonly int MysteryMisses;
        public RunHistoryChanged(int finishedRuns, int mysteryMisses) { FinishedRuns = finishedRuns; MysteryMisses = mysteryMisses; }
    }

    /// <summary>Day 0 coach-mark hold toggled (see <see cref="IGameFlow.SetTutorialHold"/>).</summary>
    public readonly struct TutorialHoldChanged
    {
        public readonly bool Held;
        public TutorialHoldChanged(bool held) { Held = held; }
    }
}

namespace FloorF.Core
{
    public enum UiSoundKind
    {
        /// <summary>Any button press (panel floor, open/close, menu).</summary>
        Click = 0,
        /// <summary>Positive confirmation (START, NEXT DAY, sticker picked).</summary>
        Confirm = 1,
        /// <summary>Rejected action (shop purchase refused, command rejected).</summary>
        Error = 2,
        /// <summary>Any pointer click/tap anywhere (the golden click effect) - soft, frequent; never stacks loudly.</summary>
        Tap = 3,
        /// <summary>Title: "press any key" -> the menu appears.</summary>
        Reveal = 4,
    }

    /// <summary>
    /// Published by UI: the scenic view (all UI hidden, camera zoomed out over the whole city) turned on/off.
    /// <see cref="Establishing"/> = the automatic establishing shot at a day's Intro (short, then zooms back in);
    /// otherwise the player's scenic mode (V key / HUD button; UI pauses the game via IMenuFlow while it is on).
    /// Rendering eases the camera between the gameplay frame and the wide (title-like) frame.
    /// </summary>
    public readonly struct ScenicViewChanged
    {
        public readonly bool On;
        public readonly bool Establishing;
        public ScenicViewChanged(bool on, bool establishing) { On = on; Establishing = establishing; }
    }

    /// <summary>
    /// Published by Gameplay's debug cheats (hand-testing aid, off until typed LUCKY / Ctrl+Shift+D) whenever their
    /// on-screen readout changes; UI draws it on top of everything (IMGUI does not show in the WebGL build).
    /// <see cref="Tag"/>: small corner tag ("DEBUG  luck +10 = 60"), null = hidden. <see cref="Body"/>: the F1 readout
    /// (state + key list, plain text with '\n'), null = hidden.
    /// </summary>
    public readonly struct DebugReadoutChanged
    {
        public readonly string Tag;
        public readonly string Body;
        public DebugReadoutChanged(string tag, string body) { Tag = tag; Body = body; }
    }

    /// <summary>Published by UI for interface feedback sounds (Audio subscribes). UI-only; Gameplay never publishes it.</summary>
    public readonly struct UiSound
    {
        public readonly UiSoundKind Kind;
        public UiSound(UiSoundKind kind) { Kind = kind; }
    }
}
