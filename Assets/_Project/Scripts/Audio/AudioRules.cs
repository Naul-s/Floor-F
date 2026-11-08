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
            new Entry(typeof(LuckStreakChanged), AudioCue.StreakUp),
            new Entry(typeof(LuckStreakChanged), AudioCue.StreakBroken),
            new Entry(typeof(WishStarAppeared), AudioCue.WishStar),
            new Entry(typeof(WishMade), AudioCue.Wish),
            new Entry(typeof(LuckJudged), AudioCue.StarSparkle),
        };

        /// <summary>Star night: a Lucky judgement also gets the quiet star sparkle layer.</summary>
        public static AudioCue StarNightLayer(AudioCue judged, bool starNight)
            => starNight && judged == AudioCue.Lucky ? AudioCue.StarSparkle : AudioCue.None;

        /// <summary>Streak went up -> StreakUp, broken (back to 0 from above 0) -> StreakBroken, anything else silent.</summary>
        public static AudioCue For(in LuckStreakChanged e)
            => e.Broken ? AudioCue.StreakBroken : e.Streak > e.Previous && e.Streak > 0 ? AudioCue.StreakUp : AudioCue.None;

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

        /// <summary>Cues that play on the single door voice (a new one cuts the previous, never stacks).</summary>
        public static bool IsDoorCue(AudioCue cue) => cue == AudioCue.DoorsOpen || cue == AudioCue.DoorsClose;

        /// <summary>
        /// Door gain right after the arrival ding: <paramref name="underDingDb"/> while the door cue starts within
        /// <paramref name="guardSeconds"/> after the ding (its loud sustain), 1 otherwise / before any ding.
        /// </summary>
        public static float DoorGainAfterDing(float sinceDing, float guardSeconds, float underDingDb)
            => sinceDing >= 0f && sinceDing < guardSeconds ? DbToLinear(underDingDb) : 1f;

        /// <summary>Linear cut fade: gain one <paramref name="dt"/> step later (0 = done), never above 1.</summary>
        public static float StepCutFade(float gain, float dt, float fadeSeconds)
            => fadeSeconds <= 0f ? 0f : Mathf.Clamp01(gain - dt / fadeSeconds);

        public static float SemitonesToPitch(float semitones) => Mathf.Pow(2f, semitones / 12f);

        /// <summary>StreakUp pitch: streak 1 = base, +<paramref name="step"/> semitones per step after that, capped at <paramref name="maxSemitones"/>.</summary>
        public static float StreakPitch(int streak, float step, float maxSemitones)
            => SemitonesToPitch(Mathf.Clamp((streak - 1) * step, 0f, Mathf.Max(0f, maxSemitones)));

        /// <summary>ClockTick pitch for the tick at <paramref name="second"/> s left: 1 at the window start, 1 + rise at 1 s left.</summary>
        public static float ClockTickPitch(int second, int window, float rise)
        {
            if (window <= 1) return 1f + rise;
            return 1f + rise * Mathf.Clamp01((window - second) / (float)(window - 1));
        }

        /// <summary>Music duck target: menu pause wins over the lighter Day 0 coach-mark hold.</summary>
        public static float DuckTarget(bool paused, bool held, float pauseDb, float holdDb)
            => paused ? DbToLinear(pauseDb) : held ? DbToLinear(holdDb) : 1f;

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

    /// <summary>
    /// Last-seconds countdown: reports each whole second (N..1) once as the day clock crosses it, only while the visible
    /// phase is Running. A second crossed while silent (paused / tutorial) is skipped, never played late. Re-arms when the
    /// clock is outside the window or the phase is not Running (next day).
    /// </summary>
    sealed class CountdownTicker
    {
        int last = int.MaxValue;

        public void Reset() => last = int.MaxValue;

        /// <summary>The second to tick now (1..<paramref name="window"/>), or 0.</summary>
        public int Step(bool running, bool silent, float remaining, int window)
        {
            if (!running || window <= 0 || remaining > window) { last = int.MaxValue; return 0; }
            int sec = Mathf.CeilToInt(remaining);
            if (sec < 1 || sec >= last) return 0;
            last = sec; // a second crossed while silent is used up, not played late
            return silent ? 0 : sec;
        }
    }

    /// <summary>Travel-hum tuning (copied from <see cref="AudioLibrary"/> so the rules stay testable without the asset).</summary>
    readonly struct TravelHumSettings
    {
        public readonly float FadeIn, FadeOut, PitchStart, SpinUp, PitchArrive, SpinDown;
        public TravelHumSettings(float fadeIn, float fadeOut, float pitchStart, float spinUp, float pitchArrive, float spinDown)
        { FadeIn = fadeIn; FadeOut = fadeOut; PitchStart = pitchStart; SpinUp = spinUp; PitchArrive = pitchArrive; SpinDown = spinDown; }

        public static TravelHumSettings From(AudioLibrary lib)
            => new TravelHumSettings(lib.travelFadeInSeconds, lib.travelFadeOutSeconds, lib.travelPitchStart,
                lib.travelSpinUpSeconds, lib.travelPitchArrive, lib.travelSpinDownSeconds);
    }

    /// <summary>
    /// The elevator travel hum's state: ONE voice. <see cref="Depart"/> (ElevatorDeparted) fades it in, it fades out so it
    /// is silent at the predicted arrival (<c>eta</c> = seconds to arrival, &lt; 0 = unknown) or, without a prediction,
    /// after <see cref="Arrive"/>. A departure while it still sounds just keeps it going (no restart, no second voice).
    /// Pitch: spin-up from PitchStart to 1 after departure, dip to PitchArrive over the last SpinDown seconds.
    /// The caller does not Step it while paused/held (the voice is paused, the state frozen).
    /// </summary>
    sealed class TravelHum
    {
        /// <summary>The car is travelling (between ElevatorDeparted and ElevatorArrived).</summary>
        public bool Moving { get; private set; }
        /// <summary>0..1 fade gain.</summary>
        public float Gain { get; private set; }
        public float Pitch { get; private set; } = 1f;
        /// <summary>Seconds since the departure that started the current sound.</summary>
        public float SinceDepart { get; private set; }
        /// <summary>The voice should be playing (fading in, on, or fading out).</summary>
        public bool Active => Gain > 0f || target > 0f;

        float target;

        public void Depart()
        {
            if (!Active) SinceDepart = 0f;
            Moving = true;
        }

        public void Arrive() => Moving = false;

        /// <summary>Immediate silence (scene/phase reset of the hum).</summary>
        public void Kill()
        {
            Moving = false;
            Gain = 0f;
            target = 0f;
            Pitch = 1f;
        }

        public void Step(float dt, float eta, bool audible, in TravelHumSettings s)
        {
            if (dt < 0f) dt = 0f;
            if (Moving) SinceDepart += dt;
            target = audible ? TargetGain(Moving, eta, s.FadeOut) : 0f;
            Gain = StepGain(Gain, target, dt, s.FadeIn, s.FadeOut);
            Pitch = PitchAt(SinceDepart, Moving ? eta : 0f, s);
        }

        /// <summary>1 while travelling, 0 once the predicted arrival is within the fade-out (or after arrival).</summary>
        public static float TargetGain(bool moving, float eta, float fadeOut)
            => moving && !(eta >= 0f && eta <= fadeOut) ? 1f : 0f;

        /// <summary>Linear fade: up at 1/fadeIn per second, down at 1/fadeOut (0 s = jump).</summary>
        public static float StepGain(float gain, float target, float dt, float fadeIn, float fadeOut)
        {
            float len = target > gain ? fadeIn : fadeOut;
            return len <= 0f ? target : Mathf.MoveTowards(gain, target, dt / len);
        }

        public static float PitchAt(float sinceDepart, float eta, in TravelHumSettings s)
        {
            float up = s.SpinUp <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, sinceDepart / s.SpinUp);
            float p = Mathf.Lerp(s.PitchStart, 1f, up);
            if (eta >= 0f && s.SpinDown > 0f && eta < s.SpinDown)
                p *= Mathf.Lerp(s.PitchArrive, 1f, Mathf.SmoothStep(0f, 1f, eta / s.SpinDown));
            return p;
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
