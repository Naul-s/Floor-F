using System;

namespace FloorF.EditorTools
{
    /// <summary>Reads "-key value" pairs from the Unity command line (batch-mode harness entry points).</summary>
    public static class HarnessArgs
    {
        public static string Get(string key, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

        public static bool Has(string key)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
