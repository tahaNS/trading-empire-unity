using System;
using System.Collections.Generic;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>One horizontal level on the chart (entry, stop, target, pending order, bid/ask).</summary>
    public struct ChartLine
    {
        public long Price;
        public Color Color;
        public bool Dashed;
        public string Label;
    }

    /// <summary>
    /// Draws candlesticks, grid, price levels and trade markers into a single UI mesh (no textures). It only ever sees
    /// the revealed bars it is given, so the chart cannot look ahead.
    /// </summary>
    public sealed class CandleGraphic : MaskableGraphic
    {
        public const float AxisRight = 78f;
        public const float AxisBottom = 24f;
        /// <summary>Room on the left for the drawing tools.</summary>
        public const float LeftMargin = 42f;

        public IReadOnlyList<Candle> Bars = new List<Candle>();
        public List<ChartLine> Lines = new List<ChartLine>();
        public List<ChartMarker> Markers = new List<ChartMarker>();
        /// <summary>The chart drawings, already in chart-local pixels (built by the chart view).</summary>
        public DrawingScene Scene;
        public int Digits = 5;
        public long TfMs = 60000;
        /// <summary>How many bars fit across the plot (the mouse wheel changes it).</summary>
        public int VisibleBars = 120;

        // Layout of the last draw, for the axis labels.
        public Rect Plot;
        public long Lo, Hi;
        public int FirstIndex, Count;
        public float Step;

        public override Texture mainTexture { get { return s_WhiteTexture; } }

        public float YOf(double price)
        {
            return Plot.yMin + (float)((price - Lo) / (double)(Hi - Lo)) * Plot.height;
        }

        public float XOfIndex(int barIndex)
        {
            return Plot.xMin + (barIndex - FirstIndex + 0.5f) * Step;
        }

        /// <summary>Fractional bar index of a time: between bars it interpolates, beyond the last bar it continues at one bar per timeframe.</summary>
        public double IndexOfTime(long t)
        {
            int n = Bars.Count;
            if (n == 0) return 0;
            long first = Bars[0].Time, last = Bars[n - 1].Time;
            if (t >= last) return n - 1 + (t - last) / (double)TfMs;
            if (t <= first) return (t - first) / (double)TfMs;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Bars[mid].Time <= t) lo = mid; else hi = mid; }
            long span = Bars[hi].Time - Bars[lo].Time;
            return lo + (span > 0 ? (t - Bars[lo].Time) / (double)span : 0);
        }

        public long TimeOfIndex(double idx)
        {
            int n = Bars.Count;
            if (n == 0) return 0;
            if (idx >= n - 1) return Bars[n - 1].Time + (long)Math.Round((idx - (n - 1)) * TfMs);
            if (idx <= 0) return Bars[0].Time + (long)Math.Round(idx * TfMs);
            int lo = (int)Math.Floor(idx);
            double frac = idx - lo;
            return Bars[lo].Time + (long)Math.Round(frac * (Bars[lo + 1].Time - Bars[lo].Time));
        }

        public float XOfTime(long t) { return Plot.xMin + (float)(IndexOfTime(t) - FirstIndex + 0.5) * Step; }
        public long TimeOfX(float x) { return TimeOfIndex((x - Plot.xMin) / Step + FirstIndex - 0.5); }
        public double PriceOfY(float y) { return Lo + (y - Plot.yMin) / Plot.height * (double)(Hi - Lo); }

        /// <summary>Work out the plot rectangle, visible bars and price range (also used to place the axis labels).</summary>
        public void ComputeLayout()
        {
            var r = GetPixelAdjustedRect();
            Plot = new Rect(r.xMin + LeftMargin, r.yMin + AxisBottom, r.width - AxisRight - LeftMargin, r.height - AxisBottom - 6);
            int total = Bars.Count;
            if (total == 0 || Plot.width < 20 || Plot.height < 20) { Count = 0; return; }

            int visible = Mathf.Clamp(VisibleBars, 20, 600);
            int rightPad = Mathf.Max(3, visible / 12); // empty space on the right so the live candle has room
            Count = Mathf.Min(total, visible - rightPad);
            FirstIndex = total - Count;
            Step = Plot.width / visible;

            long lo = long.MaxValue, hi = long.MinValue;
            for (int i = FirstIndex; i < total; i++) { lo = Math.Min(lo, Bars[i].Low); hi = Math.Max(hi, Bars[i].High); }
            foreach (var l in Lines) { if (l.Price > 0) { lo = Math.Min(lo, l.Price); hi = Math.Max(hi, l.Price); } }
            long pad = Math.Max(1, (hi - lo) / 14);
            Lo = lo - pad; Hi = hi + pad;
            if (Hi <= Lo) Hi = Lo + 1;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            ComputeLayout();
            int total = Bars.Count;
            if (Count == 0) return;

            // grid: horizontal price lines at nice steps
            foreach (long p in GridPrices(Lo, Hi, 6)) Quad(vh, Plot.xMin, YOf(p) - 0.5f, Plot.xMax, YOf(p) + 0.5f, Theme.Grid);
            // vertical grid roughly every 20 bars
            int vEvery = Mathf.Max(5, (int)(Mathf.Round(Count / 6f / 5f) * 5));
            for (int i = FirstIndex; i < total; i++)
                if (i % vEvery == 0) Quad(vh, XOfIndex(i) - 0.5f, Plot.yMin, XOfIndex(i) + 0.5f, Plot.yMax, Theme.Grid);

            float bodyW = Mathf.Max(1f, Step * 0.7f);
            for (int i = FirstIndex; i < total; i++)
            {
                var c = Bars[i];
                bool bull = c.Close >= c.Open;
                Color col = bull ? Theme.Bull : Theme.Bear;
                float x = XOfIndex(i);
                Quad(vh, x - 0.5f, YOf(c.Low), x + 0.5f, YOf(c.High), col);
                float y0 = YOf(Math.Min(c.Open, c.Close)), y1 = YOf(Math.Max(c.Open, c.Close));
                if (y1 - y0 < 1f) y1 = y0 + 1f;
                Quad(vh, x - bodyW / 2, y0, x + bodyW / 2, y1, col);
            }

            foreach (var l in Lines) DrawLine(vh, l);
            foreach (var m in Markers) DrawMarker(vh, m);
            if (Scene != null) DrawScene(vh);
        }

        void DrawLine(VertexHelper vh, ChartLine l)
        {
            if (l.Price < Lo || l.Price > Hi) return;
            float y = YOf(l.Price);
            if (!l.Dashed) { Quad(vh, Plot.xMin, y - 0.5f, Plot.xMax, y + 0.5f, l.Color); return; }
            for (float x = Plot.xMin; x < Plot.xMax; x += 9f) Quad(vh, x, y - 0.5f, Mathf.Min(x + 5f, Plot.xMax), y + 0.5f, l.Color);
        }

        void DrawMarker(VertexHelper vh, ChartMarker m)
        {
            // find the bar containing the marker time
            long start = Timeframes.BucketStart(m.Time, TfMs);
            int idx = -1;
            for (int i = Bars.Count - 1; i >= FirstIndex; i--) { if (Bars[i].Time == start) { idx = i; break; } if (Bars[i].Time < start) break; }
            if (idx < 0 || m.Price < Lo || m.Price > Hi) return;
            float x = XOfIndex(idx), y = YOf(m.Price), s = 6f;
            if (m.Kind == MarkerKind.Exit) { Quad(vh, x - s * 0.6f, y - s * 0.6f, x + s * 0.6f, y + s * 0.6f, Theme.Accent); return; }
            bool buy = m.Kind == MarkerKind.Buy;
            Color col = buy ? Theme.Bull : Theme.Bear;
            float dir = buy ? -1f : 1f; // buys point up from below, sells point down from above
            Tri(vh, new Vector2(x, y), new Vector2(x - s, y + dir * s * 1.6f), new Vector2(x + s, y + dir * s * 1.6f), col);
        }

        // ---------- chart drawings ----------

        void DrawScene(VertexHelper vh)
        {
            foreach (var f in Scene.Fills) Fan(vh, f.Points, f.Color);
            foreach (var t in Scene.Triangles) Tri(vh, t.P[0], t.P[1], t.P[2], t.Color);
            foreach (var l in Scene.Lines)
            {
                if (l.Dashed) Dashes(vh, l, 9f, 5f);
                else if (l.Dotted) Dashes(vh, l, 4f, 2f);
                else Segment(vh, l.A, l.B, l.Width, l.Color);
            }
            foreach (var h in Scene.Handles) { Quad(vh, h.At.x - 5, h.At.y - 5, h.At.x + 5, h.At.y + 5, Color.white); Quad(vh, h.At.x - 3.5f, h.At.y - 3.5f, h.At.x + 3.5f, h.At.y + 3.5f, new Color(0.25f, 0.52f, 0.96f)); }
        }

        void Dashes(VertexHelper vh, SceneLine l, float dash, float gap)
        {
            var d = l.B - l.A;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var dir = d / len;
            for (float t = 0; t < len; t += dash + gap) Segment(vh, l.A + dir * t, l.A + dir * Mathf.Min(t + dash, len), l.Width, l.Color);
        }

        /// <summary>A line of a given pixel width, as a rotated quad.</summary>
        void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * (Mathf.Max(width, 1f) / 2f);
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>A filled convex-ish polygon (a triangle fan from its first point).</summary>
        void Fan(VertexHelper vh, Vector2[] pts, Color c)
        {
            if (pts.Length < 3) return;
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            foreach (var p in pts) { v.position = p; vh.AddVert(v); }
            for (int k = 1; k + 1 < pts.Length; k++) { vh.AddTriangle(i, i + k, i + k + 1); vh.AddTriangle(i, i + k + 1, i + k); }
        }

        // ---------- mesh helpers ----------

        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = new Vector3(x0, y0); vh.AddVert(v);
            v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y0); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = a; vh.AddVert(v);
            v.position = b; vh.AddVert(v);
            v.position = c; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>Round grid prices (about <paramref name="target"/> of them) between lo and hi, in points.</summary>
        public static List<long> GridPrices(long lo, long hi, int target)
        {
            var list = new List<long>();
            double raw = (hi - lo) / (double)target;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1))));
            double norm = raw / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            long step = Math.Max(1, (long)(nice * mag));
            for (long p = (lo / step + 1) * step; p < hi; p += step) list.Add(p);
            return list;
        }
    }
}
