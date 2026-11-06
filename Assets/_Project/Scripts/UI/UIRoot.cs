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
        bool built;

        void OnEnable()
        {
            var bus = GameServices.Events;
            subs.Add(bus.Subscribe<GameStarted>(OnGameStarted));
            // the title's attract demo publishes guest/elevator/luck events too: the title card stays clean
            subs.Add(bus.Subscribe<LuckChanged>(e => { if (built && !OnTitle) hud.OnLuckChanged(e); }));
            subs.Add(bus.Subscribe<TipsChanged>(e => { if (built && !OnTitle) hud.OnTipsChanged(e); }));
            subs.Add(bus.Subscribe<FloorLabelChanged>(e => { if (built) panel.RefreshLabel(e.Floor); }));
            subs.Add(bus.Subscribe<LuckJudged>(e => { if (built && !OnTitle) { popups.OnLuckJudged(e); tutorial.OnLuckJudged(e); } }));
            subs.Add(bus.Subscribe<GuestSpoke>(OnGuestSpoke));
            subs.Add(bus.Subscribe<SettingsChanged>(e => RefreshLanguage()));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnPhaseChanged));
            subs.Add(bus.Subscribe<GuestBoarded>(e => { if (built) tutorial.OnGuestBoarded(e); }));
            subs.Add(bus.Subscribe<DoorsClosed>(e => { if (built) tutorial.OnDoorsClosed(e); }));
            subs.Add(bus.Subscribe<DoorsOpened>(e => { if (built) tutorial.OnDoorsOpened(e); }));
            subs.Add(bus.Subscribe<ElevatorDeparted>(e => { if (built) tutorial.OnDeparted(e); }));
            subs.Add(bus.Subscribe<PauseChanged>(e => { if (built) menu.Tick(); }));
        }

        void OnDisable() => subs.Clear();

        bool OnTitle => ctx.Phase == DayPhase.Title || (ctx.Menu != null && ctx.Menu.IsOnTitle);

        void Start()
        {
            ctx.ResolveServices();
            if (!ctx.HasGame)
            {
                Debug.LogWarning("[UI] gameplay services missing - HUD not built. Registered: " + string.Join(", ", GameServices.RegisteredNames()));
                return;
            }
            BuildCanvas();
            EnsureEventSystem();
            var rt = ctx.CanvasRect;
            bubbles = new GuestBubblesView(ctx, rt);
            popups = new PopupsView(ctx, rt);
            panel = new ElevatorPanelView(ctx, rt);
            hud = new HudView(ctx, rt, ToggleLanguage);
            shop = new ShopView(ctx, rt, panel);
            flow = new FlowScreensView(ctx, rt);
            tutorial = new TutorialView(ctx, rt, panel);
            hud.BringLanguageToFront();
            menu = new MenuView(ctx, rt);
            menu.BringToFront();
            built = true;
            SetLayerRecursively(canvas.transform, PresentationContract.UiLayer);
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
            ctx.CanvasRect = (RectTransform)go.transform;
        }

        /// <summary>Screen Space - Camera on the Rendering UI camera; Overlay only as a fallback while it is missing.</summary>
        void AttachCamera()
        {
            var camGo = GameObject.Find(PresentationContract.UiCameraName);
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (cam != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = UIStyle.PlaneDistance;
            }
            else if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                Debug.LogWarning("[UI] '" + PresentationContract.UiCameraName + "' not found - Overlay fallback until it appears");
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        void Update()
        {
            if (!built) return;
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == null) AttachCamera();
            float dt = Time.deltaTime;
            bool onTitle = ctx.Phase == DayPhase.Title;
            panel.SetVisible(!onTitle);
            hud.SetVisible(!onTitle);
            panel.Tick(Time.unscaledTime);
            hud.Tick(dt);
            bubbles.Tick();
            popups.Tick(dt);
            shop.Tick();
            flow.Tick();
            tutorial.Tick(dt, Time.unscaledTime);
            menu.Tick();
        }

        void OnGameStarted(GameStarted e)
        {
            if (!built) return;
            hud.OnGameStarted();
            panel.RefreshLabels();
            bubbles.Clear();
            popups.Clear();
            tutorial.Reset();
            flow.LastSpecialLine = null;
        }

        void OnGuestSpoke(GuestSpoke e)
        {
            if (!built || OnTitle) return;
            popups.OnGuestSpoke(e);
            // the mystery guest's last words come right before the 4th-floor ending - the ending card quotes them
            if (e.LineTrigger == GuestLineTriggers.Special) flow.LastSpecialLine = e.Text;
        }

        void OnPhaseChanged(DayPhaseChanged e)
        {
            if (!built) return;
            panel.RefreshLabels();
            if (e.Current == DayPhase.Ending) popups.ClearPopups(); // keep the last speech bubble behind the card
            else if (e.Current != DayPhase.Running) popups.Clear();
            if (e.Current == DayPhase.Intro) flow.LastSpecialLine = null;
            // the attract demo replays day 1 behind the title: a new run's tutorial starts from step 1
            if (e.Current == DayPhase.Title || (e.Current == DayPhase.Intro && e.Previous == DayPhase.Title)) tutorial.Reset();
            menu.Tick();
        }

        void ToggleLanguage() => ctx.ToggleLanguage(); // SettingsChanged arrives and refreshes

        void RefreshLanguage()
        {
            if (!built) return;
            panel.RefreshLanguage();
            hud.RefreshLanguage();
            shop.RefreshLanguage();
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
        internal int ActivePopupCount => popups.ActivePopupCount;
        internal int ActiveSpeechCount => popups.ActiveSpeechCount;
        internal string LastPopupText => popups.LastPopupText;
        internal int VisibleBubbleCount => bubbles.VisibleCount;
        internal bool SettingsServiceBound => ctx.Settings != null && GameServices.TryGet(out IGameSettings s) && ReferenceEquals(s, ctx.Settings);
        /// <summary>Every visible speech bubble is outside the shaft column (null) or the first offender's description.</summary>
        internal string SpeechShaftOverlap() => popups.FirstShaftOverlap();
        /// <summary>Every visible speech bubble is clear of every luck popup (null) or the first offender's description.</summary>
        internal string SpeechPopupOverlap() => popups.FirstPopupOverlap();
        internal string SpeechTexts => popups.SpeechTexts;
        internal void ShowFontCompare(bool show)
        {
            if (show && fontCompare == null) { fontCompare = FontSampleCard.Build(ctx.CanvasRect); SetLayerRecursively(fontCompare.transform, PresentationContract.UiLayer); }
            else if (!show && fontCompare != null) { Destroy(fontCompare); fontCompare = null; }
        }
        GameObject fontCompare;
        internal int TextCount => canvas.GetComponentsInChildren<TMPro.TMP_Text>(true).Length;
        internal bool FontIsGalmuri => !UIFonts.IsFallback && UIFonts.IsGalmuri(UIFonts.Main);
        /// <summary>null when every UI text uses the runtime Galmuri asset at a 12px-grid size, else the first offender.</summary>
        internal string FirstOffGridOrForeignText()
        {
            foreach (var t in canvas.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.font != UIFonts.Main || !UIFonts.IsGalmuri(t.font)) return t.name + " font " + (t.font != null ? t.font.name : "<null>");
                if (Mathf.Abs(t.fontSize % UIFonts.Grid) > 0.01f) return t.name + " size " + t.fontSize;
                if (t.enableAutoSizing) return t.name + " auto-sizes";
            }
            return null;
        }
        internal void ClickLanguage() => hud.ClickLanguage();

        // tutorial
        internal TutorialView.Step TutorialStep => tutorial.CurrentStep;
        internal string TutorialPrompt => tutorial.PromptText;
        internal string TutorialWhy => tutorial.WhyText;
        internal string TutorialTarget => tutorial.TargetName;

        // title / pause / ending
        internal bool TitleVisible => menu.TitleVisible;
        internal string TitleText => menu.TitleText;
        internal void ClickTitleStart() => menu.ClickStart();
        internal void ClickTitleLanguage() => menu.ClickTitleLanguage();
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
