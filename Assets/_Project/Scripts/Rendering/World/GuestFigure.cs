using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Body proportions per guests.json "accessory" key, so archetypes read apart by silhouette alone at ~16 px tall
    /// (floor height 3 world units ~ 33 px). Heights are ~40-60% of the floor height.
    /// </summary>
    readonly struct GuestSilhouette
    {
        public readonly float Height, Width, Depth, Head, LegFrac;
        /// <summary>Robe down to the floor (no legs visible).</summary>
        public readonly bool Cloak;
        /// <summary>Bare legs (shorts) instead of dark trousers.</summary>
        public readonly bool BareLegs;

        public GuestSilhouette(float height, float width, float depth, float head, float legFrac, bool cloak = false, bool bareLegs = false)
        {
            Height = height; Width = width; Depth = depth; Head = head; LegFrac = legFrac; Cloak = cloak; BareLegs = bareLegs;
        }

        public static GuestSilhouette For(string accessory)
        {
            switch (accessory)
            {
                case "perm_hair": return new GuestSilhouette(1.22f, 0.70f, 0.50f, 0.46f, 0.22f);           // short + round
                case "briefcase": return new GuestSilhouette(1.66f, 0.54f, 0.40f, 0.40f, 0.36f);           // tall, slim suit
                case "camera": return new GuestSilhouette(1.42f, 0.62f, 0.46f, 0.44f, 0.32f, bareLegs: true); // shorts + sun hat
                case "matching_hats": return new GuestSilhouette(1.32f, 0.54f, 0.40f, 0.42f, 0.30f);       // small, hats
                case "visor": return new GuestSilhouette(1.52f, 0.60f, 0.44f, 0.42f, 0.32f);
                case "glasses": return new GuestSilhouette(1.74f, 0.48f, 0.36f, 0.40f, 0.40f);             // tallest thin
                case "hood": return new GuestSilhouette(1.86f, 0.66f, 0.48f, 0.50f, 0.30f, cloak: true);   // looming robe
                case "collar": return new GuestSilhouette(1.60f, 0.52f, 0.40f, 0.42f, 0.30f, cloak: true); // priest: cassock
                case "flat_cap": return new GuestSilhouette(1.28f, 0.62f, 0.46f, 0.44f, 0.30f);            // short, stout nonno
                default:
                {
                    // unknown (new) archetype: stable proportions of its own from the accessory key
                    if (string.IsNullOrEmpty(accessory)) return new GuestSilhouette(1.46f, 0.58f, 0.42f, 0.42f, 0.32f);
                    uint fnv = 2166136261U;
                    foreach (char ch in accessory) fnv = (fnv ^ ch) * 16777619U;
                    int h = (int)(fnv & 0x7fffffff);
                    return new GuestSilhouette(1.30f + 0.05f * (h % 9), 0.50f + 0.03f * ((h / 9) % 6), 0.42f, 0.42f, 0.32f);
                }
            }
        }
    }

    /// <summary>Per-frame pose of a figure's "Visual" child (feet pivot) and arm pivots.</summary>
    struct GuestPose
    {
        public Vector3 Offset;
        public Vector3 Scale;
        /// <summary>Lean, degrees around Z.</summary>
        public float Tilt;
        /// <summary>Arm raise, degrees outward from hanging (0 = down, 180 = straight up).</summary>
        public float ArmL, ArmR;

        public const float JumpSeconds = 0.55f;
        const float TwoPi = Mathf.PI * 2f;

        /// <summary>
        /// Pure pose function (EditMode-tested). <paramref name="time"/> = juice clock (stops while paused),
        /// <paramref name="jumpAge"/> = seconds since an unlucky jump started (&gt;= JumpSeconds = none),
        /// <paramref name="px"/> = one low-res pixel in world units (bobs move whole pixels).
        /// </summary>
        public static GuestPose Evaluate(GuestMood mood, float time, float seed, float jumpAge, float px)
        {
            var p = new GuestPose { Scale = Vector3.one };
            float ph = time + seed * 7.31f;
            float breath = Mathf.Sin(ph * TwoPi * 0.8f);
            p.Scale.y = 1f + 0.03f * breath;
            p.Scale.x = 1f - 0.015f * breath;
            p.Offset.y = breath > 0.35f ? px : 0f;
            p.ArmL = p.ArmR = 5f + 3f * breath;

            switch (mood)
            {
                case GuestMood.Impatient:
                {
                    // foot stomping + swaying from side to side, hands on hips
                    float tap = Mathf.Sin(ph * TwoPi * 3.2f);
                    p.Offset.y = tap > 0f ? px : 0f;
                    p.Offset.x = Mathf.Sin(ph * TwoPi * 1.6f) > 0f ? px : 0f;
                    p.Tilt = Mathf.Sin(ph * TwoPi * 0.8f) > 0f ? 6f : -6f;
                    p.Scale.y = tap > 0f ? 1.03f : 0.96f;
                    p.ArmL = p.ArmR = 32f;
                    break;
                }
                case GuestMood.Angry:
                {
                    // hopping mad: stomps, shakes, fists up
                    float hop = Mathf.Abs(Mathf.Sin(ph * TwoPi * 2.2f));
                    p.Offset.y = Mathf.Round(hop * 3f) * px;
                    p.Offset.x = Mathf.Sin(ph * TwoPi * 9f) > 0f ? px : -px;
                    p.Scale.y = 1f - 0.1f * (1f - hop);
                    p.Scale.x = 1f + 0.06f * (1f - hop);
                    p.ArmL = p.ArmR = 145f + 25f * Mathf.Sin(ph * TwoPi * 4.4f);
                    p.Tilt = 0f;
                    break;
                }
                case GuestMood.Scared:
                {
                    // cowering, trembling, hands up
                    p.Offset.x = Mathf.Sin(ph * TwoPi * 14f) > 0f ? px : -px;
                    p.Offset.y = 0f;
                    p.Scale = new Vector3(1.07f, 0.84f, 1f);
                    p.ArmL = p.ArmR = 168f;
                    p.Tilt = -4f;
                    break;
                }
                case GuestMood.Happy:
                case GuestMood.Delighted:
                {
                    float period = mood == GuestMood.Delighted ? 0.9f : 1.6f;
                    float c = Mathf.Repeat(ph, period);
                    float h = c < 0.32f ? Mathf.Sin(c / 0.32f * Mathf.PI) : 0f;
                    p.Offset.y += h * (mood == GuestMood.Delighted ? 0.38f : 0.22f);
                    p.ArmL = p.ArmR = mood == GuestMood.Delighted ? 120f + 40f * Mathf.Sin(ph * TwoPi * 3f) : 20f + 50f * h;
                    break;
                }
            }

            if (jumpAge >= 0f && jumpAge < JumpSeconds)
            {
                // unlucky number: startled leap (anticipation squash -> stretched rise -> landing squash)
                float u = jumpAge / JumpSeconds;
                float air = u < 0.12f ? 0f : (u - 0.12f) / 0.76f;
                float h = air > 0f && air < 1f ? 4f * air * (1f - air) * 0.85f : 0f;
                p.Offset = new Vector3(p.Offset.x, h, 0f);
                if (u < 0.12f) p.Scale = new Vector3(1.18f, 0.8f, 1f);
                else if (u < 0.88f) p.Scale = new Vector3(0.88f, 1.16f, 1f);
                else p.Scale = new Vector3(1.14f, 0.86f, 1f);
                p.ArmL = p.ArmR = 175f;
                p.Tilt = 0f;
            }
            return p;
        }
    }

    /// <summary>
    /// One guest: Root (follows gameplay) -> Visual (pose, feet pivot) -> either a Kenney character (Model -> Rig with
    /// bones, idle/walk clips sampled on the juice clock, arm raises added on top) or the blocky greybox fallback
    /// (parts + arm pivots). Accessory primitives (hats, bags, glasses, hood...) sit on top of either.
    /// </summary>
    sealed class GuestFigure
    {
        public Transform Root { get; private set; }
        public Transform Visual { get; private set; }
        /// <summary>Screen-left / screen-right arm (pivot or shoulder bone).</summary>
        public Transform ArmL { get; private set; }
        public Transform ArmR { get; private set; }
        public string Accessory { get; private set; }
        public Color BodyColor { get; private set; }
        public GuestSilhouette Silhouette { get; private set; }
        public float HeadTop { get; private set; }

        /// <summary>Kenney model (null = blocky fallback).</summary>
        public KenneyCatalog.GuestModel ModelEntry { get; private set; }
        /// <summary>Yaw holder (faces the camera at 180, turns toward the walking direction).</summary>
        public Transform Model { get; private set; }
        /// <summary>Instantiated character prefab (clips are sampled on it).</summary>
        public GameObject Rig { get; private set; }
        public bool IsModel => Rig != null;
        /// <summary>Accessory unknown to the catalog: a closest-tint stand-in model (rebuild the catalog to give it its own).</summary>
        public bool IsFallbackModel { get; private set; }
        public bool Walking { get; private set; }
        AnimationClip idle, walk;
        Quaternion armRestL, armRestR;
        float yaw = FacingCamera, walkDir;

        public const float FacingCamera = 180f;
        /// <summary>Model width squeeze: chibi heads are wide, queue slots are 0.9 apart.</summary>
        public const float ModelWidthScale = 0.85f;
        /// <summary>Pose arm angles are authored for hanging arms; Kenney idle arms already stand out ~60 degrees.</summary>
        const float ArmGain = 0.65f;
        const float TurnSpeed = 720f;

        public static readonly Color Skin = new Color(0.98f, 0.80f, 0.64f);
        static readonly Color Trousers = new Color(0.14f, 0.13f, 0.17f);
        static readonly Color Eye = new Color(0.06f, 0.05f, 0.06f);

        /// <summary>guests.json colour pushed toward saturated/bright so it survives the dim night lighting (greys stay grey).</summary>
        public static Color Vivid(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.08f) return c;
            s = Mathf.Lerp(s, 1f, 0.3f);
            v = Mathf.Max(v, 0.78f);
            return Color.HSVToRGB(h, s, v);
        }

        public static GuestFigure Build(Transform parent, RenderAssets assets, string name, Color rawColor, string accessory,
            KenneyCatalog catalog = null, int variant = 0)
        {
            var model = catalog != null ? catalog.FindGuest(accessory ?? "", variant) : null;
            bool fallback = false;
            if (model == null && catalog != null && !string.IsNullOrEmpty(accessory))
            {
                model = catalog.FindClosestTint(Vivid(rawColor));
                fallback = model != null;
            }
            if (model != null)
            {
                var built = BuildModel(parent, assets, name, rawColor, accessory ?? "", catalog, model);
                built.IsFallbackModel = fallback && built.IsModel;
                return built;
            }
            var f = new GuestFigure();
            accessory = accessory ?? "";
            f.Accessory = accessory;
            var s = GuestSilhouette.For(accessory);
            f.Silhouette = s;
            var color = Vivid(rawColor);
            f.BodyColor = color;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            f.Root = root;
            f.Visual = new GameObject("Visual").transform;
            f.Visual.SetParent(root, false);
            var v = f.Visual;

            var body = assets.Lit(color);
            var sleeve = assets.Lit(color * 0.82f);
            var skin = assets.Lit(Skin);
            var dark = assets.Lit(Trousers);
            var eye = assets.Unlit(Eye);

            float W = s.Width, D = s.Depth, H = s.Height, head = s.Head;
            float legH = H * s.LegFrac;
            float bodyH = H - legH - head;
            float bodyY0 = legH;
            float headY = legH + bodyH + head * 0.5f;
            f.HeadTop = H;
            float front = -D * 0.5f;

            if (s.Cloak)
            {
                // robe flares out to the floor
                assets.Box(v, "Robe", new Vector3(0f, legH * 0.5f, 0f), new Vector3(W * 1.12f, legH, D * 1.05f), body);
            }
            else
            {
                var legMat = s.BareLegs ? skin : dark;
                float legW = W * 0.36f;
                assets.Box(v, "Leg L", new Vector3(-W * 0.22f, legH * 0.5f, 0f), new Vector3(legW, legH, D * 0.8f), legMat);
                assets.Box(v, "Leg R", new Vector3(W * 0.22f, legH * 0.5f, 0f), new Vector3(legW, legH, D * 0.8f), legMat);
                if (s.BareLegs)
                    assets.Box(v, "Shorts", new Vector3(0f, legH * 0.8f, 0f), new Vector3(W * 0.92f, legH * 0.4f, D * 0.85f), assets.Lit(new Color(0.80f, 0.68f, 0.42f)));
            }
            assets.Box(v, "Body", new Vector3(0f, bodyY0 + bodyH * 0.5f, 0f), new Vector3(W, bodyH, D), body);

            bool hooded = accessory == "hood";
            assets.Box(v, "Head", new Vector3(0f, headY, 0f), new Vector3(head, head, head * 0.92f), hooded ? body : skin);
            if (!hooded)
            {
                assets.Box(v, "Eye L", new Vector3(-head * 0.2f, headY + head * 0.06f, -head * 0.47f), new Vector3(0.07f, 0.09f, 0.02f), eye, false);
                assets.Box(v, "Eye R", new Vector3(head * 0.2f, headY + head * 0.06f, -head * 0.47f), new Vector3(0.07f, 0.09f, 0.02f), eye, false);
            }

            // arms on shoulder pivots so poses can raise them
            float armW = Mathf.Clamp(W * 0.22f, 0.11f, 0.15f);
            float armLen = bodyH * 0.92f;
            float shoulderY = bodyY0 + bodyH - 0.04f;
            f.ArmL = Pivot(v, "Arm L", new Vector3(-W * 0.5f - armW * 0.5f, shoulderY, 0f));
            f.ArmR = Pivot(v, "Arm R", new Vector3(W * 0.5f + armW * 0.5f, shoulderY, 0f));
            foreach (var arm in new[] { f.ArmL, f.ArmR })
            {
                assets.Box(arm, "Sleeve", new Vector3(0f, -armLen * 0.5f, 0f), new Vector3(armW, armLen, armW), hooded ? body : sleeve);
                if (!hooded) assets.Box(arm, "Hand", new Vector3(0f, -armLen - 0.05f, 0f), new Vector3(armW, 0.1f, armW), skin, false);
            }

            float top = headY + head * 0.5f;
            switch (accessory)
            {
                case "perm_hair":
                {
                    var hair = assets.Lit(new Color(0.93f, 0.90f, 1.00f));
                    assets.Box(v, "Perm", new Vector3(0f, top + 0.06f, 0.02f), new Vector3(head * 1.5f, head * 0.55f, head * 1.25f), hair);
                    assets.Box(v, "Perm Side L", new Vector3(-head * 0.62f, headY, 0.04f), new Vector3(head * 0.32f, head * 0.6f, head * 1.0f), hair);
                    assets.Box(v, "Perm Side R", new Vector3(head * 0.62f, headY, 0.04f), new Vector3(head * 0.32f, head * 0.6f, head * 1.0f), hair);
                    // handbag hangs from the left hand
                    assets.Box(f.ArmL, "Handbag", new Vector3(-0.04f, -armLen - 0.16f, -0.02f), new Vector3(0.28f, 0.24f, 0.12f), assets.Lit(new Color(0.62f, 0.16f, 0.34f)));
                    break;
                }
                case "briefcase":
                {
                    assets.Box(v, "Hair", new Vector3(0f, top - 0.04f, 0.02f), new Vector3(head * 1.06f, 0.14f, head * 1.0f), assets.Lit(new Color(0.06f, 0.05f, 0.05f)));
                    assets.Box(v, "Tie", new Vector3(0f, bodyY0 + bodyH * 0.62f, front - 0.02f), new Vector3(0.09f, bodyH * 0.6f, 0.03f), assets.Lit(new Color(0.98f, 0.82f, 0.20f)), false);
                    assets.Box(f.ArmR, "Briefcase", new Vector3(0.10f, -armLen - 0.2f, 0f), new Vector3(0.46f, 0.32f, 0.14f), assets.Lit(new Color(0.24f, 0.14f, 0.08f)));
                    assets.Box(f.ArmR, "Clasp", new Vector3(0.10f, -armLen - 0.12f, -0.08f), new Vector3(0.1f, 0.06f, 0.02f), assets.Unlit(new Color(1f, 0.85f, 0.35f)), false);
                    break;
                }
                case "camera":
                {
                    var straw = assets.Lit(new Color(0.95f, 0.86f, 0.52f));
                    assets.Box(v, "Hat Brim", new Vector3(0f, top + 0.02f, 0f), new Vector3(head * 1.95f, 0.06f, head * 1.7f), straw);
                    assets.Box(v, "Hat Crown", new Vector3(0f, top + 0.12f, 0f), new Vector3(head * 1.0f, 0.18f, head * 0.95f), straw);
                    assets.Box(v, "Hat Band", new Vector3(0f, top + 0.07f, -head * 0.48f), new Vector3(head * 1.02f, 0.06f, 0.02f), assets.Lit(new Color(0.85f, 0.2f, 0.2f)), false);
                    assets.Box(v, "Camera", new Vector3(0f, bodyY0 + bodyH * 0.62f, front - 0.07f), new Vector3(0.32f, 0.22f, 0.12f), assets.Lit(new Color(0.10f, 0.10f, 0.12f)));
                    assets.Box(v, "Lens", new Vector3(0f, bodyY0 + bodyH * 0.62f, front - 0.14f), new Vector3(0.12f, 0.12f, 0.04f), assets.Unlit(new Color(0.55f, 0.75f, 1f)), false);
                    break;
                }
                case "matching_hats":
                {
                    var white = assets.Lit(new Color(0.98f, 0.98f, 1f));
                    assets.Box(v, "Hat Brim", new Vector3(0f, top + 0.01f, 0f), new Vector3(head * 1.45f, 0.06f, head * 1.4f), white);
                    assets.Box(v, "Hat Crown", new Vector3(0f, top + 0.14f, 0f), new Vector3(head * 1.05f, 0.24f, head * 1.0f), white);
                    assets.Box(v, "Hat Band", new Vector3(0f, top + 0.08f, -head * 0.52f), new Vector3(head * 1.07f, 0.07f, 0.02f), assets.Lit(new Color(1f, 0.35f, 0.55f)), false);
                    break;
                }
                case "visor":
                {
                    var visor = assets.Unlit(new Color(0.30f, 0.95f, 0.45f));
                    assets.Box(v, "Visor Band", new Vector3(0f, top - 0.08f, 0f), new Vector3(head * 1.08f, 0.09f, head * 0.98f), visor);
                    assets.Box(v, "Visor Brim", new Vector3(0f, top - 0.1f, -head * 0.62f), new Vector3(head * 1.25f, 0.05f, head * 0.45f), visor);
                    assets.Box(v, "Chain", new Vector3(0f, bodyY0 + bodyH * 0.7f, front - 0.02f), new Vector3(W * 0.5f, 0.06f, 0.03f), assets.Unlit(new Color(1f, 0.82f, 0.25f)), false);
                    assets.Box(v, "Medal", new Vector3(0f, bodyY0 + bodyH * 0.55f, front - 0.02f), new Vector3(0.12f, 0.12f, 0.03f), assets.Unlit(new Color(1f, 0.82f, 0.25f)), false);
                    break;
                }
                case "glasses":
                {
                    assets.Box(v, "Hair", new Vector3(-0.03f, top - 0.03f, 0.02f), new Vector3(head * 1.06f, 0.12f, head * 1.0f), assets.Lit(new Color(0.45f, 0.44f, 0.46f)));
                    assets.Box(v, "Glasses", new Vector3(0f, headY + head * 0.06f, -head * 0.5f), new Vector3(head * 1.08f, 0.05f, 0.02f), assets.Lit(new Color(0.05f, 0.05f, 0.06f)), false);
                    var lens = assets.Unlit(new Color(0.85f, 0.95f, 1f));
                    assets.Box(v, "Lens L", new Vector3(-head * 0.22f, headY + head * 0.04f, -head * 0.52f), new Vector3(0.12f, 0.11f, 0.02f), lens, false);
                    assets.Box(v, "Lens R", new Vector3(head * 0.22f, headY + head * 0.04f, -head * 0.52f), new Vector3(0.12f, 0.11f, 0.02f), lens, false);
                    assets.Box(v, "Tie", new Vector3(0f, bodyY0 + bodyH * 0.62f, front - 0.02f), new Vector3(0.08f, bodyH * 0.55f, 0.03f), assets.Lit(new Color(0.15f, 0.25f, 0.55f)), false);
                    assets.Box(f.ArmL, "Clipboard", new Vector3(-0.04f, -armLen * 0.75f, -0.12f), new Vector3(0.3f, 0.38f, 0.04f), assets.Lit(new Color(0.96f, 0.93f, 0.80f)));
                    break;
                }
                case "hood":
                {
                    assets.Box(v, "Hood", new Vector3(0f, headY + 0.04f, 0.02f), new Vector3(head * 1.18f, head * 1.2f, head * 1.1f), body);
                    assets.Box(v, "Hood Tip", new Vector3(0f, top + 0.1f, 0.06f), new Vector3(head * 0.5f, 0.2f, head * 0.5f), body);
                    assets.Box(v, "Face", new Vector3(0f, headY - 0.02f, -head * 0.57f), new Vector3(head * 0.72f, head * 0.6f, 0.02f), assets.Unlit(new Color(0.01f, 0.01f, 0.02f)), false);
                    var glow = assets.Unlit(new Color(0.55f, 1f, 0.95f));
                    assets.Box(v, "Eye L", new Vector3(-head * 0.16f, headY, -head * 0.6f), new Vector3(0.09f, 0.07f, 0.02f), glow, false);
                    assets.Box(v, "Eye R", new Vector3(head * 0.16f, headY, -head * 0.6f), new Vector3(0.09f, 0.07f, 0.02f), glow, false);
                    f.HeadTop = top + 0.2f;
                    break;
                }
                case "collar":
                {
                    assets.Box(v, "Clerical Collar", new Vector3(0f, bodyY0 + bodyH - 0.06f, front - 0.02f), new Vector3(W * 0.5f, 0.1f, 0.03f), assets.Unlit(new Color(0.96f, 0.96f, 0.94f)), false);
                    assets.Box(v, "Cross", new Vector3(0f, bodyY0 + bodyH * 0.55f, front - 0.02f), new Vector3(0.06f, 0.2f, 0.02f), assets.Unlit(new Color(1f, 0.82f, 0.25f)), false);
                    break;
                }
                case "flat_cap":
                {
                    var cap = assets.Lit(new Color(0.42f, 0.34f, 0.26f));
                    assets.Box(v, "Flat Cap", new Vector3(0f, top + 0.03f, 0.02f), new Vector3(head * 1.1f, 0.12f, head * 1.0f), cap);
                    assets.Box(v, "Cap Peak", new Vector3(0f, top - 0.01f, -head * 0.6f), new Vector3(head * 0.9f, 0.05f, head * 0.35f), cap);
                    assets.Box(v, "Moustache", new Vector3(0f, headY - head * 0.18f, -head * 0.48f), new Vector3(head * 0.6f, 0.08f, 0.02f), assets.Lit(new Color(0.94f, 0.94f, 0.92f)), false);
                    f.HeadTop = top + 0.09f;
                    break;
                }
            }
            return f;
        }

        /// <summary>Visual-space height of the model itself (accessories add on top); per archetype from the silhouette table.</summary>
        public static float ModelHeight(in GuestSilhouette s) => s.Height * 0.9f;

        static GuestFigure BuildModel(Transform parent, RenderAssets assets, string name, Color rawColor, string accessory,
            KenneyCatalog catalog, KenneyCatalog.GuestModel entry)
        {
            var f = new GuestFigure { Accessory = accessory, ModelEntry = entry, idle = catalog.Idle, walk = catalog.Walk };
            var s = GuestSilhouette.For(accessory);
            f.Silhouette = s;
            f.BodyColor = entry.Tint;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            f.Root = root;
            f.Visual = new GameObject("Visual").transform;
            f.Visual.SetParent(root, false);
            var v = f.Visual;

            // Model (yaw + scale, feet at the Visual origin) -> Rig (prefab, sampled by the clips)
            float k = ModelHeight(s) / Mathf.Max(1f, entry.Bounds.max.y);
            f.Model = new GameObject("Model").transform;
            f.Model.SetParent(v, false);
            f.Model.localRotation = Quaternion.Euler(0f, FacingCamera, 0f);
            f.Model.localScale = new Vector3(k * ModelWidthScale, k, k);
            f.Rig = Object.Instantiate(entry.Prefab, f.Model, false);
            f.Rig.name = "Rig " + entry.Character;
            foreach (var smr in f.Rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.sharedMaterial = catalog.Material;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var head = FindDeep(f.Rig.transform, "head");
            var torso = FindDeep(f.Rig.transform, "torso");
            // facing the camera, the model's own left arm is on screen-right
            f.ArmL = FindDeep(f.Rig.transform, "arm-right");
            f.ArmR = FindDeep(f.Rig.transform, "arm-left");
            if (head == null || torso == null || f.ArmL == null || f.ArmR == null)
            {
                Object.Destroy(root.gameObject);
                return Build(parent, assets, name, rawColor, accessory);
            }
            f.armRestL = f.ArmL.localRotation;
            f.armRestR = f.ArmR.localRotation;

            // geometry in Visual space (model faces +Z in model space -> -Z here), measured in the idle pose
            if (f.idle != null) f.idle.SampleAnimation(f.Rig, 0f);
            Vector3 ToVisual(Vector3 modelPoint) => v.InverseTransformPoint(f.Rig.transform.TransformPoint(modelPoint));
            var hb = entry.Head;
            float hw = hb.extents.x * k * ModelWidthScale;          // head half width
            float hh = hb.size.y * k;                               // head height
            float hd = hb.size.z * k;                               // head depth
            float hTop = ToVisual(new Vector3(0f, hb.max.y, hb.center.z)).y;
            float hCy = ToVisual(hb.center).y;
            float hFront = ToVisual(new Vector3(0f, hb.center.y, hb.max.z)).z;
            float hCz = ToVisual(hb.center).z;
            var shoulderL = v.InverseTransformPoint(f.ArmL.position);
            var shoulderR = v.InverseTransformPoint(f.ArmR.position);
            float chestY = Mathf.Lerp(v.InverseTransformPoint(torso.position).y, shoulderL.y, 0.55f);
            float chestZ = hFront * 0.55f;
            float bodyW = Mathf.Abs(shoulderR.x - shoulderL.x);
            f.HeadTop = hTop;
            var handL = new Vector3(shoulderL.x - 0.08f, shoulderL.y - bodyW * 0.55f, 0f);
            var handR = new Vector3(shoulderR.x + 0.08f, shoulderR.y - bodyW * 0.55f, 0f);

            GameObject Part(Transform bone, string n, Vector3 c, Vector3 size, Material m)
            {
                var go = assets.Box(v, n, c, size, m, false);
                go.transform.SetParent(bone, true);
                return go;
            }

            switch (accessory)
            {
                case "perm_hair":
                {
                    var hair = assets.Lit(new Color(0.93f, 0.90f, 1.00f));
                    Part(head, "Perm", new Vector3(0f, hTop + 0.02f, hCz + 0.02f), new Vector3(hw * 2.1f, 0.2f, hd * 0.95f), hair);
                    Part(head, "Perm Side L", new Vector3(-hw * 1.02f, hCy + 0.04f, hCz + 0.04f), new Vector3(0.14f, hh * 0.55f, hd * 0.7f), hair);
                    Part(head, "Perm Side R", new Vector3(hw * 1.02f, hCy + 0.04f, hCz + 0.04f), new Vector3(0.14f, hh * 0.55f, hd * 0.7f), hair);
                    Part(f.ArmL, "Handbag", handL + new Vector3(-0.04f, -0.08f, -0.04f), new Vector3(0.26f, 0.22f, 0.12f), assets.Lit(new Color(0.62f, 0.16f, 0.34f)));
                    f.HeadTop = hTop + 0.12f;
                    break;
                }
                case "briefcase":
                {
                    Part(torso, "Tie", new Vector3(0f, chestY, chestZ - 0.02f), new Vector3(0.08f, 0.24f, 0.03f), assets.Lit(new Color(0.98f, 0.82f, 0.20f)));
                    Part(f.ArmR, "Briefcase", handR + new Vector3(0.06f, -0.14f, 0f), new Vector3(0.42f, 0.3f, 0.13f), assets.Lit(new Color(0.24f, 0.14f, 0.08f)));
                    Part(f.ArmR, "Clasp", handR + new Vector3(0.06f, -0.04f, -0.08f), new Vector3(0.1f, 0.06f, 0.02f), assets.Unlit(new Color(1f, 0.85f, 0.35f)));
                    break;
                }
                case "camera":
                {
                    var straw = assets.Lit(new Color(0.95f, 0.86f, 0.52f));
                    Part(head, "Hat Brim", new Vector3(0f, hTop - 0.04f, hCz), new Vector3(hw * 2.7f, 0.06f, hd * 1.15f), straw);
                    Part(head, "Hat Crown", new Vector3(0f, hTop + 0.06f, hCz), new Vector3(hw * 1.7f, 0.16f, hd * 0.75f), straw);
                    Part(head, "Hat Band", new Vector3(0f, hTop + 0.01f, hCz - hd * 0.38f), new Vector3(hw * 1.72f, 0.05f, 0.02f), assets.Lit(new Color(0.85f, 0.2f, 0.2f)));
                    Part(torso, "Camera", new Vector3(0f, chestY, chestZ - 0.06f), new Vector3(0.3f, 0.2f, 0.12f), assets.Lit(new Color(0.10f, 0.10f, 0.12f)));
                    Part(torso, "Lens", new Vector3(0f, chestY, chestZ - 0.13f), new Vector3(0.11f, 0.11f, 0.04f), assets.Unlit(new Color(0.55f, 0.75f, 1f)));
                    f.HeadTop = hTop + 0.14f;
                    break;
                }
                case "matching_hats":
                {
                    var white = assets.Lit(new Color(0.98f, 0.98f, 1f));
                    Part(head, "Hat Brim", new Vector3(0f, hTop - 0.03f, hCz), new Vector3(hw * 2.25f, 0.06f, hd * 1.05f), white);
                    Part(head, "Hat Crown", new Vector3(0f, hTop + 0.09f, hCz), new Vector3(hw * 1.6f, 0.2f, hd * 0.75f), white);
                    Part(head, "Hat Band", new Vector3(0f, hTop + 0.03f, hCz - hd * 0.38f), new Vector3(hw * 1.62f, 0.07f, 0.02f), assets.Lit(new Color(1f, 0.35f, 0.55f)));
                    f.HeadTop = hTop + 0.19f;
                    break;
                }
                case "visor":
                {
                    var visor = assets.Unlit(new Color(0.30f, 0.95f, 0.45f));
                    float vy = hTop - hh * 0.22f;
                    Part(head, "Visor Band", new Vector3(0f, vy, hCz), new Vector3(hw * 2.08f, 0.08f, hd * 1.02f), visor);
                    Part(head, "Visor Brim", new Vector3(0f, vy - 0.02f, hFront - 0.1f), new Vector3(hw * 1.7f, 0.05f, 0.26f), visor);
                    Part(torso, "Chain", new Vector3(0f, chestY + 0.06f, chestZ - 0.02f), new Vector3(bodyW * 0.45f, 0.05f, 0.03f), assets.Unlit(new Color(1f, 0.82f, 0.25f)));
                    Part(torso, "Medal", new Vector3(0f, chestY - 0.06f, chestZ - 0.02f), new Vector3(0.12f, 0.12f, 0.03f), assets.Unlit(new Color(1f, 0.82f, 0.25f)));
                    break;
                }
                case "glasses":
                {
                    float ey = hCy - hh * 0.06f;
                    Part(head, "Glasses", new Vector3(0f, ey, hFront - 0.02f), new Vector3(hw * 1.5f, 0.05f, 0.02f), assets.Lit(new Color(0.05f, 0.05f, 0.06f)));
                    var lens = assets.Unlit(new Color(0.85f, 0.95f, 1f));
                    Part(head, "Lens L", new Vector3(-hw * 0.42f, ey - 0.01f, hFront - 0.03f), new Vector3(0.13f, 0.11f, 0.02f), lens);
                    Part(head, "Lens R", new Vector3(hw * 0.42f, ey - 0.01f, hFront - 0.03f), new Vector3(0.13f, 0.11f, 0.02f), lens);
                    Part(torso, "Tie", new Vector3(0f, chestY, chestZ - 0.02f), new Vector3(0.07f, 0.22f, 0.03f), assets.Lit(new Color(0.15f, 0.25f, 0.55f)));
                    Part(f.ArmL, "Clipboard", handL + new Vector3(0.02f, 0.06f, -0.12f), new Vector3(0.28f, 0.34f, 0.04f), assets.Lit(new Color(0.96f, 0.93f, 0.80f)));
                    break;
                }
                case "hood":
                {
                    var robe = assets.Lit(new Color(0.11f, 0.10f, 0.13f));
                    Part(head, "Hood", new Vector3(0f, hCy + 0.03f, hCz + 0.02f), new Vector3(hw * 2.24f, hh * 1.12f, hd * 1.06f), robe);
                    Part(head, "Hood Tip", new Vector3(0f, hTop + 0.1f, hCz + 0.08f), new Vector3(hw * 0.9f, 0.2f, hd * 0.5f), robe);
                    Part(head, "Face", new Vector3(0f, hCy - 0.03f, hFront - 0.03f), new Vector3(hw * 1.4f, hh * 0.62f, 0.02f), assets.Unlit(new Color(0.01f, 0.01f, 0.02f)));
                    var glow = assets.Unlit(new Color(0.55f, 1f, 0.95f));
                    Part(head, "Eye L", new Vector3(-hw * 0.32f, hCy, hFront - 0.05f), new Vector3(0.1f, 0.07f, 0.02f), glow);
                    Part(head, "Eye R", new Vector3(hw * 0.32f, hCy, hFront - 0.05f), new Vector3(0.1f, 0.07f, 0.02f), glow);
                    f.HeadTop = hTop + 0.22f;
                    break;
                }
                case "collar":
                {
                    // priest: dark cassock (atlas tile), white clerical collar at the neck, small gold cross
                    float neckY = Mathf.Max(shoulderL.y + 0.02f, chestY + 0.14f);
                    var white = assets.Unlit(new Color(0.96f, 0.96f, 0.94f));
                    Part(torso, "Clerical Collar", new Vector3(0f, neckY, chestZ - 0.03f), new Vector3(bodyW * 0.5f, 0.11f, 0.03f), white);
                    Part(torso, "Collar Tab", new Vector3(0f, neckY - 0.07f, chestZ - 0.05f), new Vector3(0.1f, 0.1f, 0.02f), white);
                    var gold = assets.Unlit(new Color(1f, 0.82f, 0.25f));
                    Part(torso, "Cross V", new Vector3(0f, chestY - 0.12f, chestZ - 0.06f), new Vector3(0.06f, 0.22f, 0.02f), gold);
                    Part(torso, "Cross H", new Vector3(0f, chestY - 0.07f, chestZ - 0.06f), new Vector3(0.16f, 0.06f, 0.02f), gold);
                    break;
                }
                case "flat_cap":
                {
                    // nonno: tweed flat cap with a peak, white moustache, walking cane
                    var cap = assets.Lit(new Color(0.42f, 0.34f, 0.26f));
                    Part(head, "Flat Cap", new Vector3(0f, hTop - 0.01f, hCz + 0.02f), new Vector3(hw * 2.12f, 0.13f, hd * 0.98f), cap);
                    Part(head, "Cap Peak", new Vector3(0f, hTop - 0.05f, hFront - 0.07f), new Vector3(hw * 1.6f, 0.06f, 0.22f), cap);
                    Part(head, "Moustache", new Vector3(0f, hCy - hh * 0.2f, hFront - 0.03f), new Vector3(hw * 1.05f, 0.09f, 0.03f), assets.Lit(new Color(0.94f, 0.94f, 0.92f)));
                    float caneTop = Mathf.Max(0.2f, handR.y);
                    Part(f.ArmR, "Cane", new Vector3(handR.x + 0.06f, caneTop * 0.5f, -0.06f), new Vector3(0.09f, caneTop, 0.09f), assets.Lit(new Color(0.32f, 0.19f, 0.10f)));
                    f.HeadTop = hTop + 0.1f;
                    break;
                }
                default:
                {
                    // new archetypes before the catalog knows them: dress by keywords in the accessory key
                    string key = accessory.ToLowerInvariant();
                    if (key.Contains("priest") || key.Contains("collar") || key.Contains("cross") || key.Contains("cassock") || key.Contains("devout"))
                    {
                        var black = assets.Lit(new Color(0.08f, 0.08f, 0.10f));
                        Part(torso, "Cassock", new Vector3(0f, chestY - 0.05f, chestZ - 0.03f), new Vector3(bodyW * 0.8f, 0.36f, 0.03f), black);
                        Part(torso, "Collar", new Vector3(0f, chestY + 0.14f, chestZ - 0.05f), new Vector3(0.12f, 0.05f, 0.02f), assets.Unlit(new Color(0.95f, 0.95f, 0.92f)));
                        var gold = assets.Unlit(new Color(1f, 0.82f, 0.25f));
                        Part(torso, "Cross V", new Vector3(0f, chestY - 0.06f, chestZ - 0.06f), new Vector3(0.04f, 0.16f, 0.02f), gold);
                        Part(torso, "Cross H", new Vector3(0f, chestY - 0.02f, chestZ - 0.06f), new Vector3(0.11f, 0.04f, 0.02f), gold);
                    }
                    else if (key.Contains("nonno") || key.Contains("cap") || key.Contains("beret") || key.Contains("mustache") || key.Contains("moustache") || key.Contains("cane"))
                    {
                        var cap = assets.Lit(new Color(0.38f, 0.30f, 0.22f));
                        Part(head, "Flat Cap", new Vector3(0f, hTop + 0.02f, hCz), new Vector3(hw * 2.1f, 0.12f, hd * 0.95f), cap);
                        Part(head, "Cap Peak", new Vector3(0f, hTop - 0.02f, hFront - 0.06f), new Vector3(hw * 1.6f, 0.05f, 0.18f), cap);
                        Part(head, "Mustache", new Vector3(0f, hCy - hh * 0.22f, hFront - 0.02f), new Vector3(hw * 0.9f, 0.07f, 0.03f), assets.Lit(new Color(0.92f, 0.92f, 0.90f)));
                        f.HeadTop = hTop + 0.1f;
                    }
                    else if (key.Contains("hat"))
                    {
                        var hat = assets.Lit(Vivid(rawColor) * 0.6f);
                        Part(head, "Hat Brim", new Vector3(0f, hTop - 0.03f, hCz), new Vector3(hw * 2.4f, 0.06f, hd * 1.1f), hat);
                        Part(head, "Hat Crown", new Vector3(0f, hTop + 0.09f, hCz), new Vector3(hw * 1.6f, 0.2f, hd * 0.75f), hat);
                        f.HeadTop = hTop + 0.19f;
                    }
                    break;
                }
            }
            return f;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Transform Pivot(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        /// <summary>
        /// Pose on the juice clock. Models: idle/walk clip sampled at <paramref name="animTime"/> (frozen while paused),
        /// then the pose's arm raises are added on the shoulder bones (rotation about the view axis).
        /// </summary>
        public void Apply(in GuestPose p, float animTime = 0f)
        {
            Visual.localPosition = p.Offset;
            Visual.localScale = p.Scale;
            Visual.localRotation = Quaternion.Euler(0f, 0f, p.Tilt);
            if (!IsModel)
            {
                ArmL.localRotation = Quaternion.Euler(0f, 0f, -p.ArmL);
                ArmR.localRotation = Quaternion.Euler(0f, 0f, p.ArmR);
                return;
            }
            Model.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ArmL.localRotation = armRestL;
            ArmR.localRotation = armRestR;
            var clip = Walking && walk != null ? walk : idle;
            if (clip != null) clip.SampleAnimation(Rig, Mathf.Repeat(animTime, Mathf.Max(0.01f, clip.length)));
            var axis = Visual.TransformDirection(Vector3.forward);
            ArmL.rotation = Quaternion.AngleAxis(-p.ArmL * ArmGain, axis) * ArmL.rotation;
            ArmR.rotation = Quaternion.AngleAxis(p.ArmR * ArmGain, axis) * ArmR.rotation;
        }

        /// <summary>
        /// Locomotion from the proxy's horizontal speed (world units/s, + = right): walk clip + turn 3/4 toward the
        /// walking direction; idle faces the camera. <paramref name="dt"/> on the juice clock.
        /// </summary>
        public void SetMotion(float speedX, float dt)
        {
            if (!IsModel) return;
            bool walking = Mathf.Abs(speedX) > 0.35f;
            if (walking) walkDir = Mathf.Sign(speedX);
            Walking = walking;
            float target = walking ? FacingCamera - 45f * walkDir : FacingCamera;
            yaw = Mathf.MoveTowards(yaw, target, TurnSpeed * dt);
        }

        public float Yaw => yaw;

        /// <summary>World-space AABB of every renderer of the figure.</summary>
        public Bounds WorldBounds()
        {
            var rs = Root.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Root.position, Vector3.zero);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
