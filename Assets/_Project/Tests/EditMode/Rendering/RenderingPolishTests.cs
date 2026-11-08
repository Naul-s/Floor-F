using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Original-floor marks on sticker signs (struck-out tiny original number beside the sticker).</summary>
    public class OriginalFloorMarkTests
    {
        [Test]
        public void Mark_OnlyForStickersThatChangedTheText()
        {
            Assert.AreEqual("4", BuildingView.OriginalMark("8", true, "4"));
            Assert.AreEqual("4", BuildingView.OriginalMark("F", true, "4"));
            Assert.IsNull(BuildingView.OriginalMark("8", false, "4"), "no sticker (data label) = no mark");
            Assert.IsNull(BuildingView.OriginalMark("8", true, "8"), "sticker that keeps the number = no mark");
            Assert.IsNull(BuildingView.OriginalMark("8", true, null));
            Assert.IsNull(BuildingView.OriginalMark("8", true, ""));
        }

        [Test]
        public void MarkedTexture_KeepsTheLabelSize_AndShowsTheStruckOriginalOnTheOldPlate()
        {
            var ink = new Color(0.1f, 0.1f, 0.1f);
            var plate = new Color(0.9f, 0.8f, 0.2f);
            var oldInk = new Color(0.8f, 0.7f, 0.5f);
            var oldPlate = new Color(0.05f, 0.05f, 0.05f);
            var strike = new Color(1f, 0.2f, 0.2f);
            var plain = PixelFont.CreateTexture("8", ink, plate);
            var marked = PixelFont.CreateMarkedTexture("8", ink, plate, "4", oldInk, oldPlate, strike);
            try
            {
                int s = PixelFont.MarkedTexelScale;
                int strip = PixelFont.MarkStripWidth("4");
                Assert.AreEqual(plain.height * s, marked.height, "same height on screen (texels are half size)");
                Assert.AreEqual(strip + plain.width * s, marked.width, "plain label + the mark strip on its left");
                Assert.Less(strip, plain.width * s, "the mark is small next to the label");

                // the sticker part is the plain sign, 2x2 per texel
                var p = plain.GetPixels32();
                var m = marked.GetPixels32();
                for (int y = 0; y < marked.height; y++)
                    for (int x = 0; x < plain.width * s; x++)
                        Assert.AreEqual(p[(y / s) * plain.width + x / s], m[y * marked.width + strip + x], "label texel " + x + "," + y);

                // the strip: old plate, a few ink pixels of the tiny '4' and a red slash crossing it
                int inkPx = 0, strikePx = 0, rows = 0;
                Color32 ink32 = oldInk, strike32 = strike;
                for (int y = 0; y < marked.height; y++)
                {
                    bool rowHasStrike = false;
                    for (int x = 0; x < strip; x++)
                    {
                        var c = m[y * marked.width + x];
                        if (c.Equals(ink32)) inkPx++;
                        if (c.Equals(strike32)) { strikePx++; rowHasStrike = true; }
                    }
                    if (rowHasStrike) rows++;
                }
                Assert.Greater(inkPx, 4, "tiny original number drawn");
                Assert.GreaterOrEqual(rows, PixelFont.TinyHeight + 2, "the slash spans the whole tiny number (diagonal)");
                Assert.Greater(strikePx, 5);

                var other = PixelFont.CreateMarkedTexture("8", ink, plate, "3", oldInk, oldPlate, strike);
                Assert.AreNotEqual(string.Join(",", m), string.Join(",", other.GetPixels32()), "different originals differ");
                Object.DestroyImmediate(other);
            }
            finally
            {
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(marked);
            }
        }
    }
}
