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
    /// The indicators window on the chart: what is on the chart (visibility, settings, colours, remove) and the list to add from.
    /// It only talks to the session's <see cref="IndicatorStore"/>.
    /// </summary>
    public sealed class IndicatorPanel : MonoBehaviour
    {
        TradingSession _session;
        RectTransform _content;
        ScrollRect _scroll;
        readonly List<GameObject> _dynamic = new List<GameObject>();
        readonly List<Button> _tabs = new List<Button>();
        int _tab;
        string _selected;
        bool _dirty = true;
        const float W = 312f;

        public static IndicatorPanel Create(Transform parent)
        {
            var root = Ui.Rect(parent, "IndicatorPanel");
            Ui.Place(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-W - AxisPad, 34), new Vector2(-AxisPad, -30));
            var panel = root.gameObject.AddComponent<IndicatorPanel>();
            panel.Build(root);
            root.gameObject.SetActive(false);
            return panel;
        }

        const float AxisPad = 84f;

        void Build(RectTransform root)
        {
            Ui.Panel(root, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.98f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)root.Find("Bg"));
            var title = Ui.Label(root, "INDICATORS", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -28), new Vector2(-60, -4));
            var close = Ui.Button(root, "x", () => root.gameObject.SetActive(false), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -28), new Vector2(-8, -6));
            string[] names = { "On chart", "Add" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = Ui.Button(root, names[i], () => { _tab = index; _dirty = true; }, Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8 + i * 98, -58), new Vector2(98 * i + 100, -32));
                _tabs.Add(b);
            }
            var view = Ui.Rect(root, "Viewport");
            Ui.Stretch(view, 4, 4, 4, 64);
            var vimg = view.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();
            _content = Ui.Rect(view, "Content");
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0.5f, 1);
            _content.offsetMin = Vector2.zero; _content.offsetMax = Vector2.zero;
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = view; _scroll.content = _content;
            _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 24f;
        }

        public void Bind(TradingSession session)
        {
            if (_session != null) _session.Indicators.Changed -= MarkDirty;
            _session = session;
            _session.Indicators.Changed += MarkDirty;
            _selected = null;
            _dirty = true;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_session != null) _session.Indicators.Changed -= MarkDirty; }

        public bool Visible { get { return gameObject.activeSelf; } }
        public void Toggle() { gameObject.SetActive(!gameObject.activeSelf); _dirty = true; }
        public void Show(bool show) { gameObject.SetActive(show); _dirty = true; }
        public void RefreshNow() { _dirty = false; Rebuild(); }
        public void SelectTab(int tab) { _tab = tab; _dirty = true; }

        void Update() { if (_dirty && gameObject.activeSelf) { _dirty = false; Rebuild(); } }

        void Clear() { foreach (var g in _dynamic) Ui.Kill(g); _dynamic.Clear(); }

        Button Btn(string label, Action act, float x, float y, float w, float h, Color? fill = null, int size = 11)
        {
            var b = Ui.Button(_content, label, act, fill ?? Theme.PanelAlt, size);
            Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - h), new Vector2(x + w, -y));
            _dynamic.Add(b.gameObject);
            return b;
        }

        Text Txt(string text, float x, float y, float w, float h, int size, Color color, bool bold = false, bool wrap = false)
        {
            var t = Ui.Label(_content, text, size, color, TextAnchor.UpperLeft, false, bold);
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            Ui.Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - h), new Vector2(x + w, -y));
            _dynamic.Add(t.gameObject);
            return t;
        }

        void Rebuild()
        {
            if (_session == null) return;
            for (int i = 0; i < _tabs.Count; i++) Ui.SetButtonColor(_tabs[i], i == _tab ? Theme.Blue : Theme.PanelAlt);
            Clear();
            float y = 4;
            if (_tab == 0) y = OnChartTab(y); else y = AddTab(y);
            _content.sizeDelta = new Vector2(0, y + 8);
        }

        float OnChartTab(float y)
        {
            var items = _session.Indicators.Items;
            if (items.Count == 0)
            {
                Txt("No indicators yet. Open the Add tab and pick one: moving averages, Bollinger bands, RSI, MACD and more.", 8, y, W - 24, 60, 12, Theme.Dim, false, true);
                return y + 64;
            }
            Txt(items.Count + " of " + Indicators.MaxIndicators + " in use (at most " + Indicators.MaxPanes + " strips below the chart)", 8, y, W - 24, 16, 10, Theme.Dim);
            y += 20;
            foreach (var cfg0 in items.ToList())
            {
                var cfg = cfg0;
                var def = Indicators.Get(cfg.Type);
                bool sel = _selected == cfg.Id;
                Txt(Indicators.Label(cfg) + (def != null && !def.Overlay ? "  (strip)" : ""), 8, y + 6, 170, 18, 12, cfg.Hidden ? Theme.Dim : Theme.Text, true);
                Btn(cfg.Hidden ? "Show" : "Hide", () => _session.Indicators.Update(cfg.Id, c => c.Hidden = !c.Hidden), 176, y + 2, 42, 24, cfg.Hidden ? Theme.Blue : (Color?)null, 10);
                Btn(sel ? "Done" : "Edit", () => { _selected = sel ? null : cfg.Id; _dirty = true; }, 222, y + 2, 42, 24, sel ? Theme.Blue : (Color?)null, 10);
                Btn("x", () => { if (_selected == cfg.Id) _selected = null; _session.Indicators.Remove(cfg.Id); }, 268, y + 2, 26, 24, new Color(0.45f, 0.18f, 0.2f), 11);
                y += 30;
                if (!sel || def == null) continue;
                var p = Indicators.Normalize(def, cfg.Params);
                foreach (var pd in def.Params)
                {
                    var pdef = pd;
                    Txt(pd.Label, 16, y + 5, 130, 16, 11, Theme.Dim);
                    var input = Ui.Input(_content, p[pd.Key].ToString("0.###", CultureInfo.InvariantCulture), 12, InputField.ContentType.DecimalNumber);
                    Ui.Place((RectTransform)input.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -y - 26), new Vector2(230, -y));
                    input.onEndEdit.AddListener(text =>
                    {
                        double v;
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) _session.Indicators.Update(cfg.Id, c => c.Params[pdef.Key] = v);
                        else _dirty = true;
                    });
                    _dynamic.Add(input.gameObject);
                    y += 30;
                }
                Txt("Colours", 16, y + 5, 70, 16, 11, Theme.Dim);
                float x = 90;
                foreach (var ld in def.Lines)
                {
                    var line = ld;
                    string current; if (!cfg.Colors.TryGetValue(line.Key, out current)) current = line.Color;
                    Color col; ColorUtility.TryParseHtmlString(current, out col);
                    var b = Btn("", () => _session.Indicators.Update(cfg.Id, c => c.Colors[line.Key] = NextColor(current)), x, y + 2, 30, 22, col);
                    var tip = Ui.Label(b.transform, line.Label.Length > 5 ? line.Label.Substring(0, 5) : line.Label, 8, Color.black, TextAnchor.MiddleCenter, false, true);
                    x += 34;
                }
                y += 28;
                Txt(def.About, 16, y + 2, W - 40, 44, 10, Theme.Dim, false, true);
                y += 50;
            }
            return y;
        }

        static string NextColor(string current)
        {
            var list = DrawingKinds.Colors;
            int i = Array.FindIndex(list, c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
            return list[(i + 1) % list.Length];
        }

        float AddTab(float y)
        {
            foreach (IndicatorCategory cat in Enum.GetValues(typeof(IndicatorCategory)))
            {
                var defs = Indicators.All.Where(d => d.Category == cat).ToList();
                if (defs.Count == 0) continue;
                Txt(Indicators.CategoryLabel(cat).ToUpperInvariant(), 8, y + 2, 200, 16, 10, Theme.Accent, true);
                y += 22;
                foreach (var def0 in defs)
                {
                    var def = def0;
                    Btn(def.Name, () => { _session.Indicators.Add(def.Id); _tab = 0; _dirty = true; }, 8, y, W - 28, 24, null, 11);
                    Txt(def.About, 10, y + 26, W - 36, 28, 10, Theme.Dim, false, true);
                    y += 58;
                }
            }
            return y;
        }
    }
}
