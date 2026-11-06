using FloorF.Core;
using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// What every view needs: services (resolved in UIRoot.Start), data, language, and world -> canvas mapping.
    /// Read-only access to Gameplay except the four command services named by the brief.
    /// </summary>
    sealed class UIContext
    {
        public GameData Data;
        public IBuildingQuery Building;
        public IElevatorQuery Elevator;
        public IElevatorControl Control;
        public IGuestQuery Guests;
        public IScoreQuery Score;
        public IDayQuery Day;
        public IGameFlow Flow;
        public IShopService Shop;
        public IActorLocator Locator;
        public IGameSettings Settings;

        public RectTransform CanvasRect;
        Camera worldCamera;

        /// <summary>Used when no IGameSettings is registered (the toggle still works locally).</summary>
        bool localKorean;

        public bool Korean => Settings != null ? Settings.UseKorean : localKorean;

        public bool HasGame => Building != null && Elevator != null && Day != null;

        public void ResolveServices()
        {
            GameServices.TryGet(out Data);
            GameServices.TryGet(out Building);
            GameServices.TryGet(out Elevator);
            GameServices.TryGet(out Control);
            GameServices.TryGet(out Guests);
            GameServices.TryGet(out Score);
            GameServices.TryGet(out Day);
            GameServices.TryGet(out Flow);
            GameServices.TryGet(out Shop);
            GameServices.TryGet(out Locator);
            GameServices.TryGet(out Settings);
        }

        /// <summary>Returns true when the language flipped locally (no settings service -> caller refreshes itself).</summary>
        public bool ToggleLanguage()
        {
            if (Settings == null && GameServices.TryGet(out IGameSettings s)) Settings = s;
            if (Settings != null)
            {
                Settings.UseKorean = !Settings.UseKorean; // publishes SettingsChanged -> UIRoot refreshes
                return false;
            }
            localKorean = !localKorean;
            return true;
        }

        public string S(Loc l) => l.Get(Korean);

        // ------------------------------------------------------------------ data text

        public string DestinationName(DestinationId id)
        {
            if (Data != null && !id.IsEmpty && Data.TryGetDestination(id, out var d))
                return Korean && !string.IsNullOrEmpty(d.nameKo) ? d.nameKo : d.name;
            return "???";
        }

        public int DestinationIndex(DestinationId id)
        {
            if (Data == null || id.IsEmpty) return -1;
            var list = Data.Destinations;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
            return -1;
        }

        public Color GuestColor(GuestArchetypeId id)
        {
            if (Data != null && Data.TryGetGuest(id, out var g)) return UIStyle.Hex(g.color);
            return Color.white;
        }

        public string StickerName(StickerDef s) => s == null ? "" : Korean && !string.IsNullOrEmpty(s.nameKo) ? s.nameKo : s.name;
        public string StickerDescription(StickerDef s) => s == null ? "" : Korean && !string.IsNullOrEmpty(s.descriptionKo) ? s.descriptionKo : s.description;

        public bool TryGetDay(out DayDef def)
        {
            def = null;
            return Data != null && Day != null && Data.TryGetDay(Day.CurrentDay, out def);
        }

        // ------------------------------------------------------------------ world -> canvas

        /// <summary>
        /// World point -> canvas-local position (origin bottom-left, reference units) via
        /// Camera.main.WorldToViewportPoint and PresentationContract.ViewportToScreen (aspect-fit pixel screen).
        /// </summary>
        public bool WorldToCanvas(Vector3 world, out Vector2 pos)
        {
            pos = default;
            if (worldCamera == null || !worldCamera.isActiveAndEnabled) worldCamera = Camera.main;
            if (worldCamera == null || CanvasRect == null) return false;
            var vp = worldCamera.WorldToViewportPoint(world);
            var size = CanvasRect.rect.size;
            bool ok = PresentationContract.ViewportToScreen(vp.x, vp.y, vp.z, size.x, size.y, out float sx, out float sy);
            pos = new Vector2(sx, sy);
            return ok;
        }

        public static Vector3 ToVector3(WorldPoint p) => new Vector3(p.X, p.Y, p.Z);
    }
}
