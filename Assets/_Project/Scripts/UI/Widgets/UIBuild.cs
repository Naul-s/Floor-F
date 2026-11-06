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
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            if (bold) t.fontStyle = FontStyles.Bold;
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

        /// <summary>Sets text only when it changed (no TMP rebuild per frame).</summary>
        public static void Set(TextMeshProUGUI t, string value)
        {
            if (t != null && !string.Equals(t.text, value, System.StringComparison.Ordinal)) t.text = value;
        }
    }
}
