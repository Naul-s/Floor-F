using System.Collections.Generic;
using FloorF.Core;
using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Lottery night (ISeasonQuery.IsLotteryNight, day 9):
    ///   Drum      a lottery machine in the lobby's back corner: wooden stand, a gold octagonal drum with coloured balls,
    ///             a chute and a tray. <see cref="Draw"/> (LotteryDrawn) spins it up, a gold ball drops down the chute
    ///             into the tray and the drum slows down (<see cref="WinterSchedule.DrawSeconds"/>).
    ///   Highlight every floor whose sign currently shows <see cref="ISeasonQuery.LotteryNumber"/> gets a soft gold frame
    ///             around its sign and a gold bar under its door lintel, breathing slowly
    ///             (<see cref="WinterSchedule.HighlightLevel"/>: one 2.2 s fade, never a flash). Stickers move it along.
    /// Coins on LotteryWon are bursts of the shared JuiceFx (RenderingRoot).
    /// </summary>
    sealed class LotteryView
    {
        public static readonly Color Gold = new Color(1.00f, 0.80f, 0.28f);
        static readonly Color[] BallColors =
        {
            new Color(1f, 0.3f, 0.3f), new Color(0.35f, 0.7f, 1f), new Color(1f, 0.85f, 0.3f), new Color(0.4f, 0.95f, 0.5f),
            new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.5f, 0.85f), new Color(1f, 0.6f, 0.2f),
        };

        sealed class Highlight
        {
            public GameObject Root;
            public readonly Transform[] Bars = new Transform[5];
            public Bounds Plate;
        }

        readonly RenderAssets assets;
        readonly IWorldLayout layout;
        readonly BuildingView building;
        readonly FloorIndex lobby;
        readonly Highlight[] highlights;
        Material glow;
        Transform drum, balls, drop;
        Vector3 dropFrom, dropTo;
        float drawAge = float.MaxValue;

        public Transform Root { get; }
        public bool Built { get; private set; }
        public bool Shown { get; private set; }
        /// <summary>Tests: force the lottery dressing (null = follow ISeasonQuery.IsLotteryNight).</summary>
        public bool? Force { get; set; }
        /// <summary>Tests: force the winning number (null = ISeasonQuery.LotteryNumber).</summary>
        public int? ForceNumber { get; set; }
        public int Number { get; private set; }
        public int Draws { get; private set; }
        public bool Drawing => drawAge < WinterSchedule.DrawSeconds;
        public float DrumAngle => drum != null ? drum.localEulerAngles.z : 0f;
        public bool BallInTray => drop != null && drop.gameObject.activeSelf && !Drawing;
        public Vector3 DrumCentre => drum != null ? drum.position : Vector3.zero;
        public Color HighlightColor => glow != null ? glow.GetColor("_BaseColor") : Color.black;

        public LotteryView(Transform parent, RenderAssets assets, IWorldLayout layout, BuildingView building, FloorIndex lobby)
        {
            this.assets = assets;
            this.layout = layout;
            this.building = building;
            this.lobby = lobby;
            highlights = new Highlight[layout.FloorCount];
            Root = new GameObject("Lottery").transform;
            Root.SetParent(parent, false);
            Root.gameObject.SetActive(false);
        }

        public bool IsHighlighted(FloorIndex f) => f.Value >= 0 && f.Value < highlights.Length && highlights[f.Value] != null && highlights[f.Value].Root.activeSelf;

        /// <summary>LotteryDrawn: spin the drum, drop a ball.</summary>
        public void Draw()
        {
            drawAge = 0f;
            Draws++;
            drawsTonight++;
        }

        /// <summary>Draws since the lottery dressing last appeared (the tray starts empty every lottery night).</summary>
        int drawsTonight;

        public void Tick(float dt, float time, ISeasonQuery season)
        {
            bool show = Force ?? (season != null && season.IsLotteryNight);
            if (show && !Built) Build();
            if (Built && Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            Shown = show && Built;
            Number = show ? ForceNumber ?? (season != null ? season.LotteryNumber : 0) : 0;
            if (!Shown)
            {
                drawAge = float.MaxValue;
                // lottery night over (next day, new run): no highlight survives into the next lottery night
                foreach (var h in highlights)
                    if (h != null && h.Root.activeSelf) h.Root.SetActive(false);
                if (drop != null && drop.gameObject.activeSelf) drop.gameObject.SetActive(false);
                drawsTonight = 0;
                return;
            }
            Animate(dt);
            UpdateHighlights(time);
        }

        /// <summary>Drum centre above the lobby floor: high enough to show over the lobby sofa.</summary>
        const float DrumY = 1.62f;

        void Build()
        {
            Built = true;
            glow = assets.NewUnlit(Gold);
            float y0 = lobby.Value * layout.FloorHeight;
            float cx = building.PropZoneMinX + 0.72f, cz = 1.15f;
            var wood = assets.Lit(new Color(0.34f, 0.21f, 0.12f));
            var gold = assets.Lit(new Color(0.86f, 0.66f, 0.24f));
            var navy = assets.Lit(new Color(0.12f, 0.14f, 0.30f));
            // stand: two legs, a top rail, a back plate the balls read against
            assets.Box(Root, "Stand Leg L", new Vector3(cx - 0.42f, y0 + 0.6f, cz), new Vector3(0.1f, 1.2f, 0.4f), wood);
            assets.Box(Root, "Stand Leg R", new Vector3(cx + 0.42f, y0 + 0.6f, cz), new Vector3(0.1f, 1.2f, 0.4f), wood);
            assets.Box(Root, "Stand Base", new Vector3(cx, y0 + 0.06f, cz), new Vector3(1.05f, 0.12f, 0.6f), wood);
            assets.Box(Root, "Drum Back", new Vector3(cx, y0 + DrumY, cz + 0.3f), new Vector3(0.86f, 0.86f, 0.06f), navy, false);
            // drum: octagonal gold rim + spokes, balls tumbling inside (counter-rotating group)
            drum = new GameObject("Drum").transform;
            drum.SetParent(Root, false);
            drum.localPosition = new Vector3(cx, y0 + DrumY, cz);
            const float r = 0.44f;
            float side = 2f * r * Mathf.Tan(Mathf.PI / 8f);
            for (int i = 0; i < 8; i++)
            {
                var piece = assets.Box(drum, "Rim " + i, Vector3.zero, new Vector3(side + 0.06f, 0.07f, 0.5f), gold, false).transform;
                float a = i * 45f;
                piece.localRotation = Quaternion.Euler(0f, 0f, a);
                piece.localPosition = Quaternion.Euler(0f, 0f, a) * new Vector3(0f, -r, 0f);
            }
            for (int i = 0; i < 2; i++)
            {
                var spoke = assets.Box(drum, "Spoke " + i, new Vector3(0f, 0f, -0.2f), new Vector3(r * 2f, 0.04f, 0.04f), gold, false).transform;
                spoke.localRotation = Quaternion.Euler(0f, 0f, 45f + i * 90f);
            }
            assets.Box(drum, "Hub", new Vector3(0f, 0f, -0.24f), new Vector3(0.1f, 0.1f, 0.06f), gold, false);
            balls = new GameObject("Balls").transform;
            balls.SetParent(drum, false);
            for (int i = 0; i < BallColors.Length; i++)
            {
                float a = i * 51f * Mathf.Deg2Rad;
                float rr = 0.12f + 0.16f * ((i * 7) % 3) / 2f;
                assets.Box(balls, "Ball " + i, new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr - 0.08f, -0.05f), Vector3.one * 0.12f, assets.Unlit(BallColors[i] * 0.68f), false);
            }
            // axle mounts, chute and tray
            assets.Box(Root, "Axle", new Vector3(cx, y0 + DrumY, cz + 0.2f), new Vector3(0.94f, 0.06f, 0.06f), wood, false);
            dropFrom = new Vector3(cx + 0.3f, y0 + DrumY - 0.39f, cz - 0.25f);
            dropTo = new Vector3(cx + 0.64f, y0 + DrumY - 0.63f, cz - 0.3f);
            var chute = assets.Box(Root, "Chute", (dropFrom + dropTo) * 0.5f + new Vector3(0f, -0.08f, 0.02f), new Vector3(0.48f, 0.05f, 0.18f), gold, false).transform;
            chute.localRotation = Quaternion.Euler(0f, 0f, -36f);
            assets.Box(Root, "Tray", dropTo + new Vector3(0.06f, -0.1f, 0f), new Vector3(0.3f, 0.06f, 0.24f), gold, false);
            float legH = dropTo.y - 0.12f - y0;
            assets.Box(Root, "Tray Leg", new Vector3(dropTo.x + 0.06f, y0 + legH * 0.5f, dropTo.z + 0.08f), new Vector3(0.06f, legH, 0.06f), wood, false);
            drop = assets.Box(Root, "Drawn Ball", dropTo, Vector3.one * 0.15f, assets.Unlit(Gold), false).transform;
            drop.gameObject.SetActive(false);
        }

        void Animate(float dt)
        {
            if (drawAge < WinterSchedule.DrawSeconds)
            {
                drawAge += dt;
                float speed = WinterSchedule.DrumSpeed(drawAge);
                drum.localRotation = Quaternion.Euler(0f, 0f, drum.localEulerAngles.z + speed * dt);
                balls.localRotation = Quaternion.Euler(0f, 0f, balls.localEulerAngles.z - speed * 1.6f * dt);
                float u = Mathf.InverseLerp(WinterSchedule.DropAt, WinterSchedule.DrawSeconds, drawAge);
                bool falling = drawAge >= WinterSchedule.DropAt;
                drop.gameObject.SetActive(falling || drawsTonight > 1);
                if (falling)
                {
                    // roll down the chute with a little gravity ease, a hop at the tray
                    var p = Vector3.Lerp(dropFrom, dropTo, u * u);
                    p.y += Mathf.Sin(Mathf.Clamp01((u - 0.8f) / 0.2f) * Mathf.PI) * 0.06f;
                    drop.localPosition = p;
                }
                else if (drawsTonight > 1) drop.localPosition = dropTo;
            }
        }

        int numberTextOf = -1;
        string numberText;

        void UpdateHighlights(float time)
        {
            // the number's text is cached (no per-frame string on WebGL)
            if (numberTextOf != Number) { numberTextOf = Number; numberText = Number > 0 ? Number.ToString() : null; }
            string text = numberText;
            RenderAssets.SetColor(glow, Gold * (0.85f * WinterSchedule.HighlightLevel(time)));
            for (int i = 0; i < highlights.Length && i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                bool on = text != null && building.GetDisplayedText(f) == text;
                var h = highlights[i];
                if (!on)
                {
                    if (h != null && h.Root.activeSelf) h.Root.SetActive(false);
                    continue;
                }
                if (h == null) h = highlights[i] = NewHighlight(f);
                if (!h.Root.activeSelf) h.Root.SetActive(true);
                Fit(h, f);
            }
        }

        Highlight NewHighlight(FloorIndex f)
        {
            var h = new Highlight { Root = new GameObject("Lottery Highlight " + f.PhysicalNumber) };
            h.Root.transform.SetParent(Root, false);
            for (int k = 0; k < h.Bars.Length; k++) h.Bars[k] = assets.Box(h.Root.transform, "Bar " + k, Vector3.zero, Vector3.one, glow, false).transform;
            h.Plate = new Bounds(Vector3.one * 1e6f, Vector3.zero);
            return h;
        }

        /// <summary>Frame one pixel outside the sign plate (behind it), bar under the door lintel.</summary>
        void Fit(Highlight h, FloorIndex f)
        {
            var sign = building.GetSign(f);
            if (sign == null || sign.transform.childCount == 0) return;
            var plate = sign.transform.GetChild(0).GetComponent<Renderer>();
            if (plate == null) return;
            var b = plate.bounds;
            if (b.center == h.Plate.center && b.size == h.Plate.size) return;
            h.Plate = b;
            float px = 1f / Mathf.Max(1f, building.Grid.PixelsPerUnit);
            float t = 2f * px, z = b.max.z + 0.01f;
            float x0 = b.min.x - t, x1 = b.max.x + t, y0 = b.min.y - t, y1 = b.max.y + t;
            void Set(int k, float ax, float ay, float bx, float by)
            {
                var tr = h.Bars[k];
                tr.position = new Vector3((ax + bx) * 0.5f, (ay + by) * 0.5f, z);
                tr.localScale = new Vector3(bx - ax, by - ay, 0.02f);
            }
            Set(0, x0, y1 - t, x1, y1);
            Set(1, x0, y0, x1, y0 + t);
            Set(2, x0, y0, x0 + t, y1);
            Set(3, x1 - t, y0, x1, y1);
            // under the door lintel (lintel: y0 + h - slab - 0.24 .. y0 + h - slab, front z - d/2)
            float fy = f.Value * layout.FloorHeight + layout.FloorHeight - 0.3f - 0.24f;
            float hw = building.ShaftHalfWidth;
            var lintel = h.Bars[4];
            lintel.position = new Vector3(layout.ShaftX, fy - 0.04f, -layout.Depth * 0.5f - 0.03f);
            lintel.localScale = new Vector3(hw * 2f - 0.1f, 0.08f, 0.04f);
        }
    }
}
