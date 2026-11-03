using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class SyntheticTests
{
    // Reference values produced by the web version's generator (src/trading/synthetic.ts), so the two stay identical.
    [Fact]
    public void Matches_the_web_generator_for_seed_7()
    {
        var a = SyntheticData.Generate(7, 500);
        Assert.Equal(500, a.Count);
        var expected = new (int i, long time, long o, long h, long l, long c, double v)[]
        {
            (0, 1704067200000, 110000, 110008, 109994, 109994, 615),
            (1, 1704068100000, 109994, 109994, 109961, 109961, 809),
            (2, 1704069000000, 109961, 110002, 109961, 109989, 216),
            (100, 1704157200000, 110113, 110113, 110054, 110077, 632),
            (250, 1704292200000, 109803, 109820, 109802, 109817, 520),
            (499, 1704681900000, 108849, 108904, 108849, 108882, 251),
        };
        foreach (var e in expected)
        {
            var c = a[e.i];
            Assert.Equal((e.time, e.o, e.h, e.l, e.c, e.v), (c.Time, c.Open, c.High, c.Low, c.Close, c.Volume.Value));
        }
    }

    [Fact]
    public void Matches_the_web_generator_for_the_default_seed_over_3000_candles()
    {
        var b = SyntheticData.Generate(42, 3000);
        Assert.Equal((1704067200000L, 110000L, 110034L, 109998L, 110034L, 446.0), (b[0].Time, b[0].Open, b[0].High, b[0].Low, b[0].Close, b[0].Volume.Value));
        var last = b[2999];
        Assert.Equal((1707759900000L, 111450L, 111450L, 111422L, 111422L, 741.0), (last.Time, last.Open, last.High, last.Low, last.Close, last.Volume.Value));
    }

    [Fact]
    public void Is_deterministic_valid_and_sorted()
    {
        var a = SyntheticData.Generate(7, 500);
        var b = SyntheticData.Generate(7, 500);
        Assert.Equal(a.Select(Candle2), b.Select(Candle2));
        for (int i = 0; i < a.Count; i++)
        {
            Assert.True(a[i].High >= Math.Max(a[i].Open, a[i].Close));
            Assert.True(a[i].Low <= Math.Min(a[i].Open, a[i].Close));
            if (i > 0) Assert.True(a[i].Time > a[i - 1].Time);
        }
    }

    static string Candle2(Candle c) { return c.Time + "," + c.Open + "," + c.High + "," + c.Low + "," + c.Close + "," + c.Volume; }
}

public class TimeframeTests
{
    const long Min = 60000;
    static Candle M1(int i, long o, long h, long l, long c) { return new Candle(i * Min, o, h, l, c) { Volume = 10 }; }

    [Fact]
    public void Offers_2m_only_for_data_fine_enough_to_build_it()
    {
        var m1 = Timeframes.Available(Min).Select(t => t.Label).ToList();
        foreach (var want in new[] { "1m", "2m", "5m", "15m", "1h", "4h", "1D" }) Assert.Contains(want, m1);
        var m15 = Timeframes.Available(15 * Min).Select(t => t.Label).ToList();
        Assert.DoesNotContain("2m", m15);
        Assert.Equal(new[] { "15m", "30m", "1h", "2h", "4h", "1D" }, m15.ToArray());
        Assert.Contains("2m", Timeframes.Available(2 * Min).Select(t => t.Label));
    }

    [Fact]
    public void Builds_aligned_2_minute_bars_incrementally()
    {
        var agg = new BarAggregator(2 * Min);
        Assert.True(agg.Append(M1(0, 100, 105, 99, 102))); // opens the 00:00 bar
        Assert.False(agg.Append(M1(1, 102, 110, 101, 108))); // joins it
        Assert.Single(agg.Bars);
        var b = agg.Bars[0];
        Assert.Equal((100L, 110L, 99L, 108L, 20.0), (b.Open, b.High, b.Low, b.Close, b.Volume.Value));
        Assert.True(agg.Append(M1(2, 108, 109, 95, 96))); // new bar
        Assert.Equal(2, agg.Bars.Count);
        Assert.Equal((108L, 109L, 95L, 96L), (agg.Bars[1].Open, agg.Bars[1].High, agg.Bars[1].Low, agg.Bars[1].Close));
        foreach (var bar in agg.Bars) Assert.Equal(0, bar.Time % (2 * Min));
    }

    [Fact]
    public void Previews_a_partly_played_candle_and_takes_it_back_out()
    {
        var agg = new BarAggregator(2 * Min);
        agg.Append(M1(0, 100, 105, 99, 102));
        var before = agg.Bars.Select(b => (b.Time, b.Open, b.High, b.Low, b.Close)).ToList();
        agg.Preview(M1(1, 102, 102, 100, 100)); // joins the open 2m bar
        Assert.Single(agg.Bars);
        Assert.Equal((100L, 105L, 99L, 100L), (agg.Bars[0].Open, agg.Bars[0].High, agg.Bars[0].Low, agg.Bars[0].Close));
        agg.Preview(null);
        Assert.Equal(before, agg.Bars.Select(b => (b.Time, b.Open, b.High, b.Low, b.Close)).ToList());
        agg.Preview(M1(2, 108, 108, 108, 108)); // opens a new bar
        Assert.Equal(2, agg.Bars.Count);
        agg.Append(M1(2, 108, 109, 95, 96)); // the real candle replaces the preview
        Assert.Equal(2, agg.Bars.Count);
        Assert.Equal((108L, 109L, 95L, 96L), (agg.Bars[1].Open, agg.Bars[1].High, agg.Bars[1].Low, agg.Bars[1].Close));
    }

    [Fact]
    public void Rebuilding_equals_building_one_by_one()
    {
        var candles = SyntheticData.Generate(4, 500, Min);
        var a = new BarAggregator(5 * Min);
        foreach (var c in candles) a.Append(c);
        var b = new BarAggregator(5 * Min);
        b.Rebuild(candles);
        Func<BarAggregator, string> dump = x => string.Join(";", x.Bars.Select(k => k.Time + "," + k.Open + "," + k.High + "," + k.Low + "," + k.Close));
        Assert.Equal(dump(a), dump(b));
        var first = a.Bars[3];
        var inside = candles.Where(c => Timeframes.BucketStart(c.Time, 5 * Min) == first.Time).ToList();
        Assert.Equal(inside.Max(c => c.High), first.High);
        Assert.Equal(inside.Min(c => c.Low), first.Low);
    }
}

public class SessionTests
{
    static (TradingSession s, List<Candle> candles) Make(int count = 400)
    {
        var s = new TradingSession();
        var candles = SyntheticData.Generate(3, count);
        s.LoadDataset(candles, "test", true);
        return (s, candles);
    }

    [Fact]
    public void Exposes_only_revealed_candles_through_chart_data()
    {
        var (s, candles) = Make();
        int revealed = s.ChartData().Candles.Count;
        Assert.Equal(150, revealed);
        s.Step();
        Assert.Equal(revealed + 1, s.ChartData().Candles.Count);
        var last = s.ChartData().Candles.Last();
        Assert.Equal((candles[revealed].Time, candles[revealed].Open, candles[revealed].Close), (last.Time, last.Open, last.Close));
        // nothing public hands out the dataset: the chart only ever holds revealed bars
        Assert.True(s.ChartData().Candles.Count < candles.Count);
    }

    [Fact]
    public void Keeps_a_small_dataset_partly_hidden()
    {
        var (s, _) = Make(40);
        Assert.Equal(20, s.State.RevealedCount);
        Assert.Equal(40, s.State.Total);
    }

    [Fact]
    public void Opens_marks_to_market_and_closes_a_position()
    {
        var (s, _) = Make();
        Assert.True(s.QuickOrder(Direction.Buy, 10, 20, 40));
        Assert.Single(s.State.Positions);
        Assert.True(s.State.Account.UsedMargin.Cents > 0);
        s.ClosePosition(s.State.Positions[0].Position.Id);
        Assert.Empty(s.State.Positions);
        Assert.Single(s.State.Closed);
        // immediate round trip: 0.8 pip * 0.10 lot = $0.80 spread + $0.70 commission
        Assert.Equal(D(10_000 - 1.5), s.State.Account.Balance);
        Assert.Equal(s.Account.ExpectedBalance, s.Account.Balance);
    }

    [Fact]
    public void Rejects_invalid_orders_with_feedback_and_no_state_change()
    {
        var (s, _) = Make();
        var before = s.State.Account.Balance;
        var bad = new OrderRequest { Direction = Direction.Buy, Lots = 10, StopLoss = s.State.Ask.Value + 100 };
        Assert.False(s.PlaceOrder(bad));
        Assert.Equal(NoticeKind.Error, s.State.Notice.Kind);
        Assert.Empty(s.State.Positions);
        Assert.Equal(before, s.State.Account.Balance);
    }

    [Fact]
    public void Restart_closes_positions_and_rewinds_the_replay_keeping_the_account()
    {
        var (s, _) = Make();
        s.QuickOrder(Direction.Sell, 10, null, null);
        s.Step();
        s.Restart();
        Assert.Empty(s.State.Positions);
        Assert.Single(s.State.Closed);
        Assert.Equal(150, s.State.RevealedCount);
        Assert.Equal(s.Account.ExpectedBalance, s.Account.Balance);
    }

    [Fact]
    public void Switching_the_dataset_symbol_changes_the_instrument()
    {
        var s = new TradingSession();
        Assert.Equal("EURUSD", s.Instrument.Symbol);
        var gold = Enumerable.Range(0, 60).Select(i => new Candle(Utc(2024, 1, 2) + i * 60_000L, 2_350_00 + i, 2_350_50 + i, 2_349_50 + i, 2_350_20 + i)).ToList();
        s.LoadDataset(gold, "XAUUSD_M1.csv", false, null, null, "XAUUSD");
        Assert.Equal("XAUUSD", s.Instrument.Symbol);
        Assert.Equal("XAUUSD", s.State.Dataset.Symbol);
        Assert.Equal(60000, s.State.NativeMs);
        Assert.True(s.QuickOrder(Direction.Buy, 1, 100, 200)); // 0.01 lot = 1 oz
        s.LoadSynthetic();
        Assert.Equal("EURUSD", s.Instrument.Symbol);
    }

    [Fact]
    public void Chart_timeframe_can_be_switched_to_a_multiple_of_the_dataset()
    {
        var s = new TradingSession();
        s.LoadDataset(SyntheticData.Generate(5, 600, 60_000), "m1", true);
        s.SetTimeframe(5 * 60_000);
        Assert.Equal(5 * 60_000, s.State.ChartTfMs);
        Assert.All(s.ChartData().Candles, b => Assert.Equal(0, b.Time % (5 * 60_000)));
        s.SetTimeframe(7 * 60_000); // not a multiple: ignored
        Assert.Equal(5 * 60_000, s.State.ChartTfMs);
    }

    [Fact]
    public void Advancing_while_playing_reveals_candles_and_raises_changed()
    {
        var (s, _) = Make();
        int changed = 0;
        s.Changed += () => changed++;
        s.SetSpeed(10);
        s.Play();
        int before = s.State.RevealedCount;
        for (int i = 0; i < 20; i++) s.Advance(0.1);
        Assert.True(s.State.RevealedCount > before);
        Assert.True(changed > 0);
    }
}
