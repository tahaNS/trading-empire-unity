using System.Collections.Generic;

namespace TradingEmpire.Core
{
    public enum Direction { Buy, Sell }
    public enum PendingKind { Limit, Stop }
    public enum CloseReason { Manual, StopLoss, TakeProfit, StopOut }
    /// <summary>How an order enters the market relative to the quote.</summary>
    public enum EntryClass { Market, Limit, Stop }

    public sealed class Candle
    {
        /// <summary>Candle OPEN time, ms since epoch (UTC).</summary>
        public long Time;
        /// <summary>Bid-side OHLC (as exported from MT5), in points.</summary>
        public long Open, High, Low, Close;
        public double? Volume;
        /// <summary>Spread in points if the dataset provides it.</summary>
        public long? SpreadPts;

        public Candle() { }

        public Candle(long time, long open, long high, long low, long close, long? spreadPts = null)
        {
            Time = time; Open = open; High = high; Low = low; Close = close; SpreadPts = spreadPts;
        }
    }

    public sealed class PositionExit
    {
        public long Price;
        public long Time;
        public int CandleIndex;
        public CloseReason Reason;
        public Money Gross;
        public Money Net;
    }

    public sealed class Position
    {
        public string Id;
        public string Symbol;
        public Direction Direction;
        public long Lots;
        /// <summary>Execution price: ask for buys, bid for sells.</summary>
        public long EntryPrice;
        public long EntryTime;
        public int EntryCandleIndex;
        /// <summary>Replay run this position belongs to (chart markers only show the current run).</summary>
        public int RunId;
        public long? StopLoss;
        public long? TakeProfit;
        public Money Margin;
        /// <summary>Round-turn commission charged when the position closes.</summary>
        public Money Commission;
        /// <summary>Loss if the initial stop-loss is hit, including costs; null if no SL was set.</summary>
        public Money? InitialRisk;
        public EntryClass OrderType = EntryClass.Market;
        public bool IsOpen = true;
        public PositionExit Exit;

        public Position Clone() { return (Position)MemberwiseClone(); }
    }

    public sealed class Quote
    {
        public long Bid;
        public long Ask;
        /// <summary>Time the quote refers to (close time of the latest revealed candle).</summary>
        public long Time;
    }

    /// <summary>A resting order that fills when the market reaches <see cref="Price"/> (entry side: ask for buys, bid for sells).</summary>
    public sealed class PendingOrder
    {
        public string Id;
        public string Symbol;
        public Direction Direction;
        public PendingKind Kind;
        public long Lots;
        public long Price;
        public long? StopLoss;
        public long? TakeProfit;
        public long CreatedTime;
        public int CreatedCandleIndex;
        public int RunId;
    }
}
