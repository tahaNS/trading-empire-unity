using System;
using System.Collections.Generic;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>A small chart of one closed trade for the journal: the candles around it, its entry and exit, the stop and the target.</summary>
    public sealed class TradeChart : MaskableGraphic
    {
        public const float Axis = 62f;
        public List<Candle> Candles = new List<Candle>();
        public Position Trade;
        public int First;
        double _lo, _hi;
        float _plotW, _h;

        public override Texture mainTexture { get { return s_WhiteTexture; } }

        public float YOf(double price) { return 6f + (float)((price - _lo) / (_hi - _lo) * (_h - 12f)); }
        float XOf(int i) { return (i + 0.5f) * (_plotW / Mathf.Max(1, Candles.Count)); }

        void Layout(Rect r)
        {
            _h = r.height; _plotW = r.width - Axis;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var c in Candles) { lo = Math.Min(lo, c.Low); hi = Math.Max(hi, c.High); }
            foreach (long? v in new long?[] { Trade.EntryPrice, Trade.Exit != null ? Trade.Exit.Price : (long?)null, Trade.StopLoss, Trade.TakeProfit })
                if (v.HasValue) { lo = Math.Min(lo, v.Value); hi = Math.Max(hi, v.Value); }
            double pad = Math.Max(10, hi - lo) * 0.08;
            _lo = lo - pad; _hi = hi + pad;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Trade == null || Candles.Count == 0) return;
            var r = GetPixelAdjustedRect();
            Layout(new Rect(0, 0, r.width, r.height));
            float ox = r.xMin, oy = r.yMin;
            float bw = _plotW / Candles.Count;
            for (int i = 0; i < Candles.Count; i++)
            {
                var c = Candles[i];
                var col = c.Close >= c.Open ? Theme.Bull : Theme.Bear;
                float x = ox + XOf(i);
                Quad(vh, x - 0.5f, oy + YOf(c.Low), x + 0.5f, oy + YOf(c.High), col);
                float y0 = oy + YOf(Math.Min(c.Open, c.Close)), y1 = oy + YOf(Math.Max(c.Open, c.Close));
                if (y1 - y0 < 1f) y1 = y0 + 1f;
                Quad(vh, x - Mathf.Max(0.5f, bw * 0.35f), y0, x + Mathf.Max(0.5f, bw * 0.35f), y1, col);
            }
            Action<long?, Color, bool> level = (price, color, dashed) =>
            {
                if (!price.HasValue) return;
                float y = oy + YOf(price.Value);
                if (!dashed) { Quad(vh, ox, y - 0.5f, ox + _plotW, y + 0.5f, color); return; }
                for (float x = 0; x < _plotW; x += 9f) Quad(vh, ox + x, y - 0.5f, ox + Mathf.Min(x + 5f, _plotW), y + 0.5f, color);
            };
            level(Trade.TakeProfit, Theme.Bull, true);
            level(Trade.StopLoss, Theme.Bear, true);
            level(Trade.EntryPrice, new Color(0.43f, 0.66f, 1f), false);
            var gold = new Color(0.95f, 0.76f, 0.36f);
            if (Trade.Exit != null) level(Trade.Exit.Price, gold, false);
            // entry and exit markers, joined by a line
            int ei = Trade.EntryCandleIndex - First;
            int xi = (Trade.Exit != null ? Trade.Exit.CandleIndex : Trade.EntryCandleIndex) - First;
            var pe = new Vector2(ox + XOf(ei), oy + YOf(Trade.EntryPrice));
            Dot(vh, pe, 4f, new Color(0.43f, 0.66f, 1f));
            if (Trade.Exit != null)
            {
                var px = new Vector2(ox + XOf(xi), oy + YOf(Trade.Exit.Price));
                Line(vh, pe, px, new Color(gold.r, gold.g, gold.b, 0.5f));
                Dot(vh, px, 4f, gold);
            }
        }

        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert; v.color = c;
            v.position = new Vector3(x0, y0); vh.AddVert(v); v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v); v.position = new Vector3(x1, y0); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        }

        void Dot(VertexHelper vh, Vector2 p, float r, Color c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert; v.color = c;
            v.position = p; vh.AddVert(v);
            for (int k = 0; k <= 12; k++) { float t = k / 12f * Mathf.PI * 2; v.position = p + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r; vh.AddVert(v); }
            for (int k = 1; k <= 12; k++) { vh.AddTriangle(i, i + k, i + k + 1); }
        }

        void Line(VertexHelper vh, Vector2 a, Vector2 b, Color c)
        {
            var d = b - a; if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * 0.7f;
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert; v.color = c;
            v.position = a - n; vh.AddVert(v); v.position = a + n; vh.AddVert(v); v.position = b + n; vh.AddVert(v); v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>Build the chart (and its price labels) into a parent for one trade. Returns the root.</summary>
        public static RectTransform Create(Transform parent, TradingSession session, Position p, Rect place)
        {
            var root = Ui.Rect(parent, "TradeChart");
            root.anchorMin = root.anchorMax = new Vector2(0, 1); root.pivot = new Vector2(0, 1);
            root.anchoredPosition = new Vector2(place.x, -place.y); root.sizeDelta = new Vector2(place.width, place.height);
            var bg = Ui.Box(root, "Bg", new Color(1f, 1f, 1f, 0.03f));
            Ui.Stretch(bg.rectTransform);
            var gr = Ui.Rect(root, "Candles");
            Ui.Stretch(gr);
            gr.gameObject.AddComponent<CanvasRenderer>();
            var chart = gr.gameObject.AddComponent<TradeChart>();
            chart.raycastTarget = false;
            int first;
            chart.Candles = session.TradeCandles(p, 25, out first);
            chart.First = first; chart.Trade = p;
            if (chart.Candles.Count == 0)
            {
                var none = Ui.Label(root, "No candles to show for this trade.", 11, Theme.Dim, TextAnchor.MiddleCenter);
                return root;
            }
            chart.Layout(new Rect(0f, 0f, place.width, place.height));
            int digits = Instruments.DigitsFor(p.Symbol, session.Instrument.Digits);
            Action<long?, string, Color> label = (price, name, col) =>
            {
                if (!price.HasValue) return;
                var t = Ui.Label(root, name + " " + Units.FormatPrice(price.Value, digits), 10, col, TextAnchor.MiddleLeft, true);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0.5f);
                rt.sizeDelta = new Vector2(Axis + 4, 12);
                rt.anchoredPosition = new Vector2(place.width - Axis + 3, chart.YOf(price.Value));
            };
            label(p.TakeProfit, "TP", Theme.Bull);
            label(p.StopLoss, "SL", Theme.Bear);
            label(p.EntryPrice, "In", new Color(0.43f, 0.66f, 1f));
            if (p.Exit != null) label(p.Exit.Price, "Out", new Color(0.95f, 0.76f, 0.36f));
            return root;
        }
    }
}
