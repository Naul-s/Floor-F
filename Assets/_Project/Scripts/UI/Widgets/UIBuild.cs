using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>Runtime uGUI/TMP construction helpers. Every object goes on PresentationContract.UiLayer.</summary>
    static class UIBuild
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = PresentationContract.UiLayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Anchors at <paramref name="anchor"/> (0..1), pivot = anchor, then position/size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Box(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Box with a 2px edge (edge image behind, fill inset).</summary>
        public static Image Framed(string name, Transform parent, Color fill, Color edge, float edgeWidth = 2f, bool raycast = false)
        {
            var outer = Box(name, parent, edge, raycast);
            var inner = Box("Fill", outer.transform, fill);
            Stretch(inner.rectTransform, edgeWidth);
            return outer;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = UIFonts.Main; // before any text: TMP's default font is never rendered
            t.text = text;
            t.fontSize = UIFonts.Px(size);
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            // `bold` is accepted for intent but not applied: faux bold smears a pixel font. Hierarchy comes from size/colour.
            t.fontStyle = FontStyles.Normal;
            return t;
        }

        /// <summary>
        /// Big title / display text (36px and up): Galmuri11 Bold (<see cref="UIFonts.Heading"/>) instead of the body font.
        /// Only for short, big texts - it is wide and its 12px glyphs blur together.
        /// </summary>
        public static TextMeshProUGUI Heading(TextMeshProUGUI t)
        {
            if (t != null) t.font = UIFonts.Heading;
            return t;
        }

        public sealed class ButtonParts
        {
            public Button Button;
            public Image Background;
            public TextMeshProUGUI Label;
            public RectTransform Rect => (RectTransform)Button.transform;
        }

        public static ButtonParts Button(string name, Transform parent, string label, float fontSize, Color bg, UnityAction onClick)
        {
            var img = Box(name, parent, bg, raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            colors.colorMultiplier = 1.2f;
            colors.fadeDuration = 0.06f;
            b.colors = colors;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None; // keyboard stays with KeyboardInput (Space must not "click" a focused button)
            b.navigation = nav;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Text("Label", img.transform, label, fontSize, UIStyle.InkOn(bg), TextAlignmentOptions.Center, bold: true);
            Stretch(t.rectTransform, 4f);
            return new ButtonParts { Button = b, Background = img, Label = t };
        }

        /// <summary>
        /// Pixel-grid replacement for TMP auto-sizing: the largest multiple of 12 (&lt;= <paramref name="max"/>) at which
        /// the text fits the rect width on one line (12 at minimum).
        /// </summary>
        public static void Fit(TextMeshProUGUI t, float max)
        {
            if (t == null) return;
            t.enableAutoSizing = false;
            float width = t.rectTransform.rect.width;
            float s = UIFonts.Px(max);
            if (s > max) s -= UIFonts.Grid;
            for (; s > UIFonts.Grid; s -= UIFonts.Grid)
            {
                t.fontSize = s;
                if (width <= 0f || t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity).x <= width) return;
            }
            t.fontSize = UIFonts.Grid;
        }

        /// <summary>Hollow rectangle (four bars) stretched over its parent, <paramref name="outset"/> px outside it.</summary>
        public static RectTransform Outline(string name, Transform parent, Color color, float width, float outset)
        {
            var rt = Stretch(Rect(name, parent), -outset);
            for (int i = 0; i < 4; i++)
            {
                var bar = Box("Edge" + i, rt, color).rectTransform;
                bool horizontal = i < 2;
                float side = (i % 2 == 0) ? 0f : 1f;
                bar.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side, 0f);
                bar.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side, 1f);
                bar.pivot = horizontal ? new Vector2(0.5f, side) : new Vector2(side, 0.5f);
                bar.anchoredPosition = Vector2.zero;
                bar.sizeDelta = horizontal ? new Vector2(0f, width) : new Vector2(width, 0f);
            }
            return rt;
        }

        /// <summary>Thin bar from <paramref name="a"/> to <paramref name="b"/> (canvas-local, bottom-left origin anchors).</summary>
        public static void SetLine(RectTransform line, Vector2 a, Vector2 b, float width)
        {
            line.anchorMin = line.anchorMax = Vector2.zero;
            line.pivot = new Vector2(0f, 0.5f);
            var d = b - a;
            line.anchoredPosition = a;
            line.sizeDelta = new Vector2(d.magnitude, width);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        /// <summary>Horizontal 0..1 slider (track, fill, square handle) built from Images.</summary>
        public static Slider Slider(string name, Transform parent, Vector2 size, UnityAction<float> onChange)
        {
            var root = Rect(name, parent);
            root.sizeDelta = size;
            float inset = size.y * 0.3f;
            var track = Box("Track", root, new Color(0f, 0f, 0f, 0.85f), raycast: true);
            Stretch(track.rectTransform);
            track.rectTransform.offsetMin = new Vector2(0f, inset);
            track.rectTransform.offsetMax = new Vector2(0f, -inset);
            var fillArea = Stretch(Rect("Fill Area", root));
            fillArea.offsetMin = new Vector2(0f, inset);
            fillArea.offsetMax = new Vector2(0f, -inset);
            var fill = Box("Fill", fillArea, UIStyle.Lucky);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Stretch(Rect("Handle Area", root));
            handleArea.offsetMin = new Vector2(inset, 0f);
            handleArea.offsetMax = new Vector2(-inset, 0f);
            var handle = Box("Handle", handleArea, UIStyle.Ink, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(size.y * 0.6f, 0f);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = 0f;
            s.maxValue = 1f;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.None;
            s.navigation = nav;
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// <summary>Canvas-local (bottom-left origin) rect of any RectTransform under <paramref name="canvas"/>.</summary>
        /// <summary>First of <paramref name="names"/> (trimmed, empties skipped) that fits <paramref name="width"/> on one line at t's size, or null.</summary>
        public static string FirstThatFits(TextMeshProUGUI t, float width, params string[] names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                string s = n.Trim();
                if (t.font != null) t.font.TryAddCharacters(s); // dynamic atlas: glyphs must exist before measuring
                if (width <= 0f || t.GetPreferredValues(s, float.PositiveInfinity, float.PositiveInfinity).x <= width) return s;
            }
            return null;
        }

        /// <summary>
        /// Keeps <paramref name="size"/> (a grid size): the full <paramref name="name"/> if it fits the rect width on one
        /// line, else the curated <paramref name="shortName"/> ("Guest Rooms" -> "Rooms"), else that one (or the name when
        /// there is no curated one) cut until it fits ("Observatory" -> "Obser.") - names stay big instead of shrinking.
        /// </summary>
        public static void FitName(TextMeshProUGUI t, string name, float size, string shortName = null)
        {
            if (t == null) return;
            t.enableAutoSizing = false;
            t.fontSize = UIFonts.Px(size);
            name = string.IsNullOrEmpty(name) ? "?" : name.Trim();
            float width = t.rectTransform.rect.width;
            string whole = FirstThatFits(t, width, name, shortName);
            if (whole != null) { Set(t, whole); return; }
            if (!string.IsNullOrEmpty(shortName)) name = shortName.Trim();
            if (t.font != null) t.font.TryAddCharacters(name);
            string s = name;
            for (int max = name.Length; max >= 3; max--)
            {
                s = UIFormat.ShortName(name, max);
                if (width <= 0f || t.GetPreferredValues(s, float.PositiveInfinity, float.PositiveInfinity).x <= width) break;
            }
            Set(t, s);
        }

        static readonly Vector3[] corners = new Vector3[4];

        public static UnityEngine.Rect CanvasRect(RectTransform canvas, RectTransform target)
        {
            target.GetWorldCorners(corners);
            var a = (Vector2)canvas.InverseTransformPoint(corners[0]);
            var b = (Vector2)canvas.InverseTransformPoint(corners[2]);
            var off = canvas.rect.min; // canvas pivot is the centre
            return UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x, b.x) - off.x, Mathf.Min(a.y, b.y) - off.y, Mathf.Max(a.x, b.x) - off.x, Mathf.Max(a.y, b.y) - off.y);
        }

        /// <summary>Sets text only when it changed (no TMP rebuild per frame).</summary>
        public static void Set(TextMeshProUGUI t, string value)
        {
            if (t != null && !string.Equals(t.text, value, System.StringComparison.Ordinal)) t.text = value;
        }
    }
}
