using System.IO;
using FloorF.Audio;
using FloorF.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>
    /// Door sound: only the latch clunk of the PagDev recording (the motor/machinery run after it is trimmed off by
    /// DoorClipTrimmer), one door voice (a new door cue cuts the previous), and the clunk kept under the arrival ding.
    /// </summary>
    public class DoorAudioTests
    {
        static AudioLibrary LoadLibrary()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath + " (run FloorF.EditorTools.AudioLibraryBuilder.Build)");
            return lib;
        }

        [Test]
        public void DoorCues_UseTheTrimmedClunk_NotTheFullRecording()
        {
            var lib = LoadLibrary();
            foreach (var cue in new[] { AudioCue.DoorsOpen, AudioCue.DoorsClose })
            {
                var def = lib.Find(cue);
                Assert.IsNotNull(def, cue + " row");
                Assert.AreEqual(1, def.clips.Length, cue + ": one clip");
                var clip = def.clips[0];
                Assert.IsNotNull(clip, cue + " clip");
                Assert.AreEqual("562714__pagdev__elevator_door_clunk", clip.name, cue + " = trimmed clunk");
                Assert.That(clip.length, Is.InRange(0.5f, 0.65f), cue + ": clunk only (source is 3.58 s with the motor run)");
                Assert.IsNull(def.layer, cue + ": no layer (layers go to the shared pool)");
                Assert.That(def.volume, Is.InRange(0.3f, 0.5f), cue + ": clunk kept at the old clunk level (normalized import is +3.8 dB)");
            }
            Assert.Less(lib.Find(AudioCue.DoorsClose).pitch, 1f, "close = clunk pitched down");
            Assert.Less(lib.Find(AudioCue.DoorsClose).volume, lib.Find(AudioCue.DoorsOpen).volume, "close quieter");
            Assert.That(lib.doorCutFadeSeconds, Is.InRange(0.01f, 0.05f), "very short cut fade");
            Assert.That(lib.dingGuardSeconds, Is.InRange(0.4f, 0.8f), "covers the ping's loud sustain");
            Assert.That(lib.doorUnderDingDb, Is.InRange(-9f, -3f), "door under the ding");
        }

        [Test]
        public void ClunkFile_IsTheSourceCutAt568ms_WithSilentEdges()
        {
            Assert.IsTrue(DoorClipTrimmer.TryReadWav(File.ReadAllBytes(Path.GetFullPath(DoorClipTrimmer.SourcePath)),
                out var src, out int sch, out int srate, out string e1), e1);
            Assert.IsTrue(DoorClipTrimmer.TryReadWav(File.ReadAllBytes(Path.GetFullPath(DoorClipTrimmer.ClunkPath)),
                out var cut, out int ch, out int rate, out string e2), e2);
            Assert.AreEqual(sch, ch);
            Assert.AreEqual(srate, rate);
            int frames = cut.Length / ch;
            Assert.AreEqual(DoorClipTrimmer.CutEndSeconds, frames / (float)rate, 0.001f, "length");
            Assert.Greater(src.Length / sch / (float)srate, 3.5f, "source still the full 3.58 s recording");
            for (int c = 0; c < ch; c++)
            {
                Assert.AreEqual(0f, cut[c], 1e-4f, "first frame silent (the source starts mid-waveform)");
                Assert.AreEqual(0f, cut[(frames - 1) * ch + c], 1e-4f, "last frame silent (no click at the cut)");
            }
            // the body (between the fades) is the source itself, up to 16-bit rounding
            int a = Mathf.CeilToInt(DoorClipTrimmer.FadeInSeconds * rate) + 1;
            int b = frames - Mathf.CeilToInt(DoorClipTrimmer.FadeOutSeconds * rate) - 1;
            for (int f = a; f < b; f += 37)
                for (int c = 0; c < ch; c++)
                    Assert.AreEqual(src[f * ch + c], cut[f * ch + c], 1f / 16000f, "frame " + f);
        }

        [Test]
        public void Trim_KeepsTheHead_FadesBothEndsToZero()
        {
            const int rate = 1000, ch = 2;
            var data = new float[3000 * ch];
            for (int i = 0; i < data.Length; i++) data[i] = 0.5f;
            var o = DoorClipTrimmer.Trim(data, ch, rate, 1f, 0.01f, 0.02f);
            Assert.AreEqual(1000 * ch, o.Length, "0..1 s kept");
            Assert.AreEqual(0f, o[0], 1e-6f);
            Assert.AreEqual(0f, o[o.Length - 1], 1e-6f);
            Assert.AreEqual(0.5f, o[500 * ch], 1e-6f, "body untouched");
            Assert.AreEqual(0.5f, o[10 * ch], 1e-6f, "fade-in over 10 frames only");
            Assert.AreEqual(0.5f, o[(1000 - 21) * ch], 1e-6f, "fade-out over the last 20 frames only");
            for (int f = 1000 - 20; f < 1000; f++)
                Assert.LessOrEqual(o[f * ch], o[(f - 1) * ch] + 1e-6f, "fade-out falls monotonically");
            Assert.AreEqual(o[0], o[1], 1e-6f, "channels faded together");
            // too-long end is clamped to the input
            Assert.AreEqual(data.Length, DoorClipTrimmer.Trim(data, ch, rate, 10f, 0f, 0f).Length);
        }

        [Test]
        public void Wav16_RoundTrips()
        {
            var data = new[] { 0f, 0.25f, -0.25f, 0.999f, -1f, 0.5f };
            var bytes = DoorClipTrimmer.WriteWav16(data, 2, 22050);
            Assert.AreEqual(44 + data.Length * 2, bytes.Length);
            Assert.IsTrue(DoorClipTrimmer.TryReadWav(bytes, out var back, out int ch, out int rate, out string err), err);
            Assert.AreEqual(2, ch);
            Assert.AreEqual(22050, rate);
            Assert.AreEqual(data.Length, back.Length);
            for (int i = 0; i < data.Length; i++) Assert.AreEqual(data[i], back[i], 1f / 16000f, "sample " + i);
            Assert.IsFalse(DoorClipTrimmer.TryReadWav(new byte[] { 1, 2, 3 }, out _, out _, out _, out _), "garbage rejected");
        }

        [Test]
        public void DoorRules_OneVoice_DuckUnderDing_LinearCutFade()
        {
            Assert.IsTrue(AudioRules.IsDoorCue(AudioCue.DoorsOpen));
            Assert.IsTrue(AudioRules.IsDoorCue(AudioCue.DoorsClose));
            Assert.IsFalse(AudioRules.IsDoorCue(AudioCue.Ding), "the ding never shares (or is cut by) the door voice");
            Assert.IsFalse(AudioRules.IsDoorCue(AudioCue.None));

            Assert.AreEqual(1f, AudioRules.DoorGainAfterDing(float.PositiveInfinity, 0.6f, -6f), 1e-6f, "no ding yet");
            Assert.AreEqual(0.501f, AudioRules.DoorGainAfterDing(0f, 0.6f, -6f), 0.002f, "same frame as the ding");
            Assert.AreEqual(0.501f, AudioRules.DoorGainAfterDing(0.35f, 0.6f, -6f), 0.002f, "doors open 0.35 s after arrival");
            Assert.AreEqual(1f, AudioRules.DoorGainAfterDing(0.6f, 0.6f, -6f), 1e-6f, "after the guard");
            Assert.AreEqual(1f, AudioRules.DoorGainAfterDing(-1f, 0.6f, -6f), 1e-6f, "ding in the future = none");

            Assert.AreEqual(0.5f, AudioRules.StepCutFade(1f, 0.015f, 0.03f), 1e-5f);
            Assert.AreEqual(0f, AudioRules.StepCutFade(0.5f, 0.05f, 0.03f), 1e-6f, "clamped at 0");
            Assert.AreEqual(0f, AudioRules.StepCutFade(1f, 0.001f, 0f), 1e-6f, "no fade = immediate");
        }
    }
}
