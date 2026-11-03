using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The object tree: every drawing of the chart in one list, top of the stack first. Select a row to select the drawing; show / hide,
    /// lock, reorder and delete without hunting on the chart.
    /// </summary>
    public sealed class ObjectPanel : MonoBehaviour
    {
        TradingSession _session;
        ChartView _chart;
        RectTransform _content;
        Text _count;
        Button _hideAll;
        readonly List<GameObject> _dynamic = new List<GameObject>();
        string _selectedSeen;
        bool _dirty = true;
        const float W = 330f, AxisPad = 84f;

        public static ObjectPanel Create(Transform parent, ChartView chart)
        {
            var root = Ui.Rect(parent, "ObjectPanel");
            Ui.Place(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-W - AxisPad, 34), new Vector2(-AxisPad, -30));
            var p = root.gameObject.AddComponent<ObjectPanel>();
            p._chart = chart;
            p.Build(root);
            root.gameObject.SetActive(false);
            return p;
        }

        void Build(RectTransform root)
        {
            Ui.Panel(root, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.98f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)root.Find("Bg"));
            var title = Ui.Label(root, "OBJECTS", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -28), new Vector2(90, -4));
            _count = Ui.Label(root, "", 11, Theme.Dim, TextAnchor.MiddleLeft, true);
            Ui.Place(_count.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(84, -28), new Vector2(130, -4));
            _hideAll = Ui.Button(root, "Hide all", () => { if (_session != null) _session.Drawings.SetAllHidden(!_session.Drawings.Items.All(d => d.Hidden)); }, Theme.PanelAlt, 11);
            Ui.Place((RectTransform)_hideAll.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -28), new Vector2(-40, -6));
            var close = Ui.Button(root, "x", () => root.gameObject.SetActive(false), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -28), new Vector2(-8, -6));
            var del = Ui.Button(root, "Delete all", () => { if (_session != null) _session.Drawings.Clear(); }, new Color(0.45f, 0.18f, 0.2f), 11);
            Ui.Place((RectTransform)del.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-100, 6), new Vector2(-8, 30));
            var hint = Ui.Label(root, "Click a name to select it on the chart.", 10, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 6), new Vector2(-110, 30));

            var view = Ui.Rect(root, "Viewport");
            Ui.Stretch(view, 4, 36, 4, 36);
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
            _dirty = true;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_session != null) _session.Drawings.Changed -= MarkDirty; }

        public bool Visible { get { return gameObject.activeSelf; } }
        public void Toggle() { gameObject.SetActive(!gameObject.activeSelf); _dirty = true; }
        public void Show(bool show) { gameObject.SetActive(show); _dirty = true; }
        public void RefreshNow() { _dirty = false; Rebuild(); }

        void Update()
        {
            if (!gameObject.activeSelf) return;
            if (_chart.SelectedId != _selectedSeen) _dirty = true; // the highlighted row follows the chart's selection
            if (_dirty) { _dirty = false; Rebuild(); }
        }

        void Rebuild()
        {
            if (_session == null) return;
            foreach (var g in _dynamic) Ui.Kill(g);
            _dynamic.Clear();
            _selectedSeen = _chart.SelectedId;
            var items = _session.Drawings.Items;
            _count.text = items.Count.ToString();
            bool allHidden = items.Count > 0 && items.All(d => d.Hidden);
            Ui.SetButtonLabel(_hideAll, allHidden ? "Show all" : "Hide all");
            _hideAll.interactable = items.Count > 0;
            float y = 4;
            if (items.Count == 0)
            {
                var t = Ui.Label(_content, "No drawings yet. Pick a tool on the left and draw on the chart: it will show up here.", 12, Theme.Dim, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Place(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -y - 50), new Vector2(-12, -y));
                _dynamic.Add(t.gameObject);
                _content.sizeDelta = new Vector2(0, 60);
                return;
            }
            for (int n = items.Count - 1; n >= 0; n--)
            {
                var d = items[n];
                string id = d.Id;
                bool sel = id == _chart.SelectedId;
                var row = Ui.Box(_content, "Row " + id, sel ? new Color(0.17f, 0.27f, 0.45f) : new Color(1f, 1f, 1f, 0.04f));
                Ui.Place(row.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(4, -y - 30), new Vector2(-4, -y));
                _dynamic.Add(row.gameObject);
                Color col; ColorUtility.TryParseHtmlString(d.Color ?? "#6ea8ff", out col);
                var sw = Ui.Box(row.transform, "Swatch", col);
                Ui.Place(sw.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(6, 8), new Vector2(14, -8));
                var name = Ui.Button(row.transform, d.Name ?? Label(d), () => { _chart.Select(id); _dirty = true; }, Color.clear, 11, d.Hidden ? Theme.Dim : Theme.Text);
                Ui.Place((RectTransform)name.transform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 2), new Vector2(-124, -2));
                name.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                Mini(row.transform, d.Hidden ? "o" : "*", -118, () => _session.Drawings.Update(id, x => x.Data.Set("hidden", !x.Hidden)), !d.Hidden);
                Mini(row.transform, d.Locked ? "L" : "l", -92, () => _session.Drawings.Update(id, x => x.Data.Set("locked", !x.Locked)), d.Locked);
                Mini(row.transform, "^", -66, () => _session.Drawings.Move(id, "up"), false);
                Mini(row.transform, "v", -40, () => _session.Drawings.Move(id, "down"), false);
                Mini(row.transform, "x", -14, () => { if (_chart.SelectedId == id) _chart.Select(null); _session.Drawings.Remove(id); }, false);
                y += 34;
            }
            _content.sizeDelta = new Vector2(0, y + 6);
        }

        void Mini(Transform row, string label, float right, Action act, bool on)
        {
            var b = Ui.Button(row, label, act, on ? Theme.Blue : Theme.PanelAlt, 10);
            Ui.Place((RectTransform)b.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(right - 12, 4), new Vector2(right + 12, -4));
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "hline", "Horizontal line" }, { "vline", "Vertical line" }, { "hray", "Horizontal ray" }, { "trend", "Trend line" }, { "channel", "Parallel channel" },
            { "regression", "Regression channel" }, { "pitchfork", "Andrews' pitchfork" }, { "gannfan", "Gann fan" }, { "rect", "Rectangle" }, { "ellipse", "Ellipse" },
            { "triangle", "Triangle" }, { "fib", "Fib retracement" }, { "fibext", "Fib extension" }, { "fibchannel", "Fib channel" }, { "fibtime", "Fib time zones" },
            { "fibfan", "Fib speed fan" }, { "fibarcs", "Fib arcs" }, { "position", "Position box" }, { "text", "Text note" }, { "pricelabel", "Price label" }, { "marker", "Arrow marker" },
        };

        static string Label(Drawing d)
        {
            string name; if (!Names.TryGetValue(d.Kind ?? "", out name)) name = d.Kind ?? "Drawing";
            if (d.Kind == "text" && !string.IsNullOrEmpty(d.Text)) return name + ": " + d.Text;
            if (d.Kind == "position") return (d.Str("side") == "short" ? "Short" : "Long") + " position box";
            return name;
        }
    }
}
