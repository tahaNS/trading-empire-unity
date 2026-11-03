using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public sealed class TradingStats
    {
        public int TotalTrades, Wins, Losses, Breakeven;
        /// <summary>wins / totalTrades; null with no trades. Breakeven trades count in the denominator.</summary>
        public double? WinRate;
        public Money NetPnl, GrossProfit;
        /// <summary>Sum of losses as a positive number.</summary>
        public Money GrossLoss;
        public Money? AvgWin;
        /// <summary>Average loss as a positive number.</summary>
        public Money? AvgLoss;
        /// <summary>grossProfit / grossLoss; null when there are no losing trades.</summary>
        public double? ProfitFactor;
        /// <summary>Largest peak-to-trough fall of the closed-trade balance curve (positive number).</summary>
        public Money MaxDrawdown;
        /// <summary>Average realized R over trades that had a stop-loss.</summary>
        public double? AvgRMultiple;
    }

    public static class TradingStatistics
    {
        /// <summary>
        /// Statistics from CLOSED trades only. Net P/L includes commission. Drawdown follows the balance
        /// curve built from closed trades in exit order, starting at 0.
        /// </summary>
        public static TradingStats Compute(IEnumerable<Position> closed)
        {
            int wins = 0, losses = 0, rCount = 0;
            Money grossProfit = Money.Zero, grossLoss = Money.Zero, curve = Money.Zero, peak = Money.Zero, maxDd = Money.Zero;
            double rSum = 0;
            var ordered = closed.OrderBy(p => p.Exit != null ? p.Exit.Time : 0).ToList(); // stable
            foreach (var p in ordered)
            {
                Money net = p.Exit != null ? p.Exit.Net : Money.Zero;
                if (net.Cents > 0) { wins++; grossProfit = grossProfit + net; }
                else if (net.Cents < 0) { losses++; grossLoss = grossLoss - net; }
                if (p.InitialRisk.HasValue && p.InitialRisk.Value.Cents > 0)
                {
                    rSum += (double)net.Cents / p.InitialRisk.Value.Cents;
                    rCount++;
                }
                curve = curve + net;
                if (curve > peak) peak = curve;
                if (peak - curve > maxDd) maxDd = peak - curve;
            }
            int total = ordered.Count;
            return new TradingStats
            {
                TotalTrades = total, Wins = wins, Losses = losses, Breakeven = total - wins - losses,
                WinRate = total > 0 ? (double)wins / total : (double?)null,
                NetPnl = grossProfit - grossLoss, GrossProfit = grossProfit, GrossLoss = grossLoss,
                AvgWin = wins > 0 ? grossProfit.Scale(1.0 / wins) : (Money?)null,
                AvgLoss = losses > 0 ? grossLoss.Scale(1.0 / losses) : (Money?)null,
                ProfitFactor = grossLoss.Cents > 0 ? (double)grossProfit.Cents / grossLoss.Cents : (double?)null,
                MaxDrawdown = maxDd,
                AvgRMultiple = rCount > 0 ? rSum / rCount : (double?)null,
            };
        }
    }
}
