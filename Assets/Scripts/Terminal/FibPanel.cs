using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Settings of the selected Fibonacci drawing: change the value of any level, switch levels on or off, give them colours, add new
    /// levels or delete them, reset to the defaults, and choose what is shown (numbers, prices, extend to the right, shading).
    /// </summary>
    public sealed class FibPanel : MonoBehaviour
    {
        TradingSession _session;
        RectTransform _content;
        Text _title;
        readonly List<GameObject> _dynamic = new List<GameObject>();
        string _id, _dismissedFor;
        bool _dirty;
        const float W = 330f, AxisPad = 84f;

        public static FibPanel Create(Transform parent)
        {
            var root = Ui.Rect(parent, "FibPanel");
            Ui.Place(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-W - AxisPad, 34), new Vector2(-AxisPad, -30));
            var p = root.gameObject.AddComponent<FibPanel>();
            p.Build(root);
            root.gameObject.SetActive(false);
            return p;
        }

        void Build(RectTransform root)
        {
            var bg = Ui.Panel(root, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.98f));
            bg.rectTransform.SetAsFirstSibling();
            Ui.Stretch(bg.rectTransform);
            _title = Ui.Label(root, "LEVELS", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -28), new Vector2(-50, -4));
            var close = Ui.Button(root, "x", () => { _dismissedFor = _id; gameObject.SetActive(false); }, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -28), new Vector2(-8, -6));
            var view = Ui.Rect(root, "Viewport");
            Ui.Stretch(view, 4, 4, 4, 36);
            var vimg = view.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();
            _content = Ui.Rect(view, "Content");
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0.5f, 1);
            _content.offsetMin = Vector2.zero; _content.offsetMax = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = _content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24f;
        }

        public void Bind(TradingSession session)
        {
            if (_session != null) _session.Drawings.Changed -= MarkDirty;
            _session = session;
            _session.Drawings.Changed += MarkDirty;
            gameObject.SetActive(false);
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_session != null) _session.Drawings.Changed -= MarkDirty; }
        public bool Visible { get { return gameObject.activeSelf; } }
        public void RefreshNow() { _dirty = false; Rebuild(); }

        /// <summary>Called when the chart's selection changes: a Fibonacci drawing opens its levels, anything else closes them.</summary>
        public void OnSelect(string id)
        {
            if (_session == null) return;
            var d = id != null ? _session.Drawings.Items.FirstOrDefault(x => x.Id == id) : null;
            if (d == null || !DrawingKinds.IsFib(d.Kind)) { _id = null; gameObject.SetActive(false); return; }
            if (_id != id) _dismissedFor = null;
            _id = id;
            if (_dismissedFor == id) return;
            gameObject.SetActive(true);
            _dirty = true;
        }

        /// <summary>Open (or close) the levels of the selected drawing from the properties bar.</summary>
        public void Toggle(string id)
        {
            if (gameObject.activeSelf) { _dismissedFor = id; gameObject.SetActive(false); return; }
            _dismissedFor = null; _id = id; gameObject.SetActive(true); _dirty = true;
        }

        void Update() { if (_dirty && gameObject.activeSelf) { _dirty = false; Rebuild(); } }

        Button Btn(string label, Action act, float x, float y, float w, float h, Color? fill = null, int size = 11, Color? text = null)
        {
            var b = Ui.Button(_content, label, act, fill ?? Theme.PanelAlt, size, text);
            Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - h), new Vector2(x + w, -y));
            _dynamic.Add(b.gameObject);
            return b;
        }

        void Update(Action<List<DrawingKinds.FibLevel>> change)
        {
            _session.Drawings.Update(_id, d =>
            {
                var levels = DrawingKinds.FibLevelDefs(d);
                change(levels);
                DrawingKinds.SetFibLevels(d, levels);
            });
        }

        void Option(string key, string label, ref float y)
        {
            var d = _session.Drawings.Items.FirstOrDefault(x => x.Id == _id);
            bool on = d != null && d.Opt(key);
            Btn((on ? "[x]  " : "[ ]  ") + label, () => _session.Drawings.Update(_id, x => x.Data.Set(key, !x.Opt(key))), 8, y, W - 24, 24, on ? new Color(0.17f, 0.27f, 0.45f) : (Color?)null, 11);
            y += 28;
        }

        void Rebuild()
        {
            foreach (var g in _dynamic) Ui.Kill(g);
            _dynamic.Clear();
            var drawing = _session != null && _id != null ? _session.Drawings.Items.FirstOrDefault(x => x.Id == _id) : null;
            if (drawing == null) { gameObject.SetActive(false); return; }
            _title.text = "LEVELS  -  " + (drawing.Name ?? drawing.Kind);
            var levels = DrawingKinds.FibLevelDefs(drawing);
            float y = 6;
            for (int i = 0; i < levels.Count; i++)
            {
                int index = i;
                var lv = levels[i];
                Btn(lv.On ? "*" : "o", () => Update(l => l[index].On = !l[index].On), 8, y, 26, 26, lv.On ? Theme.Blue : (Color?)null, 12);
                var input = Ui.Input(_content, lv.V.ToString("0.###", CultureInfo.InvariantCulture), 12, InputField.ContentType.DecimalNumber);
                Ui.Place((RectTransform)input.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -y - 26), new Vector2(150, -y));
                input.onEndEdit.AddListener(text =>
                {
                    double v;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) Update(l => l[index].V = Math.Max(-20, Math.Min(100, Math.Round(v * 1000) / 1000)));
                    else _dirty = true;
                });
                _dynamic.Add(input.gameObject);
                Color col; ColorUtility.TryParseHtmlString(lv.Color ?? drawing.Color, out col);
                var sw = Btn("", () => Update(l => l[index].Color = NextColor(l[index].Color ?? drawing.Color, drawing.Color)), 158, y + 2, 34, 22, col);
                Ui.Rounded((Image)sw.targetGraphic, 4f);
                Btn("x", () => Update(l => l.RemoveAt(index)), 200, y, 26, 26, new Color(0.45f, 0.18f, 0.2f), 11);
                y += 30;
            }
            if (levels.Count == 0)
            {
                var none = Ui.Label(_content, "No levels. Add one below.", 12, Theme.Dim, TextAnchor.MiddleLeft);
                Ui.Place(none.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -y - 22), new Vector2(-12, -y));
                _dynamic.Add(none.gameObject);
                y += 26;
            }
            y += 4;
            Btn("+ Add level", () => Update(l => l.Add(new DrawingKinds.FibLevel { V = DrawingKinds.NextLevelValue(l) })), 8, y, 120, 26, new Color(0.1f, 0.5f, 0.4f), 12);
            Btn("Reset levels", () => _session.Drawings.Update(_id, d => DrawingKinds.SetFibLevels(d, DrawingKinds.DefaultFibValues(d.Kind).Select(v => new DrawingKinds.FibLevel { V = v }))), 134, y, 110, 26, null, 12);
            y += 36;
            Option("showLevel", "Show level numbers", ref y);
            if (drawing.Kind == "fib" || drawing.Kind == "fibext") Option("showPrice", "Show prices", ref y);
            if (drawing.Kind == "fib" || drawing.Kind == "fibext" || drawing.Kind == "fibchannel") Option("extend", "Extend to the right", ref y);
            if (drawing.Kind != "fibtime" && drawing.Kind != "fibarcs") Option("fill", "Shade between levels", ref y);
            var tip = Ui.Label(_content, "Click the dot to switch a level off or on, type a new value, click the colour to change it. Levels are stored with the drawing.", 10, Theme.Dim, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(tip.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -y - 44), new Vector2(-10, -y));
            _dynamic.Add(tip.gameObject);
            y += 50;
            _content.sizeDelta = new Vector2(0, y + 6);
        }

        /// <summary>Cycle a level's colour through the drawing palette; the drawing's own colour means "no colour of its own".</summary>
        static string NextColor(string current, string drawingColor)
        {
            var list = DrawingKinds.Colors;
            int i = Array.FindIndex(list, c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
            return list[(i + 1) % list.Length];
        }
    }
}
