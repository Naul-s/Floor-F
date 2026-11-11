using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Title -> ENDINGS card: every ending (<see cref="EndingRecord.All"/>) as a 3x2 grid of slots (the base run's three on
    /// top, the two winter endings and the hidden one below). Seen = its name, the
    /// ending's title, one line from its text and how often it was reached; not seen = "???" and a hint (the Luck endings
    /// state their rule from the data, the hidden 4th floor only whispers). The hidden slot carries the HIDDEN ENDING
    /// badge either way. Opened / closed by <see cref="MenuView"/> (BACK or Esc, like the credits).
    /// </summary>
    sealed class EndingsCardView
    {
        public const int Columns = 3;
        public const float CardWidth = 1180f, SlotGap = 16f, SlotWidth = (CardWidth - 80f - (Columns - 1) * SlotGap) / Columns, SlotHeight = 232f;
        public const float BackWidth = 320f;
        static readonly Color LockedEdge = UIStyle.Hex("#4A4D57"), LockedFill = new Color(0.035f, 0.04f, 0.055f, 0.98f);
        static readonly Color OpenFill = new Color(0.07f, 0.075f, 0.11f, 0.98f);
        static readonly Color HiddenLockedEdge = new Color(HiddenBadge.Ink.r * 0.55f, HiddenBadge.Ink.g * 0.55f, HiddenBadge.Ink.b * 0.55f, 1f);

        sealed class Slot
        {
            public EndingKind Kind;
            public RectTransform Root;
            public Image Edge, Fill;
            public TextMeshProUGUI Caption, Name, Line, Seen;
            public HiddenBadge Badge;
            /// <summary>Winter endings: a small snowflake in the top-right corner once found.</summary>
            public RectTransform Flake;
        }

        readonly UIContext ctx;
        readonly RectTransform card;
        readonly TextMeshProUGUI title, progress;
        readonly Slot[] slots;
        readonly UIBuild.ButtonParts back;

        public EndingsCardView(UIContext ctx, RectTransform parent, UnityEngine.Events.UnityAction onBack)
        {
            this.ctx = ctx;
            card = UIBuild.Box("EndingsCard", parent, UIStyle.TitlePanel, raycast: true).rectTransform;
            UIBuild.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, 600f));
            UIBuild.Outline("Frame", card, UIStyle.CardEdge, 3f, 0f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 22, 22);
            v.spacing = 8f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = UIBuild.Heading(UIBuild.Text("Title", card, "", 48f, UIStyle.Title, TextAlignmentOptions.Center, bold: true));
            progress = UIBuild.Text("Progress", card, "", 24f, UIStyle.InkDim, TextAlignmentOptions.Center);

            var grid = UIBuild.Rect("Slots", card);
            int rows = (EndingRecord.All.Length + Columns - 1) / Columns;
            grid.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * SlotHeight + (rows - 1) * SlotGap;
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(SlotWidth, SlotHeight);
            g.spacing = new Vector2(SlotGap, SlotGap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = Columns;
            g.childAlignment = TextAnchor.UpperCenter;
            slots = new Slot[EndingRecord.All.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = BuildSlot(grid, EndingRecord.All[i]);

            var row = UIBuild.Rect("Row", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            int side = Mathf.RoundToInt((CardWidth - 80f - BackWidth) * 0.5f);
            h.padding = new RectOffset(side, side, 6, 0);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            back = UIBuild.Button("Back", row, "", 24f, UIStyle.Action, onBack);
            back.Label.color = UIStyle.Ink;

            card.gameObject.SetActive(false);
        }

        static Slot BuildSlot(RectTransform grid, EndingKind kind)
        {
            var s = new Slot { Kind = kind };
            s.Edge = UIBuild.Framed("Slot" + kind, grid, OpenFill, UIStyle.CardEdge, 2f);
            s.Root = s.Edge.rectTransform;
            s.Fill = s.Root.GetChild(0).GetComponent<Image>();
            s.Fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = s.Root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(20, 20, 14, 12);
            v.spacing = 6f;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            s.Caption = UIBuild.Text("Caption", s.Root, "", 12f, UIStyle.CardEdge, TextAlignmentOptions.Left);
            s.Caption.characterSpacing = 100f / UIFonts.Grid;
            s.Name = UIBuild.Text("Name", s.Root, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            s.Line = UIBuild.Text("Line", s.Root, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);
            s.Line.lineSpacing = 4f;
            s.Seen = UIBuild.Text("Seen", s.Root, "", 12f, UIStyle.InkDim, TextAlignmentOptions.Left);
            if (EndingRecord.IsHidden(kind))
            {
                s.Badge = new HiddenBadge("HiddenBadge", s.Root, 12f, 10f, 22f);
                s.Badge.Root.anchorMin = s.Badge.Root.anchorMax = s.Badge.Root.pivot = new Vector2(1f, 1f);
                s.Badge.Root.anchoredPosition = new Vector2(-12f, -10f);
            }
            if (UIFormat.IsWinterEnding(kind))
            {
                var corner = UIBuild.Rect("FlakeCorner", s.Root);
                corner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UIBuild.Place(corner, new Vector2(1f, 1f), new Vector2(-22f, -22f), Vector2.zero);
                corner.pivot = new Vector2(0.5f, 0.5f);
                s.Flake = WinterDeco.Snowflake("Flake", corner, Vector2.zero, 22f, WinterDeco.Ice);
            }
            return s;
        }

        public bool Visible => card.gameObject.activeInHierarchy;

        public void Show(bool on)
        {
            card.gameObject.SetActive(on);
            if (on) Refresh();
        }

        public void Refresh()
        {
            bool ko = ctx.Korean;
            var record = ctx.Endings != null ? ctx.Endings.Record : new EndingRecord();
            UIBuild.Set(title, ctx.S(UIStrings.EndingsTitle));
            UIBuild.Set(progress, ctx.F(UIStrings.EndingsProgress, record.UnlockedCount, EndingRecord.Total));
            // the base run's last day (the winter season, once extended, would read "/10")
            int lastDay = ctx.Data != null && ctx.Data.PlayableDayCount > 0 ? ctx.Data.PlayableDayCount
                : ctx.Day != null ? (ctx.Day.PlayableDayCount > 0 ? ctx.Day.PlayableDayCount : ctx.Day.DayCount) : 7;
            int prosperous = ctx.Data != null ? ctx.Data.Rules.prosperousEndingLuck : 70;
            foreach (var s in slots) Fill(s, record, ko, lastDay, prosperous);
            UIBuild.Set(back.Label, ctx.S(UIStrings.Back));
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        void Fill(Slot s, EndingRecord record, bool ko, int lastDay, int prosperous)
        {
            bool open = record.IsUnlocked(s.Kind);
            bool hidden = EndingRecord.IsHidden(s.Kind);
            bool winter = UIFormat.IsWinterEnding(s.Kind);
            s.Edge.color = hidden ? (open ? HiddenBadge.Ink : HiddenLockedEdge) : open ? (winter ? WinterDeco.IceEdge : UIStyle.CardEdge) : LockedEdge;
            s.Fill.color = hidden ? FlowScreensView.HiddenFill : open ? (winter ? WinterDeco.IceFill : OpenFill) : LockedFill;
            UIBuild.Set(s.Caption, hidden && !open ? UIStrings.EndingLocked : UIFormat.EndingName(s.Kind, ko));
            s.Caption.color = hidden ? HiddenBadge.Ink : open ? (winter ? WinterDeco.Ice : UIStyle.CardEdge) : UIStyle.InkDim;
            if (s.Flake != null) s.Flake.gameObject.SetActive(open);
            if (open)
            {
                UIFormat.Ending(s.Kind, ko, out var t, out _);
                UIBuild.Set(s.Name, t);
                s.Name.fontSize = 24f;
                s.Name.color = NameColor(s.Kind);
                UIBuild.Set(s.Line, UIFormat.EndingShortLine(s.Kind, ko));
                s.Line.color = hidden ? HiddenBadge.Ink : UIStyle.Ink;
                UIBuild.Set(s.Seen, ctx.F(UIStrings.EndingSeen, record.Times(s.Kind).ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                UIBuild.Set(s.Name, UIStrings.EndingLocked);
                s.Name.fontSize = 36f;
                s.Name.color = hidden ? HiddenLockedEdge : UIStyle.InkDim;
                UIBuild.Set(s.Line, UIFormat.EndingHint(s.Kind, ko, lastDay, prosperous));
                s.Line.color = hidden ? HiddenBadge.Ink : UIStyle.InkDim;
                UIBuild.Set(s.Seen, "");
            }
            s.Seen.gameObject.SetActive(open);
            if (s.Badge != null)
            {
                s.Badge.SetText(ctx.S(UIStrings.HiddenEnding));
                s.Badge.Alpha = open ? 1f : 0.7f;
            }
        }

        static Color NameColor(EndingKind k) =>
            k == EndingKind.Prosperous || k == EndingKind.WinterMiracle ? UIStyle.Lucky : k == EndingKind.Haunted ? UIStyle.Unlucky
            : k == EndingKind.FourthFloor ? UIStyle.Unlucky : k == EndingKind.SilentNight ? WinterDeco.Ice : UIStyle.Ink;

        /// <summary>The hidden slot's badge breathes while the card is open (only once found).</summary>
        public void Tick(float t)
        {
            if (!Visible) return;
            foreach (var s in slots)
                if (s.Badge != null && ctx.Endings != null && ctx.Endings.Record.IsUnlocked(s.Kind)) s.Badge.Tick(t);
        }

        // ------------------------------------------------------------------ test hooks

        public void ClickBack() => back.Button.onClick.Invoke();
        public int SlotCount => slots.Length;
        public EndingKind SlotKind(int i) => slots[i].Kind;
        public bool SlotUnlocked(int i) => ctx.Endings != null && ctx.Endings.Record.IsUnlocked(slots[i].Kind);
        public bool SlotBadgeVisible(int i) => slots[i].Badge != null && slots[i].Badge.Visible;
        public string SlotBadgeText(int i) => slots[i].Badge != null ? slots[i].Badge.Label.text : null;
        /// <summary>Caption | name | line | seen of slot <paramref name="i"/> (rich text as shown).</summary>
        public string SlotText(int i)
        {
            var s = slots[i];
            return s.Caption.text + " | " + s.Name.text + " | " + s.Line.text + " | " + (s.Seen.gameObject.activeSelf ? s.Seen.text : "");
        }
        public string Text => title.text + "\n" + progress.text;

        public bool FitsIn(RectTransform canvas)
        {
            var r = UIBuild.CanvasRect(canvas, card);
            var c = canvas.rect;
            return r.xMin >= -0.5f && r.yMin >= -0.5f && r.xMax <= c.width + 0.5f && r.yMax <= c.height + 0.5f;
        }

        public Rect CardRect(RectTransform canvas) => UIBuild.CanvasRect(canvas, card);

        /// <summary>null when every slot's texts stay inside the slot (and clear of the badge), else the first offender.</summary>
        public string Overflow(RectTransform canvas)
        {
            foreach (var s in slots)
            {
                var box = UIBuild.CanvasRect(canvas, s.Root);
                foreach (var t in new[] { s.Caption, s.Name, s.Line, s.Seen })
                {
                    if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                    var b = t.textBounds;
                    var r = UIBuild.CanvasRect(canvas, t.rectTransform);
                    float textW = b.size.x, textH = t.preferredHeight;
                    if (r.yMax > box.yMax + 0.5f || r.yMax - textH < box.yMin + 2f) return s.Kind + "." + t.name + " runs out of its slot (" + r + " in " + box + ")";
                    if (r.xMin < box.xMin || r.xMin + textW > box.xMax - 2f) return s.Kind + "." + t.name + " too wide (" + textW + ")";
                    if (s.Badge != null && t == s.Caption)
                    {
                        var badge = UIBuild.CanvasRect(canvas, s.Badge.Root);
                        if (r.xMin + textW > badge.xMin - 4f) return s.Kind + " caption runs into the badge";
                    }
                }
                if (s.Badge != null && !s.Badge.TextFits) return s.Kind + " badge text does not fit";
            }
            return null;
        }
    }
}
