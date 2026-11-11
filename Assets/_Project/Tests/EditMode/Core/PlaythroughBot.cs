using System;
using System.Collections.Generic;
using System.Linq;
using FloorF.Core;
using FloorF.Tests.EditMode.Content;
using FloorF.UI;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>
    /// Full-run bot for <see cref="PlaythroughBotTests"/>: plays Day 0 -> last day -> ending on a pure Core
    /// <see cref="GameSession"/> through the public commands only (IElevatorControl / IGameFlow / IShopService / IMenuFlow),
    /// with a fixed seed and a coarse simulated frame (<see cref="Dt"/>). While it plays it checks the run invariants and
    /// records them as <see cref="Violations"/> (the test asserts there are none) plus per-day numbers for the balance table.
    /// With <see cref="ExtendSeason"/> it keeps the hotel open from the base Ending screen (ISeasonFlow) and plays the winter
    /// days 8-10 to their ending too (<see cref="FirstEnding"/> = the base run's ending, <see cref="Ending"/> = the last one).
    /// </summary>
    public sealed class PlaythroughBot
    {
        public enum Policy
        {
            /// <summary>LuckAware pilot + greedy ShopBot (buys stickers/charms, rerolls). Plays Day 0.</summary>
            Greedy,
            /// <summary>LuckBlind pilot (serves calls, ignores luck) + greedy ShopBot. Plays Day 0.</summary>
            Blind,
            /// <summary>Never touches the elevator, never buys. Skips Day 0 (it has no clock and would never end).</summary>
            Idle,
            /// <summary>Random clicks (floors, doors, wishes, pauses, wrong-phase flow calls, random shop buys/rerolls).</summary>
            Random,
            /// <summary>Greedy, plus the 4 -> F sticker onto the real 4th floor in the shop before the last day.</summary>
            FourthFloor,
        }

        /// <summary>Simulated frame (s). GameSession sub-steps it at <see cref="GameSession.MaxStepSeconds"/>.</summary>
        public const float Dt = 0.1f;
        /// <summary>This bot's frame (default <see cref="Dt"/>; finer frames for frame-rate comparisons).</summary>
        public float FrameSeconds = Dt;
        /// <summary>A tutorial still running after this much simulated time counts as stuck.</summary>
        const float TutorialLimitSeconds = 900f;
        /// <summary>Random policy: random clicks only for this long on Day 0, then a LuckBlind pilot finishes it.</summary>
        const float RandomTutorialSeconds = 150f;
        const int MaxViolations = 30;

        readonly GameData data;
        readonly RulesDef rules;
        public readonly Policy Kind;
        public readonly int Seed;
        public readonly EventBus Bus = new EventBus();
        public readonly GameSession S;
        readonly Random rng;
        readonly ShopBot shopper;

        public readonly List<string> Violations = new List<string>();

        // ---- results
        public EndingKind Ending = EndingKind.None;
        public int EndingDisplayDay = -1;
        public int FinalTips = -1;
        /// <summary>Luck / tips at the end of each display day (index = display day; -1 = not played).</summary>
        public readonly int[] LuckAfterDay, TipsAfterDay;
        public readonly List<string> Grades = new List<string>();
        public bool TutorialPlayed, TutorialEnded, RandomSoloFinishedTutorial;
        public float TutorialSeconds;
        public int TipsEarnedTotal, Delivered, LeftCount, Purchases, Rerolls, LotteryDraws, Jackpots;
        public bool FourthStickerBought;
        /// <summary>Ending screen: ExtendSeason whenever it is offered, then play the winter season.</summary>
        public bool ExtendSeason;
        /// <summary>The run's first ending (the base run's; Ending is the final one after an extension).</summary>
        public EndingKind FirstEnding = EndingKind.None;
        public int FirstEndingDisplayDay = -1;
        public bool Extended;
        /// <summary>The mystery guest was decided for tonight / actually appeared during this run.</summary>
        public bool MysteryTonightSeen, MysteryCame;

        /// <summary>Optional hook called once per loop iteration before the phase is handled; true = the bot stops.</summary>
        public Func<PlaythroughBot, bool> Interrupt;

        // ---- invariant tracking
        readonly HashSet<GuestId> openGuests = new HashSet<GuestId>();
        readonly HashSet<int> openStars = new HashSet<int>();
        readonly HashSet<int> boughtFloors = new HashSet<int>(), activeFloors = new HashSet<int>();
        readonly List<StickerId> boughtCharms = new List<StickerId>(), activeCharms = new List<StickerId>();
        int rerollK;
        int lastStartedDay;
        /// <summary>Luck / tips as the events told them (a silent change would desync every meter).</summary>
        int knownLuck, knownTips;
        readonly Dictionary<GuestId, FloorIndex> destinationOf = new Dictionary<GuestId, FloorIndex>();
        // today's events, compared with the DaySummary
        int dayDelivered, dayLeft, dayTips, dayLucky, dayUnlucky, dayBestStreak, dayLuckAtStart;
        float dayTime;
        AutoPilot pilot, fallback;
        float wait;

        public PlaythroughBot(GameData data, Policy policy, int seed)
        {
            this.data = data;
            rules = data.Rules;
            Kind = policy;
            Seed = seed;
            S = new GameSession(data, Bus, new Random(seed));
            rng = new Random(seed * 7919 + (int)policy * 104729 + 17);
            shopper = new ShopBot(data);
            LuckAfterDay = Enumerable.Repeat(-1, data.AllDayCount).ToArray();
            TipsAfterDay = Enumerable.Repeat(-1, data.AllDayCount).ToArray();
            Subscribe();
        }

        string Where => "[" + Kind + " seed " + Seed + " day " + S.DisplayDay + " " + S.Phase + " t=" + S.ElapsedSeconds.ToString("0.0") + "] ";

        public void Check(bool ok, string what)
        {
            if (ok || Violations.Count >= MaxViolations) return;
            Violations.Add(Where + what);
        }

        // =========================================================================================== events

        void Subscribe()
        {
            // a new run (restart, title -> start, debug day jump): nothing bought is pending any more
            Bus.Subscribe<GameStarted>(e =>
            {
                boughtFloors.Clear(); activeFloors.Clear(); boughtCharms.Clear(); activeCharms.Clear();
                lastStartedDay = 0;
                rerollK = 0;
                Ending = EndingKind.None;
                FirstEnding = EndingKind.None;
                FirstEndingDisplayDay = -1;
                Extended = false;
            });
            Bus.Subscribe<SeasonExtended>(e =>
            {
                Check(e.FirstDay.Number == data.DayCount + 1 && e.ExtraDays == data.ExtendedDayCount, "SeasonExtended " + e.FirstDay + " +" + e.ExtraDays);
                Check(S.IsSeasonExtended && S.Ending == EndingKind.None && S.PlayableDayCount == data.SeasonPlayableDayCount, "season not extended after SeasonExtended");
            });
            knownLuck = S.Score.Luck;
            knownTips = S.Score.Tips;
            Bus.Subscribe<LuckChanged>(e =>
            {
                Check(e.Current >= 0 && e.Current <= e.Max && e.Max == S.Score.MaxLuck, "luck out of range: " + e.Current + "/" + e.Max);
                Check(e.Previous == knownLuck && e.Current == S.Score.Luck && e.Current != e.Previous, "LuckChanged " + e.Previous + "->" + e.Current + " but known " + knownLuck + ", now " + S.Score.Luck);
                knownLuck = e.Current;
            });
            Bus.Subscribe<TipsChanged>(e =>
            {
                Check(e.Current >= 0, "tips negative: " + e.Current);
                Check(e.Previous == knownTips && e.Current == S.Score.Tips && e.Current != e.Previous, "TipsChanged " + e.Previous + "->" + e.Current + " but known " + knownTips + ", now " + S.Score.Tips);
                knownTips = e.Current;
            });
            Bus.Subscribe<GuestBoarded>(e => Check(e.Occupancy <= data.Capacity && S.Elevator.Occupancy <= S.Elevator.Capacity, "over capacity: " + e.Occupancy));
            Bus.Subscribe<GuestSpawned>(e =>
            {
                Check(openGuests.Add(e.Guest), "guest spawned twice " + e.Guest);
                destinationOf[e.Guest] = e.DestinationFloor;
                if (data.TryGetGuest(e.Archetype, out var def) && def.HasTrait(GuestTraits.Mystery))
                {
                    MysteryCame = true;
                    Check(S.MysteryTonight, "mystery guest spawned on a night it was not due");
                }
            });
            Bus.Subscribe<GuestDelivered>(e =>
            {
                Check(openGuests.Remove(e.Guest), "delivered a guest that was not open " + e.Guest);
                Check(destinationOf.TryGetValue(e.Guest, out var dest) && dest == e.Floor, "delivered off its destination: " + e.Floor);
                Check(e.Tip >= 0, "negative tip");
                Delivered++; dayDelivered++; dayTips += e.Tip;
            });
            Bus.Subscribe<GuestLeft>(e =>
            {
                Check(openGuests.Remove(e.Guest), "left: guest not open " + e.Guest);
                if (e.Reason == GuestLeaveReason.DayEnded) return;
                LeftCount++; dayLeft++;
            });
            Bus.Subscribe<LuckJudged>(e => { if (e.Verdict == LuckVerdict.Lucky) dayLucky++; else if (e.Verdict == LuckVerdict.Unlucky) dayUnlucky++; });
            Bus.Subscribe<LuckStreakChanged>(e =>
            {
                Check(e.Streak == S.Score.LuckStreak && (e.Streak == e.Previous + 1 || e.Streak == 0), "streak step " + e.Previous + "->" + e.Streak);
                dayBestStreak = Math.Max(dayBestStreak, e.Streak);
            });
            Bus.Subscribe<WishStarAppeared>(e => Check(openStars.Add(e.Id), "star appeared twice " + e.Id));
            Bus.Subscribe<WishMade>(e => Check(openStars.Remove(e.Id), "wish on a closed star " + e.Id));
            Bus.Subscribe<WishStarFaded>(e => Check(openStars.Remove(e.Id), "faded a closed star " + e.Id));

            Bus.Subscribe<DayStarted>(e =>
            {
                Check(e.Day.Number > lastStartedDay, "day order: " + e.Day.Number + " after " + lastStartedDay);
                lastStartedDay = e.Day.Number;
                dayTime = 0f;
                dayDelivered = dayLeft = dayTips = dayLucky = dayUnlucky = dayBestStreak = 0;
                dayLuckAtStart = S.Score.Luck;
                Check(S.Score.LuckStreak == 0 && S.Score.TipsEarnedToday == 0, "streak / tips today carried into a new day");
                // one-day purchases: exactly what the last shop visit bought is active now
                activeFloors.Clear(); activeFloors.UnionWith(boughtFloors); boughtFloors.Clear();
                activeCharms.Clear(); activeCharms.AddRange(boughtCharms); boughtCharms.Clear();
                var relabelled = RelabelledFloors();
                Check(relabelled.SetEquals(activeFloors), "stickers active today " + Show(relabelled) + " != bought " + Show(activeFloors));
                var charms = S.ActiveCharms.Select(c => c.Id).OrderBy(c => c.Value).ToList();
                Check(charms.SequenceEqual(activeCharms.OrderBy(c => c.Value)), "charms active today [" + string.Join(",", charms) + "] != bought [" + string.Join(",", activeCharms) + "]");
                if (e.IsTutorial) TutorialPlayed = true;
                if (S.MysteryTonight) MysteryTonightSeen = true;
                Check(S.IsExtendedDay == data.IsExtendedDay(e.Day) && (!S.IsExtendedDay || S.IsSeasonExtended), "winter day without the season extended");
                Check(e.DayCount == S.DayCount, "DayStarted.DayCount " + e.DayCount + " != " + S.DayCount);
                Check(S.IsLotteryNight == data.GetDay(e.Day).lottery, "lottery flag");
            });
            Bus.Subscribe<LotteryDrawn>(e =>
            {
                Check(S.IsLotteryNight && S.Phase == DayPhase.Running, "draw outside a running lottery night");
                Check(e.Number == S.LotteryNumber && e.DrawIndex < e.DrawCount, "draw " + e.Number + " #" + e.DrawIndex + "/" + e.DrawCount);
                bool shown = false;
                for (int f = 0; f < S.Building.FloorCount; f++)
                    if (S.Building.GetLabel(new FloorIndex(f)).TryGetNumber(out int n) && n == e.Number) shown = true;
                Check(shown, "drawn number " + e.Number + " is not on any floor button");
                LotteryDraws++;
            });
            Bus.Subscribe<LotteryWon>(e =>
            {
                Check(e.Number == S.LotteryNumber && S.Building.GetLabel(e.Floor).TryGetNumber(out int n) && n == e.Number, "jackpot on a floor not showing the number");
                Jackpots++;
            });
            Bus.Subscribe<StickersExpired>(e =>
            {
                // a day's end expires today's purchases; a forced ending from the shop / an intro also drops tomorrow's
                int labels = activeFloors.Union(boughtFloors).Count(), charms = activeCharms.Count + boughtCharms.Count;
                Check(e.Labels == labels && e.Charms == charms,
                    "expired " + e.Labels + " labels / " + e.Charms + " charms, active were " + labels + " / " + charms);
                activeFloors.Clear(); boughtFloors.Clear();
                activeCharms.Clear(); boughtCharms.Clear();
            });
            Bus.Subscribe<DayEnded>(e =>
            {
                var sum = e.Summary;
                int display = data.DisplayDayNumber(e.Day);
                bool tutorial = data.GetDay(e.Day).tutorial;
                if (tutorial) { TutorialEnded = true; TutorialSeconds = S.ElapsedSeconds; }
                else
                {
                    LuckAfterDay[display] = sum.LuckAtEnd;
                    TipsAfterDay[display] = S.Score.Tips;
                    TipsEarnedTotal += sum.TipsEarned;
                    Grades.Add(UIFormat.DayGrade(sum));
                }
                Check(openGuests.Count == 0, "guests still open at day end: " + openGuests.Count);
                Check(S.Guests.ActiveGuests.Count == 0 && S.Elevator.Occupancy == 0, "guests/riders left at day end");
                Check(sum.LuckAtEnd == S.Score.Luck && sum.LuckAtStart == dayLuckAtStart, "summary luck " + sum.LuckAtStart + "->" + sum.LuckAtEnd);
                Check(sum.Delivered == dayDelivered && sum.Left == dayLeft, "summary delivered/left " + sum.Delivered + "/" + sum.Left + " != events " + dayDelivered + "/" + dayLeft);
                Check(sum.TipsEarned == dayTips, "summary tips " + sum.TipsEarned + " != paid " + dayTips);
                Check(sum.LuckyJudgements == dayLucky && sum.UnluckyJudgements == dayUnlucky, "summary verdicts " + sum.LuckyJudgements + "/" + sum.UnluckyJudgements + " != events " + dayLucky + "/" + dayUnlucky);
                Check(sum.BestStreak == dayBestStreak, "summary best streak " + sum.BestStreak + " != " + dayBestStreak);
                Check(sum.TipsEarned >= 0 && sum.Delivered >= 0 && sum.Left >= 0, "summary negative");
            });
            Bus.Subscribe<DayPhaseChanged>(e =>
            {
                if (e.Previous == DayPhase.Running && (e.Current == DayPhase.Summary || e.Current == DayPhase.Ending)) AfterDay();
                if (e.Current == DayPhase.Shop) OnShopOpened();
            });
            Bus.Subscribe<ShopRerolled>(e =>
            {
                Rerolls++;
                int left = Math.Max(0, rules.maxRerolls - (rerollK + 1));
                Check(e.Cost == Cost(rerollK), "reroll #" + rerollK + " cost " + e.Cost + " != " + Cost(rerollK));
                Check(e.RerollsLeft == left && S.RerollsLeft == left, "rerolls left " + e.RerollsLeft + " != " + left);
                Check(e.NextCost == (left > 0 ? Cost(rerollK + 1) : 0) && S.RerollCost == e.NextCost, "next reroll cost " + e.NextCost);
                rerollK++;
            });
            Bus.Subscribe<StickerApplied>(e => { boughtFloors.Add(e.Floor.Value); Purchases++; if (e.Floor == StoryRules.RealFourthFloor && data.IsLastDay(S.CurrentDay.Next)) FourthStickerBought = true; });
            Bus.Subscribe<CharmBought>(e => { boughtCharms.Add(e.Charm); Purchases++; });
            Bus.Subscribe<EndingReached>(e =>
            {
                Ending = e.Ending;
                EndingDisplayDay = data.DisplayDayNumber(e.Day);
                FinalTips = e.TotalTips;
                Check(e.Ending != EndingKind.None, "EndingReached(None)");
                Check(e.Ending != EndingKind.Haunted || e.FinalLuck == 0, "haunted with luck " + e.FinalLuck);
                Check(e.Ending != EndingKind.Prosperous || e.FinalLuck >= rules.prosperousEndingLuck, "prosperous below the line");
                Check(e.Ending != EndingKind.Modest || (e.FinalLuck > 0 && e.FinalLuck < rules.prosperousEndingLuck), "modest outside its range " + e.FinalLuck);
                Check(e.Ending != EndingKind.WinterMiracle || e.FinalLuck >= rules.prosperousEndingLuck, "winter miracle below the line");
                Check(e.Ending != EndingKind.SilentNight || (e.FinalLuck > 0 && e.FinalLuck < rules.prosperousEndingLuck), "silent night outside its range " + e.FinalLuck);
                bool winter = e.Ending == EndingKind.WinterMiracle || e.Ending == EndingKind.SilentNight;
                Check(!winter || (S.IsSeasonExtended && data.IsLastSeasonDay(e.Day)), "winter ending outside the season's last day");
                Check(!S.IsSeasonExtended || e.Ending != EndingKind.Prosperous && e.Ending != EndingKind.Modest, "base ending during the season");
                if (FirstEnding == EndingKind.None) { FirstEnding = e.Ending; FirstEndingDisplayDay = data.DisplayDayNumber(e.Day); }
                Check(S.Offers.Count == 0 && !S.IsOpen, "shop still open at the ending");
            });
        }

        int Cost(int k) => (int)Math.Round(rules.rerollBaseCost * Math.Pow(rules.rerollCostMultiplier, k), MidpointRounding.AwayFromZero);

        HashSet<int> RelabelledFloors()
        {
            var set = new HashSet<int>();
            for (int f = 0; f < S.Building.FloorCount; f++) if (S.Building.IsRelabelled(new FloorIndex(f))) set.Add(f);
            return set;
        }

        static string Show(HashSet<int> floors) => "{" + string.Join(",", floors.OrderBy(f => f).Select(f => f + 1)) + "}";

        void AfterDay()
        {
            Check(RelabelledFloors().Count == 0, "stickers survived their day: " + Show(RelabelledFloors()));
            Check(S.ActiveCharms.Count == 0, "charms survived their day");
            Check(openStars.Count == 0, "wish stars still up after the day");
            var sky = new List<int>();
            Check(S.GetWishStarsInSky(sky) == 0 && !S.PendingWishStarTimes.Any(), "wish stars still scheduled after the day");
            for (int f = 0; f < data.FloorCount; f++) Check(S.Guests.WaitingCount(new FloorIndex(f)) == 0, "guests waiting after the day on floor " + (f + 1));
            Check(S.Elevator.Occupancy == 0, "riders in the car after the day");
        }

        void OnShopOpened()
        {
            rerollK = 0;
            Check(S.RerollsLeft == rules.maxRerolls, "fresh visit rerolls " + S.RerollsLeft);
            Check(S.RerollCost == (rules.maxRerolls > 0 ? Cost(0) : 0), "fresh visit reroll cost " + S.RerollCost);
            Check(S.Offers.Count > 0 && S.Offers.Count <= rules.shopOfferCount, "offer count " + S.Offers.Count);
            Check(S.Offers.Select(o => o.Id).Distinct().Count() == S.Offers.Count, "duplicate offers");
            var pool = data.GetDay(S.CurrentDay).shopStickers;
            if (pool.Contains("four_to_f")) Check(S.Offers.Any(o => o.Id.Value == "four_to_f"), "4 -> F staple missing");
        }

        // =========================================================================================== main loop

        /// <summary>Plays from a new game to an ending (or until <see cref="Interrupt"/> stops it).</summary>
        public PlaythroughBot Play(bool startNewGame = true)
        {
            if (startNewGame) S.StartNewGame();
            for (int guard = 0; guard < 400000; guard++)
            {
                if (Interrupt != null && Interrupt(this)) return this;
                if (S.Phase == DayPhase.Ending)
                {
                    if (!ExtendSeason || !S.CanExtendSeason) break;
                    Extended = true;
                    bool ok = S.ExtendSeason();
                    Check(ok, "ExtendSeason refused while offered");
                    if (!ok) return this;
                    continue;
                }
                if (Violations.Count >= MaxViolations) return this;
                if (!StepOnce()) return this;
            }
            Check(S.Phase == DayPhase.Ending, "no ending reached");
            Check(Ending == S.Ending && Ending != EndingKind.None, "ending " + S.Ending + " / event " + Ending);
            if (Ending == EndingKind.Prosperous || Ending == EndingKind.Modest)
                Check(EndingDisplayDay == data.PlayableDayCount, "Prosperous/Modest before the last day");
            if (Ending == EndingKind.WinterMiracle || Ending == EndingKind.SilentNight)
                Check(EndingDisplayDay == data.SeasonPlayableDayCount, "winter ending before the last winter day");
            if (ExtendSeason && data.HasExtendedDays && (FirstEnding == EndingKind.Prosperous || FirstEnding == EndingKind.Modest))
                Check(Extended, "the season was offered but not extended");
            return this;
        }

        /// <summary>One loop iteration; false = the bot gave up (a violation was recorded).</summary>
        public bool StepOnce()
        {
            switch (S.Phase)
            {
                case DayPhase.Intro: return Intro();
                case DayPhase.Running: return Running();
                case DayPhase.Summary: return Summary();
                case DayPhase.Shop: return Shop();
                case DayPhase.Ending: return true;
                default:
                    Check(false, "unexpected phase " + S.Phase);
                    return false;
            }
        }

        bool Intro()
        {
            if (Kind == Policy.Random && rng.Next(4) == 0) PauseProbe();
            if (S.IsTutorial && Kind == Policy.Idle)
            {
                Check(S.SkipTutorial(), "SkipTutorial refused on the Day 0 intro");
                return true;
            }
            // the 4th-floor bot tests the ending path, not its rarity: the mystery guest comes whenever it is scheduled
            if (Kind == Policy.FourthFloor && !S.MysteryTonight) S.DebugToggleMysteryTonight();
            var strategy = Kind == Policy.Greedy || Kind == Policy.FourthFloor ? AutoPilotStrategy.LuckAware : AutoPilotStrategy.LuckBlind;
            // the pilot refuses the tutorial when its control is the session (IDayQuery): drive Day 0 through a plain wrapper
            IElevatorControl control = S.IsTutorial ? new PlainControl(S) : (IElevatorControl)S;
            pilot = Kind == Policy.Idle ? null : new AutoPilot(S.Elevator, control, S.Guests, S.Building, strategy) { Data = data };
            fallback = S.IsTutorial ? new AutoPilot(S.Elevator, new PlainControl(S), S.Guests, S.Building, AutoPilotStrategy.LuckBlind) : null;
            wait = 0f;
            bool began = S.BeginDay();
            Check(began, "BeginDay refused on the Intro");
            return began;
        }

        bool Running()
        {
            bool tutorial = S.IsTutorial;
            if (wait <= 0f)
            {
                Drive(tutorial);
                wait = (float)rng.NextDouble() * (Kind == Policy.Random ? 0.8f : 0.4f);
            }
            wait -= FrameSeconds;
            S.Tick(FrameSeconds);
            dayTime += FrameSeconds;
            if (S.Elevator.Occupancy > S.Elevator.Capacity) Check(false, "over capacity " + S.Elevator.Occupancy);
            if (S.Score.Luck < 0 || S.Score.Luck > S.Score.MaxLuck || S.Score.Tips < 0) Check(false, "score out of range " + S.Score.Luck + " / " + S.Score.Tips);
            if (S.Phase != DayPhase.Running) return true;
            if (tutorial)
            {
                if (dayTime > TutorialLimitSeconds) { Check(false, "Day 0 never ended (" + S.Guests.ActiveGuests.Count + " guests)"); return false; }
            }
            else if (S.ElapsedSeconds > S.DurationSeconds + 1f || dayTime > S.DurationSeconds + 5f)
            {
                Check(false, "day clock ran past its duration"); return false;
            }
            return true;
        }

        void Drive(bool tutorial)
        {
            switch (Kind)
            {
                case Policy.Idle: return;
                case Policy.Random:
                    if (tutorial && dayTime > RandomTutorialSeconds)
                    {
                        // a coach-mark hold waits for the player's next command; the UI lifts its own marks
                        if (S.TutorialHold) S.SetTutorialHold(false);
                        fallback.Step();
                        return;
                    }
                    RandomClick(tutorial);
                    return;
                default:
                    pilot.Step();
                    return;
            }
        }

        bool Summary()
        {
            if (Kind == Policy.Random && rng.Next(4) == 0) PauseProbe();
            bool tutorial = S.IsTutorial;
            if (tutorial && Kind == Policy.Random && TutorialEnded && dayTime <= RandomTutorialSeconds + FrameSeconds) RandomSoloFinishedTutorial = true;
            var next = S.CurrentDay.Next;
            bool ok = S.ContinueFromSummary();
            Check(ok, "ContinueFromSummary refused");
            if (!ok) return false;
            if (tutorial)
            {
                Check(S.Phase == DayPhase.Intro && S.CurrentDay == next, "Day 0 summary -> next Intro");
                Check(S.Score.Luck == rules.startLuck && S.Score.Tips == rules.startTips, "Day 0 did not reset luck/tips: " + S.Score.Luck + " / " + S.Score.Tips);
            }
            return true;
        }

        bool Shop()
        {
            if (Kind == Policy.Random && rng.Next(3) == 0) PauseProbe();
            switch (Kind)
            {
                case Policy.Greedy:
                case Policy.Blind:
                    shopper.Shop(S);
                    break;
                case Policy.FourthFloor:
                    if (data.IsLastDay(S.CurrentDay.Next)) BuyFourToF();
                    shopper.Shop(S);
                    break;
                case Policy.Random:
                    RandomShop();
                    break;
            }
            bool ok = S.FinishShopping();
            Check(ok, "FinishShopping refused");
            return ok;
        }

        void BuyFourToF()
        {
            foreach (var o in S.Offers)
                if (o.Id.Value == "four_to_f" && S.CanBuyAndApply(o.Id, StoryRules.RealFourthFloor) == StickerApplyResult.Applied)
                {
                    Check(S.TryBuyAndApply(o.Id, StoryRules.RealFourthFloor) == StickerApplyResult.Applied, "4 -> F refused");
                    return;
                }
        }

        // =========================================================================================== random clicker

        void RandomClick(bool tutorial)
        {
            int a = rng.Next(100);
            if (a < 40) S.RequestFloor(new FloorIndex(rng.Next(data.FloorCount)));
            else if (a < 58) S.OpenDoors();
            else if (a < 72) S.CloseDoors();
            else if (a < 78)
            {
                var sky = new List<int>();
                if (S.GetWishStarsInSky(sky) > 0) Check(S.MakeWish(sky[rng.Next(sky.Count)]), "wish on a star in the sky refused");
                else Check(!S.MakeWish(12345), "wish on a missing star accepted");
            }
            else if (a < 82) PauseProbe();
            else if (a < 85)
            {
                Check(!S.BeginDay() && !S.ContinueFromSummary() && !S.FinishShopping() && !S.SkipTutorial(), "flow command accepted mid-day");
                Check(S.Reroll() == false && S.TryBuy(new StickerId("talisman")) == StickerApplyResult.ShopClosed, "shop works mid-day");
            }
            else if (a < 88 && tutorial) S.SetTutorialHold(true);
        }

        /// <summary>Pause, check that the clock and every command are frozen, resume.</summary>
        void PauseProbe()
        {
            var phase = S.Phase;
            float t = S.ElapsedSeconds;
            if (!S.SetPaused(true)) { Check(false, "pause refused in " + phase); return; }
            Check(S.IsPaused, "not paused");
            S.Tick(1f);
            Check(S.ElapsedSeconds == t && S.Phase == phase, "paused clock moved");
            Check(!S.BeginDay() && !S.ContinueFromSummary() && !S.FinishShopping(), "flow command accepted while paused");
            Check(!S.RequestFloor(new FloorIndex(rng.Next(data.FloorCount))) && !S.OpenDoors() && !S.CloseDoors() && !S.AcceptsInput, "elevator command accepted while paused");
            Check(!S.Reroll(), "reroll while paused");
            if (S.Offers.Count > 0) Check(S.CanBuy(S.Offers[0].Id) == StickerApplyResult.ShopClosed, "shop open while paused");
            Check(S.SetPaused(false) && !S.IsPaused, "unpause");
            Check(S.Phase == phase, "pause changed the phase");
        }

        void RandomShop()
        {
            for (int i = 0; i < 12; i++)
            {
                int a = rng.Next(10);
                if (a < 5 && S.Offers.Count > 0)
                {
                    var o = S.Offers[rng.Next(S.Offers.Count)];
                    int tips = S.Score.Tips;
                    if (o.Sticker.IsCharm)
                    {
                        var expected = S.CanBuy(o.Id);
                        var r = S.TryBuy(o.Id);
                        Check(r == expected, "TryBuy " + r + " but CanBuy said " + expected);
                        if (r == StickerApplyResult.Applied) Check(S.Score.Tips == tips - o.Price, "charm price not paid");
                        if (r == StickerApplyResult.NotEnoughTips) Check(tips < o.Price, "NotEnoughTips with enough tips");
                        if (r == StickerApplyResult.Applied) Check(S.CanBuy(o.Id) == StickerApplyResult.AlreadyActive || S.CanBuy(o.Id) == StickerApplyResult.OutOfStock, "charm bought twice possible");
                    }
                    else
                    {
                        var floor = new FloorIndex(rng.Next(data.FloorCount));
                        var expected = S.CanBuyAndApply(o.Id, floor);
                        var r = S.TryBuyAndApply(o.Id, floor);
                        Check(r == expected, "TryBuyAndApply " + r + " but CanBuyAndApply said " + expected);
                        if (r == StickerApplyResult.Applied)
                        {
                            Check(S.Score.Tips == tips - o.Price, "sticker price not paid");
                            Check(S.Building.GetLabel(floor) == o.Sticker.ResultLabel, "label not applied");
                        }
                        if (r == StickerApplyResult.NotEnoughTips) Check(tips < o.Price, "NotEnoughTips with enough tips");
                    }
                }
                else if (a < 8)
                {
                    int tips = S.Score.Tips, cost = S.RerollCost, left = S.RerollsLeft;
                    bool ok = S.Reroll();
                    Check(ok == (left > 0 && tips >= cost), "Reroll " + ok + " with " + left + " left, cost " + cost + ", tips " + tips);
                    if (ok) Check(S.Score.Tips == tips - cost && S.RerollsLeft == left - 1, "reroll not paid / counted");
                }
                else break;
            }
        }

        /// <summary>IElevatorControl that is not the session (so an AutoPilot drives the Day 0 tutorial too).</summary>
        sealed class PlainControl : IElevatorControl
        {
            readonly IElevatorControl c;
            public PlainControl(IElevatorControl c) { this.c = c; }
            public bool AcceptsInput => c.AcceptsInput;
            public bool RequestFloor(FloorIndex floor) => c.RequestFloor(floor);
            public bool OpenDoors() => c.OpenDoors();
            public bool CloseDoors() => c.CloseDoors();
        }
    }
}
