using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>
    /// Winter season schedules (WinterSchedule): snowfall scales with SnowAmount and stays cheap, flakes drift and sway
    /// deterministically, Christmas bulbs / the lottery highlight are slow fades (photosensitivity, WCAG 2.3.1), Silent
    /// Night switches the floors off top-down keeping the lobby, Santa's sleigh crosses the moon.
    /// </summary>
    public class WinterTests
    {
        const int Seed = 1408;

        [Test]
        public void FlakeCount_ScalesWithTheSnowAmount_AndStaysCheap()
        {
            Assert.AreEqual(0, WinterSchedule.FlakeCount(0f));
            Assert.AreEqual(0, WinterSchedule.FlakeCount(-1f));
            int first = WinterSchedule.FlakeCount(0.3f), eve = WinterSchedule.FlakeCount(1f);
            Assert.Greater(first, 20, "first snow is visible");
            Assert.Less(first, eve / 2, "first snow is light");
            Assert.AreEqual(WinterSchedule.MaxFlakes, eve);
            Assert.AreEqual(eve, WinterSchedule.FlakeCount(5f), "clamped");
            Assert.LessOrEqual(WinterSchedule.MaxFlakes, 300, "WebGL: a few hundred quads at most");
        }

        [Test]
        public void Flakes_FallDriftAndSway_Deterministically_InsideTheView()
        {
            int bigs = 0;
            for (int i = 0; i < WinterSchedule.MaxFlakes; i++)
            {
                var a = WinterSchedule.SnowFlake(Seed, i, 10f);
                var again = WinterSchedule.SnowFlake(Seed, i, 10f);
                Assert.AreEqual(a.Pos, again.Pos, "pure function of (seed, index, time)");
                Assert.That(a.Pos.x, Is.InRange(-12f, StarField.Width + 12f));
                Assert.That(a.Pos.y, Is.InRange(-8f, StarField.Height + 4f));
                Assert.That(a.Size, Is.InRange(1, 2), "chunky pixel flakes");
                if (a.Size == 2) bigs++;
                var b = WinterSchedule.SnowFlake(Seed, i, 10.2f);
                if (b.Pos.y < a.Pos.y + 1f) // not wrapped
                    Assert.Less(b.Pos.y, a.Pos.y, "falls");
            }
            Assert.That(bigs, Is.InRange(WinterSchedule.MaxFlakes / 5, WinterSchedule.MaxFlakes / 2), "a good share of near (2x2) flakes, the rest single pixels");

            // calm (Silent Night) falls slower
            float fast = 0f, slow = 0f;
            for (int i = 0; i < 40; i++)
            {
                fast += Mathf.Abs(Fall(i, 0f));
                slow += Mathf.Abs(Fall(i, 1f));
            }
            Assert.Less(slow, fast * 0.8f);
        }

        static float Fall(int i, float calm)
        {
            float y0 = WinterSchedule.SnowFlake(Seed, i, 3f, calm).Pos.y, y1 = WinterSchedule.SnowFlake(Seed, i, 3.1f, calm).Pos.y;
            return y1 < y0 ? y0 - y1 : 0f;
        }

        [Test]
        public void ChristmasBulbs_AndLotteryHighlight_AreSlowFades_NeverFlashes()
        {
            for (int g = 0; g < WinterSchedule.BulbGroups; g++)
            {
                int group = g;
                var times = PhotosensitivityTests.Transitions(t => WinterSchedule.BulbLevel(group, t), 0f, 240f);
                PhotosensitivityTests.AssertAtMostThreePerSecond(times, "bulb group " + g);
                Assert.Greater(times.Count, 20, "the bulbs do breathe");
                for (float t = 0f; t < 30f; t += 1f / 60f)
                {
                    float a = WinterSchedule.BulbLevel(g, t), b = WinterSchedule.BulbLevel(g, t + 1f / 60f);
                    Assert.Less(Mathf.Abs(b - a), 0.03f, "smooth: no jump between two frames");
                    Assert.GreaterOrEqual(a, WinterSchedule.BulbLow - 1e-4f, "never off");
                }
            }
            Assert.AreNotEqual(WinterSchedule.BulbLevel(0, 1f), WinterSchedule.BulbLevel(2, 1f), "groups out of phase");
            PhotosensitivityTests.AssertAtMostThreePerSecond(PhotosensitivityTests.Transitions(WinterSchedule.HighlightLevel, 0f, 240f), "lottery highlight");
            PhotosensitivityTests.AssertAtMostThreePerSecond(PhotosensitivityTests.Transitions(WinterSchedule.StarLevel, 0f, 240f), "tree star");
            for (float t = 0f; t < 20f; t += 0.05f) Assert.GreaterOrEqual(WinterSchedule.HighlightLevel(t), 0.5f, "the highlight never goes out");
        }

        [Test]
        public void SilentNight_FloorsGoDarkTopDown_TheLobbyStays()
        {
            const int n = 9;
            Assert.AreEqual(0, WinterSchedule.LightsOutCount(0f, n));
            int last = 0;
            for (float t = 0f; t < 60f; t += 0.1f)
            {
                int c = WinterSchedule.LightsOutCount(t, n);
                Assert.GreaterOrEqual(c, last, "monotonic");
                Assert.LessOrEqual(c - last, 1, "one floor at a time");
                last = c;
            }
            Assert.AreEqual(n - 1, last, "all but the lobby");
            Assert.AreEqual(n - 1, WinterSchedule.LightsOutFloor(0, n), "top floor first");
            Assert.AreEqual(1, WinterSchedule.LightsOutFloor(n - 2, n), "floor 2 last");
            Assert.Greater(WinterSchedule.LightsOutStep, 1f / 3f, "never faster than 3 changes per second");
        }

        [Test]
        public void Sleigh_CrossesTheMoon_ThenRests()
        {
            var moon = new Vector2Int(Mathf.RoundToInt(SkyView.MoonViewport.x * StarField.Width), Mathf.RoundToInt(SkyView.MoonViewport.y * StarField.Height));
            Assert.IsFalse(WinterSchedule.Sleigh(0f, moon, out _, out _), "a beat before the first flight");
            float best = float.MaxValue;
            for (float t = 0f; t < WinterSchedule.SleighPeriod; t += 0.02f)
            {
                if (!WinterSchedule.Sleigh(t, moon, out var at, out _)) continue;
                Assert.That(at.y, Is.InRange(200f, StarField.Height), "high in the sky");
                best = Mathf.Min(best, Vector2.Distance(at, moon));
            }
            Assert.Less(best, 6f, "passes in front of the moon's disc");
            Assert.IsFalse(WinterSchedule.Sleigh(WinterSchedule.SleighDelay + WinterSchedule.SleighSeconds + 0.5f, moon, out _, out _), "rests between flights");
        }

        [Test]
        public void LotteryDraw_SpinsUp_AndStops()
        {
            Assert.AreEqual(0f, WinterSchedule.DrumSpeed(-1f));
            Assert.Greater(WinterSchedule.DrumSpeed(WinterSchedule.DrawSeconds * 0.5f), 200f);
            Assert.AreEqual(0f, WinterSchedule.DrumSpeed(WinterSchedule.DrawSeconds));
            Assert.Less(WinterSchedule.DropAt, WinterSchedule.DrawSeconds);
        }
    }
}
