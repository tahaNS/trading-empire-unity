using System;

namespace TradingEmpire.Core
{
    public static class Pnl
    {
        /// <summary>
        /// Profit/loss in the account currency (USD), rounded half-even to cents ONCE.
        /// Quote = USD: already USD. Base = USD (USD/JPY): P/L is in the quote currency, so divide by the exit rate.
        /// Other crosses throw rather than silently mis-pricing.
        /// </summary>
        public static Money PnlMoney(InstrumentDefinition inst, Direction direction, long entry, long exit, long lots)
        {
            long move = direction == Direction.Buy ? exit - entry : entry - exit;
            double quoteMinor = (move * (double)lots * inst.ContractSize) / Math.Pow(10, inst.Digits);
            if (inst.Quote == "USD") return Money.FromCents(MoneyMath.RoundHalfEven(quoteMinor));
            if (inst.Base == "USD") return Money.FromCents(MoneyMath.RoundHalfEven(quoteMinor / (exit / Math.Pow(10, inst.Digits))));
            throw new InvalidOperationException("Unsupported quote conversion for " + inst.Symbol);
        }

        /// <summary>Round-turn (open + close) commission for a position size.</summary>
        public static Money RoundTurnCommission(InstrumentDefinition inst, long lots)
        {
            return inst.CommissionPerLotPerSide.Scale((lots * 2) / 100.0);
        }

        /// <summary>Required margin in USD: notional / leverage.</summary>
        public static Money MarginFor(InstrumentDefinition inst, long lots, long price, double leverage)
        {
            double units = (lots / 100.0) * inst.ContractSize;
            double notionalUsd = inst.Base == "USD" ? units : units * (price / Math.Pow(10, inst.Digits));
            if (inst.Base != "USD" && inst.Quote != "USD")
                throw new InvalidOperationException("Unsupported margin for " + inst.Symbol);
            return Money.FromCents(MoneyMath.RoundHalfEven((notionalUsd * 100) / leverage));
        }
    }
}
