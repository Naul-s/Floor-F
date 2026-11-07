using System;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// GDD 4 "Elevator": the clickable panel. LED (current label + direction), one button per floor whose text is the
    /// floor's CURRENT label (FloorLabelChanged), destination badge under each label (icon tinted with the destination colour + short name, see DestinationBadge), OPEN/CLOSE,
    /// riders n/8 coloured by what the riders would think of that head count if the doors closed now.
    /// In the Shop phase the floor buttons pick the sticker target (see <see cref="FloorClickInterceptor"/>).
    /// </summary>
    sealed class ElevatorPanelView
    {
        public const float Width = 254f;
        const float BtnW = 112f, BtnH = 66f, Gap = 8f, Pad = 11f, LabelSize = 36f, BadgeH = 26f, BadgeSize = 24f;

        enum Mark { Normal, Target, Applicable, Blocked }

        sealed class FloorButton
        {
            public FloorIndex Floor;
            public UIBuild.ButtonParts Parts;
            public TextMeshProUGUI Label;
            public DestinationBadge Badge;
            public Mark Mark = (Mark)(-1);
        }

        readonly UIContext ctx;
        readonly RectTransform root;
        FloorButton[] buttons = Array.Empty<FloorButton>();
        TextMeshProUGUI ledText, ledGhost, ledArrow, occupancyValue, occupancyCaption, hint;
        UIBuild.ButtonParts openButton, closeButton;
        readonly LuckProfile[] riderProfiles = new LuckProfile[16];

        /// <summary>Shop hook: return true when the click was consumed (sticker placement).</summary>
        public Func<FloorIndex, bool> FloorClickInterceptor;
        /// <summary>Shop hook: sticker currently selected (empty = none) - floors are highlighted by CanBuyAndApply.</summary>
        public StickerId SelectedSticker;

        public ElevatorPanelView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            root = UIBuild.Framed("ElevatorPanel", canvas, UIStyle.PanelPlate, UIStyle.PanelEdge, 3f).rectTransform;
            UIBuild.Place(root, new Vector2(0f, 1f), new Vector2(24f, -92f), new Vector2(Width, 100f));
            Build();
        }

        public RectTransform Root => root;
        public void SetVisible(bool visible) { if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible); }
        public int ButtonCount => buttons.Length;

        void Build()
        {
            int n = ctx.Building != null ? ctx.Building.FloorCount : 0;
            float y = -Pad;

            // LED
            var led = UIBuild.Framed("LED", root, Color.black, new Color(0.15f, 0.15f, 0.17f), 3f);
            UIBuild.Place(led.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Width - 2 * Pad, 86f));
            var ghost = ledGhost = UIBuild.Text("Ghost", led.transform, "8", 72f, UIStyle.LedGhost, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(ghost.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-24f, 0f), new Vector2(130f, 86f));
            ghost.characterSpacing = 6f;
            ledText = UIBuild.Text("Label", led.transform, "", 72f, UIStyle.Led, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(ledText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-24f, 0f), new Vector2(130f, 86f));
            ledText.characterSpacing = 6f;
            ledText.textWrappingMode = TextWrappingModes.NoWrap;
            ledArrow = UIBuild.Text("Arrow", led.transform, "", 36f, UIStyle.Led, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(ledArrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(36f, 60f));
            y -= 86f + 14f;

            // floor buttons: classic two-column panel, highest floors on top (even left / odd right, 1 alone at the bottom)
            buttons = new FloorButton[n];
            int rows = (n + 1) / 2;
            float gridW = 2 * BtnW + Gap;
            for (int i = 0; i < n; i++)
            {
                int p = i + 1;
                int row, col;
                if (n % 2 == 1)
                {
                    if (p == 1) { row = rows - 1; col = -1; }
                    else { row = rows - 2 - (p - 2) / 2; col = (p - 2) % 2; }
                }
                else { row = rows - 1 - (p - 1) / 2; col = (p - 1) % 2; }
                float x = col < 0 ? 0f : -gridW / 2f + BtnW / 2f + col * (BtnW + Gap);
                float by = y - row * (BtnH + Gap);
                buttons[i] = MakeFloorButton(new FloorIndex(i), x, by);
            }
            y -= rows * BtnH + (rows - 1) * Gap + 14f;

            // doors
            openButton = UIBuild.Button("Open", root, "", 24f, UIStyle.Btn, () => ctx.Sound(ctx.Control != null && ctx.Control.OpenDoors()));
            UIBuild.Place(openButton.Rect, new Vector2(0.5f, 1f), new Vector2(-(BtnW + Gap) / 2f, y), new Vector2(BtnW, 44f));
            closeButton = UIBuild.Button("Close", root, "", 24f, UIStyle.Btn, () => ctx.Sound(ctx.Control != null && ctx.Control.CloseDoors()));
            UIBuild.Place(closeButton.Rect, new Vector2(0.5f, 1f), new Vector2((BtnW + Gap) / 2f, y), new Vector2(BtnW, 44f));
            y -= 44f + 10f;

            // occupancy
            occupancyCaption = UIBuild.Text("OccCaption", root, "", 24f, UIStyle.InkDim, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(occupancyCaption.rectTransform, new Vector2(0f, 1f), new Vector2(Pad + 4f, y), new Vector2(100f, 36f));
            occupancyValue = UIBuild.Text("OccValue", root, "0/8", 36f, UIStyle.Ink, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(occupancyValue.rectTransform, new Vector2(1f, 1f), new Vector2(-Pad - 4f, y), new Vector2(110f, 36f));
            y -= 36f + Pad;

            root.sizeDelta = new Vector2(Width, -y);

            hint = UIBuild.Text("Hint", root, "", 12f, UIStyle.InkDim, TextAlignmentOptions.Center);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            hint.rectTransform.sizeDelta = new Vector2(Width + 20f, 20f);
            RefreshLabels();
            RefreshLanguage();
        }

        FloorButton MakeFloorButton(FloorIndex floor, float x, float y)
        {
            var fb = new FloorButton { Floor = floor };
            fb.Parts = UIBuild.Button("Floor " + floor.PhysicalNumber, root, "", LabelSize, UIStyle.Btn, () => OnFloorClicked(floor));
            UIBuild.Place(fb.Parts.Rect, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(BtnW, BtnH));
            fb.Label = fb.Parts.Label;
            fb.Label.rectTransform.offsetMin = new Vector2(2f, BadgeH + 2f);
            fb.Label.textWrappingMode = TextWrappingModes.NoWrap;
            fb.Badge = new DestinationBadge(fb.Parts.Rect, BadgeSize, DestinationBadge.NameMode.Truncate);
            var brt = fb.Badge.Rect;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(2f, 3f);
            brt.offsetMax = new Vector2(-2f, BadgeH);
            return fb;
        }

        void OnFloorClicked(FloorIndex floor)
        {
            if (FloorClickInterceptor != null && FloorClickInterceptor(floor)) return; // the shop publishes its own sound
            ctx.Sound(ctx.Control != null && ctx.Control.RequestFloor(floor));
        }

        // ------------------------------------------------------------------ refresh

        public void RefreshLabels()
        {
            if (ctx.Building == null) return;
            foreach (var b in buttons) RefreshLabel(b.Floor);
        }

        public void RefreshLabel(FloorIndex floor)
        {
            if (ctx.Building == null || floor.Value >= buttons.Length) return;
            var b = buttons[floor.Value];
            string text = ctx.Building.GetLabel(floor).Text;
            if (!string.Equals(b.Label.text, text, StringComparison.Ordinal)) { b.Label.text = text; UIBuild.Fit(b.Label, LabelSize); }
            b.Badge.Set(ctx, ctx.Building.GetDestination(floor));
        }

        public void RefreshLanguage()
        {
            UIBuild.Set(openButton.Label, ctx.S(UIStrings.Open));
            UIBuild.Set(closeButton.Label, ctx.S(UIStrings.Close));
            UIBuild.Set(occupancyCaption, ctx.S(UIStrings.InCar));
            UIBuild.Set(hint, ctx.S(UIStrings.PanelHint));
            RefreshLabels();
        }

        public void Tick(float time)
        {
            if (!ctx.HasGame) return;
            var e = ctx.Elevator;
            var phase = ctx.Day.Phase;
            bool shopMode = phase == DayPhase.Shop && ctx.Shop != null && !SelectedSticker.IsEmpty;

            // LED
            string led = ctx.Building.GetLabel(e.CurrentFloor).Text;
            if (!string.Equals(ledText.text, led, StringComparison.Ordinal))
            {
                ledText.text = led;
                ledGhost.text = new string('8', Mathf.Clamp(led.Length, 1, 3)); // unlit segments behind the digits
            }
            UIBuild.Set(ledArrow, e.Motion == ElevatorMotion.MovingUp ? "▲" : e.Motion == ElevatorMotion.MovingDown ? "▼" : "");

            // buttons
            float pulse = 0.75f + 0.25f * Mathf.Sin(time * 8f);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                Mark m;
                if (shopMode)
                {
                    var r = ctx.Shop.CanBuyAndApply(SelectedSticker, b.Floor);
                    m = r == StickerApplyResult.Applied ? Mark.Applicable : r == StickerApplyResult.NotEnoughTips ? Mark.Normal : Mark.Blocked;
                }
                else m = e.HasTarget && e.TargetFloor == b.Floor && phase == DayPhase.Running ? Mark.Target : Mark.Normal;

                if (m != b.Mark)
                {
                    b.Mark = m;
                    var bg = m == Mark.Target ? UIStyle.BtnTarget : m == Mark.Applicable ? UIStyle.BtnApplicable : m == Mark.Blocked ? UIStyle.BtnBlocked : UIStyle.Btn;
                    b.Parts.Background.color = bg;
                    b.Label.color = m == Mark.Blocked ? UIStyle.InkDim : UIStyle.InkOn(bg);
                }
                if (m == Mark.Applicable) b.Parts.Background.color = Color.Lerp(UIStyle.Btn, UIStyle.BtnApplicable, pulse);
            }

            // doors
            bool open = e.Doors == DoorState.Open || e.Doors == DoorState.Opening;
            openButton.Background.color = open ? UIStyle.ActionHot : UIStyle.Btn;
            closeButton.Background.color = open ? UIStyle.Btn : UIStyle.Action;
            openButton.Label.color = closeButton.Label.color = UIStyle.Ink;

            // occupancy preview
            int count = 0;
            var riders = e.Riders;
            for (int i = 0; i < riders.Count && count < riderProfiles.Length; i++)
                if (ctx.Guests != null && ctx.Guests.TryGet(riders[i], out var info) && !info.HideNumbers) riderProfiles[count++] = info.Luck;
            var v = UIFormat.OccupancyPreview(e.Occupancy, riderProfiles, count);
            for (int i = 0; i < count; i++) riderProfiles[i] = null;
            UIBuild.Set(occupancyValue, e.Occupancy.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + e.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            occupancyValue.color = v == LuckVerdict.Lucky ? UIStyle.Lucky : v == LuckVerdict.Unlucky ? UIStyle.Unlucky : UIStyle.Ink;
        }

        // ------------------------------------------------------------------ test hooks

        public string ButtonText(int floor) => floor >= 0 && floor < buttons.Length ? buttons[floor].Label.text : null;
        public bool IsApplicable(int floor) => floor >= 0 && floor < buttons.Length && buttons[floor].Mark == Mark.Applicable;
        public void Click(int floor) { if (floor >= 0 && floor < buttons.Length) buttons[floor].Parts.Button.onClick.Invoke(); }
        public void ClickOpen() => openButton.Button.onClick.Invoke();
        public void ClickClose() => closeButton.Button.onClick.Invoke();
        public string LedText => ledText.text;
        public RectTransform FloorButtonRect(FloorIndex floor) => floor.Value >= 0 && floor.Value < buttons.Length ? buttons[floor.Value].Parts.Rect : null;
        public RectTransform OpenRect => openButton.Rect;
        public RectTransform CloseRect => closeButton.Rect;
        public float BadgeFontSize(int floor) => floor >= 0 && floor < buttons.Length ? buttons[floor].Badge.Name.fontSize : 0f;
        public string BadgeText(int floor) => floor >= 0 && floor < buttons.Length ? buttons[floor].Badge.Text : null;
        public bool BadgeShowsIcon(int floor) => floor >= 0 && floor < buttons.Length && buttons[floor].Badge.ShowsIcon;
        public Sprite BadgeSprite(int floor) => floor >= 0 && floor < buttons.Length && buttons[floor].Badge.ShowsIcon ? buttons[floor].Badge.Icon.sprite : null;
        public Color BadgeIconColor(int floor) => floor >= 0 && floor < buttons.Length ? buttons[floor].Badge.Icon.color : Color.clear;
    }
}
