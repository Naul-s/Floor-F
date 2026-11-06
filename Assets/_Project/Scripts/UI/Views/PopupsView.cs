using System.Collections.Generic;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// LuckJudged -> big number + LUCKY!/UNLUCKY! popup over the car shaft (riders judged together are merged: "x2"),
    /// caption "floor F" (FloorLabel) or "3 people" (Occupancy). GuestSpoke -> speech bubble over that guest.
    /// </summary>
    sealed class PopupsView
    {
        const float PopupLife = 1.8f, MergeWindow = 0.3f, SpeechLife = 2.8f, SpeechMaxW = 230f;

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
        }

        readonly UIContext ctx;
        readonly RectTransform layer, speechLayer;
        readonly List<Popup> popups = new List<Popup>();
        readonly Stack<Popup> popupPool = new Stack<Popup>();
        readonly List<Speech> speeches = new List<Speech>();
        readonly Stack<Speech> speechPool = new Stack<Speech>();
        float floorHeight = 3f;

        public PopupsView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            speechLayer = UIBuild.Stretch(UIBuild.Rect("Speech", canvas));
            layer = UIBuild.Stretch(UIBuild.Rect("Popups", canvas)); // luck popups always above speech
            if (ctx.Data != null) floorHeight = ctx.Data.Layout.floorHeight;
        }

        public int ActivePopupCount => popups.Count;
        public int ActiveSpeechCount => speeches.Count;
        public string LastPopupText { get; private set; } = "";

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
            p.Number = UIBuild.Text("Number", p.Rect, "", 52f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Number.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(170f, 56f));
            p.Number.textWrappingMode = TextWrappingModes.NoWrap;
            p.Verdict = UIBuild.Text("Verdict", p.Rect, "", 22f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Verdict.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(170f, 28f));
            p.Verdict.textWrappingMode = TextWrappingModes.NoWrap;
            p.Caption = UIBuild.Text("Caption", p.Rect, "", 15f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(p.Caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(170f, 20f));
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

        // ------------------------------------------------------------------ speech

        public void OnGuestSpoke(GuestSpoke e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            Speech s = null;
            foreach (var x in speeches) if (x.Guest == e.Guest) { s = x; break; }
            if (s == null)
            {
                s = speechPool.Count > 0 ? speechPool.Pop() : MakeSpeech();
                s.Rect.gameObject.SetActive(true);
                s.Guest = e.Guest;
                s.LastWorld = new Vector3(float.NaN, 0f, 0f);
                speeches.Add(s);
            }
            s.Age = 0f;
            s.Text.text = e.Text;
            var size = s.Text.GetPreferredValues(e.Text, SpeechMaxW, 0f);
            float w = Mathf.Min(SpeechMaxW, size.x) + 16f;
            var pref = s.Text.GetPreferredValues(e.Text, w - 16f, 0f);
            s.Rect.sizeDelta = new Vector2(w, pref.y + 10f);
            s.Rect.SetAsLastSibling();
            PlaceSpeech(s);
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
            s.Text = UIBuild.Text("Text", s.Rect, "", 15f, UIStyle.Dark, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(s.Text.rectTransform, 6f);
            return s;
        }

        void PlaceSpeech(Speech s)
        {
            if (ctx.Locator != null && ctx.Locator.TryGetGuestPosition(s.Guest, out var wp)) s.LastWorld = UIContext.ToVector3(wp);
            var size = s.Rect.sizeDelta;
            bool riding = ctx.Guests != null && ctx.Guests.TryGet(s.Guest, out var info) && info.State == GuestState.Riding;
            Vector2 pos;
            if (riding && ctx.Locator != null)
            {
                // riders speak from above their chip grid (left of the car), leaving the shaft free for luck popups
                float halfCar = (ctx.Data != null ? ctx.Data.Layout.carWidth * 0.5f : 1.2f) + 0.1f;
                if (!ctx.WorldToCanvas(UIContext.ToVector3(ctx.Locator.CarPosition) + new Vector3(-halfCar, 0.1f, 0f), out pos))
                    pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
                pos.x -= 6f + size.x * 0.5f;
                pos.y += 2f * (GuestBubblesView.ChipH + 3f) + 4f;
            }
            else if (float.IsNaN(s.LastWorld.x) || !ctx.WorldToCanvas(s.LastWorld + Vector3.up * (floorHeight * 0.98f), out pos))
                pos = new Vector2(layer.rect.width * 0.5f, layer.rect.height * 0.6f);
            pos.x = Mathf.Clamp(pos.x, size.x * 0.5f + 8f, layer.rect.width - size.x * 0.5f - 8f);
            pos.y = Mathf.Clamp(pos.y, 8f, layer.rect.height - size.y - 8f);
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
                if (s.Age >= SpeechLife) { s.Rect.gameObject.SetActive(false); speeches.RemoveAt(i); speechPool.Push(s); continue; }
                s.Group.alpha = s.Age > SpeechLife - 0.4f ? Mathf.Clamp01((SpeechLife - s.Age) / 0.4f) : 1f;
                PlaceSpeech(s);
            }
            Unstack();
        }

        public void Clear()
        {
            foreach (var p in popups) { p.Rect.gameObject.SetActive(false); popupPool.Push(p); }
            popups.Clear();
            foreach (var s in speeches) { s.Rect.gameObject.SetActive(false); speechPool.Push(s); }
            speeches.Clear();
        }
    }
}
