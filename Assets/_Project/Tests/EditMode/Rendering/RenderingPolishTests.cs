using System.Collections.Generic;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Day 5 rain + distant lightning (pure schedules).</summary>
    public class WeatherScheduleTests
    {
        const int Seed = 1408;

        [Test]
        public void Rain_IsDeterministic_FallsDown_AndCoversTheView()
        {
            int left = 0, right = 0;
            for (int i = 0; i < WeatherSchedule.DropCount; i++)
            {
                var a = WeatherSchedule.RainDrop(Seed, i, 12.3f);
                var b = WeatherSchedule.RainDrop(Seed, i, 12.3f);
                Assert.AreEqual(a.Head, b.Head, "same seed + time = same drop");
                var later = WeatherSchedule.RainDrop(Seed, i, 12.32f);
                if (later.Head.y < a.Head.y) Assert.Less(later.Head.x, a.Head.x + 0.001f, "slants down-left");
                Assert.That(a.Level, Is.InRange(0.5f, 1f));
                if (a.Head.x >= 0f && a.Head.x < 320f) left++;
                if (a.Head.x >= 320f && a.Head.x < 640f) right++;
            }
            Assert.Greater(left, 25, "rain on the left half");
            Assert.Greater(right, 25, "rain on the right half");
            Assert.AreNotEqual(WeatherSchedule.RainDrop(Seed, 3, 5f).Head, WeatherSchedule.RainDrop(Seed + 1, 3, 5f).Head);
        }

        [Test]
        public void Lightning_IsRare_EveryEightToTwentySeconds()
        {
            float prev = WeatherSchedule.StrikeStart(Seed, 0);
            for (int k = 1; k < 300; k++)
            {
                float s = WeatherSchedule.StrikeStart(Seed, k);
                Assert.That(s - prev, Is.InRange(WeatherSchedule.MinGap, WeatherSchedule.MaxGap), "gap before strike " + k);
                prev = s;
            }
            // and the level function agrees: one lit stretch per slot
            int strikes = 0;
            bool lit = false;
            for (float t = 0f; t < 600f; t += 0.01f)
            {
                bool now = WeatherSchedule.LightningLevel(Seed, t) > 0f;
                if (now && !lit) strikes++;
                lit = now;
            }
            Assert.That(strikes, Is.InRange(600 / 20, 600 / 8 + 1));
        }

        [Test]
        public void Lightning_SingleFlashes_AtMostThreeTransitionsPerSecond_TwoPerStrike()
        {
            var times = PhotosensitivityTests.Transitions(t => WeatherSchedule.LightningLevel(Seed, t), 0f, 1400f);
            PhotosensitivityTests.AssertAtMostThreePerSecond(times, "lightning");
            int strikes = 0;
            for (int k = 0; k < 100; k++) if (WeatherSchedule.StrikeStart(Seed, k) + WeatherSchedule.FlashSeconds < 1400f) strikes++;
            Assert.AreEqual(2 * strikes, times.Count, "one flash per strike: up, then down (no double flash)");
            for (int seed = 0; seed < 8; seed++)
                PhotosensitivityTests.AssertAtMostThreePerSecond(PhotosensitivityTests.Transitions(t => WeatherSchedule.LightningLevel(seed, t), 0f, 600f), "lightning seed " + seed);
        }

        [Test]
        public void Lightning_StaysModerate_AndTheBoltLivesInTheSky()
        {
            var peak = WeatherView.FlashSky;
            float lum = 0.2126f * peak.r + 0.7152f * peak.g + 0.0722f * peak.b;
            Assert.Less(lum, 0.45f, "the flash brightens the sky, never to white");
            Assert.Greater(lum, 0.2f, "but is visible");
            var main = new List<Vector2Int>();
            var branch = new List<Vector2Int>();
            for (int k = 0; k < 40; k++)
            {
                WeatherSchedule.Bolt(Seed, k, main, branch);
                Assert.GreaterOrEqual(main[0].y, StarField.Height, "starts at the top of the sky");
                Assert.That(main[main.Count - 1].y, Is.InRange(120, 200), "ends at the skyline (the city hides its foot)");
                Assert.Greater(main.Count, 8, "jagged");
                foreach (var p in main) Assert.That(p.x, Is.InRange(-40, StarField.Width + 40));
                Assert.AreEqual(5, branch.Count);
            }
        }

        [Test]
        public void Splashes_ComeAndGo_Deterministically()
        {
            int on = 0;
            for (float t = 0f; t < 10f; t += 0.05f)
                for (int i = 0; i < WeatherSchedule.SplashSlots; i++)
                {
                    bool a = WeatherSchedule.Splash(Seed, i, t, out float u, out float d, out float age);
                    bool b = WeatherSchedule.Splash(Seed, i, t, out float u2, out _, out _);
                    Assert.AreEqual(a, b);
                    Assert.AreEqual(u, u2);
                    if (!a) continue;
                    on++;
                    Assert.That(age, Is.InRange(0f, 1f));
                    Assert.That(d, Is.InRange(0f, 1f));
                }
            Assert.Greater(on, 200, "a steady patter of splashes");
        }
    }

    /// <summary>Neighbour neon signs: slow hum below the flash threshold, stutter signs like the HOTEL sign.</summary>
    public class NeonHumTests
    {
        [Test]
        public void NeonHum_IsSlowAndShallow_StutterSignsStayGentle()
        {
            for (int sign = 0; sign < 12; sign++)
            {
                int s = sign;
                var hum = PhotosensitivityTests.Transitions(t => CityDressing.NeonLevel(1408, s, 0, 1, false, t), 0f, 300f);
                Assert.AreEqual(0, hum.Count, "the hum alone never counts as a flash (sign " + sign + ")");
                for (int letter = 0; letter < 5; letter++)
                {
                    int l = letter;
                    PhotosensitivityTests.AssertAtMostThreePerSecond(
                        PhotosensitivityTests.Transitions(t => CityDressing.NeonLevel(1408, s, l, 5, true, t), 0f, 600f), "stutter sign " + s + " letter " + l);
                }
            }
            float lo = 1f, hi = 0f;
            for (float t = 0f; t < 20f; t += 0.05f)
            {
                float v = CityDressing.NeonLevel(1408, 3, 0, 1, false, t);
                lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            Assert.Greater(hi - lo, 0.05f, "but it does breathe");
            Assert.AreEqual(CityDressing.NeonLevel(1408, 3, 0, 1, false, 7.7f), CityDressing.NeonLevel(1408, 3, 0, 1, false, 7.7f), "deterministic");
        }

        [Test]
        public void EveryBlockHasALook()
        {
            Assert.AreEqual(CityView.Blocks.Length, CityDressing.Looks.Length);
            int shops = 0;
            for (int i = 0; i < CityView.Blocks.Length; i++)
                if (CityView.Blocks[i].Z < 6f && CityDressing.Looks[i].Shop != CityDressing.Shop.None) shops++;
            Assert.GreaterOrEqual(shops, 8, "every near building has a shop front");
        }
    }

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
