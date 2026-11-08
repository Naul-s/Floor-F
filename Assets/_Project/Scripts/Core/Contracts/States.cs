namespace FloorF.Core
{
    /// <summary>Elevator car movement. Doors are tracked separately in <see cref="DoorState"/>.</summary>
    public enum ElevatorMotion
    {
        Idle = 0,
        MovingUp = 1,
        MovingDown = 2,
    }

    public enum DoorState
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3,
    }

    public enum GuestState
    {
        /// <summary>Standing at a floor, waiting for the elevator. Patience is draining.</summary>
        Waiting = 0,
        /// <summary>Walking into the car (doors are open at their floor).</summary>
        Boarding = 1,
        /// <summary>Inside the car.</summary>
        Riding = 2,
        /// <summary>Walking out of the car.</summary>
        Exiting = 3,
        /// <summary>Arrived at their destination. Terminal.</summary>
        Delivered = 4,
        /// <summary>Gave up and took the stairs (or refused). Terminal.</summary>
        Left = 5,
    }

    public enum GuestLeaveReason
    {
        PatienceRanOut = 0,
        /// <summary>Refused to board (e.g. a gambler who only boards at a given occupancy, a scared guest).</summary>
        Refused = 1,
        /// <summary>Got off early after an unlucky event.</summary>
        Scared = 2,
        /// <summary>Day ended while the guest was still in the building.</summary>
        DayEnded = 3,
    }

    /// <summary>Discrete mood for bubbles/animation. Changes are announced with <see cref="GuestMoodChanged"/>.</summary>
    public enum GuestMood
    {
        Neutral = 0,
        Happy = 1,
        Delighted = 2,
        Impatient = 3,
        Scared = 4,
        Angry = 5,
    }

    /// <summary>Game flow phase. Title -> (Day: Intro -> Running -> Summary -> Shop -> next Intro) ... -> Ending.</summary>
    public enum DayPhase
    {
        None = 0,
        /// <summary>Title card / tutorial text before the clock starts.</summary>
        Intro = 1,
        /// <summary>Clock running, guests spawning, player in control.</summary>
        Running = 2,
        /// <summary>Day over: tally screen.</summary>
        Summary = 3,
        /// <summary>Sticker shop between days.</summary>
        Shop = 4,
        /// <summary>An ending is showing. Terminal until restart.</summary>
        Ending = 5,
        /// <summary>Title screen (boot). Gameplay may run an attract-mode demo behind it. IMenuFlow.StartGameFromTitle leaves it.</summary>
        Title = 6,
    }

    /// <summary>GDD 7 endings. <see cref="Modest"/> = finished all days without reaching the prosperity threshold.</summary>
    public enum EndingKind
    {
        None = 0,
        /// <summary>High Luck at the end of the last day.</summary>
        Prosperous = 1,
        /// <summary>Finished, Luck in between.</summary>
        Modest = 2,
        /// <summary>Luck hit 0: "haunted hotel" rumour. Can happen on any day.</summary>
        Haunted = 3,
        /// <summary>Delivered the mystery guest to the real (physical) 4th floor.</summary>
        FourthFloor = 4,
    }

    /// <summary>Outcome of a shop purchase/apply attempt.</summary>
    public enum StickerApplyResult
    {
        Applied = 0,
        UnknownSticker = 1,
        NotInShop = 2,
        OutOfStock = 3,
        NotEnoughTips = 4,
        /// <summary>The sticker's "from" label does not match the floor's current label.</summary>
        NotApplicable = 5,
        /// <summary>Not in the Shop phase.</summary>
        ShopClosed = 6,
        /// <summary>Label stickers need a floor (TryBuyAndApply); charms are bought without one (TryBuy).</summary>
        WrongKind = 7,
        /// <summary>That charm is already active for tomorrow.</summary>
        AlreadyActive = 8,
    }
}
