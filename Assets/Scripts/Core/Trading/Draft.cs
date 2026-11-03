using System;

namespace TradingEmpire.Core
{
    /// <summary>The order being planned: entry (null = market), stop-loss, take-profit and how the size is chosen. The chart and the order panel edit this one object.</summary>
    public sealed class OrderDraft
    {
        public Direction Direction;
        /// <summary>Null = market. Anything else is a pending (limit/stop) order at that price.</summary>
        public long? Entry;
        public long StopLoss;
        public long? TakeProfit;
        /// <summary>True = size from the risk percentage; false = the fixed lots.</summary>
        public bool RiskSizing = true;
        public double RiskPct = 1;
        public long Lots = 10;

        public OrderDraft Copy() { return (OrderDraft)MemberwiseClone(); }
    }

    public sealed class DraftResolution
    {
        public OrderRequest Order;
        public long? Lots;
        /// <summary>Where the order would fill: the market fill price, or the pending level.</summary>
        public long EntryPrice;
        public EntryClass Kind;
        /// <summary>Why no order could be formed (shown to the player), else null.</summary>
        public string Issue;
    }

    public sealed class DraftView
    {
        public OrderDraft Draft;
        public DraftResolution Resolution;
        public OrderPreview Preview;
        /// <summary>The first reason the engine would refuse the order, else null.</summary>
        public string Rejection;
        public bool Valid { get { return Resolution.Order != null && Rejection == null; } }
    }

    public static class Drafts
    {
        public const double DefaultSlPips = 20, DefaultRewardRisk = 2, DefaultRiskPct = 1;
        /// <summary>Dragging the entry within this many pips of the market snaps it back to a market order.</summary>
        public const double MarketSnapPips = 0.5;

        public static OrderDraft Create(InstrumentDefinition inst, Direction direction, Quote q, OrderDraft previous = null)
        {
            long entry = OrderBuilder.EntryFor(direction, q);
            int sign = direction == Direction.Buy ? 1 : -1;
            double slDist = DefaultSlPips * inst.PipPoints;
            return new OrderDraft
            {
                Direction = direction, Entry = null,
                StopLoss = (long)Math.Round(entry - sign * slDist),
                TakeProfit = (long)Math.Round(entry + sign * slDist * DefaultRewardRisk),
                RiskSizing = previous != null ? previous.RiskSizing : true,
                RiskPct = previous != null ? previous.RiskPct : DefaultRiskPct,
                Lots = previous != null ? previous.Lots : 10,
            };
        }

        /// <summary>A dragged or typed entry price: close to the market it snaps to a market order (null).</summary>
        public static long? NormalizeEntry(InstrumentDefinition inst, Direction direction, long price, Quote q)
        {
            return Math.Abs(price - OrderBuilder.EntryFor(direction, q)) / (double)inst.PipPoints <= MarketSnapPips ? (long?)null : price;
        }

        /// <summary>Work out the lot size (from risk or fixed) and the order request.</summary>
        public static DraftResolution Resolve(OrderDraft d, InstrumentDefinition inst, Quote q, Money balance)
        {
            long entryPrice = d.Entry ?? OrderBuilder.EntryFor(d.Direction, q);
            var kind = d.Entry == null ? EntryClass.Market : PendingRules.ClassifyEntry(d.Direction, d.Entry.Value, q);
            long? lots = d.Lots;
            string issue = null;
            if (d.RiskSizing)
            {
                if (!(d.RiskPct > 0 && d.RiskPct <= 100)) { lots = null; issue = "Risk % must be between 0 and 100."; }
                else
                {
                    lots = PositionSizer.SizeForRisk(inst, d.Direction, entryPrice, d.StopLoss, balance.Scale(d.RiskPct / 100));
                    if (lots == null) issue = "Risk is too small for the minimum lot size (or the stop is on the wrong side).";
                }
            }
            var res = new DraftResolution { Lots = lots, EntryPrice = entryPrice, Kind = kind, Issue = issue };
            if (lots == null) return res;
            res.Order = new OrderRequest { Direction = d.Direction, Lots = lots.Value, StopLoss = d.StopLoss, TakeProfit = d.TakeProfit, Entry = kind == EntryClass.Market ? (long?)null : entryPrice };
            return res;
        }
    }
}
