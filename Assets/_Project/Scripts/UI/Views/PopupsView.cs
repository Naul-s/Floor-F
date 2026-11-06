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
        const float PopupLife = 1.8f, MergeWindow = 0.3f, SpeechMaxW = 240f, ShaftMargin = 6f, PopupGap = 4f;

        sealed class Popup
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Number, Verdict, Caption;
            public LuckSource Source;
            public int NumberValue;
            public LuckVerdict Kind;
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

        public void OnLuckJudged(LuckJudged e)
        {
            if (e.Verdict == LuckVerdict.Neutral) return;
            foreach (var p in popups)
            {
                if (p.Age <= MergeWindow && p.Source == e.Source && p.NumberValue == e.Number && p.Kind == e.Verdict)
                {
                    p.Count++;
                    p.Verdict.text = UIFormat.Verdict(e.Verdict, ctx.Korean) + "  x" + p.Count;
                    LastPopupText = p.Number.text + " " + p.Verdict.text + " " + p.Caption.text;
                    return;
                }
            }

            var pop = popupPool.Count > 0 ? popupPool.Pop() : MakePopup();
            pop.Rect.gameObject.SetActive(true);
            pop.Source = e.Source;
            pop.NumberValue = e.Number;
            pop.Kind = e.Verdict;
            pop.Count = 1;
            pop.Age = 0f;
            string label = ctx.Building != null && e.Floor.IsValidFor(ctx.Building.FloorCount) ? ctx.Building.GetLabel(e.Floor).Text : "";
            pop.Number.text = UIFormat.JudgedNumber(e.Number, label);
            pop.Verdict.text = UIFormat.Verdict(e.Verdict, ctx.Korean);
            pop.Caption.text = UIFormat.JudgedCaption(e.Source, e.Number, label, ctx.Korean);
            var color = e.Verdict == LuckVerdict.Lucky ? UIStyle.Lucky : UIStyle.Unlucky;
            pop.Number.color = pop.Verdict.color = color;

            // above the car, in the shaft column; lucky slightly left, unlucky slightly right, stacked upward
            var origin = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.5f);
            if (ctx.Locator != null && ctx.WorldToCanvas(UIContext.ToVector3(ctx.Locator.CarPosition) + Vector3.up * (floorHeight * 1.05f), out var pos)) origin = pos;
            origin.x += e.Verdict == LuckVerdict.Lucky ? -40f : 40f;
            origin.y += popups.Count * 30f;
            origin.y = Mathf.Min(origin.y, layer.rect.height - 120f);
            pop.Origin = origin;
            popups.Add(pop);
            pop.Rect.SetAsLastSibling();
            LastPopupText = pop.Number.text + " " + pop.Verdict.text + " " + pop.Caption.text;
            Animate(pop);
            LayoutSpeech();
        }

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
            var bg = UIBuild.Box("Back", p.Rect, new Color(0f, 0f, 0f, 0.72f));
            UIBuild.Stretch(bg.rectTransform);
            p.Number = UIBuild.Text("Number", p.Rect, "", 48f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Number.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(170f, 60f));
            p.Number.textWrappingMode = TextWrappingModes.NoWrap;
            p.Verdict = UIBuild.Text("Verdict", p.Rect, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Verdict.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(170f, 30f));
            p.Verdict.textWrappingMode = TextWrappingModes.NoWrap;
            p.Caption = UIBuild.Text("Caption", p.Rect, "", 12f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(170f, 18f));
            p.Caption.textWrappingMode = TextWrappingModes.NoWrap;
            return p;
        }

        void Animate(Popup p)
        {
            float t = p.Age;
            float punch = t < 0.12f ? Mathf.Lerp(0.4f, 1.25f, t / 0.12f) : t < 0.24f ? Mathf.Lerp(1.25f, 1f, (t - 0.12f) / 0.12f) : 1f;
            p.Rect.localScale = new Vector3(punch, punch, 1f);
            p.Rect.anchoredPosition = p.Origin + new Vector2(0f, Mathf.Min(t, 1f) * 40f);
            p.Group.alpha = t > PopupLife - 0.5f ? Mathf.Clamp01((PopupLife - t) / 0.5f) : 1f;
            if (p.Kind == LuckVerdict.Unlucky && t < 0.4f) p.Rect.anchoredPosition += new Vector2(Mathf.Sin(t * 90f) * 4f, 0f);
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
            if (riding && ctx.Locator != null)
            {
                float halfCar = ctx.CarWidth * 0.5f + 0.1f;
                if (!ctx.WorldToCanvas(UIContext.ToVector3(ctx.Locator.CarPosition) + new Vector3(-halfCar, 0.1f, 0f), out pos))
                    pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
                pos.x -= 6f + size.x * 0.5f;
                pos.y += 2f * (GuestBubblesView.ChipH + 3f) + 4f;
                s.Side = -1f;
            }
            else
            {
                if (float.IsNaN(s.LastWorld.x) || !ctx.WorldToCanvas(s.LastWorld + Vector3.up * (floorHeight * 0.98f), out pos))
                    pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
                float shaftX = ctx.Layout != null ? ctx.Layout.ShaftX : 0f;
                s.Side = float.IsNaN(s.LastWorld.x) || s.LastWorld.x >= shaftX ? 1f : -1f; // waiting line is right of the shaft
            }
            s.Rect.anchoredPosition = pos;
        }

        /// <summary>Base positions -> out of the shaft column -> unstack upward -> out of luck popups -> on screen.</summary>
        void LayoutSpeech()
        {
            if (speeches.Count == 0) return;
            foreach (var s in speeches) PlaceSpeech(s);

            bool haveColumn = ctx.ShaftColumn(ShaftMargin, out float colMin, out float colMax);
            if (haveColumn) foreach (var s in speeches) PushOutOfColumn(s, colMin, colMax);

            Unstack();

            foreach (var s in speeches)
            {
                for (int guard = 0; guard < 4; guard++)
                {
                    bool moved = false;
                    foreach (var p in popups)
                    {
                        if (p.Group.alpha <= 0.01f) continue;
                        var pr = PopupBox(p);
                        var sr = SpeechBox(s);
                        if (!sr.Overlaps(pr)) continue;
                        var pos = s.Rect.anchoredPosition;
                        pos.x = s.Side < 0f ? pr.xMin - PopupGap - sr.width * 0.5f : pr.xMax + PopupGap + sr.width * 0.5f;
                        s.Rect.anchoredPosition = pos;
                        moved = true;
                    }
                    if (!moved) break;
                }
            }

            foreach (var s in speeches)
            {
                var size = s.Rect.sizeDelta;
                var pos = s.Rect.anchoredPosition;
                pos.x = Mathf.Clamp(pos.x, size.x * 0.5f + 8f, layer.rect.width - size.x * 0.5f - 8f);
                pos.y = Mathf.Clamp(pos.y, 8f, layer.rect.height - size.y - 8f);
                s.Rect.anchoredPosition = pos;
            }
        }

        static void PushOutOfColumn(Speech s, float colMin, float colMax)
        {
            var r = SpeechBox(s);
            if (r.xMax <= colMin || r.xMin >= colMax) return;
            var pos = s.Rect.anchoredPosition;
            pos.x = s.Side < 0f ? colMin - r.width * 0.5f : colMax + r.width * 0.5f;
            s.Rect.anchoredPosition = pos;
        }

        /// <summary>Older bubbles first; a bubble overlapping an earlier one moves above it.</summary>
        void Unstack()
        {
            for (int i = 0; i < speeches.Count; i++)
            {
                var a = speeches[i].Rect;
                for (int guard = 0; guard < 8; guard++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        var b = speeches[j].Rect;
                        var pa = a.anchoredPosition; var pb = b.anchoredPosition;
                        var sa = a.sizeDelta; var sb2 = b.sizeDelta;
                        bool overlapX = Mathf.Abs(pa.x - pb.x) < (sa.x + sb2.x) * 0.5f + 2f;
                        bool overlapY = pa.y < pb.y + sb2.y + 2f && pb.y < pa.y + sa.y + 2f;
                        if (overlapX && overlapY) { a.anchoredPosition = new Vector2(pa.x, pb.y + sb2.y + 4f); moved = true; }
                    }
                    if (!moved) break;
                }
            }
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
