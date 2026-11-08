using System.Collections.Generic;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Star night wish stars (WishStarAppeared / WishMade / WishStarFaded, IGameFlow.MakeWish). Rendering draws the star;
    /// this adds an invisible, generous click target that follows it (viewport From -> To, linear, advanced by the day
    /// clock so it freezes while paused / held), corner brackets around it, a "Make a wish! [E]" hint next to the stars
    /// until the run's first wish, a one-time explain card when star night starts (tutorial hints on), and a gold
    /// "Wish granted! +4 Luck" float-up with a small sparkle burst on WishMade. A fade shows nothing.
    /// Keyboard: E wishes on the oldest star still crossing (KeyboardInput does not use E).
    /// Layers: <see cref="Targets"/> sits under the panel/HUD (their buttons stay on top), <see cref="Fx"/> (hint, card,
    /// float-ups; never raycast targets) is raised above the HUD by UIRoot.
    /// </summary>
    sealed class WishView
    {
        /// <summary>Click target edge (canvas units): the streak is a few pixels, the target is generous.</summary>
        public const float TargetSize = 112f;
        public const float GrantLife = 1.8f, SparkLife = 0.6f, CardLife = 9f, Grace = 0.4f;
        const int Sparks = 8;

        sealed class Star
        {
            public int Id;
            public Vector2 From, To;
            public float Seconds, Age;
            public RectTransform Rect, Brackets;
            public Button Button;
        }

        sealed class Grant
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public RectTransform[] Sparks;
            public Vector2[] Dirs;
            public Vector2 Origin;
            public float Age;
        }

        readonly UIContext ctx;
        public readonly RectTransform Targets, Fx;
        readonly RectTransform hint, card;
        readonly TextMeshProUGUI hintText, cardTitle, cardBody;
        readonly List<Star> stars = new List<Star>(), starPool = new List<Star>();
        readonly List<Grant> grants = new List<Grant>(), grantPool = new List<Grant>();
        float lastElapsed = -1f, cardAge = -1f;
        bool wishedThisRun, cardShownThisRun;

        public WishView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            Targets = UIBuild.Stretch(UIBuild.Rect("WishTargets", canvas));
            Fx = UIBuild.Stretch(UIBuild.Rect("WishFx", canvas));

            // one-time explain card: right column under the event badge (the tutorial column), not clickable
            var edge = UIBuild.Framed("WishCard", Fx, HudView.StarBoxFill, HudView.StarEdge, 2f);
            card = edge.rectTransform;
            UIBuild.Place(card, new Vector2(1f, 1f), new Vector2(-24f, -CardTop), new Vector2(HudCardW, 100f));
            card.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(14, 14, 8, 10);
            v.spacing = 4f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cardTitle = UIBuild.Text("Title", card, "", 24f, FlowScreensView.StarTitle, TextAlignmentOptions.Left, bold: true);
            cardBody = UIBuild.Text("Body", card, "", 24f, HudView.StarInk, TextAlignmentOptions.Left);
            cardBody.lineSpacing = 4f;
            card.gameObject.SetActive(false);

            // the hint next to a star
            var he = UIBuild.Framed("WishHint", Fx, new Color(0.02f, 0.02f, 0.07f, 0.9f), UIStyle.Lucky, 2f);
            hint = he.rectTransform;
            hint.anchorMin = hint.anchorMax = Vector2.zero;
            hint.pivot = new Vector2(0f, 1f);
            hintText = UIBuild.Text("Text", hint, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(hintText.rectTransform, 4f);
            hintText.textWrappingMode = TextWrappingModes.NoWrap;
            hint.gameObject.SetActive(false);
        }

        /// <summary>Explain card: top edge (px from the screen top) and width (the HUD's right column).</summary>
        public const float CardTop = HudView.RulesBottom + HudView.Friday13LineH + HudView.BadgeGap + HudView.BadgeH + 10f, HudCardW = 376f;

        bool Running => ctx.HasGame && ctx.Phase == DayPhase.Running;
        bool HintsOn => ctx.Settings == null || ctx.Settings.ShowTutorialHints;

        // ------------------------------------------------------------------ events

        public void OnAppeared(WishStarAppeared e)
        {
            var s = FindStar(e.Id);
            if (s == null)
            {
                s = starPool.Count > 0 ? Pop(starPool) : MakeStar();
                stars.Add(s);
            }
            s.Id = e.Id;
            s.From = new Vector2(e.FromX, e.FromY);
            s.To = new Vector2(e.ToX, e.ToY);
            s.Seconds = Mathf.Max(0.1f, e.Seconds);
            s.Age = 0f;
            s.Rect.gameObject.SetActive(true);
            s.Rect.SetAsLastSibling();
            Place(s);
            UpdateHint();
        }

        public void OnMade(WishMade e)
        {
            var s = FindStar(e.Id);
            Vector2 at = s != null ? s.Rect.anchoredPosition : StarCanvasPos(0.5f, 0.8f);
            if (s != null) Remove(s);
            wishedThisRun = true;
            if (cardAge >= 0f) cardAge = Mathf.Max(cardAge, CardLife - 0.4f); // the card did its job
            ShowGrant(at, e.LuckGain);
            UpdateHint();
        }

        public void OnFaded(WishStarFaded e)
        {
            var s = FindStar(e.Id);
            if (s != null) Remove(s); // nothing to see: it just goes
            UpdateHint();
        }

        /// <summary>New run: the hint and the explain card are shown again.</summary>
        public void Reset()
        {
            Clear();
            wishedThisRun = cardShownThisRun = false;
        }

        /// <summary>Leaving Running (or a new run): no targets, hint, card or float-ups left behind.</summary>
        public void Clear()
        {
            for (int i = stars.Count - 1; i >= 0; i--) Remove(stars[i]);
            for (int i = grants.Count - 1; i >= 0; i--) RecycleGrant(i);
            hint.gameObject.SetActive(false);
            card.gameObject.SetActive(false);
            cardAge = -1f;
            lastElapsed = -1f;
        }

        // ------------------------------------------------------------------ commands

        void TryWish(int id)
        {
            bool ok = ctx.Flow != null && ctx.Flow.MakeWish(id);
            ctx.Sound(ok);
        }

        /// <summary>E: the oldest star still crossing (the one about to fade).</summary>
        void WishOnCurrent()
        {
            Star best = null;
            foreach (var s in stars)
                if (s.Age <= s.Seconds && (best == null || s.Age / s.Seconds > best.Age / best.Seconds)) best = s;
            if (best != null) TryWish(best.Id);
        }

        static bool EPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.eKey.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            bool running = Running;
            float elapsed = ctx.HasGame ? ctx.Day.ElapsedSeconds : 0f;
            // stars move with the day clock: frozen while paused / held, like the clock itself
            float step = running && lastElapsed >= 0f ? Mathf.Max(0f, elapsed - lastElapsed) : 0f;
            lastElapsed = running ? elapsed : -1f;
            if (!running && (stars.Count > 0 || card.gameObject.activeSelf || hint.gameObject.activeSelf)) Clear();

            for (int i = stars.Count - 1; i >= 0; i--)
            {
                var s = stars[i];
                s.Age += step;
                if (s.Age > s.Seconds + Grace) { Remove(s); continue; } // WishStarFaded missed: never a stale target
                Place(s);
            }
            UpdateHint();

            if (running && !ctx.Paused && stars.Count > 0 && EPressed()) WishOnCurrent();

            // the one-time explain card when star night begins
            if (running && ctx.Day.IsStarNight && !ctx.Day.IsTutorial && !cardShownThisRun && HintsOn)
            {
                cardShownThisRun = true;
                cardAge = 0f;
                int bonus = Bonus;
                cardTitle.text = UIFonts.Star + " " + ctx.S(UIStrings.WishCardTitle);
                cardBody.text = ctx.F(UIStrings.WishCardBody, bonus);
                card.gameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            }
            if (cardAge >= 0f)
            {
                if (!ctx.Paused) cardAge += Time.unscaledDeltaTime;
                if (cardAge >= CardLife || !HintsOn) { card.gameObject.SetActive(false); cardAge = -1f; }
            }

            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var g = grants[i];
                g.Age += dt;
                if (g.Age >= GrantLife) { RecycleGrant(i); continue; }
                AnimateGrant(g);
            }
        }

        int Bonus => ctx.Data != null ? ctx.Data.Rules.wishLuckBonus : 4;

        Vector2 StarCanvasPos(float vx, float vy)
        {
            var size = ctx.CanvasRect.rect.size;
            PresentationContract.ViewportToScreen(vx, vy, 1f, size.x, size.y, out float sx, out float sy);
            return new Vector2(sx, sy);
        }

        void Place(Star s)
        {
            var v = Vector2.Lerp(s.From, s.To, Mathf.Clamp01(s.Age / s.Seconds));
            var p = StarCanvasPos(v.x, v.y);
            s.Rect.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
            // brackets breathe slowly (1.25 Hz scale, no flashing)
            float k = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 7.85f);
            s.Brackets.localScale = new Vector3(k, k, 1f);
        }

        void UpdateHint()
        {
            Star newest = null;
            foreach (var s in stars) if (s.Age <= s.Seconds && (newest == null || s.Age < newest.Age)) newest = s;
            bool show = newest != null && !wishedThisRun && Running;
            if (hint.gameObject.activeSelf != show) hint.gameObject.SetActive(show);
            if (!show) return;
            string text = ctx.S(UIStrings.WishHint);
            if (!string.Equals(hintText.text, text, System.StringComparison.Ordinal))
            {
                hintText.text = text;
                float w = hintText.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x;
                hint.sizeDelta = new Vector2(Mathf.Ceil(w) + 20f, 36f);
            }
            // beside the target, below-right of the star; flipped to stay on screen
            var size = ctx.CanvasRect.rect.size;
            var p = newest.Rect.anchoredPosition;
            float half = TargetSize * 0.5f;
            float x = p.x + half * 0.6f, y = p.y - half * 0.6f;
            if (x + hint.sizeDelta.x > size.x - 8f) x = p.x - half * 0.6f - hint.sizeDelta.x;
            if (y - hint.sizeDelta.y < 8f) y = p.y + half * 0.6f + hint.sizeDelta.y;
            y = Mathf.Min(y, size.y - 8f);
            x = Mathf.Max(8f, x);
            hint.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        // ------------------------------------------------------------------ pieces

        Star MakeStar()
        {
            var s = new Star();
            var img = UIBuild.Box("WishStar", Targets, new Color(1f, 1f, 1f, 0f), raycast: true); // invisible, still hit
            s.Rect = img.rectTransform;
            s.Rect.anchorMin = s.Rect.anchorMax = Vector2.zero;
            s.Rect.pivot = new Vector2(0.5f, 0.5f);
            s.Rect.sizeDelta = new Vector2(TargetSize, TargetSize);
            s.Button = img.gameObject.AddComponent<Button>();
            s.Button.targetGraphic = img;
            s.Button.transition = Selectable.Transition.None;
            var nav = s.Button.navigation;
            nav.mode = Navigation.Mode.None;
            s.Button.navigation = nav;
            s.Button.onClick.AddListener(() => TryWish(s.Id));
            // four corner brackets a little inside the target
            s.Brackets = UIBuild.Rect("Brackets", s.Rect);
            s.Brackets.sizeDelta = new Vector2(TargetSize * 0.62f, TargetSize * 0.62f);
            var c = new Color(UIStyle.Lucky.r, UIStyle.Lucky.g, UIStyle.Lucky.b, 0.8f);
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                Bar(s.Brackets, corner, c, new Vector2(14f, 3f));
                Bar(s.Brackets, corner, c, new Vector2(3f, 14f));
            }
            return s;
        }

        static void Bar(RectTransform parent, Vector2 corner, Color c, Vector2 size)
        {
            var b = UIBuild.Box("Bracket", parent, c).rectTransform;
            b.anchorMin = b.anchorMax = b.pivot = corner;
            b.anchoredPosition = Vector2.zero;
            b.sizeDelta = size;
        }

        void Remove(Star s)
        {
            s.Rect.gameObject.SetActive(false);
            stars.Remove(s);
            starPool.Add(s);
        }

        Star FindStar(int id)
        {
            foreach (var s in stars) if (s.Id == id) return s;
            return null;
        }

        static T Pop<T>(List<T> list)
        {
            var x = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return x;
        }

        void ShowGrant(Vector2 at, int gain)
        {
            var g = grantPool.Count > 0 ? Pop(grantPool) : MakeGrant();
            g.Rect.gameObject.SetActive(true);
            g.Rect.SetAsLastSibling();
            g.Text.text = ctx.F(UIStrings.WishGranted, gain);
            float w = g.Text.GetPreferredValues(g.Text.text, float.PositiveInfinity, float.PositiveInfinity).x;
            g.Rect.sizeDelta = new Vector2(Mathf.Ceil(w) + 24f, 40f);
            // keep the whole float-up (incl. its rise) on screen
            var size = ctx.CanvasRect.rect.size;
            float half = g.Rect.sizeDelta.x * 0.5f;
            g.Origin = new Vector2(Mathf.Clamp(at.x, half + 8f, size.x - half - 8f), Mathf.Clamp(at.y, 30f, size.y - 40f - 60f - 8f));
            g.Age = 0f;
            for (int i = 0; i < Sparks; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / Sparks;
                g.Dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 70f : 48f);
            }
            grants.Add(g);
            AnimateGrant(g);
        }

        Grant MakeGrant()
        {
            var g = new Grant();
            g.Rect = UIBuild.Rect("WishGrant", Fx);
            g.Rect.anchorMin = g.Rect.anchorMax = Vector2.zero;
            g.Rect.pivot = new Vector2(0.5f, 0.5f);
            g.Group = g.Rect.gameObject.AddComponent<CanvasGroup>();
            g.Group.blocksRaycasts = false;
            g.Group.interactable = false;
            g.Sparks = new RectTransform[Sparks];
            g.Dirs = new Vector2[Sparks];
            for (int i = 0; i < Sparks; i++)
            {
                var sp = UIBuild.Box("Spark", g.Rect, i % 2 == 0 ? UIStyle.Lucky : UIStyle.Ink).rectTransform;
                sp.anchorMin = sp.anchorMax = new Vector2(0.5f, 0.5f);
                sp.sizeDelta = new Vector2(6f, 6f);
                g.Sparks[i] = sp;
            }
            var back = UIBuild.Framed("Back", g.Rect, new Color(0f, 0f, 0f, 0.78f), UIStyle.Lucky, 2f);
            UIBuild.Stretch(back.rectTransform);
            g.Text = UIBuild.Text("Text", g.Rect, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(g.Text.rectTransform);
            g.Text.textWrappingMode = TextWrappingModes.NoWrap;
            return g;
        }

        static void AnimateGrant(Grant g)
        {
            float t = g.Age;
            float punch = t < 0.12f ? Mathf.Lerp(0.5f, 1.2f, t / 0.12f) : t < 0.24f ? Mathf.Lerp(1.2f, 1f, (t - 0.12f) / 0.12f) : 1f;
            g.Rect.localScale = new Vector3(punch, punch, 1f);
            g.Rect.anchoredPosition = g.Origin + new Vector2(0f, Mathf.Min(t / 1.2f, 1f) * 60f);
            g.Group.alpha = t > GrantLife - 0.5f ? Mathf.Clamp01((GrantLife - t) / 0.5f) : 1f;
            float k = Mathf.Clamp01(t / SparkLife);
            float ease = 1f - (1f - k) * (1f - k);
            for (int i = 0; i < g.Sparks.Length; i++)
            {
                var sp = g.Sparks[i];
                bool on = k < 1f;
                if (sp.gameObject.activeSelf != on) sp.gameObject.SetActive(on);
                if (!on) continue;
                sp.anchoredPosition = g.Dirs[i] * ease / Mathf.Max(0.01f, punch);
                float s = 6f * (1f - k * 0.7f);
                sp.sizeDelta = new Vector2(s, s);
            }
        }

        void RecycleGrant(int index)
        {
            var g = grants[index];
            g.Rect.gameObject.SetActive(false);
            grants.RemoveAt(index);
            grantPool.Add(g);
        }

        // ------------------------------------------------------------------ test hooks

        public int TargetCount => stars.Count;
        public RectTransform TargetRect(int id) => FindStar(id)?.Rect;
        public void Click(int id) { var s = FindStar(id); if (s != null) s.Button.onClick.Invoke(); }
        public bool HintVisible => hint.gameObject.activeInHierarchy;
        public string HintText => HintVisible ? hintText.text : null;
        public RectTransform Hint => hint;
        public bool CardVisible => card.gameObject.activeInHierarchy;
        public string CardText => CardVisible ? cardTitle.text + "\n" + cardBody.text : null;
        public RectTransform Card => card;
        public int GrantCount => grants.Count;
        public string GrantText => grants.Count > 0 ? grants[grants.Count - 1].Text.text : null;
        public RectTransform GrantRect => grants.Count > 0 ? grants[grants.Count - 1].Rect : null;
    }
}
