using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorF.Rendering
{
    /// <summary>
    /// Small runtime palette texture (64x64, Point) behind ONE unlit material. Every texel is a flat colour; meshes made
    /// with <see cref="PaletteMeshBuilder"/> point all four UVs of a face at one texel centre. A whole city (buildings,
    /// windows, road, lamps) is then one draw call, and lights switch by rewriting texels - no per-window objects,
    /// material swaps or renderer changes. Static colours are shared; dynamic texels belong to one light each.
    /// </summary>
    sealed class PaletteAtlas
    {
        public const int Size = 64;

        readonly Texture2D texture;
        readonly Color32[] px = new Color32[Size * Size];
        readonly Dictionary<int, int> statics = new Dictionary<int, int>();
        int next;
        bool dirty;

        public Material Material { get; }
        public Texture2D Texture => texture;
        public int Used => next;
        /// <summary>Texture uploads so far (tests: lights change without rebuilding anything).</summary>
        public int Uploads { get; private set; }

        public PaletteAtlas(RenderAssets assets, string name)
        {
            texture = assets.Own(new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            });
            Material = assets.NewUnlit(Color.white, texture);
            Material.name = name;
            dirty = true;
        }

        static int Key(Color32 c) => (c.r << 16) | (c.g << 8) | c.b;

        /// <summary>Shared texel for a flat colour.</summary>
        public int Static(Color c)
        {
            var c32 = (Color32)c;
            c32.a = 255;
            int key = Key(c32);
            if (statics.TryGetValue(key, out int t)) return t;
            t = Allocate(c32);
            statics[key] = t;
            return t;
        }

        /// <summary>Texel of its own (a light) that <see cref="Set"/> may change later.</summary>
        public int Dynamic(Color c)
        {
            var c32 = (Color32)c;
            c32.a = 255;
            return Allocate(c32);
        }

        int Allocate(Color32 c)
        {
            if (next >= px.Length) throw new System.InvalidOperationException("palette atlas full");
            px[next] = c;
            dirty = true;
            return next++;
        }

        public void Set(int texel, Color c)
        {
            var c32 = (Color32)c;
            c32.a = 255;
            var o = px[texel];
            if (o.r == c32.r && o.g == c32.g && o.b == c32.b) return;
            px[texel] = c32;
            dirty = true;
        }

        public Color32 Get(int texel) => px[texel];

        public Vector2 UV(int texel) => new Vector2((texel % Size + 0.5f) / Size, (texel / Size + 0.5f) / Size);

        /// <summary>Uploads pending texel changes (once per frame at most; 16 KB).</summary>
        public bool Flush()
        {
            if (!dirty) return false;
            texture.SetPixels32(px);
            texture.Apply(false, false);
            dirty = false;
            Uploads++;
            return true;
        }
    }

    /// <summary>
    /// Builds flat-shaded palette meshes. Only the faces the oblique camera can see are emitted: front (-Z), top (+Y)
    /// and right (+X). Boxes are shaded by hand (top lighter, side darker) because the material is unlit.
    /// </summary>
    sealed class PaletteMeshBuilder
    {
        readonly PaletteAtlas atlas;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public PaletteMeshBuilder(PaletteAtlas atlas) { this.atlas = atlas; }

        public PaletteAtlas Atlas => atlas;
        public int VertexCount => v.Count;

        public static Color Top(Color c) => Shade(c, 1.28f);
        public static Color Side(Color c) => Shade(c, 0.7f);

        static Color Shade(Color c, float k)
        {
            var r = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
            return r;
        }

        /// <summary>Quad from four corners given bottom-left, top-left, top-right, bottom-right as seen by the camera.</summary>
        public void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, int texel)
        {
            if (texel < 0) return;
            int i = v.Count;
            v.Add(bl); v.Add(tl); v.Add(tr); v.Add(br);
            var c = atlas.UV(texel);
            uv.Add(c); uv.Add(c); uv.Add(c); uv.Add(c);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Rectangle facing the camera (-Z) at depth <paramref name="z"/>.</summary>
        public void Front(float x0, float y0, float x1, float y1, float z, int texel)
            => Quad(new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z), new Vector3(x1, y0, z), texel);

        /// <summary>Horizontal rectangle facing up at height <paramref name="y"/>.</summary>
        public void Flat(float x0, float z0, float x1, float z1, float y, int texel)
            => Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), texel);

        /// <summary>Box from min to max: front / top / right texels (-1 = leave the face out).</summary>
        public void Box(Vector3 min, Vector3 max, int front, int top, int right)
        {
            Front(min.x, min.y, max.x, max.y, min.z, front);
            Flat(min.x, min.z, max.x, max.z, max.y, top);
            Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), right);
        }

        /// <summary>Box in one colour, hand-shaded.</summary>
        public void Box(Vector3 min, Vector3 max, Color c)
            => Box(min, max, atlas.Static(c), atlas.Static(Top(c)), atlas.Static(Side(c)));

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        public void Clear()
        {
            v.Clear(); uv.Clear(); t.Clear();
        }
    }

    /// <summary>Lit boxes merged into one mesh per material (static dressing: a few draw calls instead of one per box).</summary>
    sealed class LitBatch
    {
        readonly RenderAssets assets;
        readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();
        readonly List<Material> order = new List<Material>();

        public LitBatch(RenderAssets assets) { this.assets = assets; }

        public int Count { get; private set; }

        public void Box(Vector3 center, Vector3 size, Material material)
            => Mesh(assets.Cube, Matrix4x4.TRS(center, Quaternion.identity, size), material);

        public void BoxMinMax(Vector3 min, Vector3 max, Material material) => Box((min + max) * 0.5f, max - min, material);

        public void Mesh(Mesh mesh, Matrix4x4 m, Material material)
        {
            if (mesh == null || material == null) return;
            if (!parts.TryGetValue(material, out var list))
            {
                parts[material] = list = new List<CombineInstance>();
                order.Add(material);
            }
            list.Add(new CombineInstance { mesh = mesh, transform = m });
            Count++;
        }

        /// <summary>One child renderer per material under <paramref name="parent"/> (local space = parent space).</summary>
        public List<MeshRenderer> Build(Transform parent, string name, bool castShadows)
        {
            var result = new List<MeshRenderer>();
            foreach (var mat in order)
            {
                var list = parts[mat];
                var mesh = assets.Own(new Mesh { name = name + " " + mat.name });
                int verts = 0;
                foreach (var p in list) verts += p.mesh.vertexCount;
                if (verts > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(list.ToArray(), true, true);
                var go = new GameObject(name + " (" + mat.name + ")");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                result.Add(r);
            }
            parts.Clear();
            order.Clear();
            Count = 0;
            return result;
        }
    }

    /// <summary>One palette mesh renderer (static or moving) - shared helper.</summary>
    static class PaletteRenderer
    {
        public static MeshRenderer Create(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }
    }
}
