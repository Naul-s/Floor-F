using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorF.Rendering
{
    /// <summary>
    /// Greybox cut-away of the hotel built from <see cref="IWorldLayout"/> (+Y up, floor n slab top at n*floorHeight,
    /// shaft at shaftX, waiting line to +X, camera side -Z, back wall at +depth/2). One floor sign per floor shows its
    /// CURRENT label (sticker labels get a yellow sticker plate).
    /// </summary>
    sealed class BuildingView
    {
        const float SlabThickness = 0.3f;
        const float WallThickness = 0.3f;

        sealed class Floor
        {
            public GameObject Sign;
            public Material SignMaterial;
            public Texture2D SignTexture;
            public string SignText;
            public bool SignSticker;
            public Material Indicator;
        }

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly FloorLighting lighting;
        readonly float signTexel;
        readonly List<Floor> floors = new List<Floor>();

        public Transform Root { get; }
        public float ShaftHalfWidth { get; }
        public float CarWidth { get; }

        public BuildingView(Transform parent, RenderAssets assets, IWorldLayout layout, FloorLighting lighting, float pixelsPerUnit)
        {
            this.assets = assets;
            this.layout = layout;
            this.lighting = lighting;
            signTexel = RenderingStyle.SignPixelsPerTexel / Mathf.Max(1f, pixelsPerUnit);
            CarWidth = layout is HotelLayout hl ? hl.CarWidth : 2.4f;
            ShaftHalfWidth = CarWidth * 0.5f + 0.25f;
            var go = new GameObject("Building");
            go.transform.SetParent(parent, false);
            Root = go.transform;
        }

        /// <summary>Bounds the camera must keep in view (building + roof sign + a strip of street).</summary>
        public static Bounds FramingBounds(IWorldLayout layout)
        {
            float w = layout.FloorWidth, d = layout.Depth;
            float top = layout.FloorCount * layout.FloorHeight + 2.4f;
            var min = new Vector3(layout.ShaftX - w * 0.5f - WallThickness - 0.3f, -0.8f, -d * 0.5f - 0.3f);
            var max = new Vector3(layout.ShaftX + w * 0.5f + WallThickness + 0.3f, top, d * 0.5f + WallThickness);
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        public void Build(System.Func<FloorIndex, DestinationId> destinationOf, System.Func<FloorIndex, FloorLabel> labelOf, System.Func<FloorIndex, bool> relabelledOf)
        {
            float w = layout.FloorWidth, h = layout.FloorHeight, d = layout.Depth, sx = layout.ShaftX;
            float left = sx - w * 0.5f, right = sx + w * 0.5f;
            int n = layout.FloorCount;
            float height = n * h;

            var slab = assets.Lit(RenderingStyle.Slab);
            var slabEdge = assets.Lit(RenderingStyle.SlabEdge);
            var outer = assets.Lit(RenderingStyle.OuterWall);
            var shaftBack = assets.Lit(RenderingStyle.ShaftBack);
            var frame = assets.Lit(RenderingStyle.ShaftFrame);

            // foundation + roof
            assets.Box(Root, "Foundation", new Vector3(sx, -0.4f, 0f), new Vector3(w + WallThickness * 2f, 0.8f, d + WallThickness), outer);
            SlabWithShaftHole("Roof", height, slab, slabEdge, includeHole: false);
            assets.Box(Root, "Parapet", new Vector3(sx, height + 0.25f, -d * 0.5f + 0.1f), new Vector3(w + WallThickness * 2f, 0.5f, 0.2f), outer);
            // outer side walls (full height)
            assets.Box(Root, "Wall L", new Vector3(left - WallThickness * 0.5f, height * 0.5f, 0f), new Vector3(WallThickness, height, d + WallThickness), outer);
            assets.Box(Root, "Wall R", new Vector3(right + WallThickness * 0.5f, height * 0.5f, 0f), new Vector3(WallThickness, height, d + WallThickness), outer);
            // shaft: dark back panel + gold front posts for the whole height
            float shaftBackZ = d * 0.5f - 0.35f;
            assets.Box(Root, "Shaft Back", new Vector3(sx, height * 0.5f, shaftBackZ), new Vector3(ShaftHalfWidth * 2f, height, 0.1f), shaftBack, false);
            assets.Box(Root, "Shaft Post L", new Vector3(sx - ShaftHalfWidth, height * 0.5f, -d * 0.5f + 0.1f), new Vector3(0.18f, height, 0.2f), frame);
            assets.Box(Root, "Shaft Post R", new Vector3(sx + ShaftHalfWidth, height * 0.5f, -d * 0.5f + 0.1f), new Vector3(0.18f, height, 0.2f), frame);

            for (int i = 0; i < n; i++)
            {
                var f = new FloorIndex(i);
                BuildFloor(f, destinationOf(f), labelOf(f), relabelledOf(f), slab, slabEdge);
            }

            BuildRoofSign(height);
            BuildStreet(sx, w, d);
        }

        void SlabWithShaftHole(string name, float topY, Material slab, Material edge, bool includeHole)
        {
            float w = layout.FloorWidth, d = layout.Depth, sx = layout.ShaftX;
            float left = sx - w * 0.5f, right = sx + w * 0.5f;
            float y = topY - SlabThickness * 0.5f;
            if (!includeHole)
            {
                assets.Box(Root, name, new Vector3(sx, y, 0f), new Vector3(w, SlabThickness, d), slab);
                assets.Box(Root, name + " Edge", new Vector3(sx, y, -d * 0.5f - 0.02f), new Vector3(w, SlabThickness, 0.04f), edge, false);
                return;
            }
            float holeL = sx - ShaftHalfWidth, holeR = sx + ShaftHalfWidth;
            assets.Box(Root, name + " L", new Vector3((left + holeL) * 0.5f, y, 0f), new Vector3(holeL - left, SlabThickness, d), slab);
            assets.Box(Root, name + " R", new Vector3((holeR + right) * 0.5f, y, 0f), new Vector3(right - holeR, SlabThickness, d), slab);
            assets.Box(Root, name + " Edge L", new Vector3((left + holeL) * 0.5f, y, -d * 0.5f - 0.02f), new Vector3(holeL - left, SlabThickness, 0.04f), edge, false);
            assets.Box(Root, name + " Edge R", new Vector3((holeR + right) * 0.5f, y, -d * 0.5f - 0.02f), new Vector3(right - holeR, SlabThickness, 0.04f), edge, false);
        }

        void BuildFloor(FloorIndex f, DestinationId destination, FloorLabel label, bool relabelled, Material slab, Material slabEdge)
        {
            float w = layout.FloorWidth, h = layout.FloorHeight, d = layout.Depth, sx = layout.ShaftX;
            float left = sx - w * 0.5f, right = sx + w * 0.5f;
            float y0 = f.Value * h;
            var floorRoot = new GameObject("Floor " + f.PhysicalNumber).transform;
            floorRoot.SetParent(Root, false);
            var entry = new Floor();
            floors.Add(entry);

            // floor slab (ground floor stands on the foundation)
            if (f.Value > 0) SlabWithShaftHole("Slab " + f.PhysicalNumber, y0, slab, slabEdge, includeHole: true);

            var lightColor = RenderingStyle.FloorLightColor(destination, f.Value);
            var wallBase = RenderingStyle.Wallpaper(lightColor);
            var wall = assets.NewLit(wallBase);
            float wallH = h - SlabThickness;
            // back wall in two halves around the shaft (shaft back panel sits in the gap)
            float holeL = sx - ShaftHalfWidth, holeR = sx + ShaftHalfWidth;
            assets.Box(floorRoot, "Back L", new Vector3((left + holeL) * 0.5f, y0 + wallH * 0.5f, d * 0.5f + WallThickness * 0.5f), new Vector3(holeL - left, wallH, WallThickness), wall);
            assets.Box(floorRoot, "Back R", new Vector3((holeR + right) * 0.5f, y0 + wallH * 0.5f, d * 0.5f + WallThickness * 0.5f), new Vector3(right - holeR, wallH, WallThickness), wall);
            var wainscot = assets.Lit(wallBase * 0.55f);
            assets.Box(floorRoot, "Wainscot L", new Vector3((left + holeL) * 0.5f, y0 + 0.4f, d * 0.5f - 0.03f), new Vector3(holeL - left, 0.8f, 0.06f), wainscot, false);
            assets.Box(floorRoot, "Wainscot R", new Vector3((holeR + right) * 0.5f, y0 + 0.4f, d * 0.5f - 0.03f), new Vector3(right - holeR, 0.8f, 0.06f), wainscot, false);

            // shaft lintel (door header) on the front posts
            assets.Box(floorRoot, "Lintel", new Vector3(sx, y0 + h - SlabThickness - 0.12f, -d * 0.5f + 0.1f), new Vector3(ShaftHalfWidth * 2f, 0.24f, 0.2f), assets.Lit(RenderingStyle.ShaftFrame));
            // "car is here" lamp above the landing (driven by ElevatorView)
            entry.Indicator = assets.NewUnlit(RenderingStyle.IndicatorIdle);
            assets.Box(floorRoot, "Indicator", new Vector3(sx, y0 + h - SlabThickness - 0.12f, -d * 0.5f - 0.02f), new Vector3(0.5f, 0.14f, 0.06f), entry.Indicator, false);

            // waiting mat (right) and stair exit door (far right)
            assets.Box(floorRoot, "Waiting Mat", new Vector3((holeR + 0.5f + right - 0.4f) * 0.5f, y0 + 0.02f, 0f), new Vector3(right - 0.4f - holeR - 0.5f, 0.04f, 1.4f), assets.Lit(wallBase * 0.4f), false);
            assets.Box(floorRoot, "Exit Door", new Vector3(right - 0.9f, y0 + 1.05f, d * 0.5f - 0.05f), new Vector3(1.0f, 2.1f, 0.1f), assets.Lit(new Color(0.25f, 0.17f, 0.12f)), false);
            assets.Box(floorRoot, "Exit Lamp", new Vector3(right - 0.9f, y0 + 2.25f, d * 0.5f - 0.05f), new Vector3(0.5f, 0.16f, 0.1f), assets.Unlit(new Color(0.3f, 1f, 0.5f)), false);

            // one point light + one hanging tube per half (room / waiting area). Tubes hang near the front so the
            // slab edge does not hide them from the slightly-downward camera.
            lighting.AddFloor(wall, wallBase, lightColor, RenderingStyle.FloorLightIntensity);
            float roomCeil = y0 + h - SlabThickness;
            for (int t = 0; t < 2; t++)
            {
                float tx = t == 0 ? (left + holeL) * 0.5f : (holeR + right) * 0.5f;
                var lightGo = new GameObject("Light " + t);
                lightGo.transform.SetParent(floorRoot, false);
                lightGo.transform.localPosition = new Vector3(tx, roomCeil - 0.45f, -d * 0.2f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = RenderingStyle.FloorLightRange;
                light.color = lightColor;
                light.intensity = RenderingStyle.FloorLightIntensity;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                lighting.AddLight(f.Value, light);

                var tube = assets.NewUnlit(lightColor);
                assets.Box(floorRoot, "Tube " + t, new Vector3(tx, roomCeil - 0.3f, -d * 0.5f + 0.8f), new Vector3(2.4f, 0.14f, 0.14f), tube, false);
                assets.Box(floorRoot, "Hanger " + t, new Vector3(tx, roomCeil - 0.12f, -d * 0.5f + 0.8f), new Vector3(0.04f, 0.24f, 0.04f), assets.Lit(RenderingStyle.ShaftBack), false);
                lighting.AddTube(f.Value, tube);
            }

            BuildProps(floorRoot, destination, y0, left + 0.4f, holeL - 0.3f, lightColor);

            // the floor sign: front plate just left of the shaft, facing the camera
            entry.Sign = new GameObject(RenderingNames.FloorSignPrefix + f.PhysicalNumber);
            entry.Sign.transform.SetParent(floorRoot, false);
            SetLabel(f, label, relabelled);
        }

        /// <summary>Re-renders floor <paramref name="floor"/>'s sign. No-op when nothing changed.</summary>
        public void SetLabel(FloorIndex floor, FloorLabel label, bool sticker)
        {
            if (floor.Value < 0 || floor.Value >= floors.Count) return;
            var e = floors[floor.Value];
            string text = label.IsEmpty ? FloorLabel.Original(floor).Text : label.Text;
            if (e.SignTexture != null && e.SignText == text && e.SignSticker == sticker) return;

            var tex = assets.Own(PixelFont.CreateTexture(text,
                sticker ? RenderingStyle.StickerInk : RenderingStyle.SignInk,
                sticker ? RenderingStyle.StickerPlate : RenderingStyle.SignPlate));
            if (e.SignTexture != null) Object.Destroy(e.SignTexture);
            e.SignTexture = tex;
            e.SignText = text;
            e.SignSticker = sticker;

            if (e.SignMaterial == null)
            {
                e.SignMaterial = assets.NewUnlit(Color.white, tex);
                var plate = assets.Plate(e.Sign.transform, "Plate", Vector3.zero, Vector2.one, e.SignMaterial);
                plate.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            else RenderAssets.SetTexture(e.SignMaterial, tex);

            float sw = tex.width * signTexel, sh = tex.height * signTexel;
            var plateT = e.Sign.transform.GetChild(0);
            plateT.localScale = new Vector3(sw, sh, 1f);
            float h = layout.FloorHeight, d = layout.Depth, sx = layout.ShaftX;
            // right edge 0.25 left of the shaft post, vertically centred in the open part of the floor
            e.Sign.transform.localPosition = new Vector3(sx - ShaftHalfWidth - 0.35f - sw * 0.5f, floor.Value * h + (h - SlabThickness) * 0.58f, -d * 0.5f - 0.06f);
        }

        public string GetSignText(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count ? floors[floor.Value].SignText : null;
        public bool GetSignSticker(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count && floors[floor.Value].SignSticker;
        public GameObject GetSign(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count ? floors[floor.Value].Sign : null;
        public int FloorCount => floors.Count;

        public void SetIndicator(FloorIndex floor, Color color)
        {
            if (floor.Value >= 0 && floor.Value < floors.Count) RenderAssets.SetColor(floors[floor.Value].Indicator, color);
        }

        // ------------------------------------------------------------------ dressing

        /// <summary>A few boxes per destination on the left half so floors read apart (greybox, placeholder for CC0 props).</summary>
        void BuildProps(Transform parent, DestinationId destination, float y0, float xMin, float xMax, Color lightColor)
        {
            float span = xMax - xMin;
            float d = layout.Depth;
            float zBack = d * 0.5f - 0.6f;
            var wood = assets.Lit(new Color(0.45f, 0.29f, 0.17f));
            var darkWood = assets.Lit(new Color(0.26f, 0.16f, 0.10f));
            var cloth = assets.Lit(new Color(0.88f, 0.86f, 0.80f));
            var metal = assets.Lit(new Color(0.42f, 0.44f, 0.48f));
            var green = assets.Lit(new Color(0.20f, 0.48f, 0.26f));
            var felt = assets.Lit(new Color(0.10f, 0.40f, 0.22f));
            var red = assets.Lit(new Color(0.62f, 0.14f, 0.14f));
            var purple = assets.Lit(new Color(0.36f, 0.20f, 0.48f));
            float X(float t) => xMin + span * t;
            void P(string name, float t, float w, float h, float dz, float z, Material m, float yOff = 0f)
                => assets.Box(parent, name, new Vector3(X(t), y0 + yOff + h * 0.5f, z), new Vector3(w, h, dz), m);

            switch (destination.Value)
            {
                case "lobby":
                    P("Desk", 0.55f, 2.6f, 1.0f, 0.8f, zBack - 0.2f, wood);
                    P("Desk Top", 0.55f, 2.8f, 0.1f, 0.9f, zBack - 0.2f, darkWood, 1.0f);
                    P("Pot", 0.08f, 0.5f, 0.5f, 0.5f, zBack, red);
                    P("Plant", 0.08f, 0.7f, 0.9f, 0.7f, zBack, green, 0.5f);
                    P("Bell", 0.6f, 0.18f, 0.12f, 0.18f, zBack - 0.2f, assets.Unlit(RenderingStyle.WindowGlow), 1.1f);
                    break;
                case "restaurant":
                    for (int i = 0; i < 2; i++)
                    {
                        float t = 0.22f + i * 0.5f;
                        P("Table " + i, t, 1.3f, 0.75f, 1.0f, 0.4f, cloth);
                        P("Chair " + i + "a", t - 0.16f, 0.45f, 0.5f, 0.45f, 0.4f, wood);
                        P("Chair " + i + "b", t + 0.16f, 0.45f, 0.5f, 0.45f, 0.4f, wood);
                        P("Candle " + i, t, 0.12f, 0.2f, 0.12f, 0.4f, assets.Unlit(new Color(1f, 0.7f, 0.3f)), 0.75f);
                    }
                    break;
                case "gym":
                    P("Treadmill", 0.2f, 1.7f, 0.35f, 0.8f, 0.3f, metal);
                    P("Treadmill Bar", 0.32f, 0.12f, 1.2f, 0.8f, 0.3f, metal, 0.35f);
                    P("Rack", 0.72f, 1.6f, 1.2f, 0.5f, zBack, darkWood);
                    P("Weights", 0.72f, 1.4f, 0.25f, 0.4f, zBack - 0.1f, assets.Lit(new Color(0.12f, 0.12f, 0.14f)), 0.6f);
                    break;
                case "karaoke":
                    P("Screen", 0.5f, 3.0f, 1.4f, 0.1f, d * 0.5f - 0.08f, assets.Unlit(new Color(0.35f, 0.9f, 1f)), 0.9f);
                    P("Sofa", 0.5f, 3.2f, 0.55f, 0.8f, -0.2f, purple);
                    P("Sofa Back", 0.5f, 3.2f, 0.5f, 0.2f, 0.25f, purple, 0.55f);
                    break;
                case "sauna":
                    P("Bench Low", 0.42f, 4.2f, 0.45f, 1.3f, zBack - 0.2f, wood);
                    P("Bench High", 0.42f, 4.2f, 0.45f, 0.6f, zBack + 0.15f, wood, 0.45f);
                    P("Stove", 0.92f, 0.7f, 0.8f, 0.7f, 0.2f, assets.Lit(new Color(0.2f, 0.18f, 0.17f)));
                    P("Coals", 0.92f, 0.5f, 0.12f, 0.5f, 0.2f, assets.Unlit(new Color(1f, 0.35f, 0.1f)), 0.8f);
                    break;
                case "rooms":
                    P("Bed", 0.35f, 2.2f, 0.55f, 1.6f, 0.6f, cloth);
                    P("Blanket", 0.38f, 1.6f, 0.1f, 1.62f, 0.6f, assets.Lit(lightColor * 0.6f), 0.55f);
                    P("Headboard", 0.35f, 2.2f, 1.1f, 0.15f, zBack + 0.35f, darkWood);
                    P("Lamp", 0.72f, 0.3f, 0.35f, 0.3f, zBack, assets.Unlit(RenderingStyle.WindowGlow), 0.6f);
                    P("Nightstand", 0.72f, 0.6f, 0.6f, 0.5f, zBack, wood);
                    break;
                case "bar":
                    P("Counter", 0.5f, 3.6f, 1.1f, 0.7f, 0.5f, darkWood);
                    P("Shelf", 0.5f, 3.6f, 0.12f, 0.3f, d * 0.5f - 0.15f, wood, 1.5f);
                    P("Bottles", 0.5f, 3.2f, 0.35f, 0.2f, d * 0.5f - 0.15f, assets.Unlit(new Color(0.9f, 0.6f, 0.25f)), 1.62f);
                    P("Stool 0", 0.3f, 0.4f, 0.7f, 0.4f, -0.2f, red);
                    P("Stool 1", 0.6f, 0.4f, 0.7f, 0.4f, -0.2f, red);
                    break;
                case "casino":
                    P("Table", 0.35f, 2.4f, 0.8f, 1.3f, 0.3f, felt);
                    P("Slot", 0.85f, 0.8f, 1.7f, 0.6f, zBack, red);
                    P("Slot Screen", 0.85f, 0.55f, 0.45f, 0.05f, zBack - 0.33f, assets.Unlit(new Color(1f, 0.85f, 0.3f)), 1.05f);
                    break;
                case "rooftop":
                    P("Planter 0", 0.1f, 1.0f, 0.6f, 0.8f, zBack, darkWood);
                    P("Shrub 0", 0.1f, 0.9f, 0.6f, 0.7f, zBack, green, 0.6f);
                    P("Lounger", 0.55f, 2.0f, 0.4f, 0.8f, 0.2f, assets.Lit(new Color(0.85f, 0.85f, 0.9f)));
                    P("Umbrella", 0.8f, 1.6f, 0.15f, 1.6f, 0.2f, red, 2.0f);
                    P("Pole", 0.8f, 0.1f, 2.0f, 0.1f, 0.2f, metal);
                    break;
                default:
                    P("Crate", 0.3f, 1.0f, 1.0f, 1.0f, zBack, wood);
                    break;
            }
        }

        void BuildRoofSign(float height)
        {
            var tex = assets.Own(PixelFont.CreateTexture("HOTEL", RenderingStyle.HotelSign, new Color(0.10f, 0.05f, 0.06f)));
            var mat = assets.NewUnlit(Color.white, tex);
            float w = tex.width * signTexel, h = tex.height * signTexel;
            float d = layout.Depth;
            assets.Plate(Root, "Hotel Sign", new Vector3(layout.ShaftX, height + 0.5f + h * 0.5f, -d * 0.5f + 0.6f), new Vector2(w, h), mat);
            var metal = assets.Lit(new Color(0.22f, 0.22f, 0.25f));
            assets.Box(Root, "Sign Leg L", new Vector3(layout.ShaftX - w * 0.35f, height + 0.4f, -d * 0.5f + 0.7f), new Vector3(0.12f, 0.8f, 0.12f), metal);
            assets.Box(Root, "Sign Leg R", new Vector3(layout.ShaftX + w * 0.35f, height + 0.4f, -d * 0.5f + 0.7f), new Vector3(0.12f, 0.8f, 0.12f), metal);
            assets.Box(Root, "Water Tank", new Vector3(layout.ShaftX + layout.FloorWidth * 0.32f, height + 0.9f, d * 0.2f), new Vector3(1.4f, 1.8f, 1.4f), metal);
        }

        void BuildStreet(float sx, float w, float d)
        {
            assets.Box(Root, "Street", new Vector3(sx, -1.0f, 4f), new Vector3(160f, 0.4f, 40f), assets.Lit(RenderingStyle.Ground), false);
            assets.Box(Root, "Sidewalk", new Vector3(sx, -0.75f, -d * 0.5f - 1.2f), new Vector3(160f, 0.1f, 2.2f), assets.Lit(RenderingStyle.Sidewalk), false);

            // dark skyline blocks behind/next to the hotel with a few lit windows
            var block = assets.Lit(RenderingStyle.Skyline);
            var window = assets.Unlit(RenderingStyle.WindowGlow * 0.8f);
            var dim = assets.Unlit(new Color(0.20f, 0.22f, 0.34f));
            float[] xs = { -30f, -19f, 19f, 29f, -42f, 41f };
            float[] hs = { 20f, 14f, 17f, 24f, 11f, 13f };
            float[] ws = { 9f, 8f, 8f, 9f, 10f, 10f };
            for (int i = 0; i < xs.Length; i++)
            {
                float bx = sx + xs[i], bh = hs[i], bw = ws[i], bz = 8f + (i % 2) * 3f;
                assets.Box(Root, "Skyline " + i, new Vector3(bx, bh * 0.5f - 0.8f, bz), new Vector3(bw, bh, 4f), block, false);
                int cols = Mathf.FloorToInt(bw / 2f), rows = Mathf.FloorToInt(bh / 3f);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float hsh = Mathf.Repeat((i * 7 + r * 13 + c * 5) * 0.618f, 1f);
                        if (hsh > 0.55f) continue;
                        var m = hsh < 0.18f ? window : dim;
                        assets.Plate(Root, "Win", new Vector3(bx - bw * 0.5f + 1f + c * 2f, r * 3f + 1.5f, bz - 2.02f), new Vector2(0.8f, 1.1f), m);
                    }
            }
        }
    }
}
