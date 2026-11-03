using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum SessionStatus { Ready, Loading, Error }
    public enum NoticeKind { Info, Success, Error }

    public sealed class Notice
    {
        public int Seq;
        public NoticeKind Kind;
        public string Text;
    }

    public sealed class DatasetInfo
    {
        /// <summary>Stable identity: "synthetic:seed:count:minutes" or "upload:hash".</summary>
        public string Id;
        public uint? Seed;
        public string Name;
        public bool Synthetic;
        public string Symbol = "EURUSD";
        public int Total;
        public long TimeframeMs;
    }

    public sealed class LoadReport
    {
        public int RowsRead, RowsRejected, DuplicatesDropped;
        public bool WasUnsorted;
        public List<RowError> Errors = new List<RowError>();
    }

    public sealed class PositionView
    {
        public Position Position;
        /// <summary>Unrealized P/L net of the round-turn commission.</summary>
        public Money Unrealized;
    }

    public sealed class SessionState
    {
        public SessionStatus Status;
        public DatasetInfo Dataset;
        public LoadReport Report;
        public string Error;
        public int RevealedCount, Total;
        public long? Now;
        public long? Bid, Ask;
        public bool Playing;
        public int Speed;
        public AccountView Account;
        public List<PositionView> Positions;
        public List<PendingOrder> Pending;
        public List<Position> Closed;
        public TradingStats Stats;
        public Notice Notice;
        public int RunId;
        /// <summary>Timeframe the chart shows (a whole multiple of the dataset's own timeframe).</summary>
        public long ChartTfMs;
        /// <summary>The dataset's own candle length.</summary>
        public long NativeMs;
    }

    public enum MarkerKind { Buy, Sell, Exit }

    public sealed class ChartMarker
    {
        /// <summary>Time of the candle the event happened in (mapped to a chart bar by the chart).</summary>
        public long Time;
        public long Price;
        public MarkerKind Kind;
    }

    public sealed class ChartData
    {
        /// <summary>Bars at the chart timeframe, built from revealed candles only (the last one is still forming).</summary>
        public IReadOnlyList<Candle> Candles = new List<Candle>();
        public int Digits = 5, PipPoints = 10;
        public long TfMs = 60000;
        public long? Bid, Ask;
        public List<ChartMarker> Markers = new List<ChartMarker>();
        public List<PendingOrder> Pending = new List<PendingOrder>();
        public List<Position> Open = new List<Position>();
    }

    /// <summary>
    /// Trading domain facade for the UI: dataset, replay, engine and account behind one object. The UI only ever sees
    /// REVEALED candles; the full dataset stays in private fields. Subscribe to <see cref="Changed"/> to redraw.
    /// </summary>
    public sealed class TradingSession
    {
        public const double Leverage = 100;
        public const double StopOutLevel = 0.5;
        public const int ReplayWarmupHours = 36;
        public const int ReplayWarmupCandles = 150;
        public const int ReplayWarmupMaxCandles = 2000;
        public static readonly Money StartingBalance = Money.FromDollars(10000);

        public InstrumentDefinition Instrument { get; private set; }
        public readonly TradingAccount Account;
        /// <summary>Raised after any change to <see cref="State"/>.</summary>
        public event Action Changed;
        public SessionState State { get; private set; }

        readonly ExecutionEngine _engine;
        List<Candle> _candles = new List<Candle>();
        MarketReplay _replay;
        long _timeframeMs = 15 * 60000;
        int _runId;
        int _noticeSeq;
        bool _building;
        BarAggregator _agg = new BarAggregator(60000);
        // Close of the candle that is still playing (display only; orders use the last full candle).
        long? _livePrice;
        long _chartTf = 60000;

        public TradingSession(InstrumentDefinition instrument = null)
        {
            Instrument = instrument ?? Instruments.EURUSD;
            Account = new TradingAccount(StartingBalance, new IdSequence("a"), 0);
            _engine = new ExecutionEngine(Instrument, Account, new EngineConfig { Leverage = Leverage, StopOutLevel = StopOutLevel, StopFirst = true }, new IdSequence("p"));
            State = Build(SessionStatus.Loading, null);
        }

        // ---------- dataset ----------

        void UseInstrument(InstrumentDefinition inst)
        {
            Instrument = inst;
            _engine.SetInstrument(inst);
        }

        public void BeginLoading() { Patch(s => { s.Status = SessionStatus.Loading; s.Error = null; }); }

        public void Fail(string error, LoadReport report = null)
        {
            Patch(s => { s.Status = _replay != null ? SessionStatus.Ready : SessionStatus.Error; s.Error = error; s.Report = report; });
        }

        /// <summary>Install a dataset. Open positions are closed at market first so nothing is left dangling.</summary>
        public void LoadDataset(IReadOnlyList<Candle> candles, string name, bool synthetic, LoadReport report = null, uint? seed = null, string symbol = null)
        {
            CloseAllInternal();
            UseInstrument(Instruments.Find(symbol) ?? Instruments.EURUSD);
            if (_replay != null) _replay.Pause();
            _candles = candles.ToList();
            _timeframeMs = DetectTimeframe(candles);
            _chartTf = ValidChartTf(_chartTf);
            _runId++;
            StartReplay();
            var dataset = new DatasetInfo
            {
                Id = synthetic
                    ? "synthetic:" + (seed ?? 0) + ":" + candles.Count + ":" + Math.Round(_timeframeMs / 60000.0) + "m"
                    : "upload:" + HashCandles(candles),
                Seed = seed, Name = name, Synthetic = synthetic, Symbol = Instrument.Symbol,
                Total = candles.Count, TimeframeMs = _timeframeMs,
            };
            State = Build(SessionStatus.Ready, dataset);
            State.Report = report;
            Notify(NoticeKind.Info, "Loaded " + candles.Count.ToString("N0", CultureInfo.InvariantCulture) + " candles" + (synthetic ? " (synthetic test data)" : ""));
        }

        /// <summary>Convenience: load the labelled synthetic EUR/USD dataset (never presented as real data).</summary>
        public void LoadSynthetic(uint seed = 42, int count = 3000, long timeframeMs = 15 * 60000)
        {
            LoadDataset(SyntheticData.Generate(seed, count, timeframeMs), "SYNTHETIC EUR/USD (seeded random walk)", true, null, seed);
        }

        /// <summary>FNV-1a over the candle times and closes: a cheap, stable identity for an uploaded dataset.</summary>
        public static string HashCandles(IReadOnlyList<Candle> candles)
        {
            uint h = 0x811c9dc5;
            Action<uint> mix = n => { unchecked { h ^= n; h *= 0x01000193; } };
            foreach (var c in candles)
            {
                mix((uint)(c.Time & 0xffffffff));
                mix((uint)(c.Time >> 32));
                mix((uint)(c.Close & 0xffffffff));
            }
            return h.ToString("x", CultureInfo.InvariantCulture) + "-" + candles.Count;
        }

        /// <summary>Smallest positive gap among the first candles (weekend gaps are larger, so min = timeframe).</summary>
        static long DetectTimeframe(IReadOnlyList<Candle> candles)
        {
            long best = long.MaxValue;
            for (int i = 1; i < Math.Min(candles.Count, 200); i++)
            {
                long d = candles[i].Time - candles[i - 1].Time;
                if (d > 0 && d < best) best = d;
            }
            return best == long.MaxValue ? 15 * 60000 : best;
        }

        /// <summary>The chart timeframe if it is valid for this dataset, otherwise the dataset's own.</summary>
        long ValidChartTf(long ms) { return Timeframes.Available(_timeframeMs).Any(t => t.Ms == ms) ? ms : _timeframeMs; }

        void StartReplay(int? revealed = null)
        {
            _building = true;
            _agg = new BarAggregator(_chartTf);
            _livePrice = null;
            int speed = _replay != null ? _replay.Speed : 2;
            // Small datasets keep at least half their candles for replay.
            int wanted = Math.Min(ReplayWarmupMaxCandles, Math.Max(ReplayWarmupCandles, (int)Math.Round(ReplayWarmupHours * 3600000.0 / _timeframeMs)));
            int warmup = revealed ?? Math.Min(wanted, Math.Max(1, _candles.Count / 2));
            _replay = new MarketReplay(_candles, warmup, OnReveal);
            _replay.OnForming = partial =>
            {
                _agg.Preview(partial);
                _livePrice = partial != null ? partial.Close : (long?)null;
                if (partial != null) OnFormingBeat(partial);
            };
            _replay.SetSpeed(speed);
            _building = false;
        }

        /// <summary>Dataset candles, for the save layer only (never exposed to the UI or chart).</summary>
        public IReadOnlyList<Candle> DatasetCandles() { return _candles; }

        /// <summary>Restart from the beginning of the dataset. Open positions are closed at market first.</summary>
        public void Restart()
        {
            if (_replay == null) return;
            bool hadOpen = Account.Open.Count > 0;
            CloseAllInternal();
            _runId++;
            StartReplay();
            Patch(null);
            Notify(NoticeKind.Info, hadOpen ? "Session restarted; open positions were closed at market" : "Session restarted");
        }

        // ---------- replay ----------

        public void Play() { if (_replay != null) _replay.Play(); Patch(null); }
        public void Pause() { if (_replay != null) _replay.Pause(); Patch(null); }
        public void TogglePlay() { if (_replay != null && _replay.Playing) Pause(); else Play(); }

        public void Step()
        {
            if (_replay == null) return;
            if (!_replay.Step()) Notify(NoticeKind.Info, "End of dataset");
        }

        public void SetSpeed(int s) { if (_replay != null) _replay.SetSpeed(s); Patch(null); }

        /// <summary>Called every frame by the game loop while the terminal is open.</summary>
        public void Advance(double dtSeconds)
        {
            long? before = _livePrice;
            int revealed = _replay != null ? _replay.Advance(dtSeconds) : 0;
            // A reveal already published; a candle still forming needs one publish per frame.
            if (revealed == 0 && _livePrice != before) Patch(null);
        }

        void OnReveal(Candle c, int index)
        {
            long closeTime = c.Time + _timeframeMs;
            _agg.Append(c);
            var closed = _engine.ProcessCandle(c, closeTime, index);
            Report(closed);
            if (_building) return;
            if (_replay != null && _replay.AtEnd) Notify(NoticeKind.Info, "End of dataset");
            Patch(null);
        }

        /// <summary>The candle at the next index is still forming: let stops and resting orders react to what has played.</summary>
        void OnFormingBeat(Candle partial)
        {
            if (_building || _replay == null) return;
            long closeTime = partial.Time + _timeframeMs;
            Report(_engine.ProcessPartial(partial, closeTime, _replay.Revealed.Count));
        }

        /// <summary>Tell the player about fills, cancellations and exits the engine just produced.</summary>
        void Report(IReadOnlyList<Position> closed)
        {
            var events = _engine.DrainEvents();
            if (_building) return;
            foreach (var e in events)
            {
                var o = e.Order;
                string name = (o.Direction == Direction.Buy ? "buy" : "sell") + " " + (o.Kind == PendingKind.Limit ? "limit" : "stop") + " "
                    + Units.FormatLots(o.Lots) + " @ " + Units.FormatPrice(o.Price, Instrument.Digits);
                if (e.Filled) Notify(NoticeKind.Info, "Order filled: " + name);
                else Notify(NoticeKind.Error, "Order cancelled (not enough free margin): " + name);
            }
            foreach (var p in closed)
            {
                var x = p.Exit;
                if (x == null) continue;
                string what = x.Reason == CloseReason.StopLoss ? "Stop-loss hit" : x.Reason == CloseReason.TakeProfit ? "Take-profit hit" : "Stop-out";
                Notify(x.Net.Cents >= 0 ? NoticeKind.Success : NoticeKind.Error,
                    what + ": " + (p.Direction == Direction.Buy ? "buy" : "sell") + " " + Units.FormatLots(p.Lots) + " " + x.Net.ToSignedString());
            }
        }

        // ---------- trading ----------

        public OrderPreview Preview(OrderRequest order) { return _engine.Preview(order); }

        public bool PlaceOrder(OrderRequest order)
        {
            int idx = LastIndex();
            if (order.Entry.HasValue)
            {
                var pr = _engine.PlacePending(order, idx, _runId);
                if (pr.Ok)
                {
                    var o = pr.Order;
                    Patch(null);
                    Notify(NoticeKind.Success, "Placed " + (o.Direction == Direction.Buy ? "buy" : "sell") + " " + (o.Kind == PendingKind.Limit ? "limit" : "stop")
                        + " " + Units.FormatLots(o.Lots) + " " + Instrument.Display + " @ " + Units.FormatPrice(o.Price, Instrument.Digits));
                    return true;
                }
                // An entry that turned out to be at the market becomes a market order below.
                if (pr.Rejections[0].Code != RejectionCode.AtMarket)
                {
                    Notify(NoticeKind.Error, Explain(pr.Rejections));
                    return false;
                }
            }
            var r = _engine.Open(order, idx, _runId);
            if (!r.Ok)
            {
                Notify(NoticeKind.Error, Explain(r.Rejections));
                return false;
            }
            var p = r.Position;
            Patch(null);
            Notify(NoticeKind.Success, "Opened " + (p.Direction == Direction.Buy ? "buy" : "sell") + " " + Units.FormatLots(p.Lots) + " " + Instrument.Display
                + " @ " + Units.FormatPrice(p.EntryPrice, Instrument.Digits));
            return true;
        }

        /// <summary>Market order with the stop-loss and take-profit given as pip distances.</summary>
        public bool QuickOrder(Direction direction, long lots, double? slPips, double? tpPips)
        {
            var q = _engine.Quote;
            if (q == null) { Notify(NoticeKind.Error, "Order rejected: No market price yet."); return false; }
            return PlaceOrder(OrderBuilder.Build(Instrument, direction, lots, q, slPips, tpPips));
        }

        public void ClosePosition(string id)
        {
            var p = _engine.CloseManual(id, LastIndex());
            if (p == null || p.Exit == null) return;
            Patch(null);
            Notify(p.Exit.Net.Cents >= 0 ? NoticeKind.Success : NoticeKind.Error,
                "Closed " + (p.Direction == Direction.Buy ? "buy" : "sell") + " " + Units.FormatLots(p.Lots) + ": " + p.Exit.Net.ToSignedString());
        }

        public void CloseAll()
        {
            int closed = CloseAllInternal();
            Patch(null);
            if (closed > 0) Notify(NoticeKind.Info, "Closed " + closed + " position" + (closed == 1 ? "" : "s"));
        }

        int CloseAllInternal()
        {
            _engine.CancelAllPending();
            return _engine.CloseAll(LastIndex()).Count;
        }

        public void CancelPending(string id)
        {
            if (_engine.CancelPending(id) == null) return;
            Patch(null);
            Notify(NoticeKind.Info, "Order cancelled");
        }

        public void CancelAllPending()
        {
            int n = _engine.CancelAllPending().Count;
            Patch(null);
            if (n > 0) Notify(NoticeKind.Info, "Cancelled " + n + " order" + (n == 1 ? "" : "s"));
        }

        public bool ModifyPending(string id, PendingEdit patch)
        {
            var rejections = _engine.ModifyPending(id, patch);
            if (rejections.Count > 0)
            {
                Notify(NoticeKind.Error, Explain(rejections));
                Patch(null);
                return false;
            }
            Patch(null);
            return true;
        }

        public bool ModifyPosition(string id, long? stopLoss, long? takeProfit)
        {
            var rejections = _engine.Modify(id, stopLoss, takeProfit);
            if (rejections.Count > 0)
            {
                Notify(NoticeKind.Error, Explain(rejections));
                return false;
            }
            Patch(null);
            Notify(NoticeKind.Success, "Stop-loss / take-profit updated");
            return true;
        }

        static string Explain(IEnumerable<Rejection> rejections) { return "Order rejected: " + string.Join(" ", rejections.Select(r => r.Message)); }

        int LastIndex() { return Math.Max((_replay != null ? _replay.Revealed.Count : 1) - 1, 0); }

        // ---------- capital transfers (used by CapitalTransferService only) ----------

        /// <summary>Realized funds that may be moved to the city: min(balance, free margin), never negative.</summary>
        public Money EligibleForTransfer()
        {
            var v = _engine.View();
            var m = Money.Min(v.Balance, v.FreeMargin);
            return m.Cents > 0 ? m : Money.Zero;
        }

        /// <summary>Part of an atomic transfer: mutates the account WITHOUT notifying observers.</summary>
        public bool ApplyTransferOut(Money amount, long time, string memo)
        {
            if (amount > EligibleForTransfer()) return false;
            return Account.Withdraw(amount, time, memo);
        }

        /// <summary>Part of an atomic transfer FROM the city: deposits into the account WITHOUT notifying observers.</summary>
        public void ApplyTransferIn(Money amount, long time, string memo) { Account.Deposit(amount, time, memo); }

        /// <summary>Margin needed to open the smallest possible trade at the current price.</summary>
        public Money MinimumTradeMargin()
        {
            var inst = Instrument;
            long price = _engine.Quote != null ? _engine.Quote.Ask : (long)Math.Round(1.1 * Math.Pow(10, inst.Digits));
            return Pnl.MarginFor(inst, inst.MinLots, price, Leverage);
        }

        public void Publish() { Patch(null); }

        /// <summary>Test tools only (god mode): add money to the trading account.</summary>
        public void GrantFunds(Money amount)
        {
            Account.Grant(amount, _engine.Quote != null ? _engine.Quote.Time : 0);
            Patch(null);
            Notify(NoticeKind.Info, "God mode: +" + amount + " to the trading account");
        }

        // ---------- timeframe ----------

        /// <summary>Change the chart timeframe (bars are rebuilt from revealed candles only).</summary>
        public void SetTimeframe(long ms)
        {
            if (_replay == null || ms == _chartTf || ValidChartTf(ms) != ms) return;
            _chartTf = ms;
            _agg = new BarAggregator(ms);
            _agg.Rebuild(_replay.Revealed);
            _agg.Preview(_replay.Forming);
            Patch(null);
        }

        /// <summary>Reveal candles until the current chart bar is complete (or one candle if it already is).</summary>
        public void StepBar()
        {
            var replay = _replay;
            if (replay == null) return;
            long tf = _chartTf;
            Func<long, long> bucket = t => (long)Math.Floor(t / (double)tf);
            int steps = 0;
            do
            {
                if (!replay.Step()) { Notify(NoticeKind.Info, "End of dataset"); return; }
                steps++;
                // Stop once the bar that was just revealed into is complete (the next candle opens a new bar).
                var lastRevealed = replay.Revealed.Count > 0 ? replay.Revealed[replay.Revealed.Count - 1] : null;
                var next = replay.Revealed.Count < _candles.Count ? _candles[replay.Revealed.Count] : null;
                if (lastRevealed == null || next == null || bucket(next.Time) != bucket(lastRevealed.Time)) break;
            } while (steps < 100000);
        }

        // ---------- chart data (revealed candles only) ----------

        public ChartData ChartData()
        {
            var replay = _replay;
            if (replay == null) return new ChartData();
            var markers = new List<ChartMarker>();
            Func<int, long?> timeOf = i => i >= 0 && i < replay.Revealed.Count ? replay.Revealed[i].Time : (long?)null;
            foreach (var p in Account.Open.Concat(Account.Closed))
            {
                if (p.RunId != _runId) continue;
                long? t = timeOf(p.EntryCandleIndex);
                if (t.HasValue) markers.Add(new ChartMarker { Time = t.Value, Price = p.EntryPrice, Kind = p.Direction == Direction.Buy ? MarkerKind.Buy : MarkerKind.Sell });
                if (p.Exit != null)
                {
                    long? te = timeOf(p.Exit.CandleIndex);
                    if (te.HasValue) markers.Add(new ChartMarker { Time = te.Value, Price = p.Exit.Price, Kind = MarkerKind.Exit });
                }
            }
            var q = _engine.Quote;
            return new ChartData
            {
                Candles = _agg.Bars, Digits = Instrument.Digits, PipPoints = Instrument.PipPoints, TfMs = _chartTf,
                Bid = _livePrice ?? (q != null ? q.Bid : (long?)null),
                Ask = _livePrice.HasValue && q != null ? _livePrice.Value + (q.Ask - q.Bid) : (q != null ? q.Ask : (long?)null),
                Markers = markers, Pending = Account.Pending.ToList(), Open = Account.Open.ToList(),
            };
        }

        // ---------- state plumbing ----------

        SessionState Build(SessionStatus status, DatasetInfo dataset)
        {
            var replay = _replay;
            var q = _engine.Quote;
            var prev = State;
            return new SessionState
            {
                Status = status,
                Dataset = dataset ?? (prev != null ? prev.Dataset : null),
                Report = prev != null ? prev.Report : null,
                Error = prev != null ? prev.Error : null,
                RevealedCount = replay != null ? replay.Revealed.Count : 0,
                Total = replay != null ? replay.Total : 0,
                Now = q != null ? q.Time : (long?)null,
                Bid = q != null ? q.Bid : (long?)null,
                Ask = q != null ? q.Ask : (long?)null,
                Playing = replay != null && replay.Playing,
                Speed = replay != null ? replay.Speed : 2,
                Account = _engine.View(),
                Positions = Account.Open.Select(p => new PositionView { Position = p, Unrealized = _engine.Unrealized(p) }).ToList(),
                Pending = Account.Pending.ToList(),
                Closed = Account.Closed,
                Stats = TradingStatistics.Compute(Account.Closed),
                Notice = prev != null ? prev.Notice : null,
                RunId = _runId, ChartTfMs = _chartTf, NativeMs = _timeframeMs,
            };
        }

        void Patch(Action<SessionState> change)
        {
            var s = Build(State.Status, null);
            if (change != null) change(s);
            State = s;
            if (Changed != null) Changed();
        }

        void Notify(NoticeKind kind, string text)
        {
            State.Notice = new Notice { Seq = ++_noticeSeq, Kind = kind, Text = text };
            if (Changed != null) Changed();
        }
    }
}
