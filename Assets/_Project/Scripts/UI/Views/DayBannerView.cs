using FloorF.Core;
using TMPro;
using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// "DAY 3" / "3일차" strip that slides in when a real day starts running (Intro -> Running), holds a moment and slides
    /// out. The last day says so; Friday the 13th gets a red edge, star night a violet one between stars. Never a raycast
    /// target; frozen while paused.
    /// </summary>
    sealed class DayBannerView
    {
        public const float In = 0.3f, Hold = 1.1f, Out = 0.35f, Life = In + Hold + Out, Slide = 220f, Height = 56f, CentreY = 0.62f;
        readonly UIContext ctx;
        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly UnityEngine.UI.Image fill;
        readonly UnityEngine.UI.Image[] edges;
        readonly TextMeshProUGUI text;
        float age = -1f;

        public DayBannerView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            root = UIBuild.Rect("DayBanner", canvas);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, CentreY);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(520f, Height);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            fill = UIBuild.Box("Fill", root, new Color(0.03f, 0.03f, 0.06f, 0.86f));
            UIBuild.Stretch(fill.rectTransform);
            // gold hairlines top and bottom only: a strip, not a card
            edges = new UnityEngine.UI.Image[2];
            for (int i = 0; i < 2; i++)
            {
                var e = UIBuild.Box("Edge" + i, root, UIStyle.CardEdge);
                var rt = e.rectTransform;
                rt.anchorMin = new Vector2(0f, i);
                rt.anchorMax = new Vector2(1f, i);
                rt.pivot = new Vector2(0.5f, i);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, 3f);
                edges[i] = e;
            }
            text = UIBuild.Text("Text", root, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableVertexGradient = true;
            root.gameObject.SetActive(false);
        }

        public void OnPhaseChanged(DayPhaseChanged e)
        {
            if (e.Current == DayPhase.Running && e.Previous == DayPhase.Intro && ctx.HasGame && !ctx.Day.IsTutorial) Play();
            else if (e.Current != DayPhase.Running) Stop();
        }

        public void Play()
        {
            var day = ctx.Day;
            string label = ctx.F(UIStrings.DayBanner, day.DisplayDay);
            bool last = day.PlayableDayCount > 0 && day.DisplayDay >= day.PlayableDayCount;
            if (last) label += "  ·  " + ctx.S(UIStrings.FinalDayBanner);
            bool star = day.IsStarNight && !day.IsFriday13;
            if (star) label = UIFonts.Star + "  " + label + "  " + UIFonts.Star;
            text.text = label;
            var edge = day.IsFriday13 ? UIStyle.Unlucky : star ? HudView.StarEdge : UIStyle.CardEdge;
            foreach (var e in edges) e.color = edge;
            fill.color = day.IsFriday13 ? new Color(0.07f, 0.01f, 0.015f, 0.88f) : star ? HudView.StarBoxFill : new Color(0.03f, 0.03f, 0.06f, 0.86f);
            text.colorGradient = day.IsFriday13
                ? new VertexGradient(UIStyle.Hex("#FF8A8A"), UIStyle.Hex("#FF8A8A"), UIStyle.Unlucky, UIStyle.Unlucky)
                : star ? new VertexGradient(FlowScreensView.StarTitle, FlowScreensView.StarTitle, HudView.StarInk, HudView.StarInk)
                : MenuView.GoldGradient;
            text.color = Color.white;
            float w = text.GetPreferredValues(label, float.PositiveInfinity, float.PositiveInfinity).x;
            root.sizeDelta = new Vector2(Mathf.Max(320f, Mathf.Ceil(w) + 72f), Height);
            age = 0f;
            root.gameObject.SetActive(true);
            Animate();
        }

        public void Stop()
        {
            age = -1f;
            root.gameObject.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (age < 0f) return;
            if (!ctx.Paused) age += dt;
            if (age >= Life) { Stop(); return; }
            Animate();
        }

        void Animate()
        {
            float x, a;
            if (age < In) { float k = age / In; k = 1f - (1f - k) * (1f - k); x = Mathf.Lerp(-Slide, 0f, k); a = k; }
            else if (age < In + Hold) { x = 0f; a = 1f; }
            else { float k = (age - In - Hold) / Out; x = Mathf.Lerp(0f, Slide, k * k); a = 1f - k; }
            root.anchoredPosition = new Vector2(Mathf.Round(x), 0f);
            group.alpha = a;
        }

        public bool Visible => root.gameObject.activeInHierarchy;
        public string Text => Visible ? text.text : null;
        public RectTransform Rect => root;
        public float Age => age;
    }
}
