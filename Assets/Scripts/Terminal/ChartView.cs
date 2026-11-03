using System;
using System.Collections.Generic;
using System.Globalization;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>The chart area: the candle mesh plus its price / time / level labels, and mouse-wheel zoom.</summary>
    public sealed class ChartView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        CandleGraphic _graphic;
        RectTransform _root;
        readonly List<Text> _priceLabels = new List<Text>();
        readonly List<Text> _timeLabels = new List<Text>();
        readonly List<Text> _lineLabels = new List<Text>();
        Text _empty;
        bool _hover;

        public static ChartView Create(Transform parent)
        {
            var rt = Ui.Rect(parent, "Chart");
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = Theme.Panel; // also lets the pointer-enter events reach the chart
            var view = rt.gameObject.AddComponent<ChartView>();
            view._root = rt;

            var g = Ui.Rect(rt, "Candles");
            Ui.Stretch(g);
            g.gameObject.AddComponent<CanvasRenderer>();
            view._graphic = g.gameObject.AddComponent<CandleGraphic>();
            view._graphic.raycastTarget = false;
            view._empty = Ui.Label(rt, "No data loaded", 18, Theme.Dim, TextAnchor.MiddleCenter);
            return view;
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = false; }

        void Update()
        {
            if (!_hover) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _graphic.VisibleBars = Mathf.Clamp(Mathf.RoundToInt(_graphic.VisibleBars * (wheel > 0 ? 0.88f : 1.14f)), 20, 600);
                _graphic.SetVerticesDirty();
                LayoutLabels();
            }
        }

        public void Refresh(ChartData d)
        {
            _empty.gameObject.SetActive(d.Candles.Count == 0);
            var g = _graphic;
            g.Bars = d.Candles;
            g.Digits = d.Digits;
            g.TfMs = d.TfMs;
            g.Markers = d.Markers;
            g.Lines = BuildLines(d);
            g.SetVerticesDirty();
            LayoutLabels();
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

        void LayoutLabels()
        {
            var g = _graphic;
            g.ComputeLayout();
            foreach (var t in _priceLabels) t.gameObject.SetActive(false);
            foreach (var t in _timeLabels) t.gameObject.SetActive(false);
            foreach (var t in _lineLabels) t.gameObject.SetActive(false);
            if (g.Count == 0) return;

            float axisX = g.Plot.xMax + 6;
            int n = 0;
            foreach (long p in CandleGraphic.GridPrices(g.Lo, g.Hi, 6))
            {
                var t = Pooled(_priceLabels, n++, 12, Theme.Dim);
                Pos(t, axisX, g.YOf(p), 70, 14);
                t.text = Units.FormatPrice(p, g.Digits);
            }

            // time labels
            int every = Mathf.Max(5, (int)(Mathf.Round(g.Count / 6f / 5f) * 5));
            int k = 0;
            for (int i = g.FirstIndex; i < g.Bars.Count; i++)
            {
                if (i % every != 0) continue;
                var t = Pooled(_timeLabels, k++, 12, Theme.Dim);
                t.alignment = TextAnchor.MiddleCenter;
                Pos(t, g.XOfIndex(i) - 40, g.Plot.yMin - 14, 80, 14);
                t.text = FormatTime(g.Bars[i].Time, g.TfMs);
            }

            // level labels (bid, entries, SL/TP, pending): small tags at the right edge of the plot
            int m = 0;
            foreach (var l in g.Lines)
            {
                if (string.IsNullOrEmpty(l.Label) || l.Price < g.Lo || l.Price > g.Hi) continue;
                var t = Pooled(_lineLabels, m++, 11, l.Color);
                t.alignment = TextAnchor.MiddleRight;
                Pos(t, g.Plot.xMax - 190, g.YOf(l.Price) + 8, 188, 14);
                t.text = l.Label;
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
    }
}
