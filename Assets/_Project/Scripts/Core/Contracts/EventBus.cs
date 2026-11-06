using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace FloorF.Core
{
    /// <summary>
    /// Synchronous, typed publish/subscribe. Event types are the structs in Events.cs (any type works).
    /// Subscribe returns a token; dispose it in OnDisable/OnDestroy.
    /// </summary>
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void Publish<T>(T evt);
        int SubscriberCount<T>();
        /// <summary>Remove every subscription (tests, game restart).</summary>
        void Clear();
    }

    /// <summary>
    /// Default <see cref="IEventBus"/>. Not thread-safe (WebGL is single-threaded; call from the main thread).
    /// Semantics (covered by EventBusTests):
    ///  - handlers run in subscription order; the same delegate may subscribe twice and then runs twice;
    ///  - Publish iterates a snapshot: handlers (un)subscribed during a Publish take effect from the NEXT Publish;
    ///  - nested Publish from inside a handler is allowed (depth-first);
    ///  - a throwing handler does not stop the others. With <see cref="HandlerErrorSink"/> set, exceptions go there;
    ///    otherwise the first exception is rethrown after all handlers ran.
    ///  - Publish allocates nothing.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        readonly Dictionary<Type, Delegate[]> handlers = new Dictionary<Type, Delegate[]>();

        /// <summary>Gameplay wires this to UnityEngine.Debug.LogException.</summary>
        public Action<Exception> HandlerErrorSink { get; set; }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var type = typeof(T);
            handlers.TryGetValue(type, out var current);
            var next = new Delegate[(current?.Length ?? 0) + 1];
            if (current != null) Array.Copy(current, next, current.Length);
            next[next.Length - 1] = handler;
            handlers[type] = next;
            return new Subscription<T>(this, handler);
        }

        /// <summary>Removes the most recent subscription of <paramref name="handler"/> (no-op if absent).</summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!handlers.TryGetValue(type, out var current)) return;
            int idx = Array.LastIndexOf(current, handler);
            if (idx < 0) return;
            if (current.Length == 1) { handlers.Remove(type); return; }
            var next = new Delegate[current.Length - 1];
            if (idx > 0) Array.Copy(current, 0, next, 0, idx);
            if (idx < current.Length - 1) Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
            handlers[type] = next;
        }

        public void Publish<T>(T evt)
        {
            if (!handlers.TryGetValue(typeof(T), out var snapshot)) return;
            ExceptionDispatchInfo first = null;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { ((Action<T>)snapshot[i])(evt); }
                catch (Exception ex)
                {
                    var sink = HandlerErrorSink;
                    if (sink != null) sink(ex);
                    else if (first == null) first = ExceptionDispatchInfo.Capture(ex);
                }
            }
            first?.Throw();
        }

        public int SubscriberCount<T>() => handlers.TryGetValue(typeof(T), out var h) ? h.Length : 0;

        public void Clear() => handlers.Clear();

        sealed class Subscription<T> : IDisposable
        {
            EventBus bus;
            Action<T> handler;
            public Subscription(EventBus bus, Action<T> handler) { this.bus = bus; this.handler = handler; }
            public void Dispose()
            {
                if (bus == null) return;
                bus.Unsubscribe(handler);
                bus = null; handler = null;
            }
        }
    }

    /// <summary>
    /// Collects subscription tokens so a component can drop them all at once:
    /// <code>
    /// readonly Subscriptions subs = new Subscriptions();
    /// void OnEnable()  { subs.Add(GameServices.Events.Subscribe&lt;DoorsOpened&gt;(OnDoorsOpened)); }
    /// void OnDisable() { subs.Clear(); }
    /// </code>
    /// </summary>
    public sealed class Subscriptions : IDisposable
    {
        readonly List<IDisposable> tokens = new List<IDisposable>();

        public int Count => tokens.Count;

        public void Add(IDisposable token)
        {
            if (token != null) tokens.Add(token);
        }

        /// <summary>Dispose every token (reusable afterwards).</summary>
        public void Clear()
        {
            for (int i = tokens.Count - 1; i >= 0; i--) tokens[i].Dispose();
            tokens.Clear();
        }

        public void Dispose() => Clear();
    }
}
