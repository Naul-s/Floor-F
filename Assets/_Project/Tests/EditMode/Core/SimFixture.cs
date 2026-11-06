using System;
using System.Collections.Generic;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Core
{
    /// <summary>Inline game data for rule tests (never the real JSON - content-designer tunes that).</summary>
    static class SimFixture
    {
        public sealed class Defs
        {
            public HotelDef Hotel;
            public GuestCatalogDef Guests;
            public DayCatalogDef Days;
            public StickerCatalogDef Stickers;
        }

        static GuestDef Guest(string id, int[] lucky, int[] unlucky, float patience = 1000f, int tip = 10, int party = 1, string[] traits = null)
            => new GuestDef { id = id, lucky = lucky, unlucky = unlucky, patienceSeconds = patience, baseTip = tip, partySize = party, traits = traits ?? new string[0], color = "#FFFFFF" };

        public static Defs Defaults()
        {
            var floors = new FloorDef[9];
            var dests = new DestinationDef[9];
            for (int i = 0; i < 9; i++)
            {
                floors[i] = new FloorDef { number = i + 1, label = "", destination = "d" + (i + 1) };
                dests[i] = new DestinationDef { id = "d" + (i + 1), name = "D" + (i + 1) };
            }
            var hotel = new HotelDef
            {
                floorCount = 9,
                capacity = 8,
                floors = floors,
                destinations = dests,
                rules = new RulesDef
                {
                    startLuck = 50, maxLuck = 100, startTips = 0,
                    luckyFloorTipMultiplier = 2f, luckyFloorLuckBonus = 2, unluckyFloorLuckPenalty = 10,
                    luckyOccupancyTipMultiplier = 1.5f, luckyOccupancyLuckBonus = 3, unluckyOccupancyLuckPenalty = 5,
                    patienceOutLuckPenalty = 5, deliveryLuckBonus = 1, honestGuestLuckPenalty = 5,
                    prosperousEndingLuck = 70,
                    elevatorSecondsPerFloor = 1f, doorSeconds = 0f, boardSeconds = 0f,
                },
                layout = new HotelLayoutDef(),
            };
            var guests = new GuestCatalogDef
            {
                guests = new[]
                {
                    Guest("granny", new[] { 3 }, new[] { 4 }),
                    Guest("biz", new[] { 8, 9 }, new[] { 4 }, tip: 20),
                    Guest("tourist", new[] { 7 }, new int[0]),
                    Guest("couple", new[] { 7 }, new[] { 4, 9 }, party: 2),
                    Guest("plain", new[] { 99 }, new int[0]),
                    Guest("loner", new[] { 1 }, new int[0]),
                    Guest("impatient", new[] { 99 }, new int[0], patience: 5f),
                    Guest("honest", new[] { 99 }, new int[0], traits: new[] { GuestTraits.Honest }),
                    new GuestDef { id = "mystery", lucky = new int[0], unlucky = new int[0], hideNumbers = true, patienceSeconds = 1000f, baseTip = 0, traits = new[] { GuestTraits.Mystery }, color = "#000000" },
                },
            };
            var days = new DayCatalogDef
            {
                days = new[]
                {
                    new DayDef { day = 1, durationSeconds = 1000f, tutorial = true, shopStickers = new[] { "four_to_f", "any_to_8" } },
                    new DayDef { day = 2, durationSeconds = 1000f, shopStickers = new string[0] },
                },
            };
            var stickers = new StickerCatalogDef
            {
                stickers = new[]
                {
                    new StickerDef { id = "four_to_f", from = "4", to = "F", price = 20, stock = 1 },
                    new StickerDef { id = "any_to_8", from = "*", to = "8", price = 40, stock = 1 },
                },
            };
            return new Defs { Hotel = hotel, Guests = guests, Days = days, Stickers = stickers };
        }

        public static GameData Create(Action<Defs> tweak = null)
        {
            var d = Defaults();
            tweak?.Invoke(d);
            return GameData.Create(d.Hotel, d.Guests, d.Days, d.Stickers);
        }
    }

    /// <summary>A GameSession on a private bus that records every event in order.</summary>
    sealed class Sim
    {
        public readonly EventBus Bus = new EventBus();
        public readonly GameSession S;
        public readonly List<object> Log = new List<object>();

        public Sim(GameData data, int seed = 1)
        {
            S = new GameSession(data, Bus, new Random(seed));
            Rec<GameStarted>(); Rec<DayPhaseChanged>(); Rec<DayStarted>(); Rec<DayEnded>();
            Rec<GuestSpawned>(); Rec<GuestBoarded>(); Rec<GuestExited>(); Rec<GuestDelivered>(); Rec<GuestLeft>();
            Rec<GuestMoodChanged>(); Rec<GuestSpoke>();
            Rec<ElevatorDeparted>(); Rec<ElevatorPassedFloor>(); Rec<ElevatorArrived>(); Rec<DoorsOpened>(); Rec<DoorsClosed>();
            Rec<LuckJudged>(); Rec<LuckChanged>(); Rec<TipsChanged>(); Rec<FloorLabelChanged>(); Rec<StickerApplied>(); Rec<EndingReached>();
        }

        void Rec<T>() => Bus.Subscribe<T>(e => Log.Add(e));

        /// <summary>New game, Day 1 running.</summary>
        public static Sim Running(Action<SimFixture.Defs> tweak = null)
        {
            var sim = new Sim(SimFixture.Create(tweak));
            sim.S.StartNewGame();
            Assert.IsTrue(sim.S.BeginDay());
            return sim;
        }

        public static FloorIndex F(int physical) => FloorIndex.FromPhysicalNumber(physical);

        public GuestId Spawn(string archetype, int floor, int destination) => S.SpawnGuest(archetype, floor, destination);

        public void Run(float seconds) => S.Tick(seconds);

        public void RunUntil(Func<bool> done, float maxSeconds = 200f)
        {
            float t = 0f;
            while (!done())
            {
                Assert.Less(t, maxSeconds, "condition not reached in time");
                S.Tick(0.05f);
                t += 0.05f;
            }
        }

        public void GoTo(int physical)
        {
            var target = F(physical);
            Assert.IsTrue(S.RequestFloor(target), "RequestFloor " + physical);
            var e = S.Elevator;
            RunUntil(() => e.Motion == ElevatorMotion.Idle && !e.HasTarget && e.Doors == DoorState.Closed && e.CurrentFloor == target);
        }

        public void Open()
        {
            Assert.IsTrue(S.OpenDoors(), "OpenDoors");
            RunUntil(() => S.Elevator.Doors == DoorState.Open && !S.IsTransferring);
        }

        public void Close()
        {
            Assert.IsTrue(S.CloseDoors(), "CloseDoors");
            RunUntil(() => S.Elevator.Doors == DoorState.Closed);
        }

        public List<T> Events<T>()
        {
            var r = new List<T>();
            foreach (var o in Log) if (o is T t) r.Add(t);
            return r;
        }

        public int IndexOf<T>(Func<T, bool> match = null, int from = 0)
        {
            for (int i = from; i < Log.Count; i++)
                if (Log[i] is T t && (match == null || match(t))) return i;
            return -1;
        }

        /// <summary>Asserts the log contains these event types as an ordered subsequence.</summary>
        public void AssertOrder(params Type[] types)
        {
            int k = 0;
            foreach (var o in Log) if (k < types.Length && o.GetType() == types[k]) k++;
            Assert.AreEqual(types.Length, k, "expected order: " + string.Join(" -> ", Array.ConvertAll(types, t => t.Name)) +
                "\nlog: " + string.Join(", ", Log.ConvertAll(o => o.GetType().Name)));
        }

        /// <summary>From Running Day 1 to the Shop phase (day ended early).</summary>
        public void EndDayToShop()
        {
            S.EndDayNow();
            Assert.AreEqual(DayPhase.Summary, S.Phase);
            Assert.IsTrue(S.ContinueFromSummary());
            Assert.AreEqual(DayPhase.Shop, S.Phase);
        }
    }
}
