using TradingEmpire.Core;
using Xunit;

public class TutorialTests
{
    static TutorialFacts F(int revealed = 0, int orders = 0, int closed = 0, int journal = 0, int drawings = 0, int indicators = 0, int transfers = 0, bool ready = true)
    {
        return new TutorialFacts { Ready = ready, Revealed = revealed, Orders = orders, Closed = closed, JournalEntries = journal, Drawings = drawings, Indicators = indicators, Transfers = transfers };
    }

    [Fact]
    public void Steps_finish_only_when_the_game_state_moves_and_one_at_a_time()
    {
        var t = new Tutorial();
        t.Start(F(revealed: 100));
        Assert.False(t.Advance(F(revealed: 102)));          // 2 candles are not enough
        Assert.True(t.Advance(F(revealed: 103)));
        Assert.Equal(1, t.Step);
        // a trade placed and closed at once only finishes the current (place) step; "close" needs its own new close
        Assert.True(t.Advance(F(revealed: 103, orders: 1, closed: 1)));
        Assert.Equal(2, t.Step);
        Assert.False(t.Advance(F(revealed: 103, orders: 1, closed: 1)));
        Assert.True(t.Advance(F(revealed: 103, orders: 2, closed: 2)));
        Assert.Equal(3, t.Step);
    }

    [Fact]
    public void Nothing_happens_before_data_is_ready_or_when_not_active()
    {
        var t = new Tutorial();
        Assert.False(t.Advance(F(revealed: 99)));
        t.Start(F());
        Assert.False(t.Advance(F(revealed: 50, ready: false)));
        t.Dismiss();
        Assert.False(t.Advance(F(revealed: 50)));
        Assert.Equal(TutorialStatus.Dismissed, t.Status);
    }

    [Fact]
    public void Only_optional_steps_can_be_skipped_and_finishing_marks_it_done()
    {
        var t = new Tutorial();
        t.Start(F());
        Assert.False(t.Skip(F()));                          // "move" is required
        t.Resume(4, F());
        Assert.True(t.Skip(F())); Assert.True(t.Skip(F())); Assert.True(t.Skip(F()));
        Assert.True(t.Finished);
        Assert.Equal(TutorialStatus.Done, t.Status);
        Assert.Null(t.Current);
    }
}
