using FloorF.Rendering;
using NUnit.Framework;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Seeded city/sign light schedules: deterministic, varied, slow, no strobing.</summary>
    public class LivingLightsTests
    {
        const int Seed = 1408;

        static string Pattern(int seed, float time)
        {
            var c = new char[200];
            for (int i = 0; i < c.Length; i++) c[i] = LightSchedule.WindowLit(seed, i, time, 0.36f) ? '#' : '.';
            return new string(c);
        }

        [Test]
        public void Windows_SameSeedSamePattern_DifferentSeedDiffers()
        {
            Assert.AreEqual(Pattern(Seed, 123.4f), Pattern(Seed, 123.4f));
            Assert.AreNotEqual(Pattern(Seed, 123.4f), Pattern(Seed + 1, 123.4f));
            Assert.AreNotEqual(Pattern(Seed, 10f), Pattern(Seed, 400f), "windows change over time");
        }

        [Test]
        public void Windows_LitShareNearOnRatio()
        {
            int lit = 0, n = 0;
            for (int i = 0; i < 300; i++)
                for (float t = 0f; t < 1200f; t += 37f, n++)
                    if (LightSchedule.WindowLit(Seed, i, t, 0.36f)) lit++;
            float share = (float)lit / n;
            Assert.That(share, Is.InRange(0.26f, 0.46f), "about a third of the windows lit");
        }

        [Test]
        public void Windows_NeverFasterThanTwoChangesPerSecond_AndMostlySteady()
        {
            const float dt = 0.02f;
            int totalChanges = 0;
            for (int i = 0; i < 120; i++)
            {
                bool prev = LightSchedule.WindowLit(Seed, i, 0f, 0.36f);
                var recent = new System.Collections.Generic.Queue<float>();
                for (float t = dt; t < 900f; t += dt)
                {
                    bool now = LightSchedule.WindowLit(Seed, i, t, 0.36f);
                    if (now == prev) continue;
                    prev = now;
                    totalChanges++;
                    recent.Enqueue(t);
                    while (recent.Count > 0 && t - recent.Peek() > 1f) recent.Dequeue();
                    Assert.LessOrEqual(recent.Count, 4, "window " + i + " at " + t + ": at most 4 changes (2 flashes) in any second");
                }
            }
            // 120 windows x 900 s: steady city, roughly one change per window per minute or slower
            Assert.Less(totalChanges, 120 * 900 / 30, "slow changes");
            Assert.Greater(totalChanges, 120, "but the city is alive");
        }

        [Test]
        public void Sign_LetterEvents_AreRare_OneLetterAtATime_AndGentle()
        {
            const float dt = 0.02f;
            int dimFrames = 0, frames = 0, changes = 0;
            var prev = new float[5];
            for (int l = 0; l < 5; l++) prev[l] = 1f;
            var recent = new System.Collections.Generic.Queue<float>();
            for (float t = 0f; t < 600f; t += dt, frames++)
            {
                int dimNow = 0;
                for (int l = 0; l < 5; l++)
                {
                    float v = LightSchedule.SignLetterLevel(Seed, l, 5, t);
                    Assert.That(v, Is.InRange(LightSchedule.SignDim, 1f));
                    if (v < 1f) dimNow++;
                    if (v != prev[l])
                    {
                        changes++;
                        recent.Enqueue(t);
                        while (recent.Count > 0 && t - recent.Peek() > 1f) recent.Dequeue();
                        Assert.LessOrEqual(recent.Count, 4, "no strobing at " + t);
                    }
                    prev[l] = v;
                }
                Assert.LessOrEqual(dimNow, 1, "one letter at a time");
                if (dimNow > 0) dimFrames++;
            }
            Assert.Greater(changes, 10, "letters do flicker now and then");
            Assert.Less((float)dimFrames / frames, 0.12f, "the sign is lit most of the time");
            Assert.AreEqual(LightSchedule.SignLetterLevel(Seed, 2, 5, 77.7f), LightSchedule.SignLetterLevel(Seed, 2, 5, 77.7f));
        }

        [Test]
        public void Bulbs_And_Beacon_AreSlow()
        {
            Assert.AreNotEqual(LightSchedule.BulbPhase(0.1f), LightSchedule.BulbPhase(0.1f + LightSchedule.BulbStep));
            Assert.GreaterOrEqual(LightSchedule.BulbStep, 0.5f, "marquee chase at most 1 swap per 0.5 s");
            int changes = 0;
            bool prev = LightSchedule.BeaconLit(0f);
            for (float t = 0.01f; t < 24f; t += 0.01f)
            {
                bool now = LightSchedule.BeaconLit(t);
                if (now != prev) changes++;
                prev = now;
            }
            Assert.LessOrEqual(changes, 21, "beacon blinks below 0.5 Hz");
        }

        [Test]
        public void Hash_IsUniformEnough()
        {
            int low = 0;
            for (int i = 0; i < 4000; i++) if (LightSchedule.Hash(Seed, i, 3, 7) < 0.5f) low++;
            Assert.That(low, Is.InRange(1800, 2200));
        }
    }
}
