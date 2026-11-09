using System.Collections.Generic;
using FloorF.Core;

namespace FloorF.Gameplay
{
    /// <summary>Keys the debug cheats read (DebugOverlay maps the Input System keyboard to these).</summary>
    internal enum DebugKey
    {
        Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7,
        /// <summary>Ctrl+Shift+D toggles debug mode.</summary>
        D,
        /// <summary>F1: the text overlay.</summary>
        F1,
        /// <summary>Shift+P/M/G/F: endings Prosperous / Modest / Haunted ("ghost") / FourthFloor.</summary>
        P, M, G, F,
        /// <summary>T tips +100, Shift+T +1000.</summary>
        T,
        /// <summary>= or numpad + : Luck +10.</summary>
        Plus,
        /// <summary>- or numpad - : Luck -10.</summary>
        Minus,
        /// <summary>N: end the running day now.</summary>
        N,
        /// <summary>X: spawn the mystery guest (lobby -> real 4th floor).</summary>
        X,
        /// <summary>Shift+R: refill the shop's rerolls.</summary>
        R,
    }

    /// <summary>
    /// Debug mode gate + cheat dispatch, engine-free so EditMode tests drive it directly. Debug mode is OFF at start
    /// and toggled only by a deliberate input: Ctrl+Shift+D, or typing the letters L-U-C-K-Y in a row. While it is off no
    /// cheat key does anything (F1's overlay too, unless <see cref="OverlayWithoutDebugMode"/> - Editor/dev builds).
    /// Cheats ignore frames with Ctrl or Alt held (browser shortcuts). Key map: <see cref="HelpText"/>.
    /// </summary>
    internal sealed class DebugCheats
    {
        public const int TipsSmall = 100, TipsBig = 1000, LuckStep = 10;
        const string Secret = "LUCKY";

        public const string HelpText =
            "DEBUG MODE  (Ctrl+Shift+D or type LUCKY = off)\n" +
            "0-7  jump to that day's Intro\n" +
            "N  end day now    X  mystery guest\n" +
            "Shift+P/M/G/F  ending Prosperous/Modest/Haunted/4th floor\n" +
            "T  tips +100   Shift+T  tips +1000\n" +
            "=/+  luck +10   -  luck -10\n" +
            "Shift+R  refill rerolls   (overlay, shop) B buy  R reroll";

        readonly GameSession session;
        int secretMatched;

        public DebugCheats(GameSession session, bool overlayWithoutDebugMode)
        {
            this.session = session;
            OverlayWithoutDebugMode = overlayWithoutDebugMode;
        }

        public bool Enabled { get; private set; }
        public bool OverlayVisible { get; set; }
        /// <summary>F1 works without debug mode (Editor / development builds only).</summary>
        public bool OverlayWithoutDebugMode { get; }
        /// <summary>Short description of the last cheat applied (indicator feedback), or null.</summary>
        public string LastAction { get; private set; }
        /// <summary>Bumped on every applied cheat or toggle (the indicator shows <see cref="LastAction"/> briefly).</summary>
        public int ActionCount { get; private set; }

        /// <summary>
        /// One frame of input. <paramref name="letters"/>: letter keys pressed this frame (upper case, in key order) for the
        /// typed toggle. <paramref name="pressed"/>: keys pressed this frame.
        /// </summary>
        public void Handle(bool ctrl, bool shift, bool alt, IReadOnlyList<DebugKey> pressed, IReadOnlyList<char> letters)
        {
            if (session == null) return;
            if (letters != null)
                foreach (var c in letters)
                    if (FeedSecret(c)) { Toggle(); return; }
            if (pressed == null) return;
            if (ctrl && shift && !alt && Contains(pressed, DebugKey.D)) { Toggle(); return; }
            if (Contains(pressed, DebugKey.F1) && (Enabled || OverlayWithoutDebugMode)) OverlayVisible = !OverlayVisible;
            if (!Enabled || ctrl || alt) return;
            foreach (var key in pressed) Apply(key, shift);
        }

        bool FeedSecret(char c)
        {
            c = char.ToUpperInvariant(c);
            if (c == Secret[secretMatched]) secretMatched++;
            else secretMatched = c == Secret[0] ? 1 : 0;
            if (secretMatched < Secret.Length) return false;
            secretMatched = 0;
            return true;
        }

        public void Toggle()
        {
            Enabled = !Enabled;
            if (!Enabled && !OverlayWithoutDebugMode) OverlayVisible = false;
            Done(Enabled ? "debug mode ON" : "debug mode OFF");
        }

        void Apply(DebugKey key, bool shift)
        {
            switch (key)
            {
                case DebugKey.Digit0: case DebugKey.Digit1: case DebugKey.Digit2: case DebugKey.Digit3:
                case DebugKey.Digit4: case DebugKey.Digit5: case DebugKey.Digit6: case DebugKey.Digit7:
                    if (shift) return;
                    int n = key - DebugKey.Digit0;
                    if (session.DebugJumpToDay(n)) Done("jump to day " + n);
                    return;
                case DebugKey.P: if (shift) Ending(EndingKind.Prosperous); return;
                case DebugKey.M: if (shift) Ending(EndingKind.Modest); return;
                case DebugKey.G: if (shift) Ending(EndingKind.Haunted); return;
                case DebugKey.F: if (shift) Ending(EndingKind.FourthFloor); return;
                case DebugKey.T:
                    int tips = shift ? TipsBig : TipsSmall;
                    if (session.DebugAddTips(tips)) Done("tips +" + tips);
                    return;
                case DebugKey.Plus: Luck(LuckStep); return;
                case DebugKey.Minus: Luck(-LuckStep); return;
                case DebugKey.N: if (!shift && session.DebugEndDay()) Done("day ended"); return;
                case DebugKey.X: if (!shift && !session.DebugSpawnMystery().IsNone) Done("mystery guest"); return;
                case DebugKey.R: if (shift && session.DebugRefillRerolls()) Done("rerolls refilled"); return;
            }
        }

        void Ending(EndingKind kind)
        {
            if (session.DebugForceEnding(kind)) Done("ending " + kind);
        }

        void Luck(int delta)
        {
            if (!session.HasStarted || session.IsOnTitle) return;
            session.DebugAddLuck(delta);
            Done("luck " + (delta >= 0 ? "+" : "") + delta + " = " + session.Score.Luck);
        }

        void Done(string what)
        {
            LastAction = what;
            ActionCount++;
        }

        static bool Contains(IReadOnlyList<DebugKey> keys, DebugKey key)
        {
            for (int i = 0; i < keys.Count; i++) if (keys[i] == key) return true;
            return false;
        }
    }
}
