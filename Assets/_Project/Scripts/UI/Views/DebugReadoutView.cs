using FloorF.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FloorF.UI
{
    /// <summary>
    /// Draws Gameplay's debug-cheat readout (<see cref="DebugReadoutChanged"/>): a small red tag at the top centre while debug mode is
    /// on, and the F1 state/key list in a dark box in the middle. Lives on the FX canvas (above everything, no raycasts).
    /// Hidden unless the player deliberately turned debug mode on.
    /// </summary>
    sealed class DebugReadoutView
    {
        static readonly Color TagColor = new Color(1f, 0.38f, 0.32f, 0.95f);
        public const float TagMaxW = 600f;

        readonly RectTransform tagPlate, box;
        readonly TextMeshProUGUI tag, body;

        public DebugReadoutView(RectTransform fx)
        {
            var tagImg = UIBuild.Box("DebugTag", fx, new Color(0f, 0f, 0f, 0.7f));
            tagPlate = UIBuild.Place(tagImg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(TagMaxW, 30f));
            tag = UIBuild.Text("Text", tagPlate, "", 18f, TagColor, TextAlignmentOptions.Center);
            UIBuild.Stretch(tag.rectTransform, 4f);
            tag.textWrappingMode = TextWrappingModes.NoWrap;
            tag.richText = false;

            var boxImg = UIBuild.Box("DebugReadout", fx, new Color(0.02f, 0.02f, 0.05f, 0.97f));
            box = UIBuild.Place(boxImg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 620f));
            body = UIBuild.Text("Text", box, "", 17f, UIStyle.Ink, TextAlignmentOptions.TopLeft);
            UIBuild.Stretch(body.rectTransform, 10f);
            body.richText = false;
            body.textWrappingMode = TextWrappingModes.NoWrap;
            body.overflowMode = TextOverflowModes.Truncate;

            tagPlate.gameObject.SetActive(false);
            box.gameObject.SetActive(false);
        }

        public bool TagVisible => tagPlate.gameObject.activeSelf;
        public bool BodyVisible => box.gameObject.activeSelf;
        public string TagText => tag.text;

        public void On(DebugReadoutChanged e)
        {
            bool showTag = !string.IsNullOrEmpty(e.Tag);
            tagPlate.gameObject.SetActive(showTag);
            if (showTag)
            {
                tag.text = e.Tag;
                // only as wide as its text: a fixed 600-wide plate hid most of the HOTEL sign under it
                float w = tag.GetPreferredValues(e.Tag, float.PositiveInfinity, float.PositiveInfinity).x;
                tagPlate.sizeDelta = new Vector2(Mathf.Clamp(Mathf.Ceil(w) + 16f, 80f, TagMaxW), tagPlate.sizeDelta.y);
                tagPlate.SetAsLastSibling();
            }
            bool showBody = !string.IsNullOrEmpty(e.Body);
            box.gameObject.SetActive(showBody);
            if (showBody)
            {
                body.text = e.Body;
                box.SetAsLastSibling();
            }
        }
    }
}
