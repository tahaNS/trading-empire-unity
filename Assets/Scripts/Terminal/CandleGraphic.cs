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
        /// <summary>"sl", "tp" or "entry" for the lines of the order being planned (they can be dragged), else null.</summary>
        public string Tag;
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
        /// <summary>Indicators drawn over the candles, and those in strips below them. Values start at bar <see cref="IndOffset"/>.</summary>
        public List<ComputedIndicator> Overlays = new List<ComputedIndicator>();
        public List<ComputedIndicator> Panes = new List<ComputedIndicator>();
        public int IndOffset;
        /// <summary>Keep the current price range (set while dragging a plan line).</summary>
        public bool Freeze;
        /// <summary>Auto scale off: the price range stays where the player put it (Ctrl + wheel changes it).</summary>
        public bool ManualRange;
        public const float PaneH = 86f, PaneGap = 4f;
        /// <summary>Bottom of the plot area including the strips: where the time labels go.</summary>
        public float AxisY;
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
            AxisY = r.yMin + AxisBottom;
            float panesH = Panes.Count * (PaneH + PaneGap);
            Plot = new Rect(r.xMin + LeftMargin, AxisY + panesH, r.width - AxisRight - LeftMargin, r.height - AxisBottom - panesH - 6);
            int total = Bars.Count;
            if (total == 0 || Plot.width < 20 || Plot.height < 20) { Count = 0; return; }

            int visible = Mathf.Clamp(VisibleBars, 20, 600);
            int rightPad = Mathf.Max(3, visible / 12); // empty space on the right so the live candle has room
            Count = Mathf.Min(total, visible - rightPad);
            FirstIndex = total - Count;
            Step = Plot.width / visible;

            // while a plan line is being dragged the price range stays put, so the line does not run away from the pointer
            if ((Freeze || ManualRange) && Hi > Lo) return;
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

            foreach (var ind in Overlays) DrawIndicator(vh, ind, Plot, v => YOf(v), 0, 0);
            for (int k = 0; k < Panes.Count; k++) DrawPane(vh, k);
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

        // ---------- indicators ----------

        public Rect PaneRect(int k)
        {
            return new Rect(Plot.xMin, AxisY + (Panes.Count - 1 - k) * (PaneH + PaneGap), Plot.width, PaneH);
        }

        double ValueAt(ComputedLine l, int bar)
        {
            int i = bar - IndOffset;
            return i < 0 || i >= l.Values.Length ? double.NaN : l.Values[i];
        }

        public double LastValue(ComputedLine l) { for (int i = Bars.Count - 1; i >= FirstIndex; i--) { double v = ValueAt(l, i); if (!double.IsNaN(v)) return v; } return double.NaN; }

        /// <summary>The vertical range a strip shows: fixed for RSI-like indicators, else the visible values with a little room.</summary>
        public void PaneRange(ComputedIndicator ind, out double lo, out double hi)
        {
            if (ind.Def.Range != null) { lo = ind.Def.Range[0]; hi = ind.Def.Range[1]; return; }
            lo = double.MaxValue; hi = double.MinValue;
            foreach (var l in ind.Lines)
            {
                for (int i = FirstIndex; i < Bars.Count; i++) { double v = ValueAt(l, i); if (double.IsNaN(v)) continue; lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                if (l.Kind == LineKind.Hist) { lo = Math.Min(lo, 0); hi = Math.Max(hi, 0); }
            }
            foreach (double gd in ind.Def.Guides) { lo = Math.Min(lo, gd); hi = Math.Max(hi, gd); }
            if (lo > hi) { lo = 0; hi = 1; }
            if (hi - lo < 1e-9) hi = lo + 1;
            double pad = (hi - lo) * 0.08;
            lo -= pad; hi += pad;
        }

        void DrawPane(VertexHelper vh, int k)
        {
            var ind = Panes[k];
            var r = PaneRect(k);
            double lo, hi; PaneRange(ind, out lo, out hi);
            Func<double, float> y = v => r.yMin + (float)((v - lo) / (hi - lo)) * r.height;
            Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, new Color(1f, 1f, 1f, 0.035f));
            Quad(vh, r.xMin, r.yMax - 0.5f, r.xMax, r.yMax + 0.5f, new Color(1f, 1f, 1f, 0.12f));
            foreach (double gd in ind.Def.Guides) Quad(vh, r.xMin, y(gd) - 0.5f, r.xMax, y(gd) + 0.5f, new Color(1f, 1f, 1f, 0.09f));
            DrawIndicator(vh, ind, r, y, lo, hi);
        }

        void DrawIndicator(VertexHelper vh, ComputedIndicator ind, Rect clip, Func<double, float> y, double lo, double hi)
        {
            var def = ind.Def;
            if (def.Fill != null)
            {
                ComputedLine up = ind.Lines.Find(l => l.Key == def.Fill[0]), dn = ind.Lines.Find(l => l.Key == def.Fill[1]);
                if (up != null && dn != null)
                {
                    var fc = Hex(up.Color); fc.a = 0.07f;
                    for (int i = Mathf.Max(FirstIndex, 1); i < Bars.Count; i++)
                    {
                        double a0 = ValueAt(up, i - 1), a1 = ValueAt(up, i), b0 = ValueAt(dn, i - 1), b1 = ValueAt(dn, i);
                        if (double.IsNaN(a0) || double.IsNaN(a1) || double.IsNaN(b0) || double.IsNaN(b1)) continue;
                        float x0 = XOfIndex(i - 1), x1 = XOfIndex(i);
                        Func<double, float> cy = v => Mathf.Clamp(y(v), clip.yMin, clip.yMax);
                        Tri(vh, new Vector2(x0, cy(a0)), new Vector2(x1, cy(a1)), new Vector2(x1, cy(b1)), fc);
                        Tri(vh, new Vector2(x0, cy(a0)), new Vector2(x1, cy(b1)), new Vector2(x0, cy(b0)), fc);
                    }
                }
            }
            float width = ind.Config.Width.HasValue ? (float)ind.Config.Width.Value : 1.5f;
            foreach (var line in ind.Lines)
            {
                var col = Hex(line.Color);
                if (line.Kind == LineKind.Hist)
                {
                    float bw = Mathf.Max(1f, Step * 0.6f);
                    float baseY = Mathf.Clamp(y(Math.Max(0, lo)), clip.yMin, clip.yMax);
                    for (int i = FirstIndex; i < Bars.Count; i++)
                    {
                        double v = ValueAt(line, i);
                        if (double.IsNaN(v)) continue;
                        var c = lo < 0 ? (v >= 0 ? Theme.Bull : Theme.Bear) : col;
                        c.a = 0.75f;
                        float x = XOfIndex(i), yy = Mathf.Clamp(y(v), clip.yMin, clip.yMax);
                        Quad(vh, x - bw / 2, Mathf.Min(baseY, yy), x + bw / 2, Mathf.Max(baseY, yy) + 0.5f, c);
                    }
                }
                else if (line.Kind == LineKind.Dots)
                {
                    for (int i = FirstIndex; i < Bars.Count; i++)
                    {
                        double v = ValueAt(line, i);
                        if (double.IsNaN(v)) continue;
                        float x = XOfIndex(i), yy = y(v);
                        if (yy < clip.yMin || yy > clip.yMax) continue;
                        Quad(vh, x - 1.8f, yy - 1.8f, x + 1.8f, yy + 1.8f, col);
                    }
                }
                else
                {
                    bool have = false; Vector2 prev = Vector2.zero;
                    for (int i = FirstIndex; i < Bars.Count; i++)
                    {
                        double v = ValueAt(line, i);
                        if (double.IsNaN(v)) { have = false; continue; }
                        var cur = new Vector2(XOfIndex(i), y(v));
                        if (have) { var a = prev; var b = cur; if (ClipSegment(ref a, ref b, clip)) Segment(vh, a, b, width, col); }
                        prev = cur; have = true;
                    }
                }
            }
        }

        static Color Hex(string s) { Color c; return ColorUtility.TryParseHtmlString(s, out c) ? c : Color.white; }

        /// <summary>Liang-Barsky: cut a segment to a rectangle (false when it lies entirely outside).</summary>
        static bool ClipSegment(ref Vector2 a, ref Vector2 b, Rect r)
        {
            float t0 = 0, t1 = 1, dx = b.x - a.x, dy = b.y - a.y;
            for (int k = 0; k < 4; k++)
            {
                float p = k == 0 ? -dx : k == 1 ? dx : k == 2 ? -dy : dy;
                float q = k == 0 ? a.x - r.xMin : k == 1 ? r.xMax - a.x : k == 2 ? a.y - r.yMin : r.yMax - a.y;
                if (Mathf.Approximately(p, 0)) { if (q < 0) return false; continue; }
                float t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            var na = new Vector2(a.x + dx * t0, a.y + dy * t0); var nb = new Vector2(a.x + dx * t1, a.y + dy * t1);
            a = na; b = nb;
            return true;
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
