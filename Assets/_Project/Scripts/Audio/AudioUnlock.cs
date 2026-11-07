using System;
using System.Reflection;
using UnityEngine;

namespace FloorF.Audio
{
    /// <summary>
    /// WebGL audio lock: browsers keep the AudioContext suspended until a user gesture. Until then Audio only RESERVES
    /// playback (music track is chosen but not started; SFX are dropped so they do not burst out on the first click).
    /// Opens on the first key/pointer press (<see cref="InputProbe"/>) or on any event only a player can cause.
    /// Other platforms (Editor, standalone) start unlocked.
    /// </summary>
    sealed class AudioUnlock
    {
        /// <summary>Tests: force the gesture requirement on (true) / off (false). Null = WebGL player only.</summary>
        internal static bool? RequireGestureOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => RequireGestureOverride = null;

        public static bool DefaultRequiresGesture => RequireGestureOverride ?? Application.platform == RuntimePlatform.WebGLPlayer;

        public bool IsUnlocked { get; private set; }

        public AudioUnlock(bool requireGesture) => IsUnlocked = !requireGesture;

        /// <summary>Polls input while locked. Returns true on the frame it unlocks.</summary>
        public bool Poll()
        {
            if (IsUnlocked) return false;
            if (!InputProbe.AnyPressed()) return false;
            IsUnlocked = true;
            return true;
        }

        /// <summary>Returns true if this call opened the lock.</summary>
        public bool Unlock()
        {
            if (IsUnlocked) return false;
            IsUnlocked = true;
            return true;
        }
    }

    /// <summary>
    /// Read-only "is any key / pointer pressed" through the Input System, by reflection because FloorF.Audio does not
    /// reference Unity.InputSystem (asmdef owned by the orchestrator - see CONTRACT REQUEST). Never consumes input, so
    /// Gameplay's KeyboardInput is unaffected. Members are kept for IL2CPP by Scripts/Audio/link.xml.
    /// Fails soft: if the types cannot be resolved it always returns false (phase-change fallback still unlocks).
    /// </summary>
    static class InputProbe
    {
        const string Asm = ", Unity.InputSystem";
        static bool resolved;
        static PropertyInfo keyboardCurrent, keyboardAnyKey, pointerCurrent, pointerPress, buttonIsPressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => resolved = false;

        static void Resolve()
        {
            resolved = true;
            try
            {
                var keyboard = Type.GetType("UnityEngine.InputSystem.Keyboard" + Asm);
                var pointer = Type.GetType("UnityEngine.InputSystem.Pointer" + Asm);
                var button = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl" + Asm);
                const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;
                keyboardCurrent = keyboard?.GetProperty("current", S);
                keyboardAnyKey = keyboard?.GetProperty("anyKey", I);
                pointerCurrent = pointer?.GetProperty("current", S);
                pointerPress = pointer?.GetProperty("press", I);
                buttonIsPressed = button?.GetProperty("isPressed", I);
            }
            catch (Exception)
            {
                keyboardCurrent = pointerCurrent = null;
            }
        }

        public static bool AnyPressed()
        {
            if (!resolved) Resolve();
            if (buttonIsPressed == null) return false;
            try
            {
                return Pressed(keyboardCurrent, keyboardAnyKey) || Pressed(pointerCurrent, pointerPress);
            }
            catch (Exception)
            {
                buttonIsPressed = null; // stop trying
                return false;
            }
        }

        static bool Pressed(PropertyInfo device, PropertyInfo control)
        {
            if (device == null || control == null) return false;
            var d = device.GetValue(null);
            if (d == null) return false;
            var c = control.GetValue(d);
            return c != null && buttonIsPressed.GetValue(c) is bool b && b;
        }
    }
}
