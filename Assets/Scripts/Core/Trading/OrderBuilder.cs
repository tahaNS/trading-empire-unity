using System;

namespace TradingEmpire.Core
{
    public static class OrderBuilder
    {
        /// <summary>Entry price a market order would fill at (ask for buys, bid for sells).</summary>
        public static long EntryFor(Direction direction, Quote q) { return direction == Direction.Buy ? q.Ask : q.Bid; }

        /// <summary>SL/TP given as a pip distance from the entry price, on the protective side for the direction.</summary>
        public static (long? stopLoss, long? takeProfit) LevelsFromPips(InstrumentDefinition inst, Direction direction, long entry, double? slPips, double? tpPips)
        {
            int sign = direction == Direction.Buy ? 1 : -1;
            long At(double pips, bool against) { return (long)Math.Floor(entry + sign * (against ? -1 : 1) * pips * inst.PipPoints + 0.5); }
            return (slPips.HasValue ? At(slPips.Value, true) : (long?)null, tpPips.HasValue ? At(tpPips.Value, false) : (long?)null);
        }

        public static OrderRequest Build(InstrumentDefinition inst, Direction direction, long size, Quote q, double? slPips, double? tpPips)
        {
            var (sl, tp) = LevelsFromPips(inst, direction, EntryFor(direction, q), slPips, tpPips);
            return new OrderRequest { Direction = direction, Lots = size, StopLoss = sl, TakeProfit = tp };
        }
    }
}
