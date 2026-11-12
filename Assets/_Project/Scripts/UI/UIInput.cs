using FloorF.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorF.UI
{
    /// <summary>One frame of the presses the UI reacts to itself (polled once per frame by UIRoot).</summary>
    struct UIPress
    {
        /// <summary>Any keyboard key or gamepad button (not the pointer).</summary>
        public bool AnyKey;
        /// <summary>A pointer press (mouse button / touch) this frame, at <see cref="Screen"/> (screen pixels).</summary>
        public bool Pointer;
        public Vector2 Screen;
        public bool V, H, Enter;
        public bool Any => AnyKey || Pointer;
    }

    /// <summary>
    /// Polls the Input System for <see cref="UIPress"/>; tests queue presses with <see cref="Inject"/> (the test assembly
    /// has no Input System reference). Also owns the "Enter belongs to the UI" gate on Gameplay's KeyboardInput.
    /// </summary>
    static class UIInput
    {
        static UIPress injected;
        static bool hasInjected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            injected = default;
            hasInjected = false;
            blockedByUs = false;
            spaceBlockedByUs = false;
        }

        /// <summary>Test hook: the next <see cref="Poll"/> also reports these presses.</summary>
        internal static void Inject(UIPress p)
        {
            if (hasInjected)
            {
                p.AnyKey |= injected.AnyKey; p.V |= injected.V; p.H |= injected.H; p.Enter |= injected.Enter;
                if (!p.Pointer && injected.Pointer) { p.Pointer = true; p.Screen = injected.Screen; }
            }
            injected = p;
            hasInjected = true;
        }

        public static UIPress Poll()
        {
            var p = new UIPress();
            var kb = Keyboard.current;
            if (kb != null)
            {
                p.AnyKey = kb.anyKey.wasPressedThisFrame;
                if (p.AnyKey)
                {
                    p.V = kb.vKey.wasPressedThisFrame;
                    p.H = kb.hKey.wasPressedThisFrame;
                    p.Enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                }
            }
            var pad = Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) p.AnyKey = true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame))
            {
                p.Pointer = true;
                p.Screen = mouse.position.ReadValue();
            }
            else
            {
                var touch = Touchscreen.current;
                if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                {
                    p.Pointer = true;
                    p.Screen = touch.primaryTouch.position.ReadValue();
                }
            }
            if (hasInjected)
            {
                p.AnyKey |= injected.AnyKey; p.V |= injected.V; p.H |= injected.H; p.Enter |= injected.Enter;
                if (injected.Pointer) { p.Pointer = true; p.Screen = injected.Screen; }
                hasInjected = false;
                injected = default;
            }
            return p;
        }

        // ------------------------------------------------------------------ Enter gate (Gameplay KeyboardInput)

        static bool blockedByUs, spaceBlockedByUs;

        sealed class FlowGate : IUiFlowGate { public bool OwnsEnter => blockedByUs; public bool OwnsSpace => spaceBlockedByUs; }
        static readonly FlowGate gate = new FlowGate();

        /// <summary>
        /// Gameplay's KeyboardInput turns Enter into "advance the flow" (Title -> new game, Intro -> start). While the
        /// title attract screen / an establishing shot is up, Enter must only reveal / skip: the UI says so through the
        /// IUiFlowGate contract (registered here; GameServices.Reset on a scene load drops it, so re-register lazily).
        /// </summary>
        public static void BlockFlowEnter(bool block)
        {
            if (!GameServices.TryGet<IUiFlowGate>(out var g) || !ReferenceEquals(g, gate)) GameServices.Register<IUiFlowGate>(gate);
            blockedByUs = block;
        }

        /// <summary>While a coach card waits for "continue", Space only continues (never also opens / closes the doors).</summary>
        public static void BlockFlowSpace(bool block)
        {
            if (!GameServices.TryGet<IUiFlowGate>(out var g) || !ReferenceEquals(g, gate)) GameServices.Register<IUiFlowGate>(gate);
            spaceBlockedByUs = block;
        }

        /// <summary>Test hook: true when Enter advances the game flow (the UI does not own it right now).</summary>
        internal static bool? FlowEnterEnabled => !blockedByUs;
    }
}
