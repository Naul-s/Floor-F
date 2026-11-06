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
                default: return new GuestSilhouette(1.46f, 0.58f, 0.42f, 0.42f, 0.32f);
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

    /// <summary>One blocky guest: Root (follows gameplay) -> Visual (pose, feet pivot) -> parts + arm pivots.</summary>
    sealed class GuestFigure
    {
        public Transform Root { get; private set; }
        public Transform Visual { get; private set; }
        public Transform ArmL { get; private set; }
        public Transform ArmR { get; private set; }
        public string Accessory { get; private set; }
        public Color BodyColor { get; private set; }
        public GuestSilhouette Silhouette { get; private set; }
        public float HeadTop { get; private set; }

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

        public static GuestFigure Build(Transform parent, RenderAssets assets, string name, Color rawColor, string accessory)
        {
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
            }
            return f;
        }

        static Transform Pivot(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        public void Apply(in GuestPose p)
        {
            Visual.localPosition = p.Offset;
            Visual.localScale = p.Scale;
            Visual.localRotation = Quaternion.Euler(0f, 0f, p.Tilt);
            ArmL.localRotation = Quaternion.Euler(0f, 0f, -p.ArmL);
            ArmR.localRotation = Quaternion.Euler(0f, 0f, p.ArmR);
        }

        /// <summary>World-space AABB of every renderer of the figure.</summary>
        public Bounds WorldBounds()
        {
            var rs = Root.GetComponentsInChildren<MeshRenderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Root.position, Vector3.zero);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
