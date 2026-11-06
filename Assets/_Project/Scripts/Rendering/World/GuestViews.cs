using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Blocky guest proxies (<see cref="GuestFigure"/>: silhouette + accessory per guests.json, vivid colour) that follow
    /// the gameplay positions and pose from IGuestQuery mood (breathing bob, impatient stomping, angry hopping, scared
    /// trembling) plus a startled jump on unlucky judgements.
    /// Created on GuestSpawned (or when IGuestQuery lists a guest we have not seen), removed on GuestLeft /
    /// GuestDelivered (or when IGuestQuery stops listing it).
    /// Position source, best first: IActorLocator, IGuestQuery state (waiting slot / car slot), spawn slot.
    /// </summary>
    sealed class GuestViews
    {
        const float FollowSpeed = 14f;

        sealed class Proxy
        {
            public GuestId Id;
            public GuestFigure Figure;
            public FloorIndex SpawnFloor;
            public int SpawnSlot;
            public int SeenFrame;
            public bool Placed;
            public float Seed;
            public float JumpAge = float.MaxValue;
            public GuestMood Mood;
            public Transform Root => Figure.Root;
        }

        sealed class Extra
        {
            public GuestFigure Figure;
            public GuestMood Mood;
            public float Seed;
        }

        readonly Transform root;
        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly float pixel;
        readonly Dictionary<GuestId, Proxy> proxies = new Dictionary<GuestId, Proxy>();
        readonly List<Extra> extras = new List<Extra>();
        readonly List<GuestId> scratch = new List<GuestId>();
        readonly List<GuestId> waitingScratch = new List<GuestId>();
        int frame;

        public int Count => proxies.Count;
        public bool Contains(GuestId id) => proxies.ContainsKey(id);
        public IEnumerable<GuestId> Ids => proxies.Keys;
        public Transform Get(GuestId id) => proxies.TryGetValue(id, out var p) ? p.Root : null;
        public GuestFigure GetFigure(GuestId id) => proxies.TryGetValue(id, out var p) ? p.Figure : null;
        public bool IsJumping(GuestId id) => proxies.TryGetValue(id, out var p) && p.JumpAge < GuestPose.JumpSeconds;
        public GuestMood GetMood(GuestId id) => proxies.TryGetValue(id, out var p) ? p.Mood : GuestMood.Neutral;

        public GuestViews(Transform parent, RenderAssets assets, IWorldLayout layout, float pixelSize)
        {
            this.assets = assets;
            this.layout = layout;
            pixel = pixelSize;
            root = new GameObject("Guests").transform;
            root.SetParent(parent, false);
        }

        static void Describe(GameData data, GuestArchetypeId archetype, out Color color, out string accessory)
        {
            color = Color.white;
            accessory = "";
            if (data != null && data.TryGetGuest(archetype, out var def))
            {
                color = CoreUnityExtensions.ParseColor(def.color);
                accessory = def.accessory ?? "";
            }
        }

        public void Spawn(GuestId id, GuestArchetypeId archetype, FloorIndex floor, int slot, GameData data)
        {
            if (id.IsNone || proxies.ContainsKey(id)) return;
            Describe(data, archetype, out var color, out var accessory);
            var fig = GuestFigure.Build(root, assets, "Guest " + id.Value + " " + archetype.Value, color, accessory);
            var p = new Proxy
            {
                Id = id, Figure = fig, SpawnFloor = floor, SpawnSlot = Mathf.Max(0, slot), SeenFrame = frame,
                Seed = Mathf.Repeat(id.Value * 0.618034f, 1f),
            };
            proxies.Add(id, p);
            p.Root.localPosition = layout.GetWaitingSlot(floor, p.SpawnSlot).ToVector3();
        }

        public void Remove(GuestId id)
        {
            if (!proxies.TryGetValue(id, out var p)) return;
            proxies.Remove(id);
            if (p.Root != null) Object.Destroy(p.Root.gameObject);
        }

        /// <summary>Startled leap (unlucky number). Restarts if already jumping.</summary>
        public void Jump(GuestId id)
        {
            if (proxies.TryGetValue(id, out var p)) p.JumpAge = 0f;
        }

        /// <summary>World position just above the guest's head (sparkles, popups).</summary>
        public bool TryGetHead(GuestId id, out Vector3 world)
        {
            world = default;
            if (!proxies.TryGetValue(id, out var p) || p.Root == null) return false;
            world = p.Root.position + Vector3.up * (p.Figure.HeadTop + 0.1f);
            return true;
        }

        /// <summary>
        /// Presentation-only figures (not gameplay guests): one per definition, standing in the waiting slots of
        /// <paramref name="floor"/> with a fixed mood. Used by the capture showcase / silhouette tests.
        /// </summary>
        public IReadOnlyList<GuestFigure> BuildLineup(FloorIndex floor, IReadOnlyList<GuestDef> defs, GuestMood mood)
        {
            var result = new List<GuestFigure>();
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var fig = GuestFigure.Build(root, assets, "Lineup " + floor.PhysicalNumber + " " + def.id,
                    CoreUnityExtensions.ParseColor(def.color), def.accessory);
                fig.Root.localPosition = layout.GetWaitingSlot(floor, i).ToVector3();
                extras.Add(new Extra { Figure = fig, Mood = mood, Seed = i * 0.37f + floor.Value * 0.11f });
                result.Add(fig);
            }
            return result;
        }

        public void ClearLineups()
        {
            foreach (var e in extras) if (e.Figure.Root != null) Object.Destroy(e.Figure.Root.gameObject);
            extras.Clear();
        }

        /// <summary>
        /// Per frame: reconcile with IGuestQuery (if present), move proxies toward their gameplay positions (real
        /// <paramref name="dt"/>), pose them on the juice clock (<paramref name="juiceDt"/>/<paramref name="juiceTime"/> stop while paused).
        /// </summary>
        public void Tick(float dt, float juiceDt, float juiceTime, GameData data, IGuestQuery guestQuery, IActorLocator locator, IElevatorQuery elevator)
        {
            frame++;
            if (guestQuery != null)
            {
                var active = guestQuery.ActiveGuests;
                for (int i = 0; i < active.Count; i++)
                {
                    var id = active[i];
                    if (!proxies.TryGetValue(id, out var p))
                    {
                        if (!guestQuery.TryGet(id, out var info)) continue;
                        Spawn(id, info.Archetype, info.Floor, SlotOf(guestQuery, info.Floor, id), data);
                        p = proxies[id];
                    }
                    p.SeenFrame = frame;
                }
                scratch.Clear();
                foreach (var kv in proxies) if (kv.Value.SeenFrame != frame) scratch.Add(kv.Key);
                for (int i = 0; i < scratch.Count; i++) Remove(scratch[i]);
            }

            float k = 1f - Mathf.Exp(-FollowSpeed * dt);
            foreach (var kv in proxies)
            {
                var p = kv.Value;
                var target = TargetOf(p, guestQuery, locator, elevator);
                if (!p.Placed) { p.Root.localPosition = target; p.Placed = true; }
                else p.Root.localPosition = Vector3.Lerp(p.Root.localPosition, target, k);

                if (guestQuery != null && guestQuery.TryGet(p.Id, out var gi)) p.Mood = gi.Mood;
                if (p.JumpAge < GuestPose.JumpSeconds) p.JumpAge += juiceDt;
                p.Figure.Apply(GuestPose.Evaluate(p.Mood, juiceTime, p.Seed, p.JumpAge, pixel));
            }
            for (int i = 0; i < extras.Count; i++)
            {
                var e = extras[i];
                if (e.Figure.Root != null) e.Figure.Apply(GuestPose.Evaluate(e.Mood, juiceTime, e.Seed, float.MaxValue, pixel));
            }
        }

        int SlotOf(IGuestQuery q, FloorIndex floor, GuestId id)
        {
            waitingScratch.Clear();
            q.GetWaiting(floor, waitingScratch);
            int idx = waitingScratch.IndexOf(id);
            return idx < 0 ? 0 : idx;
        }

        Vector3 TargetOf(Proxy p, IGuestQuery q, IActorLocator locator, IElevatorQuery elevator)
        {
            if (locator != null && locator.TryGetGuestPosition(p.Id, out var wp)) return wp.ToVector3();
            if (q != null && q.TryGet(p.Id, out var info))
            {
                if (info.State == GuestState.Riding && elevator != null)
                {
                    int idx = IndexOf(elevator.Riders, p.Id);
                    var car = layout.GetCarPosition(elevator.Position).ToVector3();
                    return car + layout.GetCarSlotOffset(Mathf.Max(0, idx)).ToVector3();
                }
                if (info.State == GuestState.Waiting)
                    return layout.GetWaitingSlot(info.Floor, SlotOf(q, info.Floor, p.Id)).ToVector3();
                return p.Root.localPosition; // boarding/exiting without a locator: hold
            }
            return layout.GetWaitingSlot(p.SpawnFloor, p.SpawnSlot).ToVector3();
        }

        static int IndexOf(IReadOnlyList<GuestId> list, GuestId id)
        {
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
            return -1;
        }
    }
}
