using System;
using UnityEngine;
using UnityEngine.UI;

namespace WH30K.UI
{
    /// <summary>Shared visual construction for the colony's runtime uGUI surfaces.</summary>
    internal static class CommandUi
    {
        internal static readonly Color Brass = new Color32(218, 182, 111, 255);
        internal static readonly Color Parchment = new Color32(243, 233, 213, 255);
        internal static readonly Color SecondaryText = new Color32(182, 183, 172, 255);
        internal static readonly Color Iron = new Color32(24, 26, 28, 248);
        internal static readonly Color Field = new Color32(42, 43, 44, 255);
        internal static readonly Color Oxblood = new Color32(137, 57, 44, 255);
        internal static readonly Color Signal = new Color32(115, 193, 188, 255);
        internal static readonly Color Warning = new Color32(240, 179, 93, 255);
        internal static readonly Color Critical = new Color32(246, 128, 111, 255);
        private static readonly Color Backdrop = new Color32(10, 11, 12, 255);
        private static readonly Color Edge = new Color32(140, 116, 74, 220);
        private static readonly Color Hover = new Color32(65, 58, 46, 255);
        private static readonly Color Active = new Color32(91, 68, 39, 255);
        private static readonly Color Disabled = new Color32(15, 16, 17, 255);

        internal static GameObject Panel(string name, Transform parent, Vector2 size, Vector2 position,
            TextAnchor anchor, bool border = true, bool receivesPointer = true)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            var pivot = anchor == TextAnchor.UpperRight ? Vector2.one
                : anchor == TextAnchor.UpperLeft ? Vector2.up
                : anchor == TextAnchor.LowerCenter ? new Vector2(.5f, 0) : new Vector2(.5f, .5f);
            rect.anchorMin = rect.anchorMax = rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = panel.GetComponent<Image>();
            image.color = Iron;
            image.raycastTarget = receivesPointer;
            if (border)
            {
                var outline = panel.AddComponent<Outline>();
                outline.effectColor = Edge;
                outline.effectDistance = new Vector2(1, -1);
            }
            return panel;
        }

        internal static GameObject Screen(string name, Transform parent, float opacity)
        {
            var panel = Panel(name, parent, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var tint = Backdrop;
            tint.a = opacity;
            panel.GetComponent<Image>().color = tint;
            return panel;
        }

        internal static void Accent(GameObject panel, Color tint)
        {
            var accent = Panel("PanelAccent", panel.transform, new Vector2(3, 0), new Vector2(2, 0),
                TextAnchor.UpperLeft, false, false);
            var rect = (RectTransform)accent.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.up;
            rect.pivot = new Vector2(.5f, .5f);
            accent.GetComponent<Image>().color = tint;
        }

        internal static Text Label(string name, Transform parent, Font font, string value, int size,
            Vector2 position, float width, float height, Color tint)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = position;
            var text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = tint;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal static Button Button(string name, Transform parent, Font font, Vector2 position,
            string value, out Text label, float width, float height)
        {
            var obj = Panel(name, parent, new Vector2(width, height), position, TextAnchor.UpperLeft);
            var image = obj.GetComponent<Image>();
            image.color = Color.white;
            var button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Field;
            colors.highlightedColor = Hover;
            colors.selectedColor = Hover;
            colors.pressedColor = Active;
            colors.disabledColor = Disabled;
            colors.fadeDuration = .12f;
            button.colors = colors;
            label = Label("Label", obj.transform, font, value, 14, Vector2.zero, width, height, Parchment);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        internal static void Tone(Button button, bool selected)
        {
            var colors = button.colors;
            colors.normalColor = selected ? Active : Field;
            button.colors = colors;
            var edge = button.GetComponent<Outline>();
            if (edge != null) edge.effectColor = selected ? Brass : Edge;
        }

        internal static PortraitCard CreatePortraitCard(Transform parent, Font font)
        {
            var card = Panel("VoxPortraitCard", parent, new Vector2(446, 112), Vector2.zero,
                TextAnchor.UpperLeft, true, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -110);
            Accent(card, Brass);
            var frame = Panel("PortraitFrame", card.transform, new Vector2(76, 76), new Vector2(15, -18),
                TextAnchor.UpperLeft, true, false);
            var picture = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            picture.transform.SetParent(frame.transform, false);
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = Vector2.one * 2;
            pictureRect.offsetMax = -Vector2.one * 2;
            var portrait = picture.GetComponent<RawImage>();
            portrait.raycastTarget = false;
            var heading = Label("VoxSpeaker", card.transform, font, "", 13,
                new Vector2(105, -12), 326, 24, Brass);
            var message = Label("VoxMessage", card.transform, font, "", 14,
                new Vector2(105, -39), 326, 65, Parchment);
            heading.supportRichText = message.supportRichText = false;
            card.SetActive(false);
            var action = Button("VoxAction", card.transform, font, new Vector2(105, -104),
                "", out var actionLabel, 180, 34);
            action.gameObject.SetActive(false);
            return new PortraitCard(card, portrait, heading, message, action, actionLabel);
        }

        internal sealed class PortraitCard
        {
            private readonly GameObject root;
            private readonly RawImage portrait;
            private readonly Text heading;
            private readonly Text message;
            private readonly Button action;
            private readonly Text actionLabel;
            private Action currentAction;

            internal PortraitCard(GameObject root, RawImage portrait, Text heading, Text message, Button action, Text actionLabel)
            {
                this.root = root;
                this.portrait = portrait;
                this.heading = heading;
                this.message = message;
                this.action = action;
                this.actionLabel = actionLabel;
                action.onClick.AddListener(() => currentAction?.Invoke());
            }

            internal void Show(Texture portraitTexture, string speaker, string copy, Color priority, Action open = null, string openLabel = null)
            {
                portrait.texture = portraitTexture;
                portrait.enabled = portraitTexture != null;
                heading.text = speaker;
                heading.color = priority;
                message.text = copy;
                currentAction = open;
                action.gameObject.SetActive(open != null);
                actionLabel.text = string.IsNullOrWhiteSpace(openLabel) ? "OPEN DETAILS" : openLabel;
                ((RectTransform)root.transform).sizeDelta = new Vector2(446, open == null ? 112 : 150);
                root.SetActive(true);
            }

            internal void SetTopOffset(float y)
            {
                var rect = (RectTransform)root.transform;
                if (rect.anchoredPosition.y != y) rect.anchoredPosition = new Vector2(0, y);
            }

            internal void Hide() => root.SetActive(false);
        }
    }
}
