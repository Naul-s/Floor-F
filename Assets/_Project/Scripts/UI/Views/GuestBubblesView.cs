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
    /// Waiting guests: one chip above each head, packed left-to-right in line order so neighbours never overlap.
    /// Riders: grouped in a 4-wide grid hugging the left side of the car (they stand too close to label one by one).
    /// Position: IActorLocator -> Camera.main.WorldToViewportPoint -> PresentationContract.ViewportToScreen.
    /// </summary>
    sealed class GuestBubblesView
    {
        public const float ChipW = 80f, ChipH = 62f, BadgeH = 26f, BadgeSize = 24f;
        const float HeadHeight = 1.45f, Spacing = 3f, ColumnMargin = 4f;
        public const int RiderColumns = 3;

        sealed class Chip
        {
            public GuestId Id;
            public RectTransform Rect;
            public Image Edge;
            public TextMeshProUGUI Numbers;
            public DestinationBadge Badge;
            public RectTransform Patience;
            public Image PatienceImage;
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
            c.Numbers = UIBuild.Text("Numbers", c.Rect, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(c.Numbers.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(ChipW - 4f, 26f));
            c.Numbers.textWrappingMode = TextWrappingModes.NoWrap;
            c.Badge = new DestinationBadge(c.Rect, BadgeSize, DestinationBadge.NameMode.FullOrNone);
            UIBuild.Place(c.Badge.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(ChipW - 6f, BadgeH));
            var track = UIBuild.Box("PatienceTrack", c.Rect, new Color(0f, 0f, 0f, 0.9f));
            UIBuild.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(ChipW - 6f, 4f));
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
            c.Numbers.text = UIFormat.Numbers(info.Luck, info.HideNumbers);
            UIBuild.Fit(c.Numbers, 24f);
            c.ShownDestination = new DestinationId("\u0000");
            chips.Add(info.Id.Value, c);
            return c;
        }

        void Fill(Chip c, in GuestInfo info)
        {
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
            c.Seen = frame;
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
                    float minX = haveColumn ? colMax + ChipW * 0.5f : float.MinValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (!ctx.Guests.TryGet(waiting[i], out var info) || info.State != GuestState.Waiting) continue;
                        if (!ctx.Locator.TryGetGuestPosition(info.Id, out var wp)) continue;
                        if (!ctx.WorldToCanvas(UIContext.ToVector3(wp) + Vector3.up * HeadHeight, out var pos)) continue;
                        pos.x = Mathf.Max(pos.x, minX);
                        minX = pos.x + ChipW + Spacing;
                        var c = Get(info);
                        Fill(c, info);
                        c.Rect.anchoredPosition = pos;
                    }
                }

                var riders = ctx.Elevator.Riders;
                if (riders.Count > 0)
                {
                    float halfCar = ctx.CarWidth * 0.5f + 0.1f;
                    var car = UIContext.ToVector3(ctx.Locator.CarPosition);
                    if (ctx.WorldToCanvas(car + new Vector3(-halfCar, 0.1f, 0f), out var anchor))
                    {
                        if (haveColumn) anchor.x = Mathf.Min(anchor.x, colMin);
                        for (int i = 0; i < riders.Count; i++)
                        {
                            if (!ctx.Guests.TryGet(riders[i], out var info)) continue;
                            int col = i % RiderColumns, row = i / RiderColumns;
                            var c = Get(info);
                            Fill(c, info);
                            c.Rect.anchoredPosition = new Vector2(anchor.x - 6f - ChipW * 0.5f - col * (ChipW + Spacing), anchor.y + row * (ChipH + Spacing));
                        }
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
                var r = new Rect(p.x - ChipW * 0.5f, p.y, ChipW, ChipH);
                rect = any ? Rect.MinMaxRect(Mathf.Min(rect.xMin, r.xMin), Mathf.Min(rect.yMin, r.yMin), Mathf.Max(rect.xMax, r.xMax), Mathf.Max(rect.yMax, r.yMax)) : r;
                any = true;
            }
            return any;
        }

        /// <summary>First visible chip crossing the canvas x-range, or null (test hook).</summary>
        public string FirstOverlapX(float minX, float maxX)
        {
            foreach (var c in chips.Values)
            {
                if (!c.Rect.gameObject.activeSelf) continue;
                float x = c.Rect.anchoredPosition.x;
                if (x + ChipW * 0.5f > minX + 0.5f && x - ChipW * 0.5f < maxX - 0.5f)
                    return "chip x " + (x - ChipW * 0.5f).ToString("0") + ".." + (x + ChipW * 0.5f).ToString("0") + " vs " + minX.ToString("0") + ".." + maxX.ToString("0");
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
