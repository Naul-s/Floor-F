using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Phase 3b: Kenney CC0 characters for guests (one tinted character per archetype, shared atlas material, idle/walk
    /// clips) and Kenney furniture/arcade props per destination (merged per floor, inside the prop zone left of the
    /// floor sign). Writes captures/render_floors.png and render_stats.txt (draw calls / triangles).
    /// </summary>
    public class KenneyModelTests
    {
        const string AtlasAssetPath = "Assets/_Project/Resources/Rendering/Kenney/KenneyAtlas.png";

        static readonly Dictionary<string, string[]> RequiredProps = new Dictionary<string, string[]>
        {
            { "lobby", new[] { "desk", "pottedPlant", "loungeSofa", "cash-register" } },
            { "restaurant", new[] { "tableCloth", "chairCushion", "chair" } },
            { "gym", new[] { "bench", "cube:iron", "cube:chrome" } },
            { "karaoke", new[] { "speaker", "televisionModern", "dance-machine", "loungeSofa" } },
            { "sauna", new[] { "bathtub", "shower", "bench" } },
            { "rooms", new[] { "bedDouble", "bedSingle", "lampRoundTable", "cabinetBedDrawerTable" } },
            { "bar", new[] { "kitchenBar", "stoolBar", "lampSquareFloor" } },
            { "casino", new[] { "gambling-machine", "prize-wheel", "arcade-machine" } },
            { "rooftop", new[] { "pottedPlant", "loungeChair", "bench" } },
        };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static KenneyCatalog Catalog()
        {
            var c = Resources.Load<KenneyCatalog>(KenneyCatalog.ResourcePath);
            Assert.IsNotNull(c, "Resources/" + KenneyCatalog.ResourcePath + " (run KenneyCatalogBuilder.Build)");
            Assert.IsNotNull(c.Material, "atlas material");
            return c;
        }

        static Texture2D LoadAtlasPixels()
        {
            // the imported atlas is not CPU-readable (WebGL memory); read the source PNG instead
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(AtlasAssetPath)), AtlasAssetPath);
            return tex;
        }

        [UnityTest]
        public IEnumerator GuestModels_MappedPerArchetype_AndTinted()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var data = GameServices.Get<GameData>();
            var catalog = Catalog();
            var atlas = LoadAtlasPixels();
            try
            {
                var tiles = new HashSet<int>();
                foreach (var g in catalog.Guests)
                {
                    Assert.IsNotNull(g.Prefab, g.Accessory + " prefab");
                    Assert.IsTrue(tiles.Add(g.Tile), g.Accessory + "/" + g.Variant + " has its own atlas tile");
                    var px = atlas.GetPixelBilinear(g.ShirtUV.x, g.ShirtUV.y);
                    Assert.AreEqual(g.Shirt.r, px.r, 0.25f, g.Character + " shirt swatch painted r");
                    Assert.AreEqual(g.Shirt.g, px.g, 0.25f, g.Character + " shirt swatch painted g");
                    Assert.AreEqual(g.Shirt.b, px.b, 0.25f, g.Character + " shirt swatch painted b");
                }
                // priest = dark cassock (shirt far darker than the guests.json colour), every other plain guest wears its colour
                var priest = catalog.FindGuest("collar", 0);
                Assert.IsNotNull(priest, "western_priest has its own model");
                Assert.Less(priest.Shirt.grayscale, 0.2f, "priest wears dark clothes");
                Assert.IsNotNull(catalog.FindGuest("flat_cap", 0), "italian_nonno has its own model");
                Assert.AreEqual(2, catalog.VariantCount("matching_hats"), "couple = two characters");
                Assert.AreNotEqual(catalog.FindGuest("matching_hats", 0).Character, catalog.FindGuest("matching_hats", 1).Character);

                var characters = new HashSet<string>();
                foreach (var def in data.Guests)
                {
                    var entry = catalog.FindGuest(def.accessory, 0);
                    if (entry == null)
                    {
                        // archetype added to guests.json after the catalog was built: runtime stand-in until
                        // FloorF.EditorTools.KenneyCatalogBuilder.Build is re-run (it auto-maps new accessories)
                        Assert.IsNotNull(catalog.FindClosestTint(GuestFigure.Vivid(CoreUnityExtensions.ParseColor(def.color))), def.id + " stand-in model");
                        Debug.LogWarning("[KenneyModelTests] " + def.id + " (" + def.accessory + ") not in the Kenney catalog - rebuild it");
                        continue;
                    }
                    Assert.IsTrue(characters.Add(entry.Character), def.id + " uses a character of its own");
                    var vivid = GuestFigure.Vivid(CoreUnityExtensions.ParseColor(def.color));
                    Assert.AreEqual(vivid.r, entry.Tint.r, 0.02f, def.id + " tint = guests.json colour");
                    Assert.AreEqual(vivid.g, entry.Tint.g, 0.02f, def.id + " tint g");
                    Assert.AreEqual(vivid.b, entry.Tint.b, 0.02f, def.id + " tint b");
                }

                var figs = root.Guests.BuildLineup(new FloorIndex(0), data.Guests, GuestMood.Neutral);
                var mats = new HashSet<Material>();
                for (int i = 0; i < figs.Count; i++)
                {
                    var f = figs[i];
                    Assert.IsTrue(f.IsModel, data.Guests[i].id + " is a Kenney model");
                    var smrs = f.Root.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Assert.GreaterOrEqual(smrs.Length, 2, "body + head meshes");
                    foreach (var s in smrs) mats.Add(s.sharedMaterial);
                    Assert.IsNotNull(f.Visual.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("Rig ")), "rig");
                }
                Assert.AreEqual(1, mats.Count, "every guest model draws with the one atlas material");
                Assert.AreSame(catalog.Material, mats.First());
                var hood = figs[data.Guests.ToList().FindIndex(d => d.accessory == "hood")];
                Assert.IsNotNull(hood.Visual.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Eye L"), "mystery keeps glowing eyes");

                // idle clip animates on the juice clock; walking switches the clip and turns the model
                var fig = figs[0];
                var bone = fig.ArmR;
                Time.captureDeltaTime = 0.1f;
                yield return null;
                var a = bone.localRotation;
                for (int i = 0; i < 4; i++) yield return null;
                Assert.Greater(Quaternion.Angle(a, bone.localRotation), 0.5f, "idle animation moves the arms");
                fig.SetMotion(2f, 0.5f);
                Assert.IsTrue(fig.Walking);
                Assert.Less(fig.Yaw, GuestFigure.FacingCamera - 10f, "turns toward +X when walking right");
                fig.SetMotion(0f, 1f);
                Assert.IsFalse(fig.Walking);
                Assert.AreEqual(GuestFigure.FacingCamera, fig.Yaw, 1e-3f, "faces the camera again when standing");
            }
            finally { Object.Destroy(atlas); }
        }

        [UnityTest]
        public IEnumerator FloorProps_PerDestination_AvoidShaftAndQueue()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var building = GameServices.Get<IBuildingQuery>();
            var layout = GameServices.Get<IWorldLayout>();
            var view = root.Building;
            Catalog();
            var broot = view.Root;
            float zoneMax = broot.TransformPoint(new Vector3(view.PropZoneMaxX, 0f, 0f)).x;
            float zoneMin = broot.TransformPoint(new Vector3(view.PropZoneMinX, 0f, 0f)).x;
            float shaftLeft = broot.TransformPoint(new Vector3(layout.ShaftX - view.ShaftHalfWidth, 0f, 0f)).x;
            float wallIn = broot.TransformPoint(new Vector3(layout.ShaftX - layout.FloorWidth * 0.5f, 0f, 0f)).x;
            float d = layout.Depth;
            Assert.Less(zoneMax, shaftLeft, "zone ends before the shaft");

            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                var dest = building.GetDestination(f).Value;
                var props = view.GetProps(f);
                Assert.Greater(props.Count, 2, "floor " + f.PhysicalNumber + " (" + dest + ") dressed");
                if (RequiredProps.TryGetValue(dest, out var required))
                    foreach (var r in required)
                        Assert.IsTrue(props.Any(p => p.Name == r), dest + " has " + r + " (has: " + string.Join(",", props.Select(p => p.Name)) + ")");

                var sign = view.GetSign(f).GetComponentInChildren<MeshRenderer>().bounds;
                var queue = layout.GetWaitingSlot(f, 0).ToVector3();
                float y0 = broot.TransformPoint(new Vector3(0f, f.Value * layout.FloorHeight, 0f)).y;
                foreach (var p in props)
                {
                    var b = p.Bounds;
                    string at = "floor " + f.PhysicalNumber + " " + p.Name + " " + b.min.ToString("0.00") + ".." + b.max.ToString("0.00");
                    Assert.LessOrEqual(b.max.x, zoneMax + 1e-3f, at + ": inside the prop zone");
                    Assert.Less(b.max.x, sign.min.x, at + ": left of the floor sign");
                    Assert.Less(b.max.x, shaftLeft, at + ": clear of the shaft");
                    Assert.Less(b.max.x, queue.x - 1f, at + ": clear of the waiting line");
                    Assert.GreaterOrEqual(b.min.x, wallIn - 1e-3f, at + ": inside the left wall");
                    Assert.GreaterOrEqual(b.min.x, zoneMin - 1e-3f, at);
                    Assert.GreaterOrEqual(b.min.y, y0 - 0.02f, at + ": stands on its floor");
                    Assert.LessOrEqual(b.max.y, y0 + layout.FloorHeight - 0.3f + 1e-3f, at + ": below the ceiling");
                    Assert.GreaterOrEqual(b.min.z, -d * 0.5f - 0.01f, at + ": inside the cut-away");
                    Assert.LessOrEqual(b.max.z, d * 0.5f + 0.01f, at + ": in front of the back wall");
                }
            }

            // capture: all nine floors' prop zones, nearest-upscaled from the low-res target
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 4; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = false;
            var full = FrameCapture.Capture("render_floors_frame");
            Assert.IsTrue(File.Exists(full), full);
            var crop = RenderingTestDriver.CaptureTargetCrop("render_floors",
                new Vector3(wallIn - 0.3f, -0.3f, -d * 0.5f), new Vector3(shaftLeft + 0.2f, layout.FloorCount * layout.FloorHeight + 0.2f, -d * 0.5f), 3);
            Assert.IsTrue(File.Exists(crop), crop);
        }

        [UnityTest]
        public IEnumerator Props_ShareMaterials()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var catalog = Catalog();
            var view = root.Building;
            var mats = new HashSet<Material>();
            int propTris = 0, propParts = 0;
            for (int i = 0; i < view.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                var r = view.GetPropRenderer(f);
                Assert.IsNotNull(r, "floor " + f.PhysicalNumber + " props merged into one renderer");
                Assert.AreEqual(1, r.sharedMaterials.Length, "single submesh");
                foreach (var m in r.sharedMaterials) mats.Add(m);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, "no shadows");
                propTris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                propParts += view.GetProps(f).Count;
            }
            Assert.AreEqual(1, mats.Count, "all props share one material");
            Assert.AreSame(catalog.Material, mats.First(), "the Kenney atlas material");
            Assert.LessOrEqual(propTris, 40000, "prop triangle budget");

            // real guests on the atlas material too
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.1f;
            var guests = GameServices.Get<IGuestQuery>();
            for (int i = 0; i < 200 && guests.ActiveGuests.Count < 3; i++) yield return null;
            var guestMats = new HashSet<Material>();
            int guestTris = 0;
            foreach (var id in root.Guests.Ids)
                foreach (var s in root.Guests.GetFigure(id).Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    guestMats.Add(s.sharedMaterial);
                    guestTris += s.sharedMesh.triangles.Length / 3;
                }
            Assert.Greater(root.Guests.Count, 0);
            Assert.AreEqual(1, guestMats.Count, "guest models share one material");
            Assert.AreSame(catalog.Material, guestMats.First());

            // frame stats for inspection (editor counters; 0 where the backend does not report them)
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"))
            using (var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            {
                for (int i = 0; i < 5; i++) yield return null;
                var report = "[RenderStats] drawCalls=" + draws.LastValue + " batches=" + batches.LastValue + " setPass=" + setPass.LastValue +
                             " triangles=" + tris.LastValue + " | props: " + view.FloorCount + " renderers, " + propParts + " parts, " + propTris +
                             " tris, materials=" + mats.Count + " | guests: " + root.Guests.Count + " (" + guestTris + " tris), materials=" + guestMats.Count;
                Debug.Log(report);
                TestContext.WriteLine(report);
                Directory.CreateDirectory(FrameCapture.OutputDir);
                File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_stats.txt"), report + "\n");
            }
        }
    }
}
