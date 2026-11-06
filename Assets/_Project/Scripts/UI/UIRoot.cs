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
        bool built;

        void OnEnable()
        {
            var bus = GameServices.Events;
            subs.Add(bus.Subscribe<GameStarted>(OnGameStarted));
            subs.Add(bus.Subscribe<LuckChanged>(e => { if (built) hud.OnLuckChanged(e); }));
            subs.Add(bus.Subscribe<TipsChanged>(e => { if (built) hud.OnTipsChanged(e); }));
            subs.Add(bus.Subscribe<FloorLabelChanged>(e => { if (built) panel.RefreshLabel(e.Floor); }));
            subs.Add(bus.Subscribe<LuckJudged>(e => { if (built) popups.OnLuckJudged(e); }));
            subs.Add(bus.Subscribe<GuestSpoke>(e => { if (built) popups.OnGuestSpoke(e); }));
            subs.Add(bus.Subscribe<SettingsChanged>(e => RefreshLanguage()));
            subs.Add(bus.Subscribe<DayPhaseChanged>(OnPhaseChanged));
        }

        void OnDisable() => subs.Clear();

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
            hud.BringLanguageToFront();
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
            panel.Tick(Time.unscaledTime);
            hud.Tick(dt);
            bubbles.Tick();
            popups.Tick(dt);
            shop.Tick();
            flow.Tick();
        }

        void OnGameStarted(GameStarted e)
        {
            if (!built) return;
            hud.OnGameStarted();
            panel.RefreshLabels();
            bubbles.Clear();
            popups.Clear();
        }

        void OnPhaseChanged(DayPhaseChanged e)
        {
            if (!built) return;
            panel.RefreshLabels();
            if (e.Current != DayPhase.Running) popups.Clear();
        }

        void ToggleLanguage()
        {
            if (ctx.ToggleLanguage()) RefreshLanguage(); // else SettingsChanged arrives and refreshes
        }

        void RefreshLanguage()
        {
            if (!built) return;
            panel.RefreshLanguage();
            hud.RefreshLanguage();
            shop.RefreshLanguage();
            flow.Refresh();
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
        internal void ClickLanguage() => hud.ClickLanguage();
    }
}
