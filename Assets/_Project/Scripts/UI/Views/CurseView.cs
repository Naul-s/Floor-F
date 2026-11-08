using System.Collections.Generic;
using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// "666" curse: an UNLUCKY 6 (any source) from a devout guest (GuestTraits.Devout - it counts double in Core) gets a
    /// big red shaking/flashing "666!" with a caption, over a red vignette pulsing on the whole canvas, for
    /// <see cref="Life"/> seconds. UI-only (a runtime radial texture on an Image, no Rendering references); never blocks
    /// clicks. Sits above the HUD and popups, under the flow cards, coach marks and menus.
    /// </summary>
    sealed class CurseView
    {
        public const float Life = 1.5f;
        const float PunchTime = 0.15f, FlashTime = 0.45f, FadeTime = 0.35f;

        readonly UIContext ctx;
        readonly RectTransform root, textRoot;
        readonly CanvasGroup group;
        readonly Image vignette;
        readonly TextMeshProUGUI number, shadow, caption;
        readonly Dictionary<int, GuestArchetypeId> archetypes = new Dictionary<int, GuestArchetypeId>();
        float age = -1f;
        static Sprite radial;

        public CurseView(UIContext ctx, RectTransform canvas)
        {
            this.ctx = ctx;
            root = UIBuild.Stretch(UIBuild.Rect("Curse666", canvas));
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            vignette = UIBuild.Box("Vignette", root, new Color(0.85f, 0.02f, 0.02f, 0f));
            vignette.sprite = Radial();
            UIBuild.Stretch(vignette.rectTransform);

            textRoot = UIBuild.Rect("Text", root);
            UIBuild.Place(textRoot, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(560f, 200f));
            // a dark plate behind the number: it reads over the busy building
            var back = UIBuild.Box("NumberPlate", textRoot, new Color(0.02f, 0f, 0f, 0.93f));
            UIBuild.Place(back.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 4f), new Vector2(340f, 148f));
            shadow = UIBuild.Text("Shadow", textRoot, UIStrings.CurseNumber, 120f, new Color(0f, 0f, 0f, 0.9f), TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(shadow.rectTransform, new Vector2(0.5f, 1f), new Vector2(4f, -4f), new Vector2(560f, 140f));
            shadow.textWrappingMode = TextWrappingModes.NoWrap;
            number = UIBuild.Text("Number", textRoot, UIStrings.CurseNumber, 120f, UIStyle.Unlucky, TextAlignmentOptions.Center, bold: true);
            UIBuild.Place(number.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 140f));
            number.textWrappingMode = TextWrappingModes.NoWrap;
            var plate = UIBuild.Box("CaptionPlate", textRoot, new Color(0f, 0f, 0f, 0.8f));
            UIBuild.Place(plate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(560f, 40f));
            caption = UIBuild.Text("Caption", plate.rectTransform, "", 24f, UIStyle.Ink, TextAlignmentOptions.Center, bold: true);
            UIBuild.Stretch(caption.rectTransform, 4f);
            caption.textWrappingMode = TextWrappingModes.NoWrap;

            root.gameObject.SetActive(false);
        }

        /// <summary>White-alpha radial ramp (clear centre, opaque corners), built once per play session.</summary>
        static Sprite Radial()
        {
            if (radial != null) return radial;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CurseVignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f; // 0 centre .. 1 corner
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    a *= a; // edges and corners only: the building in the middle stays readable
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            radial = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            radial.name = "CurseVignette";
            return radial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => radial = null;

        public void OnGuestSpawned(GuestSpawned e) => archetypes[e.Guest.Value] = e.Archetype;

        public void OnLuckJudged(LuckJudged e)
        {
            if (UIFormat.IsCurse(e.Verdict, e.Number, IsDevout(e.Guest))) Play();
        }

        bool IsDevout(GuestId guest)
        {
            if (ctx.Data == null) return false;
            GuestArchetypeId archetype;
            if (ctx.Guests != null && ctx.Guests.TryGet(guest, out var info)) archetype = info.Archetype;
            else if (!archetypes.TryGetValue(guest.Value, out archetype)) return false;
            return ctx.Data.TryGetGuest(archetype, out var def) && def.HasTrait(GuestTraits.Devout);
        }

        public void Play()
        {
            age = 0f;
            UIBuild.Set(caption, ctx.S(UIStrings.CurseCaption));
            UIBuild.Fit(caption, 24f);
            root.gameObject.SetActive(true); // sibling order from UIRoot: above HUD/popups, under flow cards/coach marks/menu
            Animate();
        }

        public void Tick(float dt)
        {
            if (age < 0f) return;
            age += dt;
            if (age >= Life) { Clear(); return; }
            Animate();
        }

        void Animate()
        {
            float t = age;
            float punch = t < PunchTime ? Mathf.Lerp(2.2f, 1f, t / PunchTime) : 1f + 0.04f * Mathf.Sin(t * 18f) * (1f - t / Life);
            textRoot.localScale = new Vector3(punch, punch, 1f);
            float shake = 12f * (1f - t / Life);
            textRoot.anchoredPosition = new Vector2(Mathf.Sin(t * 83f) * shake, Mathf.Cos(t * 71f) * shake * 0.6f);
            // photosensitivity (WCAG 2.3.1, <= 3 flashes/s): one white pop at the start, no strobing
            bool flash = t < Mathf.Min(FlashTime, 0.12f);
            number.color = flash ? Color.white : UIStyle.Unlucky;
            // vignette: in fast, one slow heartbeat (0.75 cycles/s), out with the group
            float env = t < 0.1f ? t / 0.1f : 1f;
            float beat = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Cos(t * Mathf.PI * 2f * 0.75f));
            vignette.color = new Color(0.85f, 0.02f, 0.02f, 0.9f * env * beat);
            group.alpha = t > Life - FadeTime ? Mathf.Clamp01((Life - t) / FadeTime) : 1f;
        }

        public void Clear()
        {
            age = -1f;
            root.gameObject.SetActive(false);
        }

        /// <summary>A new run: forget spawned guests.</summary>
        public void Reset()
        {
            archetypes.Clear();
            Clear();
        }

        // test hooks
        public bool Visible => root.gameObject.activeInHierarchy;
        public string Text => Visible ? number.text + " " + caption.text : null;
        public float VignetteAlpha => Visible ? vignette.color.a * group.alpha : 0f;
        public float Age => age;
        public Rect TextRect(RectTransform canvas) => Visible ? UIBuild.CanvasRect(canvas, textRoot) : default;
        public float CaptionFontSize => caption.fontSize;
    }
}
