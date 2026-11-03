using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class DraftTests
{
    static TradingSession Ready()
    {
        var w = W.Rising();
        w.Trading.Step();
        return w.Trading;
    }

    [Fact]
    public void A_new_draft_is_a_market_order_with_a_20_pip_stop_and_a_2R_target()
    {
        var s = Ready();
        s.StartDraft(Direction.Buy);
        var d = s.Draft;
        var v = s.DescribeDraft();
        Assert.Null(d.Entry);
        Assert.Equal(EntryClass.Market, v.Resolution.Kind);
        Assert.Equal(20 * s.Instrument.PipPoints, v.Resolution.EntryPrice - d.StopLoss);
        Assert.Equal(40 * s.Instrument.PipPoints, d.TakeProfit.Value - v.Resolution.EntryPrice);
        Assert.Equal(2.0, v.Preview.RewardRisk.Value, 1);
        Assert.True(v.Valid);
    }

    [Fact]
    public void Risk_sizing_never_risks_more_than_the_chosen_percentage()
    {
        var s = Ready();
        s.StartDraft(Direction.Sell);
        s.UpdateDraft(d => d.RiskPct = 2);
        var v = s.DescribeDraft();
        Assert.NotNull(v.Resolution.Lots);
        Assert.True(v.Preview.Risk.Value <= s.State.Account.Balance.Scale(0.02));
        // a fixed size ignores the percentage
        s.UpdateDraft(d => { d.RiskSizing = false; d.Lots = 50; });
        Assert.Equal(50, s.DescribeDraft().Resolution.Lots);
    }

    [Fact]
    public void An_entry_away_from_the_market_is_pending_and_one_next_to_it_snaps_to_market()
    {
        var s = Ready();
        s.StartDraft(Direction.Buy);
        long ask = s.DescribeDraft().Resolution.EntryPrice;
        long pip = s.Instrument.PipPoints;
        s.UpdateDraft(d => { d.Entry = ask - 10 * pip; d.StopLoss = ask - 30 * pip; d.TakeProfit = ask + 20 * pip; });
        Assert.Equal(EntryClass.Limit, s.DescribeDraft().Resolution.Kind);
        Assert.True(s.SubmitDraft());
        Assert.Null(s.Draft);
        Assert.Single(s.State.Pending);
        s.StartDraft(Direction.Buy);
        s.UpdateDraft(d => d.Entry = ask + 1);   // a tenth of a pip away
        Assert.Null(s.Draft.Entry);
    }

    [Fact]
    public void A_wrong_sided_stop_is_explained_and_keeps_the_draft_open()
    {
        var s = Ready();
        s.StartDraft(Direction.Buy);
        long entry = s.DescribeDraft().Resolution.EntryPrice;
        s.UpdateDraft(d => d.StopLoss = entry + 10 * s.Instrument.PipPoints);
        var v = s.DescribeDraft();
        Assert.False(v.Valid);
        Assert.False(s.SubmitDraft());
        Assert.NotNull(s.Draft);
        s.CancelDraft();
        Assert.Null(s.Draft);
    }

    [Fact]
    public void Loading_data_or_restarting_discards_the_draft()
    {
        var s = Ready();
        s.StartDraft(Direction.Buy);
        s.Restart();
        Assert.Null(s.Draft);
    }
}
