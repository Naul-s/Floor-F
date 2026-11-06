using FloorF.Gameplay;
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

namespace FloorF.Tests.EditMode.Rendering
{
    public class JuiceMathTests
    {
        const float Px = 1f / 11f;

        [Test]
        public void GuestPose_MoodsLookDifferent_AndBobMovesWholePixels()
        {
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                var n = GuestPose.Evaluate(GuestMood.Neutral, t, 0.3f, float.MaxValue, Px);
                float steps = n.Offset.y / Px;
                Assert.AreEqual(Mathf.Round(steps), steps, 1e-4f, "idle bob in whole pixels");
                Assert.Less(n.ArmL, 20f, "arms down when calm");
            }
            var scared = GuestPose.Evaluate(GuestMood.Scared, 1f, 0.3f, float.MaxValue, Px);
            Assert.Less(scared.Scale.y, 0.9f, "scared cowers");
            Assert.Greater(scared.ArmL, 150f, "hands up");
            float angryHop = 0f, angryArms = 0f;
            for (float t = 0f; t < 1f; t += 0.02f)
            {
                var a = GuestPose.Evaluate(GuestMood.Angry, t, 0.3f, float.MaxValue, Px);
                angryHop = Mathf.Max(angryHop, a.Offset.y);
                angryArms = Mathf.Max(angryArms, a.ArmR);
            }
            Assert.GreaterOrEqual(angryHop, 2.9f * Px, "angry hops");
            Assert.Greater(angryArms, 140f, "fists up");
            int impatientMoves = 0;
            var prev = GuestPose.Evaluate(GuestMood.Impatient, 0f, 0.3f, float.MaxValue, Px);
            for (float t = 0.02f; t < 2f; t += 0.02f)
            {
                var p = GuestPose.Evaluate(GuestMood.Impatient, t, 0.3f, float.MaxValue, Px);
                if (p.Offset != prev.Offset || p.Tilt != prev.Tilt) impatientMoves++;
                prev = p;
            }
            Assert.Greater(impatientMoves, 10, "impatient stomps/sways often");
        }

        [Test]
        public void GuestPose_Jump_RisesThenLands()
        {
            float peak = 0f;
            for (float a = 0f; a < GuestPose.JumpSeconds; a += 0.01f)
                peak = Mathf.Max(peak, GuestPose.Evaluate(GuestMood.Neutral, 0f, 0f, a, Px).Offset.y);
            Assert.Greater(peak, 0.6f);
            Assert.Less(GuestPose.Evaluate(GuestMood.Neutral, 0f, 0f, 0.02f, Px).Scale.y, 0.9f, "anticipation squash");
            var after = GuestPose.Evaluate(GuestMood.Neutral, 0f, 0f, GuestPose.JumpSeconds, Px);
            Assert.Less(after.Offset.y, 0.2f, "back on the ground");
        }

        [Test]
        public void CameraShake_WholePixels_EndsAtZero()
        {
            bool moved = false;
            for (float e = 0f; e < CameraRig.ShakeSeconds; e += 0.01f)
            {
                var o = CameraRig.ShakeOffset(e, CameraRig.ShakeSeconds, CameraRig.ShakePixels);
                Assert.LessOrEqual(Mathf.Abs(o.x), 3);
                if (o != Vector2Int.zero) moved = true;
            }
            Assert.IsTrue(moved);
            Assert.AreEqual(Vector2Int.zero, CameraRig.ShakeOffset(CameraRig.ShakeSeconds, CameraRig.ShakeSeconds, 3f));
            Assert.AreEqual(Vector2Int.zero, CameraRig.ShakeOffset(float.MaxValue, CameraRig.ShakeSeconds, 3f));
        }

        [Test]
        public void TitleFrame_IsWider_AndWholePixels()
        {
            var layout = new HotelLayout(new HotelLayoutDef(), 9, 8);
            var rot = Quaternion.Euler(RenderingStyle.CameraPitch, RenderingStyle.CameraYaw, 0f);
            var f = CameraFraming.SnapToWholePixels(CameraFraming.Fit(BuildingView.FramingBounds(layout), rot, 640f / 360f, 0.05f, 20f, 360), 360);
            var t = CameraRig.MakeTitleFrame(f, 360);
            Assert.Greater(t.OrthographicSize, f.OrthographicSize);
            Assert.AreEqual(Mathf.Round(t.PixelsPerUnit), t.PixelsPerUnit, 1e-3f);
            Assert.AreNotEqual(CameraRig.TitlePanOffset(0f), CameraRig.TitlePanOffset(CameraRig.TitlePanPeriod * 0.25f));
        }

        [Test]
        public void StickScale_Overshoots_Slams_Settles()
        {
            Assert.Greater(BuildingView.StickScale(0f), 1.5f);
            float min = 2f;
            for (float a = 0f; a < BuildingView.StickSeconds; a += 0.005f) min = Mathf.Min(min, BuildingView.StickScale(a));
            Assert.Less(min, 0.9f);
            Assert.AreEqual(1f, BuildingView.StickScale(BuildingView.StickSeconds));
        }

        [Test]
        public void CarBump_SquashesThenRests()
        {
            var b = ElevatorView.BumpPose(0f, 1f);
            Assert.Less(b.y, 0.95f, "squash on arrival");
            Assert.Greater(b.x, 1.03f, "and stretch wide");
            Assert.AreEqual(new Vector3(1f, 1f, 0f), ElevatorView.BumpPose(ElevatorView.BumpSeconds, 1f));
        }

        [Test]
        public void FloorLighting_LevelEases_AndResetMoodRestores()
        {
            var l = new FloorLighting();
            for (int i = 0; i < 2; i++) l.AddFloor(null, Color.grey, Color.white, 2f);
            var f = new FloorIndex(1);
            l.SetLevel(f, 0f, 2f);
            l.SetBaseColor(f, Color.blue);
            l.Tick(0f, 0.25f);
            Assert.AreEqual(0.5f, l.GetLevel(f), 1e-4f);
            Assert.AreEqual(1f, l.GetLightIntensity(f), 1e-4f, "intensity follows level");
            l.Tick(0.25f, 0.5f);
            Assert.AreEqual(0f, l.GetLightIntensity(f));
            Assert.IsFalse(l.IsLit(f));
            Assert.IsTrue(l.IsLit(new FloorIndex(0)));
            l.ResetMood();
            l.Tick(1f, 0.1f);
            Assert.IsTrue(l.IsLit(f));
            Assert.AreEqual(Color.white, l.GetBaseColor(f));
        }

        [Test]
        public void VividColour_KeepsHue_BoostsSaturation_LeavesGreys()
        {
            var lilac = CoreUnityExtensions.ParseColor("#C9A0DC");
            Color.RGBToHSV(lilac, out float h0, out float s0, out _);
            Color.RGBToHSV(GuestFigure.Vivid(lilac), out float h1, out float s1, out float v1);
            Assert.AreEqual(h0, h1, 0.01f);
            Assert.Greater(s1, s0 + 0.15f);
            Assert.GreaterOrEqual(v1, 0.77f);
            var grey = CoreUnityExtensions.ParseColor("#2B2B2B");
            Assert.AreEqual(grey, GuestFigure.Vivid(grey));
        }
    }
}
