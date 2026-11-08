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
            subs.Add(bus.Subscribe<GuestSpawned>(e => { if (built) tutorial.OnGuestSpawned(e); }));
            subs.Add(bus.Subscribe<GuestDelivered>(e => { if (built) tutorial.OnGuestDelivered(e); }));
            subs.Add(bus.Subscribe<GuestLeft>(e => { if (built) tutorial.OnGuestLeft(e); }));
            subs.Add(bus.Subscribe<PauseChanged>(e => { if (built) menu.Tick(); }));
            subs.Add(bus.Subscribe<LuckStreakChanged>(e => { if (built && !OnTitle) { hud.OnLuckStreakChanged(e); tutorial.OnLuckStreakChanged(e); } }));
            subs.Add(bus.Subscribe<TutorialHoldChanged>(e => { if (built) tutorial.OnTutorialHoldChanged(e); }));
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
            hud.BringLanguageToFront(); // above the modal flow cards, under the coach-mark overlay
            tutorial = new TutorialView(ctx, rt, panel, bubbles, hud);
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
            if (e.Current == DayPhase.Intro || e.Current == DayPhase.Title) hud.ResetStreak(); // a new day starts at 0 silently
            // the attract demo replays a day behind the title: every tutorial day's lesson starts from wave 1
            tutorial.OnPhaseChanged(e);
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
