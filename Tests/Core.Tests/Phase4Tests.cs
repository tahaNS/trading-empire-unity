using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

static class W
{
    /// <summary>A world on a steadily rising market (an uploaded-style dataset, so it can be saved and reloaded).</summary>
    public static GameWorld Rising(bool synthetic = false)
    {
        var w = GameWorld.Create();
        var candles = Enumerable.Range(0, 600).Select(i => new Candle(Utc(2024, 1, 2) + i * 60_000L, 110000 + i * 10, 110000 + i * 10 + 12, 110000 + i * 10 - 2, 110000 + i * 10 + 10)).ToList();
        w.Trading.LoadDataset(candles, "rising.csv", synthetic);
        return w;
    }

    public static void Profit(GameWorld w)
    {
        Assert.True(w.Trading.QuickOrder(Direction.Buy, 100, null, null));
        for (int i = 0; i < 40; i++) w.Trading.Step();
        w.Trading.CloseAll();
    }
}

public class RegimeReferenceTests
{
    [Fact]
    public void Matches_the_web_generator_for_every_challenge()
    {
        var expected = new Dictionary<string, (int n, long[][] c)>
        {
            ["trend"] = (460, new[] { new long[] { 1709510400000, 110000, 110011, 110000, 110011, 536 }, new long[] { 1709511300000, 110011, 110011, 109993, 109994, 875 }, new long[] { 1709644500000, 109864, 109864, 109825, 109825, 858 }, new long[] { 1709645400000, 109825, 109839, 109806, 109806, 303 }, new long[] { 1709762400000, 110565, 110565, 110510, 110530, 345 }, new long[] { 1709923500000, 111725, 111725, 111713, 111715, 871 } }),
            ["range"] = (470, new[] { new long[] { 1709510400000, 110000, 110000, 109976, 109980, 269 }, new long[] { 1709511300000, 109980, 109996, 109980, 109992, 826 }, new long[] { 1709644500000, 109821, 109821, 109796, 109799, 832 }, new long[] { 1709645400000, 109799, 109800, 109773, 109773, 489 }, new long[] { 1709762400000, 109947, 109971, 109941, 109941, 770 }, new long[] { 1709932500000, 109745, 109753, 109740, 109744, 789 } }),
            ["spike"] = (398, new[] { new long[] { 1709510400000, 110000, 110011, 110000, 110011, 378 }, new long[] { 1709511300000, 110011, 110030, 110011, 110022, 803 }, new long[] { 1709644500000, 109915, 109919, 109910, 109917, 576 }, new long[] { 1709645400000, 109917, 109917, 109908, 109911, 770 }, new long[] { 1709762400000, 109932, 109932, 109887, 109898, 727 }, new long[] { 1709867700000, 110716, 110750, 110716, 110746, 672 } }),
            ["discipline"] = (550, new[] { new long[] { 1709510400000, 110000, 110009, 109989, 110009, 788 }, new long[] { 1709511300000, 110009, 110033, 110009, 110033, 743 }, new long[] { 1709644500000, 109856, 109860, 109839, 109839, 597 }, new long[] { 1709645400000, 109839, 109845, 109839, 109841, 737 }, new long[] { 1709762400000, 110538, 110555, 110535, 110538, 726 }, new long[] { 1710004500000, 109889, 109889, 109848, 109851, 264 } }),
        };
        foreach (var ch in Challenges.All)
        {
            var a = Scenarios.GenerateRegimeCandles(ch.Seed, ch.Regimes);
            var (n, c) = expected[ch.Id];
            Assert.Equal(n, a.Count);
            int[] pick = { 0, 1, 149, 150, 280, a.Count - 1 };
            for (int k = 0; k < pick.Length; k++)
            {
                var x = a[pick[k]];
                Assert.Equal(c[k], new[] { x.Time, x.Open, x.High, x.Low, x.Close, (long)x.Volume.Value });
            }
        }
    }
}

public class MissionTests
{
    [Fact]
    public void Config_is_consistent()
    {
        Assert.Equal(Missions.All.Count, Missions.All.Select(m => m.Id).Distinct().Count());
        var known = new HashSet<string>(Buildings.All.Select(b => b.Id).Concat(Land.Tiers.Select(t => t.UnlockId)));
        foreach (var m in Missions.All) foreach (var u in m.Unlocks) Assert.Contains(u, known);
        // every building that needs an unlock can be unlocked by some mission
        foreach (var b in Buildings.All.Where(b => b.UnlockId != null)) Assert.NotNull(Missions.Unlocking(b.UnlockId));
        foreach (var t in Land.Tiers) Assert.NotNull(Missions.Unlocking(t.UnlockId));
        Assert.Equal("Landlord income", Missions.Unlocking("restaurant").Title);
    }

    [Fact]
    public void Missions_complete_from_real_play_and_never_twice()
    {
        var w = W.Rising();
        Assert.Empty(w.Progression.Completed);
        W.Profit(w);
        Assert.True(w.Progression.Completed.ContainsKey("first-trade"));
        Assert.True(w.Progression.Completed.ContainsKey("green-trade"));
        Assert.StartsWith("Mission complete", w.City.State.Notice.Text);
        Assert.False(w.Progression.Completed.ContainsKey("first-transfer"));

        Assert.True(w.Transfers.Transfer("t1", D(3500)).Ok);
        Assert.True(w.Progression.Completed.ContainsKey("first-transfer"));
        w.City.StartPlacement("house");
        Assert.True(w.City.TryPlace(new Cell(11, 11)));
        Assert.False(w.Progression.Completed.ContainsKey("first-build")); // only a finished building counts
        C.RunDays(w.Clock, 3);
        Assert.True(w.Progression.Completed.ContainsKey("first-build"));
        long when = w.Progression.Completed["first-build"];
        C.RunDays(w.Clock, 40);
        Assert.Equal(when, w.Progression.Completed["first-build"]); // latched
        Assert.True(w.Progression.Completed.ContainsKey("first-rent")); // $50 of rent arrived
    }

    [Fact]
    public void Moving_money_back_and_forth_cannot_farm_the_transfer_mission()
    {
        var w = W.Rising();
        Assert.True(w.Transfers.Transfer("a", D(2900)).Ok);
        Assert.True(w.Transfers.TransferToTrading("b", D(2900)).Ok);
        Assert.True(w.Transfers.Transfer("c", D(2900)).Ok); // 5,800 moved in total, but only 2,900 net
        Assert.Equal(D(2900), w.Progression.Stats.Transferred);
        Assert.False(w.Progression.Completed.ContainsKey("first-transfer"));
        Assert.True(w.Transfers.Transfer("d", D(200)).Ok); // now 3,100 net
        Assert.True(w.Progression.Completed.ContainsKey("first-transfer"));
    }

    [Fact]
    public void The_next_goal_is_the_first_unfinished_mission()
    {
        var stats = new ProgressStats { TradesClosed = 3 };
        var g = Progress.NextGoalOf(new Dictionary<string, long>(), stats);
        Assert.Equal("first-trade", g.Mission.Id);
        Assert.Equal(1, g.Value);
        var done = Missions.All.ToDictionary(m => m.Id, m => 0L);
        Assert.Null(Progress.NextGoalOf(done, stats));
        done.Remove("tycoon");
        Assert.Equal("tycoon", Progress.NextGoalOf(done, stats).Mission.Id);
    }

    [Fact]
    public void Broke_means_nothing_open_no_income_and_not_enough_cash_for_the_smallest_trade()
    {
        Assert.False(Progress.IsBroke(D(0), 1, D(0), D(0), D(11))); // an open position can still recover
        Assert.False(Progress.IsBroke(D(0), 0, D(0), D(50), D(11))); // rent is on its way
        Assert.False(Progress.IsBroke(D(5), 0, D(10), D(0), D(11))); // cash can move between the accounts
        Assert.True(Progress.IsBroke(D(5), 0, D(5), D(0), D(11)));
        Assert.True(Progress.IsBroke(D(-50), 0, D(-10), D(-5), D(11)));
    }

    [Fact]
    public void Game_over_freezes_time_and_clears_itself_when_the_player_recovers()
    {
        var w = W.Rising();
        Assert.Null(w.GameOver.State);
        w.Trading.Account.Withdraw(D(9_995), 0, "test drain");
        w.Trading.Publish();
        Assert.NotNull(w.GameOver.State);
        Assert.Equal(0, w.Clock.Speed);
        Assert.False(w.Trading.State.Playing);
        w.Trading.Account.Deposit(D(500), 0);
        w.Trading.Publish();
        Assert.Null(w.GameOver.State);
    }
}

public class JournalTests
{
    static Position Trade(string id, int run, double net, double? risk, long entryTime)
    {
        return new Position
        {
            Id = id, Symbol = "EURUSD", Direction = Direction.Buy, Lots = 10, RunId = run, EntryTime = entryTime, IsOpen = false,
            InitialRisk = risk.HasValue ? D(risk.Value) : (Money?)null, Exit = new PositionExit { Net = D(net), Gross = D(net) },
        };
    }

    [Fact]
    public void Entries_are_cleaned_removed_when_empty_and_keyed_by_run()
    {
        var j = new JournalStore();
        Assert.Equal("1:p3", JournalStore.KeyOf(Trade("p3", 1, 0, null, 0)));
        j.Set("1:p1", note: "  patient entry ", tags: new[] { "Patient", "patient", "  News  ", "" }, rating: 9);
        var e = j.Get("1:p1");
        Assert.Equal(new[] { "Patient", "News" }, e.Tags.ToArray()); // trimmed, case-insensitive duplicates dropped
        Assert.Equal(0, e.Rating); // out of range = none
        j.Set("1:p1", rating: 4);
        Assert.Equal(4, j.Get("1:p1").Rating);
        j.ToggleTag("1:p1", "FOMO");
        Assert.Contains("FOMO", j.Get("1:p1").Tags);
        j.ToggleTag("1:p1", "fomo");
        Assert.DoesNotContain("FOMO", j.Get("1:p1").Tags);
        j.Set("1:p1", note: "", tags: new string[0], rating: 0);
        Assert.Empty(j.Entries); // an entry with nothing in it is removed
        j.Set("k", note: new string('x', 5000));
        Assert.Equal(JournalStore.MaxNote, j.Get("k").Note.Length);
        j.Set("t", tags: Enumerable.Range(0, 30).Select(i => "tag" + i));
        Assert.Equal(JournalStore.MaxTags, j.Get("t").Tags.Count);
    }

    [Fact]
    public void Statistics_group_by_tag_session_and_hour()
    {
        var closed = new[]
        {
            Trade("a", 1, 100, 50, Utc(2024, 1, 2, 9)), // London, 2R
            Trade("b", 1, -50, 50, Utc(2024, 1, 2, 9)), // London, -1R
            Trade("c", 1, 30, null, Utc(2024, 1, 2, 3)), // Asia, no stop-loss
        };
        var j = new JournalStore();
        j.Set("1:a", tags: new[] { "Followed plan" });
        j.Set("1:b", tags: new[] { "FOMO", "Followed plan" });
        var s = JournalStore.StatsOf(closed, j.Entries);
        Assert.Equal("Followed plan", s.ByTag[0].Label);
        Assert.Equal(2, s.ByTag[0].Trades);
        Assert.Equal(D(50), s.ByTag[0].Net);
        Assert.Equal(0.5, s.ByTag[0].AvgR.Value, 6);
        Assert.Equal(1, s.Untagged.Trades);
        var london = s.BySession.First(x => x.Label == "London");
        Assert.Equal((2, 1), (london.Trades, london.Wins));
        Assert.Equal(2, s.ByHour[9].Trades);
        Assert.Equal(24, s.ByHour.Count);
        Assert.Null(s.Untagged.AvgR);
        Assert.Equal("Off hours", JournalStore.SessionOfHour(22));
    }
}

public class ChallengeTests
{
    static RunSnapshot Snap(double balance, double equity, int open = 0, params Position[] closed)
    {
        return new RunSnapshot { Balance = D(balance), Equity = D(equity), Open = Enumerable.Range(0, open).Select(_ => new Position { InitialRisk = D(10) }).ToList(), Closed = closed.ToList(), CandlesLeft = 10 };
    }

    [Fact]
    public void Loses_at_once_on_the_drawdown_limit_and_stays_lost()
    {
        var def = Challenges.Get("trend"); // 3% drawdown
        var t = new ChallengeTracker(def, D(10_000));
        Assert.Equal(ChallengePhase.Running, t.Update(Snap(10_000, 10_100)).Phase);
        var st = t.Update(Snap(10_000, 9_700)); // 3.96% below the 10,100 peak
        Assert.Equal(ChallengePhase.Lost, st.Phase);
        Assert.Contains("Drawdown limit", st.Reason);
        Assert.Equal(ChallengePhase.Lost, t.Finish(Snap(11_000, 11_000)).Phase); // a later recovery cannot undo it
    }

    [Fact]
    public void Star_rules()
    {
        var def = Challenges.Get("trend");
        Assert.Equal(2, new ChallengeTracker(def, D(10_000)).Finish(Snap(10_160, 10_160)).Stars); // shallow drawdown, small gain
        Assert.Equal(3, new ChallengeTracker(def, D(10_000)).Finish(Snap(10_230, 10_230)).Stars); // and a gain of 1.5x the goal
        var shaky = new ChallengeTracker(def, D(10_000));
        shaky.Update(Snap(10_000, 10_000)); shaky.Update(Snap(10_000, 9_800)); // 2% drawdown > half the 3% limit
        Assert.Equal(1, shaky.Finish(Snap(10_160, 10_160)).Stars);
        Assert.Equal(ChallengePhase.Lost, new ChallengeTracker(def, D(10_000)).Finish(Snap(10_050, 10_050)).Phase); // short of the goal
    }

    [Fact]
    public void Enforces_the_rules_of_each_challenge()
    {
        var spike = new ChallengeTracker(Challenges.Get("spike"), D(10_000));
        var noStop = new Position { InitialRisk = null };
        Assert.Contains("without a stop-loss", spike.Update(Snap(10_000, 10_000, 0, noStop)).Reason);
        var range = new ChallengeTracker(Challenges.Get("range"), D(10_000)); // at most 10 trades
        Assert.Equal(ChallengePhase.Running, range.Update(Snap(10_000, 10_000, 10)).Phase);
        Assert.Contains("Too many trades", range.Update(Snap(10_000, 10_000, 11)).Reason);
        var disc = new ChallengeTracker(Challenges.Get("discipline"), D(10_000)); // risk at most 1.5% = $150
        Assert.Contains("risked more than 1.5%", disc.Update(Snap(10_000, 10_000, 0, new Position { InitialRisk = D(200) })).Reason);
    }

    [Fact]
    public void A_run_has_its_own_account_and_can_be_finished_early()
    {
        var run = new ChallengeRun(Challenges.Get("trend"));
        Assert.Equal(D(Challenges.StartBalanceDollars), run.Session.State.Account.Balance);
        Assert.True(run.Session.State.Dataset.Synthetic);
        ChallengeStatus result = null;
        run.OnSettled = r => result = r;
        run.FinishNow(); // no trades: short of the +1.5% goal
        Assert.True(run.Finished);
        Assert.Equal(ChallengePhase.Lost, result.Phase);
        var spikeRun = new ChallengeRun(Challenges.Get("spike"));
        ChallengeStatus r2 = null;
        spikeRun.OnSettled = r => r2 = r;
        spikeRun.Session.QuickOrder(Direction.Buy, 10, null, null); // no stop-loss: lost at once
        Assert.Equal(ChallengePhase.Lost, r2.Phase);
    }

    [Fact]
    public void Results_keep_the_best_and_report_the_first_win_only_once()
    {
        var store = new ChallengeStore();
        Assert.False(store.Record("trend", new ChallengeStatus { Phase = ChallengePhase.Lost }));
        Assert.True(store.Record("trend", new ChallengeStatus { Phase = ChallengePhase.Won, Stars = 2, ReturnPct = 2.0 }));
        Assert.False(store.Record("trend", new ChallengeStatus { Phase = ChallengePhase.Won, Stars = 1, ReturnPct = 1.0 }));
        var r = store.Results["trend"];
        Assert.Equal((2, 2.0, 2, 3), (r.Stars, r.BestReturnPct, r.Wins, r.Attempts));
        store.Restore(new Dictionary<string, ChallengeResult> { ["trend"] = new ChallengeResult { Stars = 9, Wins = -1 }, ["nope"] = new ChallengeResult() });
        Assert.Single(store.Results);
        Assert.Equal(3, store.Results["trend"].Stars); // clamped
        Assert.Equal(0, store.Results["trend"].Wins);
    }
}

public class AchievementTests
{
    [Fact]
    public void Are_earned_once_announced_and_kept_when_the_numbers_fall()
    {
        var t = new AchievementTracker();
        var seen = new List<string>();
        t.OnEarned = a => seen.Add(a.Name);
        t.Evaluate(new AchievementFacts { Trades = 12, Buildings = 1 });
        Assert.Equal(new[] { "First steps", "Getting going", "Ground broken" }, seen.ToArray());
        t.Evaluate(new AchievementFacts { Trades = 12, Buildings = 1 });
        Assert.Equal(3, seen.Count); // not twice
        t.Evaluate(new AchievementFacts { Trades = 0, Buildings = 0 });
        Assert.Equal(3, t.Earned.Count); // kept
        t.WithPaused(() => t.Evaluate(new AchievementFacts { Trades = 100 }));
        Assert.Equal(3, t.Earned.Count); // a half-restored game cannot earn
        t.Restore(new[] { "boom", "boom", "unknown-id" });
        Assert.Equal(new[] { "boom" }, t.Earned.ToArray());
    }

    [Fact]
    public void Real_play_earns_them_through_the_world()
    {
        var w = W.Rising();
        W.Profit(w);
        Assert.Contains("first-trade", w.Achievements.Earned);
        w.City.GrantFunds(D(100));
        w.City.SetPolicy("marketing", true);
        Assert.Contains("policy", w.Achievements.Earned);
    }
}
