using System.Collections.Generic;
using System.Globalization;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// GDD 5 sticker shop (Shop phase). Side card on the right so the building and the elevator panel stay visible:
    /// pick a sticker (IShopService.Offers) -> the panel highlights floors where CanBuyAndApply == Applied ->
    /// click a floor button -> TryBuyAndApply -> result line. NEXT DAY -> IGameFlow.FinishShopping.
    /// </summary>
    sealed class ShopView
    {
        const float CardW = 400f, OfferH = 124f;

        sealed class OfferButton
        {
            public UIBuild.ButtonParts Parts;
            public TextMeshProUGUI Headline, Name, Description, Price, Need;
            public StickerId Id;
        }

        readonly UIContext ctx;
        readonly ElevatorPanelView panel;
        readonly RectTransform card, offersRoot;
        readonly TextMeshProUGUI title, tips, hint, result;
        readonly UIBuild.ButtonParts done;
        readonly InlineIcon resultIcon;
        readonly List<OfferButton> offers = new List<OfferButton>();
        bool open;
        StickerId selected;
        int shownTips = int.MinValue;

        public ShopView(UIContext ctx, RectTransform canvas, ElevatorPanelView panel)
        {
            this.ctx = ctx;
            this.panel = panel;
            panel.FloorClickInterceptor = OnFloorClicked;

            card = UIBuild.Box("ShopCard", canvas, UIStyle.CardEdge, raycast: true).rectTransform;
            UIBuild.Place(card, new Vector2(1f, 0.5f), new Vector2(-24f, -10f), new Vector2(CardW, 300f));
            var fill = UIBuild.Box("Fill", card, UIStyle.Card);
            UIBuild.Stretch(fill.rectTransform, 3f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(20, 20, 18, 18);
            v.spacing = 10f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = UIBuild.Text("Title", card, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            tips = UIBuild.Text("Tips", card, "", 24f, UIStyle.Good, TextAlignmentOptions.Center, bold: true);
            hint = UIBuild.Text("Hint", card, "", 12f, UIStyle.Ink, TextAlignmentOptions.Center);
            offersRoot = UIBuild.Rect("Offers", card);
            var ov = offersRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            ov.spacing = 8f;
            ov.childControlWidth = ov.childControlHeight = true;
            ov.childForceExpandWidth = true;
            ov.childForceExpandHeight = false;
            result = UIBuild.Text("Result", card, "", 24f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            resultIcon = new InlineIcon(result);
            done = UIBuild.Button("Done", card, "", 24f, UIStyle.Action, () => ctx.Flow?.FinishShopping());
            done.Label.color = UIStyle.Ink;
            done.Rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            card.gameObject.SetActive(false);
        }

        public bool IsOpen => open;
        public int OfferCount => offers.Count;
        public string ResultText => result.text;

        OfferButton MakeOffer()
        {
            var o = new OfferButton();
            o.Parts = UIBuild.Button("Offer", offersRoot, "", 12f, UIStyle.Btn, null);
            o.Parts.Label.gameObject.SetActive(false);
            o.Parts.Rect.gameObject.AddComponent<LayoutElement>().preferredHeight = OfferH;
            o.Headline = UIBuild.Text("Headline", o.Parts.Rect, "", 36f, UIStyle.Lucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(o.Headline.rectTransform, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(110f, OfferH));
            o.Headline.textWrappingMode = TextWrappingModes.NoWrap;
            o.Need = UIBuild.Text("Need", o.Parts.Rect, "", 12f, UIStyle.Unlucky, TextAlignmentOptions.Center);
            UIBuild.Place(o.Need.rectTransform, new Vector2(0f, 0f), new Vector2(8f, 6f), new Vector2(110f, 16f));
            o.Need.textWrappingMode = TextWrappingModes.NoWrap;
            o.Name = UIBuild.Text("Name", o.Parts.Rect, "", 24f, UIStyle.Ink, TextAlignmentOptions.TopLeft, bold: true);
            UIBuild.Place(o.Name.rectTransform, new Vector2(0f, 1f), new Vector2(124f, -6f), new Vector2(CardW - 40f - 130f, 30f));
            o.Description = UIBuild.Text("Description", o.Parts.Rect, "", 12f, UIStyle.InkDim, TextAlignmentOptions.TopLeft);
            UIBuild.Place(o.Description.rectTransform, new Vector2(0f, 1f), new Vector2(124f, -38f), new Vector2(CardW - 40f - 130f, 34f));
            o.Price = UIBuild.Text("Price", o.Parts.Rect, "", 24f, UIStyle.Good, TextAlignmentOptions.BottomLeft, bold: true);
            UIBuild.Place(o.Price.rectTransform, new Vector2(0f, 0f), new Vector2(124f, 4f), new Vector2(CardW - 40f - 130f, 30f));
            var self = o;
            o.Parts.Button.onClick.AddListener(() => Select(self.Id));
            return o;
        }

        void Select(StickerId id)
        {
            if (!open) return;
            selected = selected == id ? default : id;
            panel.SelectedSticker = selected;
            SetResult(selected.IsEmpty ? "" : ctx.S(UIStrings.ShopPickFloor), UIStyle.Ink);
        }

        void SetResult(string text, Color color, Sprite icon = null, Color iconTint = default)
        {
            result.text = text;
            result.color = color;
            resultIcon.Set(icon, iconTint);
        }

        bool OnFloorClicked(FloorIndex floor)
        {
            if (ctx.Day == null || ctx.Day.Phase != DayPhase.Shop || ctx.Shop == null) return false;
            if (selected.IsEmpty)
            {
                SetResult(ctx.S(UIStrings.ShopHint), UIStyle.Ink);
                return true;
            }
            string before = ctx.Building.GetLabel(floor).Text;
            var r = ctx.Shop.TryBuyAndApply(selected, floor);
            string after = ctx.Building.GetLabel(floor).Text;
            // the venue keeps its icon whatever the label says (GDD 4): "Floor 4 ([icon] Karaoke) is now F!"
            string venue = InlineIcon.Markup(ctx, ctx.Building.GetDestination(floor), out var icon, out var tint);
            SetResult(UIFormat.ApplyResult(r, before, after, ctx.Korean, venue), r == StickerApplyResult.Applied ? UIStyle.Lucky : UIStyle.Unlucky,
                r == StickerApplyResult.Applied ? icon : null, tint);
            if (r == StickerApplyResult.Applied)
            {
                // keep the selection only while that sticker is still for sale
                if (!InStock(selected)) { selected = default; panel.SelectedSticker = selected; }
                RefreshOffers();
            }
            return true;
        }

        bool InStock(StickerId id)
        {
            var list = ctx.Shop.Offers;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i].Remaining != 0;
            return false;
        }

        void Open()
        {
            open = true;
            selected = default;
            panel.SelectedSticker = selected;
            SetResult("", UIStyle.Ink);
            card.gameObject.SetActive(true);
            RefreshLanguage();
        }

        void Close()
        {
            open = false;
            selected = default;
            panel.SelectedSticker = selected;
            card.gameObject.SetActive(false);
        }

        public void RefreshLanguage()
        {
            title.text = ctx.S(UIStrings.ShopTitle);
            hint.text = ctx.S(UIStrings.ShopHint);
            done.Label.text = ctx.S(UIStrings.NextDay);
            shownTips = int.MinValue;
            RefreshOffers();
        }

        void RefreshOffers()
        {
            if (ctx.Shop == null) return;
            var list = ctx.Shop.Offers;
            while (offers.Count < list.Count) offers.Add(MakeOffer());
            int tipsNow = ctx.Score != null ? ctx.Score.Tips : 0;
            for (int i = 0; i < offers.Count; i++)
            {
                var o = offers[i];
                bool active = i < list.Count;
                o.Parts.Rect.gameObject.SetActive(active);
                if (!active) continue;
                var offer = list[i];
                o.Id = offer.Id;
                string headline = UIFormat.StickerHeadline(offer.Sticker, ctx.Korean);
                if (!string.Equals(o.Headline.text, headline, System.StringComparison.Ordinal)) { o.Headline.text = headline; UIBuild.Fit(o.Headline, 36f); }
                string name = ctx.StickerName(offer.Sticker);
                // the headline already says "4 -> F"; only repeat the name when it adds something
                o.Name.text = name.Contains("→") || string.Equals(name.Replace(" ", ""), o.Headline.text.Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase) ? "" : name;
                o.Description.text = ctx.StickerDescription(offer.Sticker);
                bool soldOut = offer.Remaining == 0;
                o.Price.text = soldOut ? ctx.S(UIStrings.SoldOut)
                    : offer.Remaining < 0 ? string.Format(ctx.S(UIStrings.PriceUnlimited), offer.Price.ToString(CultureInfo.InvariantCulture))
                    : string.Format(ctx.S(UIStrings.PriceStock), offer.Price.ToString(CultureInfo.InvariantCulture), offer.Remaining.ToString(CultureInfo.InvariantCulture));
                bool isSel = !selected.IsEmpty && selected == offer.Id;
                bool broke = !soldOut && tipsNow < offer.Price;
                o.Price.color = isSel ? UIStyle.Dark : soldOut || broke ? UIStyle.Unlucky : UIStyle.Good;
                // unaffordable: still selectable (to read it / see the floors), but clearly not buyable
                o.Need.text = broke ? string.Format(ctx.S(UIStrings.NeedMore), (offer.Price - tipsNow).ToString(CultureInfo.InvariantCulture)) : "";
                o.Need.color = isSel ? UIStyle.Dark : UIStyle.Unlucky;
                o.Parts.Background.color = isSel ? UIStyle.BtnTarget : soldOut ? UIStyle.BtnBlocked : UIStyle.Btn;
                o.Name.color = isSel ? UIStyle.Dark : UIStyle.Ink;
                o.Description.color = isSel ? new Color(0.06f, 0.06f, 0.09f, 0.8f) : UIStyle.InkDim;
                o.Headline.color = isSel ? UIStyle.Dark : broke ? new Color(UIStyle.Lucky.r, UIStyle.Lucky.g, UIStyle.Lucky.b, 0.45f) : UIStyle.Lucky;
                o.Parts.Button.interactable = !soldOut;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        public void Tick()
        {
            bool shouldOpen = ctx.HasGame && ctx.Day.Phase == DayPhase.Shop;
            if (shouldOpen != open) { if (shouldOpen) Open(); else Close(); }
            if (!open) return;
            resultIcon.Place();
            int t = ctx.Score != null ? ctx.Score.Tips : 0;
            if (t != shownTips)
            {
                shownTips = t;
                tips.text = ctx.S(UIStrings.Tips) + "  $" + t.ToString(CultureInfo.InvariantCulture);
                RefreshOffers();
            }
            // selection highlight follows clicks immediately
            for (int i = 0; i < offers.Count && i < ctx.Shop.Offers.Count; i++)
            {
                bool isSel = !selected.IsEmpty && selected == offers[i].Id;
                var want = isSel ? UIStyle.BtnTarget : ctx.Shop.Offers[i].Remaining == 0 ? UIStyle.BtnBlocked : UIStyle.Btn;
                if (offers[i].Parts.Background.color != want) RefreshOffers();
            }
        }

        public void ClickOffer(int index) { if (index >= 0 && index < offers.Count) offers[index].Parts.Button.onClick.Invoke(); }
        public bool ResultIconVisible => resultIcon.Visible;
        public void ClickDone() => done.Button.onClick.Invoke();
    }
}
