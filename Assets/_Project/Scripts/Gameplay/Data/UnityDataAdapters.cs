using FloorF.Core;
using UnityEngine;

namespace FloorF.Gameplay
{
    /// <summary>JsonUtility-backed <see cref="IJsonDeserializer"/> for the Core data DTOs.</summary>
    public sealed class UnityJsonDeserializer : IJsonDeserializer
    {
        public T FromJson<T>(string json) => JsonUtility.FromJson<T>(json);
    }

    /// <summary>Reads Resources/Data/&lt;name&gt;.json as a TextAsset.</summary>
    public sealed class ResourcesTextSource : ITextSource
    {
        readonly string folder;

        public ResourcesTextSource(string folder = GameDataLoader.ResourcesFolder) { this.folder = folder; }

        public bool TryRead(string name, out string text)
        {
            var asset = Resources.Load<TextAsset>(folder + "/" + name);
            text = asset != null ? asset.text : null;
            return asset != null;
        }
    }

    public static class GameDataResources
    {
        /// <summary>Loads and validates Resources/Data/*.json. Throws GameDataException listing every problem.</summary>
        public static GameData Load() => GameDataLoader.Load(new ResourcesTextSource(), new UnityJsonDeserializer());
    }

    /// <summary>Core value -> Unity conversions (presentation modules reference Gameplay, so they can use these too).</summary>
    public static class CoreUnityExtensions
    {
        public static Vector3 ToVector3(this WorldPoint p) => new Vector3(p.X, p.Y, p.Z);
        public static WorldPoint ToWorldPoint(this Vector3 v) => new WorldPoint(v.x, v.y, v.z);

        /// <summary>"#RRGGBB" from data -> Color (white on failure).</summary>
        public static Color ParseColor(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
