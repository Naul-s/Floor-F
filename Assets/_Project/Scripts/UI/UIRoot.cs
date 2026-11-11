using FloorF.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// UI module root (installed by <see cref="UIBootstrap"/> into the Main scene).
    /// Start: resolve services, build one Screen Space - Camera canvas on PresentationContract's UI camera
    /// (planeDistance 1, sortingOrder 10, everything on UiLayer) + EventSystem/InputSystemUIInputModule if missing,
    /// then the views. OnEnable/OnDisable: event subscriptions. Commands only via IElevatorControl / IGameFlow /
    /// IShopService / IGameSettings. KeyboardInput (W/S/Space/Enter) is left untouched as the keyboard alternative.
    /// </summary>
    sealed class UIRoot : MonoBehaviour
    {
        readonly Subscriptions subs = new Subscriptions();
        readonly UIContext ctx = new UIContext();

        Canvas canvas;
        ElevatorPanelView panel;
        HudView hud;
        GuestBubblesView bubbles;
        PopupsView popups;
        FlowScreensView flow;
        ShopView shop;
        TutorialView tutorial;
        MenuView menu;
        CurseView curse;
        WishView wish;
        DayBannerView banner;
        CharmsView charms;
        ScenicView scenic;
        ClickFxView clickFx;
        DebugReadoutView debugReadout;
        DebugReadoutChanged lastDebug;
        /// <summary>Above the HUD canvas: click effect, scenic hint, establishing caption. No GraphicRaycaster (never blocks).</summary>
        Canvas fxCanvas;
        RectTransform fxRect;
        /// <summary>On the HUD canvas: fades it out for the scenic view / establishing shot.</summary>
        CanvasGroup hudGroup;
        bool built;

        void OnEnable()
        {
            var bus = GameServices.Events;
            subs.Add(bus.Subscribe<GameStarted>(OnGameStarted));
            // the title's attract demo publishes guest/elevator/luck events too: the title card stays clean
            subs.Add(bus.Subscribe<LuckChanged>(e => { if (built && !OnTitle) hud.OnLuckChanged(e); }));
            subs.Add(bus.Subscribe<TipsChanged>(e => { if (built && !OnTitle) hud.OnTipsChanged(e); }));
            subs.Add(bus.Subscribe<FloorLabelChanged>(e => { if (built) panel.RefreshLabel(e.Floor); }));
            subs.Add(bus.Subscribe<CharmTriggered>(OnCharmTriggered));
            subs.Add(bus.Subscribe<LuckJudged>(OnLuckJudged));
            subs.Add(bus.Subscribe<ShopRerolled>(e => { if (built) shop.OnRerolled(); }));
            subs.Add(bus.Subscribe<StickersExpired>(e => { if (built) flow.OnStickersExpired(e); }));
            subs.Add(bus.Subscribe<GuestSpoke>(OnGuestSpoke));
            subs.Add(bus.Subscribe<SettingsChanged>(e => RefreshLanguage()));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnPhaseChanged));
            subs.Add(bus.Subscribe<GuestBoarded>(e => { if (built) tutorial.OnGuestBoarded(e); }));
            subs.Add(bus.Subscribe<DoorsClosed>(e => { if (built) tutorial.OnDoorsClosed(e); }));
            subs.Add(bus.Subscribe<DoorsOpened>(e => { if (built) tutorial.OnDoorsOpened(e); }));
            subs.Add(bus.Subscribe<ElevatorDeparted>(e => { if (built) tutorial.OnDeparted(e); }));
            subs.Add(bus.Subscribe<GuestSpawned>(e => { if (built) { tutorial.OnGuestSpawned(e); curse.OnGuestSpawned(e); } }));
            subs.Add(bus.Subscribe<GuestDelivered>(e => { if (built) tutorial.OnGuestDelivered(e); }));
            subs.Add(bus.Subscribe<GuestLeft>(e => { if (built) tutorial.OnGuestLeft(e); }));
            subs.Add(bus.Subscribe<PauseChanged>(e => { if (built) { scenic.OnPauseChanged(e); menu.Tick(); } }));
            subs.Add(bus.Subscribe<LuckStreakChanged>(e => { if (built && !OnTitle) { hud.OnLuckStreakChanged(e); tutorial.OnLuckStreakChanged(e); } }));
            subs.Add(bus.Subscribe<TutorialHoldChanged>(e => { if (built) tutorial.OnTutorialHoldChanged(e); }));
            subs.Add(bus.Subscribe<WishStarAppeared>(e => { if (built && !OnTitle) wish.OnAppeared(e); }));
            subs.Add(bus.Subscribe<WishMade>(e => { if (built && !OnTitle) wish.OnMade(e); }));
            subs.Add(bus.Subscribe<WishStarFaded>(e => { if (built) wish.OnFaded(e); }));
            subs.Add(bus.Subscribe<EndingReached>(OnEndingReached));
            subs.Add(bus.Subscribe<DayEnded>(OnDayEnded));
            subs.Add(bus.Subscribe<DebugReadoutChanged>(e => { lastDebug = e; if (built) debugReadout.On(e); }));
        }

        /// <summary>Counts the ending in the collection with the run's final Luck and tips earned (best record);
        /// EndingReached is flow-silent on the title demo - guarded anyway.</summary>
        void OnEndingReached(EndingReached e)
        {
            if (!built || OnTitle || ctx.Endings == null) return;
            int tips = ctx.Journey.Count > 0 ? ctx.Journey.TipsEarned : e.TotalTips;
            ctx.Endings.Add(e.Ending, e.FinalLuck, tips);
            if (flow.Shown == DayPhase.Ending) flow.Refresh();
            menu.RefreshLanguage(); // the title's best-record plate
        }

        /// <summary>A real day's summary: graded into the run's journey (the ending card's stamp row). Day 0 never counts.</summary>
        void OnDayEnded(DayEnded e)
        {
            if (!built || OnTitle || ctx.Data == null) return;
            if (ctx.Data.TryGetDay(e.Day, out var def) && def.tutorial) return;
            ctx.Journey.Add(ctx.Data.DisplayDayNumber(e.Day), e.Summary);
        }

        void OnDisable() => subs.Clear();

        /// <summary>Guests whose NEXT Unlucky verdict a shield charm just ate (CharmTriggered comes right before it).</summary>
        readonly System.Collections.Generic.Dictionary<GuestId, StickerId> shielded = new System.Collections.Generic.Dictionary<GuestId, StickerId>();

        void OnCharmTriggered(CharmTriggered e)
        {
            if (!built || OnTitle) return;
            charms.OnTriggered(e);
            if (charms.IsShield(e.Charm)) shielded[e.Guest] = e.Charm;
        }

        void OnLuckJudged(LuckJudged e)
        {
            if (!built || OnTitle) return;
            string shieldLine = null;
            if (e.Verdict == LuckVerdict.Unlucky && shielded.TryGetValue(e.Guest, out var charm))
            {
                shielded.Remove(e.Guest);
                shieldLine = charms.TriggerLine(charm);
            }
            popups.OnLuckJudged(e, shieldLine);
            tutorial.OnLuckJudged(e);
            if (shieldLine == null) curse.OnLuckJudged(e); // a blocked 6 costs nothing: no 666 alarm
        }

        bool OnTitle => ctx.Phase == DayPhase.Title || (ctx.Menu != null && ctx.Menu.IsOnTitle);

        void Start()
        {
            ctx.ResolveServices();
            if (!ctx.HasGame)
            {
                Debug.LogWarning("[UI] gameplay services missing - HUD not built. Registered: " + string.Join(", ", GameServices.RegisteredNames()));
                return;
            }
            ctx.Endings = new EndingCollection(EndingCollection.CreateStore());
            // fonts first: every UI character in the atlases before the first text is measured (UIFonts.Prewarm)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int prewarmed = UIFonts.Prewarm(ctx.PrewarmText());
            Debug.Log("[UI] fonts prewarmed: " + prewarmed + " characters in " + watch.ElapsedMilliseconds + " ms");
            BuildCanvas();
            EnsureEventSystem();
            var rt = ctx.CanvasRect;
            bubbles = new GuestBubblesView(ctx, rt);
            popups = new PopupsView(ctx, rt);
            wish = new WishView(ctx, rt); // click targets under the panel / HUD buttons; its Fx layer is raised below
            panel = new ElevatorPanelView(ctx, rt);
            hud = new HudView(ctx, rt);
            charms = new CharmsView(ctx, rt, hud);
            // the right column restacks as one: rule box / tab -> event badge -> wish explain card -> charms
            wish.ColumnTop = () => hud.ColumnCardTop;
            hud.ColumnCardBottom = () => wish.CardBottom;
            clickFx = new ClickFxView(fxRect);
            scenic = new ScenicView(ctx, rt, fxRect); // eye button next to the day box; hint / caption on the FX canvas
            wish.Fx.SetAsLastSibling(); // hint, explain card, float-ups above the HUD boxes
            banner = new DayBannerView(ctx, rt);
            shop = new ShopView(ctx, rt, panel);
            curse = new CurseView(ctx, rt); // above the HUD / popups, under the flow cards, coach marks and menu
            flow = new FlowScreensView(ctx, rt);
            tutorial = new TutorialView(ctx, rt, panel, bubbles, hud);
            menu = new MenuView(ctx, rt, () => scenic.TryEnterScenic(), () => scenic.SuppressPauseMenu, p => clickFx.Spawn(p, big: true));
            menu.BringToFront();
            clickFx.Clear();
            debugReadout = new DebugReadoutView(fxRect); // above the click effects; Gameplay's debug cheats only
            debugReadout.On(lastDebug);
            built = true;
            laidOutFor = ctx.CanvasRect.rect.size;
            relayoutFrame = Time.frameCount + 1; // once more after the first rendered frame
            if (RecordBootLayout) BootFontSizes = FontSizeSnapshot();
            SetLayerRecursively(canvas.transform, PresentationContract.UiLayer);
            SetLayerRecursively(fxCanvas.transform, PresentationContract.UiLayer);
            Debug.Log("[UI] built: canvas " + canvas.renderMode + " on " + (canvas.worldCamera != null ? canvas.worldCamera.name : "<none>"));
        }

        void BuildCanvas()
        {
            var go = new GameObject("[UI] HUD Canvas", typeof(RectTransform));
            go.layer = PresentationContract.UiLayer;
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            AttachCamera();
            canvas.sortingOrder = UIStyle.SortingOrder;
            canvas.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UIStyle.RefWidth, UIStyle.RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            hudGroup = go.AddComponent<CanvasGroup>();
            ctx.CanvasRect = (RectTransform)go.transform;

            // FX canvas: same scaler, drawn after the HUD canvas, no raycaster at all
            var fx = new GameObject("[UI] FX Canvas", typeof(RectTransform));
            fx.layer = PresentationContract.UiLayer;
            fx.transform.SetParent(transform, false);
            fxCanvas = fx.AddComponent<Canvas>();
            fxCanvas.sortingOrder = UIStyle.SortingOrder + 1;
            fxCanvas.pixelPerfect = false;
            var fxScaler = fx.AddComponent<CanvasScaler>();
            fxScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            fxScaler.referenceResolution = new Vector2(UIStyle.RefWidth, UIStyle.RefHeight);
            fxScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            fxRect = (RectTransform)fx.transform;
            AttachCamera();
        }

        /// <summary>Screen Space - Camera on the Rendering UI camera; Overlay only as a fallback while it is missing.</summary>
        void AttachCamera()
        {
            var camGo = GameObject.Find(PresentationContract.UiCameraName);
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            foreach (var c in new[] { canvas, fxCanvas })
            {
                if (c == null) continue;
                if (cam != null)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = UIStyle.PlaneDistance;
                }
                else if (c.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    if (c == canvas) Debug.LogWarning("[UI] '" + PresentationContract.UiCameraName + "' not found - Overlay fallback until it appears");
                    c.renderMode = RenderMode.ScreenSpaceOverlay;
                }
            }
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[UI] EventSystem");
            go.layer = PresentationContract.UiLayer;
            go.transform.SetParent(transform, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
        }

        Vector2 laidOutFor;
        int relayoutFrame = -1;

        /// <summary>
        /// Every fitted text (UIBuild.Fit / FitName and the views' own measurements) is laid out from the canvas size at
        /// the time of the last refresh. The WebGL canvas settles its size after the first frames (and changes on window
        /// resize / fullscreen): lay everything out again whenever the canvas size changes, and once right after the first
        /// rendered frame - what a language toggle used to fix by accident.
        /// </summary>
        void RelayoutIfNeeded()
        {
            var size = ctx.CanvasRect.rect.size;
            bool valid = size.x > 1f && size.y > 1f && !float.IsNaN(size.x) && !float.IsNaN(size.y) && !float.IsInfinity(size.x) && !float.IsInfinity(size.y);
            if (!valid) return;
            if (size != laidOutFor) { laidOutFor = size; relayoutFrame = Time.frameCount; }
            if (relayoutFrame < 0 || Time.frameCount < relayoutFrame) return;
            relayoutFrame = -1;
            Relayout();
        }

        /// <summary>Re-measures and redraws every text: the language refresh plus a forced TMP rebuild of each one.</summary>
        internal void Relayout()
        {
            if (!built) return;
            RelayoutCount++;
            Canvas.ForceUpdateCanvases(); // rect sizes up to date before anything is measured
            RefreshLanguage();
            foreach (var t in canvas.GetComponentsInChildren<TMPro.TMP_Text>(false)) t.havePropertiesChanged = true;
        }

        /// <summary>Test hook: how many full relayouts ran since the UI was built.</summary>
        internal int RelayoutCount { get; private set; }

        void Update()
        {
            if (!built) return;
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == null) AttachCamera();
            RelayoutIfNeeded();
            float dt = Time.deltaTime;
            bool onTitle = ctx.Phase == DayPhase.Title;
            panel.SetVisible(!onTitle);
            hud.SetVisible(!onTitle);
            panel.Tick(Time.unscaledTime);
            hud.Tick(dt);
            charms.Tick(dt);
            shielded.Clear(); // CharmTriggered + its LuckJudged come together in one gameplay step
            bubbles.Tick();
            popups.Tick(dt);
            curse.Tick(dt);
            wish.Tick(dt);
            banner.Tick(dt);
            shop.Tick();
            flow.Tick();
            tutorial.Tick(dt, Time.unscaledTime);
            menu.Tick();
            HandlePresses(UIInput.Poll());
            clickFx.Tick(UIClock.UnscaledDt);
            FadeHud(UIClock.UnscaledDt);
            // an open title card (CREDITS / ENDINGS / OPTIONS) keeps Enter from starting a game behind it
            UIInput.BlockFlowEnter(menu.Attract || menu.Revealing || (OnTitle && menu.TitleCardOpen) || scenic.HoldsEnter);
        }

        void LateUpdate()
        {
            if (!built) return;
            if (scenic.LateTick()) menu.Tick(); // back to the pause card it came from: shown this frame
        }

        /// <summary>
        /// This frame's presses, in priority order: the title attract screen (any key / click reveals; its own burst, no
        /// click effect), the scenic view / establishing shot (skip, exit, V), H = rule box; every other pointer press
        /// gets the click effect (+ Tap unless it lands on something clickable: buttons play their own sound).
        /// </summary>
        void HandlePresses(UIPress press)
        {
            bool attractUsed = menu.HandleAttractPress(press);
            bool used = attractUsed;
            if (scenic.Tick(UIClock.UnscaledDt, used ? default : press)) used = true;
            if (!used && press.H && ctx.Phase == DayPhase.Running && !OnTitle && !ctx.Paused) hud.ToggleRules();
            if (press.Pointer && !attractUsed) OnPointerPress(press.Screen);
        }

        void OnPointerPress(Vector2 screen)
        {
            var cam = fxCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : fxCanvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(fxRect, screen, cam, out var local))
                clickFx.Spawn(local - fxRect.rect.min);
            if (!OverClickable(screen)) ctx.Sound(UiSoundKind.Tap);
        }

        /// <summary>The topmost UI hit at a screen point handles clicks itself (an interactable button, slider, wish star...).</summary>
        bool OverClickable(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            raycastHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screen }, raycastHits);
            if (raycastHits.Count == 0) return false;
            var go = raycastHits[0].gameObject;
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) ?? ExecuteEvents.GetEventHandler<IPointerDownHandler>(go);
            if (handler == null) return false;
            var sel = handler.GetComponent<Selectable>();
            return sel == null || sel.IsInteractable();
        }

        void FadeHud(float dt)
        {
            bool hide = scenic.HidesUi;
            float target = hide ? 0f : 1f;
            hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, target, dt / (hide ? 0.15f : 0.25f));
            if (hudGroup.blocksRaycasts == hide) { hudGroup.blocksRaycasts = !hide; hudGroup.interactable = !hide; }
        }

        void OnGameStarted(GameStarted e)
        {
            if (!built) return;
            hud.OnGameStarted();
            panel.RefreshLabels();
            bubbles.Clear();
            popups.Clear();
            tutorial.Reset();
            curse.Reset();
            wish.Reset();
            charms.Clear();
            flow.LastSpecialLine = null;
            flow.ExpiredLabels = flow.ExpiredCharms = 0;
            if (ctx.Endings != null) ctx.Endings.ClearNew();
            ctx.Journey.Clear();
        }

        void OnGuestSpoke(GuestSpoke e)
        {
            if (!built || OnTitle) return;
            popups.OnGuestSpoke(e);
            // the mystery guest's last words come right before the 4th-floor ending - the ending card quotes them
            if (e.LineTrigger == GuestLineTriggers.Special)
            {
                flow.LastSpecialLine = e.Text;
                // remember WHO said it, so the card can show the line in the language shown at the ending
                flow.LastSpecialArchetype = ctx.Guests != null && ctx.Guests.TryGet(e.Guest, out var info) ? info.Archetype : default;
            }
        }

        void OnPhaseChanged(DayPhaseChanged e)
        {
            if (!built) return;
            panel.RefreshLabels();
            if (e.Current == DayPhase.Ending) popups.ClearPopups(); // keep the last speech bubble behind the card
            else if (e.Current != DayPhase.Running) popups.Clear();
            if (e.Current != DayPhase.Running) curse.Clear();
            if (e.Current == DayPhase.Intro || e.Current == DayPhase.Title)
            {
                flow.LastSpecialLine = null;
                flow.ExpiredLabels = flow.ExpiredCharms = 0;
                charms.Clear(); // a new day: shield counts start over
            }
            if (e.Current == DayPhase.Intro || e.Current == DayPhase.Title) hud.ResetStreak(); // a new day starts at 0 silently
            // the attract demo replays a day behind the title: every tutorial day's lesson starts from wave 1
            tutorial.OnPhaseChanged(e);
            if (OnTitle) banner.Stop(); else banner.OnPhaseChanged(e);
            if (e.Current != DayPhase.Running) wish.Clear();
            menu.Tick();
        }


        void RefreshLanguage()
        {
            if (!built) return;
            panel.RefreshLanguage();
            hud.RefreshLanguage();
            shop.RefreshLanguage();
            charms.RefreshLanguage();
            flow.Refresh();
            menu.OnSettingsChanged();
        }

        // ------------------------------------------------------------------ test hooks (PlayMode tests, no uGUI types)

        internal bool IsBuilt => built;
        internal bool UsesUiCamera => canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
                                      && canvas.worldCamera != null && canvas.worldCamera.name == PresentationContract.UiCameraName
                                      && canvas.sortingOrder >= 0 && canvas.planeDistance < 10f;
        internal bool AllOnUiLayer
        {
            get
            {
                foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer != PresentationContract.UiLayer) return false;
                return true;
            }
        }
        internal bool Korean => ctx.Korean;
        internal DayPhase VisibleFlowScreen => built ? (shop.IsOpen ? DayPhase.Shop : flow.Shown) : DayPhase.None;
        internal string FlowTitle => flow.TitleText;
        internal string FlowBody => flow.BodyText;
        internal string FlowExtra => flow.ExtraText;
        internal void ClickFlowButton() => flow.Click();
        internal int FloorButtonCount => panel.ButtonCount;
        internal string FloorButtonText(int floor) => panel.ButtonText(floor);
        internal bool IsFloorApplicable(int floor) => panel.IsApplicable(floor);
        internal void ClickFloor(int floor) => panel.Click(floor);
        internal void ClickOpen() => panel.ClickOpen();
        internal void ClickClose() => panel.ClickClose();
        internal string LedText => panel.LedText;
        internal int ShopOfferCount => shop.OfferCount;
        internal void ClickShopOffer(int index) => shop.ClickOffer(index);
        internal void ClickShopDone() => shop.ClickDone();
        internal string ShopResult => shop.ResultText;
        internal float ShopDescriptionFontSize(int i) => shop.DescriptionFontSize(i);
        internal float ShopDescriptionContrast(int i) => shop.DescriptionContrast(i);
        internal string ShopDescriptionProblem() => shop.FirstDescriptionProblem(ctx.CanvasRect);
        // shop rework (offers grid, reroll, tomorrow strip) / charms / original-floor marks
        /// <summary>Test hook: replace the shop / building query the UI reads (fakes built on the contract). null = keep.</summary>
        internal void OverrideServicesForTests(IShopService shopService = null, IBuildingQuery building = null, IDayQuery day = null)
        {
            if (shopService != null) ctx.Shop = shopService;
            if (building != null) ctx.Building = building;
            if (day != null) ctx.Day = day;
            if (built) { panel.RefreshLabels(); shop.RefreshLanguage(); if (day != null) flow.Refresh(); }
        }

        // endings collection / hidden-ending badge
        /// <summary>Test hook: swap the endings record's store (re-read at once); the title and ending card refresh.</summary>
        internal void UseEndingStoreForTests(IEndingStore store)
        {
            ctx.Endings = new EndingCollection(store);
            if (built) { menu.RefreshLanguage(); flow.Refresh(); }
        }
        internal EndingCollection Endings => ctx.Endings;
        internal RunJourney Journey => ctx.Journey;
        internal string EndingJourneyText => flow.JourneyText;
        internal string EndingBestText => flow.BestText;
        internal Rect EndingJourneyRect => flow.JourneyRect(ctx.CanvasRect);
        internal string FlowToday => flow.TodayText;
        internal string FlowHint => flow.HintText;
        internal string FlowTextOverflow() => flow.Overflow();
        internal bool TitleBestVisible => menu.BestVisible;
        internal string TitleBestText => menu.BestText;
        internal Rect TitleBestRect => menu.BestRect(ctx.CanvasRect);
        internal bool EndingHiddenBadgeVisible => flow.HiddenBadgeVisible;
        internal string EndingHiddenBadgeText => flow.HiddenBadgeText;
        internal bool EndingHiddenBadgeFits => flow.HiddenBadgeFits;
        internal Rect EndingHiddenBadgeRect => flow.HiddenBadgeRect(ctx.CanvasRect);
        internal Rect FlowTitleRect => flow.TitleRect(ctx.CanvasRect);
        internal bool FlowHiddenLook => flow.HiddenLook;
        internal string EndingUnlockText => flow.UnlockText;
        internal string EndingsButtonText => menu.EndingsButtonText;
        internal void ClickEndings() => menu.ClickEndings();
        internal void CloseEndingsLikeEsc() => menu.CloseEndingsLikeEsc();
        internal bool EndingsVisible => menu.EndingsVisible;
        internal void ClickEndingsBack() => menu.EndingsCard.ClickBack();
        internal string EndingsCardText => menu.EndingsCard.Text;
        internal int EndingsSlotCount => menu.EndingsCard.SlotCount;
        internal EndingKind EndingsSlotKind(int i) => menu.EndingsCard.SlotKind(i);
        internal bool EndingsSlotUnlocked(int i) => menu.EndingsCard.SlotUnlocked(i);
        internal bool EndingsSlotBadgeVisible(int i) => menu.EndingsCard.SlotBadgeVisible(i);
        internal string EndingsSlotBadgeText(int i) => menu.EndingsCard.SlotBadgeText(i);
        internal string EndingsSlotText(int i) => menu.EndingsCard.SlotText(i);
        internal bool EndingsFitOnScreen => menu.EndingsCard.FitsIn(ctx.CanvasRect);
        internal Rect EndingsCardRect => menu.EndingsCard.CardRect(ctx.CanvasRect);
        internal string EndingsSlotOverflow() => menu.EndingsCard.Overflow(ctx.CanvasRect);
        internal int ShopOfferIndex(string id) => shop.OfferIndex(id);
        internal string ShopOfferId(int i) => shop.OfferId(i);
        internal string ShopOfferHeadline(int i) => shop.OfferHeadline(i);
        internal string ShopOfferKind(int i) => shop.OfferKind(i);
        internal string ShopOfferDayTag(int i) => shop.OfferDayTag(i);
        internal string ShopOfferSub(int i) => shop.OfferSub(i);
        internal bool ShopOfferInteractable(int i) => shop.OfferInteractable(i);
        internal ShopView.CardLook ShopOfferLook(int i) => shop.OfferLook(i);
        internal bool ShopOfferLocked(int i) => shop.OfferLocked(i);
        internal bool ShopOfferHoverTints(int i) => shop.OfferHoverTints(i);
        internal bool FlowWeddingLook => flow.WeddingLook;
        internal float ShopOfferHeadlineSize(int i) => shop.OfferHeadlineSize(i);
        internal void FocusShopOffer(int i) => shop.FocusOffer(i);
        internal string ShopDetailText => shop.DetailText;
        internal string ShopTomorrowText => shop.TomorrowText;
        internal string ShopRerollText => shop.RerollText;
        internal string ShopRerollReason => shop.RerollReason;
        internal bool ShopRerollInteractable => shop.RerollInteractable;
        internal void ClickShopReroll() => shop.ClickReroll();
        internal Rect ShopCardRect => shop.CardRect(ctx.CanvasRect);
        internal string OriginalMarkText(int floor) => panel.OriginalMarkText(floor);
        internal Rect OriginalMarkRect(int floor) => UIBuild.CanvasRect(ctx.CanvasRect, panel.OriginalMarkRect(floor));
        internal Rect FloorLabelRect(int floor) => UIBuild.CanvasRect(ctx.CanvasRect, panel.LabelRect(floor));
        internal float FloorLabelTextWidth(int floor) => panel.LabelPreferredWidth(floor);
        internal bool CharmsVisible => charms.Visible;
        internal string CharmsText => charms.Text;
        internal Rect CharmsRect => UIBuild.CanvasRect(ctx.CanvasRect, charms.Box);
        internal string CharmsOverflow() => charms.Overflow(ctx.CanvasRect);
        internal float CharmsMaxLineScale => charms.MaxLineScale;
        internal bool CharmFloatVisible => charms.FloatVisible;
        internal string CharmFloatText => charms.FloatText;
        internal Rect CharmFloatRect => charms.FloatVisible ? UIBuild.CanvasRect(ctx.CanvasRect, charms.Float) : default;
        internal Color LastPopupColor => popups.LastPopupColor;
        internal Rect LastPopupRect => popups.LastPopupRect;
        internal bool CurseShownForTests => curse.Visible;
        internal int ActivePopupCount => popups.ActivePopupCount;
        // rule 3 / 666 / Friday the 13th
        internal string ChipOdometer(GuestId g) => bubbles.Odometer(g);
        internal Color ChipOdometerColor(GuestId g) => bubbles.OdometerColor(g);
        internal Rect ChipRect(GuestId g) => bubbles.ChipRect(g);
        internal string ChipOdometerOverflow() => bubbles.OdometerOverflow();
        internal bool CurseVisible => curse.Visible;
        internal string CurseText => curse.Text;
        internal float CurseVignetteAlpha => curse.VignetteAlpha;
        internal Rect CurseTextRect => curse.TextRect(ctx.CanvasRect);
        internal void PlayCurse() => curse.Play();
        internal float CurseCaptionFontSize => curse.CaptionFontSize;
        internal bool FridayBadgeVisible => hud.FridayBadgeVisible;
        internal string FridayBadgeText => hud.FridayBadgeText;
        internal float FridayBadgeFontSize => hud.FridayBadgeFontSize;
        internal Rect FridayBadgeRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.FridayBadge);
        internal Rect RulesBoxRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.RulesBox);
        internal string RulesText => hud.RulesText;
        internal string RulesOverflow() => hud.RulesOverflow();
        internal bool FlowFridayLook => flow.FridayLook;
        internal int ActiveSpeechCount => popups.ActiveSpeechCount;
        internal string LastPopupText => popups.LastPopupText;
        internal int VisibleBubbleCount => bubbles.VisibleCount;
        internal bool SettingsServiceBound => ctx.Settings != null && GameServices.TryGet(out IGameSettings s) && ReferenceEquals(s, ctx.Settings);
        /// <summary>Every visible speech bubble is outside the shaft column (null) or the first offender's description.</summary>
        internal string SpeechShaftOverlap() => popups.FirstShaftOverlap();
        /// <summary>Every visible speech bubble is clear of every luck popup (null) or the first offender's description.</summary>
        internal string SpeechPopupOverlap() => popups.FirstPopupOverlap();
        internal string SpeechTexts => popups.SpeechTexts;
        /// <summary>No two visible speech bubbles overlap (null) or the first pair.</summary>
        internal string SpeechSpeechOverlap() => popups.FirstSpeechOverlap();
        internal string PopupOverlapX(float minX, float maxX) => popups.FirstPopupOverlapX(minX, maxX);
        internal void ShowFontCompare(bool show)
        {
            if (show && fontCompare == null) { fontCompare = FontSampleCard.Build(ctx.CanvasRect); SetLayerRecursively(fontCompare.transform, PresentationContract.UiLayer); }
            else if (!show && fontCompare != null) { Destroy(fontCompare); fontCompare = null; }
        }
        GameObject fontCompare;
        internal int TextCount => canvas.GetComponentsInChildren<TMPro.TMP_Text>(true).Length;
        /// <summary>Mulmaru body + Galmuri11 Bold heading runtime assets exist, both with only the Galmuri11 fallback.</summary>
        internal bool FontsCreated => UIFonts.AllFacesCreated;
        /// <summary>null when every UI text uses a runtime UI font asset (body or heading) at a 12px-grid size, else the first offender.</summary>
        internal string FirstOffGridOrForeignText()
        {
            foreach (var t in canvas.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (!UIFonts.IsUiFont(t.font)) return t.name + " font " + (t.font != null ? t.font.name : "<null>");
                if (Mathf.Abs(t.fontSize % UIFonts.Grid) > 0.01f) return t.name + " size " + t.fontSize;
                if (t.enableAutoSizing) return t.name + " auto-sizes";
            }
            return null;
        }
        /// <summary>Font captures: visible-text characters no font (primary or fallback) has, and overflowing texts.</summary>
        internal string FontReport()
        {
            var missing = new System.Text.StringBuilder();
            var over = new System.Text.StringBuilder();
            var seen = new System.Collections.Generic.HashSet<char>();
            foreach (var t in canvas.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (!t.isActiveAndEnabled || t.font == null) continue;
                foreach (char c in System.Text.RegularExpressions.Regex.Replace(t.text ?? "", "<[^>]*>", "")) // source text: TMP draws a missing glyph as □
                    if (!char.IsWhiteSpace(c) && !char.IsControl(c) && !char.IsSurrogate(c) && UIFonts.Resolve(t.font, c) == null && seen.Add(c))
                        missing.Append(c).Append(" U+").Append(((int)c).ToString("X4")).Append(" (").Append(t.name).Append("); ");
                if (t.isTextOverflowing) over.Append(t.name).Append("; ");
            }
            return "missing: " + (missing.Length > 0 ? missing.ToString() : "none") + " | overflowing: " + (over.Length > 0 ? over.ToString() : "none");
        }
        /// <summary>The pause card's language button (works while the card is hidden too: it only invokes the click).</summary>
        internal void ClickPauseLanguage() => menu.ClickPauseLanguage();
        internal string PauseLanguageText => menu.PauseLanguageText;
        /// <summary>Active buttons labelled like the language toggle outside the pause / OPTIONS cards (empty = none).</summary>
        internal string LanguageTogglesOutsideMenus() => menu.LanguageTogglesOutsideMenus(ctx.CanvasRect);

        /// <summary>Every visible UI text as "path | font size | drawn size (mesh bounds of the LAST generated mesh) | text":
        /// a fresh boot must draw exactly like after a language round trip (the tiny-text-until-toggled bug).</summary>
        internal string[] TextSizeSnapshot() => TextSnapshot(true);

        /// <summary>Every UI text, visible or not, as "path | font size" (no mesh yet at build time).</summary>
        internal string[] FontSizeSnapshot() => TextSnapshot(false);

        string[] TextSnapshot(bool drawn)
        {
            var list = new System.Collections.Generic.List<string>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in canvas.GetComponentsInChildren<TMPro.TMP_Text>(!drawn))
            {
                if (drawn && !t.isActiveAndEnabled) continue;
                var path = new System.Text.StringBuilder(t.name);
                for (var p = t.transform.parent; p != null && p != canvas.transform; p = p.parent) path.Insert(0, p.name + "/");
                if (!drawn) { list.Add(path + " | " + t.fontSize.ToString(inv)); continue; }
                var b = t.textBounds.size;
                list.Add(path + " | " + t.fontSize.ToString(inv) + " | " + Mathf.Round(b.x).ToString(inv) + "x" + Mathf.Round(b.y).ToString(inv) + " | " + t.text);
            }
            return list.ToArray();
        }

        /// <summary>Test hook: when set before the scene loads, the next UI build records <see cref="BootFontSizes"/>
        /// (the very first layout, before any relayout).</summary>
        internal static bool RecordBootLayout;
        internal string[] BootFontSizes { get; private set; }

        // tutorial
        internal TutorialView.Step TutorialStep => tutorial.CurrentStep;
        internal string TutorialPrompt => tutorial.PromptText;
        internal string TutorialWhy => tutorial.WhyText;
        internal string TutorialTarget => tutorial.TargetName;
        internal int TutorialWave => tutorial.Wave;
        internal Rect TutorialPromptRect => tutorial.PromptRect;
        internal Rect TutorialWhyRect => tutorial.WhyRect;
        internal Rect TutorialTargetRect => tutorial.TargetRect;
        internal Rect TutorialArrowRect => tutorial.ArrowRect;
        internal bool CoachVisible => tutorial.CoachVisible;
        internal Rect CoachHoleRect => tutorial.CoachHoleRect;
        internal bool CoachAwaitingContinue => tutorial.AwaitingContinue;
        internal string CoachContinueHint => tutorial.ContinueHint;
        internal void ClickCoachOverlay() => tutorial.ClickOverlay();
        internal int TutorialQueuedExplains => tutorial.QueuedExplains;
        internal System.Collections.Generic.List<TutorialView.CardSample> TutorialSampleCards() => tutorial.SampleCards();
        internal Rect MeasureTutorialCard(TutorialView.CardSample s) => tutorial.MeasureCard(s);
        internal Rect LuckBoxRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.LuckBox);
        internal Rect StreakBoxRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.StreakBox);
        internal bool StreakVisible => hud.StreakVisible;
        internal string StreakText => hud.StreakText;
        internal float StreakScale => hud.StreakScale;
        internal bool GradeStampVisible => flow.StampVisible;
        internal string GradeStampText => flow.StampGrade;
        internal int GradeStampTextCount => flow.StampTextCount;
        internal bool GradeStampLanded => flow.StampLanded;
        internal Rect GradeStampRect => flow.StampRect(ctx.CanvasRect);
        internal Rect FlowCardRect => flow.CardRect(ctx.CanvasRect);
        internal Rect TitlePartRect(MenuView.TitlePart p) => menu.TitlePartRect(ctx.CanvasRect, p);
        internal bool TitleLabelFits(MenuView.TitlePart p, out float fontSize) => menu.TitleLabelFits(p, out fontSize);
        internal string TitleMenuPanels => menu.TitleMenuPanels();
        internal string TaglineText => menu.TaglineText;
        internal float TaglineFontSize => menu.TaglineFontSize;
        internal bool TaglineFits => menu.TaglineFits;
        internal PixelLogo TitleLogo => menu.TitleLogo;
        internal PixelLogo PauseLogo => menu.PauseLogo;
        internal Rect PauseLogoRect => menu.PauseLogoRect(ctx.CanvasRect);
        internal Rect PauseCardRect => menu.PauseCardRect(ctx.CanvasRect);

        // star night / wish stars / day banner
        internal bool StarBadgeVisible => hud.StarBadgeVisible;
        internal string StarBadgeText => hud.StarBadgeText;
        internal float StarBadgeFontSize => hud.StarBadgeFontSize;
        internal bool StarBadgeFits => hud.StarBadgeFits;
        internal string StarBadgeMeasure => hud.StarBadgeMeasure;
        internal Rect StarBadgeRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.StarBadge);
        internal bool FlowStarLook => flow.StarLook;
        internal int WishTargetCount => wish.TargetCount;
        internal Rect WishTargetRect(int id) { var r = wish.TargetRect(id); return r != null ? UIBuild.CanvasRect(ctx.CanvasRect, r) : default; }
        internal void ClickWish(int id) => wish.Click(id);
        internal bool WishHintVisible => wish.HintVisible;
        internal string WishHintText => wish.HintText;
        internal Rect WishHintRect => wish.HintVisible ? UIBuild.CanvasRect(ctx.CanvasRect, wish.Hint) : default;
        internal bool WishCardVisible => wish.CardVisible;
        internal string WishCardText => wish.CardText;
        internal Rect WishCardRect => wish.CardVisible ? UIBuild.CanvasRect(ctx.CanvasRect, wish.Card) : default;
        internal int WishGrantCount => wish.GrantCount;
        internal string WishGrantText => wish.GrantText;
        internal Rect WishGrantRect => wish.GrantRect != null ? UIBuild.CanvasRect(ctx.CanvasRect, wish.GrantRect) : default;
        /// <summary>Viewport (0..1) -> canvas position, as the wish targets map it.</summary>
        internal Vector2 ViewportToCanvas(float vx, float vy)
        {
            var size = ctx.CanvasRect.rect.size;
            PresentationContract.ViewportToScreen(vx, vy, 1f, size.x, size.y, out float sx, out float sy);
            return new Vector2(sx, sy);
        }
        internal bool DayBannerVisible => banner.Visible;
        internal string DayBannerText => banner.Text;
        internal Rect DayBannerRect => banner.Visible ? UIBuild.CanvasRect(ctx.CanvasRect, banner.Rect) : default;

        /// <summary>Name of the topmost UI raycast target at canvas point <paramref name="p"/> (what a click there hits), or null.</summary>
        internal string RaycastAt(Vector2 p)
        {
            var es = EventSystem.current;
            if (es == null) return null;
            var rt = ctx.CanvasRect;
            var world = rt.TransformPoint(p + rt.rect.min);
            var screen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, world);
            var data = new PointerEventData(es) { position = screen };
            raycastHits.Clear();
            es.RaycastAll(data, raycastHits);
            return raycastHits.Count > 0 ? raycastHits[0].gameObject.name : null;
        }
        readonly System.Collections.Generic.List<RaycastResult> raycastHits = new System.Collections.Generic.List<RaycastResult>();
        internal Rect PanelRect => UIBuild.CanvasRect(ctx.CanvasRect, panel.Root);
        internal Rect FloorButtonRect(int floor) => UIBuild.CanvasRect(ctx.CanvasRect, panel.FloorButtonRect(new FloorIndex(floor)));
        internal Rect OpenButtonRect => UIBuild.CanvasRect(ctx.CanvasRect, panel.OpenRect);
        internal Rect CloseButtonRect => UIBuild.CanvasRect(ctx.CanvasRect, panel.CloseRect);
        internal string HudDayText => hud.DayText;
        internal string HudClockText => hud.ClockText;
        internal bool SkipTutorialVisible => flow.SkipVisible;
        internal void ClickSkipTutorial() => flow.ClickSkip();
        internal string SkipTutorialText => flow.SkipText;
        internal float SkipTutorialFontSize => flow.SkipFontSize;
        internal float HudClockFontSize => hud.ClockFontSize;
        internal bool HudDeltaVisible => hud.DeltaVisible;

        // attract screen / scenic view / establishing shot / rule box / click effect
        /// <summary>Test hook: a press the next UI frame sees as if it came from the Input System.</summary>
        internal void InjectPress(bool anyKey = false, bool pointer = false, Vector2 canvasPoint = default, bool v = false, bool h = false, bool enter = false)
        {
            var p = new UIPress { AnyKey = anyKey || v || h || enter, V = v, H = h, Enter = enter, Pointer = pointer };
            if (pointer) p.Screen = CanvasToScreen(canvasPoint);
            UIInput.Inject(p);
        }
        Vector2 CanvasToScreen(Vector2 p)
        {
            var rt = ctx.CanvasRect;
            var world = rt.TransformPoint(p + rt.rect.min);
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, world);
        }
        internal bool TitleAttract => menu.Attract;
        internal bool TitleRevealing => menu.Revealing;
        internal bool TitleButtonsVisible => menu.ButtonsVisible;
        internal bool TitlePromptVisible => menu.PromptVisible;
        internal string TitlePromptText => menu.PromptText;
        internal float TitlePromptAlpha => menu.PromptAlphaNow;
        internal Rect TitlePromptRect => menu.PromptRect(ctx.CanvasRect);
        internal float TitleBackdropAlpha => menu.BackdropAlpha;
        internal bool TitleCreditStripVisible => menu.CreditStripVisible;
        internal bool? FlowEnterEnabled => UIInput.FlowEnterEnabled;
        internal ScenicView.Mode ScenicMode => scenic.Current;
        internal bool HudHidden => scenic.HidesUi;
        internal float HudAlpha => hudGroup.alpha;
        internal bool HudBlocksRaycasts => hudGroup.blocksRaycasts;
        internal bool ScenicButtonVisible => scenic.ButtonVisible;
        internal Rect ScenicButtonRect => UIBuild.CanvasRect(ctx.CanvasRect, scenic.ButtonRect);
        internal void ClickScenicButton() => scenic.ClickButton();
        internal bool ScenicHintVisible => scenic.HintVisible;
        internal string ScenicHintText => scenic.HintText;
        internal Rect ScenicHintRect => UIBuild.CanvasRect(fxRect, scenic.Hint);
        internal bool PauseScenicVisible => menu.PauseScenicVisible;
        internal string PauseScenicText => menu.PauseScenicText;
        internal void ClickPauseScenic() => menu.ClickPauseScenic();
        internal bool CaptionVisible => scenic.CaptionVisible;
        internal string CaptionText => scenic.CaptionText;
        internal float CaptionAlpha => scenic.CaptionAlpha;
        internal Rect CaptionRect => UIBuild.CanvasRect(fxRect, scenic.Caption);
        internal void SetEstablishingAge(float t) => scenic.SetEstablishingAge(t);
        internal bool RulesOpen => hud.RulesOpen;
        internal bool RulesBoxShown => hud.RulesBoxVisible;
        internal bool RulesTabShown => hud.RulesTabVisible;
        internal Rect RulesTabRect => UIBuild.CanvasRect(ctx.CanvasRect, hud.RulesTab);
        internal void ClickRulesTab() => hud.ClickRulesTab();
        internal void ClickRulesHide() => hud.ClickRulesHide();
        internal string RulesHideText => hud.RulesHideText;
        internal float RightColumnBottom => hud.RightColumnBottom;
        internal int ClickFxCount => clickFx.ActiveCount;
        internal bool ClickFxLastBig => clickFx.LastIsBig;
        internal Vector2 ClickFxLastPosition => clickFx.LastPosition;
        internal bool ClickFxOnGrid => clickFx.LastOnGrid;
        internal bool ClickFxRaycastable => clickFx.AnyRaycastTarget || fxCanvas.GetComponent<GraphicRaycaster>() != null;
        internal bool FxCanvasAbove => fxCanvas.sortingOrder > canvas.sortingOrder && fxCanvas.renderMode == canvas.renderMode && fxCanvas.worldCamera == canvas.worldCamera;

        // title / pause / ending
        internal bool TitleVisible => menu.TitleVisible;
        internal string TitleText => menu.TitleText;
        internal void ClickTitleStart() => menu.ClickStart();
        internal void ClickTitleOptions() => menu.ClickTitleOptions();
        internal bool OptionsVisible => menu.OptionsVisible;
        internal void ClickOptionsLanguage() => menu.ClickOptionsLanguage();
        internal void ClickOptionsHints() => menu.ClickOptionsHints();
        internal void ClickOptionsBack() => menu.ClickOptionsBack();
        internal void CloseOptionsLikeEsc() => menu.CloseOptionsLikeEsc();
        internal void SetOptionsVolume(float v) => menu.SetOptionsVolume(v);
        internal float OptionsVolumeValue => menu.OptionsVolumeValue;
        internal string OptionsText => menu.OptionsText;
        internal Rect OptionsCardRect => menu.OptionsCardRect(ctx.CanvasRect);
        internal string OptionsOverflow() => menu.OptionsOverflow();
        internal Rect PauseButtonRect => menu.PauseButtonRect(ctx.CanvasRect);
        internal bool PauseVisible => menu.PauseVisible;
        internal bool PauseButtonVisible => menu.PauseButtonVisible;
        internal void ClickPauseButton() => menu.ClickPauseButton();
        internal void ClickResume() => menu.ClickResume();
        internal void ClickQuitToTitle() => menu.ClickQuit();
        internal void ClickHintsToggle() => menu.ClickHints();
        internal string HintsText => menu.HintsText;
        internal void SetVolumeSlider(float v) => menu.SetVolumeSlider(v);
        internal float VolumeSliderValue => menu.VolumeSliderValue;
        internal bool EndingTitleButtonVisible => flow.TitleButtonVisible;
        internal void ClickEndingTitle() => flow.ClickTitle();
        internal string FlowActionText => flow.ActionText;

        // credits / title backdrop
        internal bool CreditsVisible => menu.CreditsVisible;
        internal bool TitleMenuVisible => menu.TitleMenuVisible;
        internal void ClickCredits() => menu.ClickCredits();
        internal void ClickCreditsBack() => menu.ClickCreditsBack();
        internal string CreditsText => menu.CreditsText;
        internal bool TitleBackdropCovers(out float alpha) => menu.BackdropCoversCanvas(ctx.CanvasRect, out alpha);
        internal void SetTitleBackdrop(bool on) => menu.SetBackdropForTests(on);
        internal bool CreditsFitOnScreen => menu.CreditsFitIn(ctx.CanvasRect);

        // destination icons
        internal bool BadgeShowsIcon(int floor) => panel.BadgeShowsIcon(floor);
        internal Sprite BadgeSprite(int floor) => panel.BadgeSprite(floor);
        internal Color BadgeIconColor(int floor) => panel.BadgeIconColor(floor);
        internal string FloorButtonLayoutProblem(int floor) => panel.ButtonLayoutProblem(floor);
        internal void ChipBadgeStats(out int count, out int icons, out int names, out int textOnly) => bubbles.BadgeStats(out count, out icons, out names, out textOnly);
        internal bool TutorialIconVisible => tutorial.PromptIconVisible;
        internal bool ShopResultIconVisible => shop.ResultIconVisible;
        internal Color DestinationColor(int floor) => UIStyle.Destination(ctx.DestinationIndex(ctx.Building.GetDestination(new FloorIndex(floor))));

        // layout
        internal float BadgeFontSize(int floor) => panel.BadgeFontSize(floor);
        internal string BadgeText(int floor) => panel.BadgeText(floor);
        internal string SpeechOverlapX(float minX, float maxX) => popups.FirstOverlapX(minX, maxX);
        internal string ChipOverlapX(float minX, float maxX) => bubbles.FirstOverlapX(minX, maxX);
        internal int SpeechLeaderCount => popups.LeaderCount;
        internal Vector2 CanvasSize => ctx.CanvasRect.rect.size;
        internal bool WorldToCanvas(Vector3 world, out Vector2 pos) => ctx.WorldToCanvas(world, out pos);
    }
}
