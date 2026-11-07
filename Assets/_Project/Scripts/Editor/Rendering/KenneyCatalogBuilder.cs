using System.Collections.Generic;
using System.IO;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Rendering;
using UnityEditor;
using UnityEngine;

namespace FloorF.EditorTools
{
    /// <summary>
    /// Builds Resources/Rendering/Kenney (KenneyCatalog.asset + KenneyAtlas.png + KenneyLit.mat + one prefab per guest
    /// character) from the Kenney CC0 FBX under ThirdParty/Kenney. Never touches the FBX or their importers.
    ///  - atlas 256x256, 4x4 tiles of 64px: tile 0 = colormap (downsampled 512->64, swatches stay swatches),
    ///    tiles 1.. = per-character colormap copies with the shirt swatch recoloured to the guest colour,
    ///    tile 15 = 8px swatches for the Furniture Kit material colours + extras for primitive props.
    ///  - props: all renderers of an FBX merged into one single-submesh mesh, UVs pointed at swatches (furniture) or
    ///    remapped into tile 0 (arcade), scaled to world units, pivot bottom-centre, front facing -Z.
    ///  - guests: FBX hierarchy (bones) with mesh copies whose UVs point into the character's tile, shared material,
    ///    no Animation component (FloorF.Rendering samples the idle/walk clips itself).
    /// Idempotent. Batch: tools/unity-batch.ps1 -Mode method -Method FloorF.EditorTools.KenneyCatalogBuilder.Build
    /// </summary>
    public static class KenneyCatalogBuilder
    {
        public const string ThirdParty = "Assets/_Project/ThirdParty/Kenney";
        const string Out = "Assets/_Project/Resources/Rendering/Kenney";
        const string AtlasPath = Out + "/KenneyAtlas.png";
        const string MaterialPath = Out + "/KenneyLit.mat";
        const string CatalogPath = Out + "/KenneyCatalog.asset";
        const string GuestFolder = Out + "/Guests";

        const int AtlasSize = 256, Tile = 64, TilesPerRow = AtlasSize / Tile, PaletteTile = 15, SwatchPx = 8;

        /// <summary>Furniture Kit units -> world units (chair 4.7 -> 0.94).</summary>
        const float FurnitureScale = 0.2f;
        /// <summary>Mini Arcade units (cm-ish) -> world units (slot machine 77.6 -> 1.9).</summary>
        const float ArcadeScale = 0.025f;
        /// <summary>Kenney models face +Z; the camera looks along +Z, so props are turned to face -Z.</summary>
        const float PropYaw = 180f;

        /// <summary>guests.json accessory -> Kenney character(s). Couples: female + male (same hats added at runtime).</summary>
        static readonly (string accessory, int variant, string character, string colorHex)[] GuestMap =
        {
            ("perm_hair", 0, "character-female-d", "#C9A0DC"),     // bun + blazer -> grandma (perm + handbag added)
            ("briefcase", 0, "character-male-d", "#B22222"),       // suit + tie -> businessman
            ("camera", 0, "character-male-f", "#4A90D9"),          // t-shirt + shorts -> tourist
            ("matching_hats", 0, "character-female-b", "#F4A6B8"), // newlyweds: she...
            ("matching_hats", 1, "character-male-b", "#F4A6B8"),   // ...and he, same hats
            ("visor", 0, "character-female-f", "#2E8B57"),         // gambler
            ("glasses", 0, "character-male-e", "#708090"),         // glasses + shirt -> accountant
            ("hood", 0, "character-male-a", "#2B2B2B"),            // darkened, hooded, glowing eyes
        };

        static readonly string[] FurnitureProps =
        {
            "desk", "pottedPlant", "plantSmall1", "plantSmall2", "loungeSofa", "loungeSofaLong", "loungeChair", "table", "tableCloth",
            "tableRound", "chair", "chairCushion", "bench", "benchCushion", "speaker", "speakerSmall", "televisionModern",
            "bathtub", "shower", "bedDouble", "bedSingle", "lampRoundTable", "lampSquareFloor", "cabinetBedDrawerTable",
            "kitchenBar", "kitchenBarEnd", "stoolBar", "stoolBarSquare", "rugRectangle", "rugRound", "sideTable",
            "bookcaseOpenLow", "kitchenFridgeSmall", "coatRackStanding", "radio", "trashcan", "tableCoffee",
        };

        static readonly string[] ArcadeProps =
            { "cash-register", "gambling-machine", "prize-wheel", "arcade-machine", "dance-machine", "claw-machine", "vending-machine" };

        /// <summary>Extra flat swatches for primitive pieces (dumbbells, felt, screens) so they share the atlas material.</summary>
        static readonly (string name, Color color)[] ExtraSwatches =
        {
            ("iron", new Color(0.14f, 0.14f, 0.16f)),
            ("chrome", new Color(0.78f, 0.80f, 0.84f)),
            ("felt", new Color(0.10f, 0.46f, 0.26f)),
            ("screen", new Color(0.40f, 0.90f, 1.00f)),
            ("gold", new Color(1.00f, 0.80f, 0.25f)),
            ("neon", new Color(1.00f, 0.35f, 0.85f)),
            ("towel", new Color(0.96f, 0.94f, 0.88f)),
            ("coal", new Color(1.00f, 0.40f, 0.12f)),
            ("stone", new Color(0.36f, 0.34f, 0.33f)),
            ("bottle", new Color(0.85f, 0.55f, 0.22f)),
            ("purple", new Color(0.48f, 0.26f, 0.66f)),
            ("umbrella", new Color(0.86f, 0.20f, 0.20f)),
        };


        [MenuItem("Floor F/Rendering/Rebuild Kenney Catalog")]
        public static void Build()
        {
            Directory.CreateDirectory(Out);
            Directory.CreateDirectory(GuestFolder);
            AssetDatabase.Refresh();

            var colormapPath = ThirdParty + "/MiniCharacters/Textures/colormap.png";
            var colormap = LoadReadable(colormapPath);
            var atlas = new Color32[AtlasSize * AtlasSize];
            for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color32(0, 0, 0, 255);
            var baseTile = Downsample(colormap);
            BlitTile(atlas, 0, baseTile);

            // ---------------------------------------------------------------- swatches (tile 15)
            var swatches = new List<KenneyCatalog.Swatch>();
            var swatchIndex = new Dictionary<string, int>();
            int AddSwatch(string name, Color c)
            {
                if (swatchIndex.TryGetValue(name, out var idx)) return idx;
                idx = swatches.Count;
                if (idx >= (Tile / SwatchPx) * (Tile / SwatchPx)) throw new System.InvalidOperationException("swatch tile full");
                int cx = idx % (Tile / SwatchPx), cy = idx / (Tile / SwatchPx);
                var origin = TileOrigin(PaletteTile);
                for (int y = 0; y < SwatchPx; y++)
                    for (int x = 0; x < SwatchPx; x++)
                        atlas[(origin.y + cy * SwatchPx + y) * AtlasSize + origin.x + cx * SwatchPx + x] = c;
                var uv = new Vector2((origin.x + cx * SwatchPx + SwatchPx * 0.5f) / AtlasSize, (origin.y + cy * SwatchPx + SwatchPx * 0.5f) / AtlasSize);
                swatches.Add(new KenneyCatalog.Swatch { Name = name, Color = c, UV = uv });
                swatchIndex[name] = idx;
                return idx;
            }
            foreach (var e in ExtraSwatches) AddSwatch(e.name, e.color);

            // ---------------------------------------------------------------- props
            var catalog = AssetDatabase.LoadAssetAtPath<KenneyCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<KenneyCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            // drop old sub-assets (meshes) so a rebuild does not accumulate them
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(CatalogPath))
                if (sub != catalog && sub is Mesh) Object.DestroyImmediate(sub, true);

            var props = new List<KenneyCatalog.PropModel>();
            var report = new System.Text.StringBuilder();
            foreach (var name in FurnitureProps)
            {
                var mesh = BakeProp(ThirdParty + "/FurnitureKit/" + name + ".fbx", FurnitureScale, (mat, uv) =>
                {
                    var key = mat != null ? mat.name : "_defaultMat";
                    var c = mat == null ? Color.white : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.color : Color.white;
                    return swatches[AddSwatch(key, c)].UV;
                });
                if (mesh == null) continue;
                AssetDatabase.AddObjectToAsset(mesh, catalog);
                props.Add(new KenneyCatalog.PropModel { Name = name, Mesh = mesh });
                report.AppendLine("prop " + name + " size=" + mesh.bounds.size.ToString("0.00") + " tris=" + mesh.triangles.Length / 3);
            }
            foreach (var name in ArcadeProps)
            {
                var mesh = BakeProp(ThirdParty + "/MiniArcade/" + name + ".fbx", ArcadeScale, (mat, uv) => RemapToTile(uv, 0));
                if (mesh == null) continue;
                AssetDatabase.AddObjectToAsset(mesh, catalog);
                props.Add(new KenneyCatalog.PropModel { Name = name, Mesh = mesh });
                report.AppendLine("prop " + name + " size=" + mesh.bounds.size.ToString("0.00") + " tris=" + mesh.triangles.Length / 3);
            }

            // ---------------------------------------------------------------- material (needs the atlas first)
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            // ---------------------------------------------------------------- guests: tiles + meshes (prefabs after the atlas exists)
            var guestEntries = new List<(KenneyCatalog.GuestModel entry, GameObject instance)>();
            int tile = 1;
            var guestDefs = JsonUtility.FromJson<GuestCatalogDef>(File.ReadAllText("Assets/_Project/Resources/Data/guests.json"));
            foreach (var g in GuestMap)
            {
                var fbx = ThirdParty + "/MiniCharacters/" + g.character + ".fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (src == null) throw new System.InvalidOperationException("missing " + fbx);
                var inst = Object.Instantiate(src);
                inst.name = g.character;
                var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var body = smrs.First(s => s.name == "body-mesh");
                var head = smrs.First(s => s.name == "head-mesh");

                // tint = guests.json colour of the archetype wearing this accessory (map value is only a fallback)
                var def = guestDefs?.guests?.FirstOrDefault(d => d.accessory == g.accessory);
                var tint = GuestFigure.Vivid(CoreUnityExtensions.ParseColor(def != null && !string.IsNullOrEmpty(def.color) ? def.color : g.colorHex));
                bool mystery = g.accessory == "hood";
                // the shirt swatch is often shared with hair/eyes on the head mesh: copy it into an unused swatch of
                // this character's tile, recolour the copy and point only the BODY mesh's shirt UVs at it
                var shirt = ShirtCell(body);
                var tilePx = (Color32[])baseTile.Clone();
                if (mystery) for (int i = 0; i < tilePx.Length; i++) tilePx[i] = Darken(tilePx[i], 0.22f);
                CopyCell(tilePx, shirt, SpareCell);
                RecolorCell(tilePx, SpareCell, mystery ? tint * 0.9f : tint);
                BlitTile(atlas, tile, tilePx);
                var shirtUV = RemapToTile(new Vector2((SpareCell.x + 0.5f) / 16f, (SpareCell.y + 0.5f) / 4f), tile);

                var entry = new KenneyCatalog.GuestModel
                {
                    Accessory = g.accessory, Variant = g.variant, Character = g.character, Tile = tile, Tint = tint, ShirtUV = shirtUV,
                };
                var bounds = new Bounds();
                bool first = true;
                foreach (var s in smrs)
                {
                    var m = Object.Instantiate(s.sharedMesh);
                    m.name = g.character + " " + s.name + " t" + tile;
                    var uvs = new List<Vector2>();
                    m.GetUVs(0, uvs);
                    for (int i = 0; i < uvs.Count; i++)
                    {
                        var uv = uvs[i];
                        if (s == body && CellOf(uv) == shirt)
                            uv = new Vector2((SpareCell.x + Frac(uv.x * 16f)) / 16f, (SpareCell.y + Frac(uv.y * 4f)) / 4f);
                        uvs[i] = RemapToTile(uv, tile);
                    }
                    m.SetUVs(0, uvs);
                    AssetDatabase.AddObjectToAsset(m, catalog);
                    s.sharedMesh = m;
                    s.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    s.receiveShadows = false;
                    s.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    s.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    s.skinnedMotionVectors = false;
                    var baked = new Mesh();
                    s.BakeMesh(baked, true);
                    var bb = TransformBounds(baked.bounds, inst.transform.worldToLocalMatrix * s.transform.localToWorldMatrix);
                    Object.DestroyImmediate(baked);
                    if (s == head) entry.Head = bb;
                    if (first) { bounds = bb; first = false; } else bounds.Encapsulate(bb);
                }
                entry.Bounds = bounds;
                foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                guestEntries.Add((entry, inst));
                report.AppendLine("guest " + g.accessory + "/" + g.variant + " " + g.character + " tile=" + tile + " shirt=" + shirt +
                                  " bounds=" + bounds.size.ToString("0.0") + " head=" + entry.Head.size.ToString("0.0") + " headCenter=" + entry.Head.center.ToString("0.0"));
                tile++;
            }

            // ---------------------------------------------------------------- write atlas + material
            var tex = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(atlas);
            tex.Apply();
            File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
            ti.sRGBTexture = true;
            ti.maxTextureSize = AtlasSize;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.SaveAndReimport();
            var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new System.InvalidOperationException("Simple Lit shader missing");
            bool createMat = material == null;
            if (createMat) material = new Material(shader) { name = "KenneyLit" };
            else material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", atlasTex);
            SetFloat(material, "_Smoothness", 0.1f);
            SetFloat(material, "_SpecularHighlights", 0f);
            material.DisableKeyword("_SPECULAR_COLOR");
            SetFloat(material, "_EnvironmentReflections", 0f);
            material.enableInstancing = false;
            if (createMat) AssetDatabase.CreateAsset(material, MaterialPath);
            else EditorUtility.SetDirty(material);

            // ---------------------------------------------------------------- guest prefabs
            var guests = new List<KenneyCatalog.GuestModel>();
            foreach (var (entry, inst) in guestEntries)
            {
                foreach (var s in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.sharedMaterial = material;
                var path = GuestFolder + "/" + entry.Accessory + "_" + entry.Variant + ".prefab";
                entry.Prefab = PrefabUtility.SaveAsPrefabAsset(inst, path);
                Object.DestroyImmediate(inst);
                guests.Add(entry);
            }

            // ---------------------------------------------------------------- clips (shared skeleton: one set for all)
            var clipSource = ThirdParty + "/MiniCharacters/character-male-a.fbx";
            var clips = AssetDatabase.LoadAllAssetsAtPath(clipSource).OfType<AnimationClip>().ToList();
            catalog.Idle = clips.FirstOrDefault(c => c.name == "idle");
            catalog.Walk = clips.FirstOrDefault(c => c.name == "walk");

            catalog.Material = material;
            catalog.Atlas = atlasTex;
            catalog.Guests = guests.ToArray();
            catalog.Props = props.ToArray();
            catalog.Swatches = swatches.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteReport(report.ToString());
            Debug.Log("[KenneyCatalogBuilder] " + props.Count + " props, " + guests.Count + " guest models, " + swatches.Count + " swatches -> " + Out + "\n" + report);
        }

        static void WriteReport(string text)
        {
            var dir = System.Environment.GetEnvironmentVariable("FLOORF_HARNESS_OUT");
            if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "kenney_catalog.txt"), text);
        }

        static void SetFloat(Material m, string name, float v)
        {
            if (m.HasProperty(name)) m.SetFloat(name, v);
        }

        // ------------------------------------------------------------------ atlas helpers

        static Texture2D LoadReadable(string assetPath)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(assetPath));
            return t;
        }

        /// <summary>Point-samples the 512 colormap down to a 64 tile (swatches are 32x128 -> 4x16).</summary>
        static Color32[] Downsample(Texture2D src)
        {
            var px = src.GetPixels32();
            var o = new Color32[Tile * Tile];
            int step = src.width / Tile;
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    o[y * Tile + x] = px[(y * step + step / 2) * src.width + x * step + step / 2];
            return o;
        }

        static Vector2Int TileOrigin(int tile) => new Vector2Int((tile % TilesPerRow) * Tile, (tile / TilesPerRow) * Tile);

        static void BlitTile(Color32[] atlas, int tile, Color32[] px)
        {
            var o = TileOrigin(tile);
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    atlas[(o.y + y) * AtlasSize + o.x + x] = px[y * Tile + x];
        }

        static Vector2 RemapToTile(Vector2 uv, int tile)
        {
            var o = TileOrigin(tile);
            float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
            // keep a half-texel inset so point sampling never crosses into the neighbour tile
            u = Mathf.Clamp(u, 0.5f / Tile, 1f - 0.5f / Tile);
            v = Mathf.Clamp(v, 0.5f / Tile, 1f - 0.5f / Tile);
            return new Vector2((o.x + u * Tile) / AtlasSize, (o.y + v * Tile) / AtlasSize);
        }

        /// <summary>Colormap swatch (16 columns x 4 bands) of a UV.</summary>
        static Vector2Int CellOf(Vector2 uv) => new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(uv.x * 16f), 0, 15), Mathf.Clamp(Mathf.FloorToInt(uv.y * 4f), 0, 3));

        /// <summary>Top band of the Kenney colormap is unused black: the recoloured shirt copy goes there.</summary>
        static readonly Vector2Int SpareCell = new Vector2Int(15, 3);

        static float Frac(float x) => Mathf.Clamp(x - Mathf.Floor(x), 0.02f, 0.98f);

        static void CopyCell(Color32[] tile, Vector2Int from, Vector2Int to)
        {
            int w = Tile / 16, h = Tile / 4;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tile[(to.y * h + y) * Tile + to.x * w + x] = tile[(from.y * h + y) * Tile + from.x * w + x];
        }

        /// <summary>
        /// The non-skin swatch covering the most torso-bound triangle area of the body mesh.
        /// </summary>
        static Vector2Int ShirtCell(SkinnedMeshRenderer body)
        {
            // chest band in bind pose: above the hip (torso bone + a bit), below the shoulders, inside the arms
            var m = body.sharedMesh;
            var torso = body.bones.FirstOrDefault(t => t != null && t.name == "torso");
            var arm = body.bones.FirstOrDefault(t => t != null && t.name == "arm-left");
            var toMesh = body.transform.worldToLocalMatrix;
            float yLo = torso != null ? toMesh.MultiplyPoint3x4(torso.position).y + 4f : 20f;
            float yHi = arm != null ? toMesh.MultiplyPoint3x4(arm.position).y + 1f : 30f;
            float xMax = arm != null ? Mathf.Abs(toMesh.MultiplyPoint3x4(arm.position).x) - 0.5f : 9f;
            var verts = m.vertices;
            var uvs = m.uv;
            var tris = m.triangles;
            var area = new Dictionary<Vector2Int, float>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                var centre = (verts[a] + verts[b] + verts[c]) / 3f;
                if (centre.y < yLo || centre.y > yHi || Mathf.Abs(centre.x) > xMax) continue;
                var cell = CellOf((uvs[a] + uvs[b] + uvs[c]) / 3f);
                if (IsSkinCell(cell)) continue;
                float ar = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude;
                area.TryGetValue(cell, out var sum);
                area[cell] = sum + ar;
            }
            if (area.Count == 0) return new Vector2Int(8, 1);
            return area.OrderByDescending(kv => kv.Value).First().Key;
        }

        /// <summary>Kenney colormap skin swatches: cream (band 1, columns 0-1), salmon/brown/tan (band 0, columns 10-15).</summary>
        static bool IsSkinCell(Vector2Int c) => (c.y == 1 && c.x <= 1) || (c.y == 0 && c.x >= 10);

        /// <summary>Repaints one colormap swatch with <paramref name="tint"/>, keeping its light-to-dark gradient.</summary>
        static void RecolorCell(Color32[] tile, Vector2Int cell, Color tint)
        {
            int w = Tile / 16, h = Tile / 4;
            float sum = 0f; int n = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) { sum += Lum(tile[(cell.y * h + y) * Tile + cell.x * w + x]); n++; }
            float mean = Mathf.Max(0.05f, sum / n);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (cell.y * h + y) * Tile + cell.x * w + x;
                    float k = Mathf.Clamp(Lum(tile[i]) / mean, 0.75f, 1.2f);
                    var c = tint * k;
                    c.a = 1f;
                    tile[i] = c;
                }
        }

        static float Lum(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static Color32 Darken(Color32 c, float k) => new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var c = b.center; var e = b.extents;
            var r = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return r;
        }

        // ------------------------------------------------------------------ props

        /// <summary>Merges every MeshRenderer of the FBX into one mesh (one submesh), UVs from <paramref name="uvOf"/>.</summary>
        static Mesh BakeProp(string fbx, float scale, System.Func<Material, Vector2, Vector2> uvOf)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogWarning("[KenneyCatalogBuilder] missing " + fbx); return null; }
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var r in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.Contains("%ignore")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = mf.sharedMesh;
                var mat = r.transform.localToWorldMatrix; // asset root sits at the origin; keep its import scale/rotation
                var nmat = mat.inverse.transpose;
                var mv = m.vertices; var mn = m.normals; var mu = m.uv;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var material = s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null;
                    var idx = m.GetTriangles(s);
                    var map = new Dictionary<int, int>();
                    foreach (var vi in idx)
                    {
                        if (!map.TryGetValue(vi, out var ni))
                        {
                            ni = verts.Count;
                            map[vi] = ni;
                            verts.Add(mat.MultiplyPoint3x4(mv[vi]));
                            normals.Add(mn.Length == mv.Length ? nmat.MultiplyVector(mn[vi]).normalized : Vector3.up);
                            uvs.Add(uvOf(material, mu.Length == mv.Length ? mu[vi] : Vector2.zero));
                        }
                        tris.Add(ni);
                    }
                }
            }
            if (verts.Count == 0) return null;
            // normalise: scale, face -Z, pivot bottom-centre
            var rot = Quaternion.Euler(0f, PropYaw, 0f);
            for (int i = 0; i < verts.Count; i++) { verts[i] = rot * (verts[i] * scale); normals[i] = rot * normals[i]; }
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            var shift = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            for (int i = 0; i < verts.Count; i++) verts[i] += shift;
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(fbx) };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
