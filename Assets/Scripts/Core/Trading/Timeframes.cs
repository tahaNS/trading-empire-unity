using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public sealed class Timeframe
    {
        public readonly string Label;
        public readonly long Ms;
        public Timeframe(string label, long ms) { Label = label; Ms = ms; }
    }

    public static class Timeframes
    {
        const long Min = 60000;

        public static readonly IReadOnlyList<Timeframe> All = new[]
        {
            new Timeframe("1m", Min), new Timeframe("2m", 2 * Min), new Timeframe("3m", 3 * Min), new Timeframe("5m", 5 * Min),
            new Timeframe("10m", 10 * Min), new Timeframe("15m", 15 * Min), new Timeframe("30m", 30 * Min),
            new Timeframe("1h", 60 * Min), new Timeframe("2h", 120 * Min), new Timeframe("4h", 240 * Min), new Timeframe("1D", 1440 * Min),
        };

        /// <summary>
        /// Chart timeframes available for a dataset: its own timeframe and every whole multiple of it. Bars can only be
        /// BUILT from finer data, so M1 data offers 2m, 5m, ...; M15 data cannot offer 2m.
        /// </summary>
        public static List<Timeframe> Available(long nativeMs)
        {
            return All.Where(t => t.Ms >= nativeMs && t.Ms % nativeMs == 0).ToList();
        }

        public static string Label(long ms)
        {
            var t = All.FirstOrDefault(x => x.Ms == ms);
            return t != null ? t.Label : Math.Round(ms / (double)Min) + "m";
        }

        /// <summary>Start of the bar containing <paramref name="time"/> (bars are aligned to UTC midnight).</summary>
        public static long BucketStart(long time, long tfMs)
        {
            return (long)Math.Floor(time / (double)tfMs) * tfMs;
        }
    }

    /// <summary>
    /// Builds chart bars from REVEALED candles only, incrementally. The last bar is "forming": it grows as more
    /// candles are revealed, so a bar never contains information from the future.
    /// </summary>
    public sealed class BarAggregator
    {
        public readonly List<Candle> Bars = new List<Candle>();
        public readonly long TfMs;
        // What the preview replaced, so it can be taken back out before the next real candle.
        bool _hasUndo, _undoPopped;
        Candle _undoPrev;

        public BarAggregator(long tfMs) { TfMs = tfMs; }

        public void Reset() { Bars.Clear(); _hasUndo = false; _undoPrev = null; }

        void DropPreview()
        {
            if (!_hasUndo) return;
            if (_undoPopped) Bars.RemoveAt(Bars.Count - 1);
            else if (_undoPrev != null) Bars[Bars.Count - 1] = _undoPrev;
            _hasUndo = false;
            _undoPrev = null;
        }

        /// <summary>Show a partly played candle on the last bar (display only). Null removes it.</summary>
        public void Preview(Candle partial)
        {
            DropPreview();
            if (partial == null) return;
            long start = Timeframes.BucketStart(partial.Time, TfMs);
            var last = Bars.Count > 0 ? Bars[Bars.Count - 1] : null;
            if (last != null && last.Time == start)
            {
                _hasUndo = true; _undoPopped = false; _undoPrev = last;
                Bars[Bars.Count - 1] = new Candle(start, last.Open, Math.Max(last.High, partial.High), Math.Min(last.Low, partial.Low), partial.Close);
            }
            else
            {
                _hasUndo = true; _undoPopped = true; _undoPrev = null;
                Bars.Add(new Candle(start, partial.Open, partial.High, partial.Low, partial.Close));
            }
        }

        /// <summary>Add one revealed candle (chronological order). Returns true if it opened a new bar.</summary>
        public bool Append(Candle c)
        {
            DropPreview();
            long start = Timeframes.BucketStart(c.Time, TfMs);
            var last = Bars.Count > 0 ? Bars[Bars.Count - 1] : null;
            if (last != null && last.Time == start)
            {
                var merged = new Candle(start, last.Open, Math.Max(last.High, c.High), Math.Min(last.Low, c.Low), c.Close);
                if (last.Volume.HasValue || c.Volume.HasValue) merged.Volume = (last.Volume ?? 0) + (c.Volume ?? 0);
                Bars[Bars.Count - 1] = merged;
                return false;
            }
            var bar = new Candle(start, c.Open, c.High, c.Low, c.Close);
            if (c.Volume.HasValue) bar.Volume = c.Volume;
            Bars.Add(bar);
            return true;
        }

        public void Rebuild(IEnumerable<Candle> candles)
        {
            Reset();
            foreach (var c in candles) Append(c);
        }
    }
}
