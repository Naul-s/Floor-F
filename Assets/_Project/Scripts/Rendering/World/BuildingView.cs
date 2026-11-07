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
            /// <summary>Sticker "slap" animation age (&gt;= StickSeconds = idle).</summary>
            public float StickAge = float.MaxValue;
            /// <summary>Ending: plate showing the original number behind the (falling) sticker.</summary>
            public GameObject Reveal;
            public string RevealText;
            public bool Falling, Fallen;
            public Vector3 FallVelocity;
            public float FallSpin;
            public readonly List<PropInfo> Props = new List<PropInfo>();
            public MeshRenderer PropRenderer;
        }

        public const float StickSeconds = 0.32f;
        Material hotelSign;
        public Transform HotelSign { get; private set; }

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

            if (assets.Kenney != null) BuildKenneyProps(floorRoot, entry, destination, y0);
            else BuildProps(floorRoot, destination, y0, left + 0.4f, holeL - 0.3f, lightColor);

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

        /// <summary>Roof HOTEL sign tint (white = normal, darker = off, &gt;1 not possible: unlit, so use warm whites).</summary>
        public void SetHotelSignTint(Color tint)
        {
            if (hotelSign != null) RenderAssets.SetColor(hotelSign, tint);
        }

        public Color HotelSignTint => hotelSign != null && hotelSign.HasProperty("_BaseColor") ? hotelSign.GetColor("_BaseColor") : Color.white;

        /// <summary>Sticker "slap": the sign overshoots big, slams down small, settles (scale on the sign root).</summary>
        public void PlayStick(FloorIndex floor)
        {
            if (floor.Value >= 0 && floor.Value < floors.Count) floors[floor.Value].StickAge = 0f;
        }

        public bool IsSignAnimating(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count && floors[floor.Value].StickAge < StickSeconds;

        /// <summary>Pure: sign scale during the stick animation (1 when idle).</summary>
        public static float StickScale(float age)
        {
            if (age < 0f || age >= StickSeconds) return 1f;
            const float slam = 0.07f;
            if (age < slam) return Mathf.Lerp(1.7f, 0.82f, age / slam);
            float u = (age - slam) / (StickSeconds - slam);
            return 1f - 0.18f * Mathf.Exp(-5f * u) * Mathf.Cos(u * Mathf.PI * 3f) * (1f - u);
        }

        /// <summary>
        /// Ending reveal: a plate with <paramref name="original"/> appears behind the sticker plate and the sticker drops
        /// off (falls, spins, disappears). No-op when the sign already shows that text.
        /// </summary>
        public void RevealOriginal(FloorIndex floor, string original, Color ink, Color plate)
        {
            if (floor.Value < 0 || floor.Value >= floors.Count || string.IsNullOrEmpty(original)) return;
            var e = floors[floor.Value];
            if (e.Reveal != null) return;
            var tex = assets.Own(PixelFont.CreateTexture(original, ink, plate));
            var mat = assets.NewUnlit(Color.white, tex);
            e.Reveal = assets.Plate(e.Sign.transform, "Reveal", new Vector3(0f, 0f, 0.015f), new Vector2(tex.width * signTexel, tex.height * signTexel), mat);
            e.RevealText = original;
            if (e.SignText == original) { e.Fallen = true; return; }
            e.Falling = true;
            e.FallVelocity = new Vector3(-0.6f, 1.2f, -0.4f);
            e.FallSpin = 0f;
        }

        public bool IsRevealed(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count && floors[floor.Value].Reveal != null;
        public bool IsStickerFallen(FloorIndex floor) => floor.Value >= 0 && floor.Value < floors.Count && floors[floor.Value].Fallen;

        /// <summary>What the sign shows right now: the revealed number once the sticker fell off, else the label.</summary>
        public string GetDisplayedText(FloorIndex floor)
        {
            if (floor.Value < 0 || floor.Value >= floors.Count) return null;
            var e = floors[floor.Value];
            return e.Reveal != null && e.Fallen ? e.RevealText : e.SignText;
        }

        /// <summary>Undo endings' reveals (restart).</summary>
        public void ClearReveals()
        {
            for (int i = 0; i < floors.Count; i++)
            {
                var e = floors[i];
                if (e.Reveal != null) Object.Destroy(e.Reveal);
                e.Reveal = null;
                e.RevealText = null;
                e.Falling = e.Fallen = false;
                var plate = e.Sign.transform.childCount > 0 ? e.Sign.transform.GetChild(0) : null;
                if (plate != null)
                {
                    plate.gameObject.SetActive(true);
                    plate.localPosition = Vector3.zero;
                    plate.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>Animations on the juice clock (dt = 0 while paused).</summary>
        public void Tick(float dt)
        {
            for (int i = 0; i < floors.Count; i++)
            {
                var e = floors[i];
                if (e.StickAge < StickSeconds)
                {
                    e.StickAge += dt;
                    float k = StickScale(e.StickAge);
                    e.Sign.transform.localScale = new Vector3(k, k, 1f);
                    float tilt = e.StickAge < StickSeconds ? -9f * (1f - e.StickAge / StickSeconds) : 0f;
                    e.Sign.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                }
                if (e.Falling && dt > 0f)
                {
                    var plate = e.Sign.transform.GetChild(0);
                    e.FallVelocity += new Vector3(0f, -9f * dt, 0f);
                    plate.localPosition += e.FallVelocity * dt;
                    e.FallSpin += 220f * dt;
                    plate.localRotation = Quaternion.Euler(0f, 0f, e.FallSpin);
                    if (plate.localPosition.y < -layout.FloorHeight * 0.62f)
                    {
                        e.Falling = false;
                        e.Fallen = true;
                        plate.gameObject.SetActive(false);
                    }
                }
            }
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

        /// <summary>Greybox fallback (no Kenney catalog): a few boxes per destination on the left half.</summary>
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

        // ------------------------------------------------------------------ Kenney props

        /// <summary>One placed prop (for tests/inspection): catalog mesh or swatch-cube name + world-space bounds.</summary>
        public readonly struct PropInfo
        {
            public readonly string Name;
            public readonly Bounds Bounds;
            public PropInfo(string name, Bounds bounds) { Name = name; Bounds = bounds; }
        }

        /// <summary>Left edge of the widest possible floor sign (sign plates sit just left of the shaft).</summary>
        public float SignZoneLeftX
        {
            get
            {
                int digits = Mathf.Max(1, layout.FloorCount.ToString().Length);
                float sw = PixelFont.Measure(new string('8', digits), 1).x * signTexel;
                return layout.ShaftX - ShaftHalfWidth - 0.35f - sw;
            }
        }

        /// <summary>X range props may use: inside the left wall up to the sign zone (shaft and queue are to the right).</summary>
        public float PropZoneMinX => layout.ShaftX - layout.FloorWidth * 0.5f + 0.3f;
        public float PropZoneMaxX => SignZoneLeftX - 0.15f;

        public IReadOnlyList<PropInfo> GetProps(FloorIndex floor) =>
            floor.Value >= 0 && floor.Value < floors.Count ? floors[floor.Value].Props : (IReadOnlyList<PropInfo>)System.Array.Empty<PropInfo>();

        /// <summary>The floor's merged prop renderer (one draw per floor), null in greybox mode.</summary>
        public MeshRenderer GetPropRenderer(FloorIndex floor) =>
            floor.Value >= 0 && floor.Value < floors.Count ? floors[floor.Value].PropRenderer : null;

        /// <summary>
        /// Kenney furniture/arcade dressing on the left half of a floor, all merged into ONE mesh with the shared atlas
        /// material (props never move). Positions are authored for a 4.4-unit zone and squeezed if the zone is narrower.
        /// Meshes face the camera (-Z) with a bottom-centre pivot; yaw +90 turns a prop to face -X.
        /// </summary>
        void BuildKenneyProps(Transform floorRoot, Floor entry, DestinationId destination, float y0)
        {
            var kenney = assets.Kenney;
            float xMin = PropZoneMinX, xMax = PropZoneMaxX;
            float squeeze = Mathf.Min(1f, (xMax - xMin) / 4.4f);
            var parts = new List<CombineInstance>();
            var names = new List<string>();

            void Add(string name, Mesh mesh, Matrix4x4 m)
            {
                if (mesh == null) return;
                // keep every prop inside the zone (never under the sign, never into the shaft)
                var bb = TransformBounds(mesh.bounds, m);
                float shift = bb.min.x < xMin ? xMin - bb.min.x : bb.max.x > xMax ? xMax - bb.max.x : 0f;
                if (shift != 0f) m = Matrix4x4.Translate(new Vector3(shift, 0f, 0f)) * m;
                parts.Add(new CombineInstance { mesh = mesh, transform = m });
                names.Add(name);
            }
            // catalog mesh: x = centre from the zone's left edge, z = depth centre, uniform scale, yaw, lift (stacking)
            void M(string mesh, float x, float z, float scale = 1f, float yaw = 0f, float lift = 0f)
                => Add(mesh, kenney.FindProp(mesh), Matrix4x4.TRS(new Vector3(xMin + x * squeeze, y0 + lift, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale));
            // swatch cube: bottom at lift
            void C(string swatch, float x, float z, Vector3 size, float lift = 0f)
                => Add("cube:" + swatch, assets.SwatchCube(swatch), Matrix4x4.TRS(new Vector3(xMin + x * squeeze, y0 + lift + size.y * 0.5f, z), Quaternion.identity, size));
            float Top(string mesh, float scale = 1f) { var m = kenney.FindProp(mesh); return m != null ? m.bounds.size.y * scale : 0f; }

            switch (destination.Value)
            {
                case "lobby":
                    M("rugRectangle", 1.6f, 0.4f, 0.85f);
                    M("pottedPlant", 0.25f, 1.3f, 0.9f);
                    M("loungeSofa", 1.45f, 1.05f, 0.95f);
                    M("desk", 3.35f, -0.2f, 1.05f);
                    M("cash-register", 3.55f, -0.2f, 0.32f, 0f, Top("desk", 1.05f));
                    M("lampRoundTable", 2.85f, -0.1f, 1.1f, 0f, Top("desk", 1.05f));
                    M("plantSmall2", 4.2f, 1.5f, 2.2f);
                    break;
                case "restaurant":
                    M("chairCushion", 0.25f, 0.4f, 1f, -90f);
                    M("tableCloth", 1.2f, 0.4f, 1f);
                    M("chairCushion", 2.15f, 0.4f, 1f, 90f);
                    M("tableRound", 3.55f, 0.9f, 0.85f);
                    M("chair", 3.55f, 1.75f, 1f, 180f);
                    M("chair", 3.55f, 0.0f, 1f);
                    C("gold", 1.2f, 0.4f, new Vector3(0.08f, 0.16f, 0.08f), Top("tableCloth"));
                    break;
                case "gym":
                    // weight bench + barbell on uprights, dumbbell rack, primitive treadmill (no gym kit in the packs)
                    M("bench", 1.0f, 0.2f, 1f);
                    C("iron", 0.45f, 0.2f, new Vector3(0.08f, 1.25f, 0.08f));
                    C("iron", 1.55f, 0.2f, new Vector3(0.08f, 1.25f, 0.08f));
                    C("chrome", 1.0f, 0.2f, new Vector3(1.55f, 0.05f, 0.05f), 1.2f);
                    C("iron", 0.33f, 0.2f, new Vector3(0.08f, 0.42f, 0.42f), 1.02f);
                    C("iron", 1.67f, 0.2f, new Vector3(0.08f, 0.42f, 0.42f), 1.02f);
                    C("iron", 2.55f, 1.4f, new Vector3(1.0f, 0.5f, 0.45f));
                    for (int i = 0; i < 3; i++)
                    {
                        float dx = 2.2f + i * 0.35f;
                        C("chrome", dx, 1.25f, new Vector3(0.05f, 0.05f, 0.34f), 0.56f);
                        C("iron", dx, 1.1f, new Vector3(0.14f, 0.14f, 0.07f), 0.52f);
                        C("iron", dx, 1.4f, new Vector3(0.14f, 0.14f, 0.07f), 0.52f);
                    }
                    C("iron", 3.75f, 0.3f, new Vector3(1.1f, 0.16f, 0.62f));
                    C("chrome", 4.2f, 0.3f, new Vector3(0.07f, 1.15f, 0.07f), 0.16f);
                    C("screen", 4.15f, 0.3f, new Vector3(0.18f, 0.16f, 0.5f), 1.25f);
                    break;
                case "karaoke":
                    M("dance-machine", 0.55f, 0.5f, 0.55f);
                    M("speaker", 1.4f, 1.5f, 1f);
                    M("televisionModern", 2.55f, 1.75f, 1f, 0f, 0.75f);
                    C("neon", 2.55f, 1.92f, new Vector3(1.7f, 0.06f, 0.06f), 1.75f);
                    M("speaker", 3.7f, 1.5f, 1f);
                    M("loungeSofa", 2.55f, -0.2f, 0.8f);
                    M("speakerSmall", 4.2f, 0.4f, 1f);
                    break;
                case "sauna":
                    M("bathtub", 1.15f, 0.6f, 0.9f);
                    M("shower", 2.85f, 1.3f, 1f);
                    M("bench", 3.95f, 0.1f, 1f);
                    C("stone", 3.95f, 1.4f, new Vector3(0.6f, 0.4f, 0.5f));
                    C("coal", 3.95f, 1.4f, new Vector3(0.45f, 0.08f, 0.35f), 0.4f);
                    C("towel", 1.15f, 0.05f, new Vector3(0.5f, 0.06f, 0.3f), Top("bathtub", 0.9f));
                    break;
                case "rooms":
                    M("cabinetBedDrawerTable", 0.3f, 1.6f, 1f);
                    M("lampRoundTable", 0.3f, 1.6f, 1f, 0f, Top("cabinetBedDrawerTable"));
                    M("bedDouble", 1.45f, 0.85f, 0.85f);
                    M("bedSingle", 3.25f, 0.85f, 0.85f);
                    M("cabinetBedDrawerTable", 4.1f, 1.6f, 1f);
                    M("lampRoundTable", 4.1f, 1.6f, 1f, 0f, Top("cabinetBedDrawerTable"));
                    break;
                case "bar":
                    // shelf with bottles on the back wall, counter, stools in front
                    C("bottle", 2.0f, 1.85f, new Vector3(2.8f, 0.06f, 0.25f), 1.35f);
                    for (int i = 0; i < 7; i++) C(i % 2 == 0 ? "bottle" : "felt", 0.8f + i * 0.4f, 1.85f, new Vector3(0.12f, 0.3f, 0.12f), 1.41f);
                    M("lampSquareFloor", 0.2f, 1.5f, 1f);
                    M("kitchenBar", 1.0f, 0.55f, 1f);
                    M("kitchenBar", 1.86f, 0.55f, 1f);
                    M("kitchenBar", 2.72f, 0.55f, 1f);
                    M("kitchenBarEnd", 3.25f, 0.55f, 1f);
                    M("lampRoundTable", 2.72f, 0.55f, 0.9f, 0f, Top("kitchenBar"));
                    M("stoolBar", 1.0f, -0.35f, 1f);
                    M("stoolBar", 1.86f, -0.35f, 1f);
                    M("stoolBar", 2.72f, -0.35f, 1f);
                    break;
                case "casino":
                    M("gambling-machine", 0.5f, 1.25f, 0.75f);
                    M("gambling-machine", 1.5f, 1.25f, 0.75f);
                    M("prize-wheel", 2.65f, 1.45f, 0.75f);
                    M("arcade-machine", 3.85f, 1.2f, 0.75f);
                    // card table: felt top on a wooden base
                    C("felt", 2.1f, -0.3f, new Vector3(1.5f, 0.08f, 0.8f), 0.66f);
                    C("wood", 2.1f, -0.3f, new Vector3(1.3f, 0.66f, 0.6f));
                    C("gold", 1.8f, -0.4f, new Vector3(0.12f, 0.08f, 0.12f), 0.74f);
                    C("gold", 2.4f, -0.2f, new Vector3(0.12f, 0.12f, 0.12f), 0.74f);
                    break;
                case "rooftop":
                    M("pottedPlant", 0.25f, 1.5f, 1.0f);
                    M("plantSmall2", 0.75f, 1.6f, 2.6f);
                    M("loungeChair", 1.6f, 0.2f, 1f);
                    C("chrome", 2.35f, 0.4f, new Vector3(0.06f, 1.9f, 0.06f));
                    C("umbrella", 2.35f, 0.4f, new Vector3(1.6f, 0.12f, 1.2f), 1.85f);
                    M("loungeChair", 3.05f, 0.2f, 1f);
                    M("bench", 4.0f, 1.4f, 1f);
                    M("plantSmall1", 4.3f, 0.2f, 2.6f);
                    break;
                default:
                    M("bookcaseOpenLow", 1.0f, 1.6f, 1f);
                    M("plantSmall1", 2.0f, 1.6f, 2.5f);
                    break;
            }
            if (parts.Count == 0) return;

            var combined = assets.Own(new Mesh { name = "Props " + destination.Value });
            int verts = 0;
            foreach (var p in parts) verts += p.mesh.vertexCount;
            if (verts > 65000) combined.indexFormat = IndexFormat.UInt32;
            combined.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject("Props");
            go.transform.SetParent(floorRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = combined;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = kenney.Material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            entry.PropRenderer = r;
            var toWorld = go.transform.localToWorldMatrix;
            for (int i = 0; i < parts.Count; i++)
                entry.Props.Add(new PropInfo(names[i], TransformBounds(parts[i].mesh.bounds, toWorld * parts[i].transform)));
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var c = b.center; var e = b.extents;
            var r = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return r;
        }

        void BuildRoofSign(float height)
        {
            var tex = assets.Own(PixelFont.CreateTexture("HOTEL", RenderingStyle.HotelSign, new Color(0.10f, 0.05f, 0.06f)));
            var mat = assets.NewUnlit(Color.white, tex);
            hotelSign = mat;
            float w = tex.width * signTexel, h = tex.height * signTexel;
            float d = layout.Depth;
            var signGo = assets.Plate(Root, "Hotel Sign", new Vector3(layout.ShaftX, height + 0.5f + h * 0.5f, -d * 0.5f + 0.6f), new Vector2(w, h), mat);
            HotelSign = signGo.transform;
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
