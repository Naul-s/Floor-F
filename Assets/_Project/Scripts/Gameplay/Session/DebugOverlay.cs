using System.Collections.Generic;
using System.Text;
using FloorF.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorF.Gameplay
{
    /// <summary>
    /// Editor/development-only text readout (IMGUI) so the rules can be played before the real HUD exists.
    /// Hidden by default; F1 toggles it. While shown, in the Shop, B buys the first affordable offer onto the first floor it fits (prefers the real 4th).
    /// Not shown in release builds; not part of the UI module.
    /// </summary>
    sealed class DebugOverlay : MonoBehaviour
    {
        GameSession session;
        bool visible; // hidden by default (the real HUD is the UI module); F1 shows it
        readonly StringBuilder sb = new StringBuilder(512);
        readonly List<GuestId> buffer = new List<GuestId>();
        GUIStyle style;

        internal void Bind(GameSession s) => session = s;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || session == null) return;
            if (kb.f1Key.wasPressedThisFrame) visible = !visible;
            if (visible && kb.bKey.wasPressedThisFrame && session.IsOpen) BuyFirst();
        }

        void BuyFirst()
        {
            var b = session.Building;
            foreach (var offer in session.Offers)
            {
                if (session.TryBuyAndApply(offer.Id, StoryRules.RealFourthFloor) == StickerApplyResult.Applied) return;
                for (int f = b.FloorCount - 1; f >= 0; f--)
                    if (session.TryBuyAndApply(offer.Id, new FloorIndex(f)) == StickerApplyResult.Applied) return;
            }
        }

        void OnGUI()
        {
            if (!visible || session == null || !session.HasStarted) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = false };
            var e = session.Elevator;
            var b = session.Building;
            sb.Clear();
            sb.Append("Day ").Append(session.CurrentDay.Number).Append('/').Append(session.DayCount)
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
                case DayPhase.Running: sb.Append("[W/S] floor  [Space] doors"); break;
                case DayPhase.Summary:
                    var s = session.LastSummary;
                    sb.Append("Delivered ").Append(s.Delivered).Append("  Left ").Append(s.Left).Append("  Tips +").Append(s.TipsEarned)
                      .Append("  Luck ").Append(s.LuckDelta >= 0 ? "+" : "").Append(s.LuckDelta).Append("\n[Enter] continue");
                    break;
                case DayPhase.Shop:
                    foreach (var o in session.Offers) sb.Append(o.Sticker.name).Append("  $").Append(o.Price).Append("  x").Append(o.Remaining).Append('\n');
                    sb.Append("[B] buy first  [Enter] next day");
                    break;
                case DayPhase.Ending: sb.Append("ENDING: ").Append(session.Ending).Append("\n[Enter] play again"); break;
            }
            GUI.Box(new Rect(8, 8, 330, 24 + 15 * (8 + b.FloorCount)), sb.ToString(), style);
        }
    }
}
