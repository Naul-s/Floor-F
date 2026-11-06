using FloorF.Core;
using FloorF.Gameplay;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// The car: an open-front brass box riding the shaft, doors on its +X side (the waiting line side) that slide
    /// apart along Z, a self-lit ceiling strip, and the per-floor landing lamp (amber = car here, green = doors open).
    /// Position source, best first: IActorLocator.CarPosition, IElevatorQuery.Position, last ElevatorArrived floor
    /// (events), ground floor.
    /// </summary>
    sealed class ElevatorView
    {
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly Transform doorFront, doorBack;
        readonly float doorTravel, doorHalfDepth;
        readonly float doorSeconds;
        float open01;
        int lastIndicatorFloor = -1;
        Color lastIndicatorColor;

        public Transform Root { get; }
        public float FloorPosition { get; private set; }
        public float Open01 => open01;

        public ElevatorView(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, float doorSeconds)
        {
            this.layout = layout;
            this.building = building;
            this.doorSeconds = Mathf.Max(0.05f, doorSeconds);
            float w = building.CarWidth;
            float h = layout.FloorHeight - 0.55f;
            float d = Mathf.Min(layout.Depth * 0.62f, layout.Depth - 1.2f);
            float zc = 0.1f;
            Root = new GameObject(RenderingNames.Car).transform;
            Root.SetParent(parent, false);

            var shell = assets.Lit(RenderingStyle.Car);
            var inside = assets.Lit(RenderingStyle.CarInside);
            var doorMat = assets.Lit(RenderingStyle.CarDoor);
            assets.Box(Root, "Car Floor", new Vector3(0f, 0.06f, zc), new Vector3(w, 0.12f, d), shell);
            assets.Box(Root, "Car Ceiling", new Vector3(0f, h - 0.06f, zc), new Vector3(w, 0.12f, d), shell);
            assets.Box(Root, "Car Back", new Vector3(0f, h * 0.5f, zc + d * 0.5f - 0.05f), new Vector3(w, h, 0.1f), inside);
            assets.Box(Root, "Car Side L", new Vector3(-w * 0.5f + 0.05f, h * 0.5f, zc), new Vector3(0.1f, h, d), shell);
            assets.Box(Root, "Car Lamp", new Vector3(0f, h - 0.14f, zc), new Vector3(w * 0.6f, 0.05f, 0.25f), assets.Unlit(new Color(1f, 0.95f, 0.8f)), false);
            assets.Box(Root, "Handrail", new Vector3(0f, 0.95f, zc + d * 0.5f - 0.14f), new Vector3(w * 0.8f, 0.06f, 0.06f), shell, false);
            // front lip so the car reads as a box from the camera
            assets.Box(Root, "Car Lip Top", new Vector3(0f, h - 0.12f, zc - d * 0.5f + 0.04f), new Vector3(w, 0.24f, 0.08f), shell);
            assets.Box(Root, "Car Lip Bottom", new Vector3(0f, 0.1f, zc - d * 0.5f + 0.04f), new Vector3(w, 0.2f, 0.08f), shell);

            // own warm light so the car is the brightest, easiest-to-follow thing in the shaft
            var lightGo = new GameObject("Car Light");
            lightGo.transform.SetParent(Root, false);
            lightGo.transform.localPosition = new Vector3(0f, h - 0.4f, zc - d * 0.3f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3.2f;
            light.color = RenderingStyle.CarLight;
            light.intensity = RenderingStyle.CarLightIntensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;

            doorHalfDepth = d * 0.5f;
            doorTravel = d * 0.5f - 0.08f;
            doorFront = assets.Box(Root, "Door Front", new Vector3(w * 0.5f - 0.05f, h * 0.5f, zc - d * 0.25f), new Vector3(0.08f, h - 0.24f, d * 0.5f), doorMat).transform;
            doorBack = assets.Box(Root, "Door Back", new Vector3(w * 0.5f - 0.05f, h * 0.5f, zc + d * 0.25f), new Vector3(0.08f, h - 0.24f, d * 0.5f), doorMat).transform;
            doorZc = zc;
            SetDoors(0f);
            Place(0f);
        }

        readonly float doorZc;

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
        }

        void Place(float floorPosition)
        {
            FloorPosition = floorPosition;
            Root.localPosition = layout.GetCarPosition(floorPosition).ToVector3();
        }

        public void Tick(float dt, IActorLocator locator, IElevatorQuery elevator, int eventFloor, bool eventDoorsOpen)
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
