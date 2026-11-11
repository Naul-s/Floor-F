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
        public IWorldLayout Layout;
        /// <summary>Endings seen across sessions (UI-only record, PlayerPrefs). Set by UIRoot.Start.</summary>
        public EndingCollection Endings;
        /// <summary>This run's graded real days (ending card recap). Fed by UIRoot from DayEnded, cleared on GameStarted.</summary>
        public readonly RunJourney Journey = new RunJourney();
        IMenuFlow menu;

        /// <summary>Title + pause (Gameplay). Resolved lazily: null until Gameplay registers it.</summary>
        public IMenuFlow Menu
        {
            get
            {
                if (menu == null) GameServices.TryGet(out menu);
                return menu;
            }
        }

        ISeasonQuery season;
        ISeasonFlow seasonFlow;
        IRunHistory history;
        /// <summary>Test hook: fakes win over GameServices (null = resolve normally).</summary>
        internal ISeasonQuery SeasonOverride;
        internal ISeasonFlow SeasonFlowOverride;
        internal IRunHistory HistoryOverride;

        /// <summary>Winter season / lottery / Christmas (Gameplay). Lazy: null until registered - the UI then behaves as before.</summary>
        public ISeasonQuery Season
        {
            get
            {
                if (SeasonOverride != null) return SeasonOverride;
                if (season == null) GameServices.TryGet(out season);
                return season;
            }
        }

        /// <summary>Ending card "Keep the hotel open" (Gameplay). Lazy, null until registered.</summary>
        public ISeasonFlow SeasonFlow
        {
            get
            {
                if (SeasonFlowOverride != null) return SeasonFlowOverride;
                if (seasonFlow == null) GameServices.TryGet(out seasonFlow);
                return seasonFlow;
            }
        }

        /// <summary>Finished runs (Gameplay, saved). Lazy, null until registered.</summary>
        public IRunHistory History
        {
            get
            {
                if (HistoryOverride != null) return HistoryOverride;
                if (history == null) GameServices.TryGet(out history);
                return history;
            }
        }

        bool Real => Day != null && !Day.IsTutorial;
        public bool IsLotteryNight => Real && Season != null && Season.IsLotteryNight;
        public int LotteryNumber => IsLotteryNight ? Season.LotteryNumber : 0;
        public bool IsChristmas => Real && Season != null && Season.IsChristmas;
        public bool IsExtendedDay => Real && Season != null && Season.IsExtendedDay;
        public float Snow => Season != null ? Mathf.Clamp01(Season.SnowAmount) : 0f;
        /// <summary>Number lucky for every guest today: Christmas Eve's (ISeasonQuery.LuckyForAll) or star night's 7; 0 = none.</summary>
        public int LuckyForAll => Day == null ? 0 : UIFormat.LuckyForAll(Day.IsTutorial, Day.IsStarNight, Season != null ? Season.LuckyForAll : 0);
        public bool CanExtendSeason => Phase == DayPhase.Ending && SeasonFlow != null && SeasonFlow.CanExtendSeason;

        public DayPhase Phase => Day != null ? Day.Phase : DayPhase.None;
        public bool Paused => Menu != null && Menu.IsPaused;

        public RectTransform CanvasRect;
        Camera worldCamera;

        /// <summary>Language comes only from IGameSettings (Gameplay); without it the UI stays English.</summary>
        public bool Korean => Settings != null && Settings.UseKorean;

        public bool HasGame => Building != null && Elevator != null && Day != null;

        /// <summary>What the fonts are prewarmed with: printable ASCII, every UI string (en + ko) and the destination
        /// names (panel badges are measured at build time). Guest lines / sticker texts come later and are measured then.</summary>
        public string PrewarmText()
        {
            var sb = new System.Text.StringBuilder(UIStrings.Characters);
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            if (Data != null)
                foreach (var d in Data.Destinations)
                {
                    if (d == null) continue;
                    sb.Append(d.name).Append(d.nameKo);
                    sb.Append(UIFormat.DestinationShortName(d.id, false)).Append(UIFormat.DestinationShortName(d.id, true));
                }
            return sb.ToString();
        }

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
            GameServices.TryGet(out Layout);
            GameServices.TryGet(out menu);
            GameServices.TryGet(out season);
            GameServices.TryGet(out seasonFlow);
            GameServices.TryGet(out history);
        }

        /// <summary>
        /// Interface feedback sound (Audio subscribes): exactly one per player click, published by the handler that
        /// decides the outcome. Click = plain press, Confirm = START/CONTINUE/NEXT DAY/PLAY AGAIN/sticker, Error = refused.
        /// </summary>
        public void Sound(UiSoundKind kind) => GameServices.Events.Publish(new UiSound(kind));

        /// <summary>Command result -> Click when accepted, Error when the command returned false.</summary>
        public bool Sound(bool accepted)
        {
            Sound(accepted ? UiSoundKind.Click : UiSoundKind.Error);
            return accepted;
        }

        /// <summary>Flips IGameSettings.UseKorean (publishes SettingsChanged -> UIRoot refreshes). False when no service.</summary>
        public bool ToggleLanguage()
        {
            if (Settings == null && GameServices.TryGet(out IGameSettings s)) Settings = s;
            if (Settings == null)
            {
                Debug.LogWarning("[UI] no IGameSettings registered - language toggle ignored");
                Sound(UiSoundKind.Error);
                return false;
            }
            Settings.UseKorean = !Settings.UseKorean;
            Sound(UiSoundKind.Click);
            return true;
        }

        public float CarWidth => Data != null ? Data.Layout.carWidth : 2.4f;

        /// <summary>
        /// Canvas x-range of the elevator shaft column (IWorldLayout.ShaftX ± carWidth/2 projected at the car's height),
        /// widened by <paramref name="margin"/>. False when nothing can be projected.
        /// </summary>
        public bool ShaftColumn(float margin, out float minX, out float maxX)
        {
            minX = maxX = 0f;
            float shaftX = Layout != null ? Layout.ShaftX : Data != null ? Data.Layout.shaftX : 0f;
            var car = Locator != null ? ToVector3(Locator.CarPosition) : new Vector3(shaftX, 0f, 0f);
            float half = CarWidth * 0.5f;
            if (!WorldToCanvas(new Vector3(shaftX - half, car.y, car.z), out var a)) return false;
            if (!WorldToCanvas(new Vector3(shaftX + half, car.y, car.z), out var b)) return false;
            minX = Mathf.Min(a.x, b.x) - margin;
            maxX = Mathf.Max(a.x, b.x) + margin;
            return true;
        }

        // Rendering's floor-sign geometry (BuildingView: plate left of the shaft posts, 5x7 PixelFont glyphs, 2 screen
        // pixels per texel). Duplicated here because presentation modules may not reference each other; the PlayMode test
        // Bubbles_AvoidSignColumn checks the result against the real sign renderers.
        const float ShaftPostPad = 0.25f, SignGap = 0.35f, SignSafety = 0.2f;
        const int GlyphAdvance = 6, SignPadTexels = 2, SignPixelsPerTexel = 2;

        /// <summary>World width of the widest floor sign (labels can grow with stickers).</summary>
        public float SignWidth()
        {
            int len = 1;
            if (Building != null)
                for (int f = 0; f < Building.FloorCount; f++)
                    len = Mathf.Max(len, Building.GetLabel(new FloorIndex(f)).Text?.Length ?? 1);
            if (worldCamera == null || !worldCamera.isActiveAndEnabled) worldCamera = Camera.main;
            float ppu = worldCamera != null && worldCamera.orthographic
                ? Mathf.Max(1f, Mathf.Floor(PresentationContract.PixelHeight / (2f * worldCamera.orthographicSize)))
                : 12f;
            return (len * GlyphAdvance - 1 + SignPadTexels) * SignPixelsPerTexel / ppu;
        }

        /// <summary>
        /// Canvas x-range of the building's centre column = floor-sign plates (left of the shaft) + the shaft itself
        /// (posts included), projected at the car's height and widened by <paramref name="margin"/>.
        /// Nothing UI-drawn should sit in it: the player reads the floor numbers there.
        /// </summary>
        public bool CenterColumn(float margin, out float minX, out float maxX)
        {
            minX = maxX = 0f;
            float shaftX = Layout != null ? Layout.ShaftX : Data != null ? Data.Layout.shaftX : 0f;
            var car = Locator != null ? ToVector3(Locator.CarPosition) : new Vector3(shaftX, 0f, 0f);
            float shaftHalf = CarWidth * 0.5f + ShaftPostPad + 0.1f;
            float left = shaftX - CarWidth * 0.5f - ShaftPostPad - SignGap - SignWidth() - SignSafety;
            if (!WorldToCanvas(new Vector3(left, car.y, car.z), out var a)) return false;
            if (!WorldToCanvas(new Vector3(shaftX + shaftHalf, car.y, car.z), out var b)) return false;
            minX = Mathf.Min(a.x, b.x) - margin;
            maxX = Mathf.Max(a.x, b.x) + margin;
            return true;
        }

        /// <summary>
        /// Canvas x-range of the floor-sign plates only (left part of <see cref="CenterColumn"/>, up to the shaft posts),
        /// widened by <paramref name="margin"/>. Luck popups sit over the shaft but never on this.
        /// </summary>
        public bool SignColumn(float margin, out float minX, out float maxX)
        {
            minX = maxX = 0f;
            float shaftX = Layout != null ? Layout.ShaftX : Data != null ? Data.Layout.shaftX : 0f;
            var car = Locator != null ? ToVector3(Locator.CarPosition) : new Vector3(shaftX, 0f, 0f);
            float right = shaftX - CarWidth * 0.5f - ShaftPostPad - SignGap + SignSafety;
            float left = right - SignWidth() - 2f * SignSafety;
            if (!WorldToCanvas(new Vector3(left, car.y, car.z), out var a)) return false;
            if (!WorldToCanvas(new Vector3(right, car.y, car.z), out var b)) return false;
            minX = Mathf.Min(a.x, b.x) - margin;
            maxX = Mathf.Max(a.x, b.x) + margin;
            return true;
        }

        public string S(Loc l) => l.Get(Korean);

        /// <summary>string.Format of the current-language template; Korean particle specifiers ("{0:은}") resolved.</summary>
        public string F(Loc l, params object[] args) => KoreanParticles.Format(l.Get(Korean), args);

        // ------------------------------------------------------------------ data text

        public string DestinationName(DestinationId id)
        {
            if (Data != null && !id.IsEmpty && Data.TryGetDestination(id, out var d))
                return Korean && !string.IsNullOrEmpty(d.nameKo) ? d.nameKo : d.name;
            return "???";
        }

        /// <summary>Curated short name for tight badges ("Rooms", "객실"), or null for an id without one.</summary>
        public string DestinationShortName(DestinationId id) => id.IsEmpty ? null : UIFormat.DestinationShortName(id.Value, Korean);

        public DestinationDef Destination(DestinationId id) =>
            Data != null && !id.IsEmpty && Data.TryGetDestination(id, out var d) ? d : null;

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
