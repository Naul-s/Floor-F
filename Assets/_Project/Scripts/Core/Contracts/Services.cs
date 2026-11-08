using System.Collections.Generic;

namespace FloorF.Core
{
    // =====================================================================================================
    // Service interfaces. Registered in GameServices by their OWNER, read by everyone else.
    //   Owner Gameplay : IBuildingQuery, IElevatorQuery, IElevatorControl, IGuestQuery, IScoreQuery, IDayQuery,
    //                    IGameFlow, IShopService, IActorLocator, IWorldLayout (HotelLayout), GameData, IGameSettings, IMenuFlow
    //   Rendering / UI / Audio : register nothing by default; if one needs to expose something, it goes here first
    //                    (CONTRACT REQUEST), never as a direct reference between presentation modules.
    // Queries are cheap and allocation-free - poll them every frame if you like. Commands return false/an enum on
    // rejection instead of throwing.
    // =====================================================================================================

    /// <summary>
    /// Player-facing settings (owner: Gameplay). Setting a value that changes publishes <see cref="SettingsChanged"/>.
    /// Gameplay localizes GuestSpoke text with <see cref="UseKorean"/>; UI uses it for its own strings and the *Ko data fields.
    /// </summary>
    public interface IGameSettings
    {
        /// <summary>false = English (default, international judges), true = Korean (*Ko fields; needs a Korean-capable font).</summary>
        bool UseKorean { get; set; }
        /// <summary>0..1, read by Audio.</summary>
        float MasterVolume { get; set; }
        /// <summary>Show day-1 style rule hints (UI).</summary>
        bool ShowTutorialHints { get; set; }
    }

    /// <summary>
    /// Title screen + pause (owner: Gameplay). Pausing freezes the simulation clock (not Unity timeScale - presentation
    /// keeps animating). Changes publish <see cref="PauseChanged"/> / <see cref="DayPhaseChanged"/>.
    /// </summary>
    public interface IMenuFlow
    {
        /// <summary>True while DayPhase is Title.</summary>
        bool IsOnTitle { get; }
        /// <summary>Title -> new game, Day 1 Intro. False if not on the title screen.</summary>
        bool StartGameFromTitle();
        /// <summary>Anywhere -> Title (abandons the current run).</summary>
        void ReturnToTitle();
        bool IsPaused { get; }
        /// <summary>Only meaningful in Running (and Intro/Summary/Shop are already waiting on the player). Returns false if rejected.</summary>
        bool SetPaused(bool paused);
    }

    /// <summary>The building: floors, their CURRENT labels and destinations.</summary>
    public interface IBuildingQuery
    {
        int FloorCount { get; }
        /// <summary>Current displayed label (after stickers).</summary>
        FloorLabel GetLabel(FloorIndex floor);
        /// <summary>Label from hotel.json before any sticker.</summary>
        FloorLabel GetOriginalLabel(FloorIndex floor);
        bool IsRelabelled(FloorIndex floor);
        DestinationId GetDestination(FloorIndex floor);
        bool TryFindFloor(DestinationId destination, out FloorIndex floor);
        /// <summary>First floor (lowest) currently showing <paramref name="label"/>; labels may repeat ("8" twice).</summary>
        bool TryFindFloorByLabel(FloorLabel label, out FloorIndex floor);
    }

    public interface IElevatorQuery
    {
        /// <summary>Floor the car is at, or the last floor it passed while moving.</summary>
        FloorIndex CurrentFloor { get; }
        bool HasTarget { get; }
        FloorIndex TargetFloor { get; }
        /// <summary>Continuous car height in FLOOR units (0 = ground, 2.5 = halfway between physical 3 and 4). For smooth visuals.</summary>
        float Position { get; }
        ElevatorMotion Motion { get; }
        DoorState Doors { get; }
        int Occupancy { get; }
        int Capacity { get; }
        bool IsFull { get; }
        /// <summary>Guests inside, in boarding order.</summary>
        IReadOnlyList<GuestId> Riders { get; }
    }

    /// <summary>Player commands (UI panel buttons, keyboard). False = rejected (e.g. doors open, already there).</summary>
    public interface IElevatorControl
    {
        bool AcceptsInput { get; }
        bool RequestFloor(FloorIndex floor);
        bool OpenDoors();
        bool CloseDoors();
    }

    /// <summary>Snapshot of one guest for bubbles, visuals and audio.</summary>
    public readonly struct GuestInfo
    {
        public readonly GuestId Id;
        public readonly GuestArchetypeId Archetype;
        public readonly PartyId Party;
        public readonly GuestState State;
        public readonly GuestMood Mood;
        /// <summary>Waiting floor, or the car's floor while riding.</summary>
        public readonly FloorIndex Floor;
        public readonly DestinationId Destination;
        public readonly FloorIndex DestinationFloor;
        /// <summary>1 = full patience, 0 = about to leave. Frozen while riding unless a rule says otherwise.</summary>
        public readonly float Patience01;
        public readonly LuckProfile Luck;
        /// <summary>Mystery guest: show "?" instead of the numbers.</summary>
        public readonly bool HideNumbers;
        /// <summary>Floors travelled since boarding (rule 3, <see cref="LuckSource.RideDistance"/>); 0 while waiting.</summary>
        public readonly int FloorsTravelled;

        public GuestInfo(GuestId id, GuestArchetypeId archetype, PartyId party, GuestState state, GuestMood mood, FloorIndex floor,
            DestinationId destination, FloorIndex destinationFloor, float patience01, LuckProfile luck, bool hideNumbers)
            : this(id, archetype, party, state, mood, floor, destination, destinationFloor, patience01, luck, hideNumbers, 0) { }

        public GuestInfo(GuestId id, GuestArchetypeId archetype, PartyId party, GuestState state, GuestMood mood, FloorIndex floor,
            DestinationId destination, FloorIndex destinationFloor, float patience01, LuckProfile luck, bool hideNumbers, int floorsTravelled)
        {
            Id = id; Archetype = archetype; Party = party; State = state; Mood = mood; Floor = floor;
            Destination = destination; DestinationFloor = destinationFloor; Patience01 = patience01;
            Luck = luck ?? LuckProfile.None; HideNumbers = hideNumbers; FloorsTravelled = floorsTravelled;
        }
    }

    public interface IGuestQuery
    {
        /// <summary>Guests that are Waiting/Boarding/Riding/Exiting (not Delivered/Left).</summary>
        IReadOnlyList<GuestId> ActiveGuests { get; }
        bool TryGet(GuestId guest, out GuestInfo info);
        /// <summary>Guests currently waiting on <paramref name="floor"/>, in line order.</summary>
        int GetWaiting(FloorIndex floor, List<GuestId> results);
    }

    public interface IScoreQuery
    {
        int Luck { get; }
        int MaxLuck { get; }
        /// <summary>Current lucky streak (see <see cref="LuckStreakChanged"/>).</summary>
        int LuckStreak { get; }
        int BestStreakToday { get; }
        /// <summary>Multiplier applied to tips paid now: 1 + streak x rules.streakTipBonusPerStep, capped.</summary>
        float StreakTipMultiplier { get; }
        /// <summary>Total tips the player holds (shop currency).</summary>
        int Tips { get; }
        int TipsEarnedToday { get; }
    }

    public interface IDayQuery
    {
        /// <summary>Internal 1-based day (DayId 1 = the "Day 0" tutorial shift when the data has one). Show <see cref="DisplayDay"/>.</summary>
        DayId CurrentDay { get; }
        /// <summary>All days in the data, tutorial days included. Show <see cref="PlayableDayCount"/>.</summary>
        int DayCount { get; }
        /// <summary>Day number for players: 0 on the tutorial shift, then 1..PlayableDayCount ("Day 2/5").</summary>
        int DisplayDay { get; }
        /// <summary>Days that count (tutorial excluded): the "/5".</summary>
        int PlayableDayCount { get; }
        DayPhase Phase { get; }
        /// <summary>
        /// Tutorial shift ("Day 0"): no clock (RemainingSeconds stays at DurationSeconds and the day never times out),
        /// no patience loss, no Haunted ending; guests come in waves (next wave once the hotel is empty); Luck and tips
        /// go back to the run's start values when the player leaves its summary; no shop after it.
        /// </summary>
        bool IsTutorial { get; }
        /// <summary>
        /// "Friday the 13th" event day (DayDef.friday13): every Unlucky Luck penalty x rules.friday13PenaltyMultiplier,
        /// and a ride distance of 13 is Unlucky for EVERY guest (on top of their own numbers; their own lucky 13 loses).
        /// </summary>
        bool IsFriday13 { get; }
        /// <summary>
        /// "Star night" (DayDef.starNight, Day 7): 7 is Lucky for EVERY guest from every source (floor, head count,
        /// distance; a guest's own unlucky 7 loses), and wish stars cross the sky (<see cref="WishStarAppeared"/>).
        /// Presentation: moon, Milky Way, meteor shower.
        /// </summary>
        bool IsStarNight { get; }
        /// <summary>DayDef.weather == "rain" (Day 5): rain + distant lightning, rain ambience. Presentation only.</summary>
        bool IsRainy { get; }
        float DurationSeconds { get; }
        float ElapsedSeconds { get; }
        float RemainingSeconds { get; }
        EndingKind Ending { get; }
        /// <summary>Summary of the last finished day (valid in Summary/Shop/Ending).</summary>
        DaySummary LastSummary { get; }
    }

    /// <summary>Flow buttons: "Start", "Continue", "Done shopping", "Play again".</summary>
    public interface IGameFlow
    {
        /// <summary>Intro -> Running.</summary>
        bool BeginDay();
        /// <summary>Summary -> Shop (or straight to the next day's Intro if the shop has nothing).</summary>
        bool ContinueFromSummary();
        /// <summary>Shop -> next day's Intro.</summary>
        bool FinishShopping();
        /// <summary>Anywhere -> new game, first day's Intro (the Day 0 tutorial when the data has one).</summary>
        void RestartGame();
        /// <summary>
        /// "Skip tutorial" on a tutorial day's Intro: straight to the first real day's Intro (DayPhaseChanged Intro -> Intro,
        /// Luck/tips untouched). False anywhere else.
        /// </summary>
        bool SkipTutorial();
        /// <summary>
        /// Day 0 coach marks. While held the simulation is frozen (Tick advances nothing: motion, doors, transfers,
        /// spawns, clock) but elevator commands are still ACCEPTED: the first accepted OpenDoors/CloseDoors/RequestFloor
        /// releases the hold and takes effect at once. Only on a tutorial day in Running, not paused, not on the title;
        /// leaving Running (or the menu pause / restart / title) releases it. Changes publish <see cref="TutorialHoldChanged"/>.
        /// </summary>
        bool TutorialHold { get; }
        /// <summary>Returns true if the hold is now as requested (false = refused, e.g. not a tutorial day).</summary>
        bool SetTutorialHold(bool held);
        /// <summary>
        /// Star night (Day 7): the player clicked a shooting star that is still crossing (<see cref="WishStarAppeared"/>).
        /// Publishes <see cref="WishMade"/> and adds rules.wishLuckBonus Luck. False if the id is unknown, already wished on,
        /// already gone, or the game is paused / not Running.
        /// </summary>
        bool MakeWish(int starId);
    }

    public readonly struct ShopOffer
    {
        public readonly StickerDef Sticker;
        public readonly StickerId Id;
        public readonly int Price;
        /// <summary>How many more can be bought today. -1 = unlimited.</summary>
        public readonly int Remaining;
        public ShopOffer(StickerDef sticker, int price, int remaining)
        {
            Sticker = sticker; Id = sticker != null ? sticker.Id : default; Price = price; Remaining = remaining;
        }
    }

    /// <summary>
    /// The shop between days. Every purchase lasts ONE day: labels stuck and charms bought after day N are active during
    /// day N+1 and expire when it ends (labels go back to their originals, <see cref="StickersExpired"/>), before the next
    /// shop opens. Offers: <c>rules.shopOfferCount</c> drawn at random (seeded, weighted) from the day's shopStickers pool;
    /// <see cref="Reroll"/> redraws them (cost grows each time, at most rules.maxRerolls per visit).
    /// </summary>
    public interface IShopService
    {
        bool IsOpen { get; }
        IReadOnlyList<ShopOffer> Offers { get; }
        /// <summary>Dry run of <see cref="TryBuyAndApply"/> (for greying out buttons / highlighting valid floors).</summary>
        StickerApplyResult CanBuyAndApply(StickerId sticker, FloorIndex floor);
        /// <summary>Label stickers only (charms: WrongKind).</summary>
        StickerApplyResult TryBuyAndApply(StickerId sticker, FloorIndex floor);
        /// <summary>Dry run of <see cref="TryBuy"/>.</summary>
        StickerApplyResult CanBuy(StickerId charm);
        /// <summary>Charms only (labels: WrongKind). Publishes CharmBought + TipsChanged.</summary>
        StickerApplyResult TryBuy(StickerId charm);
        int RerollsLeft { get; }
        /// <summary>Price of the next reroll.</summary>
        int RerollCost { get; }
        /// <summary>Shop open, rerolls left, enough tips. Publishes ShopRerolled + TipsChanged; Offers is replaced.</summary>
        bool Reroll();
        /// <summary>Charms bought for (shop) or active during (Running) the next/current day.</summary>
        IReadOnlyList<StickerDef> ActiveCharms { get; }
    }

    /// <summary>
    /// Engine-free 3D point (Core cannot use UnityEngine.Vector3). Unity units. Convert with
    /// FloorF.Gameplay.CoreUnityExtensions.ToVector3().
    /// </summary>
    public readonly struct WorldPoint
    {
        public readonly float X, Y, Z;
        public WorldPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly WorldPoint Zero = new WorldPoint(0, 0, 0);
        public WorldPoint Offset(float dx, float dy, float dz) => new WorldPoint(X + dx, Y + dy, Z + dz);
        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }

    /// <summary>
    /// Static geometry of the building in world units - the shared ruler for Gameplay (where guests stand),
    /// Rendering (where to build the greybox) and UI (where to put bubbles). Implemented by <see cref="HotelLayout"/>.
    /// </summary>
    public interface IWorldLayout
    {
        int FloorCount { get; }
        float FloorHeight { get; }
        float FloorWidth { get; }
        float Depth { get; }
        /// <summary>X of the shaft centre line.</summary>
        float ShaftX { get; }
        /// <summary>Floor slab top surface at the shaft door (where the car stops).</summary>
        WorldPoint GetFloorAnchor(FloorIndex floor);
        /// <summary>Car origin (floor of the car) for a continuous <see cref="IElevatorQuery.Position"/>.</summary>
        WorldPoint GetCarPosition(float floorPosition);
        /// <summary>Where the guest in waiting line slot <paramref name="slot"/> stands (0 = nearest the shaft).</summary>
        WorldPoint GetWaitingSlot(FloorIndex floor, int slot);
        /// <summary>Spot inside the car for rider <paramref name="riderIndex"/> (0..capacity-1), relative to the car origin.</summary>
        WorldPoint GetCarSlotOffset(int riderIndex);
        /// <summary>Where guests walk to when they leave by the stairs / reach a destination (off-screen side of the floor).</summary>
        WorldPoint GetExitPoint(FloorIndex floor);
    }

    /// <summary>Live positions of moving things (owned by Gameplay, which moves them). For visuals that follow actors.</summary>
    public interface IActorLocator
    {
        bool TryGetGuestPosition(GuestId guest, out WorldPoint position);
        WorldPoint CarPosition { get; }
    }
}
