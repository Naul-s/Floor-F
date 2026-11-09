using System.Collections.Generic;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Charms during the day (IShopService.ActiveCharms, Running only): a compact box in the HUD's right column, under the
    /// rule box / event badge - "TODAY'S CHARMS", then one line per charm (coloured glyph + name). CharmTriggered pops
    /// that line (scale + flash); a shield that has eaten all its Unlucky verdicts reads "used". Effects other than the
    /// shield also float a short "Talisman helped!" over the guest (the shield's own feedback is the cyan BLOCKED!
    /// popup, see PopupsView.OnLuckJudged).
    /// </summary>
    sealed class CharmsView
    {
        public const float TitleH = 22f, LineH = 30f, Gap = 8f, BoxPad = 6f;
        const float PopLife = 0.6f, FloatLife = 1.6f, PopScale = 0.12f;
        static readonly Color BoxFill = new Color(0.05f, 0.055f, 0.08f, 1f);

        sealed class Line
        {
            public RectTransform Rect;
            public TextMeshProUGUI Name, State;
            public StickerId Id;
            public float Pop;
        }

        readonly UIContext ctx;
        readonly HudView hud;
        readonly RectTransform box, floatRect;
        readonly Image boxEdge;
        readonly TextMeshProUGUI title, floatText;
        readonly CanvasGroup floatGroup;
        readonly List<Line> lines = new List<Line>();
        readonly Dictionary<StickerId, int> triggers = new Dictionary<StickerId, int>();
        int shownCount = -1;
        bool shownKo;
        float floatAge = -1f;
        Vector2 floatOrigin;

        public CharmsView(UIContext ctx, RectTransform canvas, HudView hud)
        {
            this.ctx = ctx;
            this.hud = hud;
            // opaque fill: the edge image sits behind it, a translucent fill would wash the box cyan (and white on a pop)
            boxEdge = UIBuild.Framed("CharmsBox", canvas, BoxFill, UIStyle.Hex(UIFormat.ShieldHex), 2f);
            box = boxEdge.rectTransform;
            UIBuild.Place(box, new Vector2(1f, 1f), new Vector2(-24f, -400f), new Vector2(HudView.ColumnWidth, 60f));
            title = UIBuild.Text("Title", box, "", 12f, UIStyle.Hex(UIFormat.ShieldHex), TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -4f), new Vector2(HudView.ColumnWidth - 28f, TitleH));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            box.gameObject.SetActive(false);

            floatRect = UIBuild.Framed("CharmFloat", canvas, new Color(0.02f, 0.06f, 0.09f, 0.92f), UIStyle.Hex(UIFormat.ShieldHex), 2f).rectTransform;
            floatRect.anchorMin = floatRect.anchorMax = Vector2.zero;
            floatRect.pivot = new Vector2(0.5f, 0f);
            floatGroup = floatRect.gameObject.AddComponent<CanvasGroup>();
            floatGroup.blocksRaycasts = false;
            floatText = UIBuild.Text("Text", floatRect, "", 24f, UIStyle.Hex(UIFormat.ShieldHex), TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(floatText.rectTransform, 4f);
            floatText.textWrappingMode = TextWrappingModes.NoWrap;
            floatRect.gameObject.SetActive(false);
        }

        IReadOnlyList<StickerDef> Active => ctx.Shop != null ? ctx.Shop.ActiveCharms : null;

        string CharmName(StickerDef s)
        {
            string n = ctx.StickerName(s);
            return string.IsNullOrEmpty(n) ? (s != null ? s.id : "?") : n;
        }

        StickerDef Find(StickerId id)
        {
            var list = Active;
            if (list != null) for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Id == id) return list[i];
            return ctx.Data != null && ctx.Data.TryGetSticker(id, out var s) ? s : null;
        }

        /// <summary>"◈ Talisman blocked it!" (shield) / "♥ Kind Bell helped!" (others), glyph in the charm's colour.</summary>
        public string TriggerLine(StickerId id)
        {
            var s = Find(id);
            string glyph = s != null ? "<color=" + UIFormat.CharmHex(s.effect) + ">" + UIFormat.CharmGlyph(s.effect) + "</color> " : "";
            bool shield = s != null && string.Equals((s.effect ?? "").Trim(), StickerEffects.Shield, System.StringComparison.OrdinalIgnoreCase);
            return glyph + ctx.F(shield ? UIStrings.CharmBlocked : UIStrings.CharmHelped, s != null ? CharmName(s) : ctx.S(UIStrings.KindCharm));
        }

        public bool IsShield(StickerId id)
        {
            var s = Find(id);
            return s == null || string.Equals((s.effect ?? "").Trim(), StickerEffects.Shield, System.StringComparison.OrdinalIgnoreCase);
        }

        public void OnTriggered(CharmTriggered e)
        {
            triggers.TryGetValue(e.Charm, out int n);
            triggers[e.Charm] = n + 1;
            foreach (var l in lines) if (l.Id == e.Charm) l.Pop = PopLife;
            shownCount = -1; // "used" may change
            if (!IsShield(e.Charm)) ShowFloat(e);
        }

        void ShowFloat(CharmTriggered e)
        {
            floatText.text = TriggerLine(e.Charm);
            if (floatText.font != null) floatText.font.TryAddCharacters(floatText.text);
            float w = Mathf.Ceil(floatText.GetPreferredValues(floatText.text, float.PositiveInfinity, float.PositiveInfinity).x) + 20f;
            floatRect.sizeDelta = new Vector2(w, 36f);
            var canvas = ctx.CanvasRect != null ? ctx.CanvasRect.rect.size : new Vector2(UIStyle.RefWidth, UIStyle.RefHeight);
            var origin = canvas * 0.5f;
            float fh = ctx.Data != null ? ctx.Data.Layout.floorHeight : 3f;
            if (ctx.Locator != null && ctx.Locator.TryGetGuestPosition(e.Guest, out var wp) && ctx.WorldToCanvas(UIContext.ToVector3(wp) + Vector3.up * fh * 0.6f, out var pos)) origin = pos;
            else if (ctx.Locator != null && ctx.WorldToCanvas(UIContext.ToVector3(ctx.Locator.CarPosition) + Vector3.up * fh * 0.6f, out var car)) origin = car;
            // never over the floor numbers: beside the centre column on the guest's side
            if (ctx.CenterColumn(6f, out float cMin, out float cMax) && origin.x + w * 0.5f > cMin && origin.x - w * 0.5f < cMax)
                origin.x = origin.x < (cMin + cMax) * 0.5f ? cMin - w * 0.5f : cMax + w * 0.5f;
            origin.x = Mathf.Clamp(origin.x, w * 0.5f + 8f, canvas.x - w * 0.5f - 8f);
            origin.y = Mathf.Clamp(origin.y, 8f, canvas.y - 36f - 48f);
            floatOrigin = origin;
            floatAge = 0f;
            floatRect.gameObject.SetActive(true);
            floatRect.SetAsLastSibling();
        }

        /// <summary>A new day / run: the trigger counts start over, nothing floating.</summary>
        public void Clear()
        {
            triggers.Clear();
            shownCount = -1;
            floatAge = -1f;
            floatRect.gameObject.SetActive(false);
        }

        public void RefreshLanguage() => shownCount = -1;

        Line MakeLine()
        {
            var l = new Line();
            l.Rect = UIBuild.Rect("Charm", box);
            l.Rect.anchorMin = l.Rect.anchorMax = l.Rect.pivot = new Vector2(0f, 1f);
            l.Rect.sizeDelta = new Vector2(HudView.ColumnWidth - 28f, LineH);
            l.Name = UIBuild.Text("Name", l.Rect, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left, bold: true);
            UIBuild.Stretch(l.Name.rectTransform);
            l.Name.rectTransform.pivot = new Vector2(0f, 0.5f);
            l.Name.textWrappingMode = TextWrappingModes.NoWrap;
            l.State = UIBuild.Text("State", l.Rect, "", 12f, UIStyle.InkDim, TextAlignmentOptions.Right);
            UIBuild.Stretch(l.State.rectTransform);
            l.State.textWrappingMode = TextWrappingModes.NoWrap;
            return l;
        }

        void Rebuild(IReadOnlyList<StickerDef> list)
        {
            int n = list != null ? list.Count : 0;
            shownCount = n;
            shownKo = ctx.Korean;
            UIBuild.Set(title, ctx.S(UIStrings.CharmsToday));
            while (lines.Count < n) lines.Add(MakeLine());
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                bool on = i < n && list[i] != null;
                l.Rect.gameObject.SetActive(on);
                if (!on) continue;
                var s = list[i];
                l.Id = s.Id;
                l.Rect.anchoredPosition = new Vector2(14f, -(BoxPad + TitleH + i * LineH));
                bool shield = string.Equals((s.effect ?? "").Trim(), StickerEffects.Shield, System.StringComparison.OrdinalIgnoreCase);
                triggers.TryGetValue(s.Id, out int used);
                bool spent = shield && used >= UIFormat.ShieldCount(s.amount);
                l.State.text = spent ? ctx.S(UIStrings.CharmUsed) : "";
                float stateW = spent ? Mathf.Ceil(l.State.GetPreferredValues(l.State.text, float.PositiveInfinity, float.PositiveInfinity).x) + 8f : 0f;
                l.Name.rectTransform.offsetMax = new Vector2(-stateW, 0f);
                l.Name.text = "<color=" + UIFormat.CharmHex(s.effect) + ">" + UIFormat.CharmGlyph(s.effect) + "</color> " + CharmName(s);
                if (l.Name.font != null) l.Name.font.TryAddCharacters(l.Name.text);
                UIBuild.Fit(l.Name, 24f);
                l.Name.alpha = spent ? 0.45f : 1f;
            }
            box.sizeDelta = new Vector2(HudView.ColumnWidth, BoxPad + TitleH + n * LineH + BoxPad);
        }

        public void Tick(float dt)
        {
            var list = Active;
            bool show = ctx.HasGame && ctx.Day.Phase == DayPhase.Running && !ctx.Day.IsTutorial && list != null && list.Count > 0
                        && !(ctx.Menu != null && ctx.Menu.IsOnTitle);
            if (box.gameObject.activeSelf != show) box.gameObject.SetActive(show);
            if (show)
            {
                if (list.Count != shownCount || ctx.Korean != shownKo) Rebuild(list);
                box.anchoredPosition = new Vector2(-24f, -(hud.RightColumnBottom + Gap));
                float edgeK = 0f;
                foreach (var l in lines)
                {
                    if (!l.Rect.gameObject.activeSelf) continue;
                    float scale = 1f;
                    if (l.Pop > 0f)
                    {
                        l.Pop -= dt;
                        float t = 1f - Mathf.Clamp01(l.Pop / PopLife);
                        scale = 1f + PopScale * Mathf.Sin(t * Mathf.PI);
                        edgeK = Mathf.Max(edgeK, Mathf.Sin(t * Mathf.PI));
                    }
                    // only the name pops, growing out of its left edge: the "used" tag and the box edge stay put
                    l.Name.rectTransform.localScale = new Vector3(scale, scale, 1f);
                }
                boxEdge.color = Color.Lerp(UIStyle.Hex(UIFormat.ShieldHex), Color.white, edgeK);
            }
            if (floatAge >= 0f)
            {
                floatAge += dt;
                if (floatAge >= FloatLife) { floatAge = -1f; floatRect.gameObject.SetActive(false); }
                else
                {
                    floatRect.anchoredPosition = floatOrigin + new Vector2(0f, Mathf.Min(floatAge, 1f) * 30f);
                    floatGroup.alpha = floatAge > FloatLife - 0.4f ? Mathf.Clamp01((FloatLife - floatAge) / 0.4f) : 1f;
                }
            }
        }

        // ------------------------------------------------------------------ test hooks

        public bool Visible => box.gameObject.activeInHierarchy;
        public RectTransform Box => box;
        public bool FloatVisible => floatRect.gameObject.activeInHierarchy;
        public string FloatText => FloatVisible ? floatText.text : null;
        public RectTransform Float => floatRect;
        public string Text
        {
            get
            {
                if (!Visible) return null;
                var sb = new System.Text.StringBuilder(title.text);
                foreach (var l in lines) if (l.Rect.gameObject.activeSelf) sb.Append('\n').Append(l.Name.text).Append(l.State.text.Length > 0 ? " (" + l.State.text + ")" : "");
                return sb.ToString();
            }
        }
        public float MaxLineScale { get { float m = 1f; foreach (var l in lines) if (l.Rect.gameObject.activeSelf) m = Mathf.Max(m, l.Name.rectTransform.localScale.x); return m; } }

        /// <summary>null when every charm line fits the box at 24px and the box stays on screen, else why not.</summary>
        public string Overflow(RectTransform canvas)
        {
            if (!Visible) return null;
            var r = UIBuild.CanvasRect(canvas, box);
            if (r.yMin < 0f || r.xMax > canvas.rect.width + 0.5f) return "charms box off screen " + r;
            foreach (var l in lines)
            {
                if (!l.Rect.gameObject.activeSelf) continue;
                if (l.Name.fontSize < 24f) return "'" + l.Name.text + "' shrank to " + l.Name.fontSize;
                if (l.State.text.Length > 0)
                {
                    float sw = l.State.GetPreferredValues(l.State.text, float.PositiveInfinity, float.PositiveInfinity).x;
                    var sr = UIBuild.CanvasRect(canvas, l.State.rectTransform);
                    if (sr.xMax > r.xMax - 2f || sr.xMax - sw < r.xMin) return "'" + l.State.text + "' outside the box " + sr + " vs " + r;
                }
                float w = l.Name.GetPreferredValues(l.Name.text, float.PositiveInfinity, float.PositiveInfinity).x;
                if (w > l.Name.rectTransform.rect.width + 0.5f) return "'" + l.Name.text + "' needs " + w + " > " + l.Name.rectTransform.rect.width;
            }
            return null;
        }
    }
}
