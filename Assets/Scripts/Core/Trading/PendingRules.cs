using System;

namespace TradingEmpire.Core
{
    public static class PendingRules
    {
        /// <summary>What kind of order an entry price is relative to the quote. Buys use the ask, sells the bid. Equal = market.</summary>
        public static EntryClass ClassifyEntry(Direction direction, long entry, Quote q)
        {
            if (direction == Direction.Buy) return entry < q.Ask ? EntryClass.Limit : entry > q.Ask ? EntryClass.Stop : EntryClass.Market;
            return entry > q.Bid ? EntryClass.Limit : entry < q.Bid ? EntryClass.Stop : EntryClass.Market;
        }

        /// <summary>
        /// Fill rule for a resting order against one candle (bid OHLC; buys trade at ask = bid + spread).
        /// If the candle OPENS beyond the order level the fill is the open price (better for limits, worse for stops).
        /// </summary>
        public static long? EvaluateFill(Direction direction, PendingKind kind, long level, Candle c, long spreadPts)
        {
            bool buy = direction == Direction.Buy;
            long adj = buy ? spreadPts : 0;
            long open = c.Open + adj, high = c.High + adj, low = c.Low + adj;
            if (buy && kind == PendingKind.Limit)
            {
                if (open <= level) return open;
                if (low <= level) return level;
            }
            else if (buy)
            {
                if (open >= level) return open;
                if (high >= level) return level;
            }
            else if (kind == PendingKind.Limit)
            {
                if (open >= level) return open;
                if (high >= level) return level;
            }
            else
            {
                if (open <= level) return open;
                if (low <= level) return level;
            }
            return null;
        }
    }
}
