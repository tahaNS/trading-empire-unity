using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class EventTests
{
    [Fact]
    public void Rolling_is_deterministic_and_spread_over_zero_to_one()
    {
        for (int day = 0; day < 200; day++)
        {
            double a = CityEvents.Hash01(1337, day, 1), b = CityEvents.Hash01(1337, day, 1);
            Assert.Equal(a, b);
            Assert.InRange(a, 0.0, 0.9999999999);
        }
        var values = Enumerable.Range(0, 2000).Select(d => CityEvents.Hash01(7, d, 1)).ToList();
        double mean = values.Average();
        Assert.InRange(mean, 0.45, 0.55);
    }

    [Fact]
    public void Needs_a_minimum_city_and_happens_about_as_often_as_configured()
    {
        var ids = new[] { "b1", "b2", "b3", "b4", "b5" };
        Assert.Null(CityEvents.Roll(5, 100, new[] { "b1", "b2" }));
        int hits = Enumerable.Range(0, 10000).Count(d => CityEvents.Roll(99, d, ids) != null);
        Assert.InRange(hits, 300, 500); // about 4%
        foreach (int d in Enumerable.Range(0, 5000))
        {
            var r = CityEvents.Roll(3, d, ids);
            if (r != null && r.Kind == EventKind.Fire) Assert.Contains(r.BuildingId, ids);
        }
    }

    [Fact]
    public void Effects_a_fire_closes_only_its_building_and_insurance_shrinks_the_bills()
    {
        var fire = new[] { new CityEvent { Kind = EventKind.Fire, BuildingId = "b1" } };
        Assert.Equal((-100.0, true), CityEvents.IncomePct(fire, "b1", 0));
        Assert.Equal((0.0, false), CityEvents.IncomePct(fire, "b2", 0));
        var storm = new[] { new CityEvent { Kind = EventKind.Storm } };
        Assert.Equal(-20.0, CityEvents.IncomePct(storm, "b1", 0).pct);
        Assert.Equal(-10.0, CityEvents.IncomePct(storm, "b1", 1).pct); // insurance halves a storm
        var festival = new[] { new CityEvent { Kind = EventKind.Festival } };
        Assert.Equal(20.0, CityEvents.IncomePct(festival, "b1", 1).pct); // good news is never shielded
        Assert.Equal(D(3000).Scale(0.12), CityEvents.RepairCost(D(3000), 0));
        Assert.Equal(D(3000).Scale(0.12).Scale(0.2), CityEvents.RepairCost(D(3000), 1)); // 80% less
    }

    static (GameClock clock, CityGame g, uint seed, string fireId, long day) Fireable()
    {
        var (clock, g) = C.Make(5_000_000);
        for (int i = 0; i < 4; i++) C.Place(g, "house", 7 + i * 2, 8);
        C.FinishConstruction(clock, "house");
        var ids = g.State.Buildings.Select(b => b.Id).ToList();
        long next = clock.Day + 1;
        for (uint seed = 0; seed < 20000; seed++)
        {
            var r = CityEvents.Roll(seed, next, ids);
            if (r != null && r.Kind == EventKind.Fire) return (clock, g, seed, r.BuildingId, next);
        }
        throw new Exception("no seed found");
    }

    [Fact]
    public void A_fire_closes_a_building_costs_a_repair_bill_and_ends_on_time()
    {
        var (clock, g, seed, fireId, _) = Fireable();
        g.EventSeed = seed;
        g.Tick(clock.Time); // starts tracking the current day
        var before = g.State.Balance;
        clock.SkipDays(1);
        Assert.Single(g.State.Events);
        Assert.Equal(EventKind.Fire, g.State.Events[0].Kind);
        Assert.Equal(fireId, g.State.Events[0].BuildingId);
        var b = g.State.Buildings.First(x => x.Id == fireId);
        var bill = CityEvents.RepairCost(b.PurchaseCost, 0);
        Assert.Equal(before - bill, g.State.Balance);
        Assert.Contains(g.State.Transactions, t => t.Description.StartsWith("Fire repairs") && t.Amount == -bill);
        Assert.StartsWith("Fire at", g.State.Notice.Text);
        Assert.True(CityEconomy.ModsFor(b, g.State.Buildings, g.Context()).Closed);
        C.RunDays(clock, 11);
        Assert.Empty(g.State.Events);
        C.ExpectLedgerConsistent(g);
    }

    [Fact]
    public void Insurance_shrinks_the_repair_bill_and_costs_a_premium()
    {
        var (clock, g, seed, fireId, _) = Fireable();
        g.EventSeed = seed;
        g.SetPolicy("insurance", true);
        g.Tick(clock.Time);
        var before = g.State.Balance;
        clock.SkipDays(1);
        var b = g.State.Buildings.First(x => x.Id == fireId);
        Assert.Equal(before - CityEvents.RepairCost(b.PurchaseCost, 1), g.State.Balance);
        Assert.Contains("(insured)", g.State.Notice.Text);
        Assert.Equal(4, CityEconomy.ModsFor(b, g.State.Buildings, g.Context()).ExpensePct);
    }

    [Fact]
    public void A_restored_city_keeps_events_policies_and_the_seed()
    {
        var (clock, g, seed, _, _) = Fireable();
        g.EventSeed = seed;
        g.Tick(clock.Time);
        clock.SkipDays(1);
        g.SetPolicy("marketing", true);
        var (_, g2) = C.Make(0);
        g2.Restore(g.State.Buildings, g.State.Unlocked, g.State.LandTier, g.State.Balance, g.State.Transactions, g.State.Policies, g.State.Events, g.EventSeed);
        Assert.Equal(g.State.Events.Count, g2.State.Events.Count);
        Assert.Equal(new[] { "marketing" }, g2.State.Policies.ToArray());
        Assert.Equal(seed, g2.EventSeed);
        Assert.Equal(g.State.Balance, g2.State.Balance);
        C.ExpectLedgerConsistent(g2);
    }
}

public class TransferTests
{
    sealed class FakeSource : ITransferSource
    {
        public long Cash;
        public bool Refuse;
        public List<string> Log;
        public Money Eligible() { return Money.FromCents(Cash); }
        public Money Balance() { return Money.FromCents(Cash); }
        public bool Debit(Money amount, long time, string memo) { if (Refuse) return false; Cash -= amount.Cents; Log.Add("debit"); return true; }
        public void Publish() { Log.Add("publish-source"); }
    }

    sealed class FakeTarget : ITransferTarget
    {
        public long Cash;
        public List<string> Log;
        public Money Balance() { return Money.FromCents(Cash); }
        public void Credit(Money amount, long time, string memo) { Cash += amount.Cents; Log.Add("credit"); }
        public void Publish() { Log.Add("publish-target"); }
    }

    static (CapitalTransferService svc, FakeSource src, FakeTarget dst, List<string> log) Make(long src = 100_000, long dst = 0)
    {
        var log = new List<string>();
        var s = new FakeSource { Cash = src, Log = log };
        var t = new FakeTarget { Cash = dst, Log = log };
        return (new CapitalTransferService(s, t, () => 123), s, t, log);
    }

    [Fact]
    public void Debits_and_credits_exactly_the_same_amount_and_records_it()
    {
        var (svc, s, t, _) = Make();
        var r = svc.Transfer("r1", Money.FromCents(30_000));
        Assert.True(r.Ok);
        Assert.Equal(70_000, s.Cash);
        Assert.Equal(30_000, t.Cash);
        Assert.Equal(100_000, s.Cash + t.Cash); // money is neither created nor destroyed
        Assert.Equal(TransferDirection.ToCity, r.Record.Direction);
        Assert.Equal(Money.FromCents(70_000), r.Record.TradingBalanceAfter);
        Assert.Single(svc.History);
    }

    [Fact]
    public void Applies_both_sides_before_notifying_anyone()
    {
        var (svc, _, _, log) = Make();
        svc.Transfer("r1", Money.FromCents(100));
        Assert.Equal(new[] { "debit", "credit", "publish-source", "publish-target" }, log.ToArray());
    }

    [Fact]
    public void Rejects_bad_amounts_too_much_duplicates_and_a_refusing_source_without_changing_anything()
    {
        var (svc, s, t, _) = Make(50_000);
        Assert.Equal(TransferFailure.InvalidAmount, svc.Transfer("a", Money.Zero).Reason);
        Assert.Equal(TransferFailure.InvalidAmount, svc.Transfer("a", Money.FromCents(-5)).Reason);
        var big = svc.Transfer("a", Money.FromCents(60_000));
        Assert.Equal(TransferFailure.ExceedsEligible, big.Reason);
        Assert.Contains("Only $500.00 is eligible", big.Message);
        Assert.Equal((50_000L, 0L), (s.Cash, t.Cash));
        Assert.True(svc.Transfer("dup", Money.FromCents(1000)).Ok);
        Assert.Equal(TransferFailure.Duplicate, svc.Transfer("dup", Money.FromCents(1000)).Reason); // a double-click cannot move money twice
        Assert.Equal(1000, t.Cash);
        Assert.True(svc.Transfer("retry", Money.FromCents(40_000)).Ok);
        s.Refuse = true;
        Assert.False(svc.Transfer("refused", Money.FromCents(100)).Ok);
        Assert.Equal(1000 + 40_000, t.Cash);
        Assert.Equal(TransferFailure.ExceedsEligible, Make(0).svc.Transfer("empty", Money.FromCents(100)).Reason);
        Assert.False(Make().svc.TransferToTrading("x", Money.FromCents(100)).Ok); // no reverse route configured
    }

    // ---- with the real accounts ----

    /// <summary>A world whose trading session has a steadily rising market: easy to realize a profit.</summary>
    static GameWorld RisingWorld()
    {
        var w = GameWorld.Create();
        var candles = Enumerable.Range(0, 600).Select(i => new Candle(Utc(2024, 1, 2) + i * 60_000L, 110000 + i * 10, 110000 + i * 10 + 12, 110000 + i * 10 - 2, 110000 + i * 10 + 10)).ToList();
        w.Trading.LoadDataset(candles, "rising", true);
        return w;
    }

    [Fact]
    public void Starts_with_an_empty_treasury_so_nothing_can_be_built_before_funding()
    {
        var w = RisingWorld();
        Assert.Equal(Money.Zero, w.City.TreasuryBalance);
        w.City.StartPlacement("house");
        Assert.False(w.City.TryPlace(new Cell(11, 11)));
        Assert.Empty(w.City.State.Buildings);
    }

    [Fact]
    public void Turns_a_realized_profit_into_a_building_that_earns_income_without_creating_money()
    {
        var w = RisingWorld();
        Assert.True(w.Trading.QuickOrder(Direction.Buy, 100, null, null)); // 1.00 lot
        // margin in use cannot move, and nothing is realized yet
        var eligibleOpen = w.Transfers.EligibleAmount();
        Assert.True(eligibleOpen <= w.Trading.State.Account.Balance);
        for (int i = 0; i < 60; i++) w.Trading.Step();
        Assert.True(w.Trading.State.Account.UnrealizedPnl.Cents > 0);
        Assert.True(w.Transfers.EligibleAmount() <= w.Trading.State.Account.Balance); // unrealized profit is excluded
        w.Trading.CloseAll();
        var profit = w.Trading.State.Account.RealizedPnl;
        Assert.True(profit.Cents > 0);

        var totalBefore = w.Trading.Account.Balance + w.City.TreasuryBalance;
        var amount = Money.FromDollars(3500); // enough for a house
        var r = w.Transfers.Transfer("t1", amount);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(totalBefore, w.Trading.Account.Balance + w.City.TreasuryBalance); // conserved
        Assert.Equal(amount, w.City.TreasuryBalance);
        Assert.Equal(TransactionKind.Transfer, w.City.State.Transactions.Last().Kind);
        Assert.Equal(AccountTransactionKind.Withdrawal, w.Trading.Account.Transactions.Last().Kind);

        // the city can now build and earn
        w.City.StartPlacement("house");
        Assert.True(w.City.TryPlace(new Cell(11, 11)));
        C.RunDays(w.Clock, 40);
        Assert.Contains(w.City.State.Transactions, t => t.Kind == TransactionKind.Income);
        C.ExpectLedgerConsistent(w.City);
        Assert.Equal(w.Trading.Account.ExpectedBalance, w.Trading.Account.Balance);
    }

    [Fact]
    public void Moves_cash_back_to_trading_and_never_takes_the_treasury_below_zero()
    {
        var w = RisingWorld();
        w.City.GrantFunds(D(1000));
        var total = w.Trading.Account.Balance + w.City.TreasuryBalance;
        var r = w.Transfers.TransferToTrading("back1", D(400));
        Assert.True(r.Ok);
        Assert.Equal(TransferDirection.ToTrading, r.Record.Direction);
        Assert.Equal(D(600), w.City.TreasuryBalance);
        Assert.Equal(D(10_400), w.Trading.Account.Balance);
        Assert.Equal(total, w.Trading.Account.Balance + w.City.TreasuryBalance);
        Assert.Equal(TransactionKind.TransferOut, w.City.State.Transactions.Last().Kind);
        Assert.Equal(AccountTransactionKind.Deposit, w.Trading.Account.Transactions.Last().Kind);
        var tooMuch = w.Transfers.TransferToTrading("back2", D(700));
        Assert.False(tooMuch.Ok);
        Assert.Equal(D(600), w.City.TreasuryBalance);
        // an empty city treasury allows nothing
        var w2 = RisingWorld();
        Assert.False(w2.Transfers.TransferToTrading("x", D(1)).Ok);
    }

    [Fact]
    public void Eligible_funds_are_min_of_balance_and_free_margin_and_never_negative()
    {
        var w = RisingWorld();
        Assert.Equal(D(10_000), w.Trading.EligibleForTransfer());
        w.Trading.QuickOrder(Direction.Buy, 100, null, null);
        Assert.True(w.Trading.EligibleForTransfer() < D(10_000)); // margin in use cannot leave
        Assert.True(w.Trading.EligibleForTransfer().Cents >= 0);
        Assert.False(w.Trading.ApplyTransferOut(D(10_000), 0, "too much"));
    }
}

public class LandmarkTests
{
    static TradingRecord Rec(int trades, double net = 0, double? pf = null) { return new TradingRecord { Trades = trades, NetPnl = D(net), ProfitFactor = pf }; }

    [Fact]
    public void Trading_hq_level_follows_the_closed_trade_record()
    {
        var hq = Buildings.Get("trading-hq");
        Assert.Equal(1, Landmarks.TradeLevelOf(hq, Rec(0)));
        Assert.Equal(2, Landmarks.TradeLevelOf(hq, Rec(40, 1)));
        Assert.Equal(1, Landmarks.TradeLevelOf(hq, Rec(40, 0))); // net P/L must be strictly above the bar
        Assert.Equal(2, Landmarks.TradeLevelOf(hq, Rec(100, 6000, 1.1))); // level 3 also needs a profit factor of 1.2
        Assert.Equal(3, Landmarks.TradeLevelOf(hq, Rec(100, 6000, 1.3)));
        Assert.Equal(1, Landmarks.TradeLevelOf(hq, Rec(5))); // never below level 1
    }

    [Fact]
    public void Lists_what_the_next_level_needs_and_is_only_trade_based_for_landmarks()
    {
        var hq = Buildings.Get("trading-hq");
        var next = Landmarks.NextLevelRequirements(hq, Rec(3)).Value;
        Assert.Equal("Trading Floor", next.name);
        Assert.False(next.lines[0].Met);
        Assert.Contains("3 / 40", next.lines[0].Label);
        Assert.Null(Landmarks.NextLevelRequirements(hq, Rec(100, 6000, 1.5))); // nothing at the top
        Assert.Equal(2, Landmarks.EffectiveLevel(2, Buildings.Get("small-shop"), Rec(500, 9999, 2))); // shops keep the level they paid for
        Assert.Null(Landmarks.NextLevelRequirements(Buildings.Get("small-shop"), Rec(5)));
    }

    [Fact]
    public void Allows_only_one_and_is_locked_until_unlocked()
    {
        var (clock, g) = C.Make(1_000_000);
        Assert.Equal(BuildReason.Locked, g.Validate("trading-hq", new Cell(8, 8), 0).Reason);
        g.GrantUnlock("trading-hq");
        g.StartPlacement("trading-hq");
        Assert.True(g.TryPlace(new Cell(7, 7)));
        Assert.Equal(BuildReason.Limit, g.Validate("trading-hq", new Cell(12, 12), 0).Reason); // even while the first is still being built
        g.Demolish(g.State.Buildings[0].Id);
        Assert.True(g.Validate("trading-hq", new Cell(12, 12), 0).Ok);
    }

    [Fact]
    public void Earns_nothing_and_pays_its_upkeep_only_once_finished()
    {
        var (clock, g) = C.Make(1_000_000);
        g.GrantUnlock("trading-hq");
        C.Place(g, "trading-hq", 7, 7);
        C.RunDays(clock, 20);
        Assert.DoesNotContain(g.State.Transactions, t => t.Kind == TransactionKind.Expense); // still being built
        C.FinishConstruction(clock, "trading-hq");
        C.RunDays(clock, 31);
        Assert.DoesNotContain(g.State.Transactions, t => t.Kind == TransactionKind.Income);
        Assert.Contains(g.State.Transactions, t => t.Kind == TransactionKind.Expense);
    }

    [Fact]
    public void The_trading_record_is_derived_from_closed_trades_and_ignores_identical_updates()
    {
        var (_, g) = C.Make();
        int changes = 0;
        g.Changed += () => changes++;
        var rec = Rec(12, 100, 1.5);
        g.SetTradingRecord(rec);
        int after = changes;
        g.SetTradingRecord(new TradingRecord { Trades = 12, NetPnl = D(100), ProfitFactor = 1.5 });
        Assert.Equal(after, changes); // identical: no update
    }
}
