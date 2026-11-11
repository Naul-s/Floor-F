using System.Collections.Generic;
using System.Text;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// LuckJudged -> big number + LUCKY!/UNLUCKY! popup over the car shaft (riders judged together are merged: "x2"),
    /// caption "floor F" (FloorLabel) or "3 people" (Occupancy). GuestSpoke -> speech bubble over that guest.
    /// Speech rules (anti-clutter): at most <see cref="MaxSpeech"/> at once (oldest dropped), identical text merged into
    /// one bubble, <see cref="SpeechLife"/> s, never inside the shaft column (IWorldLayout.ShaftX ± carWidth/2 on screen -
    /// pushed to the guest's side), and luck popups win: a bubble that overlaps one is pushed sideways out of it.
    /// </summary>
    sealed class PopupsView
    {
        public const int MaxSpeech = 3;
        public const float SpeechLife = 2.5f;
        const float PopupLife = 1.8f, MergeWindow = 0.3f, SpeechMaxW = 240f, ShaftMargin = 6f, LeaderMin = 60f;

        sealed class Popup
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Number, Verdict, Caption, Charm;
            public Image Back;
            /// <summary>Lottery night jackpot (LotteryWon): gold plate, "JACKPOT x2".</summary>
            public bool Jackpot;
            public LuckSource Source;
            public int NumberValue;
            public LuckVerdict Kind;
            /// <summary>An Unlucky verdict a shield charm ate (CharmTriggered right before it): "BLOCKED!" in shield cyan.</summary>
            public bool Shielded;
            public int Count;
            public float Age;
            public Vector2 Origin;
        }

        sealed class Speech
        {
            public GuestId Guest;
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public float Age;
            public Vector3 LastWorld;
            /// <summary>-1 = left of the shaft (riders), +1 = right (waiting line side).</summary>
            public float Side;
            /// <summary>Canvas point the bubble belongs to (head / riders' chips); the leader line ends here.</summary>
            public Vector2 Anchor;
            public RectTransform Leader;
        }

        readonly UIContext ctx;
        readonly RectTransform layer, speechLayer;
        readonly List<Popup> popups = new List<Popup>();
        readonly Stack<Popup> popupPool = new Stack<Popup>();
        /// <summary>Oldest first (a refreshed bubble moves to the end).</summary>
        readonly List<Speech> speeches = new List<Speech>();
        readonly Stack<Speech> speechPool = new Stack<Speech>();
        readonly StringBuilder sb = new StringBuilder();
        float floorHeight = 3f;

        public PopupsView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            speechLayer = UIBuild.Stretch(UIBuild.Rect("Speech", canvas));
            layer = UIBuild.Stretch(UIBuild.Rect("Popups", canvas)); // luck popups always above speech
            if (ctx.Data != null) floorHeight = ctx.Data.Layout.floorHeight;
        }

        public RectTransform SpeechLayer => speechLayer;
        public int ActivePopupCount => popups.Count;
        public int ActiveSpeechCount => speeches.Count;
        public string LastPopupText { get; private set; } = "";
        public Color LastPopupColor { get; private set; }
        /// <summary>Canvas rect of the newest visible luck popup (largest box), or default.</summary>
        public Rect LastPopupRect => popups.Count > 0 ? PopupBox(popups[popups.Count - 1]) : default;

        public string SpeechTexts
        {
            get
            {
                sb.Clear();
                foreach (var s in speeches) { if (sb.Length > 0) sb.Append(" | "); sb.Append(s.Text.text); }
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ luck popups

        public static readonly Color Shield = UIStyle.Hex(UIFormat.ShieldHex);

        string VerdictWord(LuckVerdict v, bool shielded) => shielded ? ctx.S(UIStrings.BlockedWord) : UIFormat.Verdict(v, ctx.Korean);

        const float PopupW = 170f, PopupH = 104f, CharmLineH = 30f;

        static string PopupText(Popup p) =>
            p.Number.text + " " + p.Verdict.text + " " + p.Caption.text + (p.Charm.gameObject.activeSelf ? " " + p.Charm.text : "");

        /// <param name="shieldLine">
        /// Non-null: a shield charm ate this Unlucky verdict (CharmTriggered right before it) - no red alarm, a cyan
        /// "BLOCKED!" popup with this line under it ("◈ Talisman blocked it!").
        /// </param>
        public void OnLuckJudged(LuckJudged e, string shieldLine = null)
        {
            if (e.Verdict == LuckVerdict.Neutral) return;
            bool shielded = shieldLine != null && e.Verdict == LuckVerdict.Unlucky;
            foreach (var p in popups)
            {
                if (!p.Jackpot && p.Age <= MergeWindow && p.Source == e.Source && p.NumberValue == e.Number && p.Kind == e.Verdict && p.Shielded == shielded)
                {
                    p.Count++;
                    p.Verdict.text = VerdictWord(e.Verdict, shielded) + "  x" + p.Count;
                    if (shielded)
                    {
                        // a second shield in the same moment: name the latest charm, current language
                        p.Caption.text = ctx.S(UIStrings.NoLuckLost);
                        SetCharmLine(p, shieldLine);                    }
                    LastPopupText = PopupText(p);
                    LastPopupColor = p.Verdict.color;
                    return;
                }
            }

            var pop = popupPool.Count > 0 ? popupPool.Pop() : MakePopup();
            pop.Rect.gameObject.SetActive(true);
            pop.Source = e.Source;
            pop.NumberValue = e.Number;
            pop.Kind = e.Verdict;
            pop.Shielded = shielded;
            pop.Jackpot = false;
            pop.Back.color = PlainBack;
            pop.Count = 1;
            pop.Age = 0f;
            string label = ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) ? ctx.Building.GetLabel(e.Floor).Text : "";
            pop.Number.text = UIFormat.JudgedNumber(e.Source, e.Number, label);
            pop.Verdict.text = VerdictWord(e.Verdict, shielded);
            pop.Caption.text = UIFormat.JudgedCaption(e.Source, e.Number, label, ctx.Korean);
            if (shielded) pop.Caption.text = ctx.S(UIStrings.NoLuckLost); // the number says what was judged
            var color = shielded ? Shield : e.Verdict == LuckVerdict.Lucky ? UIStyle.Lucky : UIStyle.Unlucky;
            pop.Number.color = pop.Verdict.color = color;
            SetCharmLine(pop, shielded ? shieldLine : null);

            // above the car, in the shaft column; lucky slightly left, unlucky slightly right, stacked upward
            var origin = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.5f);
            if (ctx.Locator != null && ctx.WorldToCanvas(UIContext.ToVector3(ctx.Locator.CarPosition) + Vector3.up * (floorHeight * 1.05f), out var pos)) origin = pos;
            origin.x += e.Verdict == LuckVerdict.Lucky ? -40f : 40f;
            origin.y += popups.Count * 30f;
            origin.y = Mathf.Min(origin.y, layer.rect.height - 120f);
            // never over the floor-sign plates (left of the shaft): the player reads the next floor's number there
            float half = pop.Rect.sizeDelta.x * 1.25f * 0.5f; // punch scale included
            if (ctx.SignColumn(4f, out _, out float signMax)) origin.x = Mathf.Max(origin.x, signMax + half);
            origin.x = Mathf.Clamp(origin.x, half + 8f, layer.rect.width - half - 8f);
            pop.Origin = origin;
            popups.Add(pop);
            pop.Rect.SetAsLastSibling();
            LastPopupText = PopupText(pop);
            LastPopupColor = pop.Verdict.color;
            Animate(pop);
            LayoutSpeech();
        }

        static readonly Color PlainBack = new Color(0f, 0f, 0f, 0.72f), JackpotBack = new Color(0.22f, 0.15f, 0.01f, 0.9f);

        /// <summary>
        /// LotteryWon: a gold "JACKPOT x2" popup over the car (the guest steps out right there) with the winning number
        /// big and "tips x2 · Luck +3" under it; a party hitting it together merges into one popup (Luck adds up).
        /// </summary>
        public void OnLotteryWon(LotteryWon e, float tipMultiplier, int luckBonus)
        {
            string mult = UIFormat.Factor(tipMultiplier);
            foreach (var p in popups)
            {
                if (!p.Jackpot || p.Age > MergeWindow || p.NumberValue != e.Number) continue;
                p.Count++;
                p.Caption.text = ctx.F(UIStrings.JackpotCaption, mult, luckBonus * p.Count);
                LastPopupText = PopupText(p);
                return;
            }
            var pop = popupPool.Count > 0 ? popupPool.Pop() : MakePopup();
            pop.Rect.gameObject.SetActive(true);
            pop.Source = LuckSource.Lottery;
            pop.NumberValue = e.Number;
            pop.Kind = LuckVerdict.Lucky;
            pop.Shielded = false;
            pop.Jackpot = true;
            pop.Count = 1;
            pop.Age = 0f;
            pop.Back.color = JackpotBack;
            pop.Number.text = UIFonts.Star + e.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + UIFonts.Star;
            pop.Verdict.text = ctx.F(UIStrings.Jackpot, mult);
            pop.Caption.text = ctx.F(UIStrings.JackpotCaption, mult, luckBonus);
            pop.Number.color = pop.Verdict.color = UIStyle.Lucky;
            SetCharmLine(pop, null);
            var origin = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.5f);
            if (ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) && ctx.Locator != null)
            {
                var car = UIContext.ToVector3(ctx.Locator.CarPosition);
                if (ctx.WorldToCanvas(car + Vector3.up * (floorHeight * 1.05f), out var pos)) origin = pos;
            }
            origin.y += 40f + popups.Count * 30f; // above any luck popup of the same moment
            origin.y = Mathf.Min(origin.y, layer.rect.height - 120f);
            float half = pop.Rect.sizeDelta.x * 1.25f * 0.5f;
            if (ctx.SignColumn(4f, out _, out float signMax)) origin.x = Mathf.Max(origin.x, signMax + half);
            origin.x = Mathf.Clamp(origin.x, half + 8f, layer.rect.width - half - 8f);
            pop.Origin = origin;
            popups.Add(pop);
            pop.Rect.SetAsLastSibling();
            LastPopupText = PopupText(pop);
            LastPopupColor = UIStyle.Lucky;
            Animate(pop);
            LayoutSpeech();
        }

        public bool LastPopupIsJackpot => popups.Count > 0 && popups[popups.Count - 1].Jackpot;

        Popup MakePopup()
        {
            var p = new Popup();
            p.Rect = UIBuild.Rect("LuckPopup", layer);
            p.Rect.anchorMin = p.Rect.anchorMax = Vector2.zero;
            p.Rect.pivot = new Vector2(0.5f, 0f);
            p.Rect.sizeDelta = new Vector2(170f, 104f);
            p.Group = p.Rect.gameObject.AddComponent<CanvasGroup>();
            p.Group.blocksRaycasts = false;
            p.Group.interactable = false;
            var bg = p.Back = UIBuild.Box("Back", p.Rect, PlainBack);
            UIBuild.Stretch(bg.rectTransform);
            p.Number = UIBuild.Heading(UIBuild.Text("Number", p.Rect, "", 48f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true));
            UIBuild.Place(p.Number.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(170f, 60f));
            p.Number.textWrappingMode = TextWrappingModes.NoWrap;
            p.Verdict = UIBuild.Text("Verdict", p.Rect, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Verdict.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(170f, 30f));
            p.Verdict.textWrappingMode = TextWrappingModes.NoWrap;
            p.Caption = UIBuild.Text("Caption", p.Rect, "", 12f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(170f, 18f));
            p.Caption.textWrappingMode = TextWrappingModes.NoWrap;
            p.Charm = UIBuild.Text("Charm", p.Rect, "", 24f, Shield, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Charm.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(PopupW, CharmLineH));
            p.Charm.textWrappingMode = TextWrappingModes.NoWrap;
            p.Charm.gameObject.SetActive(false);
            return p;
        }

        /// <summary>Shielded popups grow a line under the caption naming the charm; the box widens to fit it.</summary>
        void SetCharmLine(Popup p, string line)
        {
            bool on = !string.IsNullOrEmpty(line);
            p.Charm.gameObject.SetActive(on);
            float w = PopupW, lift = 0f;
            if (on)
            {
                p.Charm.text = line;
                if (p.Charm.font != null) p.Charm.font.TryAddCharacters(line);
                w = Mathf.Max(PopupW, Mathf.Ceil(p.Charm.GetPreferredValues(line, float.PositiveInfinity, float.PositiveInfinity).x) + 16f);
                p.Charm.rectTransform.sizeDelta = new Vector2(w, CharmLineH);
                lift = CharmLineH;
            }
            p.Rect.sizeDelta = new Vector2(w, PopupH + lift);
            p.Verdict.rectTransform.anchoredPosition = new Vector2(0f, 22f + lift);
            p.Caption.rectTransform.anchoredPosition = new Vector2(0f, 4f + lift);
        }

        void Animate(Popup p)
        {
            float t = p.Age;
            float punch = t < 0.12f ? Mathf.Lerp(0.4f, 1.25f, t / 0.12f) : t < 0.24f ? Mathf.Lerp(1.25f, 1f, (t - 0.12f) / 0.12f) : 1f;
            p.Rect.localScale = new Vector3(punch, punch, 1f);
            p.Rect.anchoredPosition = p.Origin + new Vector2(0f, Mathf.Min(t, 1f) * 40f);
            p.Group.alpha = t > PopupLife - 0.5f ? Mathf.Clamp01((PopupLife - t) / 0.5f) : 1f;
            if (p.Kind == LuckVerdict.Unlucky && !p.Shielded && t < 0.4f) p.Rect.anchoredPosition += new Vector2(Mathf.Sin(t * 90f) * 4f, 0f);
        }

        /// <summary>Popup's on-canvas box at its largest (punch scale included).</summary>
        static Rect PopupBox(Popup p)
        {
            float k = Mathf.Max(1f, p.Rect.localScale.x);
            var size = p.Rect.sizeDelta * k;
            var pos = p.Rect.anchoredPosition;
            return new Rect(pos.x - size.x * 0.5f, pos.y, size.x, size.y);
        }

        static Rect SpeechBox(Speech s)
        {
            var size = s.Rect.sizeDelta;
            var pos = s.Rect.anchoredPosition;
            return new Rect(pos.x - size.x * 0.5f, pos.y, size.x, size.y);
        }

        // ------------------------------------------------------------------ speech

        public void OnGuestSpoke(GuestSpoke e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            Speech s = null;
            // identical line already on screen (a party saying the same thing) -> one bubble, refreshed
            foreach (var x in speeches) if (string.Equals(x.Text.text, e.Text, System.StringComparison.Ordinal)) { s = x; break; }
            if (s == null) foreach (var x in speeches) if (x.Guest == e.Guest) { s = x; break; }
            if (s != null) speeches.Remove(s);
            else
            {
                while (speeches.Count >= MaxSpeech) Recycle(0);
                s = speechPool.Count > 0 ? speechPool.Pop() : MakeSpeech();
                s.Rect.gameObject.SetActive(true);
                s.Guest = e.Guest;
                s.LastWorld = new Vector3(float.NaN, 0f, 0f);
            }
            speeches.Add(s);
            s.Age = 0f;
            s.Group.alpha = 1f;
            if (!string.Equals(s.Text.text, e.Text, System.StringComparison.Ordinal))
            {
                s.Text.text = e.Text;
                s.Text.ForceMeshUpdate(); // dynamic atlas: make sure every glyph exists before measuring
                var size = s.Text.GetPreferredValues(e.Text, float.PositiveInfinity, float.PositiveInfinity); // one-line width
                bool oneLine = size.x + 8f <= SpeechMaxW;
                s.Text.textWrappingMode = oneLine ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
                float w = Mathf.Min(SpeechMaxW, Mathf.Ceil(size.x) + 8f) + 16f;
                var pref = oneLine ? size : s.Text.GetPreferredValues(e.Text, w - 16f, float.PositiveInfinity);
                s.Rect.sizeDelta = new Vector2(w, pref.y + 10f);
                s.Text.ForceMeshUpdate();
            }
            s.Rect.SetAsLastSibling();
            LayoutSpeech();
        }

        void Recycle(int index)
        {
            var s = speeches[index];
            s.Rect.gameObject.SetActive(false);
            s.Text.text = "";
            speeches.RemoveAt(index);
            speechPool.Push(s);
        }

        Speech MakeSpeech()
        {
            var s = new Speech();
            var edge = UIBuild.Framed("Speech", speechLayer, new Color(0.98f, 0.97f, 0.92f, 0.97f), UIStyle.Dark, 2f);
            s.Rect = edge.rectTransform;
            s.Rect.anchorMin = s.Rect.anchorMax = Vector2.zero;
            s.Rect.pivot = new Vector2(0.5f, 0f);
            s.Group = s.Rect.gameObject.AddComponent<CanvasGroup>();
            s.Group.blocksRaycasts = false;
            s.Leader = UIBuild.Box("Leader", s.Rect, UIStyle.Dark).rectTransform;
            s.Leader.SetAsFirstSibling();
            s.Leader.gameObject.SetActive(false);
            s.Text = UIBuild.Text("Text", s.Rect, "", 12f, UIStyle.Dark, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(s.Text.rectTransform, 6f);
            return s;
        }

        /// <summary>Base position: above the guest (waiting) or above the riders' chip grid left of the car.</summary>
        void PlaceSpeech(Speech s)
        {
            if (ctx.Locator != null && ctx.Locator.TryGetGuestPosition(s.Guest, out var wp)) s.LastWorld = UIContext.ToVector3(wp);
            var size = s.Rect.sizeDelta;
            bool riding = ctx.Guests != null && ctx.Guests.TryGet(s.Guest, out var info) && info.State == GuestState.Riding;
            Vector2 pos;
            var anchor = new Vector2(float.NaN, float.NaN); // NaN = nothing to point at (unknown guest)
            if (riding && ctx.Locator != null)
            {
                // same corner as the riders' chip grid (GuestBubblesView), which stays left of the signs
                if (GuestBubblesView.RiderGridAnchor(ctx, layer.rect.height, ctx.Elevator.Occupancy, out pos, out int rows))
                    anchor = new Vector2(pos.x - 6f - GuestBubblesView.ChipW * 0.5f, pos.y + GuestBubblesView.RiderChipH);
                else pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
                pos.x -= 6f + size.x * 0.5f;
                pos.y += rows * (GuestBubblesView.RiderChipH + GuestBubblesView.Spacing) + 4f;
                s.Side = -1f;
            }
            else
            {
                if (!float.IsNaN(s.LastWorld.x) && ctx.WorldToCanvas(s.LastWorld + Vector3.up * (floorHeight * 0.98f), out pos)) anchor = pos;
                else pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
                float shaftX = ctx.Layout != null ? ctx.Layout.ShaftX : 0f;
                s.Side = float.IsNaN(s.LastWorld.x) || s.LastWorld.x >= shaftX ? 1f : -1f; // waiting line is right of the shaft
            }
            s.Rect.anchoredPosition = pos;
            s.Anchor = anchor;
        }

        /// <summary>
        /// Base positions -> out of the centre column (floor signs + shaft) to the guest's side -> on screen (a bubble
        /// with no room on its side goes to the other side, else to the screen edge) -> then each bubble, oldest first,
        /// settles at the nearest on-screen spot clear of the column, every luck popup (its whole rise) and every bubble
        /// already settled. De-overlap runs LAST so no later pass can stack bubbles again. Any bubble far from its guest
        /// gets a leader line.
        /// </summary>
        void LayoutSpeech()
        {
            if (speeches.Count == 0) return;
            foreach (var s in speeches) PlaceSpeech(s);

            haveColumn = ctx.CenterColumn(ShaftMargin, out colMin, out colMax);
            foreach (var s in speeches)
            {
                if (haveColumn) PushOutOfColumn(s, colMin, colMax);
                Clamp(s);
                if (haveColumn && InColumn(s, colMin, colMax))
                {
                    // no room on its side: try the other side, then the far screen edge of the roomier side
                    s.Side = -s.Side;
                    PushOutOfColumn(s, colMin, colMax);
                    Clamp(s);
                    if (InColumn(s, colMin, colMax))
                    {
                        var pos = s.Rect.anchoredPosition;
                        float half = s.Rect.sizeDelta.x * 0.5f;
                        pos.x = colMin > layer.rect.width - colMax ? half + 8f : layer.rect.width - half - 8f;
                        s.Rect.anchoredPosition = pos;
                    }
                }
            }

            blockers.Clear();
            if (haveColumn) blockers.Add(Rect.MinMaxRect(colMin, -ColumnReach, colMax, ColumnReach));
            foreach (var p in popups) if (p.Group.alpha > 0.01f) blockers.Add(PopupPath(p));
            foreach (var s in speeches)
            {
                Settle(s);
                blockers.Add(SpeechBox(s));
                UpdateLeader(s);
            }
        }

        const float ColumnReach = 100000f, SettleGap = 4f, WrongSidePenalty = 400f, DownPenalty = 4f;
        const int SettleDepth = 3, MaxCandidates = 160;
        readonly List<Rect> blockers = new List<Rect>();
        readonly List<Vector3> candidates = new List<Vector3>(); // x, y, depth
        bool haveColumn;
        float colMin, colMax;

        /// <summary>A luck popup's box over its whole life (it rises 40 px and punches to 1.25x) - bubbles never chase it.</summary>
        static Rect PopupPath(Popup p)
        {
            float w = p.Rect.sizeDelta.x * 1.25f, h = p.Rect.sizeDelta.y * 1.25f;
            float y0 = Mathf.Min(p.Rect.anchoredPosition.y, p.Origin.y);
            float y1 = p.Origin.y + 40f + h;
            float x = p.Rect.anchoredPosition.x;
            return Rect.MinMaxRect(x - w * 0.5f - 4f, y0, x + w * 0.5f + 4f, y1);
        }

        int FirstBlocker(Vector2 pos, Vector2 size)
        {
            var r = new Rect(pos.x - size.x * 0.5f, pos.y, size.x, size.y);
            for (int i = 0; i < blockers.Count; i++)
            {
                var b = blockers[i];
                if (r.xMax > b.xMin + 0.5f && r.xMin < b.xMax - 0.5f && r.yMax > b.yMin + 0.5f && r.yMin < b.yMax - 0.5f) return i;
            }
            return -1;
        }

        Vector2 ClampPos(Vector2 pos, Vector2 size)
        {
            pos.x = Mathf.Clamp(pos.x, size.x * 0.5f + 8f, layer.rect.width - size.x * 0.5f - 8f);
            pos.y = Mathf.Clamp(pos.y, 8f, layer.rect.height - size.y - 8f);
            return pos;
        }

        void AddCandidate(float x, float y, Vector2 size, int depth)
        {
            var p = ClampPos(new Vector2(x, y), size);
            for (int i = 0; i < candidates.Count; i++)
                if (Mathf.Abs(candidates[i].x - p.x) < 1f && Mathf.Abs(candidates[i].y - p.y) < 1f) return;
            candidates.Add(new Vector3(p.x, p.y, depth));
        }

        /// <summary>
        /// Nearest spot (search tree: next to whatever blocks - above, below, left, right - up to 3 hops) that is on
        /// screen and clear of every blocker; the guest's side of the column is preferred. Nothing clear -> stays put.
        /// </summary>
        void Settle(Speech s)
        {
            var size = s.Rect.sizeDelta;
            var start = s.Rect.anchoredPosition;
            if (FirstBlocker(start, size) < 0) return;
            float mid = haveColumn ? (colMin + colMax) * 0.5f : 0f;
            bool startLeft = start.x < mid;
            candidates.Clear();
            candidates.Add(new Vector3(start.x, start.y, 0f));
            var best = start;
            float bestCost = float.MaxValue;
            for (int i = 0; i < candidates.Count && candidates.Count < MaxCandidates; i++)
            {
                var c = candidates[i];
                var p = new Vector2(c.x, c.y);
                int hit = FirstBlocker(p, size);
                if (hit < 0)
                {
                    // down is expensive: below its base a bubble covers its own guest / the riders' chips
                    float dy = p.y - start.y;
                    float cost = Mathf.Abs(p.x - start.x) + (dy >= 0f ? dy : -dy * DownPenalty);
                    if (haveColumn && (p.x < mid) != startLeft) cost += WrongSidePenalty;
                    if (cost < bestCost) { bestCost = cost; best = p; }
                    continue;
                }
                int depth = (int)c.z;
                if (depth >= SettleDepth) continue;
                var o = blockers[hit];
                if (o.height < ColumnReach)
                {
                    AddCandidate(p.x, o.yMax + SettleGap, size, depth + 1);
                    AddCandidate(p.x, o.yMin - SettleGap - size.y, size, depth + 1);
                }
                AddCandidate(o.xMin - SettleGap - size.x * 0.5f, p.y, size, depth + 1);
                AddCandidate(o.xMax + SettleGap + size.x * 0.5f, p.y, size, depth + 1);
            }
            s.Rect.anchoredPosition = best;
            if (haveColumn) s.Side = best.x < mid ? -1f : 1f;
        }

        void Clamp(Speech s)
        {
            var size = s.Rect.sizeDelta;
            var pos = s.Rect.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, size.x * 0.5f + 8f, layer.rect.width - size.x * 0.5f - 8f);
            pos.y = Mathf.Clamp(pos.y, 8f, layer.rect.height - size.y - 8f);
            s.Rect.anchoredPosition = pos;
        }

        static bool InColumn(Speech s, float colMin, float colMax)
        {
            var r = SpeechBox(s);
            return r.xMax > colMin + 0.5f && r.xMin < colMax - 0.5f;
        }

        /// <summary>Leader line from the bubble's nearest bottom corner to its guest when it was pushed far away.</summary>
        void UpdateLeader(Speech s)
        {
            var r = SpeechBox(s);
            var to = s.Anchor;
            bool show = !float.IsNaN(to.x);
            var from = show ? new Vector2(Mathf.Clamp(to.x, r.xMin + 6f, r.xMax - 6f), r.yMin) : Vector2.zero;
            show &= (to - from).magnitude > LeaderMin;
            if (s.Leader.gameObject.activeSelf != show) s.Leader.gameObject.SetActive(show);
            if (!show) return;
            UIBuild.SetLine(s.Leader, from - r.min, to - r.min, 2f);
        }

        static void PushOutOfColumn(Speech s, float colMin, float colMax)
        {
            var r = SpeechBox(s);
            if (r.xMax <= colMin || r.xMin >= colMax) return;
            var pos = s.Rect.anchoredPosition;
            pos.x = s.Side < 0f ? colMin - r.width * 0.5f : colMax + r.width * 0.5f;
            s.Rect.anchoredPosition = pos;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.Age += dt;
                if (p.Age >= PopupLife) { p.Rect.gameObject.SetActive(false); popups.RemoveAt(i); popupPool.Push(p); continue; }
                Animate(p);
            }
            for (int i = speeches.Count - 1; i >= 0; i--)
            {
                var s = speeches[i];
                s.Age += dt;
                if (s.Age >= SpeechLife) { Recycle(i); continue; }
                s.Group.alpha = s.Age > SpeechLife - 0.4f ? Mathf.Clamp01((SpeechLife - s.Age) / 0.4f) : 1f;
            }
            LayoutSpeech();
        }

        public void Clear()
        {
            foreach (var p in popups) { p.Rect.gameObject.SetActive(false); popupPool.Push(p); }
            popups.Clear();
            while (speeches.Count > 0) Recycle(speeches.Count - 1);
        }

        /// <summary>Luck popups only (the ending's last line may stay visible).</summary>
        public void ClearPopups()
        {
            foreach (var p in popups) { p.Rect.gameObject.SetActive(false); popupPool.Push(p); }
            popups.Clear();
        }

        // ------------------------------------------------------------------ test hooks

        public string FirstShaftOverlap()
        {
            if (!ctx.ShaftColumn(0f, out float colMin, out float colMax)) return "shaft column not projectable";
            foreach (var s in speeches)
            {
                var r = SpeechBox(s);
                if (r.xMax > colMin + 0.5f && r.xMin < colMax - 0.5f)
                    return "'" + s.Text.text + "' x " + r.xMin.ToString("0") + ".." + r.xMax.ToString("0") + " vs shaft " + colMin.ToString("0") + ".." + colMax.ToString("0");
            }
            return null;
        }

        /// <summary>First visible speech bubble whose box crosses the canvas x-range, or null.</summary>
        public string FirstOverlapX(float minX, float maxX)
        {
            foreach (var s in speeches)
            {
                var r = SpeechBox(s);
                if (r.xMax > minX + 0.5f && r.xMin < maxX - 0.5f)
                    return "'" + s.Text.text + "' x " + r.xMin.ToString("0") + ".." + r.xMax.ToString("0") + " vs " + minX.ToString("0") + ".." + maxX.ToString("0");
            }
            return null;
        }

        /// <summary>First visible luck popup (largest box) crossing the canvas x-range, or null.</summary>
        public string FirstPopupOverlapX(float minX, float maxX)
        {
            foreach (var p in popups)
            {
                var r = PopupBox(p);
                if (r.xMax > minX + 0.5f && r.xMin < maxX - 0.5f)
                    return "popup '" + p.Number.text + " " + p.Caption.text + "' x " + r.xMin.ToString("0") + ".." + r.xMax.ToString("0") + " vs " + minX.ToString("0") + ".." + maxX.ToString("0");
            }
            return null;
        }

        /// <summary>First pair of visible speech bubbles whose boxes overlap, or null.</summary>
        public string FirstSpeechOverlap()
        {
            for (int i = 0; i < speeches.Count; i++)
                for (int j = 0; j < i; j++)
                {
                    var a = SpeechBox(speeches[i]); var b = SpeechBox(speeches[j]);
                    if (a.xMax > b.xMin + 0.5f && a.xMin < b.xMax - 0.5f && a.yMax > b.yMin + 0.5f && a.yMin < b.yMax - 0.5f)
                        return "'" + speeches[i].Text.text + "' " + a + " vs '" + speeches[j].Text.text + "' " + b;
                }
            return null;
        }

        public int LeaderCount { get { int n = 0; foreach (var s in speeches) if (s.Leader.gameObject.activeSelf) n++; return n; } }

        public string FirstPopupOverlap()
        {
            foreach (var s in speeches)
                foreach (var p in popups)
                {
                    var sr = SpeechBox(s); var pr = PopupBox(p);
                    if (sr.Overlaps(pr)) return "'" + s.Text.text + "' " + sr + " vs popup '" + p.Number.text + "' " + pr;
                }
            return null;
        }
    }
}
