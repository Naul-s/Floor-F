using System.Collections.Generic;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Number chips above guests (GDD 4: lucky number + 0-2 unlucky over every head, destination as an icon tinted with its colour;
    /// the name only when it fits whole beside the icon - DestinationBadge.NameMode.FullOrNone).
    /// Waiting guests: one COMPACT one-row chip ([destination icon] numbers, patience bar underneath) per guest, packed
    /// left-to-right in line order inside the floor's own headroom (between the heads and the ceiling) - a queue never
    /// hides the guests beside it, nor the ones waiting on the floor above.
    /// Riders: grouped in a 4-wide grid hugging the left side of the car (they stand too close to label one by one).
    /// Riders' chips grow an odometer row on top (rule 3: "↕5" = floors travelled since boarding, GuestInfo.FloorsTravelled),
    /// gold / red when stepping out now would be lucky / unlucky (UIFormat.DistanceVerdict, Friday the 13th included).
    /// Position: IActorLocator -> Camera.main.WorldToViewportPoint -> PresentationContract.ViewportToScreen.
    /// </summary>
    sealed class GuestBubblesView
    {
        public const float ChipW = 80f, ChipH = 62f, BadgeH = 26f, BadgeSize = 24f;
        /// <summary>Odometer row on top of a rider's chip; rider chips are <see cref="RiderChipH"/> tall.</summary>
        public const float OdoH = 24f, RiderChipH = ChipH + OdoH;
        public const float Spacing = 3f;
        const float HeadHeight = 1.45f, HeadFloor = 1.25f, ColumnMargin = 4f;
        /// <summary>Waiting chip: one row, <see cref="WaitH"/> tall (fits the headroom of a floor: ~34px at 1280x720);
        /// icon-only badge (a name would not fit: the text fallback gets <see cref="WaitTextBadge"/>), numbers at their
        /// natural width up to <see cref="WaitNumMax"/>.</summary>
        public const float WaitH = 32f, WaitBadge = 26f, WaitTextBadge = 58f, WaitNumMax = 78f, WaitGap = 2f;
        public const int RiderColumns = 3;

        public static readonly Color OdoNeutral = new Color(0.2f, 0.21f, 0.26f, 1f);

        sealed class Chip
        {
            public GuestId Id;
            public RectTransform Rect;
            public Image Edge;
            public TextMeshProUGUI Numbers;
            public DestinationBadge Badge;
            public RectTransform Patience, Track;
            /// <summary>Current width (waiting chips are as wide as their numbers) and the numbers' natural width at 24px.</summary>
            public float W = ChipW, NumW;
            public bool LaidOut, TextBadge;
            public Image PatienceImage;
            public RectTransform Odo;
            public Image OdoFill;
            public TextMeshProUGUI OdoText;
            public bool Riding;
            public int ShownFloors = -1;
            public LuckVerdict OdoVerdict;
            public int Seen;
            public DestinationId ShownDestination;
            public bool LanguageKorean;
        }

        readonly UIContext ctx;
        readonly RectTransform layer;
        readonly Dictionary<int, Chip> chips = new Dictionary<int, Chip>();
        readonly Stack<Chip> pool = new Stack<Chip>();
        readonly List<GuestId> waiting = new List<GuestId>();
        readonly List<int> remove = new List<int>();
        int frame;

        public GuestBubblesView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            layer = UIBuild.Stretch(UIBuild.Rect("GuestBubbles", canvas));
        }

        public RectTransform Layer => layer;
        public int VisibleCount
        {
            get { int n = 0; foreach (var c in chips.Values) if (c.Rect.gameObject.activeSelf) n++; return n; }
        }

        Chip Make()
        {
            if (pool.Count > 0) { var p = pool.Pop(); p.Rect.gameObject.SetActive(true); return p; }
            var c = new Chip();
            c.Edge = UIBuild.Framed("Chip", layer, new Color(0.06f, 0.06f, 0.09f, 0.92f), Color.white, 2f);
            c.Rect = c.Edge.rectTransform;
            c.Rect.anchorMin = c.Rect.anchorMax = Vector2.zero;
            c.Rect.pivot = new Vector2(0.5f, 0f);
            c.Rect.sizeDelta = new Vector2(ChipW, ChipH);
            // everything hangs from the bottom edge: a rider's chip grows upward by the odometer row
            c.Numbers = UIBuild.Text("Numbers", c.Rect, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(c.Numbers.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, ChipH - 1f - 26f), new Vector2(ChipW - 4f, 26f));
            c.Numbers.textWrappingMode = TextWrappingModes.NoWrap;
            c.OdoFill = UIBuild.Box("Odometer", c.Rect, OdoNeutral);
            c.Odo = c.OdoFill.rectTransform;
            UIBuild.Place(c.Odo, new Vector2(0.5f, 0f), new Vector2(0f, ChipH - 2f), new Vector2(ChipW - 6f, OdoH - 2f));
            c.OdoText = UIBuild.Text("Distance", c.Odo, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(c.OdoText.rectTransform);
            c.OdoText.textWrappingMode = TextWrappingModes.NoWrap;
            c.Odo.gameObject.SetActive(false);
            c.Badge = new DestinationBadge(c.Rect, BadgeSize, DestinationBadge.NameMode.FullOrNone);
            UIBuild.Place(c.Badge.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(ChipW - 6f, BadgeH));
            var track = UIBuild.Box("PatienceTrack", c.Rect, new Color(0f, 0f, 0f, 0.9f));
            c.Track = track.rectTransform;
            UIBuild.Place(c.Track, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(ChipW - 6f, 4f));
            c.PatienceImage = UIBuild.Box("Patience", track.transform, UIStyle.Good);
            c.Patience = c.PatienceImage.rectTransform;
            c.Patience.anchorMin = Vector2.zero;
            c.Patience.anchorMax = new Vector2(1f, 1f);
            c.Patience.pivot = new Vector2(0f, 0.5f);
            c.Patience.offsetMin = c.Patience.offsetMax = Vector2.zero;
            return c;
        }

        Chip Get(in GuestInfo info)
        {
            if (chips.TryGetValue(info.Id.Value, out var c)) return c;
            c = Make();
            c.Id = info.Id;
            c.Edge.color = ctx.GuestColor(info.Archetype);
            c.Numbers.text = UIFormat.Numbers(info.Luck, info.HideNumbers, ctx.LuckyForAll);
            c.Numbers.fontSize = UIFonts.Px(24f);
            if (c.Numbers.font != null) c.Numbers.font.TryAddCharacters(c.Numbers.text);
            c.NumW = Mathf.Clamp(Mathf.Ceil(c.Numbers.GetPreferredValues(c.Numbers.text, float.PositiveInfinity, float.PositiveInfinity).x) + 1f, 14f, WaitNumMax);
            c.LaidOut = false; // Fill lays it out (waiting: compact row / riding: numbers over the badge + odometer)
            c.ShownDestination = new DestinationId("\u0000");
            chips.Add(info.Id.Value, c);
            return c;
        }

        void Fill(Chip c, in GuestInfo info, bool riding)
        {
            bool textBadge = !riding && UIIcons.For(ctx.Destination(info.Destination)) == null;
            if (!c.LaidOut || c.Riding != riding || c.TextBadge != textBadge) ApplyLayout(c, riding, textBadge);
            if (c.ShownDestination != info.Destination || c.LanguageKorean != ctx.Korean)
            {
                c.ShownDestination = info.Destination;
                c.LanguageKorean = ctx.Korean;
                c.Badge.Set(ctx, info.Destination);
            }
            float p = Mathf.Clamp01(info.Patience01);
            c.Patience.anchorMax = new Vector2(p, 1f);
            c.Patience.offsetMax = Vector2.zero;
            c.PatienceImage.color = UIStyle.Patience(p);
            if (riding) FillOdometer(c, info);
            c.Seen = frame;
        }

        /// <summary>
        /// Riding: 80px chip, numbers over a full-width badge (icon + name when it fits), odometer row on top.
        /// Waiting: one compact row - [icon] numbers - over a thin patience bar.
        /// </summary>
        static void ApplyLayout(Chip c, bool riding, bool textBadge)
        {
            c.LaidOut = true;
            c.Riding = riding;
            c.TextBadge = textBadge;
            c.Odo.gameObject.SetActive(riding);
            if (riding)
            {
                c.W = ChipW;
                c.Rect.sizeDelta = new Vector2(ChipW, RiderChipH);
                UIBuild.Place(c.Numbers.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, ChipH - 1f - 26f), new Vector2(ChipW - 4f, 26f));
                UIBuild.Place(c.Badge.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(ChipW - 6f, BadgeH));
                UIBuild.Place(c.Track, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(ChipW - 6f, 4f));
            }
            else
            {
                float bw = textBadge ? WaitTextBadge : WaitBadge;
                c.W = 3f + bw + 3f + c.NumW + 4f;
                c.Rect.sizeDelta = new Vector2(c.W, WaitH);
                UIBuild.Place(c.Badge.Rect, Vector2.zero, new Vector2(3f, 6f), new Vector2(bw, WaitH - 9f));
                UIBuild.Place(c.Numbers.rectTransform, Vector2.zero, new Vector2(3f + bw + 3f, 5f), new Vector2(c.NumW, WaitH - 8f));
                UIBuild.Place(c.Track, Vector2.zero, new Vector2(2f, 2f), new Vector2(c.W - 4f, 3f));
            }
            UIBuild.Fit(c.Numbers, 24f);
            c.ShownDestination = new DestinationId("\u0000"); // the badge re-fits its new rect
            c.ShownFloors = -1;
        }

        /// <summary>Test hook: guest id -> floors shown instead of GuestInfo.FloorsTravelled (wide numbers, colours).</summary>
        internal static readonly Dictionary<int, int> DistanceOverride = new Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DistanceOverride.Clear();

        void FillOdometer(Chip c, in GuestInfo info)
        {
            int floors = Mathf.Max(0, DistanceOverride.TryGetValue(info.Id.Value, out int forced) ? forced : info.FloorsTravelled);
            var v = UIFormat.DistanceVerdict(info.Luck, info.HideNumbers, floors, ctx.Day != null && ctx.Day.IsFriday13, ctx.LuckyForAll);
            if (floors == c.ShownFloors && v == c.OdoVerdict) return;
            c.ShownFloors = floors;
            c.OdoVerdict = v;
            c.OdoText.text = UIFormat.Distance(floors);
            c.OdoFill.color = OdoColor(v);
            c.OdoText.color = v == LuckVerdict.Lucky ? UIStyle.Dark : v == LuckVerdict.Unlucky ? Color.white : UIStyle.Ink;
        }

        public static Color OdoColor(LuckVerdict v) => v == LuckVerdict.Lucky ? UIStyle.Lucky : v == LuckVerdict.Unlucky ? UIStyle.Unlucky : OdoNeutral;

        /// <summary>
        /// Bottom-right corner of the riders' chip grid (left of the car / the floor-sign column) and its row count; the
        /// grid is pulled down when its top row would leave the screen. PopupsView anchors rider speech on the same point.
        /// </summary>
        public static bool RiderGridAnchor(UIContext ctx, float canvasHeight, int riderCount, out Vector2 anchor, out int rows)
        {
            rows = Mathf.Max(1, (riderCount + RiderColumns - 1) / RiderColumns);
            anchor = default;
            if (ctx.Locator == null) return false;
            float halfCar = ctx.CarWidth * 0.5f + 0.1f;
            var car = UIContext.ToVector3(ctx.Locator.CarPosition);
            if (!ctx.WorldToCanvas(car + new Vector3(-halfCar, 0.1f, 0f), out anchor)) return false;
            if (ctx.CenterColumn(ColumnMargin, out float colMin, out _)) anchor.x = Mathf.Min(anchor.x, colMin);
            // never below the 2nd floor's slab: on the ground floor the grid would cover the lobby and the street shops
            if (ctx.Layout != null && ctx.Layout.FloorCount > 1 &&
                ctx.WorldToCanvas(UIContext.ToVector3(ctx.Layout.GetFloorAnchor(new FloorIndex(1))) + new Vector3(-halfCar, 0.1f, 0f), out var low))
                anchor.y = Mathf.Max(anchor.y, low.y);
            anchor.y = Mathf.Min(anchor.y, canvasHeight - 8f - rows * (RiderChipH + Spacing));
            return true;
        }

        public void Tick()
        {
            frame++;
            bool show = ctx.HasGame && ctx.Guests != null && ctx.Locator != null && ctx.Day.Phase == DayPhase.Running;
            if (show)
            {
                // chips never cover the floor signs / shaft: waiting chips start right of it, the riders' grid ends left of it
                bool haveColumn = ctx.CenterColumn(ColumnMargin, out float colMin, out float colMax);
                int floors = ctx.Building.FloorCount;
                for (int f = 0; f < floors; f++)
                {
                    int n = ctx.Guests.GetWaiting(new FloorIndex(f), waiting);
                    float minLeft = haveColumn ? colMax : float.MinValue;
                    float floorH = ctx.Layout != null ? ctx.Layout.FloorHeight : 3f;
                    for (int i = 0; i < n; i++)
                    {
                        if (!ctx.Guests.TryGet(waiting[i], out var info) || info.State != GuestState.Waiting) continue;
                        if (!ctx.Locator.TryGetGuestPosition(info.Id, out var wp)) continue;
                        var world = UIContext.ToVector3(wp);
                        if (!ctx.WorldToCanvas(world + Vector3.up * HeadHeight, out var pos)) continue;
                        var c = Get(info);
                        Fill(c, info, riding: false);
                        // one row per floor, in line order: each chip as close to its guest as the previous one allows
                        float left = Mathf.Max(pos.x - c.W * 0.5f, minLeft);
                        minLeft = left + c.W + WaitGap;
                        // inside this floor's headroom: just above the head, top under the ceiling (the floor above
                        // keeps its guests visible); on a tiny view it may dip to the hairline, never onto the faces
                        float y = pos.y + 2f;
                        if (ctx.WorldToCanvas(world + Vector3.up * floorH, out var ceiling)) y = Mathf.Min(y, ceiling.y - WaitH);
                        if (ctx.WorldToCanvas(world + Vector3.up * HeadFloor, out var low)) y = Mathf.Max(y, low.y);
                        c.Rect.anchoredPosition = new Vector2(left + c.W * 0.5f, y);
                    }
                }

                var riders = ctx.Elevator.Riders;
                if (riders.Count > 0 && RiderGridAnchor(ctx, layer.rect.height, riders.Count, out var anchor, out _))
                {
                    for (int i = 0; i < riders.Count; i++)
                    {
                        if (!ctx.Guests.TryGet(riders[i], out var info)) continue;
                        int col = i % RiderColumns, row = i / RiderColumns;
                        var c = Get(info);
                        Fill(c, info, riding: true);
                        c.Rect.anchoredPosition = new Vector2(anchor.x - 6f - ChipW * 0.5f - col * (ChipW + Spacing), anchor.y + row * (RiderChipH + Spacing));
                    }
                }
            }

            remove.Clear();
            foreach (var kv in chips) if (kv.Value.Seen != frame) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                var c = chips[id];
                chips.Remove(id);
                c.Rect.gameObject.SetActive(false);
                pool.Push(c);
            }
        }

        /// <summary>Canvas rect (bottom-left origin) around the riders' chips (the tutorial points at it). False when none shows.</summary>
        public bool RiderChipsRect(out Rect rect)
        {
            rect = default;
            bool any = false;
            var riders = ctx.Elevator != null ? ctx.Elevator.Riders : null;
            if (riders == null) return false;
            for (int i = 0; i < riders.Count; i++)
            {
                if (!chips.TryGetValue(riders[i].Value, out var c) || !c.Rect.gameObject.activeSelf || c.Seen != frame) continue;
                var p = c.Rect.anchoredPosition;
                var r = new Rect(p.x - c.W * 0.5f, p.y, c.W, c.Rect.sizeDelta.y);
                rect = any ? Rect.MinMaxRect(Mathf.Min(rect.xMin, r.xMin), Mathf.Min(rect.yMin, r.yMin), Mathf.Max(rect.xMax, r.xMax), Mathf.Max(rect.yMax, r.yMax)) : r;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Visible chips' odometers (test hook): "↕5" plain, "↕7:Lucky" / "↕13:Unlucky" when coloured, "-" for a chip
        /// without one (waiting guests). Keyed by guest id.
        /// </summary>
        public string Odometer(GuestId guest)
        {
            if (!chips.TryGetValue(guest.Value, out var c) || !c.Rect.gameObject.activeSelf) return null;
            if (!c.Odo.gameObject.activeSelf) return "-";
            return c.OdoText.text + (c.OdoVerdict == LuckVerdict.Neutral ? "" : ":" + c.OdoVerdict);
        }

        /// <summary>Odometer plate colour of a visible rider chip (test hook).</summary>
        public Color OdometerColor(GuestId guest) => chips.TryGetValue(guest.Value, out var c) ? c.OdoFill.color : Color.clear;

        /// <summary>Canvas rect of a visible chip (test hook), or an empty rect.</summary>
        public Rect ChipRect(GuestId guest)
        {
            if (!chips.TryGetValue(guest.Value, out var c) || !c.Rect.gameObject.activeSelf) return default;
            var p = c.Rect.anchoredPosition;
            return new Rect(p.x - c.W * 0.5f, p.y, c.W, c.Rect.sizeDelta.y);
        }

        /// <summary>Widest odometer text vs its plate (test hook): null when every visible one fits, else the offender.</summary>
        public string OdometerOverflow()
        {
            foreach (var c in chips.Values)
            {
                if (!c.Rect.gameObject.activeSelf || !c.Odo.gameObject.activeSelf) continue;
                float w = c.OdoText.GetPreferredValues(c.OdoText.text, float.PositiveInfinity, float.PositiveInfinity).x;
                if (w > c.Odo.rect.width + 0.5f) return "'" + c.OdoText.text + "' " + w.ToString("0") + "px in a " + c.Odo.rect.width.ToString("0") + "px plate";
            }
            return null;
        }

        /// <summary>First visible chip crossing the canvas x-range, or null (test hook).</summary>
        public string FirstOverlapX(float minX, float maxX)
        {
            foreach (var c in chips.Values)
            {
                if (!c.Rect.gameObject.activeSelf) continue;
                float x = c.Rect.anchoredPosition.x;
                if (x + c.W * 0.5f > minX + 0.5f && x - c.W * 0.5f < maxX - 0.5f)
                    return "chip x " + (x - c.W * 0.5f).ToString("0") + ".." + (x + c.W * 0.5f).ToString("0") + " vs " + minX.ToString("0") + ".." + maxX.ToString("0");
            }
            return null;
        }

        /// <summary>Visible chips: how many show an icon / a name / only the text fallback (test hook).</summary>
        public void BadgeStats(out int count, out int icons, out int names, out int textOnly)
        {
            count = icons = names = textOnly = 0;
            foreach (var c in VisibleChips())
            {
                count++;
                if (c.Badge.ShowsIcon) icons++;
                if (c.Badge.ShowsName) names++;
                if (!c.Badge.ShowsIcon && c.Badge.ShowsName) textOnly++;
            }
        }

        IEnumerable<Chip> VisibleChips()
        {
            foreach (var c in chips.Values) if (c.Rect.gameObject.activeSelf) yield return c;
        }

        public void Clear()
        {
            foreach (var c in chips.Values) { c.Rect.gameObject.SetActive(false); pool.Push(c); }
            chips.Clear();
        }
    }
}
