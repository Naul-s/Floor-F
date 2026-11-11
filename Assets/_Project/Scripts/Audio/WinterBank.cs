using System;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>
    /// The winter-season sound table (AudioLibrary's procedural counterpart, see <see cref="WinterSynth"/>): cue rows,
    /// the two ending tracks, the wind loop and the Christmas bell layer. Clips are synthesized at runtime ONCE, on first
    /// use or by <see cref="PrewarmOne"/> (AudioRoot runs one per frame after the season is extended, before any winter
    /// day plays), and cached for the session (reset on SubsystemRegistration, domain reload is off).
    /// Levels are set from the measured file level: each row states how loud it PLAYS (dBFS, loudest 100 ms window =
    /// file x volume; the same yardstick as AudioLibraryBuilder's comments), so the balance with the imported SFX holds
    /// whatever the synth amplitudes are.
    /// </summary>
    static class WinterBank
    {
        // As played (max100ms dBFS). References (AudioLibraryBuilder): ding -17.8, door -17.7, Lucky twinkle -24.8,
        // wish chime -18.5, charm bell -20.1, shield chime -18.7, wish-star shimmer -23.2, music max100ms -27.2.
        /// <summary>Santa's sleigh shake: bright and busy - a little under the wish-star shimmer.</summary>
        public const float SleighDb = -24f;
        /// <summary>Carolers' handbells: like the charm bell.</summary>
        public const float CarolDb = -21f;
        /// <summary>Lottery reveal (roll + ding): the event of the night - just under the arrival ding.</summary>
        public const float LotteryDrawDb = -19.5f;
        /// <summary>Jackpot: flashier than a tip, as loud as the ding (it plays before that guest's tip).</summary>
        public const float JackpotDb = -18f;
        /// <summary>Winter-season stinger on the ending screen: like the wish chime.</summary>
        public const float WinterStartDb = -19f;
        /// <summary>Winter wind at full snow, rms (dBFS) as played: a breath under the rain bed (-38.3), far under the music.</summary>
        public const float WindRmsDb = -40f;
        /// <summary>Christmas bell layer (max100ms as played at full music level): ~9 dB under the song it rides on.</summary>
        public const float BellsLayerDb = -36.5f;
        /// <summary>
        /// Ending tracks, max100ms as played (file x musicVolume x track volume): the warm bells sit with the elevator
        /// song (-27.2); the music box is a "silent night" - softer.
        /// </summary>
        public const float MiracleTrackDb = -27f;
        public const float MusicBoxTrackDb = -30f;
        /// <summary>The music bus level the track volumes are computed for (AudioLibraryBuilder sets musicVolume 0.45).</summary>
        public const float AssumedMusicBus = 0.45f;

        public const float WindFadeInSeconds = 3f;
        public const float WindFadeOutSeconds = 2.5f;
        public const float BellsFadeSeconds = 1.5f;

        sealed class Row
        {
            public AudioCue Cue;
            public MusicTrack Track;
            public string Name;
            public int Rate;
            public Func<float[]> Synth;
            public float TargetDb;
            public bool Rms;
            public float Pitch = 1f, Jitter = 0.05f, MinInterval = 0.3f;
            // generated
            public AudioClip Clip;
            public float Volume;
            public WinterSynth.Levels FileLevels;
            public CueDef Def;
            public TrackDef TrackDef;
        }

        static Row[] rows;
        static Row wind, bells;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { rows = null; wind = bells = null; }

        static void EnsureRows()
        {
            if (rows != null) return;
            int r = WinterSynth.Rate;
            rows = new[]
            {
                new Row { Cue = AudioCue.WinterStart, Name = "winter_season_stinger", Rate = r, Synth = WinterSynth.WinterStart, TargetDb = WinterStartDb, MinInterval = 2f },
                new Row { Cue = AudioCue.LotteryDraw, Name = "winter_lottery_draw", Rate = r, Synth = WinterSynth.LotteryDraw, TargetDb = LotteryDrawDb, MinInterval = 1f },
                // each party member wins separately (same moment): one shower
                new Row { Cue = AudioCue.LotteryJackpot, Name = "winter_lottery_jackpot", Rate = r, Synth = WinterSynth.Jackpot, TargetDb = JackpotDb, Jitter = 0.05f, MinInterval = 0.6f },
                new Row { Cue = AudioCue.SleighBells, Name = "winter_sleigh_shake", Rate = r, Synth = WinterSynth.SleighShake, TargetDb = SleighDb, Jitter = 0.06f, MinInterval = 0.8f },
                // a party of three boards one after the other: one run per party
                new Row { Cue = AudioCue.CarolChime, Name = "winter_carol_handbells", Rate = r, Synth = WinterSynth.CarolChime, TargetDb = CarolDb, MinInterval = 2f },
                new Row { Track = MusicTrack.WinterBells, Name = "winter_miracle_bells_loop", Rate = WinterSynth.MidRate, Synth = WinterSynth.WinterMiracleLoop, TargetDb = MiracleTrackDb },
                new Row { Track = MusicTrack.MusicBox, Name = "winter_silent_night_music_box", Rate = WinterSynth.LowRate, Synth = WinterSynth.SilentNightLoop, TargetDb = MusicBoxTrackDb },
            };
            wind = new Row { Name = "winter_wind_loop", Rate = WinterSynth.LowRate, Synth = WinterSynth.Wind, TargetDb = WindRmsDb, Rms = true };
            bells = new Row { Name = "winter_christmas_bells_bar", Rate = r, Synth = WinterSynth.ChristmasBellsBar, TargetDb = BellsLayerDb };
        }

        static void Generate(Row row)
        {
            if (row.Clip != null) return;
            var data = row.Synth();
            row.FileLevels = WinterSynth.Measure(data, row.Rate);
            float file = row.Rms ? row.FileLevels.RmsDb : row.FileLevels.Max100msDb;
            float vol = AudioRules.DbToLinear(row.TargetDb - file);
            if (row.Track != MusicTrack.None) vol /= AssumedMusicBus;
            row.Volume = Mathf.Min(1f, vol);
            var clip = AudioClip.Create(row.Name, data.Length, 1, row.Rate, false);
            clip.SetData(data, 0);
            row.Clip = clip;
            if (row.Cue != AudioCue.None)
                row.Def = new CueDef
                {
                    cue = row.Cue, clips = new[] { clip }, volume = row.Volume, pitch = row.Pitch,
                    pitchJitter = row.Jitter, minInterval = row.MinInterval,
                };
            if (row.Track != MusicTrack.None)
                row.TrackDef = new TrackDef { track = row.Track, clip = clip, volume = row.Volume, seamFade = 0f };
        }

        static Row RowFor(AudioCue cue)
        {
            EnsureRows();
            foreach (var r in rows) if (r.Cue == cue && cue != AudioCue.None) return r;
            return null;
        }

        static Row RowFor(MusicTrack track)
        {
            EnsureRows();
            foreach (var r in rows) if (r.Track == track && track != MusicTrack.None) return r;
            return null;
        }

        /// <summary>The cue is one of the procedural winter rows (no clip in the AudioLibrary asset).</summary>
        public static bool Has(AudioCue cue) => RowFor(cue) != null;
        public static bool Has(MusicTrack track) => RowFor(track) != null;

        /// <summary>The cue row, its clip synthesized on first use. Null for cues that are not winter rows.</summary>
        public static CueDef Find(AudioCue cue)
        {
            var r = RowFor(cue);
            if (r == null) return null;
            Generate(r);
            return r.Def;
        }

        public static TrackDef Find(MusicTrack track)
        {
            var r = RowFor(track);
            if (r == null) return null;
            Generate(r);
            return r.TrackDef;
        }

        /// <summary>Snow-day wind loop and its level at full snow (x MasterVolume x <see cref="WinterRules.WindLevel"/>).</summary>
        public static AudioClip WindLoop { get { EnsureRows(); Generate(wind); return wind.Clip; } }
        public static float WindVolume { get { EnsureRows(); Generate(wind); return wind.Volume; } }

        /// <summary>One bar of sleigh bells, looped in sync with the elevator song on Christmas Eve, and its layer level.</summary>
        public static AudioClip ChristmasBells { get { EnsureRows(); Generate(bells); return bells.Clip; } }
        public static float ChristmasBellsVolume { get { EnsureRows(); Generate(bells); return bells.Volume; } }

        /// <summary>The clip's file level (tests / tuning).</summary>
        internal static WinterSynth.Levels FileLevels(AudioClip clip)
        {
            EnsureRows();
            foreach (var r in rows) if (r.Clip == clip && clip != null) return r.FileLevels;
            if (wind.Clip == clip && clip != null) return wind.FileLevels;
            if (bells.Clip == clip && clip != null) return bells.FileLevels;
            return default;
        }

        /// <summary>Clips not synthesized yet.</summary>
        public static int Pending
        {
            get
            {
                EnsureRows();
                int n = 0;
                foreach (var r in rows) if (r.Clip == null) n++;
                if (wind.Clip == null) n++;
                if (bells.Clip == null) n++;
                return n;
            }
        }

        /// <summary>
        /// Synthesizes the next missing clip (the season stinger first - it plays right away). False when all are cached.
        /// Spread over frames so no single frame pays for all of them.
        /// </summary>
        public static bool PrewarmOne()
        {
            EnsureRows();
            foreach (var r in rows) if (r.Clip == null) { Generate(r); return true; }
            if (bells.Clip == null) { Generate(bells); return true; }
            if (wind.Clip == null) { Generate(wind); return true; }
            return false;
        }
    }
}
