using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The chart area: the candle mesh plus its price / time / level labels, mouse-wheel zoom, and the drawing tools (a tool strip on the
    /// left, drawing and editing with the mouse, a properties bar for the selected drawing).
    /// </summary>
    public sealed class ChartView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        sealed class ToolDef { public string Id, Label, Hint; public ToolDef(string id, string label, string hint) { Id = id; Label = label; Hint = hint; } }

        static readonly ToolDef[] Tools =
        {
            new ToolDef(null, "Sel", "Select: click a drawing to select, drag it or its handles. Del deletes."),
            new ToolDef("hline", "H", "Horizontal line: click a price."),
            new ToolDef("vline", "V", "Vertical line: click a time."),
            new ToolDef("hray", "Ray", "Horizontal ray: click where it starts."),
            new ToolDef("trend", "Tr", "Trend line: click two points (or drag)."),
            new ToolDef("channel", "Ch", "Parallel channel: click two points, then the width."),
            new ToolDef("rect", "Rc", "Rectangle: click two corners (or drag)."),
            new ToolDef("ellipse", "El", "Ellipse: click two corners (or drag)."),
            new ToolDef("fib", "Fib", "Fibonacci retracement: click the swing start and end."),
            new ToolDef("fibext", "FX", "Fibonacci extension: click the move (2 points), then where it restarts."),
            new ToolDef("regression", "Rg", "Regression channel: click the first and last bar of the stretch."),
            new ToolDef("pitchfork", "Pf", "Andrews' pitchfork: click three points (the pivot, then a high and a low)."),
            new ToolDef("gannfan", "Gn", "Gann fan: click the origin, then a second point."),
            new ToolDef("fibchannel", "FC", "Fibonacci channel: click two points, then the width."),
            new ToolDef("fibtime", "FT", "Fibonacci time zones: click two points one unit apart."),
            new ToolDef("fibfan", "FF", "Fibonacci speed fan: click the start and end of a move."),
            new ToolDef("fibarcs", "FA", "Fibonacci arcs: click the start and end of a move."),
            new ToolDef("text", "Txt", "Text note: click where it goes, then type its text above."),
            new ToolDef("pricelabel", "$", "Price label: click a price."),
            new ToolDef("up", "Up", "Arrow up marker: click."),
            new ToolDef("down", "Dn", "Arrow down marker: click."),
            new ToolDef("long", "Lg", "Long position box (entry, 20 pip stop, 40 pip target): click the entry."),
            new ToolDef("short", "Sh", "Short position box (entry, 20 pip stop, 40 pip target): click the entry."),
        };

        CandleGraphic _graphic;
        RectTransform _root;
        readonly List<Text> _priceLabels = new List<Text>();
        readonly List<Text> _timeLabels = new List<Text>();
        readonly List<Text> _lineLabels = new List<Text>();
        readonly List<Text> _drawLabels = new List<Text>();
        readonly List<Text> _indLabels = new List<Text>();
        IndicatorPanel _indicatorPanel;
        ObjectPanel _objectPanel;
        Button _indicatorButton, _objectButton;
        Text _empty, _hint;
        bool _hover;

        // drawing state
        TradingSession _session;
        string _tool;
        readonly List<Anchor> _pending = new List<Anchor>();
        bool _dragCreating;
        Vector2 _downPos;
        string _selectedId;
        int _dragHandle = -1;
        bool _moving;
        Anchor _lastAnchor;
        int _colorIndex;
        ChartData _data;
        readonly List<Button> _toolButtons = new List<Button>();
        RectTransform _props;
        Button _lockButton, _hideButton, _planButton;
        InputField _textField;
        Text _widthLabel;
        bool _propsDirty = true;

        /// <summary>Called when a position box should fill the order panel.</summary>
        public Action<Drawing> PlanFromPosition;

        public static ChartView Create(Transform parent)
        {
            var rt = Ui.Rect(parent, "Chart");
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = Theme.Panel; // also lets the pointer events reach the chart
            var view = rt.gameObject.AddComponent<ChartView>();
            view._root = rt;

            var g = Ui.Rect(rt, "Candles");
            Ui.Stretch(g);
            g.gameObject.AddComponent<CanvasRenderer>();
            view._graphic = g.gameObject.AddComponent<CandleGraphic>();
            view._graphic.raycastTarget = false;
            view._empty = Ui.Label(rt, "No data loaded", 18, Theme.Dim, TextAnchor.MiddleCenter);
            view.BuildToolStrip();
            view.BuildProps();
            view._indicatorPanel = IndicatorPanel.Create(rt);
            view._objectPanel = ObjectPanel.Create(rt, view);
            view._objectButton = Ui.Button(rt, "Objects", () => { view._indicatorPanel.Show(false); view._objectPanel.Toggle(); }, Theme.PanelAlt, 11);
            Ui.Place((RectTransform)view._objectButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-132, -26), new Vector2(-84, -4));
            view._indicatorButton = Ui.Button(rt, "Indicators", () => { view._objectPanel.Show(false); view._indicatorPanel.Toggle(); }, Theme.PanelAlt, 11);
            Ui.Place((RectTransform)view._indicatorButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -26), new Vector2(-138, -4));
            return view;
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = false; }

        // ---------------------------------------------------------------- session binding

        /// <summary>Show the drawings of this trading session (called again when a challenge swaps the session).</summary>
        public void Bind(TradingSession session)
        {
            if (_session != null) { _session.Drawings.Changed -= OnDrawingsChanged; _session.Indicators.Changed -= OnIndicatorsChanged; }
            _session = session;
            _session.Drawings.Changed += OnDrawingsChanged;
            _session.Indicators.Changed += OnIndicatorsChanged;
            if (_indicatorPanel != null) _indicatorPanel.Bind(session);
            if (_objectPanel != null) _objectPanel.Bind(session);
            _tool = null;
            _pending.Clear();
            _selectedId = null;
            _propsDirty = true;
        }

        void OnDestroy() { if (_session != null) { _session.Drawings.Changed -= OnDrawingsChanged; _session.Indicators.Changed -= OnIndicatorsChanged; } }
        void OnIndicatorsChanged() { if (_data != null) Refresh(_data); RefreshIndicatorButton(); }
        void RefreshIndicatorButton()
        {
            int n = _session != null ? _session.Indicators.Items.Count : 0;
            Ui.SetButtonLabel(_indicatorButton, n > 0 ? "Indicators (" + n + ")" : "Indicators");
            Ui.SetButtonColor(_indicatorButton, n > 0 ? Theme.Blue : Theme.PanelAlt);
        }
        void OnDrawingsChanged() { _propsDirty = true; }

        // ---------------------------------------------------------------- tool strip and properties bar

        RectTransform _strip;
        float _stripHeight = -1;

        /// <summary>The tool buttons shrink to fit when the chart is short, so none of them is cut off.</summary>
        void LayoutTools()
        {
            if (_strip == null) return;
            float height = _strip.rect.height;
            if (Mathf.Approximately(height, _stripHeight) || height < 60) return;
            _stripHeight = height;
            int n = _toolButtons.Count;
            float gap = 2f, clearRoom = 30f;
            float h = Mathf.Clamp((height - clearRoom - gap) / n - gap, 11f, 19f);
            float y = gap;
            foreach (var b in _toolButtons)
            {
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -y - h), new Vector2(-2, -y));
                y += h + gap;
            }
        }

        void BuildToolStrip()
        {
            var strip = Ui.Rect(_root, "Tools");
            Ui.Place(strip, new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, 28), new Vector2(40, -4));
            Ui.Panel(strip, "Bg", new Color(0.05f, 0.07f, 0.1f, 0.9f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)strip.Find("Bg"));
            _strip = strip;
            float y = 2, h = 19;
            foreach (var t in Tools)
            {
                var tool = t;
                var b = Ui.Button(strip, t.Label, () => SetTool(tool.Id), Theme.PanelAlt, 10);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -y - h), new Vector2(-2, -y));
                _toolButtons.Add(b);
                y += h + 2;
            }
            var del = Ui.Button(strip, "Clr", () => { if (_session != null) _session.Drawings.Clear(); _selectedId = null; }, new Color(0.45f, 0.18f, 0.2f), 10);
            Ui.Place((RectTransform)del.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 2), new Vector2(-2, 26));
            _hint = Ui.Label(_root, "", 11, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(_hint.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -24), new Vector2(-90, -4));
        }

        void BuildProps()
        {
            _props = Ui.Rect(_root, "DrawingProps");
            Ui.Place(_props, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 34), new Vector2(250, 68));
            Ui.Panel(_props, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.97f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_props.Find("Bg"));
            float x = 6;
            for (int i = 0; i < DrawingKinds.Colors.Length; i++)
            {
                string hex = DrawingKinds.Colors[i];
                ColorUtility.TryParseHtmlString(hex, out var c);
                var b = Ui.Button(_props, "", () => EditSelected(d => d.Data.Set("color", hex)), c, 10);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 22, -6));
                x += 26;
            }
            x += 6;
            var minus = Ui.Button(_props, "-", () => EditSelected(d => d.Data.Set("width", Math.Max(1, d.Width - 0.5))), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)minus.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 22, -6));
            _widthLabel = Ui.Label(_props, "1.5", 11, Theme.Text, TextAnchor.MiddleCenter, true);
            Ui.Place(_widthLabel.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x + 22, 0), new Vector2(x + 48, 0));
            var plus = Ui.Button(_props, "+", () => EditSelected(d => d.Data.Set("width", Math.Min(4, d.Width + 0.5))), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)plus.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x + 48, 6), new Vector2(x + 70, -6));
            x += 76;
            var dash = Ui.Button(_props, "Dash", () => EditSelected(d => d.Data.Set("dash", d.Dash == "solid" ? "dashed" : d.Dash == "dashed" ? "dotted" : "solid")), Theme.PanelAlt, 10);
            Ui.Place((RectTransform)dash.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 44, -6));
            x += 48;
            _lockButton = Ui.Button(_props, "Lock", () => EditSelected(d => d.Data.Set("locked", !d.Locked)), Theme.PanelAlt, 10);
            Ui.Place((RectTransform)_lockButton.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 44, -6));
            x += 48;
            _hideButton = Ui.Button(_props, "Hide", () => EditSelected(d => d.Data.Set("hidden", !d.Hidden)), Theme.PanelAlt, 10);
            Ui.Place((RectTransform)_hideButton.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 44, -6));
            x += 48;
            var del = Ui.Button(_props, "Del", DeleteSelected, new Color(0.45f, 0.18f, 0.2f), 10);
            Ui.Place((RectTransform)del.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 6), new Vector2(x + 40, -6));
            _props.gameObject.SetActive(false);

            // a text field for the text of a text note, and a button to send a position box to the order panel
            _textField = Ui.Input(_root, "", 12);
            Ui.Place((RectTransform)_textField.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 72), new Vector2(250, 100));
            _textField.onEndEdit.AddListener(t => EditSelected(d => { if (d.Kind == "text") d.Data.Set("text", t); }));
            _textField.gameObject.SetActive(false);
            _planButton = Ui.Button(_root, "Use in the order panel", () => { var d = SelectedDrawing(); if (d != null && PlanFromPosition != null) PlanFromPosition(d); }, new Color(0.1f, 0.5f, 0.4f), 11);
            Ui.Place((RectTransform)_planButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 72), new Vector2(-70, 100));
            _planButton.gameObject.SetActive(false);
        }

        public string Tool { get { return _tool; } }
        public string SelectedId { get { return _selectedId; } }
        public int PendingCount { get { return _pending.Count; } }

        public void SetTool(string id)
        {
            _tool = id;
            _pending.Clear();
            _dragCreating = false;
            if (id != null) _selectedId = null;
            _propsDirty = true;
        }

        Drawing SelectedDrawing() { return _session != null && _selectedId != null ? _session.Drawings.Items.FirstOrDefault(d => d.Id == _selectedId) : null; }

        void EditSelected(Action<Drawing> change)
        {
            if (_session == null || _selectedId == null) return;
            _session.Drawings.Update(_selectedId, change);
        }

        public void DeleteSelected()
        {
            if (_session == null || _selectedId == null) return;
            _session.Drawings.Remove(_selectedId);
            _selectedId = null;
            _propsDirty = true;
        }

        public void Select(string id) { _selectedId = id; _propsDirty = true; }

        void RefreshProps()
        {
            _propsDirty = false;
            for (int i = 0; i < _toolButtons.Count; i++) Ui.SetButtonColor(_toolButtons[i], Tools[i].Id == _tool ? Theme.Blue : Theme.PanelAlt);
            var tool = Tools.FirstOrDefault(t => t.Id == _tool);
            _hint.text = tool != null ? tool.Hint : "";
            var d = SelectedDrawing();
            _props.gameObject.SetActive(d != null);
            _textField.gameObject.SetActive(d != null && d.Kind == "text");
            _planButton.gameObject.SetActive(d != null && d.Kind == "position");
            if (d == null) return;
            _widthLabel.text = d.Width.ToString("0.0", CultureInfo.InvariantCulture);
            Ui.SetButtonColor(_lockButton, d.Locked ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonColor(_hideButton, d.Hidden ? Theme.Blue : Theme.PanelAlt);
            if (d.Kind == "text" && !_textField.isFocused) _textField.text = d.Text ?? "";
        }

        // ---------------------------------------------------------------- mouse

        bool TopHitIsChart()
        {
            var es = EventSystem.current;
            if (es == null) return true;
            var results = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, results);
            return results.Count > 0 && results[0].gameObject == gameObject;
        }

        bool Typing()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        Vector2 LocalMouse()
        {
            var canvas = GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Input.mousePosition, cam, out var local);
            return local;
        }

        Anchor AnchorAt(Vector2 local) { return new Anchor(_graphic.TimeOfX(local.x), Math.Round(_graphic.PriceOfY(local.y))); }

        void Update()
        {
            LayoutTools();
            if (_propsDirty) RefreshProps();
            if (!_hover) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && TopHitIsChart())
            {
                _graphic.VisibleBars = Mathf.Clamp(Mathf.RoundToInt(_graphic.VisibleBars * (wheel > 0 ? 0.88f : 1.14f)), 20, 600);
                _graphic.SetVerticesDirty();
                LayoutLabels();
            }
            if (_session == null || _graphic.Count == 0) return;
            HandleDrawingInput();
        }

        void HandleDrawingInput()
        {
            if (Typing()) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_pending.Count > 0 || _tool != null) SetTool(null); else Select(null); }
            if (Input.GetKeyDown(KeyCode.V)) SetTool(null);
            if (Input.GetKeyDown(KeyCode.H)) SetTool("hline");
            if (Input.GetKeyDown(KeyCode.T)) SetTool("trend");
            if (Input.GetKeyDown(KeyCode.R)) SetTool("rect");
            if (Input.GetKeyDown(KeyCode.G)) SetTool("fib");
            if (Input.GetKeyDown(KeyCode.X)) SetTool("text");
            if ((Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) && _selectedId != null) DeleteSelected();
            if (Input.GetMouseButtonDown(1)) SetTool(null);

            var local = LocalMouse();
            bool inPlot = _graphic.Plot.Contains(local);
            if (_pending.Count > 0 || _dragHandle >= 0 || _moving) LayoutLabels(); // the preview and the drag follow the pointer

            if (Input.GetMouseButtonDown(0) && inPlot && TopHitIsChart()) PointerDown(local);
            else if (Input.GetMouseButton(0)) PointerDrag(local);
            if (Input.GetMouseButtonUp(0)) PointerUp(local);
        }

        // ---- drawing with the tool

        /// <summary>Handle a press at a chart-local point (also used by tests).</summary>
        public void PointerDown(Vector2 local)
        {
            var a = AnchorAt(local);
            if (_tool != null)
            {
                int need = NeededAnchors(_tool);
                if (need == 0) { Place(_tool, new List<Anchor> { a }); return; }
                _pending.Add(a);
                if (_pending.Count == 1) { _dragCreating = true; _downPos = local; }
                if (_pending.Count == need) Place(_tool, new List<Anchor>(_pending));
                return;
            }
            // the order being planned: grab one of its lines
            if (_session.Draft != null)
                foreach (var l in _graphic.Lines)
                    if (l.Tag != null && Mathf.Abs(local.y - _graphic.YOf(l.Price)) <= 7f) { _dragDraft = l.Tag; _graphic.Freeze = true; return; }
            // cursor: handles first, then a body
            var scene = _graphic.Scene;
            var sel = SelectedDrawing();
            if (sel != null && scene != null && !sel.Locked)
            {
                foreach (var h in scene.Handles)
                    if (h.DrawingId == sel.Id && Vector2.Distance(h.At, local) <= 10f) { _dragHandle = h.Index; return; }
            }
            string hit = scene != null ? DrawingRenderer.HitTest(scene, _session.Drawings.Items, local) : null;
            Select(hit);
            if (hit != null) { var d = SelectedDrawing(); if (d != null && !d.Locked) { _moving = true; _lastAnchor = a; } }
        }

        public void PointerDrag(Vector2 local)
        {
            var a = AnchorAt(local);
            if (_dragDraft != null) { DragDraft(_dragDraft, (long)Math.Round(_graphic.PriceOfY(local.y))); return; }
            if (_dragHandle >= 0 && _selectedId != null) { int idx = _dragHandle; EditSelected(d => DrawingKinds.SetAnchorAt(d, idx, a)); }
            else if (_moving && _selectedId != null)
            {
                long dt = a.Time - _lastAnchor.Time; double dp = a.Price - _lastAnchor.Price;
                _lastAnchor = a;
                if (dt != 0 || dp != 0) EditSelected(d => DrawingKinds.Translate(d, dt, dp));
            }
        }

        public void PointerUp(Vector2 local)
        {
            _dragDraft = null;
            _graphic.Freeze = false;
            _dragHandle = -1;
            _moving = false;
            if (_dragCreating && _tool != null && _pending.Count == 1)
            {
                _dragCreating = false;
                // dragging from the first point to another draws the second point in one go
                if (Vector2.Distance(local, _downPos) > 8f && NeededAnchors(_tool) >= 2)
                {
                    _pending.Add(AnchorAt(local));
                    if (_pending.Count == NeededAnchors(_tool)) Place(_tool, new List<Anchor>(_pending));
                }
            }
            _dragCreating = false;
        }

        static int NeededAnchors(string tool)
        {
            switch (tool)
            {
                case "up": case "down": case "long": case "short": return 0;
                default: return DrawingKinds.AnchorCount.TryGetValue(tool, out var n) ? n : 0;
            }
        }

        string NextColor() { return DrawingKinds.Colors[_colorIndex % DrawingKinds.Colors.Length]; }

        /// <summary>Create the drawing for the tool from its anchors, select it, and go back to the cursor.</summary>
        public Drawing Build(string tool, List<Anchor> anchors, string color)
        {
            var a = anchors[0];
            int pip = _graphic != null && _data != null ? _data.PipPoints : 10;
            long barMs = _graphic != null ? _graphic.TfMs : 60000;
            switch (tool)
            {
                case "hline": return DrawingKinds.HLine(a.Price, color);
                case "vline": return DrawingKinds.VLine(a.Time, color);
                case "text": return DrawingKinds.Text(a, "Note", color);
                case "up": return DrawingKinds.Marker(a, true, color);
                case "down": return DrawingKinds.Marker(a, false, color);
                case "long": return DrawingKinds.Position(true, a, a.Time + 30 * barMs, a.Price + 40 * pip, a.Price - 20 * pip, color);
                case "short": return DrawingKinds.Position(false, a, a.Time + 30 * barMs, a.Price - 40 * pip, a.Price + 20 * pip, color);
                case "trend": return DrawingKinds.Trend(a, anchors[1], color);
                default:
                {
                    var keys = tool == "hray" || tool == "pricelabel" ? new[] { "a" } : new[] { "a", "b", "c" };
                    string kind = tool;
                    var list = new List<(string, Anchor)>();
                    string[] ks = kind == "pricelabel" ? new[] { "at" } : keys;
                    for (int i = 0; i < anchors.Count && i < ks.Length; i++) list.Add((ks[i], anchors[i]));
                    return DrawingKinds.Make(kind, color, list.ToArray());
                }
            }
        }

        public void Place(string tool, List<Anchor> anchors)
        {
            var d = Build(tool, anchors, NextColor());
            string id = _session.Drawings.Add(d);
            _colorIndex++;
            _pending.Clear();
            _tool = null;
            Select(id);
            _propsDirty = true;
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(ChartData d)
        {
            LayoutTools();
            _data = d;
            _empty.gameObject.SetActive(d.Candles.Count == 0);
            var g = _graphic;
            g.Bars = d.Candles;
            g.Digits = d.Digits;
            g.TfMs = d.TfMs;
            g.Markers = d.Markers;
            g.Lines = BuildLines(d);
            AddDraftLines(g.Lines);
            ComputeIndicators(d);
            g.SetVerticesDirty();
            LayoutLabels();
            if (_propsDirty) RefreshProps();
        }

        /// <summary>The order being planned: entry, stop-loss and take-profit lines with their money, which can be dragged.</summary>
        void AddDraftLines(List<ChartLine> lines)
        {
            var v = _session != null ? _session.DescribeDraft() : null;
            if (v == null) return;
            var inst = _session.Instrument;
            Func<long, string> f = p => Units.FormatPrice(p, inst.Digits);
            long entry = v.Resolution.EntryPrice;
            string side = v.Draft.Direction == Direction.Buy ? "BUY" : "SELL";
            string kind = v.Resolution.Kind == EntryClass.Market ? "MARKET" : v.Resolution.Kind == EntryClass.Limit ? "LIMIT" : "STOP";
            string lots = v.Resolution.Lots.HasValue ? " " + Units.FormatLots(v.Resolution.Lots.Value) : "";
            lines.Add(new ChartLine { Price = entry, Color = v.Draft.Direction == Direction.Buy ? Theme.Bull : Theme.Bear, Dashed = false, Tag = "entry", Label = "PLAN " + side + " " + kind + lots + "  " + f(entry) });
            double slPips = Math.Abs(v.Draft.StopLoss - entry) / (double)inst.PipPoints;
            string risk = v.Preview != null && v.Preview.Risk.HasValue ? "  -" + v.Preview.Risk.Value : "";
            lines.Add(new ChartLine { Price = v.Draft.StopLoss, Color = Theme.Bear, Dashed = true, Tag = "sl", Label = "SL " + slPips.ToString("0.0", CultureInfo.InvariantCulture) + " pips" + risk });
            if (v.Draft.TakeProfit.HasValue)
            {
                double tpPips = Math.Abs(v.Draft.TakeProfit.Value - entry) / (double)inst.PipPoints;
                string reward = v.Preview != null && v.Preview.Reward.HasValue ? "  +" + v.Preview.Reward.Value : "";
                string rr = v.Preview != null && v.Preview.RewardRisk.HasValue ? "  R:R " + v.Preview.RewardRisk.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                lines.Add(new ChartLine { Price = v.Draft.TakeProfit.Value, Color = Theme.Bull, Dashed = true, Tag = "tp", Label = "TP " + tpPips.ToString("0.0", CultureInfo.InvariantCulture) + " pips" + reward + rr });
            }
        }

        string _dragDraft;

        void DragDraft(string tag, long price)
        {
            var v = _session.DescribeDraft();
            if (v == null) return;
            long current = v.Resolution.EntryPrice;
            _session.UpdateDraft(d =>
            {
                if (tag == "sl") d.StopLoss = price;
                else if (tag == "tp") d.TakeProfit = price;
                else { long delta = price - current; d.Entry = price; d.StopLoss += delta; if (d.TakeProfit.HasValue) d.TakeProfit += delta; }
            });
        }

        /// <summary>Indicators are computed over the revealed bars only (the last 2000 of them: older bars no longer change the values).</summary>
        void ComputeIndicators(ChartData d)
        {
            var g = _graphic;
            g.Overlays = new List<ComputedIndicator>();
            g.Panes = new List<ComputedIndicator>();
            g.IndOffset = 0;
            if (_session == null || _session.Indicators.Items.Count == 0 || d.Candles.Count == 0) return;
            int start = Math.Max(0, d.Candles.Count - 2000);
            IReadOnlyList<Candle> bars = start == 0 ? d.Candles : d.Candles.Skip(start).ToList();
            g.IndOffset = start;
            foreach (var cfg in _session.Indicators.Items)
            {
                if (cfg.Hidden) continue;
                var c = Indicators.Compute(cfg, bars);
                if (c == null) continue;
                if (c.Def.Overlay) g.Overlays.Add(c);
                else if (g.Panes.Count < Indicators.MaxPanes) g.Panes.Add(c);
            }
        }

        static List<ChartLine> BuildLines(ChartData d)
        {
            var lines = new List<ChartLine>();
            Func<long, string> f = p => Units.FormatPrice(p, d.Digits);
            if (d.Bid.HasValue) lines.Add(new ChartLine { Price = d.Bid.Value, Color = new Color(0.6f, 0.68f, 0.8f, 0.9f), Dashed = true, Label = f(d.Bid.Value) });
            if (d.Ask.HasValue && d.Bid.HasValue && d.Ask.Value != d.Bid.Value) lines.Add(new ChartLine { Price = d.Ask.Value, Color = new Color(0.6f, 0.68f, 0.8f, 0.35f), Dashed = true, Label = "" });
            foreach (var p in d.Open)
            {
                string side = p.Direction == Direction.Buy ? "BUY" : "SELL";
                lines.Add(new ChartLine { Price = p.EntryPrice, Color = Theme.Blue, Dashed = false, Label = side + " " + Units.FormatLots(p.Lots) });
                if (p.StopLoss.HasValue) lines.Add(new ChartLine { Price = p.StopLoss.Value, Color = Theme.Bear, Dashed = true, Label = "SL " + f(p.StopLoss.Value) });
                if (p.TakeProfit.HasValue) lines.Add(new ChartLine { Price = p.TakeProfit.Value, Color = Theme.Bull, Dashed = true, Label = "TP " + f(p.TakeProfit.Value) });
            }
            foreach (var o in d.Pending)
            {
                string kind = (o.Direction == Direction.Buy ? "BUY " : "SELL ") + (o.Kind == PendingKind.Limit ? "LIMIT" : "STOP");
                lines.Add(new ChartLine { Price = o.Price, Color = Theme.Accent, Dashed = true, Label = kind });
                if (o.StopLoss.HasValue) lines.Add(new ChartLine { Price = o.StopLoss.Value, Color = Theme.Bear, Dashed = true, Label = "SL" });
                if (o.TakeProfit.HasValue) lines.Add(new ChartLine { Price = o.TakeProfit.Value, Color = Theme.Bull, Dashed = true, Label = "TP" });
            }
            return lines;
        }

        List<Vector2> ClosesBetween(long t0, long t1)
        {
            var g = _graphic; var list = new List<Vector2>();
            for (int i = 0; i < g.Bars.Count; i++)
            {
                if (g.Bars[i].Time < t0) continue;
                if (g.Bars[i].Time > t1) break;
                list.Add(new Vector2(g.XOfIndex(i), g.YOf(g.Bars[i].Close)));
            }
            return list;
        }

        void BuildScene()
        {
            var g = _graphic;
            if (_session == null || g.Count == 0) { g.Scene = null; return; }
            var map = new ChartMap { X = g.XOfTime, Y = p => g.YOf(p), Plot = g.Plot, Digits = _data != null ? _data.Digits : 5, PipPoints = _data != null ? _data.PipPoints : 10, Closes = ClosesBetween };
            Drawing preview = null;
            if (_tool != null && _pending.Count > 0 && NeededAnchors(_tool) > 0)
            {
                var list = new List<Anchor>(_pending);
                var mouse = AnchorAt(LocalMouse());
                while (list.Count < NeededAnchors(_tool)) list.Add(mouse);
                preview = Build(_tool, list, "#9fb3c8");
                preview.Data.Set("id", "preview");
                preview.Data.Set("dash", "dashed");
            }
            g.Scene = DrawingRenderer.Build(_session.Drawings.Items, map, _selectedId, preview);
        }

        void LayoutLabels()
        {
            var g = _graphic;
            g.ComputeLayout();
            foreach (var t in _priceLabels) t.gameObject.SetActive(false);
            foreach (var t in _timeLabels) t.gameObject.SetActive(false);
            foreach (var t in _lineLabels) t.gameObject.SetActive(false);
            foreach (var t in _drawLabels) t.gameObject.SetActive(false);
            foreach (var t in _indLabels) t.gameObject.SetActive(false);
            BuildScene();
            g.SetVerticesDirty();
            if (g.Count == 0) return;

            float axisX = g.Plot.xMax + 6;
            int n = 0;
            foreach (long p in CandleGraphic.GridPrices(g.Lo, g.Hi, 6))
            {
                var t = Pooled(_priceLabels, n++, 12, Theme.Dim);
                Pos(t, axisX, g.YOf(p), 70, 14);
                t.text = Units.FormatPrice(p, g.Digits);
            }

            int every = Mathf.Max(5, (int)(Mathf.Round(g.Count / 6f / 5f) * 5));
            int k = 0;
            for (int i = g.FirstIndex; i < g.Bars.Count; i++)
            {
                if (i % every != 0) continue;
                var t = Pooled(_timeLabels, k++, 12, Theme.Dim);
                t.alignment = TextAnchor.MiddleCenter;
                Pos(t, g.XOfIndex(i) - 40, g.AxisY - 14, 80, 14);
                t.text = FormatTime(g.Bars[i].Time, g.TfMs);
            }

            int m = 0;
            foreach (var l in g.Lines)
            {
                if (string.IsNullOrEmpty(l.Label) || l.Price < g.Lo || l.Price > g.Hi) continue;
                var t = Pooled(_lineLabels, m++, 11, l.Color);
                t.alignment = TextAnchor.MiddleRight;
                Pos(t, g.Plot.xMax - 340, g.YOf(l.Price) + 8, 338, 14);
                t.text = l.Label;
            }

            LayoutIndicatorLabels(g);

            if (g.Scene != null)
            {
                int dl = 0;
                foreach (var lb in g.Scene.Labels)
                {
                    if (!g.Plot.Contains(lb.At)) continue;
                    var t = Pooled(_drawLabels, dl++, 11, lb.Color);
                    t.alignment = TextAnchor.MiddleLeft;
                    Pos(t, lb.At.x, lb.At.y, Mathf.Max(60, lb.Text.Length * 7 + 8), 14);
                    t.text = lb.Text;
                }
            }
        }

        static string Num(double v, IndicatorDef def, int pipPoints)
        {
            if (double.IsNaN(v)) return "-";
            if (def.Id == "macd" || def.Id == "atr") return (v / Math.Max(1, pipPoints)).ToString("0.0", CultureInfo.InvariantCulture);
            if (def.Id == "volume") return v >= 1000 ? (v / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k" : v.ToString("0", CultureInfo.InvariantCulture);
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        void LayoutIndicatorLabels(CandleGraphic g)
        {
            int n = 0;
            int pip = _data != null ? _data.PipPoints : 10;
            float y = g.Plot.yMax - 36;
            foreach (var ind in g.Overlays)
            {
                var t = Pooled(_indLabels, n++, 11, Theme.Text);
                var sb = new System.Text.StringBuilder(Indicators.Label(ind.Config));
                foreach (var l in ind.Lines)
                {
                    double v = g.LastValue(l);
                    sb.Append("  <color=").Append(l.Color).Append(">").Append(double.IsNaN(v) ? "-" : Units.FormatPrice((long)Math.Round(v), g.Digits)).Append("</color>");
                }
                t.text = sb.ToString();
                Pos(t, g.Plot.xMin + 8, y, 380, 14);
                y -= 15;
            }
            for (int k = 0; k < g.Panes.Count; k++)
            {
                var ind = g.Panes[k];
                var r = g.PaneRect(k);
                var t = Pooled(_indLabels, n++, 11, Theme.Text);
                var sb = new System.Text.StringBuilder(Indicators.Label(ind.Config));
                foreach (var l in ind.Lines) sb.Append("  <color=").Append(l.Color).Append(">").Append(Num(g.LastValue(l), ind.Def, pip)).Append("</color>");
                t.text = sb.ToString();
                Pos(t, r.xMin + 6, r.yMax - 8, 380, 14);
                double lo, hi; g.PaneRange(ind, out lo, out hi);
                foreach (double gd in ind.Def.Guides)
                {
                    var gt = Pooled(_indLabels, n++, 10, Theme.Dim);
                    gt.text = gd.ToString("0.#", CultureInfo.InvariantCulture);
                    Pos(gt, g.Plot.xMax + 6, r.yMin + (float)((gd - lo) / (hi - lo)) * r.height, 60, 12);
                }
            }
        }

        static string FormatTime(long ms, long tfMs)
        {
            var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            return tfMs >= 86400000L ? d.ToString("d MMM yy", CultureInfo.InvariantCulture) : d.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        }

        Text Pooled(List<Text> pool, int i, int size, Color color)
        {
            Text t;
            if (i < pool.Count) t = pool[i];
            else
            {
                t = Ui.Label(_root, "", size, color, TextAnchor.MiddleLeft, true);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0, 0);
                pool.Add(t);
                if (_indicatorPanel != null) t.transform.SetSiblingIndex(Mathf.Min(_indicatorPanel.transform.GetSiblingIndex(), _objectPanel != null ? _objectPanel.transform.GetSiblingIndex() : int.MaxValue)); // keep labels under the indicator window
            }
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.gameObject.SetActive(true);
            return t;
        }

        /// <summary>Place by bottom-left corner in the chart's local space (its pivot is the centre).</summary>
        static void Pos(Text t, float x, float y, float w, float h)
        {
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y - h / 2);
        }

        // for tests and tools
        public CandleGraphic Graphic { get { return _graphic; } }
        public IndicatorPanel IndicatorWindow { get { return _indicatorPanel; } }
        public ObjectPanel ObjectWindow { get { return _objectPanel; } }
    }
}
