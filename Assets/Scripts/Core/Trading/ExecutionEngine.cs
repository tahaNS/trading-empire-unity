using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public sealed class EngineConfig
    {
        public double Leverage = 100;
        /// <summary>Stop-out when equity &lt; usedMargin * StopOutLevel.</summary>
        public double StopOutLevel = 0.5;
        /// <summary>
        /// If one candle touches both SL and TP the real order is unknown. Stop-first (default, conservative)
        /// assumes the stop was hit first; the player never gets the better outcome by luck.
        /// </summary>
        public bool StopFirst = true;
    }

    public sealed class OrderRequest
    {
        public Direction Direction;
        public long Lots;
        public long? StopLoss;
        public long? TakeProfit;
        /// <summary>Entry price for a pending order (limit/stop). Null = market order; a price equal to the market is a market order.</summary>
        public long? Entry;

        public OrderRequest Copy() { return (OrderRequest)MemberwiseClone(); }
    }

    public enum RejectionCode { NoQuote, SizeRange, SizeStep, InvalidSl, InvalidTp, InsufficientMargin, AtMarket }

    public sealed class Rejection
    {
        public RejectionCode Code;
        public string Message;
        public Rejection(RejectionCode code, string message) { Code = code; Message = message; }
    }

    /// <summary>What happened to resting orders while a candle was processed (drained by the session).</summary>
    public sealed class PendingEvent
    {
        public bool Filled; // false = cancelled for insufficient margin
        public PendingOrder Order;
        public Position Position;
    }

    public sealed class OrderPreview
    {
        /// <summary>Price the order would fill at (market fill price, or the pending order's level).</summary>
        public long Entry;
        public EntryClass Kind;
        public Money Margin, Commission;
        /// <summary>Loss if the SL is hit (incl. costs); null without an SL.</summary>
        public Money? Risk;
        /// <summary>Profit if the TP is hit (net of costs); null without a TP.</summary>
        public Money? Reward;
        public double? RewardRisk;
        public List<Rejection> Rejections;
    }

    public sealed class OpenResult
    {
        public bool Ok;
        public Position Position;
        public List<Rejection> Rejections;
    }

    public sealed class PlaceResult
    {
        public bool Ok;
        public PendingOrder Order;
        public List<Rejection> Rejections;
    }

    public sealed class PendingEdit
    {
        public long? Price;
        public bool SetStopLoss; public long? StopLoss;
        public bool SetTakeProfit; public long? TakeProfit;
    }

    public sealed class PendingEditCheck
    {
        public List<Rejection> Rejections;
        public EntryClass Kind;
        public OrderRequest Next;
    }

    /// <summary>
    /// Order validation, execution and SL/TP evaluation against the REVEALED candles only.
    /// Price policy (data is bid OHLC): ask = bid + spread (candle SPREAD column, else the instrument's fixed
    /// spread). Buys open at ask and close at bid; sells open at bid and close at ask. Market orders fill at the
    /// close of the latest revealed candle (no future data). Buy SL/TP are compared against bid prices; sell
    /// SL/TP against ask. Fills are at the SL/TP level; if a candle OPENS beyond the level (gap) the fill is the
    /// open price. No slippage. Margin = notional / leverage.
    /// </summary>
    public sealed class ExecutionEngine
    {
        public Quote Quote;
        public InstrumentDefinition Instrument { get; private set; }
        public readonly TradingAccount Account;
        readonly EngineConfig _config;
        readonly IdSequence _nextPositionId;
        readonly IdSequence _nextOrderId;
        long _lastSpread;
        List<PendingEvent> _events = new List<PendingEvent>();

        public ExecutionEngine(InstrumentDefinition instrument, TradingAccount account, EngineConfig config, IdSequence nextPositionId, IdSequence nextOrderId = null)
        {
            Instrument = instrument;
            Account = account;
            _config = config;
            _nextPositionId = nextPositionId;
            _nextOrderId = nextOrderId ?? new IdSequence("o");
            _lastSpread = instrument.FixedSpreadPts;
        }

        /// <summary>Switch symbol (only between datasets, with nothing open).</summary>
        public void SetInstrument(InstrumentDefinition instrument)
        {
            Instrument = instrument;
            _lastSpread = instrument.FixedSpreadPts;
        }

        /// <summary>Continue numbering after the given (restored) positions and orders.</summary>
        public void ResumeIds(IEnumerable<Position> positions, IEnumerable<PendingOrder> orders = null)
        {
            _nextPositionId.ResetAfter(positions.Select(p => p.Id));
            _nextOrderId.ResetAfter((orders ?? new PendingOrder[0]).Select(o => o.Id));
        }

        /// <summary>Fills/cancellations of resting orders since the last call.</summary>
        public List<PendingEvent> DrainEvents()
        {
            var e = _events;
            _events = new List<PendingEvent>();
            return e;
        }

        long SpreadOf(Candle c) { return c.SpreadPts ?? Instrument.FixedSpreadPts; }

        /// <summary>Update the quote from a revealed candle's close (does NOT evaluate SL/TP).</summary>
        public void SetQuote(Candle c, long closeTime)
        {
            _lastSpread = SpreadOf(c);
            Quote = new Quote { Bid = c.Close, Ask = c.Close + _lastSpread, Time = closeTime };
        }

        // ---------- validation & preview ----------

        EntryClass EntryOf(OrderRequest order, Quote q, out long price)
        {
            bool buy = order.Direction == Direction.Buy;
            var kind = order.Entry.HasValue ? PendingRules.ClassifyEntry(order.Direction, order.Entry.Value, q) : EntryClass.Market;
            price = kind == EntryClass.Market ? (buy ? q.Ask : q.Bid) : order.Entry.Value;
            return kind;
        }

        public OrderPreview Preview(OrderRequest order)
        {
            var q = Quote;
            if (q == null) return null;
            var inst = Instrument;
            var kind = EntryOf(order, q, out long entry);
            var rejections = Validate(order);
            Money commission = Pnl.RoundTurnCommission(inst, order.Lots);
            return new OrderPreview
            {
                Entry = entry, Kind = kind,
                Margin = Pnl.MarginFor(inst, order.Lots, entry, _config.Leverage),
                Commission = commission,
                Risk = order.StopLoss.HasValue ? PositionSizer.LossAtStop(inst, order.Direction, entry, order.StopLoss.Value, order.Lots) : (Money?)null,
                Reward = order.TakeProfit.HasValue ? Pnl.PnlMoney(inst, order.Direction, entry, order.TakeProfit.Value, order.Lots) - commission : (Money?)null,
                RewardRisk = PositionSizer.PriceRewardRisk(entry, order.StopLoss, order.TakeProfit),
                Rejections = rejections,
            };
        }

        public List<Rejection> Validate(OrderRequest order)
        {
            var output = new List<Rejection>();
            var q = Quote;
            var inst = Instrument;
            if (q == null) { output.Add(new Rejection(RejectionCode.NoQuote, "No market price yet.")); return output; }
            if (order.Lots < inst.MinLots || order.Lots > inst.MaxLots)
                output.Add(new Rejection(RejectionCode.SizeRange, "Size must be between " + inst.MinLots / 100.0 + " and " + inst.MaxLots / 100.0 + " lots."));
            else if (order.Lots % inst.StepLots != 0)
                output.Add(new Rejection(RejectionCode.SizeStep, "Size must be a multiple of " + inst.StepLots / 100.0 + " lot."));
            bool buy = order.Direction == Direction.Buy;
            var kind = EntryOf(order, q, out long entry);
            if (kind == EntryClass.Market)
            {
                // Exit side: buys exit at bid, sells at ask. A level already crossed would trigger instantly.
                CheckLevels(output, buy, order.StopLoss, order.TakeProfit, buy ? q.Bid : q.Ask, "the current bid", "the current ask");
            }
            else
            {
                // Resting order: protective levels are measured from the order's own entry price.
                CheckLevels(output, buy, order.StopLoss, order.TakeProfit, entry, "the entry price", "the entry price");
            }
            if (output.Count == 0)
            {
                Money required = Pnl.MarginFor(inst, order.Lots, entry, _config.Leverage);
                if (required > View().FreeMargin)
                    output.Add(new Rejection(RejectionCode.InsufficientMargin, "Insufficient free margin for this size."));
            }
            return output;
        }

        static void CheckLevels(List<Rejection> output, bool buy, long? sl, long? tp, long reference, string buyRef, string sellRef)
        {
            if (sl.HasValue && (buy ? sl.Value >= reference : sl.Value <= reference))
                output.Add(new Rejection(RejectionCode.InvalidSl, buy ? "Stop-loss must be below " + buyRef + "." : "Stop-loss must be above " + sellRef + "."));
            if (tp.HasValue && (buy ? tp.Value <= reference : tp.Value >= reference))
                output.Add(new Rejection(RejectionCode.InvalidTp, buy ? "Take-profit must be above " + buyRef + "." : "Take-profit must be below " + sellRef + "."));
        }

        // ---------- opening / closing ----------

        Position MakePosition(Direction direction, long lots, long entry, long? stopLoss, long? takeProfit, long time, int candleIndex, int runId, EntryClass orderType)
        {
            var inst = Instrument;
            return new Position
            {
                Id = _nextPositionId.Next(), Symbol = inst.Symbol, Direction = direction, Lots = lots,
                EntryPrice = entry, EntryTime = time, EntryCandleIndex = candleIndex, RunId = runId,
                StopLoss = stopLoss, TakeProfit = takeProfit,
                Margin = Pnl.MarginFor(inst, lots, entry, _config.Leverage),
                Commission = Pnl.RoundTurnCommission(inst, lots),
                InitialRisk = stopLoss.HasValue ? PositionSizer.LossAtStop(inst, direction, entry, stopLoss.Value, lots) : (Money?)null,
                OrderType = orderType, IsOpen = true,
            };
        }

        /// <summary>Open a MARKET position (fills at ask for buys, bid for sells). Pending entries use <see cref="PlacePending"/>.</summary>
        public OpenResult Open(OrderRequest order, int candleIndex, int runId)
        {
            var asMarket = order.Copy();
            asMarket.Entry = null;
            var rejections = Validate(asMarket);
            var q = Quote;
            if (rejections.Count > 0 || q == null) return new OpenResult { Ok = false, Rejections = rejections };
            long entry = order.Direction == Direction.Buy ? q.Ask : q.Bid;
            var position = MakePosition(order.Direction, order.Lots, entry, order.StopLoss, order.TakeProfit, q.Time, candleIndex, runId, EntryClass.Market);
            Account.Open.Add(position);
            return new OpenResult { Ok = true, Position = position };
        }

        // ---------- resting (pending) orders ----------

        /// <summary>Place a limit/stop order at <c>order.Entry</c>. Rejects entries at the market price.</summary>
        public PlaceResult PlacePending(OrderRequest order, int candleIndex, int runId)
        {
            var q = Quote;
            if (q == null) return new PlaceResult { Ok = false, Rejections = new List<Rejection> { new Rejection(RejectionCode.NoQuote, "No market price yet.") } };
            var kind = EntryOf(order, q, out long price);
            if (kind == EntryClass.Market)
                return new PlaceResult { Ok = false, Rejections = new List<Rejection> { new Rejection(RejectionCode.AtMarket, "The entry is at the market price; use a market order.") } };
            var rejections = Validate(order);
            if (rejections.Count > 0) return new PlaceResult { Ok = false, Rejections = rejections };
            var pending = new PendingOrder
            {
                Id = _nextOrderId.Next(), Symbol = Instrument.Symbol, Direction = order.Direction,
                Kind = kind == EntryClass.Limit ? PendingKind.Limit : PendingKind.Stop,
                Lots = order.Lots, Price = price, StopLoss = order.StopLoss, TakeProfit = order.TakeProfit,
                CreatedTime = q.Time, CreatedCandleIndex = candleIndex, RunId = runId,
            };
            Account.Pending.Add(pending);
            return new PlaceResult { Ok = true, Order = pending };
        }

        public PendingOrder CancelPending(string id)
        {
            int i = Account.Pending.FindIndex(o => o.Id == id);
            if (i < 0) return null;
            var removed = Account.Pending[i];
            Account.Pending.RemoveAt(i);
            return removed;
        }

        public List<PendingOrder> CancelAllPending()
        {
            var all = Account.Pending.ToList();
            Account.Pending.Clear();
            return all;
        }

        /// <summary>Validate an edit of a resting order WITHOUT applying it (used for live drag previews).</summary>
        public PendingEditCheck CheckPendingEdit(string id, PendingEdit patch)
        {
            var o = Account.Pending.FirstOrDefault(x => x.Id == id);
            var q = Quote;
            if (o == null || q == null)
                return new PendingEditCheck { Rejections = new List<Rejection> { new Rejection(RejectionCode.NoQuote, "Order or market price not available.") }, Kind = EntryClass.Market, Next = null };
            var next = new OrderRequest
            {
                Direction = o.Direction, Lots = o.Lots,
                Entry = patch.Price ?? o.Price,
                StopLoss = patch.SetStopLoss ? patch.StopLoss : o.StopLoss,
                TakeProfit = patch.SetTakeProfit ? patch.TakeProfit : o.TakeProfit,
            };
            var kind = EntryOf(next, q, out long _);
            if (kind == EntryClass.Market)
                return new PendingEditCheck { Rejections = new List<Rejection> { new Rejection(RejectionCode.AtMarket, "The order would be at the market price; cancel it or move it away.") }, Kind = kind, Next = next };
            return new PendingEditCheck { Rejections = Validate(next), Kind = kind, Next = next };
        }

        /// <summary>Move a resting order's level and/or its SL/TP. Validated like a new order.</summary>
        public List<Rejection> ModifyPending(string id, PendingEdit patch)
        {
            var check = CheckPendingEdit(id, patch);
            var o = Account.Pending.FirstOrDefault(x => x.Id == id);
            if (check.Rejections.Count > 0 || o == null || check.Next == null) return check.Rejections;
            o.Price = check.Next.Entry.Value;
            o.Kind = check.Kind == EntryClass.Limit ? PendingKind.Limit : PendingKind.Stop;
            o.StopLoss = check.Next.StopLoss;
            o.TakeProfit = check.Next.TakeProfit;
            return new List<Rejection>();
        }

        /// <summary>Close at an explicit price.</summary>
        Position CloseAt(Position p, long price, long time, int candleIndex, CloseReason reason)
        {
            Money gross = Pnl.PnlMoney(Instrument, p.Direction, p.EntryPrice, price, p.Lots);
            Money net = gross - p.Commission;
            var closed = p.Clone();
            closed.IsOpen = false;
            closed.Exit = new PositionExit { Price = price, Time = time, CandleIndex = candleIndex, Reason = reason, Gross = gross, Net = net };
            Account.SettleTrade(closed, net, time);
            return closed;
        }

        /// <summary>Manual close at the current market (bid for buys, ask for sells).</summary>
        public Position CloseManual(string id, int candleIndex)
        {
            var p = Account.Open.FirstOrDefault(x => x.Id == id);
            var q = Quote;
            if (p == null || q == null) return null;
            return CloseAt(p, p.Direction == Direction.Buy ? q.Bid : q.Ask, q.Time, candleIndex, CloseReason.Manual);
        }

        public List<Position> CloseAll(int candleIndex, CloseReason reason = CloseReason.Manual)
        {
            var q = Quote;
            if (q == null) return new List<Position>();
            return Account.Open.ToList().Select(p => CloseAt(p, p.Direction == Direction.Buy ? q.Bid : q.Ask, q.Time, candleIndex, reason)).ToList();
        }

        /// <summary>Validate new SL/TP levels for an open position WITHOUT applying them.</summary>
        public List<Rejection> CheckModify(string id, long? stopLoss, long? takeProfit)
        {
            var p = Account.Open.FirstOrDefault(x => x.Id == id);
            var q = Quote;
            if (p == null || q == null) return new List<Rejection> { new Rejection(RejectionCode.NoQuote, "Position or market price not available.") };
            bool buy = p.Direction == Direction.Buy;
            var output = new List<Rejection>();
            CheckLevels(output, buy, stopLoss, takeProfit, buy ? q.Bid : q.Ask, "the current bid", "the current ask");
            return output;
        }

        /// <summary>Modify SL/TP of an open position with the same validation as a new order.</summary>
        public List<Rejection> Modify(string id, long? stopLoss, long? takeProfit)
        {
            var output = CheckModify(id, stopLoss, takeProfit);
            var p = Account.Open.FirstOrDefault(x => x.Id == id);
            if (output.Count == 0 && p != null)
            {
                p.StopLoss = stopLoss;
                p.TakeProfit = takeProfit;
            }
            return output;
        }

        /// <summary>
        /// Process a newly revealed candle: evaluate SL/TP for positions opened BEFORE this candle, then refresh
        /// the quote, then apply stop-out. Returns positions closed by this candle.
        /// </summary>
        public List<Position> ProcessCandle(Candle c, long closeTime, int index)
        {
            var closed = new List<Position>();
            foreach (var p in Account.Open.ToList())
            {
                // Opened by a resting order that filled while this candle was still forming: SL only.
                bool restingFill = p.OrderType != EntryClass.Market;
                if (restingFill && p.EntryCandleIndex == index)
                {
                    var h = EvaluateExit(p, c, true);
                    if (h != null) closed.Add(CloseAt(p, h.Price, closeTime, index, h.Reason));
                    continue;
                }
                if (p.EntryCandleIndex >= index) continue;
                var hit = EvaluateExit(p, c, false);
                if (hit != null) closed.Add(CloseAt(p, hit.Price, closeTime, index, hit.Reason));
            }
            FillPending(c, closeTime, index, closed);
            SetQuote(c, closeTime);
            if (Quote != null && Account.Open.Count > 0)
            {
                var v = View();
                if (v.Equity < v.UsedMargin.Scale(_config.StopOutLevel)) closed.AddRange(CloseAll(index, CloseReason.StopOut));
            }
            return closed;
        }

        /// <summary>
        /// React to a candle that is still forming, so stops and resting orders trigger the moment price crosses
        /// them. Only events that the finished candle would produce anyway are applied (stop-loss wins a same-candle
        /// conflict), so the result equals <see cref="ProcessCandle"/>'s. Take-profit is left to ProcessCandle.
        /// </summary>
        public List<Position> ProcessPartial(Candle partial, long closeTime, int index)
        {
            var closed = new List<Position>();
            foreach (var p in Account.Open.ToList())
            {
                if (p.EntryCandleIndex >= index) continue;
                var hit = EvaluateExit(p, partial, true);
                if (hit != null) closed.Add(CloseAt(p, hit.Price, closeTime, index, hit.Reason));
            }
            FillPending(partial, closeTime, index, closed);
            return closed;
        }

        /// <summary>
        /// Fill resting orders touched by this candle (orders created before it). Margin is checked at the fill; an
        /// order that can no longer be afforded is cancelled. On the FILL candle only the stop-loss can trigger.
        /// </summary>
        void FillPending(Candle c, long closeTime, int index, List<Position> closed)
        {
            foreach (var o in Account.Pending.ToList())
            {
                if (o.CreatedCandleIndex >= index) continue;
                long? fill = PendingRules.EvaluateFill(o.Direction, o.Kind, o.Price, c, SpreadOf(c));
                if (fill == null) continue;
                CancelPending(o.Id);
                Money required = Pnl.MarginFor(Instrument, o.Lots, fill.Value, _config.Leverage);
                if (required > View().FreeMargin)
                {
                    _events.Add(new PendingEvent { Filled = false, Order = o });
                    continue;
                }
                var position = MakePosition(o.Direction, o.Lots, fill.Value, o.StopLoss, o.TakeProfit, closeTime, index, o.RunId,
                    o.Kind == PendingKind.Limit ? EntryClass.Limit : EntryClass.Stop);
                Account.Open.Add(position);
                _events.Add(new PendingEvent { Filled = true, Order = o, Position = position });
                var hit = EvaluateExit(position, c, true);
                if (hit != null) closed.Add(CloseAt(position, hit.Price, closeTime, index, hit.Reason));
            }
        }

        sealed class ExitHit { public long Price; public CloseReason Reason; }

        ExitHit EvaluateExit(Position p, Candle c, bool slOnly)
        {
            bool buy = p.Direction == Direction.Buy;
            // Convert the bid candle to the exit side's prices (sells exit at ask = bid + spread).
            long adj = buy ? 0 : SpreadOf(c);
            long open = c.Open + adj, high = c.High + adj, low = c.Low + adj;
            long? sl = p.StopLoss;
            long? tp = slOnly ? null : p.TakeProfit;

            // Gaps: the candle opened beyond a level; the open price is the (known) fill.
            if (sl.HasValue && (buy ? open <= sl.Value : open >= sl.Value)) return new ExitHit { Price = open, Reason = CloseReason.StopLoss };
            if (tp.HasValue && (buy ? open >= tp.Value : open <= tp.Value)) return new ExitHit { Price = open, Reason = CloseReason.TakeProfit };

            bool slHit = sl.HasValue && (buy ? low <= sl.Value : high >= sl.Value);
            bool tpHit = tp.HasValue && (buy ? high >= tp.Value : low <= tp.Value);
            if (slHit && tpHit)
                return _config.StopFirst ? new ExitHit { Price = sl.Value, Reason = CloseReason.StopLoss } : new ExitHit { Price = tp.Value, Reason = CloseReason.TakeProfit };
            if (slHit) return new ExitHit { Price = sl.Value, Reason = CloseReason.StopLoss };
            if (tpHit) return new ExitHit { Price = tp.Value, Reason = CloseReason.TakeProfit };
            return null;
        }

        // ---------- account valuation ----------

        /// <summary>Unrealized P/L of a position at the current quote, net of its round-turn commission.</summary>
        public Money Unrealized(Position p)
        {
            var q = Quote;
            if (q == null) return Money.Zero;
            Money gross = Pnl.PnlMoney(Instrument, p.Direction, p.EntryPrice, p.Direction == Direction.Buy ? q.Bid : q.Ask, p.Lots);
            return gross - p.Commission;
        }

        public AccountView View()
        {
            Money unrealized = Money.Zero, margin = Money.Zero;
            foreach (var p in Account.Open)
            {
                unrealized = unrealized + Unrealized(p);
                margin = margin + p.Margin;
            }
            Money equity = Account.Balance + unrealized;
            return new AccountView
            {
                Balance = Account.Balance, Equity = equity, UsedMargin = margin, FreeMargin = equity - margin,
                UnrealizedPnl = unrealized, RealizedPnl = Account.RealizedPnl,
            };
        }
    }
}
