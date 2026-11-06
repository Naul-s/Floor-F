using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Blocky guest proxies (body + head + accessory, colour from guests.json) that follow the gameplay positions.
    /// Created on GuestSpawned (or when IGuestQuery lists a guest we have not seen), removed on GuestLeft /
    /// GuestDelivered (or when IGuestQuery stops listing it).
    /// Position source, best first: IActorLocator, IGuestQuery state (waiting slot / car slot), spawn slot.
    /// </summary>
    sealed class GuestViews
    {
        const float BodyW = 0.46f, BodyH = 0.72f, BodyD = 0.36f, Head = 0.38f;
        const float FollowSpeed = 14f;

        sealed class Proxy
        {
            public GuestId Id;
            public Transform Root;
            public FloorIndex SpawnFloor;
            public int SpawnSlot;
            public int SeenFrame;
            public bool Placed;
        }

        readonly Transform root;
        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly Dictionary<GuestId, Proxy> proxies = new Dictionary<GuestId, Proxy>();
        readonly List<GuestId> scratch = new List<GuestId>();
        readonly List<GuestId> waitingScratch = new List<GuestId>();
        int frame;

        public int Count => proxies.Count;
        public bool Contains(GuestId id) => proxies.ContainsKey(id);
        public Transform Get(GuestId id) => proxies.TryGetValue(id, out var p) ? p.Root : null;

        public GuestViews(Transform parent, RenderAssets assets, IWorldLayout layout)
        {
            this.assets = assets;
            this.layout = layout;
            root = new GameObject("Guests").transform;
            root.SetParent(parent, false);
        }

        public void Spawn(GuestId id, GuestArchetypeId archetype, FloorIndex floor, int slot, GameData data)
        {
            if (id.IsNone || proxies.ContainsKey(id)) return;
            Color color = Color.white;
            string accessory = "";
            if (data != null && data.TryGetGuest(archetype, out var def))
            {
                color = CoreUnityExtensions.ParseColor(def.color);
                accessory = def.accessory ?? "";
            }
            var go = new GameObject("Guest " + id.Value + " " + archetype.Value);
            go.transform.SetParent(root, false);
            BuildBody(go.transform, color, accessory);
            var p = new Proxy { Id = id, Root = go.transform, SpawnFloor = floor, SpawnSlot = Mathf.Max(0, slot), SeenFrame = frame };
            proxies.Add(id, p);
            p.Root.localPosition = layout.GetWaitingSlot(floor, p.SpawnSlot).ToVector3();
        }

        public void Remove(GuestId id)
        {
            if (!proxies.TryGetValue(id, out var p)) return;
            proxies.Remove(id);
            if (p.Root != null) Object.Destroy(p.Root.gameObject);
        }

        void BuildBody(Transform t, Color color, string accessory)
        {
            var body = assets.Lit(color);
            var skin = assets.Lit(new Color(0.96f, 0.80f, 0.66f));
            var dark = assets.Lit(new Color(0.12f, 0.11f, 0.12f));
            assets.Box(t, "Legs", new Vector3(0f, 0.18f, 0f), new Vector3(BodyW * 0.8f, 0.36f, BodyD * 0.8f), dark);
            assets.Box(t, "Body", new Vector3(0f, 0.36f + BodyH * 0.5f - 0.1f, 0f), new Vector3(BodyW, BodyH - 0.2f, BodyD), body);
            float headY = 0.36f + BodyH - 0.2f + Head * 0.5f;
            bool hooded = accessory == "hood";
            assets.Box(t, "Head", new Vector3(0f, headY, 0f), new Vector3(Head, Head, Head), hooded ? body : skin);
            switch (accessory)
            {
                case "perm_hair":
                    assets.Box(t, "Hair", new Vector3(0f, headY + Head * 0.42f, 0.03f), new Vector3(Head * 1.25f, Head * 0.45f, Head * 1.2f), assets.Lit(new Color(0.88f, 0.86f, 0.92f)));
                    break;
                case "briefcase":
                    assets.Box(t, "Case", new Vector3(BodyW * 0.5f + 0.1f, 0.35f, 0f), new Vector3(0.12f, 0.3f, 0.38f), assets.Lit(new Color(0.28f, 0.17f, 0.10f)));
                    break;
                case "camera":
                    assets.Box(t, "Camera", new Vector3(0f, 0.36f + BodyH * 0.55f, -BodyD * 0.5f - 0.06f), new Vector3(0.24f, 0.16f, 0.12f), dark);
                    assets.Box(t, "Hat", new Vector3(0f, headY + Head * 0.55f, 0f), new Vector3(Head * 1.5f, 0.06f, Head * 1.5f), assets.Lit(new Color(0.85f, 0.78f, 0.5f)));
                    break;
                case "matching_hats":
                    assets.Box(t, "Brim", new Vector3(0f, headY + Head * 0.5f, 0f), new Vector3(Head * 1.4f, 0.05f, Head * 1.4f), assets.Lit(new Color(0.95f, 0.95f, 0.98f)));
                    assets.Box(t, "Crown", new Vector3(0f, headY + Head * 0.5f + 0.1f, 0f), new Vector3(Head * 0.9f, 0.18f, Head * 0.9f), assets.Lit(new Color(0.95f, 0.95f, 0.98f)));
                    break;
                case "hood":
                    assets.Box(t, "Hood", new Vector3(0f, headY + 0.03f, 0.03f), new Vector3(Head * 1.15f, Head * 1.15f, Head * 1.1f), body);
                    assets.Box(t, "Face", new Vector3(0f, headY - 0.02f, -Head * 0.56f), new Vector3(Head * 0.7f, Head * 0.55f, 0.02f), assets.Lit(new Color(0.02f, 0.02f, 0.03f)), false);
                    break;
            }
        }

        /// <summary>Per frame: reconcile with IGuestQuery (if present) and move proxies toward their gameplay positions.</summary>
        public void Tick(float dt, GameData data, IGuestQuery guestQuery, IActorLocator locator, IElevatorQuery elevator)
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
