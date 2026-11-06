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
            if (kb.escapeKey.wasPressedThisFrame) Press(KeyPress.Escape);
            if (kb.wKey.wasPressedThisFrame) Press(KeyPress.W);
            else if (kb.upArrowKey.wasPressedThisFrame) Press(KeyPress.UpArrow);
            if (kb.sKey.wasPressedThisFrame) Press(KeyPress.S);
            else if (kb.downArrowKey.wasPressedThisFrame) Press(KeyPress.DownArrow);
            if (kb.spaceKey.wasPressedThisFrame) Press(KeyPress.Space);
            if (AdvanceWithEnter && kb.enterKey.wasPressedThisFrame) Press(KeyPress.Enter);
            else if (AdvanceWithEnter && kb.numpadEnterKey.wasPressedThisFrame) Press(KeyPress.NumpadEnter);
        }

        void Press(KeyPress key)
            => PlayerCommands.Apply(PlayerCommands.IntentForKey(key), session.Elevator, session, session.Building, session, session, session);
    }
}
