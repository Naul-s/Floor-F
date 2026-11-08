using System.Collections.Generic;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Night sky (pure parts): seeded star field, star-night bake, viewport anchoring, schedules.</summary>
    public class SkyTests
    {
        static Vector2Int Moon => new Vector2Int(Mathf.RoundToInt(SkyView.MoonViewport.x * 640f), Mathf.RoundToInt(SkyView.MoonViewport.y * 360f));

        static string Fingerprint(List<SkyStar> stars)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in stars) sb.Append(s.X).Append(',').Append(s.Y).Append(',').Append((int)s.Kind).Append(',').Append(s.Group).Append(s.Cross).Append(';');
            return sb.ToString();
        }

        [Test]
        public void StarField_IsDeterministicPerSeed_WithAllThreeKinds()
        {
            var a = StarField.Generate(1408, Moon, SkyView.HaloRadius + 3);
            var b = StarField.Generate(1408, Moon, SkyView.HaloRadius + 3);
            var c = StarField.Generate(77, Moon, SkyView.HaloRadius + 3);
            Assert.AreEqual(Fingerprint(a), Fingerprint(b), "same seed, same sky");
            Assert.AreNotEqual(Fingerprint(a), Fingerprint(c), "another seed, another sky");

            int steady = 0, twinkle = 0, glint = 0, crosses = 0;
            var groups = new HashSet<int>();
            foreach (var s in a)
            {
                if (s.Kind == StarKind.Steady) steady++;
                else if (s.Kind == StarKind.Twinkle) { twinkle++; groups.Add(s.Group); }
                else glint++;
                if (s.Cross) crosses++;
                Assert.That(s.X, Is.InRange(1, 638));
                Assert.That(s.Y, Is.InRange(StarField.MinY, 358));
                if (s.Kind == StarKind.Glint) Assert.GreaterOrEqual(s.Y, StarField.GlintMinY, "glints in the open upper sky");
                var d = new Vector2(s.X - Moon.x, s.Y - Moon.y);
                Assert.Greater(d.magnitude, SkyView.HaloRadius + 1f, "no star on the moon or its halo");
            }
            Assert.AreEqual(StarField.SteadyCount, steady);
            Assert.AreEqual(StarField.TwinkleCount, twinkle);
            Assert.AreEqual(StarField.GlintCount, glint);
            Assert.AreEqual(StarField.TwinkleGroups, groups.Count, "every twinkle group used");
            Assert.Greater(crosses, 5, "a few bright '+' stars");
            for (int i = 0; i < a.Count; i++)
                for (int j = i + 1; j < a.Count; j++)
                    Assert.IsFalse(Mathf.Abs(a[i].X - a[j].X) <= StarField.Spacing && Mathf.Abs(a[i].Y - a[j].Y) <= StarField.Spacing, "stars never touch");
        }

        static float Lum(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        [Test]
        public void StarNightBake_IsDeterministic_WithABrighterBandAndDenserStars()
        {
            var a = SkyView.BakeStarNight(1408);
            var b = SkyView.BakeStarNight(1408);
            var c = SkyView.BakeStarNight(5);
            Assert.AreEqual(640 * 360, a.Length);
            bool same = true, differs = false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i])) same = false;
                if (!a[i].Equals(c[i])) differs = true;
            }
            Assert.IsTrue(same, "same seed, same bake");
            Assert.IsTrue(differs, "another seed, another bake");

            // on the band's centre line vs. well off it (same rows, so the gradient cancels out)
            float on = 0f, off = 0f;
            int onStars = 0, offStars = 0, n = 0;
            for (int x = 40; x < 600; x++)
            {
                int yc = Mathf.RoundToInt(SkyView.BandCentre(x));
                for (int dy = -6; dy <= 6; dy++)
                {
                    int y = yc + dy, yo = yc - 75 + dy;
                    if (y < 0 || y >= 360 || yo < 0) continue;
                    float l = Lum(a[y * 640 + x]), lo = Lum(a[yo * 640 + x]);
                    on += l; off += lo; n++;
                    if (l > 0.35f) onStars++;
                    if (lo > 0.35f) offStars++;
                }
            }
            Assert.Greater(on / n, off / n + 0.02f, "Milky Way glow");
            Assert.Greater(onStars, offStars * 3 + 10, "denser stars in the band");
            // the sky stays dark: never brighter than the HOTEL sign glow on average
            float mean = 0f;
            foreach (var p in a) mean += Lum(p);
            Assert.Less(mean / a.Length, 0.15f);
        }

        static Vector2 Viewport(CameraFrame f, Vector3 world)
        {
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(f.Position, f.Rotation, Vector3.one).inverse;
            var clip = CameraFraming.ProjectionMatrix(f, 640f / 360f, 0.3f, 100f) * view * new Vector4(world.x, world.y, world.z, 1f);
            return new Vector2((clip.x / clip.w + 1f) * 0.5f, (clip.y / clip.w + 1f) * 0.5f);
        }

        [Test]
        public void SkyLayers_AreViewportAnchored_OnWholePixels_InGameplayTitleAndPan()
        {
            const int h = 360;
            var rect = new Rect(-8f, -2f, 18f, 30f);
            var shear = RenderingStyle.CameraShear;
            var baseFrame = CameraFraming.SnapToWholePixels(CameraFraming.FitOblique(rect, shear, -2.5f, -7.7f, 640f / 360f, 0.05f, 20f, h), h);
            var title = CameraRig.MakeTitleFrame(baseFrame, h);
            Assert.AreNotEqual(baseFrame.PixelsPerUnit, title.PixelsPerUnit, "title zooms out");
            foreach (var f0 in new[] { baseFrame, title })
                for (int pan = -26; pan <= 26; pan += 13)
                {
                    var f = f0.WithPosition(f0.Position + new Vector3(pan / f0.PixelsPerUnit, 3f / f0.PixelsPerUnit, 0f), h);
                    foreach (float z in new[] { SkyView.StarDepth, SkyView.BandDepth, SkyView.FxDepth })
                    {
                        var o = Viewport(f, SkyView.LayerOrigin(f, z));
                        Assert.AreEqual(0f, o.x * 640f, 1e-3f, "layer origin on the left edge");
                        Assert.AreEqual(0f, o.y * 360f, 1e-3f, "layer origin on the bottom edge");
                        // one layer unit (1 / ppu) = one target pixel, so every star corner is a pixel corner
                        var p = Viewport(f, SkyView.LayerOrigin(f, z) + new Vector3(123f, 301f, 0f) / f.PixelsPerUnit);
                        Assert.AreEqual(123f, p.x * 640f, 2e-3f);
                        Assert.AreEqual(301f, p.y * 360f, 2e-3f);
                        // the wish-star mapping
                        var vp = new Vector2(0.3172f, 0.8813f);
                        var back = Viewport(f, SkyView.ViewportToWorld(f, vp, z));
                        Assert.AreEqual(vp.x, back.x, 1e-4f);
                        Assert.AreEqual(vp.y, back.y, 1e-4f);
                    }
                }
        }

        [Test]
        public void Glints_AreRare_AndUseEveryKindOfSlot()
        {
            int visible = 0, samples = 0;
            var seen = new HashSet<int>();
            for (float t = 0f; t < 600f; t += 0.05f)
            {
                samples++;
                if (SkySchedule.Glint(1408, StarField.GlintCount, t, out int i) > 0f) { visible++; seen.Add(i); }
            }
            float share = visible / (float)samples;
            Assert.That(share, Is.InRange(0.1f, 0.35f), "a glint somewhere now and then, the sky is mostly still");
            Assert.Greater(seen.Count, StarField.GlintCount / 2, "different glint stars flash");
            for (int i = 0; i < StarField.GlintCount; i++)
            {
                int on = 0;
                for (float t = 0f; t < 600f; t += 0.05f) if (SkySchedule.GlintLevel(1408, StarField.GlintCount, i, t) > 0f) on++;
                Assert.Less(on / (float)samples, 0.08f, "each glint star is invisible most of the time");
            }
        }

        [Test]
        public void MeteorShower_OneAtATime_NotTooBusy()
        {
            int meteors = 0, last = int.MinValue;
            for (float t = 0f; t < 600f; t += 0.02f)
                if (SkySchedule.Meteor(1408, t, out var m))
                {
                    if (m.Slot != last) { meteors++; last = m.Slot; }
                    Assert.That(m.Head.x, Is.InRange(-150f, 800f));
                    Assert.Greater(m.Dir.y, -0.75f, "shallow streaks");
                    Assert.Less(m.Dir.x, 0f, "one radiant: every meteor falls down-left");
                }
            float every = 600f / Mathf.Max(1, meteors);
            Assert.That(every, Is.InRange(3.5f, 9f), "about one meteor every ~5 s");
        }

        [Test]
        public void WishHead_FadesInAndOut()
        {
            Assert.AreEqual(0f, SkySchedule.WishHeadLevel(0f, -1f));
            Assert.AreEqual(1f, SkySchedule.WishHeadLevel(1f, -1f));
            Assert.AreEqual(1f, SkySchedule.WishHeadLevel(2f, 2f));
            Assert.AreEqual(0f, SkySchedule.WishHeadLevel(2f + SkySchedule.WishFadeOut, 2f));
            Assert.AreEqual(0f, SkySchedule.SparkleLevel(SkySchedule.SparkleLife));
            Assert.Greater(SkySchedule.BurstOffset(0.5f, 3).magnitude, 5f, "burst sparks fly out");
        }
    }
}
