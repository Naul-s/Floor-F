using System.Collections.Generic;
using System.Text;
using FloorF.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorF.Gameplay
{
    /// <summary>
    /// Hand-testing aid, present in EVERY build (WebGL release included) but inert until debug mode is switched on
    /// deliberately (Ctrl+Shift+D, or typing L-U-C-K-Y). See <see cref="DebugCheats"/> for the gate and the key map.
    /// While debug mode is on a small "DEBUG" tag shows in the top-right corner (with the last cheat for a moment).
    /// F1 toggles the IMGUI text readout (release builds: only in debug mode; Editor/dev builds: always) which then lists
    /// the debug keys too. While the readout is shown, in the Shop, B buys the first affordable offer (a charm, or a label
    /// onto the first floor it fits - prefers the real 4th), R rerolls. Not part of the UI module.
    /// </summary>
    sealed class DebugOverlay : MonoBehaviour
    {
        const float LastActionSeconds = 2.5f;

        // Key.Digit0 comes AFTER Digit9 in the Input System enum: explicit tables for days 0..7
        static readonly Key[] DigitKeys = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7 };
        static readonly Key[] NumpadKeys = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7 };

        GameSession session;
        DebugCheats cheats;
        readonly StringBuilder sb = new StringBuilder(1024);
        readonly List<GuestId> buffer = new List<GuestId>();
        readonly List<DebugKey> pressed = new List<DebugKey>(8);
        readonly List<char> letters = new List<char>(4);
        GUIStyle style, tagStyle;
        int shownActionCount;
        float actionShownUntil;

        internal DebugCheats Cheats => cheats;

        internal void Bind(GameSession s)
        {
            session = s;
            cheats = new DebugCheats(s, Application.isEditor || Debug.isDebugBuild);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || session == null) return;
            if (kb.anyKey.wasPressedThisFrame) ReadKeys(kb);
            else { pressed.Clear(); letters.Clear(); }
            bool wasEnabled = cheats.Enabled;
            Feed(kb.ctrlKey.isPressed, kb.shiftKey.isPressed, kb.altKey.isPressed, pressed, letters);
            if (cheats.Enabled != wasEnabled) Debug.Log("[Gameplay] " + cheats.LastAction);
            if (cheats.OverlayVisible && session.IsOpen && !kb.shiftKey.isPressed && !kb.ctrlKey.isPressed)
            {
                if (kb.bKey.wasPressedThisFrame) BuyFirst();
                if (kb.rKey.wasPressedThisFrame) session.Reroll();
            }
        }

        /// <summary>One frame of (already mapped) input; PlayMode tests call this directly.</summary>
        internal void Feed(bool ctrl, bool shift, bool alt, IReadOnlyList<DebugKey> keys, IReadOnlyList<char> typed)
        {
            if (cheats == null) return;
            cheats.Handle(ctrl, shift, alt, keys, typed);
            if (cheats.ActionCount != shownActionCount)
            {
                shownActionCount = cheats.ActionCount;
                actionShownUntil = Time.unscaledTime + LastActionSeconds;
            }
        }

        void ReadKeys(Keyboard kb)
        {
            pressed.Clear();
            letters.Clear();
            for (var k = Key.A; k <= Key.Z; k++)
                if (kb[k].wasPressedThisFrame) letters.Add((char)('A' + (k - Key.A)));
            for (int i = 0; i < DigitKeys.Length; i++)
                if (kb[DigitKeys[i]].wasPressedThisFrame || kb[NumpadKeys[i]].wasPressedThisFrame) pressed.Add(DebugKey.Digit0 + i);
            Add(kb.dKey, DebugKey.D);
            Add(kb.f1Key, DebugKey.F1);
            Add(kb.pKey, DebugKey.P);
            Add(kb.mKey, DebugKey.M);
            Add(kb.gKey, DebugKey.G);
            Add(kb.fKey, DebugKey.F);
            Add(kb.tKey, DebugKey.T);
            Add(kb.nKey, DebugKey.N);
            Add(kb.xKey, DebugKey.X);
            Add(kb.rKey, DebugKey.R);
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) pressed.Add(DebugKey.Plus);
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) pressed.Add(DebugKey.Minus);
        }

        void Add(UnityEngine.InputSystem.Controls.KeyControl key, DebugKey mapped)
        {
            if (key.wasPressedThisFrame) pressed.Add(mapped);
        }

        void BuyFirst()
        {
            var b = session.Building;
            foreach (var offer in session.Offers)
            {
                if (offer.Sticker.IsCharm) { if (session.TryBuy(offer.Id) == StickerApplyResult.Applied) return; continue; }
                if (session.TryBuyAndApply(offer.Id, StoryRules.RealFourthFloor) == StickerApplyResult.Applied) return;
                for (int f = b.FloorCount - 1; f >= 0; f--)
                    if (session.TryBuyAndApply(offer.Id, new FloorIndex(f)) == StickerApplyResult.Applied) return;
            }
        }

        void OnGUI()
        {
            if (session == null || cheats == null) return;
            if (cheats.Enabled) DrawTag();
            if (!cheats.OverlayVisible || !session.HasStarted) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = false };
            var e = session.Elevator;
            var b = session.Building;
            sb.Clear();
            sb.Append("Day ").Append(session.DisplayDay).Append('/').Append(session.PlayableDayCount)
              .Append("  ").Append(session.Phase).Append("  t=").Append(session.RemainingSeconds.ToString("0")).Append("s\n");
            sb.Append("Luck ").Append(session.Score.Luck).Append('/').Append(session.Score.MaxLuck)
              .Append("  Tips ").Append(session.Score.Tips).Append(" (+").Append(session.Score.TipsEarnedToday).Append(")\n");
            sb.Append("Car @").Append(b.GetLabel(e.CurrentFloor)).Append(' ').Append(e.Motion).Append(' ').Append(e.Doors)
              .Append("  ").Append(e.Occupancy).Append('/').Append(e.Capacity);
            if (e.HasTarget) sb.Append("  -> ").Append(b.GetLabel(e.TargetFloor));
            sb.Append('\n');
            foreach (var id in e.Riders)
                if (session.Guests.TryGet(id, out var r)) sb.Append("  rider ").Append(r.Archetype).Append(" -> ").Append(b.GetLabel(r.DestinationFloor)).Append('\n');
            for (int f = b.FloorCount - 1; f >= 0; f--)
            {
                var fi = new FloorIndex(f);
                int n = session.Guests.GetWaiting(fi, buffer);
                sb.Append('[').Append(b.GetLabel(fi)).Append("] ");
                for (int i = 0; i < n; i++)
                    if (session.Guests.TryGet(buffer[i], out var w))
                        sb.Append(w.Archetype.Value.Length > 0 ? w.Archetype.Value.Substring(0, 1).ToUpperInvariant() : "?")
                          .Append("->").Append(b.GetLabel(w.DestinationFloor)).Append('(').Append((w.Patience01 * 100f).ToString("0")).Append("%) ");
                sb.Append('\n');
            }
            switch (session.Phase)
            {
                case DayPhase.Intro: sb.Append(session.CurrentDayDef.title).Append("\n[Enter] start"); break;
                case DayPhase.Running: sb.Append(session.IsTutorial ? "(training shift - no clock)\n" : "").Append("[W/S] floor  [Space] doors"); break;
                case DayPhase.Summary:
                    var s = session.LastSummary;
                    sb.Append("Delivered ").Append(s.Delivered).Append("  Left ").Append(s.Left).Append("  Tips +").Append(s.TipsEarned)
                      .Append("  Luck ").Append(s.LuckDelta >= 0 ? "+" : "").Append(s.LuckDelta).Append("\n[Enter] continue");
                    break;
                case DayPhase.Shop:
                    foreach (var o in session.Offers) sb.Append(o.Sticker.name).Append("  $").Append(o.Price).Append("  x").Append(o.Remaining).Append('\n');
                    foreach (var c in session.ActiveCharms) sb.Append("  charm for tomorrow: ").Append(c.name).Append('\n');
                    sb.Append("[B] buy first  [R] reroll $").Append(session.RerollCost).Append(" (").Append(session.RerollsLeft).Append(" left)  [Enter] next day");
                    break;
                case DayPhase.Ending: sb.Append("ENDING: ").Append(session.Ending).Append("\n[Enter] play again"); break;
            }
            if (cheats.Enabled) sb.Append("\n\n").Append(DebugCheats.HelpText);
            int lines = 1;
            for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') lines++;
            GUI.Box(new Rect(8, 8, 380, 12 + 15 * lines), sb.ToString(), style);
        }

        /// <summary>Small unobtrusive tag in the top-right corner while debug mode is on.</summary>
        void DrawTag()
        {
            if (tagStyle == null)
                tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.35f, 0.3f, 0.85f) } };
            string text = Time.unscaledTime < actionShownUntil && !string.IsNullOrEmpty(cheats.LastAction)
                ? "DEBUG  " + cheats.LastAction
                : "DEBUG";
            GUI.Label(new Rect(Screen.width - 408, 2, 400, 18), text, tagStyle);
        }
    }
}
