using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorF.Rendering
{
    /// <summary>
    /// Traffic and pedestrians in front of the hotel (pooled, seeded, juice clock):
    ///   cars  - up to <see cref="RenderingStyle.MaxCars"/> palette-mesh boxes (one draw each, headlights, tail lights and a
    ///           headlight pool on the asphalt), far lane drives right, near lane drives left, cars queue behind slower ones;
    ///   people - up to <see cref="RenderingStyle.MaxPedestrians"/> Kenney Mini Characters from the guest catalog (meshes
    ///           re-pointed at the untinted colormap tile, shared atlas material) walking both ways on the sidewalk.
    /// Positions snap to the active frame's pixel grid so moving boxes keep crisp edges. Everything stays below floor 1
    /// on screen (street level y &lt; 0) and never touches the hotel's cut-away.
    /// </summary>
    sealed class StreetLife
    {
        sealed class Car
        {
            public Transform T;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Active;
            public int Lane, Dir, Variant;
            public float X, Speed, Cruise, Length;
        }

        sealed class Walker
        {
            public Transform T;
            public Transform Model;
            public GameObject Rig;
            public readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>();
            public bool Active;
            public int Dir;
            public float X, Z, Speed, Anim;
        }

        struct CarKind
        {
            public Color Body;
            public int Shape; // 0 sedan, 1 taxi, 2 van
        }

        static readonly CarKind[] Kinds =
        {
            new CarKind { Body = new Color(0.55f, 0.13f, 0.11f), Shape = 0 },
            new CarKind { Body = new Color(0.16f, 0.27f, 0.52f), Shape = 0 },
            new CarKind { Body = new Color(0.80f, 0.62f, 0.16f), Shape = 1 },
            new CarKind { Body = new Color(0.62f, 0.63f, 0.66f), Shape = 0 },
            new CarKind { Body = new Color(0.18f, 0.36f, 0.27f), Shape = 0 },
            new CarKind { Body = new Color(0.11f, 0.11f, 0.13f), Shape = 0 },
            new CarKind { Body = new Color(0.52f, 0.52f, 0.55f), Shape = 2 },
            new CarKind { Body = new Color(0.42f, 0.29f, 0.19f), Shape = 2 },
        };

        public const float MinCarGap = 2.2f;

        readonly RenderAssets assets;
        readonly PaletteAtlas atlas;
        readonly Vector2 shear;
        readonly float z0;
        readonly System.Random rng;
        readonly List<Car> cars = new List<Car>();
        readonly List<Walker> walkers = new List<Walker>();
        readonly Mesh[,] carMeshes = new Mesh[Kinds.Length, 2];
        Mesh[,] snowyMeshes;
        bool snowyApplied;

        /// <summary>Winter (heavy snow): cars wear snow on their roofs and bonnets (meshes built on first use).</summary>
        public bool Snowy { get; set; }

        Mesh CarMesh(int variant, int dir)
        {
            if (Snowy && snowyMeshes == null)
            {
                snowyMeshes = new Mesh[Kinds.Length, 2];
                for (int k = 0; k < Kinds.Length; k++)
                    for (int d = 0; d < 2; d++)
                        snowyMeshes[k, d] = assets.Own(BuildCarMesh(Kinds[k], d == 0 ? 1 : -1, true));
            }
            var set = Snowy ? snowyMeshes : carMeshes;
            return set[variant, dir > 0 ? 0 : 1];
        }
        readonly float[] laneTimer = new float[2];
        readonly float[] walkTimer = new float[2];
        AnimationClip walk;

        public Transform Root { get; }
        public int Seed { get; }
        public int CarPool => cars.Count;
        public int PedestrianPool => walkers.Count;
        public int ActiveCars { get; private set; }
        public int ActivePedestrians { get; private set; }
        public int CarsSpawned { get; private set; }
        public int PedestriansSpawned { get; private set; }

        public StreetLife(Transform parent, RenderAssets assets, PaletteAtlas atlas, Vector2 shear, float shearDepth, int seed)
        {
            this.assets = assets;
            this.atlas = atlas;
            this.shear = shear;
            z0 = shearDepth;
            Seed = seed;
            rng = new System.Random(seed);
            Root = new GameObject("Street").transform;
            Root.SetParent(parent, false);
        }

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public static float LaneZ(int lane) => lane == 0 ? RenderingStyle.FarLaneZ : RenderingStyle.NearLaneZ;
        public static int LaneDir(int lane) => lane == 0 ? 1 : -1;

        public void Build()
        {
            for (int k = 0; k < Kinds.Length; k++)
                for (int d = 0; d < 2; d++)
                    carMeshes[k, d] = assets.Own(BuildCarMesh(Kinds[k], d == 0 ? 1 : -1));
            var carRoot = new GameObject("Cars").transform;
            carRoot.SetParent(Root, false);
            for (int i = 0; i < RenderingStyle.MaxCars; i++)
            {
                var r = PaletteRenderer.Create(carRoot, "Car " + i, carMeshes[0, 0], atlas.Material);
                r.gameObject.SetActive(false);
                cars.Add(new Car { T = r.transform, Filter = r.GetComponent<MeshFilter>(), Renderer = r });
            }
            BuildWalkers();
            for (int i = 0; i < 2; i++)
            {
                laneTimer[i] = Range(0f, 2f);
                walkTimer[i] = Range(0f, 2f);
            }
        }

        // ------------------------------------------------------------------ cars

        Mesh BuildCarMesh(CarKind kind, int dir, bool snow = false)
        {
            var b = new PaletteMeshBuilder(atlas);
            bool van = kind.Shape == 2;
            float half = van ? 1.6f : 1.3f;
            // x ranges authored for a car facing +X; mirrored for -X (min/max swap keeps the faces' winding)
            Vector3 P(float x, float y, float z) => new Vector3(x * dir, y, z);
            void Box(float x0, float y0, float z0b, float x1, float y1, float z1, Color c)
            {
                var a = P(x0, y0, z0b);
                var e = P(x1, y1, z1);
                b.Box(new Vector3(Mathf.Min(a.x, e.x), y0, z0b), new Vector3(Mathf.Max(a.x, e.x), y1, z1), c);
            }
            void Lamp(float x0, float y0, float z0b, float x1, float y1, float z1, Color c)
            {
                var a = P(x0, y0, z0b);
                var e = P(x1, y1, z1);
                int t = atlas.Static(c);
                b.Box(new Vector3(Mathf.Min(a.x, e.x), y0, z0b), new Vector3(Mathf.Max(a.x, e.x), y1, z1), t, t, t);
            }
            void Front(float x0, float y0, float x1, float y1, float z, Color c)
            {
                float a = x0 * dir, e = x1 * dir;
                b.Front(Mathf.Min(a, e), y0, Mathf.Max(a, e), y1, z, atlas.Static(c));
            }

            var tyre = new Color(0.05f, 0.05f, 0.06f);
            var hub = new Color(0.36f, 0.37f, 0.40f);
            var glass = new Color(0.20f, 0.26f, 0.36f);
            var body = kind.Body;
            // headlight pool on the road ahead (drawn first, flat just above the asphalt)
            {
                float a = (half + 0.1f) * dir, e = (half + 2.8f) * dir;
                b.Flat(Mathf.Min(a, e), -0.42f, Mathf.Max(a, e), 0.42f, 0.006f, atlas.Static(new Color(0.17f, 0.16f, 0.13f)));
            }
            foreach (float wx in new[] { -half + 0.55f, half - 0.55f })
            {
                Box(wx - 0.28f, 0f, -0.52f, wx + 0.28f, 0.42f, 0.52f, tyre);
                Front(wx - 0.1f, 0.12f, wx + 0.1f, 0.3f, -0.53f, hub);
            }
            float roofTop = van ? 1.2f : 0.62f;
            Box(-half, 0.2f, -0.5f, half, roofTop, 0.5f, body);
            if (van)
            {
                Front(half - 0.85f, 0.72f, half - 0.12f, 1.02f, -0.51f, glass);
                Front(-half + 0.1f, 0.5f, half - 1.0f, 0.6f, -0.51f, Color.Lerp(body, Color.white, 0.35f));
            }
            else
            {
                float c0 = -half + 0.5f, c1 = half - 0.75f;
                Box(c0, 0.62f, -0.42f, c1, 1.0f, 0.42f, body * 0.92f);
                Front(c0 + 0.12f, 0.68f, c1 - 0.1f, 0.94f, -0.43f, glass);
                Front((c0 + c1) * 0.5f - 0.05f, 0.68f, (c0 + c1) * 0.5f + 0.05f, 0.94f, -0.44f, body * 0.8f);
                if (kind.Shape == 1) Lamp(-0.25f, 1.0f, -0.15f, 0.25f, 1.14f, 0.15f, new Color(1f, 0.9f, 0.45f));
            }
            if (snow)
            {
                var white = WinterView.CarSnow;
                if (van) Box(-half + 0.05f, 1.2f, -0.46f, half - 0.05f, 1.36f, 0.46f, white);
                else
                {
                    float c0 = -half + 0.5f, c1 = half - 0.75f;
                    Box(c0 + 0.04f, 1.0f, -0.38f, c1 - 0.04f, 1.16f, 0.38f, white);
                    Box(c1 + 0.05f, 0.62f, -0.44f, half - 0.15f, 0.7f, 0.44f, white);
                    Box(-half + 0.05f, 0.62f, -0.44f, c0 - 0.05f, 0.7f, 0.44f, white);
                }
            }
            Lamp(half - 0.2f, 0.36f, -0.52f, half + 0.03f, 0.54f, 0.46f, new Color(1f, 0.96f, 0.78f));
            Lamp(-half - 0.03f, 0.38f, -0.52f, -half + 0.16f, 0.54f, 0.46f, new Color(0.95f, 0.14f, 0.12f));
            return b.Build("Car " + kind.Shape + (dir > 0 ? " R" : " L"));
        }

        float SnapX(float x, float z, PixelGrid grid)
        {
            float s = x + shear.x * (z - z0);
            return grid.SnapScreen(s) - shear.x * (z - z0);
        }

        float SnapY(float y, float z, PixelGrid grid)
        {
            float s = y + shear.y * (z - z0);
            return grid.SnapScreen(s) - shear.y * (z - z0);
        }

        Car Leader(Car c)
        {
            Car best = null;
            float bestGap = float.MaxValue;
            foreach (var o in cars)
            {
                if (!o.Active || o == c || o.Lane != c.Lane) continue;
                float gap = (o.X - c.X) * c.Dir;
                if (gap > 0f && gap < bestGap) { bestGap = gap; best = o; }
            }
            return best;
        }

        bool LaneEntryClear(int lane)
        {
            int dir = LaneDir(lane);
            float start = -dir * RenderingStyle.StreetHalfLength;
            foreach (var o in cars)
                if (o.Active && o.Lane == lane && (o.X - start) * dir < o.Length + MinCarGap + 3f) return false;
            return true;
        }

        void SpawnCar(int lane)
        {
            Car c = null;
            int free = 0;
            foreach (var o in cars) if (!o.Active) free++;
            if (free == 0) return;
            int pick = rng.Next(free);
            foreach (var o in cars)
                if (!o.Active && pick-- == 0) { c = o; break; }
            c.Lane = lane;
            c.Dir = LaneDir(lane);
            c.Variant = rng.Next(Kinds.Length);
            c.Length = Kinds[c.Variant].Shape == 2 ? 3.2f : 2.6f;
            c.Cruise = c.Speed = lane == 0 ? Range(4.5f, 6.5f) : Range(5f, 7.5f);
            c.X = -c.Dir * RenderingStyle.StreetHalfLength;
            c.Filter.sharedMesh = CarMesh(c.Variant, c.Dir);
            c.Active = true;
            c.T.gameObject.SetActive(true);
            CarsSpawned++;
        }

        // ------------------------------------------------------------------ pedestrians

        void BuildWalkers()
        {
            var kenney = assets.Kenney;
            if (kenney == null || kenney.Guests.Length == 0 || kenney.Atlas == null) return;
            walk = kenney.Walk;
            var remapped = new Dictionary<Mesh, Mesh>();
            const int tilePx = 64;
            int perRow = Mathf.Max(1, kenney.Atlas.width / tilePx);
            var models = new List<KenneyCatalog.GuestModel>();
            foreach (var g in kenney.Guests) if (g != null && g.Prefab != null) models.Add(g);
            if (models.Count == 0) return;
            var walkRoot = new GameObject("Pedestrians").transform;
            walkRoot.SetParent(Root, false);
            for (int i = 0; i < RenderingStyle.MaxPedestrians; i++)
            {
                var entry = models[i % models.Count];
                var w = new Walker();
                w.T = new GameObject("Pedestrian " + i).transform;
                w.T.SetParent(walkRoot, false);
                w.Model = new GameObject("Model").transform;
                w.Model.SetParent(w.T, false);
                float height = 1.22f + 0.2f * LightSchedule.Hash(Seed, i, 21, 0);
                float k = height / Mathf.Max(0.1f, entry.Bounds.max.y);
                w.Model.localScale = new Vector3(k * GuestFigure.ModelWidthScale, k, k);
                w.Rig = Object.Instantiate(entry.Prefab, w.Model, false);
                w.Rig.name = "Rig " + entry.Character;
                var offset = new Vector2((entry.Tile % perRow) * tilePx, (entry.Tile / perRow) * tilePx) / kenney.Atlas.width;
                foreach (var smr in w.Rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.sharedMesh;
                    if (src != null && !remapped.TryGetValue(src, out var copy))
                    {
                        // same mesh, UVs moved from the guest's tinted tile to tile 0 (plain Kenney colours)
                        copy = assets.Own(Object.Instantiate(src));
                        copy.name = src.name + " (street)";
                        var uv = copy.uv;
                        for (int u = 0; u < uv.Length; u++) uv[u] -= offset;
                        copy.uv = uv;
                        remapped[src] = copy;
                    }
                    if (src != null) smr.sharedMesh = remapped[src];
                    smr.sharedMaterial = kenney.Material;
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                    smr.receiveShadows = false;
                    smr.lightProbeUsage = LightProbeUsage.Off;
                    smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    w.Skins.Add(smr);
                }
                w.T.gameObject.SetActive(false);
                walkers.Add(w);
            }
        }

        void SpawnWalker(int dir)
        {
            int free = 0;
            foreach (var w in walkers) if (!w.Active) free++;
            if (free == 0) return;
            int pick = rng.Next(free);
            foreach (var w in walkers)
            {
                if (w.Active || pick-- != 0) continue;
                w.Dir = dir;
                w.Z = RenderingStyle.WalkLanes[rng.Next(RenderingStyle.WalkLanes.Length)];
                w.Speed = Range(0.85f, 1.45f);
                w.X = -dir * RenderingStyle.StreetHalfLength + Range(-1f, 1f);
                w.Anim = Range(0f, 2f);
                w.Model.localRotation = Quaternion.Euler(0f, GuestFigure.FacingCamera - 70f * dir, 0f);
                w.Active = true;
                w.T.gameObject.SetActive(true);
                PedestriansSpawned++;
                return;
            }
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Runs <paramref name="seconds"/> of street time without rendering (busy street on the first frame).</summary>
        public void Prewarm(float seconds, PixelGrid grid)
        {
            for (float t = 0f; t < seconds; t += 0.1f) Step(0.1f);
            Place(grid, true);
        }

        /// <summary>Advance on the juice clock (dt = 0 holds everything) and place on <paramref name="grid"/>.</summary>
        public void Tick(float dt, PixelGrid grid)
        {
            if (Snowy != snowyApplied)
            {
                snowyApplied = Snowy;
                foreach (var c in cars) if (c.Active) c.Filter.sharedMesh = CarMesh(c.Variant, c.Dir);
            }
            if (dt > 0f) Step(Mathf.Min(dt, 0.1f));
            Place(grid, dt > 0f);
        }

        void Step(float dt)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                laneTimer[lane] -= dt;
                if (laneTimer[lane] <= 0f && LaneEntryClear(lane))
                {
                    SpawnCar(lane);
                    laneTimer[lane] = Range(2.5f, 8.5f);
                }
            }
            for (int d = 0; d < 2; d++)
            {
                walkTimer[d] -= dt;
                if (walkTimer[d] <= 0f)
                {
                    SpawnWalker(d == 0 ? 1 : -1);
                    walkTimer[d] = Range(3f, 9f);
                }
            }
            int activeCars = 0;
            foreach (var c in cars)
            {
                if (!c.Active) continue;
                var lead = Leader(c);
                float target = c.Cruise;
                if (lead != null)
                {
                    float gap = (lead.X - c.X) * c.Dir - (lead.Length + c.Length) * 0.5f;
                    if (gap < MinCarGap + 2f) target = Mathf.Min(target, lead.Speed * Mathf.Clamp01((gap - MinCarGap) / 2f));
                }
                c.Speed = Mathf.MoveTowards(c.Speed, target, 6f * dt);
                c.X += c.Dir * c.Speed * dt;
                if ((c.X - c.Dir * RenderingStyle.StreetHalfLength) * c.Dir > 0f)
                {
                    c.Active = false;
                    c.T.gameObject.SetActive(false);
                    continue;
                }
                activeCars++;
            }
            ActiveCars = activeCars;
            int activeWalkers = 0;
            foreach (var w in walkers)
            {
                if (!w.Active) continue;
                w.X += w.Dir * w.Speed * dt;
                w.Anim += dt * w.Speed * 0.9f;
                if ((w.X - w.Dir * (RenderingStyle.StreetHalfLength + 1f)) * w.Dir > 0f)
                {
                    w.Active = false;
                    w.T.gameObject.SetActive(false);
                    continue;
                }
                activeWalkers++;
            }
            ActivePedestrians = activeWalkers;
        }

        void Place(PixelGrid grid, bool animate)
        {
            foreach (var c in cars)
            {
                if (!c.Active) continue;
                float z = LaneZ(c.Lane);
                c.T.localPosition = new Vector3(SnapX(c.X, z, grid), SnapY(RenderingStyle.RoadY, z, grid), z);
            }
            foreach (var w in walkers)
            {
                if (!w.Active) continue;
                w.T.localPosition = new Vector3(SnapX(w.X, w.Z, grid), SnapY(RenderingStyle.GroundY, w.Z, grid), w.Z);
                if (animate && walk != null) walk.SampleAnimation(w.Rig, Mathf.Repeat(w.Anim, Mathf.Max(0.01f, walk.length)));
            }
        }

        // ------------------------------------------------------------------ queries (tests)

        public IEnumerable<Renderer> ActiveRenderers()
        {
            foreach (var c in cars) if (c.Active) yield return c.Renderer;
            foreach (var w in walkers)
                if (w.Active)
                    foreach (var s in w.Skins) yield return s;
        }

        public IEnumerable<Transform> ActiveCarTransforms()
        {
            foreach (var c in cars) if (c.Active) yield return c.T;
        }

        public IEnumerable<Transform> ActivePedestrianTransforms()
        {
            foreach (var w in walkers) if (w.Active) yield return w.T;
        }

        /// <summary>Compact state string (determinism tests): lane/variant/x of cars, x of walkers.</summary>
        public string Signature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in cars) if (c.Active) sb.Append('c').Append(c.Lane).Append(c.Variant).Append(':').Append(c.X.ToString("F2")).Append(' ');
            foreach (var w in walkers) if (w.Active) sb.Append('w').Append(w.Dir > 0 ? '+' : '-').Append(':').Append(w.X.ToString("F2")).Append(' ');
            return sb.ToString();
        }
    }
}
