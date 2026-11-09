using System.Collections.Generic;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Pure wall patterns: real masonry (courses, joints, bond), flat bands (no speckle), seamless tiling, determinism.</summary>
    public class PixelPatternTests
    {
        static readonly WallPattern[] All =
        {
            WallPattern.Brick, WallPattern.Stone, WallPattern.Panel, WallPattern.Tile, WallPattern.Bands, WallPattern.Dentil, WallPattern.Gravel,
        };

        [Test]
        public void EveryPattern_IsAPattern_FewLevels_TilesSeamlessly([NUnit.Framework.Range(0, 6)] int index)
        {
            var p = All[index];
            var s = PixelPatterns.Size(p);
            Assert.GreaterOrEqual(s.x, 4);
            Assert.GreaterOrEqual(s.y, 1);
            Assert.That(PixelPatterns.Levels(p), Is.InRange(2, 10), "a pattern of a few flat shades, not a flat colour or noise");
            for (int y = -s.y; y < 2 * s.y; y++)
                for (int x = -s.x; x < 2 * s.x; x++)
                {
                    float v = PixelPatterns.Shade(p, x, y);
                    Assert.AreEqual(v, PixelPatterns.Shade(p, x + s.x, y), "wraps in x");
                    Assert.AreEqual(v, PixelPatterns.Shade(p, x, y + s.y), "wraps in y");
                    Assert.That(v, Is.InRange(0.5f, 1.2f), "a shade of the wall colour, never black or blown out");
                }
            Assert.That(PixelPatterns.Max(p), Is.InRange(1f, 1.2f));
            // no checkerboard dither: no 2x2 block alternates diagonally
            int checkers = 0;
            for (int y = 0; y < s.y; y++)
                for (int x = 0; x < s.x; x++)
                {
                    float a = PixelPatterns.Shade(p, x, y), b = PixelPatterns.Shade(p, x + 1, y), c = PixelPatterns.Shade(p, x, y + 1), d = PixelPatterns.Shade(p, x + 1, y + 1);
                    if (a == d && b == c && a != b) checkers++;
                }
            Assert.LessOrEqual(checkers, s.x * s.y / 16, "masonry, not dither");
        }

        [Test]
        public void Brick_RunningBond_FourPixelCourses()
        {
            for (int x = 0; x < 32; x++) Assert.AreEqual(PixelPatterns.Joint, PixelPatterns.Shade(WallPattern.Brick, x, 0), "bed joint every 4 rows");
            // head joints: course 0 at x = 0, 8, ...; course 1 offset by half a brick
            Assert.AreEqual(PixelPatterns.Joint, PixelPatterns.Shade(WallPattern.Brick, 8, 2));
            Assert.AreNotEqual(PixelPatterns.Joint, PixelPatterns.Shade(WallPattern.Brick, 8, 6));
            Assert.AreEqual(PixelPatterns.Joint, PixelPatterns.Shade(WallPattern.Brick, 4, 6));
            // lit top row of every brick
            Assert.Greater(PixelPatterns.Shade(WallPattern.Brick, 2, 3), PixelPatterns.Shade(WallPattern.Brick, 2, 2));
        }

        [Test]
        public void Dentil_IsUniformInY_SoAnyBandHeightWorks()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 1; y < 4; y++)
                    Assert.AreEqual(PixelPatterns.Shade(WallPattern.Dentil, x, 0), PixelPatterns.Shade(WallPattern.Dentil, x, y));
        }

        [Test]
        public void Bake_IsDeterministic_AndClamped()
        {
            var c = new Color(0.9f, 0.5f, 0.2f);
            foreach (var p in All)
            {
                var a = PixelPatterns.Bake(p, c);
                var b = PixelPatterns.Bake(p, c);
                Assert.AreEqual(a, b, p + " deterministic");
                var s = PixelPatterns.Size(p);
                Assert.AreEqual(s.x * s.y, a.Length);
                foreach (var px in a) Assert.AreEqual(255, px.a);
            }
            // normalised (lit textures): the brightest texel is white
            var n = PixelPatterns.Bake(WallPattern.Brick, Color.white, 1f / PixelPatterns.Max(WallPattern.Brick));
            byte max = 0;
            foreach (var px in n) max = (byte)Mathf.Max(max, px.r);
            Assert.AreEqual(255, max);
        }

        [Test]
        public void NeighbourWalls_AllPatterned_WithVariety()
        {
            var used = new HashSet<WallPattern>();
            foreach (var b in CityView.Blocks)
            {
                var p = CityView.PatternOf(b);
                Assert.AreNotEqual(WallPattern.None, p, "no untextured wall");
                used.Add(p);
                if (b.Z > 6f) Assert.AreEqual(WallPattern.Bands, p, "far towers: quiet floor lines");
            }
            Assert.GreaterOrEqual(used.Count, 4, "brick, panels, tiles, stone, bands");
        }
    }
}
