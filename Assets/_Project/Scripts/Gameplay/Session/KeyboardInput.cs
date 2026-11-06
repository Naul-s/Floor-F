using FloorF.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorF.Gameplay
{
    /// <summary>
    /// Keyboard bindings (GDD 4: W/S + Space as the keyboard alternative to the panel). Enter advances the flow
    /// TEMPORARILY until ui-dev's buttons call IGameFlow; set <see cref="AdvanceWithEnter"/> false then.
    /// </summary>
    sealed class KeyboardInput : MonoBehaviour
    {
        /// <summary>Enter drives IGameFlow (Intro/Summary/Shop/Ending). Temporary.</summary>
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
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) Do(PlayerIntent.Up);
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) Do(PlayerIntent.Down);
            if (kb.spaceKey.wasPressedThisFrame) Do(PlayerIntent.ToggleDoors);
            if (AdvanceWithEnter && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Do(PlayerIntent.Advance);
        }

        void Do(PlayerIntent intent) => PlayerCommands.Apply(intent, session.Elevator, session, session.Building, session, session);
    }
}
