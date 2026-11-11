using FloorF.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorF.Gameplay
{
    /// <summary>
    /// Keyboard bindings (GDD 4: W/S + Space as the keyboard alternative to the panel), Esc = pause toggle,
    /// Enter = flow shortcut (Title -> new game, Intro -> start, ...). Mapping: <see cref="PlayerCommands.IntentForKey"/>.
    /// Enter can be turned off with <see cref="AdvanceWithEnter"/> if the UI wants to own it.
    /// </summary>
    sealed class KeyboardInput : MonoBehaviour
    {
        /// <summary>Enter drives IGameFlow / IMenuFlow (Title/Intro/Summary/Shop/Ending).</summary>
        public static bool AdvanceWithEnter = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AdvanceWithEnter = true;

        GameSession session;

        internal void Bind(GameSession s) => session = s;

        void Update()
        {
            if (session == null || !session.HasStarted) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            // P = pause too: in browser fullscreen Esc belongs to the browser (it leaves fullscreen)
            if (IsPauseKey(kb.escapeKey.wasPressedThisFrame, kb.pKey.wasPressedThisFrame, kb.shiftKey.isPressed, kb.ctrlKey.isPressed, kb.altKey.isPressed))
                Press(KeyPress.Escape);
            if (kb.wKey.wasPressedThisFrame) Press(KeyPress.W);
            else if (kb.upArrowKey.wasPressedThisFrame) Press(KeyPress.UpArrow);
            if (kb.sKey.wasPressedThisFrame) Press(KeyPress.S);
            else if (kb.downArrowKey.wasPressedThisFrame) Press(KeyPress.DownArrow);
            if (kb.spaceKey.wasPressedThisFrame) Press(KeyPress.Space);
            bool enter = AdvanceWithEnter && !(GameServices.TryGet<IUiFlowGate>(out var gate) && gate.OwnsEnter);
            if (enter && kb.enterKey.wasPressedThisFrame) Press(KeyPress.Enter);
            else if (enter && kb.numpadEnterKey.wasPressedThisFrame) Press(KeyPress.NumpadEnter);
        }

        /// <summary>
        /// Esc, or a plain P (fullscreen browsers keep Esc). Shift+P is the debug "Prosperous ending" cheat, Ctrl/Alt+P are
        /// browser shortcuts (print...): none of them toggles the pause.
        /// </summary>
        internal static bool IsPauseKey(bool esc, bool p, bool shift, bool ctrl, bool alt) => esc || (p && !shift && !ctrl && !alt);

        /// <summary>One key press, already mapped (PlayMode tests call this: the test assembly has no Input System reference).</summary>
        internal bool Press(KeyPress key)
            => session != null && session.HasStarted && PlayerCommands.Apply(PlayerCommands.IntentForKey(key), session.Elevator, session, session.Building, session, session, session);
    }
}
