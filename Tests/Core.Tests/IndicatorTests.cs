using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class IndicatorTests
{
    static List<Candle> Bars(params long[] closes)
    {
        var l = new List<Candle>();
        for (int i = 0; i < closes.Length; i++) l.Add(new Candle(i * 60000L, closes[i], closes[i] + 10, closes[i] - 10, closes[i]));
        return l;
    }

    [Fact]
    public void Sma_is_the_average_of_the_last_n_closes_and_empty_before_that()
    {
        var c = Indicators.Compute(new IndicatorConfig { Id = "i1", Type = "sma", Params = new Dictionary<string, double> { { "period", 3 } } }, Bars(10, 20, 30, 40, 50));
        var v = c.Lines[0].Values;
        Assert.True(double.IsNaN(v[0]) && double.IsNaN(v[1]));
        Assert.Equal(20, v[2]); Assert.Equal(30, v[3]); Assert.Equal(40, v[4]);
    }

    [Fact]
    public void A_value_never_depends_on_later_bars()
    {
        var all = Bars(100, 104, 99, 107, 111, 108, 115, 120, 118, 125, 130, 128, 135, 133, 140, 138, 145, 150, 149, 155);
        foreach (var def in Indicators.All)
        {
            var cfg = new IndicatorConfig { Id = "i1", Type = def.Id, Params = Indicators.DefaultParams(def) };
            for (int cut = 5; cut < all.Count; cut += 3)
            {
                var full = Indicators.Compute(cfg, all);
                var part = Indicators.Compute(cfg, all.Take(cut).ToList());
                for (int l = 0; l < full.Lines.Count; l++)
                    for (int i = 0; i < cut; i++)
                    {
                        double a = full.Lines[l].Values[i], b = part.Lines[l].Values[i];
                        Assert.True((double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) < 1e-9, def.Id + " bar " + i + " changed when later bars were removed");
                    }
            }
        }
    }

    [Fact]
    public void Rsi_is_100_in_a_straight_rise_and_inside_0_to_100_otherwise()
    {
        var up = Indicators.Rsi(Enumerable.Range(1, 30).Select(x => (double)x).ToArray(), 14);
        Assert.Equal(100, up[29]);
        var mixed = Indicators.Rsi(new double[] { 5, 6, 5, 7, 6, 8, 7, 9, 8, 7, 9, 10, 8, 9, 11, 10, 12, 9 }, 5);
        foreach (var v in mixed.Where(x => !double.IsNaN(x))) Assert.InRange(v, 0, 100);
    }

    [Fact]
    public void Parameters_are_clamped_and_unknown_types_are_dropped_on_restore()
    {
        var def = Indicators.Get("ema");
        Assert.Equal(500, Indicators.Normalize(def, new Dictionary<string, double> { { "period", 9999 } })["period"]);
        Assert.Equal(1, Indicators.Normalize(def, new Dictionary<string, double> { { "period", -4 } })["period"]);
        var store = new IndicatorStore();
        store.Restore(new[] { new IndicatorConfig { Id = "i7", Type = "ema" }, new IndicatorConfig { Id = "i8", Type = "nope" } });
        Assert.Single(store.Items);
        Assert.Equal("i8", store.Add("rsi"));
    }

    [Fact]
    public void Indicators_survive_a_save()
    {
        var w = W.Rising();
        var id = w.Trading.Indicators.Add("bb");
        w.Trading.Indicators.Update(id, c => { c.Params["period"] = 30; c.Colors["upper"] = "#ff0000"; c.Hidden = true; });
        w.Trading.Indicators.Add("rsi");
        string text = new SaveManager(w, new MemorySaveStorage()).ExportText();
        var w2 = GameWorld.Create();
        Assert.True(new SaveManager(w2, new MemorySaveStorage()).Import(text).Ok);
        Assert.Equal(2, w2.Trading.Indicators.Items.Count);
        var bb = w2.Trading.Indicators.Items[0];
        Assert.Equal(30, bb.Params["period"]);
        Assert.Equal("#ff0000", bb.Colors["upper"]);
        Assert.True(bb.Hidden);
        Assert.Equal("BB (30, 2)", Indicators.Label(bb));
    }
}
