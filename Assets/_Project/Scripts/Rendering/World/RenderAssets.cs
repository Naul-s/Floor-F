using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FloorF.Rendering
{
    /// <summary>
    /// Meshes, materials and textures the rendering module creates at runtime. Owned by <see cref="RenderingRoot"/> and
    /// destroyed with it (domain reload is off, so nothing here may be static).
    /// Material templates come from Resources/Rendering/Materials (built by FloorF.EditorTools.RenderingAssetBuilder) so
    /// their shaders are guaranteed to be in WebGL builds; Shader.Find is only an editor-time fallback.
    /// </summary>
    sealed class RenderAssets : IDisposable
    {
        public const string LitTemplatePath = "Rendering/Materials/GreyboxLit";
        public const string UnlitTemplatePath = "Rendering/Materials/GreyboxUnlit";
        public const string LitShader = "Universal Render Pipeline/Simple Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<Color32, Material> litCache = new Dictionary<Color32, Material>();
        readonly Dictionary<Color32, Material> unlitCache = new Dictionary<Color32, Material>();
        readonly Material litTemplate;
        readonly Material unlitTemplate;

        public Mesh Cube { get; }
        public Mesh Quad { get; }
        public bool UsingBuiltTemplates { get; }
        /// <summary>Kenney model catalog (Resources/Rendering/Kenney); null = greybox fallback everywhere.</summary>
        public KenneyCatalog Kenney { get; }
        readonly Dictionary<string, Mesh> swatchCubes = new Dictionary<string, Mesh>();

        public RenderAssets()
        {
            Cube = Own(MeshFactory.CreateCube());
            Quad = Own(MeshFactory.CreateQuad());
            var lit = Resources.Load<Material>(LitTemplatePath);
            var unlit = Resources.Load<Material>(UnlitTemplatePath);
            UsingBuiltTemplates = lit != null && unlit != null;
            litTemplate = lit != null ? lit : Own(FromShader(LitShader, "Universal Render Pipeline/Lit"));
            unlitTemplate = unlit != null ? unlit : Own(FromShader(UnlitShader, LitShader));
            Kenney = Resources.Load<KenneyCatalog>(KenneyCatalog.ResourcePath);
            if (Kenney != null && Kenney.Material == null) Kenney = null;
            if (!UsingBuiltTemplates)
                Debug.LogWarning("[Rendering] material templates missing under Resources/" + LitTemplatePath +
                                 " - run FloorF.EditorTools.RenderingAssetBuilder.Build (falling back to Shader.Find; WebGL builds may lose the shader)");
        }

        static Material FromShader(string name, string fallback)
        {
            var shader = Shader.Find(name);
            if (shader == null) shader = Shader.Find(fallback);
            if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");
            return new Material(shader) { name = name };
        }

        /// <summary>Unit cube whose UVs all point at Kenney atlas swatch <paramref name="swatch"/> (shares the atlas material).</summary>
        public Mesh SwatchCube(string swatch)
        {
            if (Kenney == null || !Kenney.TryGetSwatch(swatch, out var uv)) return null;
            if (swatchCubes.TryGetValue(swatch, out var m)) return m;
            m = Own(Object.Instantiate(Cube));
            m.name = "Swatch Cube " + swatch;
            var uvs = new Vector2[m.vertexCount];
            for (int i = 0; i < uvs.Length; i++) uvs[i] = uv;
            m.uv = uvs;
            swatchCubes[swatch] = m;
            return m;
        }

        public T Own<T>(T obj) where T : Object
        {
            if (obj != null) owned.Add(obj);
            return obj;
        }

        /// <summary>Shared lit material for a colour (do not modify it - use <see cref="NewLit"/> for animated ones).</summary>
        public Material Lit(Color color)
        {
            var key = (Color32)color;
            if (!litCache.TryGetValue(key, out var m)) litCache[key] = m = NewLit(color);
            return m;
        }

        /// <summary>Shared unlit (self-lit) material: lamps, screens, glowing windows.</summary>
        public Material Unlit(Color color)
        {
            var key = (Color32)color;
            if (!unlitCache.TryGetValue(key, out var m)) unlitCache[key] = m = NewUnlit(color);
            return m;
        }

        /// <summary>Unique lit material the caller may animate (owned here).</summary>
        public Material NewLit(Color color)
        {
            var m = Own(new Material(litTemplate) { name = "Lit " + ColorUtility.ToHtmlStringRGB(color) });
            SetColor(m, color);
            return m;
        }

        public Material NewUnlit(Color color, Texture texture = null)
        {
            var m = Own(new Material(unlitTemplate) { name = "Unlit " + ColorUtility.ToHtmlStringRGB(color) });
            SetColor(m, color);
            if (texture != null && m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, texture);
            return m;
        }

        /// <summary>Resources path of the "added on top of the world" palette shader (wish stars, rain).</summary>
        public const string AdditiveShaderPath = "Rendering/Shaders/FloorF_PaletteAdditive";

        /// <summary>
        /// Palette material that ADDS its texels on top of everything the world camera drew (ZTest Always, late queue).
        /// Null when the shader is missing (callers fall back to the opaque palette material).
        /// </summary>
        public Material NewAdditive(Texture texture, string name)
        {
            var shader = Resources.Load<Shader>(AdditiveShaderPath);
            if (shader == null || !shader.isSupported) return null;
            var m = Own(new Material(shader) { name = name });
            m.mainTexture = texture;
            return m;
        }

        // ------------------------------------------------------------------ lit pixel patterns (the hotel's masonry)

        readonly Dictionary<long, Material> patternCache = new Dictionary<long, Material>();
        readonly Dictionary<WallPattern, Texture2D> patternTextures = new Dictionary<WallPattern, Texture2D>();
        readonly List<Material> patternMaterials = new List<Material>();
        readonly List<Vector2Int> patternSizes = new List<Vector2Int>();
        float patternPpu = -1f;

        /// <summary>Pattern materials made so far (tests).</summary>
        public IReadOnlyList<Material> PatternMaterials => patternMaterials;
        public float PatternPixelsPerUnit => patternPpu;

        public bool IsPattern(Material m) => m != null && patternMaterials.Contains(m);

        /// <summary>Repeating grey tile of <paramref name="p"/> (shade / max), point-filtered.</summary>
        public Texture2D PatternTexture(WallPattern p)
        {
            if (patternTextures.TryGetValue(p, out var tex)) return tex;
            var size = PixelPatterns.Size(p);
            tex = Own(new Texture2D(size.x, size.y, TextureFormat.RGBA32, false)
            {
                name = "Pattern " + p,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            });
            tex.SetPixels32(PixelPatterns.Bake(p, Color.white, 1f / PixelPatterns.Max(p)));
            tex.Apply(false, false);
            patternTextures[p] = tex;
            return tex;
        }

        /// <summary>
        /// Shared lit material with pixel pattern <paramref name="p"/> in <paramref name="color"/>. Meshes using it need
        /// screen-plane UVs (<see cref="ProjectUVs"/>); <see cref="ApplyPatternGrid"/> keeps one tile texel = one target
        /// pixel in the active frame.
        /// </summary>
        public Material PatternLit(Color color, WallPattern p)
        {
            long key = ((long)ColorKey(color) << 8) | (long)p;
            if (patternCache.TryGetValue(key, out var m)) return m;
            float max = PixelPatterns.Max(p);
            m = NewLit(new Color(Mathf.Clamp01(color.r * max), Mathf.Clamp01(color.g * max), Mathf.Clamp01(color.b * max), 1f));
            m.name = "Pattern " + p + " " + ColorUtility.ToHtmlStringRGB(color);
            SetTexture(m, PatternTexture(p));
            patternCache[key] = m;
            patternMaterials.Add(m);
            patternSizes.Add(PixelPatterns.Size(p));
            if (patternPpu > 0f) Tile(m, PixelPatterns.Size(p), patternPpu);
            return m;
        }

        static int ColorKey(Color c)
        {
            var c32 = (Color32)c;
            return (c32.r << 16) | (c32.g << 8) | c32.b;
        }

        static void Tile(Material m, Vector2Int size, float ppu)
        {
            m.SetTextureScale(BaseMapId, new Vector2(ppu / size.x, ppu / size.y));
            m.SetTextureOffset(BaseMapId, Vector2.zero);
        }

        /// <summary>Active frame's pixels per unit: pattern texels stay whole target pixels (gameplay and title frames).</summary>
        public void ApplyPatternGrid(float pixelsPerUnit)
        {
            if (Mathf.Approximately(pixelsPerUnit, patternPpu)) return;
            patternPpu = pixelsPerUnit;
            for (int i = 0; i < patternMaterials.Count; i++) Tile(patternMaterials[i], patternSizes[i], pixelsPerUnit);
        }

        /// <summary>
        /// UV = screen-plane position (world units) of every vertex: a planar projection along the camera's oblique view, so
        /// pattern texels land on the screen's pixel lattice on every face (front, top and side alike).
        /// </summary>
        public static void ProjectUVs(Mesh mesh, Matrix4x4 localToWorld, Vector2 shear, float shearDepth)
        {
            var v = mesh.vertices;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = PixelGrid.Project(localToWorld.MultiplyPoint3x4(v[i]), shear, shearDepth);
            mesh.uv = uv;
        }

        /// <summary>Box with its own mesh carrying screen-plane UVs (pattern materials on per-object boxes).</summary>
        public GameObject PatternBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material material, Vector2 shear, float shearDepth, bool castShadows = true)
        {
            var go = Box(parent, name, localCenter, size, material, castShadows);
            var mesh = Own(Object.Instantiate(Cube));
            mesh.name = name;
            ProjectUVs(mesh, go.transform.localToWorldMatrix, shear, shearDepth);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            return go;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            else m.color = color;
        }

        public static void SetTexture(Material m, Texture texture)
        {
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, texture);
            else m.mainTexture = texture;
        }

        /// <summary>Box with a world-space centre/size under <paramref name="parent"/> (parent assumed unscaled).</summary>
        public GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, Material material, bool castShadows = true)
            => MeshObject(parent, name, Cube, localCenter, size, material, castShadows);

        /// <summary>Quad facing -Z (toward the camera).</summary>
        public GameObject Plate(Transform parent, string name, Vector3 localCenter, Vector2 size, Material material)
            => MeshObject(parent, name, Quad, localCenter, new Vector3(size.x, size.y, 1f), material, false);

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Vector3 localCenter, Vector3 scale, Material material, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        public void Dispose()
        {
            foreach (var o in owned)
                if (o != null) Object.Destroy(o);
            owned.Clear();
            litCache.Clear();
            unlitCache.Clear();
            swatchCubes.Clear();
            patternCache.Clear();
            patternTextures.Clear();
            patternMaterials.Clear();
            patternSizes.Clear();
        }
    }

    /// <summary>Procedural unit meshes (no CreatePrimitive: it adds colliders and drags physics into the WebGL build).</summary>
    static class MeshFactory
    {
        public static Mesh CreateCube()
        {
            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var t = new List<int>(36);
            AddFace(Vector3.right, Vector3.up, v, n, uv, t);
            AddFace(Vector3.left, Vector3.up, v, n, uv, t);
            AddFace(Vector3.up, Vector3.forward, v, n, uv, t);
            AddFace(Vector3.down, Vector3.back, v, n, uv, t);
            AddFace(Vector3.forward, Vector3.up, v, n, uv, t);
            AddFace(Vector3.back, Vector3.up, v, n, uv, t);
            var mesh = new Mesh { name = "FloorF Cube" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>1x1 quad in the XY plane, visible from -Z.</summary>
        public static Mesh CreateQuad()
        {
            var v = new List<Vector3>(4);
            var n = new List<Vector3>(4);
            var uv = new List<Vector2>(4);
            var t = new List<int>(6);
            AddFace(Vector3.back, Vector3.up, v, n, uv, t, 0f);
            var mesh = new Mesh { name = "FloorF Quad" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Face with outward normal <paramref name="normal"/>, wound clockwise as seen from outside (Unity front face).</summary>
        static void AddFace(Vector3 normal, Vector3 up, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, float offset = 0.5f)
        {
            var right = Vector3.Cross(normal, up);
            var c = normal * offset;
            int i = v.Count;
            v.Add(c - right * 0.5f - up * 0.5f);
            v.Add(c - right * 0.5f + up * 0.5f);
            v.Add(c + right * 0.5f + up * 0.5f);
            v.Add(c + right * 0.5f - up * 0.5f);
            for (int k = 0; k < 4; k++) n.Add(normal);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
    }
}
