using System;
using System.Collections.Generic;
using FloorF.Core;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Contracts
{
    public class EventBusTests
    {
        EventBus bus;

        [SetUp]
        public void SetUp() => bus = new EventBus();

        static DoorsOpened Doors(int floor, string label, int occupancy) => new DoorsOpened(new FloorIndex(floor), new FloorLabel(label), occupancy);

        [Test]
        public void Publish_DeliversPayloadToSubscribersInOrder()
        {
            var log = new List<string>();
            bus.Subscribe<DoorsOpened>(e => log.Add("a" + e.Label));
            bus.Subscribe<DoorsOpened>(e => log.Add("b" + e.Occupancy));
            bus.Publish(Doors(3, "F", 2));
            CollectionAssert.AreEqual(new[] { "aF", "b2" }, log);
        }

        [Test]
        public void Publish_OnlyReachesThatEventType()
        {
            int doors = 0, luck = 0;
            bus.Subscribe<DoorsOpened>(_ => doors++);
            bus.Subscribe<LuckChanged>(_ => luck++);
            bus.Publish(new LuckChanged(50, 40, 100, LuckSource.FloorLabel));
            Assert.AreEqual(0, doors);
            Assert.AreEqual(1, luck);
        }

        [Test]
        public void Publish_WithoutSubscribers_IsNoOp()
        {
            Assert.DoesNotThrow(() => bus.Publish(new TipsChanged(0, 10)));
        }

        [Test]
        public void DisposeToken_Unsubscribes_AndIsIdempotent()
        {
            int n = 0;
            var token = bus.Subscribe<TipsChanged>(_ => n++);
            bus.Publish(new TipsChanged(0, 1));
            token.Dispose();
            token.Dispose();
            bus.Publish(new TipsChanged(1, 2));
            Assert.AreEqual(1, n);
            Assert.AreEqual(0, bus.SubscriberCount<TipsChanged>());
        }

        [Test]
        public void Unsubscribe_ByDelegate()
        {
            int n = 0;
            Action<TipsChanged> h = _ => n++;
            bus.Subscribe(h);
            bus.Unsubscribe(h);
            bus.Unsubscribe(h); // absent: no-op
            bus.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(0, n);
        }

        [Test]
        public void SubscribeDuringPublish_TakesEffectNextPublish()
        {
            int late = 0;
            bus.Subscribe<TipsChanged>(_ => bus.Subscribe<TipsChanged>(__ => late++));
            bus.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(0, late);
            bus.Publish(new TipsChanged(1, 2));
            Assert.AreEqual(1, late);
        }

        [Test]
        public void UnsubscribeDuringPublish_StillCompletesCurrentPublish()
        {
            int second = 0;
            IDisposable token2 = null;
            bus.Subscribe<TipsChanged>(_ => token2.Dispose());
            token2 = bus.Subscribe<TipsChanged>(_ => second++);
            bus.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(1, second, "snapshot semantics: removed handler still sees the in-flight event");
            bus.Publish(new TipsChanged(1, 2));
            Assert.AreEqual(1, second);
        }

        [Test]
        public void NestedPublish_IsDepthFirst()
        {
            var log = new List<string>();
            bus.Subscribe<DoorsClosed>(e => { log.Add("closed"); bus.Publish(new LuckJudged(new GuestId(1), LuckSource.Occupancy, e.Occupancy, LuckVerdict.Unlucky, e.Floor)); log.Add("closed-end"); });
            bus.Subscribe<LuckJudged>(e => log.Add("judged" + e.Number));
            bus.Publish(new DoorsClosed(FloorIndex.Ground, new FloorLabel("1"), 4));
            CollectionAssert.AreEqual(new[] { "closed", "judged4", "closed-end" }, log);
        }

        [Test]
        public void ThrowingHandler_DoesNotStopOthers_AndFirstExceptionIsRethrown()
        {
            int after = 0;
            bus.Subscribe<TipsChanged>(_ => throw new InvalidOperationException("first"));
            bus.Subscribe<TipsChanged>(_ => throw new ArgumentException("second"));
            bus.Subscribe<TipsChanged>(_ => after++);
            var ex = Assert.Throws<InvalidOperationException>(() => bus.Publish(new TipsChanged(0, 1)));
            Assert.AreEqual("first", ex.Message);
            Assert.AreEqual(1, after);
        }

        [Test]
        public void ThrowingHandler_WithSink_IsReportedNotThrown()
        {
            var errors = new List<Exception>();
            bus.HandlerErrorSink = errors.Add;
            int after = 0;
            bus.Subscribe<TipsChanged>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<TipsChanged>(_ => after++);
            Assert.DoesNotThrow(() => bus.Publish(new TipsChanged(0, 1)));
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(1, after);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            int n = 0;
            bus.Subscribe<TipsChanged>(_ => n++);
            bus.Subscribe<LuckChanged>(_ => n++);
            bus.Clear();
            bus.Publish(new TipsChanged(0, 1));
            bus.Publish(new LuckChanged(1, 2, 3, LuckSource.Other));
            Assert.AreEqual(0, n);
        }

        [Test]
        public void SameDelegateTwice_RunsTwice_DisposeRemovesOne()
        {
            int n = 0;
            Action<TipsChanged> h = _ => n++;
            var t1 = bus.Subscribe(h);
            bus.Subscribe(h);
            bus.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(2, n);
            t1.Dispose();
            bus.Publish(new TipsChanged(1, 2));
            Assert.AreEqual(3, n);
        }

        [Test]
        public void Subscriptions_ClearDisposesAll()
        {
            int n = 0;
            var subs = new Subscriptions();
            subs.Add(bus.Subscribe<TipsChanged>(_ => n++));
            subs.Add(bus.Subscribe<TipsChanged>(_ => n++));
            subs.Add(null);
            Assert.AreEqual(2, subs.Count);
            subs.Clear();
            bus.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(0, n);
            Assert.AreEqual(0, subs.Count);
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<TipsChanged>(null));
        }

        [Test]
        public void EventStructs_DerivedValues()
        {
            Assert.AreEqual(-10, new LuckChanged(50, 40, 100, LuckSource.FloorLabel).Delta);
            Assert.AreEqual(15, new TipsChanged(5, 20).Delta);
            Assert.AreEqual(-7, new DaySummary(1, 2, 3, 50, 43, 1, 2).LuckDelta);
        }
    }

    public class GameServicesTests
    {
        interface IFoo { }
        sealed class Foo : IFoo { }

        [SetUp]
        public void SetUp() => GameServices.Reset();

        [TearDown]
        public void TearDown() => GameServices.Reset();

        [Test]
        public void RegisterGet_RoundTrips()
        {
            var foo = new Foo();
            GameServices.Register<IFoo>(foo);
            Assert.AreSame(foo, GameServices.Get<IFoo>());
            Assert.IsTrue(GameServices.Has<IFoo>());
        }

        [Test]
        public void Get_Missing_ThrowsHelpfulMessage()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => GameServices.Get<IFoo>());
            StringAssert.Contains("IFoo", ex.Message);
            StringAssert.Contains("Start()", ex.Message);
            Assert.IsFalse(GameServices.TryGet<IFoo>(out _));
        }

        [Test]
        public void Register_Replaces_AndUnregisterOnlyRemovesSameInstance()
        {
            var a = new Foo();
            var b = new Foo();
            GameServices.Register<IFoo>(a);
            GameServices.Register<IFoo>(b); // e.g. Main reloaded before the old root's OnDestroy
            Assert.IsFalse(GameServices.Unregister<IFoo>(a));
            Assert.AreSame(b, GameServices.Get<IFoo>());
            Assert.IsTrue(GameServices.Unregister<IFoo>(b));
            Assert.IsFalse(GameServices.Has<IFoo>());
        }

        [Test]
        public void Reset_ClearsServicesAndSubscriptions()
        {
            int n = 0;
            GameServices.Register<IFoo>(new Foo());
            var oldBus = GameServices.Events;
            oldBus.Subscribe<TipsChanged>(_ => n++);
            GameServices.Reset();
            GameServices.Events.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(0, n);
            Assert.AreNotSame(oldBus, GameServices.Events);
            Assert.IsFalse(GameServices.Has<IFoo>());
        }

        [Test]
        public void HandlerErrorSink_AppliesToBus()
        {
            var errors = new List<Exception>();
            GameServices.HandlerErrorSink = errors.Add;
            GameServices.Events.Subscribe<TipsChanged>(_ => throw new Exception("x"));
            GameServices.Events.Publish(new TipsChanged(0, 1));
            Assert.AreEqual(1, errors.Count);
        }
    }
}
