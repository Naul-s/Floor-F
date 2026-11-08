using FloorF.Core;
using FloorF.Gameplay;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// The car: an open-front brass box riding the shaft. Root follows the gameplay position exactly; everything visual
    /// hangs under "Body" so arrival squash &amp; stretch / door-thud wobble never moves the logical car.
    /// Doors: framed front panels (big windows so riders stay countable) that slide apart and telescope into the car
    /// sides, plus the side doors on +X (the waiting-line side). Per-floor landing lamp: amber = car here, green = open.
    /// Position source, best first: IActorLocator.CarPosition, IElevatorQuery.Position, last ElevatorArrived floor
    /// (events), ground floor.
    /// </summary>
    sealed class ElevatorView
    {
        public const float BumpSeconds = 0.6f;

        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly Transform body;
        readonly Transform doorFront, doorBack, panelL, panelR;
        readonly float doorTravel, doorHalfDepth, doorZc, carWidth;
        readonly float doorSeconds;
        readonly Light carLight;
        readonly Material lampMat;
        readonly Transform arrow;
        float lastFloorPosition = float.NaN;
        /// <summary>Warm amber like the landing lamps.</summary>
        public static readonly Color ArrowColor = new Color(1.00f, 0.70f, 0.25f);
        float open01;
        int lastIndicatorFloor = -1;
        Color lastIndicatorColor;
        float bumpAge = float.MaxValue, bumpStrength;
        float lightLevel = 1f;

        public Transform Root { get; }
        public Transform Body => body;
        public float FloorPosition { get; private set; }
        public float Open01 => open01;
        public bool IsBumping => bumpAge < BumpSeconds;
        public float LightLevel => lightLevel;
        /// <summary>+1 = arrow up, -1 = arrow down, 0 = hidden (car idle).</summary>
        public int ArrowDirection { get; private set; }

        /// <summary>Triangle pointing up (+Y), facing the camera (-Z), centred on the origin.</summary>
        static Mesh ArrowMesh(float width, float height)
        {
            var m = new Mesh { name = "Direction Arrow" };
            m.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new Vector3(-width * 0.5f, -height * 0.5f, 0f), new Vector3(0f, height * 0.5f, 0f), new Vector3(width * 0.5f, -height * 0.5f, 0f),
            });
            m.SetNormals(new System.Collections.Generic.List<Vector3> { Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new System.Collections.Generic.List<Vector2> { Vector2.zero, new Vector2(0.5f, 1f), Vector2.right });
            // double-sided so the down arrow (scale.y = -1 flips the winding) still shows
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Pure: arrow direction from the gameplay motion, else from the car's movement this frame.</summary>
        public static int Direction(ElevatorMotion? motion, float floorDelta)
        {
            if (motion.HasValue) return motion.Value == ElevatorMotion.MovingUp ? 1 : motion.Value == ElevatorMotion.MovingDown ? -1 : 0;
            return floorDelta > 1e-3f ? 1 : floorDelta < -1e-3f ? -1 : 0;
        }

        public ElevatorView(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, float doorSeconds)
        {
            this.layout = layout;
            this.building = building;
            this.doorSeconds = Mathf.Max(0.05f, doorSeconds);
            float w = building.CarWidth;
            carWidth = w;
            float h = layout.FloorHeight - 0.55f;
            float d = Mathf.Min(layout.Depth * 0.62f, layout.Depth - 1.2f);
            float zc = 0.1f;
            Root = new GameObject(RenderingNames.Car).transform;
            Root.SetParent(parent, false);
            body = new GameObject("Body").transform;
            body.SetParent(Root, false);
            var B = body;

            var shell = assets.Lit(RenderingStyle.Car);
            var inside = assets.Lit(RenderingStyle.CarInside);
            var doorMat = assets.Lit(RenderingStyle.CarDoor);
            lampMat = assets.NewUnlit(new Color(1f, 0.95f, 0.8f));
            assets.Box(B, "Car Floor", new Vector3(0f, 0.06f, zc), new Vector3(w, 0.12f, d), shell);
            assets.Box(B, "Car Ceiling", new Vector3(0f, h - 0.06f, zc), new Vector3(w, 0.12f, d), shell);
            assets.Box(B, "Car Back", new Vector3(0f, h * 0.5f, zc + d * 0.5f - 0.05f), new Vector3(w, h, 0.1f), inside);
            assets.Box(B, "Car Side L", new Vector3(-w * 0.5f + 0.05f, h * 0.5f, zc), new Vector3(0.1f, h, d), shell);
            assets.Box(B, "Car Lamp", new Vector3(0f, h - 0.14f, zc), new Vector3(w * 0.6f, 0.05f, 0.25f), lampMat, false);
            assets.Box(B, "Handrail", new Vector3(0f, 0.95f, zc + d * 0.5f - 0.14f), new Vector3(w * 0.8f, 0.06f, 0.06f), shell, false);
            // front lip so the car reads as a box from the camera
            assets.Box(B, "Car Lip Top", new Vector3(0f, h - 0.12f, zc - d * 0.5f + 0.04f), new Vector3(w, 0.24f, 0.08f), shell);
            assets.Box(B, "Car Lip Bottom", new Vector3(0f, 0.1f, zc - d * 0.5f + 0.04f), new Vector3(w, 0.2f, 0.08f), shell);
            // direction lamp on the top lip: a small glowing up/down triangle while the car travels
            var arrowGo = new GameObject("Direction Arrow");
            arrowGo.transform.SetParent(B, false);
            arrowGo.transform.localPosition = new Vector3(0f, h - 0.12f, zc - d * 0.5f - 0.02f);
            arrowGo.AddComponent<MeshFilter>().sharedMesh = assets.Own(ArrowMesh(0.56f, 0.3f));
            var arrowRenderer = arrowGo.AddComponent<MeshRenderer>();
            arrowRenderer.sharedMaterial = assets.Unlit(ArrowColor);
            arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrowRenderer.receiveShadows = false;
            arrow = arrowGo.transform;
            arrowGo.SetActive(false);

            // own warm light so the car is the brightest, easiest-to-follow thing in the shaft
            var lightGo = new GameObject("Car Light");
            lightGo.transform.SetParent(B, false);
            lightGo.transform.localPosition = new Vector3(0f, h - 0.4f, zc - d * 0.3f);
            carLight = lightGo.AddComponent<Light>();
            carLight.type = LightType.Point;
            carLight.range = 3.2f;
            carLight.color = RenderingStyle.CarLight;
            carLight.intensity = RenderingStyle.CarLightIntensity;
            carLight.shadows = LightShadows.None;
            carLight.renderMode = LightRenderMode.ForcePixel;

            doorHalfDepth = d * 0.5f;
            doorTravel = d * 0.5f - 0.08f;
            doorFront = assets.Box(B, "Door Front", new Vector3(w * 0.5f - 0.05f, h * 0.5f, zc - d * 0.25f), new Vector3(0.08f, h - 0.24f, d * 0.5f), doorMat).transform;
            doorBack = assets.Box(B, "Door Back", new Vector3(w * 0.5f - 0.05f, h * 0.5f, zc + d * 0.25f), new Vector3(0.08f, h - 0.24f, d * 0.5f), doorMat).transform;
            doorZc = zc;

            // framed front doors (camera side)
            float y0 = 0.2f, y1 = h - 0.24f;
            float zFront = zc - d * 0.5f - 0.05f;
            panelL = BuildPanel(assets, B, "Front Door L", -1, w * 0.5f, y0, y1, zFront, doorMat);
            panelR = BuildPanel(assets, B, "Front Door R", +1, w * 0.5f, y0, y1, zFront, doorMat);
            SetDoors(0f);
            Place(0f);
        }

        /// <summary>One panel (width <paramref name="pw"/>) = kick plate, top rail, outer + inner stile; the window is open.</summary>
        static Transform BuildPanel(RenderAssets assets, Transform parent, string name, int side, float pw, float y0, float y1, float z, Material mat)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            float ph = y1 - y0;
            float outer = side * 0.5f * pw, inner = -side * 0.5f * pw; // panel-local x of outer / inner edges
            var edge = assets.Lit(new Color(0.92f, 0.92f, 0.96f));
            var dark = assets.Lit(new Color(0.34f, 0.34f, 0.40f));
            assets.Box(t, "Kick", new Vector3(0f, y0 + 0.17f, 0f), new Vector3(pw, 0.34f, 0.06f), mat);
            assets.Box(t, "Top", new Vector3(0f, y1 - 0.08f, 0f), new Vector3(pw, 0.16f, 0.06f), mat);
            assets.Box(t, "Outer", new Vector3(outer - side * 0.05f, y0 + ph * 0.5f, 0f), new Vector3(0.1f, ph, 0.06f), mat);
            assets.Box(t, "Inner", new Vector3(inner + side * 0.06f, y0 + ph * 0.5f, 0f), new Vector3(0.12f, ph, 0.06f), edge);
            assets.Box(t, "Seam", new Vector3(inner + side * 0.015f, y0 + ph * 0.5f, -0.01f), new Vector3(0.03f, ph, 0.06f), dark, false);
            t.localPosition = new Vector3(side * pw * 0.5f, 0f, z);
            return t;
        }

        void SetDoors(float t)
        {
            var pf = doorFront.localPosition;
            var pb = doorBack.localPosition;
            pf.z = doorZc - doorHalfDepth * 0.5f - doorTravel * t;
            pb.z = doorZc + doorHalfDepth * 0.5f + doorTravel * t;
            doorFront.localPosition = pf;
            doorBack.localPosition = pb;
            // squash the panels so they tuck into the car walls instead of poking out of the shaft
            float s = Mathf.Lerp(1f, 0.15f, t);
            var sf = doorFront.localScale; sf.z = doorHalfDepth * s; doorFront.localScale = sf;
            var sb = doorBack.localScale; sb.z = doorHalfDepth * s; doorBack.localScale = sb;

            // front panels: slide out to the car sides and telescope to 12%
            float e = t * t * (3f - 2f * t);
            float pw = carWidth * 0.5f;
            float sx = Mathf.Lerp(1f, 0.12f, e);
            float centre = Mathf.Lerp(pw * 0.5f, pw - pw * 0.06f, e);
            SetPanel(panelL, -centre, sx);
            SetPanel(panelR, centre, sx);
        }

        static void SetPanel(Transform p, float x, float sx)
        {
            var lp = p.localPosition; lp.x = x; p.localPosition = lp;
            p.localScale = new Vector3(sx, 1f, 1f);
        }

        void Place(float floorPosition)
        {
            FloorPosition = floorPosition;
            Root.localPosition = layout.GetCarPosition(floorPosition).ToVector3();
        }

        /// <summary>Arrival squash &amp; stretch (strength 1) / door-thud wobble (smaller).</summary>
        public void Bump(float strength)
        {
            bumpAge = 0f;
            bumpStrength = Mathf.Max(strength, IsBumping ? bumpStrength * 0.5f : 0f);
        }

        /// <summary>Pure: (scaleX, scaleY, offsetX) of the body <paramref name="age"/> seconds into a bump.</summary>
        public static Vector3 BumpPose(float age, float strength)
        {
            if (age < 0f || age >= BumpSeconds) return new Vector3(1f, 1f, 0f);
            float s = Mathf.Exp(-7f * age) * Mathf.Cos(age * 26f) * strength;
            float jitter = Mathf.Exp(-10f * age) * Mathf.Sin(age * 70f) * strength;
            return new Vector3(1f + 0.07f * s, 1f - 0.11f * s, 0.06f * jitter);
        }

        public void SetLightLevel(float level)
        {
            lightLevel = Mathf.Max(0f, level);
            carLight.intensity = RenderingStyle.CarLightIntensity * lightLevel;
            var c = new Color(1f, 0.95f, 0.8f) * Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(lightLevel));
            c.a = 1f;
            RenderAssets.SetColor(lampMat, c);
        }

        /// <summary><paramref name="dt"/> = real frame time (doors follow gameplay), <paramref name="juiceDt"/> = juice clock.</summary>
        public void Tick(float dt, float juiceDt, IActorLocator locator, IElevatorQuery elevator, int eventFloor, bool eventDoorsOpen)
        {
            float pos;
            if (elevator != null) pos = elevator.Position;
            else pos = Mathf.Max(0, eventFloor);
            if (locator != null)
            {
                var p = locator.CarPosition.ToVector3();
                Root.localPosition = p;
                FloorPosition = layout.FloorHeight > 0f ? p.y / layout.FloorHeight : pos;
            }
            else Place(pos);

            bool wantOpen;
            if (elevator != null)
            {
                var doors = elevator.Doors;
                wantOpen = doors == DoorState.Open || doors == DoorState.Opening;
            }
            else wantOpen = eventDoorsOpen;
            open01 = Mathf.MoveTowards(open01, wantOpen ? 1f : 0f, dt / doorSeconds);
            SetDoors(open01);

            if (bumpAge < BumpSeconds) bumpAge += juiceDt;
            var b = BumpPose(bumpAge, bumpStrength);
            body.localScale = new Vector3(b.x, b.y, 1f);
            body.localPosition = new Vector3(b.z, 0f, 0f);

            // direction arrow (frozen with the juice clock: a paused car keeps its arrow)
            if (juiceDt > 0f || elevator != null)
            {
                float delta = float.IsNaN(lastFloorPosition) ? 0f : FloorPosition - lastFloorPosition;
                int dir = Direction(elevator != null ? elevator.Motion : (ElevatorMotion?)null, delta);
                if (dir != ArrowDirection)
                {
                    ArrowDirection = dir;
                    arrow.gameObject.SetActive(dir != 0);
                    arrow.localScale = new Vector3(1f, dir < 0 ? -1f : 1f, 1f);
                }
            }
            lastFloorPosition = FloorPosition;

            // landing lamps
            int here = Mathf.RoundToInt(FloorPosition);
            bool atFloor = Mathf.Abs(FloorPosition - here) < 0.05f;
            var color = !atFloor ? RenderingStyle.IndicatorIdle : open01 > 0.5f ? RenderingStyle.IndicatorOpen : RenderingStyle.IndicatorHere;
            if (here != lastIndicatorFloor || color != lastIndicatorColor)
            {
                if (lastIndicatorFloor >= 0 && lastIndicatorFloor != here) building.SetIndicator(new FloorIndex(lastIndicatorFloor), RenderingStyle.IndicatorIdle);
                if (here >= 0 && here < building.FloorCount) building.SetIndicator(new FloorIndex(here), color);
                lastIndicatorFloor = here;
                lastIndicatorColor = color;
            }
        }
    }
}
