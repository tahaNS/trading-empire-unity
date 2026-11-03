using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;

static class H
{
    public static long P(string s) { return Units.ParsePrice(s, 5); }
    public static Candle C(long time, string o, string h, string l, string c, long? spread = null)
    {
        return new Candle(time, P(o), P(h), P(l), P(c), spread);
    }
    public static long T(int i) { return new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() + i * 15 * 60_000L; }
    public static long Utc(int y, int mo, int d, int h = 0, int mi = 0) { return new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(); }
    public static Money D(double dollars) { return Money.FromDollars(dollars); }

    public static EngineConfig Cfg(bool stopFirst = true) { return new EngineConfig { Leverage = 100, StopOutLevel = 0.5, StopFirst = stopFirst }; }

    public static (TradingAccount account, ExecutionEngine engine) Setup(double balance = 10_000, bool stopFirst = true)
    {
        var account = new TradingAccount(D(balance), new IdSequence("a"), 0);
        var engine = new ExecutionEngine(Instruments.EURUSD, account, Cfg(stopFirst), new IdSequence("p"));
        // quote: bid 1.10000, ask 1.10008 (fixed 0.8 pip spread)
        engine.SetQuote(C(T(0), "1.10000", "1.10000", "1.10000", "1.10000"), T(1));
        return (account, engine);
    }

    public static OrderRequest Order(Direction dir, long lots = 10, string sl = null, string tp = null, string entry = null)
    {
        return new OrderRequest
        {
            Direction = dir, Lots = lots,
            StopLoss = sl != null ? P(sl) : (long?)null,
            TakeProfit = tp != null ? P(tp) : (long?)null,
            Entry = entry != null ? P(entry) : (long?)null,
        };
    }

    /// <summary>Everything that identifies a trade's outcome, for comparing two runs.</summary>
    public static string Describe(Position p)
    {
        return string.Join("|", p.Id, p.Symbol, p.Direction, p.Lots, p.EntryPrice, p.EntryTime, p.EntryCandleIndex, p.StopLoss, p.TakeProfit,
            p.Margin.Cents, p.OrderType, p.IsOpen, p.Exit == null ? "-" : string.Join(",", p.Exit.Price, p.Exit.Time, p.Exit.CandleIndex, p.Exit.Reason, p.Exit.Gross.Cents, p.Exit.Net.Cents));
    }

    public static string Describe(PendingOrder o)
    {
        return string.Join("|", o.Id, o.Direction, o.Kind, o.Lots, o.Price, o.StopLoss, o.TakeProfit, o.CreatedCandleIndex);
    }
}
