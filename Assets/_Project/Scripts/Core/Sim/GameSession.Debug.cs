using System;

namespace FloorF.Core
{
    /// <summary>
    /// Debug cheats for hand-testing builds (Gameplay's DebugCheats calls these only while debug mode is on). Internal:
    /// not part of any contract. Every operation publishes the normal events so UI / Audio / Rendering stay consistent.
    ///
    /// Jump to day N (<see cref="DebugJumpToDay"/>, N = the player-facing day number: 0 = training shift): from ANY
    /// phase (title, paused, mid-day, shop, ending) -> that day's Intro. Guests and wish stars are dropped (GuestLeft
    /// DayEnded / WishStarFaded), stickered floors get their original labels back (FloorLabelChanged), charms and the shop
    /// are cleared, the ending is cleared; Luck and tips are KEPT (from the title: a fresh run's start values, the demo's
    /// numbers are not the player's). Then GameStarted(current Luck) -> DayPhaseChanged(shown -> Intro), so every view
    /// resets as for a new run. No DayEnded for an interrupted day.
    ///
    /// Force ending (<see cref="DebugForceEnding"/>): the real ending path (DayEnded + expiry when a day runs, then
    /// DayPhaseChanged(-> Ending) -> EndingReached), so the endings collection records it like a real one. Luck is made
    /// consistent with the ending first (LuckChanged, source Other): Haunted -> 0, Prosperous -> at least
    /// rules.prosperousEndingLuck, Modest -> below it (min 1), FourthFloor unchanged. Prosperous/Modest happen on the last
    /// day (the day moves there). From the title a run is started first (GameStarted -> Intro).
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>Player-facing day number (0 = tutorial shift) -> that day's Intro. False when there is no such day.</summary>
        internal bool DebugJumpToDay(int displayDay)
        {
            int number = displayDay + data.TutorialDayCount;
            if (number < 1 || number > data.DayCount) return false;
            var shown = Phase;
            Unpause();
            ReleaseTutorialHold();
            if (onTitle || !started)
            {
                onTitle = false;
                ResetRun();
            }
            else
            {
                FadeAllWishStars();
                DropAllGuests();
                Building.ResetLabels();
                CloseShop();
                ClearCharms();
                ending = EndingKind.None;
            }
            spawns = Array.Empty<SpawnDef>();
            nextSpawn = 0;
            day = new DayId(number);
            dayDef = data.GetDay(day);
            elapsed = 0f;
            waveClock = 0f;
            emptyFor = 0f;
            ResetElevator();
            phase = DayPhase.Intro;
            bus.Publish(new GameStarted(data.DayCount, Score.Luck, Score.MaxLuck));
            SetPhase(DayPhase.Intro, shown);
            return true;
        }

        /// <summary>Reaches <paramref name="kind"/> through the real ending path (see the class remarks).</summary>
        internal bool DebugForceEnding(EndingKind kind)
        {
            if (kind != EndingKind.Prosperous && kind != EndingKind.Modest && kind != EndingKind.Haunted && kind != EndingKind.FourthFloor)
                return false;
            Unpause();
            if (onTitle || !started) RestartGame();
            ReleaseTutorialHold();
            Score.SetLuck(LuckForEnding(kind), LuckSource.Other);
            if (phase == DayPhase.Running) CloseOutDay();
            else ExpirePurchases();
            if (kind == EndingKind.Prosperous || kind == EndingKind.Modest)
            {
                day = new DayId(data.DayCount);
                dayDef = data.GetDay(day);
            }
            ReachEnding(kind);
            return true;
        }

        int LuckForEnding(EndingKind kind)
        {
            int luck = Score.Luck;
            int threshold = rules.prosperousEndingLuck;
            switch (kind)
            {
                case EndingKind.Haunted: return 0;
                case EndingKind.Prosperous: return Math.Max(luck, threshold);
                case EndingKind.Modest: return Math.Max(1, Math.Min(luck, threshold - 1));
                default: return luck;
            }
        }

        /// <summary>Tips +/- (clamped at 0, not counted as earned): TipsChanged. False on the title / before a run.</summary>
        internal bool DebugAddTips(int delta)
        {
            if (onTitle || !started) return false;
            Score.AddTips(delta, earned: false);
            return true;
        }

        /// <summary>
        /// Luck +/- (clamped 0..max, source Other): LuckChanged. Emptying the meter on a running real day haunts the hotel
        /// as in play (a menu pause is lifted first). Returns the applied delta (0 on the title / before a run).
        /// </summary>
        internal int DebugAddLuck(int delta)
        {
            if (onTitle || !started) return 0;
            int applied = Score.AddLuck(delta, LuckSource.Other);
            if (phase == DayPhase.Running && !dayDef.tutorial && Score.Luck <= 0)
            {
                Unpause();
                CheckHaunted();
            }
            return applied;
        }

        /// <summary>Shop open: every reroll of this visit is available again at the base price. Publishes ShopRerolled(cost 0) so the shop refreshes.</summary>
        internal bool DebugRefillRerolls()
        {
            if (!IsOpen || onTitle) return false;
            rerollsDone = 0;
            bus.Publish(new ShopRerolled(0, RerollsLeft, RerollCost));
            return true;
        }

        /// <summary>Running day: the mystery guest (first archetype with the mystery trait) waits in the lobby, bound for the real 4th floor.</summary>
        internal GuestId DebugSpawnMystery()
        {
            if (onTitle || phase != DayPhase.Running) return GuestId.None;
            GuestDef def = null;
            foreach (var g in data.Guests)
                if (g.HasTrait(GuestTraits.Mystery)) { def = g; break; }
            if (def == null) return GuestId.None;
            var dest = StoryRules.RealFourthFloor;
            var from = new FloorIndex(0);
            if (!Building.IsValid(dest) || from == dest) return GuestId.None;
            return SpawnParty(def, from, Building.GetDestination(dest), dest);
        }

        /// <summary>Running day (not the title demo): ends it now as if the clock ran out (-> Summary).</summary>
        internal bool DebugEndDay()
        {
            if (onTitle || phase != DayPhase.Running) return false;
            Unpause();
            EndDayNow();
            return true;
        }
    }
}
