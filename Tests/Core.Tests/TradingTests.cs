using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class PnlTests
{
    [Fact]
    public void EurUsd_ten_pips_on_one_lot_is_100_dollars()
    {
        var e = Instruments.EURUSD;
        Assert.Equal(D(100), Pnl.PnlMoney(e, Direction.Buy, P("1.10000"), P("1.10100"), 100));
        Assert.Equal(D(-100), Pnl.PnlMoney(e, Direction.Sell, P("1.10000"), P("1.10100"), 100));
        Assert.Equal(D(10), Pnl.PnlMoney(e, Direction.Buy, P("1.10000"), P("1.10100"), 10));
    }

    [Fact]
    public void UsdJpy_converts_jpy_pnl_to_usd_at_the_exit_rate()
    {
        // +0.500 JPY x 100,000 = 50,000 JPY / 150.500 = $332.2259 -> $332.23
        var j = Instruments.USDJPY;
        Assert.Equal(33223, Pnl.PnlMoney(j, Direction.Buy, Units.ParsePrice("150.000", 3), Units.ParsePrice("150.500", 3), 100).Cents);
        Assert.Equal(-33223, Pnl.PnlMoney(j, Direction.Sell, Units.ParsePrice("150.000", 3), Units.ParsePrice("150.500", 3), 100).Cents);
    }

    [Fact]
    public void Margin_and_commission()
    {
        Assert.Equal(D(1100), Pnl.MarginFor(Instruments.EURUSD, 100, P("1.10000"), 100));
        Assert.Equal(D(1000), Pnl.MarginFor(Instruments.USDJPY, 100, Units.ParsePrice("150.000", 3), 100));
        Assert.Equal(D(7), Pnl.RoundTurnCommission(Instruments.EURUSD, 100));
        Assert.Equal(70, Pnl.RoundTurnCommission(Instruments.EURUSD, 10).Cents);
    }

    [Fact]
    public void Gold_and_usd_base_pairs_are_priced_in_dollars()
    {
        // 1 lot of gold (100 oz), +$10.00 move = $1,000
        Assert.Equal(100_000, Pnl.PnlMoney(Instruments.XAUUSD, Direction.Buy, 2_000_00, 2_010_00, 100).Cents);
        // 1 lot USD/JPY (100,000 units), +1.000 yen = 100,000 JPY / 151.000 = $662.25
        Assert.Equal(66_225, Pnl.PnlMoney(Instruments.USDJPY, Direction.Buy, 150_000, 151_000, 100).Cents);
    }

    [Fact]
    public void Crosses_without_usd_are_refused_not_mispriced()
    {
        var cross = Instruments.EURUSD.With(i => { i.Symbol = "EURGBP"; i.Quote = "GBP"; });
        Assert.Throws<InvalidOperationException>(() => Pnl.PnlMoney(cross, Direction.Buy, 85000, 85100, 100));
    }
}

public class OrderValidationTests
{
    [Fact]
    public void Rejects_bad_sizes()
    {
        var (_, engine) = Setup();
        Assert.Equal(RejectionCode.SizeRange, engine.Validate(Order(Direction.Buy, 0))[0].Code);
        Assert.Equal(RejectionCode.SizeRange, engine.Validate(Order(Direction.Buy, -5))[0].Code);
        Assert.Equal(RejectionCode.SizeRange, engine.Validate(Order(Direction.Buy, 20_000))[0].Code);
        Assert.Empty(engine.Validate(Order(Direction.Buy, 10)));
    }

    [Fact]
    public void Rejects_sl_tp_on_the_wrong_side()
    {
        var (_, engine) = Setup();
        Assert.Equal(RejectionCode.InvalidSl, engine.Validate(Order(Direction.Buy, sl: "1.10100"))[0].Code);
        Assert.Equal(RejectionCode.InvalidTp, engine.Validate(Order(Direction.Buy, tp: "1.09900"))[0].Code);
        Assert.Equal(RejectionCode.InvalidSl, engine.Validate(Order(Direction.Sell, sl: "1.09900"))[0].Code);
        Assert.Equal(RejectionCode.InvalidTp, engine.Validate(Order(Direction.Sell, tp: "1.10100"))[0].Code);
        Assert.Empty(engine.Validate(Order(Direction.Sell, sl: "1.10100", tp: "1.09900")));
    }

    [Fact]
    public void Rejects_insufficient_margin_and_does_not_open()
    {
        var (account, engine) = Setup(1_000);
        var r = engine.Open(Order(Direction.Buy, 100), 0, 1);
        Assert.False(r.Ok);
        Assert.Empty(account.Open);
        Assert.Equal(RejectionCode.InsufficientMargin, engine.Validate(Order(Direction.Buy, 100))[0].Code);
    }
}

public class ExecutionTests
{
    [Fact]
    public void Buys_at_ask_and_closes_at_bid_sells_at_bid_and_closes_at_ask()
    {
        var (_, engine) = Setup();
        var b = engine.Open(Order(Direction.Buy, 100), 0, 1);
        var s = engine.Open(Order(Direction.Sell, 100), 0, 1);
        Assert.Equal(P("1.10008"), b.Position.EntryPrice);
        Assert.Equal(P("1.10000"), s.Position.EntryPrice);
        var cb = engine.CloseManual(b.Position.Id, 0);
        var cs = engine.CloseManual(s.Position.Id, 0);
        Assert.Equal(P("1.10000"), cb.Exit.Price); // bid
        Assert.Equal(P("1.10008"), cs.Exit.Price); // ask
        // immediate round trip loses spread (0.8 pip = $8) + commission ($7)
        Assert.Equal(D(-8), cb.Exit.Gross);
        Assert.Equal(D(-15), cb.Exit.Net);
        Assert.Equal(D(-15), cs.Exit.Net);
    }

    [Fact]
    public void Balance_equals_start_plus_realized_and_each_trade_settles_once()
    {
        var (account, engine) = Setup();
        var r = engine.Open(Order(Direction.Buy, 100), 0, 1);
        engine.ProcessCandle(C(T(1), "1.10000", "1.10300", "1.09990", "1.10200"), T(2), 1);
        engine.CloseManual(r.Position.Id, 1);
        Assert.Null(engine.CloseManual(r.Position.Id, 1)); // no double close
        // entry 1.10008, exit bid 1.10200: +19.2 pips = $192 - $7
        Assert.Equal(D(10_000 + 192 - 7), account.Balance);
        Assert.Equal(account.ExpectedBalance, account.Balance);
        Assert.Single(account.Closed);
        Assert.Empty(account.Open);
    }

    [Fact]
    public void Equity_versus_balance_for_open_positions()
    {
        var (_, engine) = Setup();
        engine.Open(Order(Direction.Buy, 100), 0, 1);
        var v = engine.View();
        Assert.Equal(D(10_000), v.Balance);
        Assert.Equal(D(-15), v.UnrealizedPnl); // spread + commission estimate
        Assert.Equal(D(9_985), v.Equity);
        Assert.Equal(D(1_100.08), v.UsedMargin);
        Assert.Equal(v.Equity - v.UsedMargin, v.FreeMargin);
    }

    [Fact]
    public void Uses_the_dataset_spread_column_when_present()
    {
        var (_, engine) = Setup();
        engine.SetQuote(C(T(1), "1.10000", "1.10000", "1.10000", "1.10000", 20), T(2));
        Assert.Equal(P("1.10020"), engine.Quote.Ask);
    }
}

public class SlTpTests
{
    static (TradingAccount account, ExecutionEngine engine, string id) BuyWith(string sl, string tp, bool stopFirst = true, string dir = "buy")
    {
        var (account, engine) = Setup(10_000, stopFirst);
        var r = engine.Open(Order(dir == "buy" ? Direction.Buy : Direction.Sell, 100, sl, tp), 0, 1);
        Assert.True(r.Ok);
        return (account, engine, r.Position.Id);
    }

    [Fact]
    public void Buy_stop_loss_triggers_on_the_bid_low()
    {
        var (account, engine, _) = BuyWith("1.09900", "1.10300");
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10050", "1.09890", "1.09950"), T(2), 1);
        Assert.Equal(CloseReason.StopLoss, closed[0].Exit.Reason);
        Assert.Equal(P("1.09900"), closed[0].Exit.Price);
        Assert.Empty(account.Open);
    }

    [Fact]
    public void Buy_take_profit_triggers()
    {
        var (_, engine, _) = BuyWith("1.09900", "1.10300");
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10350", "1.09950", "1.10300"), T(2), 1);
        Assert.Equal(CloseReason.TakeProfit, closed[0].Exit.Reason);
        Assert.Equal(P("1.10300"), closed[0].Exit.Price);
    }

    [Fact]
    public void Stop_wins_when_one_candle_touches_both()
    {
        var (_, engine, _) = BuyWith("1.09900", "1.10300");
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10400", "1.09800", "1.10100"), T(2), 1);
        Assert.Equal(CloseReason.StopLoss, closed[0].Exit.Reason);
    }

    [Fact]
    public void Can_be_configured_target_first()
    {
        var (_, engine, _) = BuyWith("1.09900", "1.10300", stopFirst: false);
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10400", "1.09800", "1.10100"), T(2), 1);
        Assert.Equal(CloseReason.TakeProfit, closed[0].Exit.Reason);
    }

    [Fact]
    public void Gap_through_the_stop_fills_at_the_open_price()
    {
        var (_, engine, _) = BuyWith("1.09900", null);
        var closed = engine.ProcessCandle(C(T(1), "1.09700", "1.09750", "1.09600", "1.09650"), T(2), 1);
        Assert.Equal(P("1.09700"), closed[0].Exit.Price);
        Assert.Equal(CloseReason.StopLoss, closed[0].Exit.Reason);
    }

    [Fact]
    public void Sells_trigger_on_ask_prices()
    {
        var (_, engine, _) = BuyWith("1.10100", null, dir: "sell");
        // bid high 1.10095 + 0.8 pip spread = ask high 1.10103 >= SL 1.10100
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10095", "1.09990", "1.10050"), T(2), 1);
        Assert.Equal(CloseReason.StopLoss, closed[0].Exit.Reason);
        Assert.Equal(P("1.10100"), closed[0].Exit.Price);
    }

    [Fact]
    public void Does_not_trigger_on_the_candle_already_revealed_at_entry()
    {
        var (account, engine, _) = BuyWith("1.09900", null);
        engine.ProcessCandle(C(T(0), "1.10000", "1.10000", "1.09000", "1.10000"), T(1), 0);
        Assert.Single(account.Open);
    }

    [Fact]
    public void Modify_rejects_the_wrong_side_and_applies_valid_changes()
    {
        var (account, engine, id) = BuyWith("1.09900", null);
        Assert.Equal(RejectionCode.InvalidSl, engine.Modify(id, P("1.10500"), null)[0].Code);
        Assert.Equal(P("1.09900"), account.Open[0].StopLoss);
        Assert.Empty(engine.Modify(id, P("1.09800"), P("1.10500")));
        Assert.Equal(P("1.10500"), account.Open[0].TakeProfit);
    }

    [Fact]
    public void Stops_out_when_equity_falls_below_the_stop_out_level()
    {
        var (account, engine) = Setup(1_500);
        engine.Open(Order(Direction.Buy, 100), 0, 1); // 1 lot, margin ~$1,100
        var closed = engine.ProcessCandle(C(T(1), "1.10000", "1.10000", "1.08800", "1.08900"), T(2), 1);
        Assert.Equal(CloseReason.StopOut, closed[0].Exit.Reason);
        Assert.Empty(account.Open);
    }
}

public class SizingTests
{
    [Fact]
    public void Sizes_to_risk_including_spread_and_commission()
    {
        long entry = P("1.10008"), stop = P("1.09908"); // 10 pips below entry
        long? size = PositionSizer.SizeForRisk(Instruments.EURUSD, Direction.Buy, entry, stop, D(100));
        Assert.Equal(93L, size); // loss per lot = $100 + $7 = $107 => 0.93 lots
        Assert.True(PositionSizer.LossAtStop(Instruments.EURUSD, Direction.Buy, entry, stop, size.Value) <= D(100));
        Assert.True(PositionSizer.LossAtStop(Instruments.EURUSD, Direction.Buy, entry, stop, size.Value + 1) > D(100));
    }

    [Fact]
    public void Returns_null_for_a_wrong_side_stop_or_too_small_risk()
    {
        Assert.Null(PositionSizer.SizeForRisk(Instruments.EURUSD, Direction.Buy, P("1.10008"), P("1.10500"), D(100)));
        Assert.Null(PositionSizer.SizeForRisk(Instruments.EURUSD, Direction.Buy, P("1.10008"), P("1.09008"), D(1)));
    }

    [Fact]
    public void Reward_risk_from_price_distances()
    {
        Assert.Equal(2.0, PositionSizer.PriceRewardRisk(P("1.10000"), P("1.09900"), P("1.10200")));
        Assert.Null(PositionSizer.PriceRewardRisk(P("1.10000"), null, P("1.10200")));
    }
}

public class ReplayTests
{
    static List<Candle> Data()
    {
        return Enumerable.Range(0, 10).Select(i => C(T(i), "1.10000", "1.10100", "1.09900", "1.10" + i + "00")).ToList();
    }

    [Fact]
    public void Reveals_in_order_and_never_exposes_the_future()
    {
        var seen = new List<int>();
        var data = Data();
        var r = new MarketReplay(data, 3, (_, i) => seen.Add(i));
        Assert.Equal(3, r.Revealed.Count);
        Assert.Equal(new[] { 0, 1, 2 }, seen.ToArray());
        r.Step();
        Assert.Equal(data.Take(4).Select(c => c.Time).ToArray(), r.Revealed.Select(c => c.Time).ToArray());
        // the full dataset is private: nothing public lets the UI reach candle 9
        Assert.DoesNotContain(r.GetType().GetProperties().Select(p => p.Name), n => n == "All");
        Assert.True(r.Revealed.Count == 4 && r.Total == 10);
    }

    [Fact]
    public void Advances_without_skipping_candles_and_stops_at_the_end()
    {
        var seen = new List<int>();
        var r = new MarketReplay(Data(), 1, (_, i) => seen.Add(i));
        r.SetSpeed(30);
        r.Play();
        for (int i = 0; i < 20; i++) r.Advance(0.25);
        Assert.Equal(Enumerable.Range(0, 10).ToArray(), seen.ToArray());
        Assert.False(r.Playing);
        Assert.True(r.AtEnd);
        Assert.False(r.Step());
    }

    [Fact]
    public void Plays_each_candle_as_open_first_extreme_second_extreme_then_full()
    {
        var data = Data();
        var bull = C(T(1), "1.10000", "1.10300", "1.09800", "1.10200");
        var bear = C(T(2), "1.10200", "1.10300", "1.09800", "1.10000");
        var r = new MarketReplay(new[] { data[0], bull, bear }, 1, (_, __) => { });
        r.SetSpeed(1);
        r.Play();
        Action beat = () => r.Advance(0.25); // one beat at 1 candle/s
        Assert.Null(r.Forming);
        beat();
        Assert.Equal((P("1.10000"), P("1.10000"), P("1.10000")), (r.Forming.Open, r.Forming.High, r.Forming.Low));
        beat();
        // bullish: dips to the low first; the high is not shown yet
        Assert.Equal((P("1.10000"), P("1.09800"), P("1.09800")), (r.Forming.High, r.Forming.Low, r.Forming.Close));
        beat();
        Assert.Equal((P("1.10300"), P("1.09800"), P("1.10300")), (r.Forming.High, r.Forming.Low, r.Forming.Close));
        Assert.Equal(1, r.Revealed.Count);
        beat();
        Assert.Equal(2, r.Revealed.Count);
        Assert.Null(r.Forming);
        beat();
        beat();
        // bearish: spikes to the high first
        Assert.Equal((P("1.10300"), P("1.10200"), P("1.10300")), (r.Forming.High, r.Forming.Low, r.Forming.Close));
    }

    [Fact]
    public void Next_candle_completes_a_half_played_candle_and_paused_does_nothing()
    {
        var r = new MarketReplay(Data(), 1, (_, __) => { });
        Assert.Equal(0, r.Advance(1));
        r.SetSpeed(1);
        r.Play();
        r.Advance(0.25);
        r.Advance(0.25);
        Assert.NotNull(r.Forming);
        r.Step();
        Assert.Equal(2, r.Revealed.Count);
        Assert.Null(r.Forming);
    }

    /// <summary>
    /// Playing a candle in beats must give exactly the same trades as processing it whole (the no-lookahead
    /// guarantee of the live stop / pending-order handling).
    /// </summary>
    [Fact]
    public void Playing_in_beats_gives_the_same_trades_as_processing_whole_candles()
    {
        var candles = Synthetic(7, 600);
        (TradingAccount account, ExecutionEngine engine) Make()
        {
            var account = new TradingAccount(D(1_000_000), new IdSequence("a"), 0);
            return (account, new ExecutionEngine(Instruments.EURUSD, account, Cfg(), new IdSequence("p")));
        }
        var whole = Make();
        var beats = Make();
        Func<Candle, long> close = c => c.Time + 60_000;
        long seed = 12345;
        Func<double> rand = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648.0; };
        Func<double, long> pips = n => (long)Math.Round(n * 10);

        void Place((TradingAccount account, ExecutionEngine engine) e, int idx, double[] spec)
        {
            var q = e.engine.Quote;
            if (q == null) return;
            var direction = spec[0] < 0.5 ? Direction.Buy : Direction.Sell;
            bool buy = direction == Direction.Buy;
            long baseP = buy ? q.Ask : q.Bid;
            int sgn = buy ? 1 : -1;
            long? entry = spec[1] < 0.4 ? (long?)null : baseP + (spec[1] < 0.7 ? -sgn : sgn) * pips(2 + spec[4] * 10);
            long reference = entry ?? baseP;
            var req = new OrderRequest
            {
                Direction = direction, Lots = 10, Entry = entry,
                StopLoss = reference - sgn * pips(3 + spec[2] * 15),
                TakeProfit = reference + sgn * pips(3 + spec[3] * 15),
            };
            if (entry == null) e.engine.Open(req, idx, 1);
            else e.engine.PlacePending(req, idx, 1);
        }

        MarketReplay replay = null;
        replay = new MarketReplay(candles, 1, (c, i) => beats.engine.ProcessCandle(c, close(c), i));
        replay.OnForming = p => { if (p != null) beats.engine.ProcessPartial(p, close(p), replay.Revealed.Count); };
        whole.engine.ProcessCandle(candles[0], close(candles[0]), 0);
        replay.SetSpeed(1);
        replay.Play();
        int shown = replay.Revealed.Count;
        while (shown < candles.Count)
        {
            replay.Advance(0.25); // one beat
            if (replay.Revealed.Count == shown) continue;
            int i = replay.Revealed.Count - 1;
            whole.engine.ProcessCandle(candles[i], close(candles[i]), i);
            shown = replay.Revealed.Count;
            if (shown % 5 == 0)
            {
                var spec = new[] { rand(), rand(), rand(), rand(), rand() };
                Place(whole, i, spec);
                Place(beats, i, spec);
            }
        }
        Assert.True(whole.account.Closed.Count > 10);
        // Same trades with the same fills and exits; only the order of trades closing inside one candle can differ.
        Func<IEnumerable<Position>, string> byId = l => string.Join("\n", l.OrderBy(p => p.Id, StringComparer.Ordinal).Select(Describe));
        Assert.Equal(byId(whole.account.Closed), byId(beats.account.Closed));
        Assert.Equal(byId(whole.account.Open), byId(beats.account.Open));
        Assert.Equal(string.Join("\n", whole.account.Pending.Select(Describe)), string.Join("\n", beats.account.Pending.Select(Describe)));
    }

    /// <summary>A seeded random walk of valid M1 candles (test data only).</summary>
    static List<Candle> Synthetic(int seed, int n)
    {
        long s = seed * 7919 + 1;
        Func<double> rand = () => { s = (s * 1103515245 + 12345) % 2147483648; return s / 2147483648.0; };
        long price = P("1.10000");
        var list = new List<Candle>();
        for (int i = 0; i < n; i++)
        {
            long o = price;
            long c = o + (long)Math.Round((rand() - 0.5) * 60);
            long h = Math.Max(o, c) + (long)Math.Round(rand() * 20);
            long l = Math.Min(o, c) - (long)Math.Round(rand() * 20);
            list.Add(new Candle(Utc(2024, 1, 2) + i * 60_000L, o, h, l, c));
            price = c;
        }
        return list;
    }
}

public class StatisticsTests
{
    static Position Trade(double net, double? risk, long t)
    {
        return new Position
        {
            Id = t.ToString(), Symbol = "EURUSD", Direction = Direction.Buy, Lots = 100, EntryPrice = P("1.1"), RunId = 1,
            Margin = Money.Zero, Commission = Money.Zero,
            InitialRisk = risk.HasValue ? D(risk.Value) : (Money?)null,
            IsOpen = false,
            Exit = new PositionExit { Price = P("1.1"), Time = t, Reason = CloseReason.Manual, Gross = D(net), Net = D(net) },
        };
    }

    [Fact]
    public void Computes_the_defined_statistics_from_closed_trades()
    {
        var s = TradingStatistics.Compute(new[] { Trade(100, 50, 1), Trade(-50, 50, 2), Trade(-30, 50, 3), Trade(0, null, 4) });
        Assert.Equal(4, s.TotalTrades);
        Assert.Equal(1, s.Wins);
        Assert.Equal(2, s.Losses);
        Assert.Equal(1, s.Breakeven);
        Assert.Equal(0.25, s.WinRate);
        Assert.Equal(D(20), s.NetPnl);
        Assert.Equal(D(100), s.AvgWin);
        Assert.Equal(D(40), s.AvgLoss);
        Assert.Equal(100.0 / 80, s.ProfitFactor.Value, 6);
        Assert.Equal(D(80), s.MaxDrawdown); // 100 -> 20
        Assert.Equal((2 - 1 - 0.6) / 3, s.AvgRMultiple.Value, 6);
    }

    [Fact]
    public void Handles_no_trades_and_no_losses()
    {
        var e = TradingStatistics.Compute(new Position[0]);
        Assert.Null(e.WinRate);
        Assert.Null(e.ProfitFactor);
        var w = TradingStatistics.Compute(new[] { Trade(10, null, 1) });
        Assert.Null(w.ProfitFactor);
        Assert.Equal(1.0, w.WinRate);
    }
}
