using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Rendering
{
    /// <summary>Chimney smoke on the hotel roof (pure schedule): a continuous, rising, wind-leaning stream that never flashes.</summary>
    public class SmokeScheduleTests
    {
        const int Seed = 1408;

        [Test]
        public void Smoke_IsDeterministic_Rises_LeansWithTheWind_AndFades()
        {
            for (int i = 0; i < SmokeSchedule.Puffs; i++)
            {
                var a = SmokeSchedule.At(Seed, i, 31.7f);
                var b = SmokeSchedule.At(Seed, i, 31.7f);
                Assert.AreEqual(a.Offset, b.Offset, "same seed + time = same puff");
                Assert.AreEqual(a.Level, b.Level);
                Assert.That(a.Level, Is.InRange(0f, 1f));
                Assert.That(a.Size, Is.InRange(1, 3));
                Assert.GreaterOrEqual(a.Offset.y, 1f, "above the mouth");
            }
            // one puff over its life: rises, drifts left (wind from the right, like the snow), fades out
            float life = SmokeSchedule.Life;
            var young = SmokeSchedule.At(Seed, 0, 0.6f);
            var old = SmokeSchedule.At(Seed, 0, 0.6f + life * 0.7f);
            Assert.Greater(old.Offset.y, young.Offset.y + 8f, "rises");
            Assert.Less(old.Offset.x, young.Offset.x - 4f, "leans left");
            Assert.Less(old.Level, young.Level, "fades as it rises");
            Assert.GreaterOrEqual(old.Size, young.Size, "spreads");
        }

        [Test]
        public void Smoke_IsAContinuousStream_WithSmoothLevels()
        {
            // at any time most puffs are visible (a stream, not bursts)
            for (float t = 0f; t < 30f; t += 0.37f)
            {
                int visible = 0;
                for (int i = 0; i < SmokeSchedule.Puffs; i++) if (SmokeSchedule.At(Seed, i, t).Level > 0.05f) visible++;
                Assert.GreaterOrEqual(visible, SmokeSchedule.Puffs / 2, "stream at t=" + t);
            }
            // no puff ever pops: per-frame level changes stay small except the wrap at the end of a life (level ~0 there)
            for (int i = 0; i < SmokeSchedule.Puffs; i++)
            {
                float prev = SmokeSchedule.At(Seed, i, 0f).Level;
                for (float t = 1f / 60f; t < 20f; t += 1f / 60f)
                {
                    float l = SmokeSchedule.At(Seed, i, t).Level;
                    Assert.Less(Mathf.Abs(l - prev), 0.06f, "puff " + i + " at " + t);
                    prev = l;
                }
            }
        }
    }
}
