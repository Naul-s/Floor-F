using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Title screen (DayPhase.Title: big "FLOOR F", subtitle, START -> IMenuFlow.StartGameFromTitle, language toggle,
    /// credit line, CREDITS card; Gameplay's attract demo plays behind a full-screen dimming backdrop + translucent panel) and the pause menu (IMenuFlow.IsPaused /
    /// PauseChanged: RESUME, volume slider -> IGameSettings.MasterVolume, language, tutorial hints, quit to title), plus
    /// the HUD pause button. Everything is hidden while no IMenuFlow is registered. Esc / Enter stay with KeyboardInput.
    /// </summary>
    sealed class MenuView
    {
        readonly UIContext ctx;
        /// <summary>Full-screen dimming behind the title / credits cards: the bright floor signs of the attract demo
        /// shone through the translucent panel. The demo stays visible, just pushed back.</summary>
        public static readonly Color Backdrop = new Color(0f, 0f, 0.02f, 0.66f);

        readonly RectTransform titleRoot, pauseRoot, titlePanel, creditsCard;
        readonly Image backdrop;
        readonly TextMeshProUGUI gameName, subtitle, titleHint, credits, pauseTitle, volumeCaption, volumeValue;
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
            // translucent fill + hollow gold frame (a Framed box would tint the see-through fill gold)
            var panel = titlePanel = UIBuild.Box("TitlePanel", titleRoot, UIStyle.TitlePanel, raycast: true).rectTransform;
            UIBuild.Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 380f));
            UIBuild.Outline("Frame", panel, UIStyle.CardEdge, 3f, 0f);
            var v = Column(panel, new RectOffset(40, 40, 30, 30), 14f);
            gameName = UIBuild.Text("GameName", panel, UIStrings.GameName, 96f, UIStyle.Title, TextAlignmentOptions.Center, bold: true);
            gameName.textWrappingMode = TextWrappingModes.NoWrap;
            gameName.characterSpacing = 8f;
            subtitle = UIBuild.Text("Subtitle", panel, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            titleHint = UIBuild.Text("Hint", panel, "", 24f, UIStyle.InkDim, TextAlignmentOptions.Center);
            var row = Row(panel, 64f);
            start = UIBuild.Button("Start", row, "", 36f, UIStyle.Action, OnStart);
            start.Label.color = UIStyle.Ink;
            start.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f;
            titleCredits = UIBuild.Button("Credits", row, "", 24f, UIStyle.Btn, () => { ctx.Sound(UiSoundKind.Click); SetCredits(true); });
            titleCredits.Label.color = UIStyle.Ink;
            titleCredits.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            titleLanguage = UIBuild.Button("Language", row, "", 24f, UIStyle.Btn, () => ctx.ToggleLanguage());
            titleLanguage.Label.color = UIStyle.Ink;
            titleLanguage.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

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
            pauseTitle = UIBuild.Text("Title", card, "", 48f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
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
            titlePanel.gameObject.SetActive(!creditsOpen);
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
            UIBuild.Set(subtitle, ctx.S(UIStrings.Subtitle));
            UIBuild.Set(titleHint, ctx.S(UIStrings.TitleHint));
            UIBuild.Set(credits, ctx.S(UIStrings.Credits));
            UIBuild.Set(start.Label, ctx.S(UIStrings.Start));
            UIBuild.Set(titleLanguage.Label, ctx.S(UIStrings.Language));
            UIBuild.Set(titleCredits.Label, ctx.S(UIStrings.CreditsButton));
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
                if (title) { RefreshLanguage(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)gameName.transform.parent); }
            }
            if (paused != pauseShown)
            {
                pauseShown = paused;
                pauseRoot.gameObject.SetActive(paused);
                if (paused) { RefreshLanguage(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)pauseTitle.transform.parent); }
            }
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
        public bool TitlePanelVisible => titlePanel.gameObject.activeInHierarchy;
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
                   && backdrop.transform.GetSiblingIndex() < titlePanel.GetSiblingIndex();
        }
        public bool PauseButtonVisible => pauseButton.Rect.gameObject.activeSelf;
        public string TitleText => gameName.text + " | " + subtitle.text + " | " + start.Label.text;
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
