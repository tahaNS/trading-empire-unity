using System;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>Tiny code-only uGUI toolkit (no prefabs, no TextMeshPro) so the terminal needs no assets to run.</summary>
    public static class Theme
    {
        public static readonly Color Bg = new Color(0.047f, 0.063f, 0.094f);
        public static readonly Color Panel = new Color(0.078f, 0.102f, 0.145f);
        public static readonly Color PanelAlt = new Color(0.105f, 0.133f, 0.184f);
        public static readonly Color Border = new Color(0.17f, 0.21f, 0.28f);
        public static readonly Color Text = new Color(0.86f, 0.89f, 0.94f);
        public static readonly Color Dim = new Color(0.52f, 0.58f, 0.68f);
        public static readonly Color Accent = new Color(0.96f, 0.75f, 0.24f);
        public static readonly Color Blue = new Color(0.25f, 0.52f, 0.96f);
        /// <summary>Colour-blind candles: blue and orange instead of green and red (a setting).</summary>
        public static bool ColorBlind;
        public static Color Bull { get { return ColorBlind ? new Color(0.30f, 0.62f, 0.95f) : new Color(0.15f, 0.76f, 0.58f); } }
        public static Color Bear { get { return ColorBlind ? new Color(0.98f, 0.66f, 0.18f) : new Color(0.93f, 0.32f, 0.36f); } }
        public static readonly Color Grid = new Color(1f, 1f, 1f, 0.06f);

        static Font _font, _mono;
        public static Font Font
        {
            get { if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return _font; }
        }
        public static Font Mono
        {
            get
            {
                if (_mono == null)
                {
                    try { _mono = Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Consolas", "Courier New" }, 14); }
                    catch (Exception) { _mono = null; }
                    if (_mono == null) _mono = Font;
                }
                return _mono;
            }
        }
    }

    public static class Ui
    {
        /// <summary>Destroy a UI object: deferred while playing, immediate in the editor (where Destroy does not run).</summary>
        public static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        /// <summary>Place by anchors (0..1) and pixel offsets from those anchors.</summary>
        public static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        public static Image Box(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A panel background that also blocks the mouse, so clicks on it never reach the 3D city underneath.</summary>
        public static Image Panel(Transform parent, string name, Color color)
        {
            var img = Box(parent, name, color);
            img.raycastTarget = true;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool mono = false, bool bold = false)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = mono ? Theme.Mono : Theme.Font;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            Stretch(rt);
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color? fill = null, int size = 14, Color? textColor = null)
        {
            var rt = Rect(parent, "Button " + label);
            var img = rt.gameObject.AddComponent<Image>();
            Color c = fill ?? Theme.PanelAlt;
            img.color = c;
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1, 1, 1, 0.4f);
            b.colors = colors;
            b.targetGraphic = img;
            b.onClick.AddListener(() => AudioManager.Click());
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Label(rt, label, size, textColor ?? Theme.Text, TextAnchor.MiddleCenter, false, true);
            t.name = "Label";
            return b;
        }

        public static void SetButtonColor(Button b, Color c) { ((Image)b.targetGraphic).color = c; }
        public static void SetButtonLabel(Button b, string text) { b.transform.Find("Label").GetComponent<Text>().text = text; }

        public static InputField Input(Transform parent, string value, int size = 14, InputField.ContentType type = InputField.ContentType.Standard)
        {
            var rt = Rect(parent, "Input");
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.03f, 0.04f, 0.07f);
            var field = rt.gameObject.AddComponent<InputField>();
            var text = Label(rt, "", size, Theme.Text, TextAnchor.MiddleLeft, true);
            Stretch(text.rectTransform, 8, 2, 8, 2);
            text.raycastTarget = false;
            field.textComponent = text;
            field.targetGraphic = img;
            field.contentType = type;
            field.text = value;
            return field;
        }
    }
}
