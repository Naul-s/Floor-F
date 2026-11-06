using FloorF.Core;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    public class PixelFontTests
    {
        [Test]
        public void HasGlyphs_ForEveryLabelCharacter()
        {
            foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ?- ")
                Assert.IsTrue(PixelFont.HasGlyph(c), "missing glyph '" + c + "'");
            Assert.IsTrue(PixelFont.HasGlyph('f'), "lower case maps to upper case");
        }

        [Test]
        public void Measure_GrowsWithText()
        {
            Assert.AreEqual(new Vector2Int(5 + 2, 7 + 2), PixelFont.Measure("4", 1));
            Assert.AreEqual(new Vector2Int(3 * 6 - 1 + 2, 9), PixelFont.Measure("12A", 1));
        }

        [Test]
        public void Rasterize_DifferentLabelsDiffer_AndInkStaysInsidePadding()
        {
            var four = PixelFont.Rasterize("4", 1, out var size);
            var f = PixelFont.Rasterize("F", 1, out _);
            Assert.AreNotEqual(string.Join(",", four), string.Join(",", f));
            for (int x = 0; x < size.x; x++)
            {
                Assert.IsFalse(four[x], "bottom padding row is plate");
                Assert.IsFalse(four[(size.y - 1) * size.x + x], "top padding row is plate");
            }
            // 'F' top bar is the full glyph width on the top glyph row (row 0 is the bottom)
            int top = (1 + 6) * size.x;
            for (int x = 1; x <= 5; x++) Assert.IsTrue(f[top + x], "F top bar at x=" + x);
        }

        [Test]
        public void Rasterize_EmptyText_RendersQuestionMark()
        {
            var empty = PixelFont.Rasterize("", 1, out var a);
            var q = PixelFont.Rasterize("?", 1, out var b);
            Assert.AreEqual(b, a);
            CollectionAssert.AreEqual(q, empty);
        }

        [Test]
        public void CreateTexture_IsPointFiltered()
        {
            var tex = PixelFont.CreateTexture("88", Color.white, Color.black);
            try
            {
                Assert.AreEqual(FilterMode.Point, tex.filterMode);
                Assert.AreEqual(PixelFont.Measure("88", 1).x, tex.width);
            }
            finally { Object.DestroyImmediate(tex); }
        }
    }

    public class CameraFramingTests
    {
        [Test]
        public void Fit_KeepsWholeBuildingInView_AndSnapsToWholePixels()
        {
            var layout = new HotelLayout(new HotelLayoutDef(), 9, 8);
            var bounds = BuildingView.FramingBounds(layout);
            var rot = Quaternion.Euler(RenderingStyle.CameraPitch, RenderingStyle.CameraYaw, 0f);
            float aspect = 640f / 360f;
            var frame = CameraFraming.SnapToWholePixels(CameraFraming.Fit(bounds, rot, aspect, 0.05f, 20f, 360), 360);
            Assert.AreEqual(Mathf.Round(frame.PixelsPerUnit), frame.PixelsPerUnit, 1e-3f, "integer pixels per unit");
            Assert.Greater(frame.PixelsPerUnit, 8f, "9 floors should still be reasonably large");

            var go = new GameObject("FramingTestCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = frame.OrthographicSize;
                cam.aspect = aspect;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                go.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
                var c = bounds.center; var e = bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    var v = cam.WorldToViewportPoint(corner);
                    Assert.That(v.x, Is.InRange(0f, 1f), "corner " + i + " x");
                    Assert.That(v.y, Is.InRange(0f, 1f), "corner " + i + " y");
                    Assert.Greater(v.z, 0.1f, "corner " + i + " in front of the camera");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }

    public class FloorLightingTests
    {
        static FloorLighting Make(int floors)
        {
            var l = new FloorLighting();
            for (int i = 0; i < floors; i++) l.AddFloor(null, Color.grey, Color.white, 1f);
            return l;
        }

        [Test]
        public void Flash_StartsAtFlashColour_AndFadesBackToBase()
        {
            var l = Make(3);
            var f = new FloorIndex(1);
            l.Flash(f, Color.red, 1f);
            l.Tick(0f, 0.0001f);
            Assert.Greater(l.GetCurrentColor(f).r, 0.99f);
            Assert.Less(l.GetCurrentColor(f).g, 0.05f, "red flash");
            Assert.AreEqual(Color.white, l.GetCurrentColor(new FloorIndex(0)), "other floors untouched");
            for (int i = 0; i < 20; i++) l.Tick(i * 0.1f, 0.1f);
            Assert.IsFalse(l.IsFlashing(f));
            Assert.AreEqual(Color.white, l.GetCurrentColor(f));
        }

        [Test]
        public void Flicker_IsMostlyOn_ButDropsOut()
        {
            int off = 0, n = 2000;
            for (int i = 0; i < n; i++) if (FloorLighting.FlickerValue(i * 0.013f, 3) < 0.5f) off++;
            Assert.Greater(off, n / 50, "drops out sometimes");
            Assert.Less(off, n / 2, "mostly on");
        }

        [Test]
        public void SetFlicker_OutOfRange_IsIgnored()
        {
            var l = Make(2);
            l.SetFlicker(new FloorIndex(5), true);
            l.SetFlicker(new FloorIndex(1), true);
            Assert.IsTrue(l.IsFlickering(new FloorIndex(1)));
            Assert.IsFalse(l.IsFlickering(new FloorIndex(5)));
        }
    }
}
