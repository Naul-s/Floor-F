using FloorF.Core;
using FloorF.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorF.Rendering
{
    /// <summary>
    /// Rendering module root (one per Main scene load, see RenderingBootstrap).
    ///   Awake    - pixel pipeline: re-targets the scene's "Main Camera" to the 640x360 RT, creates the UI camera +
    ///              Screen Space - Camera canvas (exists from Awake, so other modules can find the UI camera in Start).
    ///   OnEnable - subscribes Core events.
    ///   Start    - resolves IWorldLayout/GameData/IBuildingQuery, builds the greybox, frames the camera.
    ///   LateUpdate - follows elevator/guests (services polled with TryGet: gameplay may add them later), lighting.
    /// Everything degrades gracefully: no gameplay services = initial labels from GameData, car parked at floor 1.
    /// </summary>
    sealed class RenderingRoot : MonoBehaviour
    {
        readonly Subscriptions subs = new Subscriptions();

        RenderAssets assets;
        PixelPipeline pipeline;
        FloorLighting lighting;
        BuildingView building;
        ElevatorView elevator;
        GuestViews guests;
        JuiceFx fx;
        CameraRig rig;
        EndingDirector ending;
        PaletteAtlas palette;
        LivingLights livingLights;
        CityView city;
        HotelFacade facade;
        StreetLife street;
        readonly float[] letterLevels = new float[BuildingView.HotelText.Length];
        Transform world;

        IWorldLayout layout;
        GameData data;
        IBuildingQuery buildingQuery;
        IElevatorQuery elevatorQuery;
        IGuestQuery guestQuery;
        IActorLocator locator;
        IDayQuery dayQuery;
        IMenuFlow menuFlow;
        bool labelsSynced;

        // juice clock: stops while the game is paused (PauseChanged / IMenuFlow.IsPaused)
        bool eventPaused;
        float juiceTime;

        // event-driven fallbacks when IElevatorQuery is not registered
        int eventCarFloor;
        bool eventDoorsOpen;

        internal PixelPipeline Pipeline => pipeline;
        internal FloorLighting Lighting => lighting;
        internal BuildingView Building => building;
        internal ElevatorView Elevator => elevator;
        internal GuestViews Guests => guests;
        internal JuiceFx Fx => fx;
        internal CameraRig Rig => rig;
        internal EndingDirector Ending => ending;
        internal bool IsPaused => menuFlow != null ? menuFlow.IsPaused || eventPaused : eventPaused;
        internal float JuiceTime => juiceTime;
        internal bool IsBuilt => building != null;
        internal PaletteAtlas Palette => palette;
        internal LivingLights LivingLights => livingLights;
        internal CityView City => city;
        internal HotelFacade Facade => facade;
        internal StreetLife Street => street;
        /// <summary>Seconds of street/city time before the first frame (busy street, lights already mixed).</summary>
        public const float StreetPrewarmSeconds = 60f;

        void Awake()
        {
            assets = new RenderAssets();
            lighting = new FloorLighting();
            pipeline = PixelPipeline.Create(transform, FindSceneCamera());
            world = new GameObject(RenderingNames.World).transform;
            world.SetParent(transform, false);
        }

        Camera FindSceneCamera()
        {
            var main = Camera.main;
            if (main != null && main.gameObject.scene == gameObject.scene) return main;
            foreach (var go in gameObject.scene.GetRootGameObjects())
                if (go.name == "Main Camera" && go.TryGetComponent<Camera>(out var c)) return c;
            return main;
        }

        void OnEnable()
        {
            var bus = GameServices.Events;
            subs.Add(bus.Subscribe<FloorLabelChanged>(OnFloorLabelChanged));
            subs.Add(bus.Subscribe<DoorsOpened>(OnDoorsOpened));
            subs.Add(bus.Subscribe<DoorsClosed>(OnDoorsClosed));
            subs.Add(bus.Subscribe<ElevatorArrived>(OnElevatorArrived));
            subs.Add(bus.Subscribe<LuckJudged>(OnLuckJudged));
            subs.Add(bus.Subscribe<GuestSpawned>(OnGuestSpawned));
            subs.Add(bus.Subscribe<GuestLeft>(OnGuestLeft));
            subs.Add(bus.Subscribe<GuestDelivered>(OnGuestDelivered));
            subs.Add(bus.Subscribe<PauseChanged>(e => eventPaused = e.Paused));
            subs.Add(bus.Subscribe<StickerApplied>(OnStickerApplied));
            subs.Add(bus.Subscribe<EndingReached>(OnEndingReached));
            subs.Add(bus.Subscribe<GameStarted>(e => ResetEnding()));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnDayPhaseChanged));
        }

        void OnDisable() => subs.Clear();

        void Start()
        {
            GameServices.TryGet(out layout);
            GameServices.TryGet(out data);
            if (layout == null)
            {
                Debug.LogWarning("[Rendering] no IWorldLayout registered - using default HotelLayout (9 floors)");
                layout = data != null ? HotelLayout.From(data.Hotel) : new HotelLayout(new HotelLayoutDef(), 9, 8);
            }
            ResolveQueries();
            SetupScene();
            Build();
        }

        void ResolveQueries()
        {
            if (buildingQuery == null) GameServices.TryGet(out buildingQuery);
            if (elevatorQuery == null) GameServices.TryGet(out elevatorQuery);
            if (guestQuery == null) GameServices.TryGet(out guestQuery);
            if (locator == null) GameServices.TryGet(out locator);
            if (dayQuery == null) GameServices.TryGet(out dayQuery);
            if (menuFlow == null) GameServices.TryGet(out menuFlow);
        }

        /// <summary>Night mood: dim moonlight from the scene's directional light, flat dark ambient.</summary>
        void SetupScene()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = RenderingStyle.Ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(RenderingStyle.Ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = false;
            foreach (var go in gameObject.scene.GetRootGameObjects())
            {
                if (!go.TryGetComponent<Light>(out var l) || l.type != LightType.Directional) continue;
                l.color = RenderingStyle.MoonColor;
                l.intensity = RenderingStyle.MoonIntensity;
                l.shadows = LightShadows.Hard;
                l.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            }
        }

        void Build()
        {
            var frame = FitGameplayFrame(layout);
            rig = new CameraRig(pipeline, frame, RenderingStyle.PixelHeight);

            building = new BuildingView(world, assets, layout, lighting, frame.PixelsPerUnit);
            building.Build(DestinationOf, CurrentLabel, IsRelabelled);
            for (int i = 0; i < layout.FloorCount; i++) UpdateFlicker(new FloorIndex(i));
            labelsSynced = buildingQuery != null;

            float doorSeconds = data != null ? data.Rules.doorSeconds : 0.35f;
            elevator = new ElevatorView(world, assets, layout, building, doorSeconds);
            guests = new GuestViews(world, assets, layout, 1f / frame.PixelsPerUnit);
            fx = new JuiceFx(world, assets);
            ending = new EndingDirector(lighting, building, elevator, fx, OriginalLabel);

            // exterior: one palette (city + facade lights + cars), living lights, street life
            float z0 = -layout.Depth * 0.5f;
            palette = new PaletteAtlas(assets, "City Palette");
            livingLights = new LivingLights(palette, RenderingStyle.CitySeed);
            city = new CityView(world, palette, livingLights, RenderingStyle.CameraShear, z0);
            city.Build(z0 - 0.15f);
            facade = new HotelFacade(world, assets, layout, building, palette, livingLights);
            facade.Build();
            street = new StreetLife(world, assets, palette, RenderingStyle.CameraShear, z0, RenderingStyle.CitySeed);
            street.Build();
            street.Prewarm(StreetPrewarmSeconds, frame.Grid);
            TickExterior(0f);
        }

        /// <summary>
        /// Gameplay frame: whole pixels per unit from the hotel's screen rectangle (plinth top to HOTEL sign), top edge a
        /// little above the sign; the remaining slack shows the street below floor 1.
        /// </summary>
        internal static CameraFrame FitGameplayFrame(IWorldLayout layout)
        {
            float aspect = (float)RenderingStyle.PixelWidth / RenderingStyle.PixelHeight;
            int h = RenderingStyle.PixelHeight;
            float z0 = -layout.Depth * 0.5f;
            var frame = CameraFraming.FitOblique(BuildingView.FramingRect(layout), RenderingStyle.CameraShear, z0, RenderingStyle.CurbZ - 4f,
                aspect, RenderingStyle.CameraMargin, RenderingStyle.CameraDistance, h);
            frame = CameraFraming.SnapToWholePixels(frame, h);
            return CameraFraming.AnchorTop(frame, BuildingView.HotelSignTop(layout, frame.PixelsPerUnit) + RenderingStyle.CameraTopHeadroom, h);
        }

        /// <summary>City windows, facade lamps, HOTEL letters (juice clock + prewarm offset so the city is mixed at boot).</summary>
        void TickExterior(float time)
        {
            float t = time + StreetPrewarmSeconds;
            livingLights.Tick(t);
            for (int i = 0; i < letterLevels.Length; i++)
                letterLevels[i] = LightSchedule.SignLetterLevel(RenderingStyle.CitySeed, i, letterLevels.Length, t);
            building.SetHotelLetterLevels(letterLevels);
            palette.Flush();
        }

        DestinationId DestinationOf(FloorIndex f)
        {
            if (buildingQuery != null) return buildingQuery.GetDestination(f);
            if (data != null && f.Value < data.FloorCount) return data.GetFloor(f).Destination;
            return default;
        }

        FloorLabel CurrentLabel(FloorIndex f)
        {
            if (buildingQuery != null) return buildingQuery.GetLabel(f);
            if (data != null && f.Value < data.FloorCount) return data.GetFloor(f).InitialLabel;
            return FloorLabel.Original(f);
        }

        FloorLabel OriginalLabel(FloorIndex f)
        {
            if (buildingQuery != null) return buildingQuery.GetOriginalLabel(f);
            if (data != null && f.Value < data.FloorCount) return data.GetFloor(f).InitialLabel;
            return FloorLabel.Original(f);
        }

        bool IsRelabelled(FloorIndex f) => buildingQuery != null ? buildingQuery.IsRelabelled(f) : CurrentLabel(f) != OriginalLabel(f);

        void UpdateFlicker(FloorIndex f) => lighting.SetFlicker(f, building.GetSignText(f) == RenderingStyle.FlickerLabel);

        void LateUpdate()
        {
            if (!IsBuilt) return;
            ResolveQueries();
            if (!labelsSynced && buildingQuery != null)
            {
                // gameplay registered its building after we built: adopt its current labels once
                labelsSynced = true;
                for (int i = 0; i < building.FloorCount; i++)
                {
                    var f = new FloorIndex(i);
                    building.SetLabel(f, CurrentLabel(f), IsRelabelled(f));
                    UpdateFlicker(f);
                }
            }
            float dt = Time.deltaTime;
            float jdt = IsPaused ? 0f : dt;
            juiceTime += jdt;
            elevator.Tick(dt, jdt, locator, elevatorQuery, eventCarFloor, eventDoorsOpen);
            guests.Tick(dt, jdt, juiceTime, data, guestQuery, locator, elevatorQuery);
            building.Tick(jdt);
            fx.Tick(jdt);
            ending.Tick(jdt);
            lighting.Tick(juiceTime, jdt);
            rig.SetTitle(dayQuery != null && dayQuery.Phase == DayPhase.Title);
            rig.Tick(jdt);
            var grid = rig.Current.Grid;
            building.ApplyPixelGrid(grid);
            street.Tick(jdt, grid);
            TickExterior(juiceTime);
        }

        void ResetEnding()
        {
            if (!IsBuilt || ending.Active == EndingKind.None) return;
            ending.Reset();
            for (int i = 0; i < building.FloorCount; i++) UpdateFlicker(new FloorIndex(i));
        }

        void OnDayPhaseChanged(DayPhaseChanged e)
        {
            if (e.Previous == DayPhase.Ending || e.Current == DayPhase.Title) ResetEnding();
        }

        void OnEndingReached(EndingReached e)
        {
            if (IsBuilt) ending.Begin(e.Ending);
        }

        void OnStickerApplied(StickerApplied e)
        {
            if (!IsBuilt) return;
            building.PlayStick(e.Floor);
            var sign = building.GetSign(e.Floor);
            if (sign != null) fx.Burst(sign.transform.position + new Vector3(0f, 0f, -0.2f), new Color(1f, 0.97f, 0.8f), 8, 2.4f, 0.13f);
        }

        // ------------------------------------------------------------------ events

        void OnFloorLabelChanged(FloorLabelChanged e)
        {
            if (!IsBuilt) return;
            bool sticker = buildingQuery != null ? buildingQuery.IsRelabelled(e.Floor) : e.Current != OriginalLabel(e.Floor);
            building.SetLabel(e.Floor, e.Current, sticker);
            UpdateFlicker(e.Floor);
        }

        void OnElevatorArrived(ElevatorArrived e)
        {
            eventCarFloor = e.Floor.Value;
            if (IsBuilt) elevator.Bump(1f);
        }

        void OnDoorsOpened(DoorsOpened e)
        {
            eventCarFloor = e.Floor.Value;
            eventDoorsOpen = true;
            if (IsBuilt) lighting.Flash(e.Floor, Color.white, 0.35f, 0.35f); // small "ding" brightening
        }

        void OnDoorsClosed(DoorsClosed e)
        {
            eventDoorsOpen = false;
            if (IsBuilt) elevator.Bump(0.35f);
        }

        void OnLuckJudged(LuckJudged e)
        {
            if (!IsBuilt) return;
            if (e.Verdict == LuckVerdict.Unlucky)
            {
                lighting.Flash(e.Floor, RenderingStyle.UnluckyFlash, RenderingStyle.UnluckyFlashSeconds);
                rig.Shake();
                guests.Jump(e.Guest);
            }
            else if (e.Verdict == LuckVerdict.Lucky)
            {
                lighting.Flash(e.Floor, RenderingStyle.LuckyFlash, RenderingStyle.LuckyFlashSeconds, 0.7f);
                if (!guests.TryGetHead(e.Guest, out var at))
                    at = world.TransformPoint(layout.GetFloorAnchor(e.Floor).ToVector3() + new Vector3(0f, layout.FloorHeight * 0.6f, 0f));
                fx.Burst(at, RenderingStyle.LuckyFlash, 14);
            }
        }

        void OnGuestSpawned(GuestSpawned e)
        {
            if (IsBuilt) guests.Spawn(e.Guest, e.Archetype, e.Floor, e.WaitingSlot, data);
        }

        void OnGuestLeft(GuestLeft e)
        {
            if (IsBuilt) guests.Remove(e.Guest);
        }

        void OnGuestDelivered(GuestDelivered e)
        {
            if (IsBuilt) guests.Remove(e.Guest);
        }

        void OnDestroy()
        {
            subs.Clear();
            pipeline?.Dispose();
            assets?.Dispose();
        }
    }
}
