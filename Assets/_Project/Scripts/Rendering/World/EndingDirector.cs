using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// GDD 7 ending lighting, driven by EndingReached (juice clock, so it holds while paused):
    ///   FourthFloor - every floor goes dark, then only the PHYSICAL 4th floor comes back cold and flickering, and its
    ///                 sticker drops off the sign to reveal the original "4".
    ///   Haunted     - all floors red and flickering (slow sags, single drop-outs, one shared wiring), HOTEL sign with them.
    ///   Prosperous  - warm gold wave lights every floor bottom-up brighter than normal, HOTEL sign blinks.
    ///   Modest      - unchanged.
    ///   WinterMiracle - "A Christmas Miracle": every floor warms up bottom-up (soft, no flash), steady warm HOTEL sign,
    ///                 Christmas dressing on, snow, and Santa's sleigh crossing the moon again and again (WinterView).
    ///   SilentNight - quiet slow snow, Christmas dressing on, the floors' lights fade out one by one from the top (the
    ///                 lobby with its tree stays lit), the HOTEL sign dims slowly.
    /// Reset() (restart / back to title) restores the normal look.
    /// </summary>
    sealed class EndingDirector
    {
        public const float DarkSeconds = 0.7f;
        public const float Floor4OnAt = 1.2f;
        public const float RevealAt = 1.9f;

        public static readonly Color ColdLight = new Color(0.55f, 0.82f, 1.00f);
        public static readonly Color HauntedLight = new Color(1.00f, 0.12f, 0.08f);
        public static readonly Color ProsperousLight = new Color(1.00f, 0.80f, 0.42f);
        public static readonly Color MiracleLight = new Color(1.00f, 0.74f, 0.46f);
        public static readonly Color MiracleSign = new Color(1.00f, 0.90f, 0.72f);
        /// <summary>Winter Miracle: seconds between two floors warming up (bottom-up).</summary>
        public const float MiracleStep = 0.3f;
        public const float SilentSignSeconds = 9f;

        /// <summary>Snow, sleigh (endings); null in tests that build the director alone.</summary>
        public WinterView Winter { get; set; }
        public ChristmasDecor Christmas { get; set; }
        static readonly Color RevealInk = new Color(0.75f, 0.95f, 1.00f);
        static readonly Color RevealPlate = new Color(0.03f, 0.04f, 0.06f);

        readonly FloorLighting lighting;
        readonly BuildingView building;
        readonly ElevatorView elevator;
        readonly JuiceFx fx;
        readonly System.Func<FloorIndex, FloorLabel> originalLabel;
        float t;
        bool floor4On, revealed;
        int wave;

        public EndingKind Active { get; private set; }
        public float Elapsed => t;
        public static FloorIndex RealFourth => FloorIndex.FromPhysicalNumber(4);

        public EndingDirector(FloorLighting lighting, BuildingView building, ElevatorView elevator, JuiceFx fx, System.Func<FloorIndex, FloorLabel> originalLabel)
        {
            this.lighting = lighting;
            this.building = building;
            this.elevator = elevator;
            this.fx = fx;
            this.originalLabel = originalLabel;
        }

        public void Begin(EndingKind kind)
        {
            Reset();
            Active = kind;
            t = 0f;
            int n = lighting.FloorCount;
            switch (kind)
            {
                case EndingKind.FourthFloor:
                    for (int i = 0; i < n; i++) lighting.SetLevel(new FloorIndex(i), 0f, 1f / DarkSeconds);
                    break;
                case EndingKind.Haunted:
                    for (int i = 0; i < n; i++)
                    {
                        var f = new FloorIndex(i);
                        lighting.SetBaseColor(f, HauntedLight);
                        lighting.SetFlicker(f, true);
                        lighting.SetLevel(f, 0.85f, 2f);
                    }
                    lighting.FlashAll(HauntedLight, 0.8f);
                    elevator.SetLightLevel(0.35f);
                    break;
                case EndingKind.Prosperous:
                    for (int i = 0; i < n; i++)
                    {
                        var f = new FloorIndex(i);
                        lighting.SetBaseColor(f, ProsperousLight);
                        lighting.SetFlicker(f, false);
                        lighting.SetLevel(f, 0.5f, 0f);
                    }
                    break;
                case EndingKind.WinterMiracle:
                    for (int i = 0; i < n; i++)
                    {
                        var f = new FloorIndex(i);
                        lighting.SetBaseColor(f, MiracleLight);
                        lighting.SetFlicker(f, false);
                        lighting.SetLevel(f, 0.7f, 1f);
                    }
                    SetWinter(0.7f, 0f, sleigh: true);
                    break;
                case EndingKind.SilentNight:
                    for (int i = 0; i < n; i++) lighting.SetFlicker(new FloorIndex(i), false);
                    SetWinter(0.45f, 1f, sleigh: false);
                    break;
            }
        }

        void SetWinter(float snow, float calm, bool sleigh)
        {
            if (Winter != null)
            {
                Winter.EndingSnow = snow;
                Winter.Calm = calm;
                if (sleigh) Winter.SleighFlight = true;
                else Winter.ResetSleigh();
            }
            if (Christmas != null) Christmas.EndingShow = true;
        }

        /// <summary>Back to normal (flicker is re-derived from the signs by the caller).</summary>
        public void Reset()
        {
            Active = EndingKind.None;
            floor4On = revealed = false;
            wave = 0;
            lighting.ResetMood();
            building.ClearReveals();
            building.SetHotelSignTint(Color.white);
            elevator.SetLightLevel(1f);
            if (Winter != null)
            {
                Winter.EndingSnow = null;
                Winter.Calm = 0f;
                Winter.ResetSleigh();
            }
            if (Christmas != null) Christmas.EndingShow = false;
        }

        /// <summary>Haunted HOTEL sign brightness 0..1 (the floors' broken wiring, see FloorLighting.FlickerValue).</summary>
        public static float HauntedSignLevel(float t) => FloorLighting.FlickerValue(t, FloorLighting.WiringSeed);

        public const float ProsperousBlinkPeriod = 1.2f;

        /// <summary>Prosperous HOTEL sign: bright 0.8 s, warm 0.4 s (2 changes per 1.2 s, photosensitivity-safe).</summary>
        public static bool ProsperousSignLit(float t) => Mathf.Repeat(Mathf.Max(0f, t), ProsperousBlinkPeriod) < ProsperousBlinkPeriod * (2f / 3f);

        /// <param name="clock">Juice clock the floor lights flicker on (the haunted sign stays in phase with them).</param>
        public void Tick(float dt, float clock)
        {
            if (Active == EndingKind.None || dt <= 0f) return;
            t += dt;
            int n = lighting.FloorCount;
            switch (Active)
            {
                case EndingKind.FourthFloor:
                {
                    elevator.SetLightLevel(Mathf.Clamp01(1f - t / DarkSeconds));
                    building.SetHotelSignTint(Color.Lerp(Color.white, new Color(0.18f, 0.18f, 0.2f), Mathf.Clamp01(t / DarkSeconds)));
                    var f4 = RealFourth;
                    if (!floor4On && t >= Floor4OnAt)
                    {
                        floor4On = true;
                        for (int i = 0; i < n; i++) lighting.SetFlicker(new FloorIndex(i), false);
                        lighting.SetBaseColor(f4, ColdLight);
                        lighting.SetFlicker(f4, true);
                        lighting.SetLevel(f4, 1f, 0f);
                        lighting.Flash(f4, Color.white, 0.4f, 0.8f);
                    }
                    if (!revealed && t >= RevealAt)
                    {
                        revealed = true;
                        var original = originalLabel(f4);
                        building.RevealOriginal(f4, original.IsEmpty ? FloorLabel.Original(f4).Text : original.Text, RevealInk, RevealPlate);
                    }
                    break;
                }
                case EndingKind.Haunted:
                {
                    // same wiring as the floors: the sign sags and drops out with them (<= 3 transitions per second)
                    building.SetHotelSignTint(Color.Lerp(new Color(0.25f, 0.1f, 0.1f), new Color(1f, 0.55f, 0.55f), HauntedSignLevel(clock)));
                    break;
                }
                case EndingKind.Prosperous:
                {
                    // bottom-up wave of gold, then everything brighter than normal and the HOTEL sign blinking
                    while (wave < n && t >= 0.15f * wave)
                    {
                        var f = new FloorIndex(wave);
                        lighting.SetLevel(f, 1.3f, 3f);
                        lighting.Flash(f, RenderingStyle.LuckyFlash, 0.6f, 0.8f);
                        wave++;
                    }
                    bool blink = ProsperousSignLit(t);
                    if (building.HotelSign != null && Mathf.Repeat(t, 0.7f) < dt)
                        fx.Burst(building.HotelSign.position + new Vector3(Random.Range(-2.5f, 2.5f), 0.4f, -0.4f), RenderingStyle.LuckyFlash, 6, 2.6f);
                    building.SetHotelSignTint(blink ? Color.white : new Color(1f, 0.75f, 0.35f) * 0.6f);
                    break;
                }
                case EndingKind.WinterMiracle:
                {
                    // bottom-up warm wave (a slow rise, no flash), the sign steady and warm
                    while (wave < n && t >= MiracleStep * wave)
                    {
                        lighting.SetLevel(new FloorIndex(wave), 1.2f, 1.2f);
                        wave++;
                    }
                    building.SetHotelSignTint(Color.Lerp(Color.white, MiracleSign, Mathf.Clamp01(t)));
                    break;
                }
                case EndingKind.SilentNight:
                {
                    // one floor after another fades out, top first; the lobby keeps its light
                    int dark = WinterSchedule.LightsOutCount(t, n);
                    while (wave < dark)
                    {
                        var f = new FloorIndex(WinterSchedule.LightsOutFloor(wave, n));
                        lighting.SetLevel(f, WinterSchedule.LightsOutLevel, WinterSchedule.LightsOutSpeed);
                        wave++;
                    }
                    building.SetHotelSignTint(Color.Lerp(Color.white, new Color(0.38f, 0.34f, 0.36f), Mathf.Clamp01(t / SilentSignSeconds)));
                    break;
                }
            }
        }
    }
}
