using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// Destination icons: white 96px sprites (Material Symbols, Apache 2.0, orchestrator import) at Resources
    /// "Icons/&lt;DestinationDef.IconKey&gt;", tinted with the destination colour by the views. A missing sprite returns
    /// null (callers fall back to the old text badge) and warns once per key.
    /// </summary>
    static class UIIcons
    {
        public const string ResourceFolder = "Icons/";

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>Test hook: icon keys treated as missing (exercise the text fallback).</summary>
        internal static readonly HashSet<string> ForceMissing = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            cache.Clear();
            warned.Clear();
            ForceMissing.Clear();
        }

        public static Sprite For(DestinationDef d) => d == null ? null : For(d.IconKey);

        public static Sprite For(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (ForceMissing.Contains(key)) { Warn(key); return null; }
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            s = Resources.Load<Sprite>(ResourceFolder + key);
            if (s == null) { Warn(key); return null; }
            cache[key] = s;
            return s;
        }

        static void Warn(string key)
        {
            if (warned.Add(key)) Debug.LogWarning("[UI] destination icon '" + ResourceFolder + key + "' not found - text badge fallback");
        }

        /// <summary>Badge fill behind a tinted icon: the destination colour sunk into the dark card colour.</summary>
        public static Color BadgeFill(Color destination)
        {
            var c = Color.Lerp(UIStyle.Dark, destination, 0.24f);
            c.a = 1f;
            return c;
        }

        /// <summary>Name ink next to the icon: the destination colour lifted towards white (readable on <see cref="BadgeFill"/>).</summary>
        public static Color NameInk(Color destination) => Color.Lerp(destination, Color.white, 0.45f);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
