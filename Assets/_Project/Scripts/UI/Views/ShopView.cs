using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// GDD 5 shop (Shop phase). Side card on the right so the floor signs and the elevator panel stay visible.
    /// <para>Offers (IShopService.Offers, a random draw each visit) are cards in a two-column grid: headline ("4 → F" /
    /// "◈ Talisman"), price, a kind badge (STICKER / CHARM) and a "1 DAY" tag (every purchase lasts tomorrow only), stock
    /// or what is missing. The full description of the hovered (else selected, else first) card sits in one details box
    /// under the grid, sized for the longest one so nothing jumps while the mouse moves.</para>
    /// <para>Label sticker: click the card -> the panel highlights floors where CanBuyAndApply == Applied -> click a floor
    /// -> TryBuyAndApply. Charm: one click -> TryBuy (no floor). REROLL redraws the offers (IShopService.Reroll; cost grows,
    /// limited per visit; greyed with the reason when out of rerolls or tips). The TOMORROW strip lists what is bought for
    /// the next day: relabelled floors ("4→F") and IShopService.ActiveCharms. NEXT DAY -> IGameFlow.FinishShopping.</para>
    /// </summary>
    sealed class ShopView
    {
        public const float CardW = 600f, Pad = 18f, InnerW = CardW - 2f * Pad, CellGap = 8f, CellH = 80f;
        public const float CellW = (InnerW - CellGap) * 0.5f, CellPad = 8f, HeaderH = 44f, ButtonsH = 52f;
        const float PillH = 20f, DetailPad = 10f;

        sealed class OfferCell
        {
            public UIBuild.ButtonParts Parts;
            public TextMeshProUGUI Headline, Price, Sub, KindText, DayText;
            public Image KindPill, DayPill;
            public RectTransform Focus;
            public StickerId Id;
            public bool Charm;
            public int Index;
        }

        readonly UIContext ctx;
        readonly ElevatorPanelView panel;
        readonly RectTransform card, grid, details;
        readonly TextMeshProUGUI title, tips, status, detailEffect, detailDesc, tomorrow;
        readonly LayoutElement detailsLayout;
        readonly UIBuild.ButtonParts done, reroll;
        readonly TextMeshProUGUI rerollSub;
        readonly InlineIcon resultIcon;
        readonly List<OfferCell> cells = new List<OfferCell>();
        readonly StringBuilder sb = new StringBuilder(128);
        bool open;
        StickerId selected;
        int hovered = -1, shownCount;
        bool hasResult;
        long signature = long.MinValue;

        public ShopView(UIContext ctx, RectTransform canvas, ElevatorPanelView panel)
        {
            this.ctx = ctx;
            this.panel = panel;
            panel.FloorClickInterceptor = OnFloorClicked;

            card = UIBuild.Box("ShopCard", canvas, UIStyle.CardEdge, raycast: true).rectTransform;
            UIBuild.Place(card, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(CardW, 300f));
            var fill = UIBuild.Box("Fill", card, UIStyle.Card);
            UIBuild.Stretch(fill.rectTransform, 3f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset((int)Pad, (int)Pad, 14, 16);
            v.spacing = 8f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // header: title left, tips right
            var header = UIBuild.Rect("Header", card);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = HeaderH;
            title = UIBuild.Text("Title", header, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(title.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(InnerW * 0.62f, HeaderH));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            tips = UIBuild.Text("Tips", header, "", 24f, UIStyle.Good, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(tips.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(InnerW * 0.38f, HeaderH));
            tips.textWrappingMode = TextWrappingModes.NoWrap;

            // the one instruction / the last result (24px, readable)
            status = UIBuild.Text("Status", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center);
            resultIcon = new InlineIcon(status);

            grid = UIBuild.Rect("Offers", card);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(CellW, CellH);
            g.spacing = new Vector2(CellGap, CellGap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            g.childAlignment = TextAnchor.UpperLeft;

            // details of the focused card
            var dEdge = UIBuild.Framed("Details", card, new Color(0.03f, 0.035f, 0.055f, 0.96f), new Color(UIStyle.CardEdge.r, UIStyle.CardEdge.g, UIStyle.CardEdge.b, 0.45f), 2f);
            details = dEdge.rectTransform;
            detailsLayout = details.gameObject.AddComponent<LayoutElement>();
            detailEffect = UIBuild.Text("Effect", details, "", 24f, UIStyle.Lucky, TextAlignmentOptions.TopLeft, bold: true);
            detailDesc = UIBuild.Text("Description", details, "", 24f, Color.white, TextAlignmentOptions.TopLeft);

            tomorrow = UIBuild.Text("Tomorrow", card, "", 24f, UIStyle.Ink, TextAlignmentOptions.Left);

            // buttons: REROLL | NEXT DAY
            var row = UIBuild.Rect("Buttons", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = ButtonsH;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = CellGap;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            reroll = UIBuild.Button("Reroll", row, "", 24f, UIStyle.Btn, OnReroll);
            reroll.Label.color = UIStyle.Ink;
            reroll.Label.textWrappingMode = TextWrappingModes.NoWrap;
            reroll.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            rerollSub = UIBuild.Text("Reason", reroll.Rect, "", 12f, UIStyle.Unlucky, TextAlignmentOptions.Bottom);
            UIBuild.Stretch(rerollSub.rectTransform, 3f);
            rerollSub.textWrappingMode = TextWrappingModes.NoWrap;
            done = UIBuild.Button("Done", row, "", 24f, UIStyle.Action,
                () => ctx.Sound(ctx.Flow != null && ctx.Flow.FinishShopping() ? UiSoundKind.Confirm : UiSoundKind.Error));
            done.Label.color = UIStyle.Ink;
            done.Label.textWrappingMode = TextWrappingModes.NoWrap;
            done.Rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            card.gameObject.SetActive(false);
        }

        public bool IsOpen => open;
        public int OfferCount => shownCount;
        public string ResultText => hasResult ? status.text : "";

        // ------------------------------------------------------------------ cells

        OfferCell MakeCell(int index)
        {
            var c = new OfferCell { Index = index };
            c.Parts = UIBuild.Button("Offer" + index, grid, "", 12f, UIStyle.Btn, null);
            c.Parts.Label.gameObject.SetActive(false);
            var r = c.Parts.Rect;
            c.Headline = UIBuild.Text("Headline", r, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Left, bold: true);
            UIBuild.Place(c.Headline.rectTransform, new Vector2(0f, 1f), new Vector2(CellPad, -4f), new Vector2(CellW - 2f * CellPad, 44f));
            c.Headline.textWrappingMode = TextWrappingModes.NoWrap;
            c.Price = UIBuild.Text("Price", r, "", 24f, UIStyle.Good, TextAlignmentOptions.Right, bold: true);
            UIBuild.Place(c.Price.rectTransform, new Vector2(1f, 1f), new Vector2(-CellPad, -6f), new Vector2(60f, 36f));
            c.Price.textWrappingMode = TextWrappingModes.NoWrap;
            c.KindPill = Pill("Kind", r, out c.KindText);
            c.DayPill = Pill("Day", r, out c.DayText);
            c.Sub = UIBuild.Text("Sub", r, "", 12f, UIStyle.Ink, TextAlignmentOptions.BottomRight);
            UIBuild.Place(c.Sub.rectTransform, new Vector2(1f, 0f), new Vector2(-CellPad, 6f), new Vector2(110f, PillH));
            c.Sub.textWrappingMode = TextWrappingModes.NoWrap;
            c.Focus = UIBuild.Outline("Focus", r, UIStyle.Ink, 2f, 0f);
            c.Focus.gameObject.SetActive(false);
            var hover = r.gameObject.AddComponent<HoverRelay>();
            var self = c;
            hover.Enter = () => SetHovered(self.Index);
            hover.Exit = () => { if (hovered == self.Index) SetHovered(-1); };
            c.Parts.Button.onClick.AddListener(() => OnCellClicked(self));
            return c;
        }

        static Image Pill(string name, RectTransform parent, out TextMeshProUGUI text)
        {
            var img = UIBuild.Box(name, parent, UIStyle.Btn);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(40f, PillH);
            text = UIBuild.Text("Text", rt, "", 12f, UIStyle.Dark, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return img;
        }

        static float PillWidth(TextMeshProUGUI t)
        {
            if (t.font != null) t.font.TryAddCharacters(t.text);
            return Mathf.Ceil(t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity).x) + 12f;
        }

        void OnCellClicked(OfferCell c)
        {
            if (!open || ctx.Shop == null) return;
            hovered = c.Index; // a click focuses that card's details too (touch / no hover)
            if (c.Charm) { BuyCharm(c); return; }
            selected = selected == c.Id ? default : c.Id;
            ctx.Sound(selected.IsEmpty ? UiSoundKind.Click : UiSoundKind.Confirm); // picked = confirm, un-picked = click
            panel.SelectedSticker = selected;
            if (selected.IsEmpty) ClearResult();
            else SetResult(ctx.S(UIStrings.ShopPickFloor), UIStyle.Ink);
            Refresh();
        }

        void BuyCharm(OfferCell c)
        {
            if (!selected.IsEmpty) { selected = default; panel.SelectedSticker = selected; }
            var def = OfferSticker(c.Id);
            var r = ctx.Shop.TryBuy(c.Id);
            ctx.Sound(r == StickerApplyResult.Applied ? UiSoundKind.Confirm : UiSoundKind.Error);
            SetResult(UIFormat.CharmResult(r, CharmTitle(def, false), ctx.Korean), r == StickerApplyResult.Applied ? UIStyle.Lucky : UIStyle.Unlucky);
            Refresh();
        }

        void OnReroll()
        {
            if (!open || ctx.Shop == null) { ctx.Sound(UiSoundKind.Error); return; }
            bool ok = ctx.Shop.Reroll();
            ctx.Sound(ok ? UiSoundKind.Confirm : UiSoundKind.Error);
            if (ok)
            {
                selected = default;
                panel.SelectedSticker = selected;
                hovered = -1;
                SetResult(ctx.S(UIStrings.RerollDone), UIStyle.Lucky);
            }
            Refresh();
        }

        /// <summary>ShopRerolled (from any source, e.g. a keyboard shortcut): the offers were replaced.</summary>
        public void OnRerolled()
        {
            if (!open) return;
            if (!selected.IsEmpty && !InStock(selected)) { selected = default; panel.SelectedSticker = selected; }
            Refresh();
        }

        void SetHovered(int index)
        {
            if (hovered == index) return;
            hovered = index;
            if (open) RefreshDetails();
        }

        void SetResult(string text, Color color, Sprite icon = null, Color iconTint = default)
        {
            hasResult = true;
            status.text = text;
            status.color = color;
            resultIcon.Set(icon, iconTint);
        }

        void ClearResult()
        {
            hasResult = false;
            status.text = ctx.S(UIStrings.ShopHintCharms);
            status.color = UIStyle.Ink;
            resultIcon.Set(null, default);
        }

        bool OnFloorClicked(FloorIndex floor)
        {
            if (ctx.Day == null || ctx.Day.Phase != DayPhase.Shop || ctx.Shop == null) return false;
            if (selected.IsEmpty)
            {
                SetResult(ctx.S(UIStrings.ShopHint), UIStyle.Ink);
                ctx.Sound(UiSoundKind.Error); // no sticker picked yet
                return true;
            }
            string before = ctx.Building.GetLabel(floor).Text;
            var r = ctx.Shop.TryBuyAndApply(selected, floor);
            ctx.Sound(r == StickerApplyResult.Applied ? UiSoundKind.Confirm : UiSoundKind.Error);
            string after = ctx.Building.GetLabel(floor).Text;
            // the venue keeps its icon whatever the label says (GDD 4): "Floor 4 ([icon] Karaoke) is now F!"
            string venue = InlineIcon.Markup(ctx, ctx.Building.GetDestination(floor), out var icon, out var tint);
            SetResult(UIFormat.ApplyResult(r, before, after, ctx.Korean, venue), r == StickerApplyResult.Applied ? UIStyle.Lucky : UIStyle.Unlucky,
                r == StickerApplyResult.Applied ? icon : null, tint);
            if (r == StickerApplyResult.Applied)
            {
                // keep the selection only while that sticker is still for sale
                if (!InStock(selected)) { selected = default; panel.SelectedSticker = selected; }
                Refresh();
            }
            return true;
        }

        bool InStock(StickerId id)
        {
            var list = ctx.Shop.Offers;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i].Remaining != 0;
            return false;
        }

        StickerDef OfferSticker(StickerId id)
        {
            var list = ctx.Shop != null ? ctx.Shop.Offers : null;
            if (list != null) for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i].Sticker;
            return ctx.Data != null && ctx.Data.TryGetSticker(id, out var s) ? s : null;
        }

        /// <summary>Charm name (with its coloured glyph when <paramref name="glyph"/>).</summary>
        string CharmTitle(StickerDef s, bool glyph)
        {
            string name = ctx.StickerName(s);
            if (string.IsNullOrEmpty(name)) name = s != null ? s.id : "?";
            return glyph && s != null ? "<color=" + UIFormat.CharmHex(s.effect) + ">" + UIFormat.CharmGlyph(s.effect) + "</color> " + name : name;
        }

        void Open()
        {
            open = true;
            selected = default;
            hovered = -1;
            panel.SelectedSticker = selected;
            card.gameObject.SetActive(true);
            ClearResult();
            RefreshLanguage();
        }

        void Close()
        {
            open = false;
            selected = default;
            hovered = -1;
            panel.SelectedSticker = selected;
            card.gameObject.SetActive(false);
        }

        public void RefreshLanguage()
        {
            title.text = ctx.S(UIStrings.ShopTitle);
            UIBuild.Fit(title, 36f);
            done.Label.text = ctx.S(UIStrings.NextDay);
            if (!hasResult) status.text = ctx.S(UIStrings.ShopHintCharms);
            Refresh();
        }

        // ------------------------------------------------------------------ refresh

        int Tips => ctx.Score != null ? ctx.Score.Tips : 0;

        /// <summary>Cheap fingerprint of everything the card shows (services may update in place).</summary>
        long Signature()
        {
            if (ctx.Shop == null) return 0;
            unchecked
            {
                long s = 17;
                s = s * 31 + Tips;
                var list = ctx.Shop.Offers;
                s = s * 31 + (list != null ? list.Count : -1);
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        s = s * 31 + list[i].Remaining * 7 + list[i].Price + (list[i].Id.Value != null ? list[i].Id.Value.GetHashCode() : 0);
                s = s * 31 + ctx.Shop.RerollsLeft;
                s = s * 31 + ctx.Shop.RerollCost;
                var charms = ctx.Shop.ActiveCharms;
                s = s * 31 + (charms != null ? charms.Count : 0);
                if (ctx.Building != null)
                    for (int f = 0; f < ctx.Building.FloorCount; f++)
                        s = s * 31 + (ctx.Building.GetLabel(new FloorIndex(f)).Text ?? "").GetHashCode();
                s = s * 31 + (selected.IsEmpty ? 0 : selected.Value.GetHashCode());
                s = s * 31 + (ctx.Korean ? 1 : 0);
                return s;
            }
        }

        void Refresh()
        {
            if (ctx.Shop == null || !open) return;
            RefreshCells();
            RefreshDetails();
            RefreshTomorrow();
            RefreshReroll();
            tips.text = ctx.S(UIStrings.Tips) + "  $" + Tips.ToString(CultureInfo.InvariantCulture);
            signature = Signature();
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        void RefreshCells()
        {
            var list = ctx.Shop.Offers;
            int n = list != null ? list.Count : 0;
            while (cells.Count < n) cells.Add(MakeCell(cells.Count));
            shownCount = n;
            int tipsNow = Tips;
            bool ko = ctx.Korean;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool active = i < n;
                if (c.Parts.Rect.gameObject.activeSelf != active) c.Parts.Rect.gameObject.SetActive(active);
                if (!active) continue;
                var offer = list[i];
                var def = offer.Sticker;
                c.Id = offer.Id;
                c.Charm = def != null && def.IsCharm;
                bool soldOut = offer.Remaining == 0;
                bool owned = c.Charm && ctx.Shop.CanBuy(c.Id) == StickerApplyResult.AlreadyActive;
                bool broke = !soldOut && !owned && tipsNow < offer.Price;
                bool isSel = !c.Charm && !selected.IsEmpty && selected == offer.Id;

                // price (natural width) + headline (the rest, 36px stepping down the grid when long)
                c.Price.text = "$" + offer.Price.ToString(CultureInfo.InvariantCulture);
                if (c.Price.font != null) c.Price.font.TryAddCharacters(c.Price.text);
                float priceW = Mathf.Ceil(c.Price.GetPreferredValues(c.Price.text, float.PositiveInfinity, float.PositiveInfinity).x) + 2f;
                c.Price.rectTransform.sizeDelta = new Vector2(priceW, 36f);
                c.Headline.text = c.Charm ? CharmTitle(def, true) : UIFormat.StickerHeadline(def, ko);
                if (c.Headline.font != null) c.Headline.font.TryAddCharacters(c.Headline.text);
                c.Headline.rectTransform.sizeDelta = new Vector2(Mathf.Max(60f, CellW - 2f * CellPad - priceW - 8f), 44f);
                UIBuild.Fit(c.Headline, 36f);

                // badges: kind + 1 DAY
                c.KindText.text = ctx.S(c.Charm ? UIStrings.KindCharm : UIStrings.KindLabel);
                c.DayText.text = ctx.S(UIStrings.OneDay);
                float kw = PillWidth(c.KindText), dw = PillWidth(c.DayText);
                c.KindPill.rectTransform.anchoredPosition = new Vector2(CellPad, 7f);
                c.KindPill.rectTransform.sizeDelta = new Vector2(kw, PillH);
                c.DayPill.rectTransform.anchoredPosition = new Vector2(CellPad + kw + 4f, 7f);
                c.DayPill.rectTransform.sizeDelta = new Vector2(dw, PillH);
                c.KindPill.color = c.Charm ? CharmPill : LabelPill;
                c.DayPill.color = DayPill;
                c.KindText.color = c.DayText.color = UIStyle.Dark;

                // stock / need / bought (right of the badges)
                string sub;
                Color subColor;
                if (owned) { sub = ctx.S(UIStrings.Owned); subColor = UIStyle.Good; }
                else if (soldOut) { sub = ctx.S(UIStrings.SoldOut); subColor = UIStyle.Unlucky; }
                else if (broke) { sub = string.Format(ctx.S(UIStrings.NeedMore), (offer.Price - tipsNow).ToString(CultureInfo.InvariantCulture)); subColor = UIStyle.Unlucky; }
                else if (offer.Remaining > 0) { sub = string.Format(ctx.S(UIStrings.StockLeft), offer.Remaining.ToString(CultureInfo.InvariantCulture)); subColor = UIStyle.InkDim; }
                else { sub = ""; subColor = UIStyle.InkDim; }
                c.Sub.text = sub;
                float subX = CellPad + kw + 4f + dw + 6f;
                c.Sub.rectTransform.sizeDelta = new Vector2(Mathf.Max(20f, CellW - subX - CellPad), PillH);
                UIBuild.Fit(c.Sub, 12f);

                var bg = isSel ? UIStyle.BtnTarget : soldOut || owned ? UIStyle.BtnBlocked : UIStyle.Btn;
                c.Parts.Background.color = bg;
                c.Price.color = isSel ? UIStyle.Dark : soldOut || broke ? UIStyle.Unlucky : UIStyle.Good;
                c.Sub.color = isSel ? UIStyle.Dark : subColor;
                c.Headline.color = isSel ? UIStyle.Dark : broke || owned || soldOut ? FadedLucky : UIStyle.Lucky;
                // unaffordable: still clickable (labels: to see the floors; charms: says what is missing), never when gone
                c.Parts.Button.interactable = !soldOut && !owned;
            }
        }

        static readonly Color LabelPill = UIStyle.Hex("#C9A15A"), CharmPill = UIStyle.Hex("#7FDBFF"), DayPill = UIStyle.Hex("#F4EEDC");
        static readonly Color FadedLucky = new Color(UIStyle.Lucky.r, UIStyle.Lucky.g, UIStyle.Lucky.b, 0.5f);

        int FocusIndex
        {
            get
            {
                if (hovered >= 0 && hovered < shownCount) return hovered;
                if (!selected.IsEmpty)
                    for (int i = 0; i < shownCount; i++) if (cells[i].Id == selected) return i;
                return shownCount > 0 ? 0 : -1;
            }
        }

        float DetailInnerW => InnerW - 2f * DetailPad - 4f;

        /// <summary>Effect line (charms) + description for one offer; height = what it needs at the details width.</summary>
        float MeasureDetail(StickerDef s, out string effect, out string desc)
        {
            effect = UIFormat.CharmEffect(s, ctx.Korean);
            desc = ctx.StickerDescription(s);
            float w = DetailInnerW, h = 0f;
            if (!string.IsNullOrEmpty(effect)) h += Need(detailEffect, effect, w) + 4f;
            if (!string.IsNullOrEmpty(desc)) h += Need(detailDesc, desc, w);
            return h;
        }

        static float Need(TextMeshProUGUI t, string text, float width)
        {
            if (t.font != null) t.font.TryAddCharacters(text);
            return Mathf.Ceil(t.GetPreferredValues(text, width, float.PositiveInfinity).y) + 2f;
        }

        void RefreshDetails()
        {
            var list = ctx.Shop != null ? ctx.Shop.Offers : null;
            float maxH = 0f;
            for (int i = 0; i < shownCount; i++) maxH = Mathf.Max(maxH, MeasureDetail(list[i].Sticker, out _, out _));
            int focus = FocusIndex;
            for (int i = 0; i < shownCount; i++)
            {
                bool f = i == focus;
                if (cells[i].Focus.gameObject.activeSelf != f) cells[i].Focus.gameObject.SetActive(f);
            }
            details.gameObject.SetActive(shownCount > 0);
            if (focus < 0) return;
            var def = list[focus].Sticker;
            MeasureDetail(def, out string effect, out string desc);
            float w = DetailInnerW, y = DetailPad;
            detailEffect.gameObject.SetActive(!string.IsNullOrEmpty(effect));
            if (!string.IsNullOrEmpty(effect))
            {
                detailEffect.text = effect;
                detailEffect.color = UIStyle.Hex(UIFormat.CharmHex(def.effect));
                float eh = Need(detailEffect, effect, w);
                Place(detailEffect.rectTransform, y, w, eh);
                y += eh + 4f;
            }
            detailDesc.text = desc;
            float dh = string.IsNullOrEmpty(desc) ? 0f : Need(detailDesc, desc, w);
            Place(detailDesc.rectTransform, y, w, dh);
            detailsLayout.preferredHeight = maxH + 2f * DetailPad;
        }

        static void Place(RectTransform rt, float top, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(DetailPad + 2f, -top);
            rt.sizeDelta = new Vector2(w, h);
        }

        void RefreshTomorrow()
        {
            sb.Clear();
            sb.Append("<color=").Append(UIFormat.LuckyHex).Append('>').Append(ctx.S(UIStrings.Tomorrow)).Append("</color>  ");
            int items = 0;
            if (ctx.Building != null)
                for (int f = 0; f < ctx.Building.FloorCount; f++)
                {
                    var floor = new FloorIndex(f);
                    string orig = ElevatorPanelView.OriginalIfRelabelled(ctx.Building, floor);
                    if (orig == null) continue;
                    if (items++ > 0) sb.Append("   ");
                    sb.Append("<color=#F4EEDC>").Append(orig).Append("→</color><color=").Append(UIFormat.LuckyHex).Append('>')
                      .Append(ctx.Building.GetLabel(floor).Text).Append("</color>");
                }
            var charms = ctx.Shop.ActiveCharms;
            if (charms != null)
                for (int i = 0; i < charms.Count; i++)
                {
                    if (charms[i] == null) continue;
                    if (items++ > 0) sb.Append("   ");
                    sb.Append(CharmTitle(charms[i], true));
                }
            if (items == 0) sb.Append("<color=#F4EEDC8C>").Append(ctx.S(UIStrings.TomorrowNone)).Append("</color>");
            tomorrow.text = sb.ToString();
            if (tomorrow.font != null) tomorrow.font.TryAddCharacters(tomorrow.text);
        }

        void RefreshReroll()
        {
            int left = ctx.Shop.RerollsLeft, cost = ctx.Shop.RerollCost;
            bool none = left <= 0, broke = !none && Tips < cost;
            reroll.Label.text = UIFormat.RerollLabel(cost, left, ctx.Korean);
            reroll.Label.rectTransform.offsetMin = new Vector2(4f, broke ? 14f : 4f); // room for the reason line
            UIBuild.Fit(reroll.Label, 24f);
            rerollSub.text = broke ? string.Format(ctx.S(UIStrings.RerollNeed), (cost - Tips).ToString(CultureInfo.InvariantCulture)) : "";
            rerollSub.gameObject.SetActive(broke);
            bool can = !none && !broke;
            reroll.Button.interactable = can;
            reroll.Background.color = can ? UIStyle.Btn : UIStyle.BtnBlocked;
            reroll.Label.color = can ? UIStyle.Ink : UIStyle.InkDim;
        }

        public void Tick()
        {
            bool shouldOpen = ctx.HasGame && ctx.Day.Phase == DayPhase.Shop;
            if (shouldOpen != open) { if (shouldOpen) Open(); else Close(); }
            if (!open) return;
            resultIcon.Place();
            if (Signature() != signature) Refresh();
        }

        // ------------------------------------------------------------------ test hooks

        public float DescriptionFontSize(int i) => i >= 0 && i < shownCount ? detailDesc.fontSize : 0f;

        /// <summary>WCAG contrast ratio of the description ink on the details box (the card shows offer <paramref name="i"/>).</summary>
        public float DescriptionContrast(int i)
        {
            if (i < 0 || i >= shownCount) return 0f;
            FocusOffer(i);
            return UIStyle.ContrastRatio(detailDesc.color, details.GetChild(0).GetComponent<Image>().color);
        }

        /// <summary>Simulates the pointer over card <paramref name="i"/> (-1 = none).</summary>
        public void FocusOffer(int i) => SetHovered(i);
        public string DetailText => detailEffect.gameObject.activeSelf ? detailEffect.text + "\n" + detailDesc.text : detailDesc.text;
        public string TomorrowText => tomorrow.text;
        public string RerollText => reroll.Label.text;
        public string RerollReason => rerollSub.gameObject.activeSelf ? rerollSub.text : "";
        public bool RerollInteractable => reroll.Button.interactable;
        public void ClickReroll() { if (reroll.Button.interactable) reroll.Button.onClick.Invoke(); }
        public string OfferHeadline(int i) => i >= 0 && i < shownCount ? cells[i].Headline.text : null;
        public string OfferKind(int i) => i >= 0 && i < shownCount ? cells[i].KindText.text : null;
        public string OfferDayTag(int i) => i >= 0 && i < shownCount ? cells[i].DayText.text : null;
        public string OfferSub(int i) => i >= 0 && i < shownCount ? cells[i].Sub.text : null;
        public bool OfferInteractable(int i) => i >= 0 && i < shownCount && cells[i].Parts.Button.interactable;
        public float OfferHeadlineSize(int i) => i >= 0 && i < shownCount ? cells[i].Headline.fontSize : 0f;
        public int OfferIndex(string id)
        {
            for (int i = 0; i < shownCount; i++) if (cells[i].Id.Value == id) return i;
            return -1;
        }
        public Rect CardRect(RectTransform canvas) => UIBuild.CanvasRect(canvas, card);

        /// <summary>
        /// null when the card stays on the canvas and every text fits its box for EVERY offer in focus (headline vs price,
        /// badges + stock inside the cell, details, Tomorrow strip, buttons), else why not.
        /// </summary>
        public string FirstDescriptionProblem(RectTransform canvas)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            var c = canvas.rect;
            var cr = UIBuild.CanvasRect(canvas, card);
            if (cr.yMin < 4f || cr.yMax > c.height - 4f || cr.xMin < -0.5f || cr.xMax > c.width + 0.5f) return "shop card off screen " + cr + " in " + c.size;
            var pr0 = UIBuild.CanvasRect(canvas, panel.Root);
            if (cr.xMin < pr0.xMax + 4f) return "shop card covers the elevator panel " + cr + " vs " + pr0;
            string p = Overflow(title, false) ?? Overflow(tips, false) ?? Overflow(status, true) ?? Overflow(tomorrow, true)
                       ?? Overflow(reroll.Label, false) ?? Overflow(done.Label, false);
            if (p != null) return p;
            int keep = hovered;
            for (int i = 0; i < shownCount; i++)
            {
                SetHovered(i);
                var o = cells[i];
                var orc = UIBuild.CanvasRect(canvas, o.Parts.Rect);
                var hr = UIBuild.CanvasRect(canvas, o.Headline.rectTransform);
                var prr = UIBuild.CanvasRect(canvas, o.Price.rectTransform);
                if (hr.xMax > prr.xMin + 0.5f) return "offer " + i + " headline box runs into the price " + hr + " vs " + prr;
                p = Overflow(o.Headline, false) ?? Overflow(o.Price, false) ?? Overflow(o.Sub, false) ?? Overflow(o.KindText, false) ?? Overflow(o.DayText, false);
                if (p != null) return "offer " + i + ": " + p;
                foreach (var part in new[] { o.Headline.rectTransform, o.Price.rectTransform, o.Sub.rectTransform, o.KindPill.rectTransform, o.DayPill.rectTransform })
                {
                    var r = UIBuild.CanvasRect(canvas, part);
                    if (r.xMin < orc.xMin - 0.5f || r.xMax > orc.xMax + 0.5f || r.yMin < orc.yMin - 0.5f || r.yMax > orc.yMax + 0.5f)
                        return "offer " + i + " " + part.name + " spills out of its card " + r + " vs " + orc;
                }
                var sr = UIBuild.CanvasRect(canvas, o.Sub.rectTransform);
                var dr0 = UIBuild.CanvasRect(canvas, o.DayPill.rectTransform);
                if (o.Sub.text.Length > 0 && sr.xMin < dr0.xMax - 0.5f) return "offer " + i + " stock line runs into the 1 DAY tag";
                p = Overflow(detailDesc, true) ?? (detailEffect.gameObject.activeSelf ? Overflow(detailEffect, true) : null);
                if (p != null) return "offer " + i + " details: " + p;
                var dbox = UIBuild.CanvasRect(canvas, details);
                var dd = UIBuild.CanvasRect(canvas, detailDesc.rectTransform);
                if (dd.yMin < dbox.yMin - 0.5f) return "offer " + i + " description spills out of the details box " + dd + " vs " + dbox;
            }
            SetHovered(keep);
            return null;
        }

        static string Overflow(TextMeshProUGUI t, bool wraps)
        {
            if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) return null;
            var r = t.rectTransform.rect;
            var need = wraps ? t.GetPreferredValues(t.text, r.width, float.PositiveInfinity) : t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity);
            if (!wraps && need.x > r.width + 0.5f) return "'" + t.text + "' needs width " + need.x + " > " + r.width;
            if (need.y > r.height + 0.5f) return "'" + t.text + "' needs height " + need.y + " > " + r.height;
            return null;
        }

        public void ClickOffer(int index) { if (index >= 0 && index < shownCount) cells[index].Parts.Button.onClick.Invoke(); }
        public bool ResultIconVisible => resultIcon.Visible;
        public void ClickDone() => done.Button.onClick.Invoke();
    }
}
