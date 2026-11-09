using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>
    /// Banded pixel lighting (PixelGlow) pure parts: the world-anchored light grid, pool projection, band constants that
    /// keep halos stable, and the texel ledger that lets the city rebuild per zoom without new lights.
    /// </summary>
    public class PixelLightingTests
    {
        const int W = RenderingStyle.PixelWidth, H = RenderingStyle.PixelHeight;

        [Test]
        public void GridOffset_IsCameraPixelMod4_AlwaysInRange()
        {
            Assert.AreEqual(new Vector2Int(0, 0), PixelGlow.GridOffsetFor(Vector2.zero));
            Assert.AreEqual(new Vector2Int(1, 3), PixelGlow.GridOffsetFor(new Vector2(5f, -1f)));
            Assert.AreEqual(new Vector2Int(0, 3), PixelGlow.GridOffsetFor(new Vector2(-4f, 7f)));
            for (int c = -50; c <= 50; c++)
            {
                var o = PixelGlow.GridOffsetFor(new Vector2(c, -c));
                Assert.That(o.x, Is.InRange(0, PixelGlow.Downsample - 1));
                Assert.That(o.y, Is.InRange(0, PixelGlow.Downsample - 1));
            }
        }

        [Test]
        public void LightCells_StayOnTheSameWorldPixels_WhateverTheCameraPixel()
        {
            // light cell i covers screen pixels [4i - offset, 4i - offset + 4); in world pixels (screen + camera - W/2)
            // every cell must start on a multiple of 4, so a whole-pixel pan never re-bins the light
            for (int cam = -37; cam <= 41; cam++)
            {
                int ox = PixelGlow.GridOffsetFor(new Vector2(cam, cam)).x;
                for (int i = 0; i < 3; i++)
                {
                    int worldStart = PixelGlow.Downsample * i - ox + cam - W / 2;
                    Assert.AreEqual(0, ((worldStart % PixelGlow.Downsample) + PixelGlow.Downsample) % PixelGlow.Downsample, "camera pixel " + cam);
                }
            }
        }

        [Test]
        public void Pools_ProjectWithTheCamera_ByWholePixels()
        {
            var shear = RenderingStyle.CameraShear;
            const float depth = -2f, ppu = 12f;
            var f = new CameraFrame(new Vector3(1f, 5f, -30f), Quaternion.identity, H / (2f * ppu), H, shear, depth);
            // a point on the shear plane under the camera centre is the target centre
            var c = PixelGlow.ToTargetPixels(f, new Vector3(1f, 5f, depth), W, H);
            Assert.AreEqual(W * 0.5f, c.x, 1e-3f);
            Assert.AreEqual(H * 0.5f, c.y, 1e-3f);
            // deeper points shift up/right with the shear; camera moves shift every pool by the same whole pixels
            var p = new Vector3(-3f, 1f, depth + 2f);
            var a = PixelGlow.ToTargetPixels(f, p, W, H);
            var moved = f.WithPosition(f.Position + new Vector3(3f, -2f, 0f) / ppu, H);
            var b = PixelGlow.ToTargetPixels(moved, p, W, H);
            Assert.AreEqual(a.x - 3f, b.x, 1e-3f);
            Assert.AreEqual(a.y + 2f, b.y, 1e-3f);
            Assert.AreEqual(((-3f + shear.x * 2f) - 1f) * ppu + W * 0.5f, a.x, 1e-3f);
        }

        [Test]
        public void Bands_AreFewFlatAscendingSteps_AndHummingNeonSaturates()
        {
            Assert.AreEqual(4, PixelGlow.BandCount);
            var t = PixelGlow.Bands;
            var l = PixelGlow.Levels;
            Assert.That(t.x < t.y && t.y < t.z && t.z < t.w, "thresholds ascend");
            Assert.That(0f < l.x && l.x < l.y && l.y < l.z && l.z < l.w, "levels ascend");
            Assert.Less(l.w, 0.35f, "subtle: the brightest band stays a tint");
            // the neon's slow hum never drops below the full-weight brightness -> the halo does not breathe
            Assert.GreaterOrEqual(1f - CityDressing.HumDepth, PixelGlow.Strong.y);
            Assert.Less(PixelGlow.Weak.z, 0.1f, "lit windows only add a faint wash, no halo per window");
            Assert.LessOrEqual(PixelGlow.Weak.y, PixelGlow.Strong.x);
        }

        [Test]
        public void TexelLedger_ReplaysTheSameTexels_AndNeverAllocatesMore()
        {
            var ledger = new TexelLedger();
            int next = 10;
            var first = new int[5];
            for (int i = 0; i < first.Length; i++) first[i] = ledger.Take(() => next++);
            Assert.IsFalse(ledger.Replaying);
            ledger.Replay();
            Assert.IsTrue(ledger.Replaying);
            for (int i = 0; i < first.Length; i++) Assert.AreEqual(first[i], ledger.Take(() => next++));
            Assert.AreEqual(15, next, "nothing allocated while replaying");
            Assert.Throws<System.InvalidOperationException>(() => ledger.Take(() => next++));
            ledger.Replay();
            Assert.AreEqual(first[0], ledger.Take(() => next++), "replays again from the start");
        }
    }
}
