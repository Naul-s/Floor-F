using System.Collections.Generic;
using FloorF.Audio;
using FloorF.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FloorF.Tests.EditMode.Audio
{
    /// <summary>
    /// Objective mix audit (<see cref="AudioMixAudit"/>, nobody can listen): every clip rendered at its played level -
    /// no clipping, no DC on our own clips, clean loop seams, and the winter cues as loud (K-weighted, "LUFS-ish") as the
    /// existing cues they sit next to. SFX compare momentary loudness (loudest 400 ms), music integrated / short-term.
    /// </summary>
    public class MixAuditTests
    {
        static List<AudioMixAudit.Row> rows;

        [OneTimeSetUp]
        public void Collect()
        {
            var lib = AudioLibrary.Load();
            Assert.IsNotNull(lib, "Resources/" + AudioLibrary.ResourcePath);
            rows = AudioMixAudit.Collect(lib);
            Debug.Log(AudioMixAudit.Format(rows));
        }

        static AudioMixAudit.Stats Get(string group, string name)
        {
            var r = AudioMixAudit.Find(rows, group, name);
            Assert.IsNotNull(r, group + "/" + name + " in the audit");
            Assert.AreNotEqual("unreadable", r.Source, group + "/" + name + " readable");
            return r.Played;
        }

        [Test]
        public void EveryClip_IsMeasured()
        {
            Assert.Greater(rows.Count, 40);
            foreach (var r in rows) Assert.AreNotEqual("unreadable", r.Source, r.Group + "/" + r.Name);
            Assert.AreEqual(7, rows.FindAll(r => r.Group == "winter").Count, "5 winter cues + wind + bell layer");
            Assert.AreEqual(6, rows.FindAll(r => r.Group == "ending").Count, "every ending has music");
        }

        [Test]
        public void NothingClips_OurClipsHaveNoDc()
        {
            foreach (var r in rows)
            {
                string n = r.Group + "/" + r.Name;
                Assert.Less(r.Played.PeakDb, -1f, n + ": played peak under -1 dBFS (no clipping)");
                bool ours = string.IsNullOrEmpty(r.Path) || r.Path.Contains("/Generated/"); // runtime winter clips + ProceduralSfxSynth
                if (!ours) continue;
                Assert.Less(r.File.PeakDb, -0.1f, n + ": generated file never hits full scale");
                if (string.IsNullOrEmpty(r.Path)) Assert.LessOrEqual(r.File.PeakDb, -2.9f, n + ": winter synth normalizes to -3 dBFS");
                Assert.Less(r.File.DcDb, -60f, n + ": no DC offset");
            }
            foreach (var r in rows.FindAll(x => x.Group == "winter"))
                Assert.Less(r.Played.PeakDb, -6f, r.Name + ": layered with other cues, keeps headroom");
        }

        [Test]
        public void Loops_HaveNoSeamClick_BedsNoLevelJump()
        {
            foreach (var r in rows)
            {
                if (!r.Loop || r.SeamFade > 0f) continue; // the game fades those seams (AudioRules.SeamGain)
                Assert.LessOrEqual(r.Played.SeamRatio, 1.5f, r.Group + "/" + r.Name + ": wrap-around step is an ordinary step");
            }
            foreach (var r in rows)
            {
                bool bed = r.Group == "bed" || r.Name.StartsWith("wind");
                if (!bed) continue;
                Assert.That(r.Played.SeamJumpDb, Is.InRange(-3f, 3f), r.Name + ": no level jump at the loop point");
            }
        }

        /// <summary>
        /// Imported music loops whose loop point steps (sample click, a level jump - e.g. the mystery loop's +5.9 dB - or
        /// sits on a DC offset) get the game's seam fade (AudioRules.SeamGain). The procedural winter loops start on a
        /// note by design and are wrapped seamlessly (no fade, checked above).
        /// </summary>
        [Test]
        public void ImportedMusicLoops_WithASteppedSeam_AreFaded()
        {
            int checkedLoops = 0;
            foreach (var r in rows)
            {
                if (!r.Loop || (r.Group != "music" && r.Group != "ending") || string.IsNullOrEmpty(r.Path)) continue;
                checkedLoops++;
                var f = r.File;
                bool stepped = f.SeamRatio > 1.5f || Mathf.Abs(f.SeamJumpDb) > 3f || f.DcDb > -50f;
                if (stepped) Assert.GreaterOrEqual(r.SeamFade, 0.1f, r.Group + "/" + r.Name + ": stepped seam must be faded");
            }
            Assert.Greater(checkedLoops, 3);
            var mystery = AudioMixAudit.Find(rows, "ending", "Haunted");
            Assert.IsNotNull(mystery);
            Assert.Greater(Mathf.Abs(mystery.File.SeamJumpDb), 3f, "the mystery loop's seam steps (why it is faded)");
            Assert.That(mystery.SeamFade, Is.InRange(0.1f, 0.3f), "short fade: a dip, not a gap");
        }

        [Test]
        public void WinterCues_AsLoudAsTheirNeighbours()
        {
            var sleigh = Get("winter", "SleighBells").MomentaryLufs;
            var star = Get("sfx", "WishStar").MomentaryLufs;
            Assert.That(sleigh - star, Is.InRange(-3f, 0f), "sleigh shake a little under the wish-star shimmer");

            var carol = Get("winter", "CarolChime").MomentaryLufs;
            var charm = Get("sfx", "CharmBought").MomentaryLufs;
            Assert.That(carol - charm, Is.InRange(-2f, 2f), "handbells like the charm bell");

            var ding = Get("sfx", "Ding").MomentaryLufs;
            var draw = Get("winter", "LotteryDraw").MomentaryLufs;
            Assert.That(draw - ding, Is.InRange(-4f, 0.5f), "lottery reveal just under the arrival ding");

            var jack = Get("winter", "LotteryJackpot").MomentaryLufs;
            Assert.That(jack - ding, Is.InRange(-1.5f, 1f), "jackpot as loud as the ding");
            foreach (var r in rows)
                if (r.Group == "sfx" && r.Name.StartsWith("Tip")) Assert.Greater(jack, r.Played.MomentaryLufs + 6f, "flashier than " + r.Name);

            var stinger = Get("winter", "WinterStart").MomentaryLufs;
            var wish = Get("sfx", "Wish").MomentaryLufs;
            Assert.That(stinger - wish, Is.InRange(-2f, 1f), "season stinger like the wish chime");

            var day = Get("sfx", "DayStart").MomentaryLufs;
            foreach (var r in rows.FindAll(x => x.Group == "winter" && !x.Loop))
                Assert.LessOrEqual(r.Played.MomentaryLufs, day + 0.5f, r.Name + ": not louder than the loudest existing cue");
        }

        [Test]
        public void WinterBeds_UnderTheMusic()
        {
            var song = Get("music", "Running Elevator");
            var bells = Get("winter", "christmas bells layer");
            Assert.That(song.ShortTermLufs - bells.ShortTermLufs, Is.InRange(7f, 11f), "bell layer ~9 LU under the song it rides on");
            var wind = Get("winter", "wind").IntegratedLufs;
            var rain = Get("bed", "rain loop").IntegratedLufs;
            Assert.That(wind - rain, Is.InRange(-4f, 0f), "wind a breath under the rain bed");
            Assert.Less(wind, song.IntegratedLufs - 9f, "far under the music");
        }

        [Test]
        public void EndingTracks_MatchTheOtherEndings()
        {
            var prosperous = Get("ending", "Prosperous").IntegratedLufs;
            var modest = Get("ending", "Modest").IntegratedLufs;
            var mystery = Get("ending", "Haunted").IntegratedLufs;
            var miracle = Get("ending", "WinterMiracle").IntegratedLufs;
            var box = Get("ending", "SilentNight").IntegratedLufs;
            Assert.That(miracle - prosperous, Is.InRange(-1.5f, 1.5f), "the warm bells sit with the Prosperous ending");
            Assert.That(miracle - box, Is.InRange(1.5f, 5f), "the music box is softer");
            Assert.GreaterOrEqual(box, modest - 1.5f, "...but not buried: about the Modest ending");
            Assert.LessOrEqual(miracle, mystery + 1f, "no winter ending louder than the mystery step-up");
            var song = Get("music", "Running Elevator");
            Assert.That(miracle - song.IntegratedLufs, Is.InRange(-2f, 2f), "no jump from the Christmas Eve music");
        }

        /// <summary>The music references come from a model of the importer (source + mono-normalize): it must match the readable clips.</summary>
        [Test]
        public void SourceModel_MatchesTheImportedSfx()
        {
            int n = 0;
            var lib = AudioLibrary.Load();
            foreach (var c in lib.cues)
                foreach (var clip in c.clips)
                {
                    string path = AssetDatabase.GetAssetPath(clip);
                    if (!path.Contains("/ThirdParty/") || clip.loadType != AudioClipLoadType.DecompressOnLoad) continue;
                    Assert.IsTrue(AudioMixAudit.TryMono(clip, out var a, out int ra, out _));
                    Assert.IsTrue(AudioMixAudit.TrySourceModel(path, out var b, out int rb, out _));
                    float imported = AudioMixAudit.Analyze(a, ra, 1f, false).Max100msDb;
                    float model = AudioMixAudit.Analyze(b, rb, 1f, false).Max100msDb;
                    Assert.AreEqual(imported, model, 1.5f, clip.name + ": imported level = source model");
                    n++;
                }
            Assert.Greater(n, 3);
            Assert.AreEqual(WinterAudioTests.ElevatorPlayedDb, Get("music", "Running Elevator").Max100msDb, 0.75f,
                "the elevator reference the winter levels use");
        }
    }
}
