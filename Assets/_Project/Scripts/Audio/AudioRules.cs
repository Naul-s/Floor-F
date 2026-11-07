using System;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>
    /// Which Contracts event plays which cue. The handlers in <see cref="AudioRoot"/> call these functions, and
    /// <see cref="Mapped"/> lists every (event, cue) pair so the EditMode table test can check each has a clip.
    /// </summary>
    static class AudioEventMap
    {
        internal readonly struct Entry
        {
            public readonly Type Event;
            public readonly AudioCue Cue;
            public Entry(Type evt, AudioCue cue) { Event = evt; Cue = cue; }
            public override string ToString() => Event.Name + " -> " + Cue;
        }

        internal static readonly Entry[] Mapped =
        {
            new Entry(typeof(ElevatorArrived), AudioCue.Ding),
            new Entry(typeof(DoorsOpened), AudioCue.DoorsOpen),
            new Entry(typeof(DoorsClosed), AudioCue.DoorsClose),
            new Entry(typeof(LuckJudged), AudioCue.Lucky),
            new Entry(typeof(LuckJudged), AudioCue.Unlucky),
            new Entry(typeof(TipsChanged), AudioCue.Tip),
            new Entry(typeof(GuestDelivered), AudioCue.Tip),
            new Entry(typeof(StickerApplied), AudioCue.Sticker),
            new Entry(typeof(GuestLeft), AudioCue.GuestLeft),
            new Entry(typeof(GuestMoodChanged), AudioCue.Impatient),
            new Entry(typeof(DayStarted), AudioCue.DayStart),
        };

        public static AudioCue For(in LuckJudged e)
            => e.Verdict == LuckVerdict.Lucky ? AudioCue.Lucky : e.Verdict == LuckVerdict.Unlucky ? AudioCue.Unlucky : AudioCue.None;

        public static AudioCue For(in TipsChanged e) => e.Delta > 0 ? AudioCue.Tip : AudioCue.None;

        /// <summary>Day-end sweeps (everyone still around leaves) are silent; real walk-outs bong.</summary>
        public static AudioCue For(in GuestLeft e) => e.Reason == GuestLeaveReason.DayEnded ? AudioCue.None : AudioCue.GuestLeft;

        public static AudioCue For(in GuestMoodChanged e)
            => e.Current == GuestMood.Impatient && e.Previous != GuestMood.Impatient ? AudioCue.Impatient : AudioCue.None;
    }

    /// <summary>Music for a moment of the game: which track, its level (x track volume) and base pitch.</summary>
    readonly struct MusicSpec
    {
        public readonly MusicTrack Track;
        public readonly float Volume;
        public readonly float Pitch;
        public MusicSpec(MusicTrack track, float volume, float pitch) { Track = track; Volume = volume; Pitch = pitch; }
        public override string ToString() => Track + " vol " + Volume + " pitch " + Pitch;
    }

    static class AudioRules
    {
        public const float MinJitter = 0.05f;
        public const float MaxJitter = 0.10f;

        /// <summary>basePitch x (1 +- jitter), uniform. Jitter is clamped to <see cref="MinJitter"/>..<see cref="MaxJitter"/>.</summary>
        public static float PickPitch(float basePitch, float jitter, System.Random rng)
        {
            float j = Mathf.Clamp(jitter, MinJitter, MaxJitter);
            return basePitch * (1f + j * (float)(rng.NextDouble() * 2.0 - 1.0));
        }

        public static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);

        /// <summary>0..1 gain that fades the first and last <paramref name="fade"/> seconds of a looping clip (hides the seam).</summary>
        public static float SeamGain(float time, float length, float fade)
        {
            if (fade <= 0f || length <= fade * 2f) return 1f;
            return Mathf.Clamp01(Mathf.Min(time / fade, (length - time) / fade));
        }

        /// <summary>GDD 8 music per phase / ending.</summary>
        public static MusicSpec MusicFor(DayPhase phase, EndingKind ending)
        {
            switch (phase)
            {
                case DayPhase.Summary: return new MusicSpec(MusicTrack.Elevator, 0.6f, 1f);
                case DayPhase.Shop: return new MusicSpec(MusicTrack.ShopLoop, 0.8f, 1f);
                case DayPhase.Ending:
                    switch (ending)
                    {
                        case EndingKind.FourthFloor: return new MusicSpec(MusicTrack.Mystery, 1f, 1f);
                        case EndingKind.Haunted: return new MusicSpec(MusicTrack.Mystery, 1f, 0.8f);
                        case EndingKind.Prosperous: return new MusicSpec(MusicTrack.Elevator, 1f, 1.06f);
                        case EndingKind.Modest: return new MusicSpec(MusicTrack.Elevator, 0.75f, 0.97f);
                        default: return new MusicSpec(MusicTrack.Elevator, 0.6f, 1f);
                    }
                default: return new MusicSpec(MusicTrack.Elevator, 1f, 1f); // None / Title / Intro / Running
            }
        }

        /// <summary>
        /// Phase changes only the player can cause (Start, Begin day, Continue, Done shopping, Play again) - each one
        /// proves a user gesture happened, so the WebGL audio lock can open even if the input probe is unavailable.
        /// </summary>
        public static bool IsPlayerDrivenTransition(DayPhase previous, DayPhase current)
        {
            if (previous == current) return false;
            switch (previous)
            {
                case DayPhase.Title:
                case DayPhase.Intro:
                case DayPhase.Summary:
                case DayPhase.Shop:
                case DayPhase.Ending:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Spam guard: a cue may not restart within its min interval (several guests judged in the same frame = one sound).</summary>
    sealed class CueThrottle
    {
        readonly float[] last;

        public CueThrottle()
        {
            last = new float[Enum.GetValues(typeof(AudioCue)).Length + 1];
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < last.Length; i++) last[i] = float.NegativeInfinity;
        }

        /// <summary>True (and remembers <paramref name="now"/>) when the cue may play.</summary>
        public bool TryConsume(AudioCue cue, float now, float minInterval)
        {
            int i = (int)cue;
            if (i < 0 || i >= last.Length) return true;
            if (now - last[i] < minInterval) return false;
            last[i] = now;
            return true;
        }
    }

    /// <summary>GDD 8 "unlucky floor": music pitch drops to <c>depth</c> over <c>down</c> s, then glides back over <c>up</c> s.</summary>
    struct PitchDip
    {
        float start;
        bool active;

        public bool Active => active;

        public void Trigger(float now) { start = now; active = true; }

        public void Clear() => active = false;

        /// <summary>Pitch multiplier at <paramref name="now"/> (1 when idle).</summary>
        public float Evaluate(float now, float depth, float down, float up)
        {
            if (!active) return 1f;
            float t = now - start;
            if (t < 0f) return 1f;
            if (t < down) return Mathf.Lerp(1f, depth, down <= 0f ? 1f : t / down);
            t -= down;
            if (t < up) return Mathf.Lerp(depth, 1f, Mathf.SmoothStep(0f, 1f, t / up));
            active = false;
            return 1f;
        }
    }
}
