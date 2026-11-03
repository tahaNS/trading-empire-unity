using System;
using System.Collections.Generic;

namespace TradingEmpire.Core
{
    /// <summary>
    /// The only source of "current market data". The full dataset is private; candles are copied into
    /// <see cref="Revealed"/> only when stepped into view, strictly in order. Nothing visible ever
    /// contains candle N+1 or later (no lookahead).
    /// </summary>
    public sealed class MarketReplay
    {
        public static readonly int[] Speeds = { 1, 2, 5, 10, 30 }; // candles per real second
        const double MaxRealDtSeconds = 0.25;
        /// <summary>Each candle plays in 4 beats: open, first extreme, second extreme, then the full candle.</summary>
        const int BeatsPerCandle = 4;

        readonly Candle[] _all;
        readonly List<Candle> _shown = new List<Candle>();
        readonly int _warmup;
        readonly Action<Candle, int> _onReveal;
        double _accumulator;
        int _cps = 2;
        bool _playing;
        int _beat;
        Candle _partial;

        /// <summary>Called whenever the forming candle changes (null when no candle is forming).</summary>
        public Action<Candle> OnForming;

        public MarketReplay(IReadOnlyList<Candle> candles, int warmup, Action<Candle, int> onReveal)
        {
            _all = new Candle[candles.Count];
            for (int i = 0; i < _all.Length; i++) _all[i] = candles[i];
            _warmup = warmup;
            _onReveal = onReveal;
            int n = Math.Min(Math.Max(_warmup, 1), _all.Length);
            for (int i = 0; i < n; i++) RevealNext();
        }

        /// <summary>Candles revealed so far (read-only view; append-only).</summary>
        public IReadOnlyList<Candle> Revealed { get { return _shown; } }
        public int Total { get { return _all.Length; } }
        public int Speed { get { return _cps; } }
        /// <summary>The next candle as far as it has played (never holds values it has not reached yet).</summary>
        public Candle Forming { get { return _partial; } }
        public bool Playing { get { return _playing; } }
        public bool AtEnd { get { return _shown.Count >= _all.Length; } }

        public void SetSpeed(int s)
        {
            if (Array.IndexOf(Speeds, s) < 0) throw new ArgumentOutOfRangeException("s", "Unsupported replay speed " + s);
            _cps = s;
        }

        public void Play() { if (!AtEnd) _playing = true; }
        public void Pause() { _playing = false; }

        /// <summary>Reveal exactly one more candle. Returns false at the end of the data.</summary>
        public bool Step() { return RevealNext(); }

        /// <summary>
        /// Play one beat of the next candle. A bullish candle runs open -> low -> high -> close, a bearish one
        /// open -> high -> low -> close. Returns true when the candle completed and was revealed.
        /// </summary>
        bool PlayBeat()
        {
            if (_shown.Count >= _all.Length) return RevealNext();
            var c = _all[_shown.Count];
            if (_beat >= BeatsPerCandle - 1) return RevealNext();
            _beat++;
            bool bull = c.Close >= c.Open;
            long first = bull ? c.Low : c.High;
            long second = bull ? c.High : c.Low;
            var p = new Candle(c.Time, c.Open, c.Open, c.Open, c.Open, c.SpreadPts);
            if (_beat >= 2)
            {
                p.Close = first;
                p.High = bull ? c.Open : first;
                p.Low = bull ? first : c.Open;
            }
            if (_beat >= 3)
            {
                p.Close = second;
                p.High = c.High;
                p.Low = c.Low;
            }
            _partial = p;
            if (OnForming != null) OnForming(p);
            return false;
        }

        void ClearForming()
        {
            _beat = 0;
            if (_partial != null)
            {
                _partial = null;
                if (OnForming != null) OnForming(null);
            }
        }

        bool RevealNext()
        {
            int i = _shown.Count;
            if (i >= _all.Length)
            {
                _playing = false;
                return false;
            }
            var c = _all[i];
            ClearForming();
            _shown.Add(c);
            _onReveal(c, i);
            if (AtEnd) _playing = false;
            return true;
        }

        /// <summary>Advance by real elapsed seconds while playing. Returns candles revealed. Never skips candles.</summary>
        public int Advance(double realDtSeconds)
        {
            if (!_playing || !(realDtSeconds > 0)) return 0;
            _accumulator += Math.Min(realDtSeconds, MaxRealDtSeconds) * _cps * BeatsPerCandle;
            int n = 0;
            while (_accumulator >= 1 && _playing)
            {
                _accumulator -= 1;
                if (PlayBeat()) n++;
            }
            return n;
        }
    }
}
