using FloorF.Core;
using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.UI
{
    /// <summary>Shop rework / charm strings (UIFormat, FlowScreensView.ExpiredNote).</summary>
    public class UIShopFormatTests
    {
        static StickerDef Charm(string effect, float amount) => new StickerDef { id = "c", kind = StickerKinds.Charm, effect = effect, amount = amount };

        [Test]
        public void CharmGlyph_PerEffect_UnknownFallsBack()
        {
            Assert.AreEqual("◈", UIFormat.CharmGlyph(StickerEffects.Shield));
            Assert.AreEqual("♥", UIFormat.CharmGlyph(StickerEffects.Patience));
            Assert.AreEqual("≫", UIFormat.CharmGlyph(StickerEffects.Speed));
            Assert.AreEqual("$", UIFormat.CharmGlyph(StickerEffects.Tips));
            Assert.AreEqual("◆", UIFormat.CharmGlyph("luck"));
            Assert.AreEqual("◈", UIFormat.CharmGlyph(" Shield "), "trimmed, any case");
            Assert.AreEqual(UIFormat.ShieldHex, UIFormat.CharmHex(StickerEffects.Shield));
        }

        [Test]
        public void LabelEffect_SaysWhatTheStickerDoes_EnKo()
        {
            var four = new StickerDef { id = "four_to_f", from = "4", to = "F" };
            StickerDef Any(string to) => new StickerDef { id = "any", from = "*", to = to };
            Assert.AreEqual("Renumbers floor 4 to F for a day", UIFormat.LabelEffect(four, false));
            Assert.AreEqual("4층의 번호를 하루 동안 F로 바꿈", UIFormat.LabelEffect(four, true));
            Assert.AreEqual("Renumbers any one floor to 8 for a day", UIFormat.LabelEffect(Any("8"), false));
            Assert.AreEqual("원하는 층 하나의 번호를 하루 동안 8로 바꿈", UIFormat.LabelEffect(Any("8"), true), "팔 + 로");
            Assert.AreEqual("원하는 층 하나의 번호를 하루 동안 3으로 바꿈", UIFormat.LabelEffect(Any("3"), true), "삼 + 으로");
            Assert.AreEqual("원하는 층 하나의 번호를 하루 동안 13으로 바꿈", UIFormat.LabelEffect(Any("13"), true));
            Assert.AreEqual("", UIFormat.LabelEffect(Charm(StickerEffects.Tips, 0.3f), false), "charms: CharmEffect");
            Assert.AreEqual("Tips +30%", UIFormat.StickerEffect(Charm(StickerEffects.Tips, 0.3f), false));
            Assert.AreEqual(UIFormat.LabelEffect(four, true), UIFormat.StickerEffect(four, true));
        }

        [Test]
        public void CharmEffect_EnKo()
        {
            Assert.AreEqual("Guests wait 50% longer", UIFormat.CharmEffect(Charm(StickerEffects.Patience, 0.5f), false));
            Assert.AreEqual("손님이 50% 더 오래 기다림", UIFormat.CharmEffect(Charm(StickerEffects.Patience, 0.5f), true));
            Assert.AreEqual("Elevator 25% faster", UIFormat.CharmEffect(Charm(StickerEffects.Speed, 0.25f), false));
            Assert.AreEqual("Tips +30%", UIFormat.CharmEffect(Charm(StickerEffects.Tips, 0.3f), false));
            Assert.AreEqual("팁 +30%", UIFormat.CharmEffect(Charm(StickerEffects.Tips, 0.3f), true));
            Assert.AreEqual("Blocks the first UNLUCKY moment", UIFormat.CharmEffect(Charm(StickerEffects.Shield, 0f), false), "amount 0 -> at least 1");
            Assert.AreEqual("Blocks the first 2 UNLUCKY moments", UIFormat.CharmEffect(Charm(StickerEffects.Shield, 2f), false));
            Assert.AreEqual("첫 불길 판정 2번을 막음", UIFormat.CharmEffect(Charm(StickerEffects.Shield, 2f), true));
            Assert.AreEqual("", UIFormat.CharmEffect(Charm("mystery", 1f), false), "unknown effect: no line");
            Assert.AreEqual("", UIFormat.CharmEffect(new StickerDef { id = "four_to_f", from = "4", to = "F" }, false), "labels: no line");
            Assert.AreEqual(1, UIFormat.ShieldCount(0.4f));
            Assert.AreEqual(3, UIFormat.ShieldCount(2.6f));
        }

        [Test]
        public void RerollLabel_AndCharmResults()
        {
            Assert.AreEqual("REROLL $20 (3)", UIFormat.RerollLabel(20, 3, false));
            Assert.AreEqual("다시 뽑기 $20 (3)", UIFormat.RerollLabel(20, 3, true));
            Assert.AreEqual("NO REROLLS LEFT", UIFormat.RerollLabel(160, 0, false));
            Assert.AreEqual("다시 뽑기 끝", UIFormat.RerollLabel(160, 0, true));
            Assert.AreEqual("Talisman bought - it works all of tomorrow!", UIFormat.CharmResult(StickerApplyResult.Applied, "Talisman", false));
            Assert.AreEqual("Already active for tomorrow.", UIFormat.CharmResult(StickerApplyResult.AlreadyActive, "x", false));
            Assert.AreEqual("Not enough tips.", UIFormat.CharmResult(StickerApplyResult.NotEnoughTips, "x", false));
            Assert.AreEqual(UIStrings.ResultWrongKind.En, UIFormat.ApplyResult(StickerApplyResult.WrongKind, "4", "F", false));
        }

        [Test]
        public void ExpiredNote_ByWhatRanOut()
        {
            Assert.IsNull(FlowScreensView.ExpiredNote(0, 0, false));
            StringAssert.Contains("Stickers and charms wore off", FlowScreensView.ExpiredNote(2, 1, false));
            StringAssert.StartsWith("스티커와 부적은 오늘로 끝", FlowScreensView.ExpiredNote(1, 1, true));
            StringAssert.Contains("real number", FlowScreensView.ExpiredNote(3, 0, false));
            Assert.AreEqual("Charms wore off.", FlowScreensView.ExpiredNote(0, 2, false));
        }

        [Test]
        public void KoreanCharmPop_UsesTheRightParticle()
        {
            Assert.AreEqual("행운 부적이 막았다!", KoreanParticles.Format(UIStrings.CharmBlocked.Ko, "행운 부적"));
            Assert.AreEqual("평온의 종이 막았다!", KoreanParticles.Format(UIStrings.CharmBlocked.Ko, "평온의 종"));
            Assert.AreEqual("황금 동전이 막았다!", KoreanParticles.Format(UIStrings.CharmBlocked.Ko, "황금 동전"));
            Assert.AreEqual("네잎 클로버가 막았다!", KoreanParticles.Format(UIStrings.CharmBlocked.Ko, "네잎 클로버"));
        }
    }
}
