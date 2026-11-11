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
        SkyView sky;
        WeatherView weather;
        WinterView winter;
        ChristmasDecor christmas;
        LotteryView lottery;
        ISeasonQuery season;
        Light moonLight;
        float appliedMood = -1f;
        readonly float[] letterLevels = new float[BuildingView.HotelText.Length];
        readonly System.Collections.Generic.Dictionary<int, GuestArchetypeId> archetypes = new System.Collections.Generic.Dictionary<int, GuestArchetypeId>();
        Transform world;

        IWorldLayout layout;
        GameData data;
        IBuildingQuery buildingQuery;
        IElevatorQuery elevatorQuery;
        IGuestQuery guestQuery;
        IActorLocator locator;
        IDayQuery dayQuery;
        IMenuFlow menuFlow;
        IGameFlow gameFlow;
        bool labelsSynced;
        bool eventHeld;

        // juice clock: stops while the game is paused (PauseChanged / IMenuFlow.IsPaused)
        bool eventPaused;
        float juiceTime;
        // exterior clock (city lights, street life, weather, sky): the juice clock, plus unscaled time while the
        // scenic view holds the game paused - the city the player stopped to look at stays alive
        float ambientTime;

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
        internal float AmbientTime => ambientTime;
        /// <summary>Tests: fixed unscaled step for the camera clock (null = Time.unscaledDeltaTime).</summary>
        internal float? CameraStepOverride { get; set; }
        internal bool IsBuilt => building != null;
        internal PaletteAtlas Palette => palette;
        internal RenderAssets Assets => assets;
        internal LivingLights LivingLights => livingLights;
        internal CityView City => city;
        internal HotelFacade Facade => facade;
        internal StreetLife Street => street;
        internal SkyView Sky => sky;
        internal WeatherView Weather => weather;
        internal WinterView Winter => winter;
        internal ChristmasDecor Christmas => christmas;
        internal LotteryView Lottery => lottery;
        /// <summary>Wish stars freeze with the simulation: menu pause or the Day 0 coach-mark hold.</summary>
        internal bool IsSimHeld => IsPaused || eventHeld || (gameFlow != null && gameFlow.TutorialHold);
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
            subs.Add(bus.Subscribe<GameStarted>(e => { ResetEnding(); if (IsBuilt) sky.ClearWishes(); }));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnDayPhaseChanged));
            subs.Add(bus.Subscribe<TutorialHoldChanged>(e => eventHeld = e.Held));
            subs.Add(bus.Subscribe<WishStarAppeared>(e => { if (IsBuilt) sky.WishAppeared(e.Id, e.FromX, e.FromY, e.ToX, e.ToY, e.Seconds); }));
            subs.Add(bus.Subscribe<WishMade>(e => { if (IsBuilt) sky.WishMade(e.Id); }));
            subs.Add(bus.Subscribe<WishStarFaded>(e => { if (IsBuilt) sky.WishFaded(e.Id); }));
            subs.Add(bus.Subscribe<ScenicViewChanged>(OnScenicViewChanged));
            subs.Add(bus.Subscribe<LotteryDrawn>(e => { if (IsBuilt) lottery.Draw(); }));
            subs.Add(bus.Subscribe<LotteryWon>(OnLotteryWon));
        }

        /// <summary>
        /// UI's scenic view / establishing shot: the rig zooms (integer-ppu ladder, unscaled clock). Title mode is
        /// refreshed first - the establishing shot can arrive in the very frame the title hands over to Day 1.
        /// </summary>
        void OnScenicViewChanged(ScenicViewChanged e)
        {
            if (rig == null) return;
            if (dayQuery == null) GameServices.TryGet(out dayQuery);
            if (dayQuery != null) rig.SetTitle(dayQuery.Phase == DayPhase.Title);
            rig.SetScenic(e.On, e.Establishing, StarNightView);
        }

        /// <summary>Star night as the sky shows it (tests may force it).</summary>
        bool StarNightView => sky != null && sky.ForceStarNight.HasValue ? sky.ForceStarNight.Value : IsStarNightNow;

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
            if (gameFlow == null) GameServices.TryGet(out gameFlow);
            // the season service may register late (or be replaced): cheap, look it up every frame
            GameServices.TryGet(out season);
        }

        /// <summary>Season as presentation sees it: nothing on the title (its attract demo is an ordinary night).</summary>
        ISeasonQuery SeasonNow => dayQuery != null && dayQuery.Phase == DayPhase.Title ? null : season;

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
                if (moonLight == null) moonLight = l;
            }
        }

        void Build()
        {
            var frame = FitGameplayFrame(layout);
            rig = new CameraRig(pipeline, frame, RenderingStyle.PixelHeight);

            building = new BuildingView(world, assets, layout, lighting, frame.PixelsPerUnit);
            building.Build(DestinationOf, CurrentLabel, IsRelabelled, OriginalLabel);
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
            city = new CityView(world, palette, livingLights, RenderingStyle.CameraShear, z0, frame.PixelsPerUnit);
            city.Build(z0 - 0.15f);
            facade = new HotelFacade(world, assets, layout, building, palette, livingLights);
            facade.Build(frame.Grid);
            assets.ApplyPatternGrid(frame.PixelsPerUnit);
            pipeline.Glow?.SetPools(LightPools());
            street = new StreetLife(world, assets, palette, RenderingStyle.CameraShear, z0, RenderingStyle.CitySeed);
            street.Build();
            street.Prewarm(StreetPrewarmSeconds, frame.Grid);
            sky = new SkyView(world, assets, palette, RenderingStyle.CitySeed);
            sky.Build();
            weather = new WeatherView(world, assets, palette, layout, RenderingStyle.CitySeed);
            weather.Build();
            var lobby = LobbyFloor();
            winter = new WinterView(world, assets, layout, building, facade, RenderingStyle.CitySeed);
            winter.Build();
            christmas = new ChristmasDecor(world, assets, layout, building, facade, winter.Atlas, lobby, RenderingStyle.CitySeed);
            lottery = new LotteryView(world, assets, layout, building, lobby);
            ending.Winter = winter;
            ending.Christmas = christmas;
            TickWeather(StreetPrewarmSeconds);
            sky.Tick(StreetPrewarmSeconds, 0f, rig.Current, IsStarNightNow);
            TickExterior(0f);
            PrewarmZoomLevels(frame);
        }

        FloorIndex LobbyFloor()
        {
            for (int i = 0; i < layout.FloorCount; i++)
                if (DestinationOf(new FloorIndex(i)).Value == "lobby") return new FloorIndex(i);
            return new FloorIndex(0);
        }

        /// <summary>
        /// Builds the city / facade meshes of every scenic zoom rung now (each whole ppu has its own mesh), so the
        /// first zoom never hitches, then returns to the gameplay grid.
        /// </summary>
        void PrewarmZoomLevels(CameraFrame gameplay)
        {
            foreach (int ppu in ScenicZoom.Levels(gameplay, rig.WideFrame))
            {
                var g = new PixelGrid(ppu, gameplay.Shear, gameplay.ShearDepth);
                city.ApplyPixelGrid(g);
                facade.ApplyPixelGrid(g);
            }
            city.ApplyPixelGrid(gameplay.Grid);
            facade.ApplyPixelGrid(gameplay.Grid);
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

        /// <summary>Authored banded light pools: street lamps, shop neon on the pavement, the HOTEL sign on the roof.</summary>
        System.Collections.Generic.List<PixelGlow.LightPool> LightPools()
        {
            var list = new System.Collections.Generic.List<PixelGlow.LightPool>();
            float roof = layout.FloorCount * layout.FloorHeight;
            var sign = building.HotelSignRoot != null ? world.InverseTransformPoint(building.HotelSignRoot.position) : new Vector3(layout.ShaftX, roof, 0f);
            list.Add(new PixelGlow.LightPool(world.TransformPoint(new Vector3(sign.x, roof + 0.15f, sign.z)), new Vector2(4.2f, 1.3f), RenderingStyle.HotelSign * HotelRoofLight));
            list.AddRange(city.LightPools);
            return list;
        }

        /// <summary>Intensity of the HOTEL sign's red pool on the roof (light units, see PixelGlow.Bands).</summary>
        const float HotelRoofLight = 0.3f;

        /// <summary>City windows, facade lamps, HOTEL letters (juice clock + prewarm offset so the city is mixed at boot).</summary>
        void TickExterior(float time)
        {
            float t = time + StreetPrewarmSeconds;
            bool eerie = dayQuery != null && dayQuery.IsFriday13;
            livingLights.Eerie = eerie;
            livingLights.Tick(t);
            city.Tick(t);
            for (int i = 0; i < letterLevels.Length; i++)
                letterLevels[i] = LightSchedule.SignLetterLevel(RenderingStyle.CitySeed, i, letterLevels.Length, t, eerie);
            building.SetHotelLetterLevels(letterLevels);
            palette.Flush();
        }

        /// <summary>Day 5 rain (never on the title): rain, splashes, wet street, lightning; the sky goes overcast.</summary>
        bool IsRainyNow => dayQuery != null && dayQuery.IsRainy && dayQuery.Phase != DayPhase.Title;

        void TickWeather(float time)
        {
            weather.Tick(time, rig.Current, IsRainyNow);
            sky.Overcast = weather.Raining;
            var cam = pipeline.WorldCamera;
            var skyColor = weather.Raining || winter == null ? weather.SkyColor : WinterView.MoodSky(winter.Mood);
            if (cam != null && cam.backgroundColor != skyColor) cam.backgroundColor = skyColor;
        }

        /// <summary>Winter season: snow, cover, Christmas dressing, lottery, cold mood (ambient + moonlight).</summary>
        void TickSeason(float time, float jdt)
        {
            var s = SeasonNow;
            winter.Tick(time, jdt, rig.Current, s, weather.Raining);
            christmas.Tick(time, s != null && s.IsChristmas, rig.Current.Grid);
            lottery.Tick(jdt, juiceTime, s);
            ApplyMood(winter.Mood);
        }

        void ApplyMood(float mood)
        {
            if (Mathf.Abs(mood - appliedMood) < 0.002f) return;
            appliedMood = mood;
            var ambient = WinterView.MoodAmbient(mood);
            RenderSettings.ambientLight = ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambient);
            RenderSettings.ambientProbe = sh;
            if (moonLight != null) moonLight.color = WinterView.MoodMoon(mood);
        }

        /// <summary>Day 7 "star night" (never on the title, whose attract demo is an ordinary night).</summary>
        bool IsStarNightNow => dayQuery != null && dayQuery.IsStarNight && dayQuery.Phase != DayPhase.Title;

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
                    building.SetLabel(f, CurrentLabel(f), IsRelabelled(f), OriginalLabel(f).Text);
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
            ending.Tick(jdt, juiceTime);
            lighting.Tick(juiceTime, jdt);
            rig.SetTitle(dayQuery != null && dayQuery.Phase == DayPhase.Title);
            // the scenic view pauses the game: its zoom + pan run on the unscaled clock
            float cdt = Mathf.Min(CameraStepOverride ?? Time.unscaledDeltaTime, CameraRig.MaxCameraStep);
            rig.Tick(jdt, cdt);
            if (rig.LevelStepped) pipeline.Glow?.BeginDissolve(CameraRig.StepDissolveSeconds);
            pipeline.Glow?.TickDissolve(cdt);
            float adt = jdt > 0f ? jdt : rig.InScenicView ? cdt : 0f;
            ambientTime += adt;
            var grid = rig.Current.Grid;
            building.ApplyPixelGrid(grid);
            city.ApplyPixelGrid(grid);
            facade.ApplyPixelGrid(grid);
            assets.ApplyPatternGrid(grid.PixelsPerUnit);
            street.Tick(adt, grid);
            TickWeather(ambientTime + StreetPrewarmSeconds);
            TickSeason(ambientTime + StreetPrewarmSeconds, jdt);
            sky.Tick(ambientTime + StreetPrewarmSeconds, IsSimHeld ? 0f : jdt, rig.Current, IsStarNightNow);
            TickExterior(ambientTime);
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
            building.SetLabel(e.Floor, e.Current, sticker, OriginalLabel(e.Floor).Text);
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
            if (IsBuilt) lighting.Flash(e.Floor, Color.white, 0.35f, FloorLighting.MinorFlashStrength); // soft "ding" glow
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
                // "666": one red pulse through the whole hotel (one flash per floor, in step, rate-limited by
                // FloorLighting); pulse first so the judged floor's own flash merges into it
                if (e.Number == StoryRules.DevoutNumber && IsDevout(e.Guest))
                    lighting.FlashAll(RenderingStyle.UnluckyFlash, RenderingStyle.CurseFlashSeconds, RenderingStyle.CurseFlashStrength);
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

        /// <summary>Lottery jackpot: a shower of gold coins where the guest stepped out, a soft gold glow on that floor.</summary>
        void OnLotteryWon(LotteryWon e)
        {
            if (!IsBuilt) return;
            if (!guests.TryGetHead(e.Guest, out var at))
                at = world.TransformPoint(layout.GetFloorAnchor(e.Floor).ToVector3() + new Vector3(0f, layout.FloorHeight * 0.6f, 0f));
            fx.Burst(at, LotteryView.Gold, 16, 3.8f, 0.2f);
            fx.Burst(at + Vector3.up * 0.2f, new Color(1f, 0.95f, 0.65f), 8, 2.6f, 0.13f);
            lighting.Flash(e.Floor, LotteryView.Gold, 0.9f, 0.6f);
        }

        void OnGuestSpawned(GuestSpawned e)
        {
            archetypes[e.Guest.Value] = e.Archetype;
            if (IsBuilt) guests.Spawn(e.Guest, e.Archetype, e.Floor, e.WaitingSlot, data);
        }

        /// <summary>Guest's archetype has GuestTraits.Devout (the "666" curse).</summary>
        bool IsDevout(GuestId guest)
        {
            if (data == null) return false;
            GuestArchetypeId archetype;
            if (guestQuery != null && guestQuery.TryGet(guest, out var info)) archetype = info.Archetype;
            else if (!archetypes.TryGetValue(guest.Value, out archetype)) return false;
            return data.TryGetGuest(archetype, out var def) && def.HasTrait(GuestTraits.Devout);
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
