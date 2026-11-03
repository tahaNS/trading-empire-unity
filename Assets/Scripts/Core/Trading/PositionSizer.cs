using System;

namespace TradingEmpire.Core
{
    public static class PositionSizer
    {
        /// <summary>Loss (positive Money) if the stop-loss is hit at <paramref name="stop"/>, including round-turn commission.</summary>
        public static Money LossAtStop(InstrumentDefinition inst, Direction direction, long entry, long stop, long size)
        {
            return Pnl.RoundTurnCommission(inst, size) - Pnl.PnlMoney(inst, direction, entry, stop, size);
        }

        /// <summary>
        /// Risk-based sizing: the largest lot size (on the instrument's step) whose loss at the stop, including
        /// costs and the spread already in <paramref name="entry"/>, does not exceed <paramref name="risk"/>.
        /// Null if the stop is on the wrong side of entry or even the minimum size risks more than <paramref name="risk"/>.
        /// </summary>
        public static long? SizeForRisk(InstrumentDefinition inst, Direction direction, long entry, long stop, Money risk)
        {
            Money perLot = LossAtStop(inst, direction, entry, stop, 100);
            if (perLot.Cents <= 0 || risk.Cents <= 0) return null;
            double raw = ((double)risk.Cents / perLot.Cents) * 100;
            long step = inst.StepLots;
            long size = (long)(Math.Floor(raw / step) * step);
            size = Math.Min(size, inst.MaxLots);
            while (size >= inst.MinLots && LossAtStop(inst, direction, entry, stop, size) > risk) size -= step;
            return size >= inst.MinLots ? size : (long?)null;
        }

        /// <summary>Reward : risk as traders read it: distance to the target over distance to the stop (costs not included).</summary>
        public static double? PriceRewardRisk(long entry, long? stopLoss, long? takeProfit)
        {
            if (stopLoss == null || takeProfit == null) return null;
            double risk = Math.Abs(entry - stopLoss.Value);
            return risk > 0 ? Math.Abs(takeProfit.Value - entry) / risk : (double?)null;
        }
    }
}
