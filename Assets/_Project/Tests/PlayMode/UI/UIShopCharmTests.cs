using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>
    /// Shop rework (offer grid, kind + 1-day badges, reroll, charms, Tomorrow strip), charms during the day
    /// (HUD row, CharmTriggered -> BLOCKED! popup), the day-end note, and the panel's struck-through original floors.
    /// Built against the contract with <see cref="FakeShop"/> / <see cref="FakeBuilding"/>.
    /// </summary>
    public class UIShopCharmTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        internal static string Plain(string rich) => Regex.Replace(rich ?? "", "<[^>]*>", "");

        /// <summary>Day 1 -> its summary -> the shop, with <paramref name="tips"/> to spend.</summary>
        internal static IEnumerator ToShop(int tips, bool korean = false)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().UseKorean = korean;
            yield return UITestDriver.ClickStart();
            var s = UITestDriver.Session();
            s.Score.AddTips(tips - s.Score.Tips, earned: true);
            s.EndDayNow();
            yield return null;
            UITestDriver.UI().ClickFlowButton(); // CONTINUE
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Shop, GameServices.Get<IDayQuery>().Phase);
        }

        internal static FakeShop InstallShop(int offers, int rerolls = 3, int cost = 20, params StickerDef[] charms)
        {
            if (charms.Length == 0) charms = new[] { FakeShop.Talisman(), FakeShop.Bell(), FakeShop.Coin(), FakeShop.Cable() };
            var s = UITestDriver.Session();
            var fake = new FakeShop(s, GameServices.Get<IShopService>(), FakeShop.Pool(GameServices.Get<GameData>(), charms), offers, rerolls, cost);
            UITestDriver.UI().OverrideServicesForTests(shopService: fake);
            return fake;
        }

        static int Tips => UITestDriver.Session().Score.Tips;

        // ------------------------------------------------------------------ layout

        [UnityTest]
        public IEnumerator Shop_Layout_Fits_3To5Offers_EnKo()
        {
            yield return ToShop(500);
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            foreach (int n in new[] { 3, 4, 5 })
            {
                InstallShop(n);
                foreach (bool ko in new[] { false, true })
                {
                    settings.UseKorean = ko;
                    yield return UITestDriver.Frames(2);
                    string tag = n + " offers " + (ko ? "ko" : "en");
                    Assert.AreEqual(n, ui.ShopOfferCount, tag);
                    Assert.IsNull(ui.ShopDescriptionProblem(), tag);
                    for (int i = 0; i < n; i++)
                    {
                        Assert.GreaterOrEqual(ui.ShopOfferHeadlineSize(i), 24f, tag + " offer " + i + " headline stays >= 24px: " + ui.ShopOfferHeadline(i));
                        Assert.AreEqual(ko ? "하루" : "1 DAY", ui.ShopOfferDayTag(i), tag + " 1-day tag on every card");
                        ui.FocusShopOffer(i);
                        Assert.GreaterOrEqual(ui.ShopDescriptionFontSize(i), 24f);
                        Assert.GreaterOrEqual(ui.ShopDescriptionContrast(i), 7f);
                    }
                    int charm = ui.ShopOfferIndex("talisman"), label = ui.ShopOfferIndex("four_to_f");
                    Assert.AreEqual(ko ? "부적" : "CHARM", ui.ShopOfferKind(charm));
                    Assert.AreEqual(ko ? "스티커" : "STICKER", ui.ShopOfferKind(label));
                    ui.FocusShopOffer(charm);
                    StringAssert.Contains(ko ? "첫 불길 판정" : "Blocks the first UNLUCKY", ui.ShopDetailText, "charm effect line in the details");
                    ui.FocusShopOffer(-1);
                    var card = ui.ShopCardRect;
                    Assert.Less(card.yMax, ui.CanvasSize.y - 4f, tag + " card top on screen");
                    Assert.Greater(card.yMin, 4f, tag + " card bottom on screen");
                }
            }
            settings.UseKorean = false;
            yield return null;
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "every new text on the 12px grid in Galmuri");
        }

        [UnityTest]
        public IEnumerator CharmGlyphs_AreInTheFont()
        {
            yield return UITestDriver.LoadMain();
            var font = UIFonts.Main;
            Assert.IsTrue(UIFonts.IsGalmuri(font));
            foreach (var e in new[] { StickerEffects.Shield, StickerEffects.Patience, StickerEffects.Speed, StickerEffects.Tips, "unknown" })
            {
                string g = UIFormat.CharmGlyph(e);
                Assert.IsTrue(font.HasCharacter(g[0], false, true), "glyph '" + g + "' for " + e);
            }
            Assert.IsTrue(font.HasCharacter('→', false, true));
        }

        // ------------------------------------------------------------------ reroll

        [UnityTest]
        public IEnumerator Shop_RerollButton_CostGrows_GreysOutWithReason()
        {
            yield return ToShop(100);
            var ui = UITestDriver.UI();
            InstallShop(4, rerolls: 3, cost: 20);
            yield return UITestDriver.Frames(2);
            Assert.AreEqual("REROLL $20 (3)", ui.ShopRerollText);
            Assert.IsTrue(ui.ShopRerollInteractable);
            string before = ui.ShopOfferHeadline(0);

            var heard = new List<UiSoundKind>();
            int rerolled = 0;
            using (GameServices.Events.Subscribe<UiSound>(e => heard.Add(e.Kind)))
            using (GameServices.Events.Subscribe<ShopRerolled>(e => rerolled++))
            {
                ui.ClickShopReroll();
                yield return UITestDriver.Frames(2);
                Assert.AreEqual(1, rerolled);
                Assert.AreEqual(UiSoundKind.Confirm, heard[heard.Count - 1]);
                Assert.AreEqual(80, Tips, "paid $20");
                Assert.AreEqual("REROLL $40 (2)", ui.ShopRerollText, "cost doubled, one less");
                Assert.AreNotEqual(before, ui.ShopOfferHeadline(0), "new offers drawn");
                Assert.IsNull(ui.ShopDescriptionProblem(), "redrawn grid still fits");

                ui.ClickShopReroll(); // $40 -> 40 left, next $80
                yield return UITestDriver.Frames(2);
                Assert.AreEqual(40, Tips);
                Assert.AreEqual("REROLL $80 (1)", ui.ShopRerollText);
                Assert.IsFalse(ui.ShopRerollInteractable, "can't afford $80 with $40");
                Assert.AreEqual("need +$40", ui.ShopRerollReason);

                GameServices.Get<IGameSettings>().UseKorean = true;
                yield return UITestDriver.Frames(2);
                Assert.AreEqual("다시 뽑기 $80 (1)", ui.ShopRerollText);
                Assert.AreEqual("$40 부족", ui.ShopRerollReason);
                GameServices.Get<IGameSettings>().UseKorean = false;

                UITestDriver.Session().Score.AddTips(100, earned: true);
                yield return UITestDriver.Frames(2);
                Assert.IsTrue(ui.ShopRerollInteractable, "affordable again");
                Assert.AreEqual("", ui.ShopRerollReason);
                ui.ClickShopReroll();
                yield return UITestDriver.Frames(2);
                Assert.AreEqual(3, rerolled);
                Assert.AreEqual("NO REROLLS LEFT", ui.ShopRerollText);
                Assert.IsFalse(ui.ShopRerollInteractable, "max 3 per visit");
                Assert.IsNull(ui.ShopDescriptionProblem());
            }
        }

        // ------------------------------------------------------------------ charm purchase + tomorrow strip

        [UnityTest]
        public IEnumerator Shop_CharmOneClick_LabelNeedsFloor_TomorrowStrip()
        {
            yield return ToShop(100);
            var ui = UITestDriver.UI();
            var fake = InstallShop(4);
            yield return UITestDriver.Frames(2);
            StringAssert.Contains("nothing yet", Plain(ui.ShopTomorrowText));

            var heard = new List<UiSoundKind>();
            int bought = 0;
            using (GameServices.Events.Subscribe<UiSound>(e => heard.Add(e.Kind)))
            using (GameServices.Events.Subscribe<CharmBought>(e => bought++))
            {
                int talisman = ui.ShopOfferIndex("talisman");
                ui.ClickShopOffer(talisman);
                yield return UITestDriver.Frames(2);
                Assert.AreEqual(1, bought, "one click buys a charm");
                Assert.AreEqual(UiSoundKind.Confirm, heard[heard.Count - 1]);
                Assert.AreEqual(60, Tips);
                for (int f = 0; f < ui.FloorButtonCount; f++) Assert.IsFalse(ui.IsFloorApplicable(f), "charms light no floor");
                StringAssert.Contains("Lucky Talisman", ui.ShopResult);
                StringAssert.Contains("Lucky Talisman", Plain(ui.ShopTomorrowText));
                Assert.AreEqual("BOUGHT", ui.ShopOfferSub(talisman));
                Assert.IsFalse(ui.ShopOfferInteractable(talisman), "bought: greyed");

                ui.ClickShopOffer(talisman); // the hook still clicks: the service refuses
                Assert.AreEqual(UiSoundKind.Error, heard[heard.Count - 1]);
                StringAssert.Contains("Already active", ui.ShopResult);

                int coin = ui.ShopOfferIndex("golden_coin");
                if (coin < 0) { fake.OfferCount = 5; yield return UITestDriver.Frames(2); coin = ui.ShopOfferIndex("golden_coin"); }
                UITestDriver.Session().Score.AddTips(-50, earned: false); // $10 left
                yield return UITestDriver.Frames(2);
                Assert.AreEqual("need +$40", ui.ShopOfferSub(coin));
                ui.ClickShopOffer(coin);
                Assert.AreEqual(UiSoundKind.Error, heard[heard.Count - 1]);
                Assert.AreEqual("Not enough tips.", ui.ShopResult);
                Assert.AreEqual(1, bought);

                // label sticker: pick, then a floor
                UITestDriver.Session().Score.AddTips(100, earned: true);
                yield return UITestDriver.Frames(2);
                int label = ui.ShopOfferIndex("four_to_f");
                ui.ClickShopOffer(label);
                yield return null;
                var fourth = FloorIndex.FromPhysicalNumber(4);
                Assert.IsTrue(ui.IsFloorApplicable(fourth.Value));
                ui.ClickFloor(fourth.Value);
                yield return UITestDriver.Frames(2);
                Assert.AreEqual("F", ui.FloorButtonText(fourth.Value));
                string strip = Plain(ui.ShopTomorrowText);
                StringAssert.Contains("TOMORROW", strip);
                StringAssert.Contains("4→F", strip);
                StringAssert.Contains("Lucky Talisman", strip);
                Assert.IsNull(ui.ShopDescriptionProblem());

                GameServices.Get<IGameSettings>().UseKorean = true;
                yield return UITestDriver.Frames(2);
                strip = Plain(ui.ShopTomorrowText);
                StringAssert.Contains("내일", strip);
                StringAssert.Contains("행운 부적", strip);
                Assert.IsNull(ui.ShopDescriptionProblem(), "ko");
                GameServices.Get<IGameSettings>().UseKorean = false;
            }
        }

        // ------------------------------------------------------------------ during the day

        /// <summary>Buys the talisman + bell in the fake shop, then NEXT DAY -> START.</summary>
        static IEnumerator ToDayWithCharms(bool korean = false)
        {
            yield return ToShop(200, korean);
            var ui = UITestDriver.UI();
            var fake = InstallShop(4);
            yield return UITestDriver.Frames(2);
            ui.ClickShopOffer(ui.ShopOfferIndex("talisman"));
            ui.ClickShopOffer(ui.ShopOfferIndex("calm_bell"));
            Assert.AreEqual(2, fake.Active.Count);
            ui.ClickShopDone();
            yield return null;
            yield return UITestDriver.ClickStart();
            yield return UITestDriver.Frames(3);
        }

        [UnityTest]
        public IEnumerator Hud_ShowsTodaysCharms_UnderTheRuleBox()
        {
            yield return ToDayWithCharms();
            var ui = UITestDriver.UI();
            Assert.IsTrue(ui.CharmsVisible);
            string text = Plain(ui.CharmsText);
            StringAssert.Contains("TODAY'S CHARMS", text);
            StringAssert.Contains("Lucky Talisman", text);
            StringAssert.Contains("Calm Bell", text);
            Assert.IsNull(ui.CharmsOverflow());
            var charms = ui.CharmsRect;
            var rules = ui.RulesBoxRect;
            Assert.LessOrEqual(charms.yMax, rules.yMin + 0.5f, "under the rule box " + charms + " vs " + rules);
            Assert.AreEqual(rules.xMax, charms.xMax, 1f, "same right column");
            Assert.Greater(charms.yMin, ui.CanvasSize.y * 0.3f, "compact, upper half of the column");

            GameServices.Get<IGameSettings>().UseKorean = true;
            yield return UITestDriver.Frames(2);
            text = Plain(ui.CharmsText);
            StringAssert.Contains("오늘의 부적", text);
            StringAssert.Contains("평온의 종", text);
            Assert.IsNull(ui.CharmsOverflow(), "ko");
            GameServices.Get<IGameSettings>().UseKorean = false;

            UITestDriver.Session().EndDayNow();
            yield return null;
            Assert.IsFalse(ui.CharmsVisible, "Running only");
        }

        [UnityTest]
        public IEnumerator CharmTriggered_ReplacesTheRedPopup_PopsTheHudLine()
        {
            yield return ToDayWithCharms();
            var ui = UITestDriver.UI();
            var bus = GameServices.Events;
            var guest = new GuestId(9100);
            bus.Publish(new CharmTriggered(new StickerId("talisman"), guest));
            bus.Publish(new LuckJudged(guest, LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, FloorIndex.FromPhysicalNumber(4)));
            string pop = ui.LastPopupText;
            StringAssert.Contains("BLOCKED!", pop);
            StringAssert.DoesNotContain("UNLUCKY", pop);
            StringAssert.Contains("Lucky Talisman blocked it!", Plain(pop));
            StringAssert.Contains("no luck lost", pop);
            Assert.AreEqual(PopupsView.Shield, ui.LastPopupColor, "cyan, not red");
            Time.captureDeltaTime = 0.1f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;
            Assert.Greater(ui.CharmsMaxLineScale, 1.01f, "the talisman line pops");
            yield return UITestDriver.Frames(1);
            StringAssert.Contains("used", Plain(ui.CharmsText), "a 1-block shield is spent");
            var r = ui.LastPopupRect;
            Assert.LessOrEqual(r.xMax, ui.CanvasSize.x, "popup on screen");

            // the next Unlucky verdict (no trigger before it) is the normal red one
            bus.Publish(new LuckJudged(new GuestId(9101), LuckSource.Occupancy, 4, LuckVerdict.Unlucky, FloorIndex.Ground));
            StringAssert.Contains("UNLUCKY", ui.LastPopupText);
            Assert.AreEqual(UIStyle.Unlucky, ui.LastPopupColor);

            // a non-shield effect floats its own line near the guest
            bus.Publish(new CharmTriggered(new StickerId("calm_bell"), guest));
            yield return null;
            Assert.IsTrue(ui.CharmFloatVisible);
            StringAssert.Contains("Calm Bell helped!", Plain(ui.CharmFloatText));
            var fr = ui.CharmFloatRect;
            Assert.IsTrue(fr.xMin >= 0f && fr.xMax <= ui.CanvasSize.x && fr.yMin >= 0f && fr.yMax <= ui.CanvasSize.y, "float on screen " + fr);

            GameServices.Get<IGameSettings>().UseKorean = true;
            yield return null;
            bus.Publish(new CharmTriggered(new StickerId("talisman"), guest));
            bus.Publish(new LuckJudged(guest, LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, FloorIndex.FromPhysicalNumber(4)));
            StringAssert.Contains("막았다!", ui.LastPopupText);
            StringAssert.Contains("행운 부적이 막았다!", Plain(ui.LastPopupText));
            GameServices.Get<IGameSettings>().UseKorean = false;
        }

        // ------------------------------------------------------------------ day end

        [UnityTest]
        public IEnumerator Summary_NotesThatStickersWoreOff()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            yield return UITestDriver.ClickStart();
            UITestDriver.Session().EndDayNow();
            yield return null;
            Assert.AreEqual(DayPhase.Summary, ui.VisibleFlowScreen);
            var bus = GameServices.Events;
            StringAssert.DoesNotContain("wore off", ui.FlowExtra, "nothing was bought for day 1");
            bus.Publish(new StickersExpired(2, 1));
            yield return null;
            StringAssert.Contains("Stickers and charms wore off", ui.FlowExtra);
            GameServices.Get<IGameSettings>().UseKorean = true;
            yield return null;
            StringAssert.Contains("스티커와 부적은 오늘로 끝", ui.FlowExtra);
            Assert.Less(ui.FlowCardRect.yMax, ui.CanvasSize.y, "card still on screen");
            Assert.Greater(ui.FlowCardRect.yMin, 0f);
            GameServices.Get<IGameSettings>().UseKorean = false;
            bus.Publish(new StickersExpired(0, 1));
            yield return null;
            StringAssert.Contains("Charms wore off.", ui.FlowExtra);
            ui.ClickFlowButton(); // -> shop -> next intro clears it
            yield return null;
            UITestDriver.Session().FinishShopping();
            yield return null;
            yield return UITestDriver.ClickStart();
            UITestDriver.Session().EndDayNow();
            yield return null;
            StringAssert.DoesNotContain("wore off", ui.FlowExtra, "the note does not carry over to a day with nothing bought");
        }

        // ------------------------------------------------------------------ panel original-floor marks

        [UnityTest]
        public IEnumerator Panel_OriginalFloorMarks_FollowFloorLabelChanged()
        {
            yield return ToShop(500);
            var ui = UITestDriver.UI();
            var building = GameServices.Get<IBuildingQuery>();
            for (int f = 0; f < ui.FloorButtonCount; f++) Assert.IsNull(ui.OriginalMarkText(f), "no marks before stickers");

            var fourth = FloorIndex.FromPhysicalNumber(4);
            ui.ClickShopOffer(ui.ShopOfferIndex("four_to_f"));
            ui.ClickFloor(fourth.Value);
            yield return null;
            Assert.AreEqual("F", ui.FloorButtonText(fourth.Value));
            Assert.AreEqual(building.GetOriginalLabel(fourth).Text, ui.OriginalMarkText(fourth.Value), "struck-through real floor");
            for (int f = 0; f < ui.FloorButtonCount; f++) if (f != fourth.Value) Assert.IsNull(ui.OriginalMarkText(f), "floor " + f);
            AssertMarkPlacement(ui, fourth.Value);

            // labels coming and going (the one-day expiry publishes FloorLabelChanged for each)
            var fake = new FakeBuilding(building);
            ui.OverrideServicesForTests(building: fake);
            var bus = GameServices.Events;
            fake.Labels[1] = "8";
            fake.Labels[fourth.Value] = building.GetOriginalLabel(fourth).Text;
            bus.Publish(new FloorLabelChanged(new FloorIndex(1), building.GetOriginalLabel(new FloorIndex(1)), new FloorLabel("8")));
            bus.Publish(new FloorLabelChanged(fourth, new FloorLabel("F"), building.GetOriginalLabel(fourth)));
            yield return null;
            Assert.AreEqual("8", ui.FloorButtonText(1));
            Assert.AreEqual("2", ui.OriginalMarkText(1), "Any -> 8 on floor 2 shows the 2");
            Assert.IsNull(ui.OriginalMarkText(fourth.Value), "back to its original: mark gone");
            AssertMarkPlacement(ui, 1);
            fake.Labels.Remove(1);
            bus.Publish(new FloorLabelChanged(new FloorIndex(1), new FloorLabel("8"), building.GetOriginalLabel(new FloorIndex(1))));
            yield return null;
            Assert.IsNull(ui.OriginalMarkText(1));
            ui.OverrideServicesForTests(building: building);
        }

        internal static void AssertMarkPlacement(UIRoot ui, int floor)
        {
            var mark = ui.OriginalMarkRect(floor);
            var button = ui.FloorButtonRect(floor);
            var label = ui.FloorLabelRect(floor);
            Assert.IsTrue(mark.xMin >= button.xMin && mark.xMax <= button.xMax && mark.yMin >= button.yMin && mark.yMax <= button.yMax,
                "mark inside its button " + mark + " vs " + button);
            float textHalf = ui.FloorLabelTextWidth(floor) * 0.5f;
            Assert.LessOrEqual(mark.xMax, label.center.x - textHalf + 0.5f, "mark clear of the big label's glyphs");
        }

        // ------------------------------------------------------------------ captures (-Filter Capture)

        [UnityTest]
        public IEnumerator Capture_ShopRework() => ShopCapture(false);

        [UnityTest]
        public IEnumerator Capture_ShopRework_Korean() => ShopCapture(true);

        static IEnumerator ShopCapture(bool ko)
        {
            yield return ToShop(160, ko);
            var ui = UITestDriver.UI();
            InstallShop(5);
            yield return UITestDriver.Frames(2);
            ui.ClickShopOffer(ui.ShopOfferIndex("talisman"));
            ui.ClickShopOffer(ui.ShopOfferIndex("four_to_f")); // picked: floors glow
            ui.FocusShopOffer(ui.ShopOfferIndex("golden_coin"));
            yield return UITestDriver.Frames(3);
            Assert.IsNull(ui.ShopDescriptionProblem());
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_shop_rework" + (ko ? "_ko" : ""))));
            GameServices.Get<IGameSettings>().UseKorean = false;
        }

        [UnityTest]
        public IEnumerator Capture_ShopRerollEmpty()
        {
            yield return ToShop(160);
            var ui = UITestDriver.UI();
            InstallShop(4, rerolls: 3, cost: 20);
            yield return UITestDriver.Frames(2);
            for (int i = 0; i < 3; i++) { ui.ClickShopReroll(); yield return null; }
            Assert.AreEqual("NO REROLLS LEFT", ui.ShopRerollText);
            yield return UITestDriver.Frames(3);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_shop_reroll_empty")));
        }

        [UnityTest]
        public IEnumerator Capture_HudCharms()
        {
            yield return ToDayWithCharms();
            Time.captureDeltaTime = 0.1f;
            yield return UITestDriver.Frames(20);
            Time.captureDeltaTime = 0f;
            yield return null;
            Assert.IsTrue(UITestDriver.UI().CharmsVisible);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_hud_charms")));
        }

        [UnityTest]
        public IEnumerator Capture_CharmBlocked()
        {
            yield return ToDayWithCharms();
            var ui = UITestDriver.UI();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new List<GuestId>();
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 100 && guests.GetWaiting(FloorIndex.Ground, waiting) < 1; i++) yield return null;
            Time.captureDeltaTime = 0f;
            var guest = waiting.Count > 0 ? waiting[0] : new GuestId(9100);
            var bus = GameServices.Events;
            bus.Publish(new CharmTriggered(new StickerId("talisman"), guest));
            bus.Publish(new LuckJudged(guest, LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, FloorIndex.FromPhysicalNumber(4)));
            Time.captureDeltaTime = 0.05f;
            yield return UITestDriver.Frames(6);
            Time.captureDeltaTime = 0f;
            yield return null;
            StringAssert.Contains("BLOCKED!", ui.LastPopupText);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_charm_blocked")));
        }

        [UnityTest]
        public IEnumerator Capture_PanelOriginalFloor() => PanelCapture(false);

        [UnityTest]
        public IEnumerator Capture_PanelOriginalFloor_Korean() => PanelCapture(true);

        static IEnumerator PanelCapture(bool ko)
        {
            yield return ToShop(600, ko);
            var ui = UITestDriver.UI();
            ui.ClickShopOffer(ui.ShopOfferIndex("four_to_f"));
            ui.ClickFloor(FloorIndex.FromPhysicalNumber(4).Value);
            int any8 = ui.ShopOfferIndex("any_to_8");
            ui.ClickShopOffer(any8);
            ui.ClickFloor(FloorIndex.FromPhysicalNumber(2).Value);
            yield return null;
            ui.ClickFloor(FloorIndex.FromPhysicalNumber(6).Value); // still picked: 2 more in stock
            yield return null;
            Assert.AreEqual("2", ui.OriginalMarkText(1));
            ui.ClickShopDone();
            yield return null;
            yield return UITestDriver.ClickStart();
            Time.captureDeltaTime = 0.1f;
            yield return UITestDriver.Frames(10);
            Time.captureDeltaTime = 0f;
            yield return null;
            // after the merge the labels last exactly this day: still on now
            Assert.AreEqual("8", ui.FloorButtonText(1));
            Assert.AreEqual("2", ui.OriginalMarkText(1));
            Assert.AreEqual("6", ui.OriginalMarkText(5));
            AssertMarkPlacement(ui, 1);
            Assert.IsTrue(System.IO.File.Exists(FrameCapture.Capture("ui_panel_original_floor" + (ko ? "_ko" : ""))));
            GameServices.Get<IGameSettings>().UseKorean = false;
        }
    }
}
