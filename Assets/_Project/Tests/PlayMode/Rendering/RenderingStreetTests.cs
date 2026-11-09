using System.Collections;
using System.Collections.Generic;
using System.IO;
using FloorF.Core;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Exterior on the real Main scene: facade dressing, living city lights, street life (cars + pedestrians) - budgets,
    /// seeded determinism, nothing in front of the hotel floors' cut-away. Captures render_title_street,
    /// render_day_street and zoomed facade / sign / skyline crops; writes render_street_stats.txt.
    /// </summary>
    public class RenderingStreetTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static IWorldLayout Layout => GameServices.Get<IWorldLayout>();

        /// <summary>Screen rectangle (low-res pixels) of the floors' cut-away: walls' inner faces, floor 1 to the roof slab.</summary>
        static Rect FloorsRect(Camera cam)
        {
            var l = Layout;
            float z0 = -l.Depth * 0.5f;
            var a = cam.WorldToViewportPoint(new Vector3(l.ShaftX - l.FloorWidth * 0.5f, 0f, z0));
            var b = cam.WorldToViewportPoint(new Vector3(l.ShaftX + l.FloorWidth * 0.5f, l.FloorCount * l.FloorHeight - 0.3f, z0));
            return Rect.MinMaxRect(a.x * 640f, a.y * 360f, b.x * 640f, b.y * 360f);
        }

        static Rect ScreenRect(Camera cam, IEnumerable<Vector3> points)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in points)
            {
                var v = cam.WorldToViewportPoint(p);
                x0 = Mathf.Min(x0, v.x * 640f); x1 = Mathf.Max(x1, v.x * 640f);
                y0 = Mathf.Min(y0, v.y * 360f); y1 = Mathf.Max(y1, v.y * 360f);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }

        static bool Overlaps(Rect a, Rect b, float tolerancePx)
            => a.xMin < b.xMax - tolerancePx && a.xMax > b.xMin + tolerancePx && a.yMin < b.yMax - tolerancePx && a.yMax > b.yMin + tolerancePx;

        /// <summary>Every triangle that lies entirely in front of the hotel front must stay off the floors' rectangle.</summary>
        static void AssertMeshClearOfFloors(Camera cam, Rect floors, MeshFilter mf, string what)
        {
            var mesh = mf.sharedMesh;
            var m = mf.transform.localToWorldMatrix;
            var v = mesh.vertices;
            var t = mesh.triangles;
            float z0 = -Layout.Depth * 0.5f;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = w[t[i]], b = w[t[i + 1]], c = w[t[i + 2]];
                if (Mathf.Max(a.z, Mathf.Max(b.z, c.z)) > z0 - 0.01f) continue; // behind/inside the hotel: occluded
                var r = ScreenRect(cam, new[] { a, b, c });
                Assert.IsFalse(Overlaps(r, floors, 0.5f), what + " triangle " + (i / 3) + " " + r + " covers the floors " + floors);
            }
        }

        [UnityTest]
        public IEnumerator Exterior_Exists_WithinBudget()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            Assert.IsNotNull(root.City, "city");
            Assert.IsNotNull(root.City.Renderer, "city is one merged palette mesh");
            Assert.Greater(root.City.WindowCount, 150, "skyline windows");
            Assert.AreEqual(1, root.City.Root.GetComponentsInChildren<Renderer>().Length, "city = 1 renderer");
            Assert.IsNotNull(root.Facade);
            Assert.LessOrEqual(root.Facade.Renderers.Count, 16, "facade merged per material (+ pixel details)");
            Assert.Greater(root.Facade.BulbCount, 20, "HOTEL sign bulbs");
            Assert.AreSame(root.Palette.Material, root.City.Renderer.sharedMaterial, "city shares the palette material");
            foreach (var t in root.Street.ActiveCarTransforms())
                Assert.AreSame(root.Palette.Material, t.GetComponent<MeshRenderer>().sharedMaterial, "cars share the palette material");
            Assert.LessOrEqual(root.Street.CarPool, RenderingStyle.MaxCars, "car pool");
            Assert.LessOrEqual(root.Street.PedestrianPool, RenderingStyle.MaxPedestrians, "pedestrian pool");
            Assert.Greater(root.Street.ActiveCars, 0, "prewarmed traffic");
            Assert.Greater(root.Street.ActivePedestrians, 0, "prewarmed pedestrians");
            int streetRenderers = 0;
            foreach (var r in root.Street.Root.GetComponentsInChildren<Renderer>()) if (r.enabled) streetRenderers++;
            Assert.LessOrEqual(streetRenderers, RenderingStyle.MaxCars + RenderingStyle.MaxPedestrians * 2, "street draw budget");
            if (root.Street.PedestrianPool > 0)
            {
                var kenney = Resources.Load<KenneyCatalog>(KenneyCatalog.ResourcePath);
                foreach (var t in root.Street.ActivePedestrianTransforms())
                    foreach (var s in t.GetComponentsInChildren<SkinnedMeshRenderer>())
                        Assert.AreSame(kenney.Material, s.sharedMaterial, "pedestrians share the Kenney atlas material");
            }
        }

        [UnityTest]
        public IEnumerator Street_IsDeterministicPerSeed()
        {
            yield return RenderingTestDriver.LoadMain();
            var assets = new RenderAssets();
            var holder = new GameObject("Street Determinism");
            try
            {
                var atlas = new PaletteAtlas(assets, "Test Palette");
                var grid = RenderingTestDriver.Rendering().Rig.BaseFrame.Grid;
                string Run(int seed)
                {
                    var s = new StreetLife(holder.transform, assets, atlas, RenderingStyle.CameraShear, -Layout.Depth * 0.5f, seed);
                    s.Build();
                    s.Prewarm(45f, grid);
                    for (int i = 0; i < 50; i++) s.Tick(0.1f, grid);
                    Assert.LessOrEqual(s.ActiveCars, RenderingStyle.MaxCars);
                    Assert.LessOrEqual(s.ActivePedestrians, RenderingStyle.MaxPedestrians);
                    return s.Signature();
                }
                string a = Run(7), b = Run(7), c = Run(8);
                Assert.IsNotEmpty(a);
                Assert.AreEqual(a, b, "same seed, same street");
                Assert.AreNotEqual(a, c, "another seed, another street");
            }
            finally
            {
                Object.Destroy(holder);
                assets.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Exterior_NeverCoversTheFloors()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var cam = root.Pipeline.WorldCamera;
            var floors = FloorsRect(cam);
            Assert.Greater(floors.width, 100f);
            foreach (var mf in root.Facade.Root.GetComponentsInChildren<MeshFilter>()) AssertMeshClearOfFloors(cam, floors, mf, "facade " + mf.name);
            foreach (var mf in root.City.Root.GetComponentsInChildren<MeshFilter>()) AssertMeshClearOfFloors(cam, floors, mf, "city");
            if (root.Building.HotelSignRoot != null)
                foreach (var r in root.Building.HotelSignRoot.GetComponentsInChildren<Renderer>())
                    Assert.Greater(ScreenRect(cam, Corners(r.bounds)).yMin, floors.yMax - 0.5f, "sign above the roof: " + r.name);
            float hotelLeft = Layout.ShaftX - Layout.FloorWidth * 0.5f, hotelRight = Layout.ShaftX + Layout.FloorWidth * 0.5f;
            foreach (var b in root.City.BuildingRects)
                Assert.IsTrue(b.xMax <= hotelLeft - 1f || b.xMin >= hotelRight + 1f, "neighbour building clear of the hotel: " + b);

            // street life over a few seconds of traffic
            Time.captureDeltaTime = 0.1f;
            int checkedCars = 0, checkedPeople = 0;
            for (int f = 0; f < 60; f++)
            {
                yield return null;
                foreach (var r in root.Street.ActiveRenderers())
                {
                    var sr = ScreenRect(cam, Corners(r.bounds));
                    Assert.Less(sr.yMax, floors.yMin + 0.5f, r.name + " stays below floor 1 on screen (" + sr + ")");
                    if (r is SkinnedMeshRenderer) checkedPeople++; else checkedCars++;
                }
            }
            Assert.Greater(checkedCars, 0);
            Assert.Greater(checkedPeople, 0);
        }

        [UnityTest]
        public IEnumerator LivingLights_ChangeOverTime_WithoutNewRenderers()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var lights = root.LivingLights;
            int renderers = root.City.Root.GetComponentsInChildren<Renderer>().Length;
            int lit0 = lights.LitWindows, changes0 = lights.Changes, uploads0 = root.Palette.Uploads;
            Assert.That(lit0, Is.InRange(lights.WindowCount / 6, lights.WindowCount * 2 / 3), "a mix of lit and dark windows");
            Time.captureDeltaTime = 0.25f;
            bool letterOut = false;
            for (int i = 0; i < 480 && !(letterOut && lights.Changes > changes0 + 5); i++)
            {
                yield return null;
                for (int l = 0; l < BuildingView.HotelText.Length; l++)
                    if (root.Building.GetHotelLetterLevel(l) < 1f) letterOut = true;
            }
            Assert.Greater(lights.Changes, changes0 + 5, "windows switched on/off");
            Assert.Greater(root.Palette.Uploads, uploads0, "by rewriting palette texels");
            Assert.IsTrue(letterOut, "a HOTEL letter flickered");
            Assert.AreEqual(renderers, root.City.Root.GetComponentsInChildren<Renderer>().Length, "no extra renderers");
        }

        static void HideOtherCanvases()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = false;
        }

        static string Stats(string label, RenderingRoot root, ProfilerRecorder draws, ProfilerRecorder batches, ProfilerRecorder setPass, ProfilerRecorder tris)
            => "[RenderStats] " + label + " drawCalls=" + draws.LastValue + " batches=" + batches.LastValue + " setPass=" + setPass.LastValue +
               " triangles=" + tris.LastValue + " | city renderers=" + root.City.Root.GetComponentsInChildren<Renderer>().Length +
               " facade=" + root.Facade.Renderers.Count + " cars=" + root.Street.ActiveCars + " people=" + root.Street.ActivePedestrians +
               " windows=" + root.City.WindowCount + " palette texels=" + root.Palette.Used;

        [UnityTest]
        public IEnumerator Capture_TitleAndDayStreet_WithZooms()
        {
            var report = new List<string>();
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"))
            using (var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            {
                // title screen (attract demo + zoomed-out camera), UI included as players see it
                yield return RenderingTestDriver.LoadTitle();
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 20; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                var root = RenderingTestDriver.Rendering();
                Assert.IsTrue(root.Rig.TitleMode);
                for (int i = 0; i < 5; i++) yield return null;
                report.Add(Stats("title", root, draws, batches, setPass, tris));
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_title_street")));
                var l = Layout;
                float H = l.FloorCount * l.FloorHeight;
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_title_sign_zoom",
                    new Vector3(-4f, H + 0.3f, -l.Depth * 0.5f), new Vector3(4f, H + 3.2f, -l.Depth * 0.5f), 6)));
                RenderingTestDriver.Unfreeze();

                // day 1 running, world only (HUD hidden) + zoomed crops
                yield return RenderingTestDriver.LoadMain();
                Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
                Time.captureDeltaTime = 0.1f;
                for (int i = 0; i < 30; i++) yield return null;
                yield return RenderingTestDriver.Freeze();
                root = RenderingTestDriver.Rendering();
                for (int i = 0; i < 5; i++) yield return null;
                report.Add(Stats("day (HUD)", root, draws, batches, setPass, tris));
                // same frame without the exterior (city + facade + street) = what the additions cost
                root.City.Root.gameObject.SetActive(false);
                root.Facade.Root.gameObject.SetActive(false);
                root.Street.Root.gameObject.SetActive(false);
                if (root.Building.HotelSignRoot.Find("Sign Frame") != null) root.Building.HotelSignRoot.Find("Sign Frame").gameObject.SetActive(false);
                for (int i = 0; i < 5; i++) yield return null;
                report.Add("[RenderStats] day without exterior drawCalls=" + draws.LastValue + " setPass=" + setPass.LastValue + " triangles=" + tris.LastValue);
                root.City.Root.gameObject.SetActive(true);
                root.Facade.Root.gameObject.SetActive(true);
                root.Street.Root.gameObject.SetActive(true);
                if (root.Building.HotelSignRoot.Find("Sign Frame") != null) root.Building.HotelSignRoot.Find("Sign Frame").gameObject.SetActive(true);
                yield return null;
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_day_street")));
                HideOtherCanvases();
                Assert.IsTrue(File.Exists(FrameCapture.Capture("render_day_street_world")));
                float z0 = -l.Depth * 0.5f;
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_facade_zoom",
                    new Vector3(-11f, RenderingStyle.RoadY - 1.2f, z0), new Vector3(15f, 4.5f, z0), 3)));
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_sign_zoom",
                    new Vector3(-4f, H + 0.3f, z0), new Vector3(4f, H + 3.2f, z0), 6)));
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_roof_zoom",
                    new Vector3(-9.5f, H - 3.5f, z0), new Vector3(10.5f, H + 3.2f, z0), 4)));
                Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_skyline_zoom",
                    new Vector3(-27f, -2f, z0), new Vector3(-9f, 16f, z0), 4)));
                Directory.CreateDirectory(FrameCapture.OutputDir);
                File.WriteAllText(Path.Combine(FrameCapture.OutputDir, "render_street_stats.txt"), string.Join("\n", report) + "\n");
                foreach (var line in report)
                {
                    Debug.Log(line);
                    TestContext.WriteLine(line);
                }
            }
        }
    }
}
