using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum IndicatorCategory { Trend, Momentum, Volatility, Volume }
    public enum LineKind { Line, Hist, Dots }

    public sealed class ParamDef
    {
        public string Key, Label;
        public double Def, Min, Max, Step;
        public ParamDef(string key, string label, double def, double min, double max, double step = 1) { Key = key; Label = label; Def = def; Min = min; Max = max; Step = step; }
    }

    public sealed class LineDef
    {
        public string Key, Label, Color;
        public LineKind Kind;
        public LineDef(string key, string label, string color, LineKind kind = LineKind.Line) { Key = key; Label = label; Color = color; Kind = kind; }
    }

    public sealed class IndicatorDef
    {
        public string Id, Name, About, Short;
        public IndicatorCategory Category;
        /// <summary>True = drawn over the candles, false = in a strip of its own below.</summary>
        public bool Overlay;
        public ParamDef[] Params;
        public LineDef[] Lines;
        /// <summary>A pane with a fixed range (RSI is always 0-100), else null.</summary>
        public double[] Range;
        public double[] Guides = new double[0];
        /// <summary>Shade between the two named lines (Bollinger bands).</summary>
        public string[] Fill;
        public Func<IReadOnlyList<Candle>, Dictionary<string, double>, Dictionary<string, double[]>> Compute;
    }

    /// <summary>One indicator on the chart, with the player's settings. Saved with the game.</summary>
    public sealed class IndicatorConfig
    {
        public string Id, Type;
        public Dictionary<string, double> Params = new Dictionary<string, double>();
        public Dictionary<string, string> Colors = new Dictionary<string, string>();
        public double? Width;
        public bool Hidden;

        public IndicatorConfig Clone()
        {
            return new IndicatorConfig { Id = Id, Type = Type, Params = new Dictionary<string, double>(Params), Colors = new Dictionary<string, string>(Colors), Width = Width, Hidden = Hidden };
        }
    }

    public sealed class ComputedLine
    {
        public string Key, Label, Color;
        public LineKind Kind;
        /// <summary>One value per bar; NaN where there is not yet enough history.</summary>
        public double[] Values;
    }

    public sealed class ComputedIndicator
    {
        public IndicatorConfig Config;
        public IndicatorDef Def;
        public List<ComputedLine> Lines = new List<ComputedLine>();
    }

    /// <summary>
    /// Technical indicators: pure maths on candles. Every value at bar i uses only bars 0..i, so nothing here can look ahead.
    /// Missing values are NaN.
    /// </summary>
    public static class Indicators
    {
        public const int MaxIndicators = 16;
        public const int MaxPanes = 4;

        // ---------------------------------------------------------------- building blocks

        static double[] Blank(int n) { var a = new double[n]; for (int i = 0; i < n; i++) a[i] = double.NaN; return a; }
        static double[] Closes(IReadOnlyList<Candle> b) { var a = new double[b.Count]; for (int i = 0; i < a.Length; i++) a[i] = b[i].Close; return a; }

        public static double[] Sma(double[] v, int n)
        {
            var o = Blank(v.Length);
            double sum = 0; int count = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (double.IsNaN(v[i])) { sum = 0; count = 0; continue; }
                sum += v[i]; count++;
                if (count > n) { double drop = i - n >= 0 && !double.IsNaN(v[i - n]) ? v[i - n] : 0; sum -= drop; count = n; }
                if (count == n) o[i] = sum / n;
            }
            return o;
        }

        /// <summary>Exponential average, started from the simple average of the first n values.</summary>
        public static double[] Ema(double[] v, int n)
        {
            var o = Blank(v.Length);
            double k = 2.0 / (n + 1), prev = double.NaN, seed = 0; int count = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (double.IsNaN(v[i])) continue;
                if (double.IsNaN(prev)) { seed += v[i]; count++; if (count == n) { prev = seed / n; o[i] = prev; } }
                else { prev = v[i] * k + prev * (1 - k); o[i] = prev; }
            }
            return o;
        }

        public static double[] Wma(double[] v, int n)
        {
            var o = Blank(v.Length);
            double den = n * (n + 1) / 2.0;
            for (int i = n - 1; i < v.Length; i++)
            {
                double s = 0; bool ok = true;
                for (int j = 0; j < n; j++) { double x = v[i - n + 1 + j]; if (double.IsNaN(x)) { ok = false; break; } s += x * (j + 1); }
                if (ok) o[i] = s / den;
            }
            return o;
        }

        /// <summary>Wilder's smoothing (the average used by RSI, ATR and ADX).</summary>
        static double[] Wilder(double[] v, int n)
        {
            var o = Blank(v.Length);
            double prev = double.NaN, seed = 0; int count = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (double.IsNaN(v[i])) continue;
                if (double.IsNaN(prev)) { seed += v[i]; count++; if (count == n) { prev = seed / n; o[i] = prev; } }
                else { prev = (prev * (n - 1) + v[i]) / n; o[i] = prev; }
            }
            return o;
        }

        static double[] TrueRange(IReadOnlyList<Candle> b)
        {
            var o = new double[b.Count];
            for (int i = 0; i < o.Length; i++)
            {
                double hl = b[i].High - b[i].Low;
                o[i] = i == 0 ? hl : Math.Max(hl, Math.Max(Math.Abs(b[i].High - b[i - 1].Close), Math.Abs(b[i].Low - b[i - 1].Close)));
            }
            return o;
        }

        public static double[] Atr(IReadOnlyList<Candle> b, int n) { return Wilder(TrueRange(b), n); }

        static double[] Window(double[] v, int n, bool max)
        {
            var o = Blank(v.Length);
            for (int i = n - 1; i < v.Length; i++)
            {
                double m = max ? double.NegativeInfinity : double.PositiveInfinity;
                for (int j = i - n + 1; j <= i; j++) m = max ? Math.Max(m, v[j]) : Math.Min(m, v[j]);
                o[i] = m;
            }
            return o;
        }
        public static double[] Highest(double[] v, int n) { return Window(v, n, true); }
        public static double[] Lowest(double[] v, int n) { return Window(v, n, false); }

        static double[] Highs(IReadOnlyList<Candle> b) { var a = new double[b.Count]; for (int i = 0; i < a.Length; i++) a[i] = b[i].High; return a; }
        static double[] Lows(IReadOnlyList<Candle> b) { var a = new double[b.Count]; for (int i = 0; i < a.Length; i++) a[i] = b[i].Low; return a; }

        public static double[] Rsi(double[] v, int n)
        {
            var o = Blank(v.Length);
            if (v.Length <= n) return o;
            double gain = 0, loss = 0;
            for (int i = 1; i <= n; i++) { double d = v[i] - v[i - 1]; if (d >= 0) gain += d; else loss -= d; }
            double ag = gain / n, al = loss / n;
            Func<double> calc = () => al == 0 ? (ag == 0 ? 50 : 100) : 100 - 100 / (1 + ag / al);
            o[n] = calc();
            for (int i = n + 1; i < v.Length; i++)
            {
                double d = v[i] - v[i - 1];
                ag = (ag * (n - 1) + Math.Max(d, 0)) / n;
                al = (al * (n - 1) + Math.Max(-d, 0)) / n;
                o[i] = calc();
            }
            return o;
        }

        static double[] Minus(double[] a, double[] b)
        {
            var o = new double[a.Length];
            for (int i = 0; i < o.Length; i++) o[i] = double.IsNaN(a[i]) || double.IsNaN(b[i]) ? double.NaN : a[i] - b[i];
            return o;
        }

        static double[] Stdev(double[] v, int n)
        {
            var o = Blank(v.Length);
            for (int i = n - 1; i < v.Length; i++)
            {
                double s = 0; for (int j = i - n + 1; j <= i; j++) s += v[j];
                double mean = s / n, q = 0;
                for (int j = i - n + 1; j <= i; j++) q += (v[j] - mean) * (v[j] - mean);
                o[i] = Math.Sqrt(q / n);
            }
            return o;
        }

        static double[] ParabolicSar(IReadOnlyList<Candle> b, double step, double max)
        {
            var o = Blank(b.Count);
            if (b.Count < 2) return o;
            bool up = b[1].Close >= b[0].Close;
            double ep = up ? b[0].High : b[0].Low, sar = up ? b[0].Low : b[0].High, af = step;
            for (int i = 1; i < b.Count; i++)
            {
                var c = b[i]; var p = b[i - 1];
                sar += af * (ep - sar);
                if (up)
                {
                    sar = Math.Min(sar, Math.Min(p.Low, i >= 2 ? b[i - 2].Low : p.Low));
                    if (c.Low < sar) { up = false; sar = ep; ep = c.Low; af = step; }
                    else if (c.High > ep) { ep = c.High; af = Math.Min(af + step, max); }
                }
                else
                {
                    sar = Math.Max(sar, Math.Max(p.High, i >= 2 ? b[i - 2].High : p.High));
                    if (c.High > sar) { up = true; sar = ep; ep = c.High; af = step; }
                    else if (c.Low < ep) { ep = c.Low; af = Math.Min(af + step, max); }
                }
                o[i] = sar;
            }
            return o;
        }

        static Dictionary<string, double[]> Adx(IReadOnlyList<Candle> b, int n)
        {
            int len = b.Count;
            var plusDm = new double[len]; var minusDm = new double[len];
            for (int i = 1; i < len; i++)
            {
                double up = b[i].High - b[i - 1].High, down = b[i - 1].Low - b[i].Low;
                plusDm[i] = up > down && up > 0 ? up : 0;
                minusDm[i] = down > up && down > 0 ? down : 0;
            }
            var tr = Wilder(TrueRange(b), n); var pd = Wilder(plusDm, n); var md = Wilder(minusDm, n);
            var plus = Blank(len); var minus = Blank(len); var dx = Blank(len);
            for (int i = 0; i < len; i++)
            {
                if (double.IsNaN(tr[i]) || tr[i] == 0) continue;
                if (!double.IsNaN(pd[i])) plus[i] = 100 * pd[i] / tr[i];
                if (!double.IsNaN(md[i])) minus[i] = 100 * md[i] / tr[i];
                if (!double.IsNaN(plus[i]) && !double.IsNaN(minus[i])) dx[i] = plus[i] + minus[i] == 0 ? 0 : 100 * Math.Abs(plus[i] - minus[i]) / (plus[i] + minus[i]);
            }
            return new Dictionary<string, double[]> { { "adx", Wilder(dx, n) }, { "plus", plus }, { "minus", minus } };
        }

        static double P(Dictionary<string, double> p, string key, double def) { double v; return p.TryGetValue(key, out v) ? v : def; }
        static int PI(Dictionary<string, double> p, string key, int def) { return (int)Math.Round(P(p, key, def)); }

        // ---------------------------------------------------------------- the indicators

        public static readonly IndicatorDef[] All =
        {
            new IndicatorDef { Id = "sma", Short = "SMA", Name = "Moving Average (simple)", About = "The average close of the last N bars.", Category = IndicatorCategory.Trend, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 1, 500) }, Lines = new[] { new LineDef("ma", "SMA", "#f2c25b") },
                Compute = (b, p) => new Dictionary<string, double[]> { { "ma", Sma(Closes(b), PI(p, "period", 20)) } } },
            new IndicatorDef { Id = "ema", Short = "EMA", Name = "Moving Average (exponential)", About = "An average that gives more weight to recent bars.", Category = IndicatorCategory.Trend, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 1, 500) }, Lines = new[] { new LineDef("ma", "EMA", "#6ea8ff") },
                Compute = (b, p) => new Dictionary<string, double[]> { { "ma", Ema(Closes(b), PI(p, "period", 20)) } } },
            new IndicatorDef { Id = "wma", Short = "WMA", Name = "Moving Average (weighted)", About = "A linearly weighted average: the newest bar counts most.", Category = IndicatorCategory.Trend, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 1, 500) }, Lines = new[] { new LineDef("ma", "WMA", "#c792ea") },
                Compute = (b, p) => new Dictionary<string, double[]> { { "ma", Wma(Closes(b), PI(p, "period", 20)) } } },
            new IndicatorDef { Id = "bb", Short = "BB", Name = "Bollinger Bands", About = "A moving average with bands a number of standard deviations either side.", Category = IndicatorCategory.Volatility, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 2, 500), new ParamDef("dev", "Deviations", 2, 0.1, 10, 0.1) },
                Lines = new[] { new LineDef("upper", "Upper", "#6ea8ff"), new LineDef("mid", "Basis", "#f2c25b"), new LineDef("lower", "Lower", "#6ea8ff") }, Fill = new[] { "upper", "lower" },
                Compute = (b, p) =>
                {
                    var c = Closes(b); int n = PI(p, "period", 20); double k = P(p, "dev", 2);
                    var mid = Sma(c, n); var sd = Stdev(c, n);
                    var up = new double[c.Length]; var lo = new double[c.Length];
                    for (int i = 0; i < c.Length; i++) { up[i] = mid[i] + k * sd[i]; lo[i] = mid[i] - k * sd[i]; }
                    return new Dictionary<string, double[]> { { "mid", mid }, { "upper", up }, { "lower", lo } };
                } },
            new IndicatorDef { Id = "donchian", Short = "Donchian", Name = "Donchian Channels", About = "The highest high and lowest low of the last N bars.", Category = IndicatorCategory.Volatility, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 1, 500) },
                Lines = new[] { new LineDef("upper", "Upper", "#2fcfae"), new LineDef("mid", "Middle", "#f2c25b"), new LineDef("lower", "Lower", "#f2686b") }, Fill = new[] { "upper", "lower" },
                Compute = (b, p) =>
                {
                    int n = PI(p, "period", 20);
                    var hi = Highest(Highs(b), n); var lo = Lowest(Lows(b), n); var mid = new double[hi.Length];
                    for (int i = 0; i < mid.Length; i++) mid[i] = (hi[i] + lo[i]) / 2;
                    return new Dictionary<string, double[]> { { "upper", hi }, { "lower", lo }, { "mid", mid } };
                } },
            new IndicatorDef { Id = "keltner", Short = "Keltner", Name = "Keltner Channels", About = "An exponential average with bands a multiple of the ATR away.", Category = IndicatorCategory.Volatility, Overlay = true,
                Params = new[] { new ParamDef("period", "Period", 20, 1, 500), new ParamDef("atrPeriod", "ATR period", 10, 1, 200), new ParamDef("mult", "Multiplier", 2, 0.1, 10, 0.1) },
                Lines = new[] { new LineDef("upper", "Upper", "#c792ea"), new LineDef("mid", "Middle", "#f2c25b"), new LineDef("lower", "Lower", "#c792ea") }, Fill = new[] { "upper", "lower" },
                Compute = (b, p) =>
                {
                    var mid = Ema(Closes(b), PI(p, "period", 20)); var a = Atr(b, PI(p, "atrPeriod", 10)); double m = P(p, "mult", 2);
                    var up = new double[mid.Length]; var lo = new double[mid.Length];
                    for (int i = 0; i < mid.Length; i++) { up[i] = mid[i] + m * a[i]; lo[i] = mid[i] - m * a[i]; }
                    return new Dictionary<string, double[]> { { "mid", mid }, { "upper", up }, { "lower", lo } };
                } },
            new IndicatorDef { Id = "psar", Short = "SAR", Name = "Parabolic SAR", About = "Dots that trail the price and flip sides when the trend turns.", Category = IndicatorCategory.Trend, Overlay = true,
                Params = new[] { new ParamDef("step", "Step", 0.02, 0.001, 0.5, 0.001), new ParamDef("max", "Maximum", 0.2, 0.01, 1, 0.01) }, Lines = new[] { new LineDef("sar", "SAR", "#2fcfae", LineKind.Dots) },
                Compute = (b, p) => new Dictionary<string, double[]> { { "sar", ParabolicSar(b, P(p, "step", 0.02), P(p, "max", 0.2)) } } },
            new IndicatorDef { Id = "rsi", Short = "RSI", Name = "Relative Strength Index", About = "Momentum from 0 to 100. Above 70 is often called overbought, below 30 oversold.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 14, 2, 200) }, Lines = new[] { new LineDef("rsi", "RSI", "#c792ea") }, Range = new double[] { 0, 100 }, Guides = new double[] { 30, 50, 70 },
                Compute = (b, p) => new Dictionary<string, double[]> { { "rsi", Rsi(Closes(b), PI(p, "period", 14)) } } },
            new IndicatorDef { Id = "macd", Short = "MACD", Name = "MACD", About = "The gap between a fast and a slow average, its signal line and the histogram.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("fast", "Fast", 12, 1, 200), new ParamDef("slow", "Slow", 26, 2, 400), new ParamDef("signal", "Signal", 9, 1, 200) },
                Lines = new[] { new LineDef("hist", "Histogram", "#8fa6bf", LineKind.Hist), new LineDef("macd", "MACD", "#6ea8ff"), new LineDef("signal", "Signal", "#f2c25b") }, Guides = new double[] { 0 },
                Compute = (b, p) =>
                {
                    var c = Closes(b);
                    var macd = Minus(Ema(c, PI(p, "fast", 12)), Ema(c, PI(p, "slow", 26)));
                    var signal = Ema(macd, PI(p, "signal", 9));
                    return new Dictionary<string, double[]> { { "macd", macd }, { "signal", signal }, { "hist", Minus(macd, signal) } };
                } },
            new IndicatorDef { Id = "stoch", Short = "Stoch", Name = "Stochastic Oscillator", About = "Where the close sits inside the recent high-low range, from 0 to 100.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("k", "%K period", 14, 1, 200), new ParamDef("smooth", "Smoothing", 3, 1, 50), new ParamDef("d", "%D period", 3, 1, 50) },
                Lines = new[] { new LineDef("k", "%K", "#6ea8ff"), new LineDef("d", "%D", "#f2686b") }, Range = new double[] { 0, 100 }, Guides = new double[] { 20, 50, 80 },
                Compute = (b, p) =>
                {
                    int kn = PI(p, "k", 14);
                    var hi = Highest(Highs(b), kn); var lo = Lowest(Lows(b), kn); var raw = Blank(b.Count);
                    for (int i = 0; i < raw.Length; i++) if (!double.IsNaN(hi[i])) raw[i] = hi[i] == lo[i] ? 50 : 100.0 * (b[i].Close - lo[i]) / (hi[i] - lo[i]);
                    var k = Sma(raw, PI(p, "smooth", 3));
                    return new Dictionary<string, double[]> { { "k", k }, { "d", Sma(k, PI(p, "d", 3)) } };
                } },
            new IndicatorDef { Id = "atr", Short = "ATR", Name = "Average True Range", About = "How far the price moves in a typical bar: a measure of volatility.", Category = IndicatorCategory.Volatility, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 14, 1, 200) }, Lines = new[] { new LineDef("atr", "ATR", "#f2c25b") },
                Compute = (b, p) => new Dictionary<string, double[]> { { "atr", Atr(b, PI(p, "period", 14)) } } },
            new IndicatorDef { Id = "cci", Short = "CCI", Name = "Commodity Channel Index", About = "How far the typical price is from its average, in units of mean deviation.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 20, 2, 200) }, Lines = new[] { new LineDef("cci", "CCI", "#2fcfae") }, Guides = new double[] { -100, 0, 100 },
                Compute = (b, p) =>
                {
                    int n = PI(p, "period", 20);
                    var tp = new double[b.Count]; for (int i = 0; i < tp.Length; i++) tp[i] = (b[i].High + b[i].Low + b[i].Close) / 3.0;
                    var mean = Sma(tp, n); var o = Blank(b.Count);
                    for (int i = n - 1; i < b.Count; i++)
                    {
                        if (double.IsNaN(mean[i])) continue;
                        double dev = 0; for (int j = i - n + 1; j <= i; j++) dev += Math.Abs(tp[j] - mean[i]);
                        dev /= n;
                        o[i] = dev == 0 ? 0 : (tp[i] - mean[i]) / (0.015 * dev);
                    }
                    return new Dictionary<string, double[]> { { "cci", o } };
                } },
            new IndicatorDef { Id = "willr", Short = "%R", Name = "Williams %R", About = "Like the stochastic upside down: 0 to -100, near 0 is overbought.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 14, 2, 200) }, Lines = new[] { new LineDef("r", "%R", "#f2686b") }, Range = new double[] { -100, 0 }, Guides = new double[] { -80, -50, -20 },
                Compute = (b, p) =>
                {
                    int n = PI(p, "period", 14);
                    var hi = Highest(Highs(b), n); var lo = Lowest(Lows(b), n); var o = Blank(b.Count);
                    for (int i = 0; i < o.Length; i++) if (!double.IsNaN(hi[i])) o[i] = hi[i] == lo[i] ? -50 : -100.0 * (hi[i] - b[i].Close) / (hi[i] - lo[i]);
                    return new Dictionary<string, double[]> { { "r", o } };
                } },
            new IndicatorDef { Id = "roc", Short = "ROC", Name = "Rate of Change", About = "The percentage the price has moved over the last N bars.", Category = IndicatorCategory.Momentum, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 12, 1, 200) }, Lines = new[] { new LineDef("roc", "ROC", "#6ea8ff") }, Guides = new double[] { 0 },
                Compute = (b, p) =>
                {
                    int n = PI(p, "period", 12); var o = Blank(b.Count);
                    for (int i = n; i < o.Length; i++) if (b[i - n].Close != 0) o[i] = 100.0 * (b[i].Close - b[i - n].Close) / b[i - n].Close;
                    return new Dictionary<string, double[]> { { "roc", o } };
                } },
            new IndicatorDef { Id = "adx", Short = "ADX", Name = "Average Directional Index", About = "How strong a trend is (ADX), and which side is pushing (+DI and -DI).", Category = IndicatorCategory.Trend, Overlay = false,
                Params = new[] { new ParamDef("period", "Period", 14, 2, 200) },
                Lines = new[] { new LineDef("adx", "ADX", "#f2c25b"), new LineDef("plus", "+DI", "#2fcfae"), new LineDef("minus", "-DI", "#f2686b") }, Guides = new double[] { 20, 40 },
                Compute = (b, p) => Adx(b, PI(p, "period", 14)) },
            new IndicatorDef { Id = "volume", Short = "Volume", Name = "Volume", About = "The volume of each bar, when the data has it.", Category = IndicatorCategory.Volume, Overlay = false,
                Params = new ParamDef[0], Lines = new[] { new LineDef("vol", "Volume", "#6ea8ff", LineKind.Hist) },
                Compute = (b, p) => { var o = Blank(b.Count); for (int i = 0; i < o.Length; i++) if (b[i].Volume.HasValue) o[i] = b[i].Volume.Value; return new Dictionary<string, double[]> { { "vol", o } }; } },
        };

        public static IndicatorDef Get(string id) { return All.FirstOrDefault(d => d.Id == id); }

        public static string CategoryLabel(IndicatorCategory c)
        {
            switch (c) { case IndicatorCategory.Trend: return "Trend"; case IndicatorCategory.Momentum: return "Momentum"; case IndicatorCategory.Volatility: return "Volatility"; default: return "Volume"; }
        }

        public static Dictionary<string, double> DefaultParams(IndicatorDef def)
        {
            var d = new Dictionary<string, double>();
            foreach (var p in def.Params) d[p.Key] = p.Def;
            return d;
        }

        /// <summary>Clamp every parameter into its allowed range (whole numbers where the step is 1).</summary>
        public static Dictionary<string, double> Normalize(IndicatorDef def, Dictionary<string, double> raw)
        {
            var o = new Dictionary<string, double>();
            foreach (var p in def.Params)
            {
                double v; if (!raw.TryGetValue(p.Key, out v) || double.IsNaN(v) || double.IsInfinity(v)) v = p.Def;
                v = Math.Min(Math.Max(v, p.Min), p.Max);
                if (p.Step >= 1) v = Math.Round(v);
                o[p.Key] = v;
            }
            return o;
        }

        /// <summary>Compute an indicator over the bars (NaN where there is not yet enough history), or null for an unknown type.</summary>
        public static ComputedIndicator Compute(IndicatorConfig cfg, IReadOnlyList<Candle> bars)
        {
            var def = Get(cfg.Type);
            if (def == null) return null;
            var values = def.Compute(bars, Normalize(def, cfg.Params));
            var res = new ComputedIndicator { Config = cfg, Def = def };
            foreach (var l in def.Lines)
            {
                string color; if (!cfg.Colors.TryGetValue(l.Key, out color)) color = l.Color;
                double[] v; if (!values.TryGetValue(l.Key, out v)) v = Blank(bars.Count);
                res.Lines.Add(new ComputedLine { Key = l.Key, Label = l.Label, Color = color, Kind = l.Kind, Values = v });
            }
            return res;
        }

        /// <summary>"SMA (20)": the name shown on the chart.</summary>
        public static string Label(IndicatorConfig cfg)
        {
            var def = Get(cfg.Type);
            if (def == null) return cfg.Type;
            var p = Normalize(def, cfg.Params);
            string args = string.Join(", ", def.Params.Select(d => p[d.Key].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            return def.Short + (args.Length > 0 ? " (" + args + ")" : "");
        }
    }

    /// <summary>The indicators on the chart. Saved with the game; pure state.</summary>
    public sealed class IndicatorStore
    {
        readonly List<IndicatorConfig> _items = new List<IndicatorConfig>();
        readonly IdSequence _ids = new IdSequence("i");
        public event Action Changed;

        public IReadOnlyList<IndicatorConfig> Items { get { return _items; } }

        void Fire() { var h = Changed; if (h != null) h(); }

        public string Add(string type)
        {
            var def = Indicators.Get(type);
            if (def == null || _items.Count >= Indicators.MaxIndicators) return null;
            var cfg = new IndicatorConfig { Id = _ids.Next(), Type = type, Params = Indicators.DefaultParams(def) };
            _items.Add(cfg);
            Fire();
            return cfg.Id;
        }

        public void Update(string id, Action<IndicatorConfig> change)
        {
            var cfg = _items.FirstOrDefault(c => c.Id == id);
            if (cfg == null) return;
            change(cfg);
            var def = Indicators.Get(cfg.Type);
            if (def != null) cfg.Params = Indicators.Normalize(def, cfg.Params);
            Fire();
        }

        public void Remove(string id) { if (_items.RemoveAll(c => c.Id == id) > 0) Fire(); }
        public void Clear() { if (_items.Count > 0) { _items.Clear(); Fire(); } }

        public List<IndicatorConfig> Snapshot() { return _items.Select(c => c.Clone()).ToList(); }

        /// <summary>Replace everything from a loaded save; unknown indicator types are dropped.</summary>
        public void Restore(IEnumerable<IndicatorConfig> items)
        {
            _items.Clear();
            if (items != null) foreach (var c in items) if (Indicators.Get(c.Type) != null && _items.Count < Indicators.MaxIndicators) _items.Add(c.Clone());
            _ids.ResetAfter(_items.Select(c => c.Id));
            Fire();
        }
    }
}
