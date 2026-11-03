using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;

namespace TradingEmpire.Terminal
{
    public struct SceneLine { public Vector2 A, B; public Color Color; public float Width; public bool Dashed, Dotted; public string DrawingId; }
    public struct SceneFill { public Vector2[] Points; public Color Color; public string DrawingId; }
    public struct SceneLabel { public Vector2 At; public string Text; public Color Color; public TextAnchor Anchor; public string DrawingId; }
    public struct SceneTri { public Vector2[] P; public Color Color; public string DrawingId; }
    public struct SceneHandle { public Vector2 At; public string DrawingId; public int Index; }

    /// <summary>Everything the chart has to draw for the drawings, in chart-local pixels. Built from the drawings and the chart's current view.</summary>
    public sealed class DrawingScene
    {
        public readonly List<SceneLine> Lines = new List<SceneLine>();
        public readonly List<SceneFill> Fills = new List<SceneFill>();
        public readonly List<SceneLabel> Labels = new List<SceneLabel>();
        public readonly List<SceneHandle> Handles = new List<SceneHandle>();
        public readonly List<SceneTri> Triangles = new List<SceneTri>();
    }

    /// <summary>How a time or a price maps to the chart (the chart provides it).</summary>
    public sealed class ChartMap
    {
        public Func<long, float> X;
        public Func<double, float> Y;
        public Rect Plot;
        public int Digits = 5;
        public int PipPoints = 10;
        /// <summary>The chart pixel x and the close of every revealed bar between two times (for the regression channel).</summary>
        public Func<long, long, List<Vector2>> Closes;
    }

    /// <summary>Turns drawings into lines, fills, labels and handles. Kinds that cannot be drawn yet show as a dotted outline with their name, so nothing is invisible.</summary>
    public static class DrawingRenderer
    {
        static Color Parse(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString(hex ?? "#6ea8ff", out var c)) c = new Color(0.43f, 0.66f, 1f);
            c.a = alpha;
            return c;
        }

        public static DrawingScene Build(IReadOnlyList<Drawing> drawings, ChartMap m, string selectedId, Drawing preview = null)
        {
            var s = new DrawingScene();
            foreach (var d in drawings)
            {
                if (d.Hidden) continue;
                Add(s, d, m, d.Id == selectedId);
            }
            if (preview != null) Add(s, preview, m, false);
            // handles of the selected drawing
            if (selectedId != null)
            {
                var sel = drawings.FirstOrDefault(d => d.Id == selectedId);
                if (sel != null && !sel.Hidden) AddHandles(s, sel, m);
            }
            return s;
        }

        static Vector2 P(ChartMap m, Anchor a) { return new Vector2(m.X(a.Time), m.Y(a.Price)); }

        static void Line(DrawingScene s, Vector2 a, Vector2 b, Drawing d, bool selected, float alpha = 1f, bool? dashed = null, bool? dotted = null, string colorOverride = null)
        {
            s.Lines.Add(new SceneLine
            {
                A = a, B = b, Color = Parse(colorOverride ?? d.Color, alpha), Width = (float)d.Width + (selected ? 0.7f : 0f), DrawingId = d.Id,
                Dashed = dashed ?? d.Dash == "dashed", Dotted = dotted ?? d.Dash == "dotted",
            });
        }

        static string Price(ChartMap m, double p) { return Units.FormatPrice((long)Math.Round(p), m.Digits); }

        /// <summary>Clip the infinite line through a and b to the plot, from a toward b (and the other way when both = true).</summary>
        static (Vector2, Vector2) Extend(Vector2 a, Vector2 b, Rect plot, bool right, bool left)
        {
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) return (a, b);
            float big = plot.width + plot.height + 2000f;
            var from = left ? a - dir.normalized * big : a;
            var to = right ? a + dir.normalized * big : b;
            return (ClipToPlot(from, to, plot), ClipToPlot(to, from, plot));
        }

        // returns the point of segment p->q moved onto the plot border if p is outside
        static Vector2 ClipToPlot(Vector2 p, Vector2 q, Rect plot)
        {
            if (plot.Contains(p)) return p;
            float t0 = 0, t1 = 1;
            var d = q - p;
            // Liang-Barsky
            float[] pp2 = { -d.x, d.x, -d.y, d.y };
            float[] qq = { p.x - plot.xMin, plot.xMax - p.x, p.y - plot.yMin, plot.yMax - p.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(pp2[i]) < 1e-6f) { if (qq[i] < 0) return p; continue; }
                float r = qq[i] / pp2[i];
                if (pp2[i] < 0) { if (r > t1) return p; if (r > t0) t0 = r; }
                else { if (r < t0) return p; if (r < t1) t1 = r; }
            }
            return p + d * t0;
        }

        static readonly (float, string)[] GannRatios = { (8f, "8x1"), (4f, "4x1"), (3f, "3x1"), (2f, "2x1"), (1f, "1x1"), (1f / 2, "1x2"), (1f / 3, "1x3"), (1f / 4, "1x4"), (1f / 8, "1x8") };

        /// <summary>The point where a ray from p along dir leaves the plot.</summary>
        static Vector2 RayEnd(Vector2 p, Vector2 dir, Rect plot)
        {
            if (dir.sqrMagnitude < 1e-9f) return p;
            var far = p + dir.normalized * (plot.width + plot.height + 2000f);
            return ClipToPlot(far, p, plot);
        }

        static void Add(DrawingScene s, Drawing d, ChartMap m, bool selected)
        {
            var plot = m.Plot;
            string kind = d.Kind;
            switch (kind)
            {
                case "hline":
                {
                    float y = m.Y(d.Num("price") ?? 0);
                    Line(s, new Vector2(plot.xMin, y), new Vector2(plot.xMax, y), d, selected);
                    s.Labels.Add(new SceneLabel { At = new Vector2(plot.xMin + 6, y + 8), Text = Price(m, d.Num("price") ?? 0), Color = Parse(d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
                case "vline":
                {
                    float x = m.X((long)(d.Num("time") ?? 0));
                    Line(s, new Vector2(x, plot.yMin), new Vector2(x, plot.yMax), d, selected);
                    break;
                }
                case "hray":
                {
                    var a = d.GetAnchor("a").Value;
                    var pa = P(m, a);
                    Line(s, pa, new Vector2(plot.xMax, pa.y), d, selected);
                    s.Labels.Add(new SceneLabel { At = new Vector2(pa.x + 6, pa.y + 8), Text = Price(m, a.Price), Color = Parse(d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
                case "trend":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    string ext = d.Str("extend") ?? (d.Flag("ray") ? "right" : "none");
                    var (p0, p1) = Extend(a, b, plot, ext == "right" || ext == "both", ext == "both");
                    if (ext == "none") { p0 = a; p1 = b; }
                    Line(s, p0, p1, d, selected);
                    if (d.Flag("arrow") && (b - a).sqrMagnitude > 1)
                    {
                        var dir = (b - a).normalized; var n = new Vector2(-dir.y, dir.x);
                        s.Triangles.Add(new SceneTri { P = new[] { b, b - dir * 12 + n * 5, b - dir * 12 - n * 5 }, Color = Parse(d.Color), DrawingId = d.Id });
                    }
                    break;
                }
                case "channel":
                {
                    var A = d.GetAnchor("a").Value; var B = d.GetAnchor("b").Value; var C = d.GetAnchor("c").Value;
                    var a = P(m, A); var b = P(m, B);
                    double off = DrawingKinds.ChannelOffset(A, B, C);
                    var a2 = P(m, new Anchor(A.Time, A.Price + off)); var b2 = P(m, new Anchor(B.Time, B.Price + off));
                    Line(s, a, b, d, selected); Line(s, a2, b2, d, selected);
                    Line(s, (a + a2) / 2, (b + b2) / 2, d, selected, 0.5f, false, true);
                    if (d.Flag("fill")) s.Fills.Add(new SceneFill { Points = new[] { a, b, b2, a2 }, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    break;
                }
                case "rect":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    var c1 = new Vector2(a.x, b.y); var c2 = new Vector2(b.x, a.y);
                    Line(s, a, c1, d, selected); Line(s, c1, b, d, selected); Line(s, b, c2, d, selected); Line(s, c2, a, d, selected);
                    if (d.Opt("fill")) s.Fills.Add(new SceneFill { Points = new[] { a, c1, b, c2 }, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    break;
                }
                case "ellipse":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    var center = (a + b) / 2; float rx = Mathf.Abs(b.x - a.x) / 2, ry = Mathf.Abs(b.y - a.y) / 2;
                    var pts = new Vector2[48];
                    for (int i = 0; i < 48; i++) { float t = i / 48f * Mathf.PI * 2; pts[i] = center + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry); }
                    for (int i = 0; i < 48; i++) Line(s, pts[i], pts[(i + 1) % 48], d, selected);
                    if (d.Flag("fill")) s.Fills.Add(new SceneFill { Points = pts, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    break;
                }
                case "triangle":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value); var c = P(m, d.GetAnchor("c").Value);
                    Line(s, a, b, d, selected); Line(s, b, c, d, selected); Line(s, c, a, d, selected);
                    if (d.Flag("fill")) s.Fills.Add(new SceneFill { Points = new[] { a, b, c }, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    break;
                }
                case "fib":
                case "fibext":
                {
                    var A = d.GetAnchor("a").Value; var B = d.GetAnchor("b").Value;
                    bool ext = kind == "fibext";
                    var C = ext ? d.GetAnchor("c").Value : B;
                    long left = ext ? C.Time : Math.Min(A.Time, B.Time);
                    float x0 = m.X(left);
                    float x1 = d.Opt("extend") ? plot.xMax : m.X(Math.Max(A.Time, B.Time));
                    float prevY = float.NaN;
                    foreach (var fl in DrawingKinds.FibLevelsOn(d))
                    {
                        double lv = fl.V; string lc = fl.Color;
                        double price = ext ? DrawingKinds.FibExtPrice(A.Price, B.Price, C.Price, lv) : DrawingKinds.FibPrice(A.Price, B.Price, lv);
                        float y = m.Y(price);
                        Line(s, new Vector2(x0, y), new Vector2(x1, y), d, selected, lv == 0 || lv == 1 ? 0.9f : 0.6f, null, null, lc);
                        string text = (d.Opt("showLevel") ? lv.ToString("0.###", CultureInfo.InvariantCulture) : "") + (d.Opt("showLevel") && d.Opt("showPrice") ? "  " : "") + (d.Opt("showPrice") ? Price(m, price) : "");
                        if (text.Length > 0) s.Labels.Add(new SceneLabel { At = new Vector2(x0 + 4, y + 8), Text = text, Color = Parse(lc ?? d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                        if (!float.IsNaN(prevY) && (d.Opt("fill")))
                            s.Fills.Add(new SceneFill { Points = new[] { new Vector2(x0, prevY), new Vector2(x1, prevY), new Vector2(x1, y), new Vector2(x0, y) }, Color = Parse(d.Color, 0.05f), DrawingId = d.Id });
                        prevY = y;
                    }
                    // the measured move itself
                    Line(s, P(m, A), P(m, B), d, selected, 0.6f, false, true);
                    if (ext) Line(s, P(m, B), P(m, C), d, selected, 0.6f, false, true);
                    break;
                }
                case "text":
                {
                    var at = P(m, d.GetAnchor("at").Value);
                    s.Labels.Add(new SceneLabel { At = at, Text = d.Text ?? "", Color = Parse(d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
                case "pricelabel":
                {
                    var a = d.GetAnchor("at").Value; var at = P(m, a);
                    Line(s, at, at + new Vector2(60, 0), d, selected, 0.8f);
                    s.Labels.Add(new SceneLabel { At = at + new Vector2(4, 9), Text = Price(m, a.Price), Color = Parse(d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
                case "marker":
                {
                    var at = P(m, d.GetAnchor("at").Value);
                    bool up = d.Str("dir") != "down";
                    float k = up ? 1 : -1;
                    s.Triangles.Add(new SceneTri { P = new[] { at + new Vector2(0, k * 4), at + new Vector2(-8, k * 18), at + new Vector2(8, k * 18) }, Color = Parse(d.Color), DrawingId = d.Id });
                    s.Lines.Add(new SceneLine { A = at, B = at + new Vector2(0, k * 4), Color = Parse(d.Color), Width = 1, DrawingId = d.Id });
                    break;
                }
                case "position":
                {
                    var A = d.GetAnchor("a").Value; var B = d.GetAnchor("b").Value; var C = d.GetAnchor("c").Value;
                    bool isLong = d.Str("side") != "short";
                    float xl = m.X(A.Time), xr = m.X(B.Time);
                    float yEntry = m.Y(A.Price), yTp = m.Y(B.Price), ySl = m.Y(C.Price);
                    var green = new Color(0.15f, 0.76f, 0.58f); var red = new Color(0.93f, 0.32f, 0.36f);
                    s.Fills.Add(new SceneFill { Points = new[] { new Vector2(xl, yEntry), new Vector2(xr, yEntry), new Vector2(xr, yTp), new Vector2(xl, yTp) }, Color = new Color(green.r, green.g, green.b, 0.20f), DrawingId = d.Id });
                    s.Fills.Add(new SceneFill { Points = new[] { new Vector2(xl, yEntry), new Vector2(xr, yEntry), new Vector2(xr, ySl), new Vector2(xl, ySl) }, Color = new Color(red.r, red.g, red.b, 0.20f), DrawingId = d.Id });
                    Line(s, new Vector2(xl, yEntry), new Vector2(xr, yEntry), d, selected);
                    Line(s, new Vector2(xl, yTp), new Vector2(xr, yTp), d, selected, 0.9f, true);
                    Line(s, new Vector2(xl, ySl), new Vector2(xr, ySl), d, selected, 0.9f, true);
                    double tpPips = Math.Abs(B.Price - A.Price) / m.PipPoints, slPips = Math.Abs(C.Price - A.Price) / m.PipPoints;
                    double rr = slPips > 0 ? tpPips / slPips : 0;
                    s.Labels.Add(new SceneLabel { At = new Vector2(xl + 6, yEntry + (isLong ? 10 : -10)), Text = (isLong ? "LONG" : "SHORT") + "   R:R " + rr.ToString("0.0", CultureInfo.InvariantCulture), Color = Color.white, Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    s.Labels.Add(new SceneLabel { At = new Vector2(xl + 6, yTp + (yTp > yEntry ? -10 : 10)), Text = "TP " + tpPips.ToString("0.0", CultureInfo.InvariantCulture) + " pips   " + Price(m, B.Price), Color = green, Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    s.Labels.Add(new SceneLabel { At = new Vector2(xl + 6, ySl + (ySl > yEntry ? -10 : 10)), Text = "SL " + slPips.ToString("0.0", CultureInfo.InvariantCulture) + " pips   " + Price(m, C.Price), Color = red, Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
                case "regression":
                {
                    var A = d.GetAnchor("a").Value; var B = d.GetAnchor("b").Value;
                    var a = P(m, A); var b = P(m, B);
                    var pts = m.Closes != null ? m.Closes(Math.Min(A.Time, B.Time), Math.Max(A.Time, B.Time)) : new List<Vector2>();
                    if (pts.Count < 3) { Line(s, a, b, d, selected, 0.7f, true, false); break; }
                    // least squares through the closes (x in pixels, y in pixels: the line and its deviation are the same in either unit)
                    int n = pts.Count;
                    float mx = pts.Average(q => q.x), my = pts.Average(q => q.y), sxx = 0, sxy = 0;
                    foreach (var q in pts) { sxx += (q.x - mx) * (q.x - mx); sxy += (q.x - mx) * (q.y - my); }
                    float slope = sxx == 0 ? 0 : sxy / sxx;
                    Func<float, float> at = x => my + slope * (x - mx);
                    float sd = Mathf.Sqrt(pts.Sum(q => (q.y - at(q.x)) * (q.y - at(q.x))) / n);
                    float devs = (float)(d.Num("devs") ?? 2);
                    float x0 = pts[0].x, x1 = pts[n - 1].x;
                    Vector2 U0 = new Vector2(x0, at(x0) + devs * sd), U1 = new Vector2(x1, at(x1) + devs * sd), L0 = new Vector2(x0, at(x0) - devs * sd), L1 = new Vector2(x1, at(x1) - devs * sd);
                    if (d.Opt("fill")) s.Fills.Add(new SceneFill { Points = new[] { U0, U1, L1, L0 }, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    Line(s, new Vector2(x0, at(x0)), new Vector2(x1, at(x1)), d, selected, 0.8f, true, false);
                    Line(s, U0, U1, d, selected); Line(s, L0, L1, d, selected);
                    break;
                }
                case "pitchfork":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value); var c = P(m, d.GetAnchor("c").Value);
                    var mid = (b + c) / 2; var dir = mid - a;
                    if (dir.sqrMagnitude < 1e-6f) { Line(s, a, b, d, selected); break; }
                    var eb = RayEnd(b, dir, plot); var ec = RayEnd(c, dir, plot);
                    s.Fills.Add(new SceneFill { Points = new[] { b, eb, ec, c }, Color = Parse(d.Color, 0.12f), DrawingId = d.Id });
                    s.Lines.Add(new SceneLine { A = a, B = RayEnd(a, dir, plot), Color = Parse(d.Color), Width = (float)d.Width * 1.3f + (selected ? 0.7f : 0f), DrawingId = d.Id, Dashed = d.Dash == "dashed", Dotted = d.Dash == "dotted" });
                    Line(s, b, eb, d, selected); Line(s, c, ec, d, selected);
                    Line(s, b, c, d, selected, 0.6f, false, true);
                    break;
                }
                case "gannfan":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    float dx = b.x - a.x, dy = b.y - a.y;
                    if (Mathf.Abs(dx) < 1e-3f) { Line(s, a, b, d, selected); break; }
                    foreach (var (r, label) in GannRatios)
                    {
                        var end = RayEnd(a, new Vector2(dx, dy * r), plot);
                        Line(s, a, end, d, selected, r == 1 ? 1f : 0.55f);
                        float lx = Mathf.Clamp(end.x, plot.xMin + 24, plot.xMax - 24);
                        s.Labels.Add(new SceneLabel { At = new Vector2(lx - 22, a.y + dy * r * (lx - a.x) / dx + 8), Text = label, Color = Parse(d.Color, 0.85f), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    }
                    break;
                }
                case "fibchannel":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value); var c = P(m, d.GetAnchor("c").Value);
                    float k = Mathf.Approximately(b.x, a.x) ? 0 : (b.y - a.y) / (b.x - a.x);
                    float off = c.y - (a.y + k * (c.x - a.x));
                    Line(s, a, b, d, selected, 0.7f, true, false);
                    bool extend = d.Opt("extend");
                    var lines = new List<(Vector2 a, Vector2 b)>();
                    foreach (var fl in DrawingKinds.FibLevelsOn(d))
                    {
                        double lv = fl.V; string lc = fl.Color;
                        var p0 = new Vector2(a.x, a.y + off * (float)lv); var p1 = new Vector2(b.x, b.y + off * (float)lv);
                        var (e0, e1) = extend ? Extend(p0, p1, plot, true, false) : (p0, p1);
                        Line(s, e0, e1, d, selected, lv == 0 || lv == 1 ? 0.95f : 0.65f, null, null, lc);
                        if (d.Opt("showLevel")) s.Labels.Add(new SceneLabel { At = new Vector2(Mathf.Min(e1.x, plot.xMax) - 40, e1.y + 8), Text = lv.ToString("0.###", CultureInfo.InvariantCulture), Color = Parse(lc ?? d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                        lines.Add((e0, e1));
                    }
                    if (d.Opt("fill"))
                        for (int i = 0; i + 1 < lines.Count; i += 2) s.Fills.Add(new SceneFill { Points = new[] { lines[i].a, lines[i].b, lines[i + 1].b, lines[i + 1].a }, Color = Parse(d.Color, 0.06f), DrawingId = d.Id });
                    break;
                }
                case "fibtime":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    float unit = b.x - a.x;
                    Line(s, a, b, d, selected, 0.5f, true, false);
                    if (Mathf.Abs(unit) < 1f) break;
                    foreach (var fl in DrawingKinds.FibLevelsOn(d))
                    {
                        double lv = fl.V; string lc = fl.Color;
                        float x = Mathf.Round(a.x + unit * (float)lv) + 0.5f;
                        if (x < plot.xMin - 2000 || x > plot.xMax + 2000) continue;
                        Line(s, new Vector2(x, plot.yMin), new Vector2(x, plot.yMax), d, selected, lv == 0 || lv == 1 ? 0.9f : 0.55f, null, null, lc);
                        if (d.Opt("showLevel")) s.Labels.Add(new SceneLabel { At = new Vector2(x + 4, plot.yMax - 10), Text = lv.ToString("0.###", CultureInfo.InvariantCulture), Color = Parse(lc ?? d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    }
                    break;
                }
                case "fibfan":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    float dx = b.x - a.x;
                    if (Mathf.Abs(dx) < 1e-3f) { Line(s, a, b, d, selected); break; }
                    Line(s, new Vector2(b.x, a.y), b, d, selected, 0.5f, false, true);
                    var ends = new List<(double lv, Vector2 end, string color)>();
                    foreach (var fl in DrawingKinds.FibLevelsOn(d))
                        ends.Add((fl.V, RayEnd(a, new Vector2(dx, (b.y - a.y) * (float)fl.V), plot), fl.Color));
                    for (int i = 0; i < ends.Count; i++)
                    {
                        if ((d.Opt("fill")) && i + 1 < ends.Count && i % 2 == 0)
                            s.Fills.Add(new SceneFill { Points = new[] { a, ends[i].end, ends[i + 1].end }, Color = Parse(d.Color, 0.07f), DrawingId = d.Id });
                        Line(s, a, ends[i].end, d, selected, ends[i].lv == 1 ? 1f : 0.65f, null, null, ends[i].color);
                        var e = ends[i].end;
                        float lx = Mathf.Clamp(e.x, plot.xMin + 24, plot.xMax - 4);
                        if (d.Opt("showLevel")) s.Labels.Add(new SceneLabel { At = new Vector2(lx - 30, a.y + (e.y - a.y) / (Mathf.Abs(e.x - a.x) < 1e-3f ? 1f : e.x - a.x) * (lx - a.x) + 8), Text = ends[i].lv.ToString("0.###", CultureInfo.InvariantCulture), Color = Parse(ends[i].color ?? d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    }
                    break;
                }
                case "fibarcs":
                {
                    var a = P(m, d.GetAnchor("a").Value); var b = P(m, d.GetAnchor("b").Value);
                    float R = Vector2.Distance(a, b), ang = Mathf.Atan2(b.y - a.y, b.x - a.x);
                    Line(s, a, b, d, selected, 0.6f, true, false);
                    if (R < 1f) break;
                    foreach (var fl in DrawingKinds.FibLevelsOn(d))
                    {
                        double lv = fl.V; string lc = fl.Color;
                        float r = R * (float)lv;
                        float alpha = lv == 1 ? 0.95f : 0.65f;
                        const int Steps = 36;
                        Vector2 prev = Vector2.zero;
                        for (int i = 0; i <= Steps; i++)
                        {
                            float t = ang - Mathf.PI / 2 + Mathf.PI * i / Steps;
                            var q = b + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
                            if (i > 0) Line(s, prev, q, d, selected, alpha, null, null, lc);
                            prev = q;
                        }
                        if (d.Opt("showLevel")) s.Labels.Add(new SceneLabel { At = b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r + new Vector2(4, 8), Text = lv.ToString("0.###", CultureInfo.InvariantCulture), Color = Parse(lc ?? d.Color), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    }
                    break;
                }
                default:
                {
                    // a kind drawn by the web version that this chart cannot draw yet: show its anchors, joined by a dotted line, with its name
                    var pts = d.Anchors().Select(a => P(m, a)).ToList();
                    for (int i = 0; i + 1 < pts.Count; i++) Line(s, pts[i], pts[i + 1], d, selected, 0.7f, false, true);
                    if (pts.Count > 0) s.Labels.Add(new SceneLabel { At = pts[0] + new Vector2(6, 10), Text = kind + " (shown as an outline)", Color = Parse(d.Color, 0.8f), Anchor = TextAnchor.MiddleLeft, DrawingId = d.Id });
                    break;
                }
            }
        }

        static void AddHandles(DrawingScene s, Drawing d, ChartMap m)
        {
            if (d.Kind == "hline") { s.Handles.Add(new SceneHandle { At = new Vector2(m.Plot.xMin + 30, m.Y(d.Num("price") ?? 0)), DrawingId = d.Id, Index = 0 }); return; }
            if (d.Kind == "vline") { s.Handles.Add(new SceneHandle { At = new Vector2(m.X((long)(d.Num("time") ?? 0)), m.Plot.yMin + 30), DrawingId = d.Id, Index = 0 }); return; }
            var anchors = d.Anchors();
            for (int i = 0; i < anchors.Count; i++) s.Handles.Add(new SceneHandle { At = P(m, anchors[i]), DrawingId = d.Id, Index = i });
        }

        // ---------- hit testing ----------

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        public static bool InsidePolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        /// <summary>The topmost drawing under the pointer (lines within <paramref name="tolerance"/> pixels, or inside a zone), or null.</summary>
        public static string HitTest(DrawingScene s, IReadOnlyList<Drawing> drawings, Vector2 p, float tolerance = 7f)
        {
            for (int i = drawings.Count - 1; i >= 0; i--)
            {
                var d = drawings[i];
                if (d.Hidden) continue;
                foreach (var l in s.Lines) if (l.DrawingId == d.Id && DistanceToSegment(p, l.A, l.B) <= tolerance) return d.Id;
                foreach (var f in s.Fills) if (f.DrawingId == d.Id && f.Color.a >= 0.1f && InsidePolygon(p, f.Points)) return d.Id;
                foreach (var lb in s.Labels) if (lb.DrawingId == d.Id && Mathf.Abs(p.y - lb.At.y) < 9 && p.x >= lb.At.x - 2 && p.x <= lb.At.x + Mathf.Max(40, lb.Text.Length * 7) && (d.Kind == "text" || d.Kind == "pricelabel")) return d.Id;
                foreach (var t in s.Triangles) if (t.DrawingId == d.Id && InsidePolygon(p, t.P)) return d.Id;
            }
            return null;
        }
    }
}
