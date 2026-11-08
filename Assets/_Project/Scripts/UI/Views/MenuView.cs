using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Title screen (DayPhase.Title: bobbing "FLOOR F" logo at the top, a centred stack of separate START ->
    /// IMenuFlow.StartGameFromTitle / CREDITS / language buttons, credit line, CREDITS card; Gameplay's attract demo plays
    /// behind a full-screen dimming backdrop, no panel) and the pause menu (IMenuFlow.IsPaused /
    /// PauseChanged: RESUME, volume slider -> IGameSettings.MasterVolume, language, tutorial hints, quit to title), plus
    /// the HUD pause button. Everything is hidden while no IMenuFlow is registered. Esc / Enter stay with KeyboardInput.
    /// </summary>
    sealed class MenuView
    {
        readonly UIContext ctx;
        /// <summary>Full-screen dimming behind the title / credits cards: the bright floor signs of the attract demo
        /// shone through the translucent panel. The demo stays visible, just pushed back.</summary>
        public static readonly Color Backdrop = new Color(0f, 0f, 0.02f, 0.66f);

        /// <summary>Title layout (1280x720 reference): logo top edge below the screen top, canvas units per logo pixel
        /// (<see cref="PixelLogo"/>), the tagline plate under it, button stack centre above the screen centre.
        /// Logo + tagline end above ~22% of the screen height (the hotel's sign sits below that in the title framing).</summary>
        public const float LogoTop = 12f, LogoPixel = 6f, TaglineGap = 2f, TaglineH = 20f, StackCentreY = 16f, StackGap = 16f;
        /// <summary>Pause card's mini logo: canvas units per logo pixel, gap above the card.</summary>
        public const float PauseLogoPixel = 3f, PauseLogoGap = 10f;
        public static readonly Vector2 StartSize = new Vector2(400f, 76f), ButtonSize = new Vector2(300f, 52f);
        /// <summary>Gold-to-amber TMP vertex gradient matching the logo face (pause title).</summary>
        public static readonly VertexGradient GoldGradient = new VertexGradient(UIStyle.Hex("#FFE066"), UIStyle.Hex("#FFE066"), UIStyle.Hex("#F0962A"), UIStyle.Hex("#F0962A"));

        readonly RectTransform titleRoot, pauseRoot, titleMenu, logo, tagline, buttonStack, creditsCard;
        readonly PixelLogo titleLogo, pauseLogo;
        readonly Image backdrop;
        readonly TextMeshProUGUI taglineText, credits, pauseTitle, volumeCaption, volumeValue;
        readonly TextMeshProUGUI creditsTitle, musicHead, music, soundHead, sound, artHead, art, aiLine, jamLine;
        readonly UIBuild.ButtonParts start, titleLanguage, titleCredits, back, resume, pauseLanguage, hints, quit, pauseButton;
        readonly Slider volume;
        bool titleShown, pauseShown, creditsOpen;

        public MenuView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;

            // ---------------------------------------------------------- title
            titleRoot = UIBuild.Stretch(UIBuild.Rect("Title", canvas));
            backdrop = UIBuild.Box("TitleBackdrop", titleRoot, Backdrop);
            UIBuild.Stretch(backdrop.rectTransform);
            // no shared panel: the logo floats at the top over the night sky, the buttons are a narrow centred stack, and
            // the bottom ~20% stays clear for the street in front of the hotel (only the credit strip sits there)
            titleMenu = UIBuild.Stretch(UIBuild.Rect("TitleMenu", titleRoot));
            // pixel-art logo (block letters + LED floor indicator "F"), then a brass tagline plate under it
            titleLogo = new PixelLogo("Logo", titleMenu, LogoPixel);
            logo = titleLogo.Root;
            UIBuild.Place(logo, new Vector2(0.5f, 1f), new Vector2(0f, -LogoTop), titleLogo.Size);
            tagline = UIBuild.Framed("Tagline", logo, new Color(0.035f, 0.03f, 0.06f, 0.9f), UIStyle.CardEdge, 1f).rectTransform;
            UIBuild.Place(tagline, new Vector2(0.5f, 0f), new Vector2(0f, -TaglineGap), new Vector2(300f, TaglineH));
            tagline.pivot = new Vector2(0.5f, 1f);
            taglineText = UIBuild.Text("Text", tagline, "", 12f, UIStyle.Lucky, TextAlignmentOptions.Center);
            UIBuild.Stretch(taglineText.rectTransform);
            taglineText.textWrappingMode = TextWrappingModes.NoWrap;
            taglineText.characterSpacing = 100f / UIFonts.Grid; // one font pixel at 12px: stays on the grid

            float stackHeight = StartSize.y + 2f * (ButtonSize.y + StackGap);
            var stack = buttonStack = UIBuild.Place(UIBuild.Rect("Buttons", titleMenu), new Vector2(0.5f, 0.5f),
                new Vector2(0f, StackCentreY + stackHeight * 0.5f), new Vector2(StartSize.x, stackHeight));
            stack.pivot = new Vector2(0.5f, 1f); // children hang from the top edge
            start = MenuButton("Start", stack, StartSize, 0f, 36f, UIStyle.Action, UIStyle.Title, 3f, OnStart);
            titleCredits = MenuButton("Credits", stack, ButtonSize, -(StartSize.y + StackGap), 24f, UIStyle.Btn, UIStyle.CardEdge, 2f,
                () => { ctx.Sound(UiSoundKind.Click); SetCredits(true); });
            titleLanguage = MenuButton("Language", stack, ButtonSize, -(StartSize.y + ButtonSize.y + 2f * StackGap), 24f, UIStyle.Btn, UIStyle.CardEdge, 2f,
                () => ctx.ToggleLanguage());

            // ---------------------------------------------------------- credits (title -> CREDITS; Docs/CREDITS.md)
            creditsCard = UIBuild.Box("CreditsCard", titleRoot, UIStyle.TitlePanel, raycast: true).rectTransform;
            UIBuild.Place(creditsCard, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 600f));
            UIBuild.Outline("Frame", creditsCard, UIStyle.CardEdge, 3f, 0f);
            Column(creditsCard, new RectOffset(40, 40, 22, 22), 6f);
            creditsTitle = UIBuild.Text("Title", creditsCard, "", 48f, UIStyle.Title, TextAlignmentOptions.Center, bold: true);
            musicHead = Head(creditsCard);
            music = Body(creditsCard, UIStyle.Ink);
            soundHead = Head(creditsCard);
            sound = Body(creditsCard, UIStyle.Ink);
            artHead = Head(creditsCard);
            art = Body(creditsCard, UIStyle.Ink);
            aiLine = Body(creditsCard, UIStyle.Lucky);
            jamLine = UIBuild.Text("Jam", creditsCard, "", 12f, UIStyle.InkDim, TextAlignmentOptions.Center);
            var brow = Row(creditsCard, 52f);
            brow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(320, 320, 6, 0);
            back = UIBuild.Button("Back", brow, "", 24f, UIStyle.Action, () => { ctx.Sound(UiSoundKind.Click); SetCredits(false); });
            back.Label.color = UIStyle.Ink;
            creditsCard.gameObject.SetActive(false);

            var strip = UIBuild.Box("CreditStrip", titleRoot, new Color(0f, 0f, 0f, 0.7f)).rectTransform;
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = Vector2.zero;
            strip.sizeDelta = new Vector2(0f, 28f);
            credits = UIBuild.Text("Credits", strip, "", 12f, UIStyle.InkDim, TextAlignmentOptions.Center);
            UIBuild.Stretch(credits.rectTransform);
            credits.textWrappingMode = TextWrappingModes.NoWrap;

            // ---------------------------------------------------------- pause
            pauseRoot = UIBuild.Box("PauseDim", canvas, UIStyle.Dim, raycast: true).rectTransform;
            UIBuild.Stretch(pauseRoot);
            var card = UIBuild.Framed("PauseCard", pauseRoot, UIStyle.Card, UIStyle.CardEdge, 3f).rectTransform;
            UIBuild.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 400f));
            Column(card, new RectOffset(36, 36, 26, 28), 14f);
            pauseTitle = UIBuild.Text("Title", card, "", 48f, Color.white, TextAlignmentOptions.Center, bold: true);
            pauseTitle.enableVertexGradient = true; // the logo's gold-to-amber face
            pauseTitle.colorGradient = GoldGradient;
            // the title logo in miniature, sitting on top of the card (outside its layout)
            pauseLogo = new PixelLogo("PauseLogo", card, PauseLogoPixel, glintPhase: 2f);
            pauseLogo.Root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIBuild.Place(pauseLogo.Root, new Vector2(0.5f, 1f), new Vector2(0f, PauseLogoGap), pauseLogo.Size);
            pauseLogo.Root.pivot = new Vector2(0.5f, 0f);
            resume = Wide(card, UIStyle.Action, OnResume);
            var vrow = Row(card, 44f);
            vrow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            volumeCaption = UIBuild.Text("VolumeCaption", vrow, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            volumeCaption.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
            volume = UIBuild.Slider("Volume", vrow, new Vector2(220f, 36f), OnVolume);
            var vle = volume.gameObject.AddComponent<LayoutElement>();
            vle.flexibleWidth = 1f;
            vle.preferredHeight = 36f;
            volumeValue = UIBuild.Text("VolumeValue", vrow, "", 24f, UIStyle.Ink, TextAlignmentOptions.Right);
            volumeValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 72f;
            pauseLanguage = Wide(card, UIStyle.Btn, () => ctx.ToggleLanguage());
            hints = Wide(card, UIStyle.Btn, OnHints);
            quit = Wide(card, UIStyle.Btn, OnQuit);

            // ---------------------------------------------------------- HUD pause button (left of the language toggle)
            pauseButton = UIBuild.Button("Pause", canvas, "II", 24f, UIStyle.Btn, OnPause);
            UIBuild.Place(pauseButton.Rect, new Vector2(1f, 0f), new Vector2(-24f - 170f - 8f, 16f), new Vector2(56f, 36f));
            pauseButton.Label.color = UIStyle.Ink;

            titleRoot.gameObject.SetActive(false);
            pauseRoot.gameObject.SetActive(false);
            pauseButton.Rect.gameObject.SetActive(false);
            RefreshLanguage();
        }

        /// <summary>One free-standing title button (no shared panel) hanging <paramref name="y"/> below the stack top, with its own frame.</summary>
        static UIBuild.ButtonParts MenuButton(string name, RectTransform stack, Vector2 size, float y, float font, Color bg, Color edge,
            float edgeWidth, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIBuild.Button(name, stack, "", font, bg, onClick);
            b.Label.color = UIStyle.Ink;
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            var rt = b.Rect;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = size;
            UIBuild.Outline("Frame", rt, edge, edgeWidth, 0f);
            return b;
        }

        static VerticalLayoutGroup Column(RectTransform card, RectOffset padding, float spacing)
        {
            card.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // the Framed fill
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = padding;
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return v;
        }

        static TextMeshProUGUI Head(RectTransform card)
        {
            var t = UIBuild.Text("Head", card, "", 12f, UIStyle.CardEdge, TextAlignmentOptions.Center);
            t.margin = new Vector4(0f, 8f, 0f, 0f);
            return t;
        }

        static TextMeshProUGUI Body(RectTransform card, Color color)
        {
            var t = UIBuild.Text("Body", card, "", 24f, color, TextAlignmentOptions.Center);
            t.lineSpacing = 4f;
            return t;
        }

        static RectTransform Row(RectTransform parent, float height)
        {
            var row = UIBuild.Rect("Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            return row;
        }

        static UIBuild.ButtonParts Wide(RectTransform parent, Color bg, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIBuild.Button("Button", parent, "", 24f, bg, onClick);
            b.Label.color = UIStyle.Ink;
            b.Rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            return b;
        }

        // ------------------------------------------------------------------ commands (IMenuFlow / IGameSettings only)

        void OnStart() => ctx.Sound(ctx.Menu != null && ctx.Menu.StartGameFromTitle() ? UiSoundKind.Confirm : UiSoundKind.Error);

        void SetCredits(bool show)
        {
            creditsOpen = show && titleShown;
            creditsCard.gameObject.SetActive(creditsOpen);
            titleMenu.gameObject.SetActive(!creditsOpen);
            if (creditsOpen) { RefreshLanguage(); LayoutRebuilder.ForceRebuildLayoutImmediate(creditsCard); }
        }
        void OnPause() => ctx.Sound(ctx.Menu != null && ctx.Menu.SetPaused(true));
        void OnResume() => ctx.Sound(ctx.Menu != null && ctx.Menu.SetPaused(false));

        void OnQuit()
        {
            ctx.Sound(UiSoundKind.Click);
            ctx.Menu?.ReturnToTitle();
        }

        void OnHints()
        {
            ctx.Sound(ctx.Settings != null);
            if (ctx.Settings != null) ctx.Settings.ShowTutorialHints = !ctx.Settings.ShowTutorialHints;
            RefreshLanguage();
        }

        void OnVolume(float v)
        {
            if (ctx.Settings != null) ctx.Settings.MasterVolume = v;
            ShowVolume();
        }

        void ShowVolume()
        {
            float v = ctx.Settings != null ? ctx.Settings.MasterVolume : volume.value;
            UIBuild.Set(volumeValue, Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + "%");
        }

        // ------------------------------------------------------------------ refresh

        public void RefreshLanguage()
        {
            UIBuild.Set(taglineText, ctx.S(UIStrings.Tagline));
            float tw = taglineText.GetPreferredValues(taglineText.text, float.PositiveInfinity, float.PositiveInfinity).x;
            tagline.sizeDelta = new Vector2(Mathf.Ceil(tw) + 28f, TaglineH);
            UIBuild.Set(credits, ctx.S(UIStrings.Credits));
            UIBuild.Set(start.Label, ctx.S(UIStrings.Start));
            UIBuild.Set(titleLanguage.Label, ctx.S(UIStrings.Language));
            UIBuild.Set(titleCredits.Label, ctx.S(UIStrings.CreditsButton));
            UIBuild.Fit(start.Label, 36f); // fixed-size buttons: one line at the biggest grid size that fits (en / ko)
            UIBuild.Fit(titleCredits.Label, 24f);
            UIBuild.Fit(titleLanguage.Label, 24f);
            UIBuild.Set(creditsTitle, ctx.S(UIStrings.CreditsTitle));
            UIBuild.Set(musicHead, ctx.S(UIStrings.CreditsMusicHead));
            UIBuild.Set(music, UIStrings.CreditsMusicCcBy);
            UIBuild.Set(soundHead, ctx.S(UIStrings.CreditsSoundHead));
            UIBuild.Set(sound, ctx.S(UIStrings.CreditsSound));
            UIBuild.Set(artHead, ctx.S(UIStrings.CreditsArtHead));
            UIBuild.Set(art, ctx.S(UIStrings.CreditsArt));
            UIBuild.Set(aiLine, ctx.S(UIStrings.CreditsAi));
            UIBuild.Set(jamLine, ctx.S(UIStrings.CreditsJam));
            UIBuild.Set(back.Label, ctx.S(UIStrings.Back));
            if (creditsOpen) LayoutRebuilder.ForceRebuildLayoutImmediate(creditsCard);
            UIBuild.Set(pauseTitle, ctx.S(UIStrings.Paused));
            UIBuild.Set(resume.Label, ctx.S(UIStrings.Resume));
            UIBuild.Set(volumeCaption, ctx.S(UIStrings.Volume));
            UIBuild.Set(pauseLanguage.Label, ctx.S(UIStrings.Language));
            bool on = ctx.Settings == null || ctx.Settings.ShowTutorialHints;
            UIBuild.Set(hints.Label, ctx.S(on ? UIStrings.HintsOn : UIStrings.HintsOff));
            UIBuild.Set(quit.Label, ctx.S(UIStrings.QuitToTitle));
            if (ctx.Settings != null) volume.SetValueWithoutNotify(ctx.Settings.MasterVolume);
            ShowVolume();
        }

        /// <summary>Settings changed from anywhere (incl. our own buttons): language, hints label, slider position.</summary>
        public void OnSettingsChanged() => RefreshLanguage();

        public void Tick()
        {
            var menu = ctx.Menu;
            var phase = ctx.Phase;
            bool title = menu != null && ctx.HasGame && phase == DayPhase.Title;
            bool paused = menu != null && !title && menu.IsPaused;
            if (title != titleShown)
            {
                titleShown = title;
                titleRoot.gameObject.SetActive(title);
                if (!title) SetCredits(false);
                if (title) RefreshLanguage();
            }
            // gentle idle float, glint and LED flicker; the drop shadow and the tagline stay where they are
            if (title && !creditsOpen) titleLogo.Tick(Time.unscaledTime);
            if (paused != pauseShown)
            {
                pauseShown = paused;
                pauseRoot.gameObject.SetActive(paused);
                if (paused) { RefreshLanguage(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)pauseTitle.transform.parent); }
            }
            if (paused) pauseLogo.Tick(Time.unscaledTime);
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (creditsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCredits(false);
            bool button = menu != null && phase == DayPhase.Running && !paused;
            if (pauseButton.Rect.gameObject.activeSelf != button) pauseButton.Rect.gameObject.SetActive(button);
        }

        /// <summary>Pause card + title above everything else (the HUD language toggle stays below the title).</summary>
        public void BringToFront()
        {
            pauseButton.Rect.SetAsLastSibling();
            titleRoot.SetAsLastSibling();
            pauseRoot.SetAsLastSibling();
        }

        // ------------------------------------------------------------------ test hooks

        public bool TitleVisible => titleRoot.gameObject.activeSelf;
        public bool PauseVisible => pauseRoot.gameObject.activeSelf;
        public bool CreditsVisible => creditsCard.gameObject.activeInHierarchy;
        /// <summary>Logo + title buttons shown (they step aside for the credits card).</summary>
        public bool TitleMenuVisible => titleMenu.gameObject.activeInHierarchy;
        public void ClickCredits() => titleCredits.Button.onClick.Invoke();
        public void ClickCreditsBack() => back.Button.onClick.Invoke();
        public string CreditsText => string.Join("\n", creditsTitle.text, musicHead.text, music.text, soundHead.text, sound.text,
            artHead.text, art.text, aiLine.text, jamLine.text);
        public void SetBackdropForTests(bool on) => backdrop.enabled = on;
        public bool CreditsFitIn(RectTransform canvas)
        {
            var r = UIBuild.CanvasRect(canvas, creditsCard);
            var c = canvas.rect;
            return r.xMin >= -0.5f && r.yMin >= -0.5f && r.xMax <= c.width + 0.5f && r.yMax <= c.height + 0.5f;
        }
        /// <summary>Backdrop active, stretched over the whole canvas and behind the title panel; its alpha.</summary>
        public bool BackdropCoversCanvas(RectTransform canvas, out float alpha)
        {
            alpha = backdrop.color.a;
            if (!backdrop.gameObject.activeInHierarchy) return false;
            var r = UIBuild.CanvasRect(canvas, backdrop.rectTransform);
            var c = canvas.rect;
            return r.xMin <= 0.5f && r.yMin <= 0.5f && r.width >= c.width - 1f && r.height >= c.height - 1f
                   && backdrop.transform.GetSiblingIndex() < titleMenu.GetSiblingIndex();
        }
        public bool PauseButtonVisible => pauseButton.Rect.gameObject.activeSelf;

        /// <summary>Logo = the bobbing pixel-art face, Tagline = the plate under it.</summary>
        public enum TitlePart { Logo, Start, Credits, Language, Tagline }
        RectTransform Part(TitlePart p) => p == TitlePart.Logo ? titleLogo.Face.rectTransform : p == TitlePart.Tagline ? tagline
            : p == TitlePart.Start ? start.Rect : p == TitlePart.Credits ? titleCredits.Rect : titleLanguage.Rect;
        /// <summary>Canvas rect of a title element, default while hidden.</summary>
        public Rect TitlePartRect(RectTransform canvas, TitlePart p) =>
            titleMenu.gameObject.activeInHierarchy ? UIBuild.CanvasRect(canvas, Part(p)) : default;
        public string TaglineText => taglineText.text;
        public float TaglineFontSize => taglineText.fontSize;
        /// <summary>Tagline text inside its plate on one line.</summary>
        public bool TaglineFits => taglineText.GetPreferredValues(taglineText.text, float.PositiveInfinity, float.PositiveInfinity).x <= tagline.rect.width - 8f;
        public PixelLogo TitleLogo => titleLogo;
        public PixelLogo PauseLogo => pauseLogo;
        public Rect PauseLogoRect(RectTransform canvas) => PauseVisible ? UIBuild.CanvasRect(canvas, pauseLogo.Face.rectTransform) : default;
        public Rect PauseCardRect(RectTransform canvas) => PauseVisible ? UIBuild.CanvasRect(canvas, (RectTransform)pauseTitle.transform.parent) : default;
        /// <summary>Label of a title button on one line inside its button (after the grid Fit), and its font size.</summary>
        public bool TitleLabelFits(TitlePart p, out float fontSize)
        {
            var b = p == TitlePart.Start ? start : p == TitlePart.Credits ? titleCredits : titleLanguage;
            fontSize = b.Label.fontSize;
            return b.Label.GetPreferredValues(b.Label.text, float.PositiveInfinity, float.PositiveInfinity).x <= b.Label.rectTransform.rect.width + 0.5f;
        }
        /// <summary>Names of the Images under the title menu other than the buttons' own fills/frames and the logo's own
        /// tagline plate (a shared panel or a box behind the logo would show up here); empty when none.</summary>
        public string TitleMenuPanels()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var img in titleMenu.GetComponentsInChildren<Image>(true))
            {
                var t = img.transform;
                bool own = t == start.Rect || t == titleCredits.Rect || t == titleLanguage.Rect
                           || t.IsChildOf(start.Rect) || t.IsChildOf(titleCredits.Rect) || t.IsChildOf(titleLanguage.Rect)
                           || t.IsChildOf(tagline);
                if (!own) sb.Append(img.name).Append(' ');
            }
            return sb.ToString().Trim();
        }
        public string TitleText => UIStrings.GameName + " | " + taglineText.text + " | " + start.Label.text + " | " + titleCredits.Label.text + " | " + titleLanguage.Label.text;
        public void ClickStart() => start.Button.onClick.Invoke();
        public void ClickTitleLanguage() => titleLanguage.Button.onClick.Invoke();
        public void ClickPauseButton() => pauseButton.Button.onClick.Invoke();
        public void ClickResume() => resume.Button.onClick.Invoke();
        public void ClickQuit() => quit.Button.onClick.Invoke();
        public void ClickHints() => hints.Button.onClick.Invoke();
        public string HintsText => hints.Label.text;
        /// <summary>Moves the slider like a drag would (onValueChanged fires).</summary>
        public void SetVolumeSlider(float v) => volume.value = v;
        public float VolumeSliderValue => volume.value;
    }
}
