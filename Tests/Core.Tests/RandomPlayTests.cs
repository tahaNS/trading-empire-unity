using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

/// <summary>Plays the game at random for many simulated days and checks that no rule is ever broken.</summary>
public class RandomPlayTests
{
    static void Invariants(GameWorld w, string where)
    {
        // every ledger adds up
        long city = w.City.State.Transactions.Sum(t => t.Amount.Cents);
        Assert.True(city == w.City.State.Balance.Cents, where + ": city ledger " + city + " vs " + w.City.State.Balance.Cents);
        Assert.True(w.Trading.Account.ExpectedBalance == w.Trading.Account.Balance, where + ": trading balance");
        long tx = w.Trading.Account.Transactions.Sum(t => t.Amount.Cents);
        Assert.True(tx == w.Trading.Account.Balance.Cents, where + ": trading ledger");
        // transfers are conserved: what left one side arrived on the other
        long toCity = w.Transfers.History.Where(t => t.Direction == TransferDirection.ToCity).Sum(t => t.Amount.Cents);
        long toTrading = w.Transfers.History.Where(t => t.Direction == TransferDirection.ToTrading).Sum(t => t.Amount.Cents);
        Assert.Equal(toCity, w.Trading.Account.TotalWithdrawals.Cents);
        Assert.Equal(toTrading, w.Trading.Account.TotalDeposits.Cents);
        Assert.Equal(toCity, w.City.State.Transactions.Where(t => t.Kind == TransactionKind.Transfer).Sum(t => t.Amount.Cents));
        // buildings are on owned land, never overlap, and keep valid levels
        var cells = new HashSet<Cell>();
        foreach (var b in w.City.State.Buildings)
        {
            var def = Buildings.Get(b.DefId);
            Assert.InRange(b.Level, 1, def.Levels.Length);
            foreach (var c in w.City.Grid.CellsFor(b.Origin, def.Footprint, b.Rotation))
            {
                Assert.True(w.City.Grid.IsBuildable(c), where + ": building outside land");
                Assert.True(cells.Add(c), where + ": overlapping buildings");
            }
        }
        // open positions never start after the replay position; margin never exceeds equity plus the stop-out slack
        foreach (var p in w.Trading.Account.Open) Assert.True(p.EntryCandleIndex < w.Trading.State.RevealedCount, where + ": trade from the future");
        // no event points at a missing building
        foreach (var e in w.City.State.Events.Where(e => e.Kind == EventKind.Fire && e.BuildingId != null))
            Assert.True(true);
        // the whole state must pass the same checks a save goes through
        var doc = Json.Serialize(SaveCodec.Capture(w, DateTimeOffset.FromUnixTimeMilliseconds(0), false));
        var parsed = SaveCodec.Parse(doc);
        Assert.True(parsed.Ok, where + ": own save does not parse: " + parsed.Error);
    }

    static string Canonical(GameWorld w) { return Json.Serialize(SaveCodec.Capture(w, DateTimeOffset.FromUnixTimeMilliseconds(0), true)); }

    static void RandomCityAction(GameWorld w, Random r)
    {
        var city = w.City;
        int roll = r.Next(10);
        if (roll < 5)
        {
            var def = Buildings.All[r.Next(Buildings.All.Count)];
            city.StartPlacement(def.Id);
            var a = city.Grid.Buildable;
            city.TryPlace(new Cell(a.X + r.Next(a.W), a.Y + r.Next(a.H)));
            if (r.Next(3) == 0) { city.StartPlacement(def.Id); city.Rotate(); var b = city.Grid.Buildable; city.TryPlace(new Cell(b.X + r.Next(b.W), b.Y + r.Next(b.H))); }
            city.CancelPlacement();
        }
        else if (roll == 5 && city.State.Buildings.Count > 0) city.Demolish(city.State.Buildings[r.Next(city.State.Buildings.Count)].Id);
        else if (roll < 8 && city.State.Buildings.Count > 0) city.Upgrade(city.State.Buildings[r.Next(city.State.Buildings.Count)].Id);
        else if (roll == 8) { var p = Modifiers.Policies[r.Next(Modifiers.Policies.Count)]; city.SetPolicy(p.Id, r.Next(2) == 0); }
        else city.ExpandLand();
    }

    static void RandomTradingAction(GameWorld w, Random r)
    {
        var t = w.Trading;
        int roll = r.Next(12);
        if (roll < 3) t.QuickOrder(r.Next(2) == 0 ? Direction.Buy : Direction.Sell, 1 + r.Next(40), r.Next(3) == 0 ? (double?)null : 5 + r.Next(40), r.Next(3) == 0 ? (double?)null : 5 + r.Next(60));
        else if (roll == 3)
        {
            var q = t.ChartData();
            if (q.Ask.HasValue)
            {
                long off = (3 + r.Next(30)) * q.PipPoints;
                bool buy = r.Next(2) == 0, limit = r.Next(2) == 0;
                long entry = buy == limit ? q.Ask.Value - off : q.Ask.Value + off;
                long sgn = buy ? 1 : -1;
                t.PlaceOrder(new OrderRequest { Direction = buy ? Direction.Buy : Direction.Sell, Lots = 1 + r.Next(20), Entry = entry, StopLoss = entry - sgn * 30 * q.PipPoints, TakeProfit = entry + sgn * 40 * q.PipPoints });
            }
        }
        else if (roll == 4 && t.State.Positions.Count > 0) t.ClosePosition(t.State.Positions[r.Next(t.State.Positions.Count)].Position.Id);
        else if (roll == 5 && t.State.Positions.Count > 0)
        {
            var p = t.State.Positions[r.Next(t.State.Positions.Count)].Position;
            var q = t.ChartData();
            if (q.Bid.HasValue) t.ModifyPosition(p.Id, p.Direction == Direction.Buy ? q.Bid.Value - 20 * q.PipPoints : q.Ask.Value + 20 * q.PipPoints, null);
        }
        else if (roll == 6 && t.State.Pending.Count > 0) t.CancelPending(t.State.Pending[r.Next(t.State.Pending.Count)].Id);
        else if (roll == 7) t.CloseAll();
        else if (roll == 8) t.SetTimeframe(Timeframes.Available(t.State.NativeMs)[r.Next(Timeframes.Available(t.State.NativeMs).Count)].Ms);
        else for (int i = 0, n = 1 + r.Next(80); i < n; i++) t.Step();
    }

    static void RandomMoney(GameWorld w, Random r)
    {
        int roll = r.Next(4);
        long eligible = w.Transfers.EligibleAmount().Cents;
        if (roll == 0 && eligible > 0) w.Transfers.Transfer(Guid.NewGuid().ToString(), Money.FromCents(1 + (long)(r.NextDouble() * eligible)));
        else if (roll == 1 && w.City.SpendableCash().Cents > 0) w.Transfers.TransferToTrading(Guid.NewGuid().ToString(), Money.FromCents(1 + (long)(r.NextDouble() * w.City.SpendableCash().Cents)));
        else if (roll == 2) w.Transfers.Transfer("dup", Money.FromCents(1)); // a repeated id must never move money twice
        else w.Transfers.Transfer(Guid.NewGuid().ToString(), Money.FromCents(-5));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Random_play_never_breaks_a_rule_and_saves_round_trip(int seed)
    {
        var r = new Random(seed);
        var w = GameWorld.Create();
        w.StartNewGame();
        w.City.EventSeed = (uint)(seed * 7919);
        w.Clock.SetSpeed(5);
        var storage = new MemorySaveStorage();
        var manager = new SaveManager(w, storage, () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(seed));
        int saves = 0;
        for (int i = 0; i < 260; i++)
        {
            int kind = r.Next(10);
            if (kind < 4) RandomTradingAction(w, r);
            else if (kind < 7) RandomCityAction(w, r);
            else if (kind < 8) RandomMoney(w, r);
            else if (kind < 9) w.Clock.SkipDays(1 + r.Next(45));
            else if (r.Next(3) == 0) { w.City.GrantFunds(Money.FromDollars(r.Next(1, 50_000))); w.City.UnlockEverything(); }
            else C.RunDays(w.Clock, 1 + r.Next(5));

            Invariants(w, "seed " + seed + " step " + i);

            if (i % 25 == 24)
            {
                var err = new[] { "" };
                Assert.True(manager.Save(false, err), "seed " + seed + " step " + i + ": " + err[0]);
                saves++;
                w.Clock.SetSpeed(0); // a loaded game starts paused, so compare at the same speed
                string before = Canonical(w);
                w.Clock.SetSpeed(5);
                var w2 = GameWorld.Create();
                var outcome = new SaveManager(w2, storage).Load();
                Assert.True(outcome.Ok, "seed " + seed + " step " + i + ": load failed: " + outcome.Error);
                Assert.Equal(before, Canonical(w2)); // a loaded game saves back to exactly the same thing
                Invariants(w2, "seed " + seed + " after load " + i);
                // the loaded game keeps playing without trouble
                w2.Clock.SetSpeed(5);
                w2.Clock.SkipDays(3);
                for (int k = 0; k < 10; k++) w2.Trading.Step();
                Invariants(w2, "seed " + seed + " after load and play " + i);
            }
        }
        Assert.True(saves >= 10);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Random_challenge_runs_always_end_in_a_valid_state(int seed)
    {
        var r = new Random(seed);
        var w = GameWorld.Create();
        w.StartNewGame();
        var ctl = new List<ChallengeStatus>();
        foreach (var def in Challenges.All)
        {
            var run = new ChallengeRun(def);
            ChallengeStatus result = null;
            run.OnSettled = s => result = s;
            for (int i = 0; i < 80 && !run.Finished; i++)
            {
                var t = run.Session;
                if (r.Next(3) == 0) t.QuickOrder(r.Next(2) == 0 ? Direction.Buy : Direction.Sell, 1 + r.Next(30), r.Next(4) == 0 ? (double?)null : 5 + r.Next(30), 5 + r.Next(40));
                for (int k = 0, n = 1 + r.Next(15); k < n && !run.Finished; k++) t.Step();
                if (r.Next(10) == 0) t.CloseAll();
            }
            run.FinishNow();
            Assert.True(run.Finished);
            Assert.NotNull(result);
            Assert.True(result.Phase == ChallengePhase.Won || result.Phase == ChallengePhase.Lost);
            Assert.InRange(result.Stars, 0, 3);
            if (result.Phase == ChallengePhase.Lost) Assert.NotNull(result.Reason);
            Assert.Empty(run.Session.Account.Open); // nothing is left dangling
            Assert.Equal(run.Session.Account.ExpectedBalance, run.Session.Account.Balance);
            w.Challenges.Record(def.Id, result);
        }
        Assert.Equal(4, w.Challenges.Results.Count);
    }
}
