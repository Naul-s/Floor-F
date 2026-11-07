using System;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Kenney CC0 model catalog (built by FloorF.EditorTools.KenneyCatalogBuilder into Resources/Rendering/Kenney).
    /// Every mesh here samples ONE atlas texture through ONE material: tile 0 = Kenney colormap (arcade machines),
    /// one tinted colormap copy per guest character (shirt swatch = guests.json colour), one swatch tile for the
    /// Furniture Kit's flat material colours (+ a few extra swatches for primitive props). Prop meshes are baked
    /// bottom-centre pivoted, facing -Z (the camera), in world units; guest prefabs are unscaled (model units).
    /// </summary>
    public sealed class KenneyCatalog : ScriptableObject
    {
        public const string ResourcePath = "Rendering/Kenney/KenneyCatalog";

        [Serializable]
        public sealed class GuestModel
        {
            /// <summary>guests.json "accessory" key this model stands for.</summary>
            public string Accessory;
            /// <summary>0 = main, 1 = partner (couples alternate per guest id).</summary>
            public int Variant;
            public string Character;
            public GameObject Prefab;
            /// <summary>Atlas tile the meshes sample (distinct per model).</summary>
            public int Tile;
            /// <summary>Colour painted into the shirt swatch of the tile (Vivid(guests.json colour)).</summary>
            public Color Tint;
            /// <summary>Atlas UV of the shirt swatch centre (tests sample it).</summary>
            public Vector2 ShirtUV;
            /// <summary>Rest-pose bounds in model space (prefab root, model faces +Z): whole figure and head mesh.</summary>
            public Bounds Bounds;
            public Bounds Head;
        }

        [Serializable]
        public sealed class PropModel
        {
            public string Name;
            public Mesh Mesh;
        }

        [Serializable]
        public sealed class Swatch
        {
            public string Name;
            public Color Color;
            public Vector2 UV;
        }

        public Material Material;
        public Texture2D Atlas;
        public AnimationClip Idle;
        public AnimationClip Walk;
        public GuestModel[] Guests = new GuestModel[0];
        public PropModel[] Props = new PropModel[0];
        public Swatch[] Swatches = new Swatch[0];

        public GuestModel FindGuest(string accessory, int variant)
        {
            GuestModel any = null;
            foreach (var g in Guests)
            {
                if (g == null || g.Prefab == null || g.Accessory != accessory) continue;
                if (g.Variant == variant) return g;
                if (any == null) any = g;
            }
            return any;
        }

        public int VariantCount(string accessory)
        {
            int n = 0;
            foreach (var g in Guests) if (g != null && g.Accessory == accessory) n++;
            return n;
        }

        public Mesh FindProp(string name)
        {
            foreach (var p in Props) if (p != null && p.Name == name) return p.Mesh;
            return null;
        }

        public bool TryGetSwatch(string name, out Vector2 uv)
        {
            foreach (var s in Swatches)
                if (s != null && s.Name == name) { uv = s.UV; return true; }
            uv = default;
            return false;
        }
    }
}
