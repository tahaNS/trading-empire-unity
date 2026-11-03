using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

static class C
{
    public static (GameClock clock, CityGame game) Make(double funds = 100_000)
    {
        var clock = new GameClock();
        var game = new CityGame(new CityGrid(24, 24, 1f, new RectI(6, 6, 12, 12)), clock, D(funds));
        clock.Tick += game.Tick;
        return (clock, game);
    }

    /// <summary>Advance by (at least) this many sim days at the clock's current speed, in real-time frames.</summary>
    public static void RunDays(GameClock clock, double days)
    {
        double perFrame = GameClock.MaxRealDtSeconds * GameClock.SimDaysPerRealSecond * clock.Speed;
        int frames = perFrame > 0 ? (int)Math.Ceiling(days / perFrame) : (int)Math.Ceiling(days * 10);
        for (int i = 0; i < frames; i++) clock.Advance(GameClock.MaxRealDtSeconds);
    }

    public static void FinishConstruction(GameClock clock, params string[] defIds)
    {
        RunDays(clock, defIds.Max(id => Buildings.Get(id).ConstructionDays) + 0.5);
    }

    public static Money Net(string defId, int level)
    {
        var l = Buildings.Get(defId).Levels[level - 1];
        return l.Income - l.Expense;
    }

    public static void Place(CityGame g, string defId, int x, int y)
    {
        g.StartPlacement(defId);
        Assert.True(g.TryPlace(new Cell(x, y)), "could not place " + defId + " at " + x + "," + y);
    }

    public static void ExpectLedgerConsistent(CityGame g)
    {
        long sum = g.State.Transactions.Sum(r => r.Amount.Cents);
        Assert.Equal(sum, g.State.Balance.Cents);
        if (g.State.Transactions.Count > 0) Assert.Equal(g.State.Balance, g.State.Transactions.Last().BalanceAfter);
    }
}

public class GridTests
{
    static CityGrid Make() { return new CityGrid(24, 24, 1f, new RectI(6, 6, 12, 12)); }

    [Fact]
    public void Converts_between_world_and_cell_coordinates()
    {
        var g = Make();
        Assert.Equal((0.5f, 0.5f), g.CellToWorld(new Cell(12, 12)));
        Assert.Equal(new Cell(12, 12), g.WorldToCell(0.5f, 0.5f));
        Assert.Equal(new Cell(0, 0), g.WorldToCell(-11.9f, -11.9f));
        var w = g.CellToWorld(new Cell(7, 9));
        Assert.Equal(new Cell(7, 9), g.WorldToCell(w.x, w.z));
    }

    [Fact]
    public void Swaps_footprint_dimensions_on_odd_rotations()
    {
        Assert.Equal((3, 2), (CityGrid.Rotated(new Footprint(2, 3), 1).W, CityGrid.Rotated(new Footprint(2, 3), 1).H));
        Assert.Equal((2, 3), (CityGrid.Rotated(new Footprint(2, 3), 2).W, CityGrid.Rotated(new Footprint(2, 3), 2).H));
        Assert.Equal(0, CityGrid.NextRotation(3));
    }

    [Fact]
    public void Accepts_inside_and_rejects_outside_the_buildable_land()
    {
        var g = Make();
        Assert.Equal(PlacementReason.Ok, g.Check(new Cell(6, 6), new Footprint(2, 2), 0));
        Assert.Equal(PlacementReason.OutsideLand, g.Check(new Cell(0, 0), new Footprint(1, 1), 0));
        Assert.Equal(PlacementReason.OutsideLand, g.Check(new Cell(5, 6), new Footprint(2, 2), 0));
        Assert.Equal(PlacementReason.OutsideLand, g.Check(new Cell(17, 17), new Footprint(2, 2), 0));
        Assert.Equal(PlacementReason.OutsideLand, g.Check(new Cell(-1, 8), new Footprint(1, 1), 0));
    }

    [Fact]
    public void Rejects_overlap_and_frees_cells_on_release()
    {
        var g = Make();
        g.Occupy("a", new Cell(8, 8), new Footprint(2, 2), 0);
        Assert.Equal("a", g.OccupantAt(new Cell(9, 9)));
        Assert.Equal(PlacementReason.Occupied, g.Check(new Cell(9, 9), new Footprint(2, 2), 0));
        Assert.Equal(PlacementReason.Ok, g.Check(new Cell(10, 8), new Footprint(2, 2), 0));
        g.Release("a");
        Assert.Equal(PlacementReason.Ok, g.Check(new Cell(9, 9), new Footprint(2, 2), 0));
    }

    [Fact]
    public void Uses_the_rotated_footprint_and_throws_on_invalid_or_duplicate_occupy()
    {
        var g = Make();
        Assert.Equal(PlacementReason.Ok, g.Check(new Cell(16, 6), new Footprint(2, 3), 0));
        Assert.Equal(PlacementReason.OutsideLand, g.Check(new Cell(16, 6), new Footprint(2, 3), 1));
        g.Occupy("a", new Cell(8, 8), new Footprint(1, 1), 0);
        Assert.Throws<InvalidOperationException>(() => g.Occupy("b", new Cell(8, 8), new Footprint(1, 1), 0));
        Assert.Throws<InvalidOperationException>(() => g.Occupy("a", new Cell(9, 9), new Footprint(1, 1), 0));
    }

    [Fact]
    public void Centres_a_footprint_under_its_cells()
    {
        var g = Make();
        Assert.Equal((0f, 0f), g.FootprintCenter(new Cell(11, 11), new Footprint(2, 2), 0));
    }
}

public class RoadTests
{
    [Fact]
    public void Roads_sit_on_fixed_world_lines_and_classify_intersections()
    {
        Assert.Equal(Roads.Kind.NorthSouth, Roads.KindOf(new Cell(16, 10)));
        Assert.Equal(Roads.Kind.EastWest, Roads.KindOf(new Cell(10, 16)));
        Assert.Equal(Roads.Kind.Cross, Roads.KindOf(new Cell(16, 16)));
        Assert.Equal(Roads.Kind.None, Roads.KindOf(new Cell(12, 12)));
        Assert.True(Roads.IsRoadCell(new Cell(9, 0)));
    }

    [Fact]
    public void Streets_extend_when_land_grows_and_never_move()
    {
        var before = Roads.RoadCellsIn(Land.StartingArea).Select(c => c.cell).ToHashSet();
        foreach (var tier in Land.Tiers)
        {
            var now = Roads.RoadCellsIn(tier.Area).Select(c => c.cell).ToHashSet();
            foreach (var c in before) Assert.Contains(c, now);
        }
    }

    [Fact]
    public void The_starting_plot_has_one_intersection()
    {
        var cells = Roads.RoadCellsIn(Land.StartingArea);
        Assert.Single(cells.Where(c => c.kind == Roads.Kind.Cross));
        Assert.Equal(12 + 12 - 1, cells.Count);
    }

    [Fact]
    public void Refuses_to_build_on_a_road_without_charging_but_loads_old_saves_on_roads()
    {
        var w = GameWorld.Create();
        w.City.GrantFunds(D(100_000));
        var before = w.City.TreasuryBalance;
        w.City.StartPlacement("small-shop");
        Assert.Equal(BuildReason.Road, w.City.Validate("small-shop", new Cell(16, 12), 0).Reason);
        Assert.False(w.City.TryPlace(new Cell(16, 12)));
        Assert.Equal(before, w.City.TreasuryBalance);
        Assert.Equal(BuildReason.Road, w.City.Validate("apartment", new Cell(15, 12), 0).Reason);
        Assert.True(w.City.Validate("small-shop", new Cell(15, 12), 0).Ok);

        var g = new CityGrid(32, 32, 1f, Land.StartingArea) { RoadRule = Roads.IsRoadCell };
        Assert.Equal(PlacementReason.Road, g.Check(new Cell(16, 12), new Footprint(1, 1), 0));
        g.Occupy("old", new Cell(16, 12), new Footprint(1, 1), 0); // must not throw
    }

    [Fact]
    public void Measures_the_empty_cells_between_two_footprints()
    {
        var a = new RectI(0, 0, 2, 2);
        Assert.Equal(0, CityEconomy.RectGap(a, new RectI(2, 0, 1, 1)));
        Assert.Equal(3, CityEconomy.RectGap(a, new RectI(5, 0, 1, 1)));
        Assert.Equal(3, CityEconomy.RectGap(a, new RectI(4, 5, 1, 1)));
    }
}

public class CityPlacementTests
{
    [Fact]
    public void Building_definitions_are_valid_and_unique()
    {
        Buildings.Validate(Buildings.All);
        Assert.Equal(new[] { "small-shop", "apartment", "small-office", "restaurant", "luxury-tower", "corporate-office", "house", "park", "plaza", "hotel", "trading-hq" },
            Buildings.All.Select(d => d.Id).ToArray());
        Assert.Throws<ArgumentException>(() => Buildings.Get("nope"));
        var shop = Buildings.Get("small-shop");
        Assert.Throws<InvalidOperationException>(() => Buildings.Validate(new[] { shop, shop }));
        Assert.Throws<InvalidOperationException>(() => Buildings.Validate(new[] { Clone(shop, d => d.Footprint = new Footprint(0, 1)) }));
        Assert.Throws<InvalidOperationException>(() => Buildings.Validate(new[] { Clone(shop, d => d.Levels = new BuildingLevel[0]) }));
    }

    static BuildingDefinition Clone(BuildingDefinition d, Action<BuildingDefinition> change)
    {
        var c = new BuildingDefinition
        {
            Id = d.Id, Name = d.Name, Category = d.Category, District = d.District, Description = d.Description, Cost = d.Cost, ConstructionDays = d.ConstructionDays,
            Footprint = d.Footprint, IncomeIntervalDays = d.IncomeIntervalDays, Levels = d.Levels,
        };
        change(c);
        return c;
    }

    [Fact]
    public void Places_a_valid_building_and_gives_feedback()
    {
        var (_, g) = C.Make(1_000_000);
        g.StartPlacement("apartment");
        g.SetHover(new Cell(8, 8));
        Assert.True(g.State.Hover.Check.Ok);
        Assert.True(g.TryPlace(new Cell(8, 8)));
        Assert.Single(g.State.Buildings);
        Assert.Null(g.State.SelectedId); // placing does not pop up the inspection panel
        Assert.Equal(NoticeKind.Success, g.State.Notice.Kind);
        Assert.Equal(g.State.Buildings[0].Id, g.Grid.OccupantAt(new Cell(9, 9)));
    }

    [Fact]
    public void Refuses_invalid_placement_and_reports_why()
    {
        var (_, g) = C.Make(1_000_000);
        g.StartPlacement("small-shop");
        Assert.False(g.TryPlace(new Cell(0, 0)));
        Assert.Empty(g.State.Buildings);
        Assert.Equal(NoticeKind.Error, g.State.Notice.Kind);
        g.TryPlace(new Cell(8, 8));
        Assert.False(g.TryPlace(new Cell(8, 8)));
        Assert.Single(g.State.Buildings);
    }

    [Fact]
    public void Rotation_changes_the_validated_footprint()
    {
        var (_, g) = C.Make(1_000_000);
        g.StartPlacement("small-office"); // 2x3
        g.SetHover(new Cell(16, 6));
        Assert.True(g.State.Hover.Check.Ok);
        g.Rotate(); // 3x2 sticks out east
        Assert.Equal(1, g.State.Placement.Rotation);
        Assert.False(g.State.Hover.Check.Ok);
    }

    [Fact]
    public void Selects_by_cell_and_escape_cancels_placement_before_selection()
    {
        var (_, g) = C.Make(1_000_000);
        g.StartPlacement("small-shop");
        g.TryPlace(new Cell(10, 10));
        g.CancelPlacement();
        g.Select(null);
        g.SelectAt(new Cell(10, 10));
        Assert.NotNull(g.State.SelectedId);
        g.Escape();
        Assert.Null(g.State.SelectedId);
        g.SelectAt(new Cell(7, 7));
        Assert.Null(g.State.SelectedId);
        g.StartPlacement("small-shop");
        g.Escape();
        Assert.Null(g.State.Placement);
    }

    [Fact]
    public void Locked_buildings_cannot_be_placed_until_unlocked()
    {
        var (_, g) = C.Make(1_000_000);
        Assert.Equal(BuildReason.Locked, g.Validate("restaurant", new Cell(8, 8), 0).Reason);
        g.StartPlacement("restaurant");
        Assert.Null(g.State.Placement);
        Assert.Equal(NoticeKind.Error, g.State.Notice.Kind);
        Assert.True(g.GrantUnlock("restaurant"));
        Assert.False(g.GrantUnlock("restaurant"));
        Assert.True(g.Validate("restaurant", new Cell(8, 8), 0).Ok);
    }
}

public class TreasuryTests
{
    [Fact]
    public void Rejects_overspending_without_changing_anything()
    {
        var t = new CityTreasury(new IdSequence("t"));
        t.Credit(TransactionKind.InitialFunding, D(100), 0, "seed");
        var r = t.Debit(TransactionKind.Construction, D(150), 1, "too much");
        Assert.False(r.Ok);
        Assert.Equal(D(50), r.Shortfall);
        Assert.Equal(D(100), t.Balance);
        Assert.Single(t.History);
    }

    [Fact]
    public void Only_goes_negative_when_explicitly_allowed_and_rejects_non_positive_amounts()
    {
        var t = new CityTreasury(new IdSequence("t"));
        t.Debit(TransactionKind.Expense, D(10), 0, "bills", null, true);
        Assert.Equal(D(-10), t.Balance);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Credit(TransactionKind.Income, Money.Zero, 0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Debit(TransactionKind.Expense, D(-1), 0, "x"));
    }
}

public class ConstructionAndIncomeTests
{
    [Fact]
    public void Construction_is_refused_with_insufficient_funds_and_deducts_exactly_otherwise()
    {
        var (_, poor) = C.Make(1_000);
        poor.StartPlacement("apartment");
        Assert.False(poor.TryPlace(new Cell(8, 8)));
        Assert.Equal(D(1_000), poor.State.Balance);
        Assert.Empty(poor.State.Buildings);

        var (_, g) = C.Make();
        g.StartPlacement("small-shop");
        Assert.False(g.TryPlace(new Cell(0, 0))); // invalid location: no charge
        Assert.Equal(D(100_000), g.State.Balance);
        g.TryPlace(new Cell(8, 8));
        Assert.Equal(D(100_000) - Buildings.Get("small-shop").Cost, g.State.Balance);
        Assert.Equal(TransactionKind.Construction, g.State.Transactions.Last().Kind);
        C.ExpectLedgerConsistent(g);
    }

    [Fact]
    public void Pays_income_and_expenses_each_interval_once()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        C.FinishConstruction(clock, "small-shop");
        var after = g.State.Balance;
        C.RunDays(clock, 29);
        Assert.Equal(after, g.State.Balance); // nothing before the first interval
        C.RunDays(clock, 2);
        Assert.Equal(after + C.Net("small-shop", 1), g.State.Balance);
        var b = g.State.Buildings[0];
        Assert.Equal(Buildings.Get("small-shop").Levels[0].Income, b.LifetimeIncome);
        Assert.Equal(Buildings.Get("small-shop").Levels[0].Expense, b.LifetimeExpense);
        C.RunDays(clock, 30);
        Assert.Equal(after + C.Net("small-shop", 1) + C.Net("small-shop", 1), g.State.Balance);
        C.ExpectLedgerConsistent(g);
    }

    [Fact]
    public void Records_income_and_expense_separately_at_the_scheduled_time()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        C.FinishConstruction(clock, "small-shop");
        C.RunDays(clock, 31);
        var s = Transactions.Summarize(g.State.Transactions, GameClock.SimStart - 1, clock.Time);
        var level = Buildings.Get("small-shop").Levels[0];
        Assert.Equal(level.Income, s.Income);
        Assert.Equal(level.Expense, s.Expenses);
        Assert.Equal(C.Net("small-shop", 1), s.NetOperating);
        Assert.Equal(Buildings.Get("small-shop").Cost, s.CapitalSpending);
        var income = g.State.Transactions.First(t => t.Kind == TransactionKind.Income);
        Assert.Equal(GameClock.SimStart + (long)((Buildings.Get("small-shop").ConstructionDays + 30) * GameClock.DayMs), income.Time);
    }

    [Fact]
    public void A_paused_clock_produces_nothing_and_a_settlement_never_pays_twice()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        clock.SetSpeed(0);
        C.RunDays(clock, 100);
        Assert.DoesNotContain(g.State.Transactions, t => t.Kind == TransactionKind.Income);

        var (_, g2) = C.Make(1_000_000);
        C.Place(g2, "small-shop", 8, 8);
        var before = g2.TreasuryBalance;
        long due = g2.State.Buildings[0].NextPayoutAt;
        g2.Tick(due);
        g2.Tick(due); // the same moment again must not pay twice
        Assert.Equal(C.Net("small-shop", 1), g2.TreasuryBalance - before);
        Assert.Contains("Rent collected $135.00, running costs $46.00", g2.State.Notice.Text);
    }

    [Fact]
    public void Income_is_credited_before_the_expense()
    {
        var (clock, g) = C.Make(4000);
        C.Place(g, "small-shop", 8, 8);
        C.FinishConstruction(clock, "small-shop");
        Assert.Equal(Money.Zero, g.State.Balance);
        C.RunDays(clock, 31);
        Assert.Equal(C.Net("small-shop", 1), g.State.Balance);
    }

    [Fact]
    public void Projects_net_income_from_the_actual_state()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        C.Place(g, "apartment", 14, 14); // far from the shop, so no synergy changes the figures
        Assert.Equal(Money.Zero, CityEconomy.ProjectedNetPer30Days(g.State.Buildings)); // still being built
        Assert.Equal(C.Net("small-shop", 1) + C.Net("apartment", 1), CityEconomy.ProjectedNetPer30Days(g.State.Buildings, true));
        C.FinishConstruction(clock, "small-shop", "apartment");
        Assert.Equal(C.Net("small-shop", 1) + C.Net("apartment", 1), CityEconomy.ProjectedNetPer30Days(g.State.Buildings));
    }

    [Fact]
    public void A_building_earns_nothing_until_finished_then_completes_once_with_a_notice()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        var b = g.State.Buildings[0];
        Assert.Equal(BuildStatus.Building, b.Status);
        Assert.Equal(b.BuiltAt + (long)(Buildings.Get("small-shop").ConstructionDays * GameClock.DayMs), b.CompletesAt);
        C.RunDays(clock, 1);
        Assert.Equal(BuildStatus.Building, g.State.Buildings[0].Status);
        Assert.DoesNotContain(g.State.Transactions, t => t.Kind == TransactionKind.Income);
        C.FinishConstruction(clock, "small-shop");
        Assert.Equal(BuildStatus.Complete, g.State.Buildings[0].Status);
        Assert.Contains("Small Shop finished", string.Join("|", g.State.Notice.Text));
    }

    [Fact]
    public void Upgrade_is_refused_while_building_unaffordable_and_at_max_level_and_otherwise_charges()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        string id = g.State.Buildings[0].Id;
        Assert.False(g.Upgrade(id)); // under construction
        C.FinishConstruction(clock, "small-shop");
        var before = g.State.Balance;
        Assert.True(g.Upgrade(id));
        Assert.Equal(before - Buildings.Get("small-shop").Levels[0].UpgradeCost.Value, g.State.Balance);
        Assert.Equal(2, g.State.Buildings[0].Level);
        Assert.True(g.Upgrade(id));
        Assert.False(g.Upgrade(id)); // max level
        C.ExpectLedgerConsistent(g);

        var (c2, poor) = C.Make(4000);
        C.Place(poor, "small-shop", 8, 8);
        C.FinishConstruction(c2, "small-shop");
        Assert.False(poor.Upgrade(poor.State.Buildings[0].Id)); // unaffordable
        Assert.Equal(1, poor.State.Buildings[0].Level);
    }

    [Fact]
    public void Demolition_refunds_part_of_the_cost_through_the_ledger_and_stops_income()
    {
        var (clock, g) = C.Make();
        C.Place(g, "small-shop", 8, 8);
        string id = g.State.Buildings[0].Id;
        var cost = Buildings.Get("small-shop").Cost;
        var before = g.State.Balance;
        var refund = g.DemolishRefund(id);
        Assert.Equal(cost.Scale(Balance.DemolishRefundPct / 100.0), refund);
        Assert.True(refund < cost); // never a profit
        Assert.True(g.Demolish(id));
        Assert.Empty(g.State.Buildings);
        Assert.Null(g.Grid.OccupantAt(new Cell(8, 8)));
        Assert.Equal(before + refund, g.State.Balance);
        Assert.Equal(TransactionKind.Demolition, g.State.Transactions.Last().Kind);
        C.ExpectLedgerConsistent(g);
        Assert.False(g.Demolish(id)); // cannot be repeated
        C.RunDays(clock, 90);
        Assert.DoesNotContain(g.State.Transactions, t => t.Kind == TransactionKind.Income);
        Assert.Equal(cost - refund, Transactions.Summarize(g.State.Transactions, GameClock.SimStart - 1, clock.Time).CapitalSpending);
        g.StartPlacement("small-shop");
        Assert.True(g.TryPlace(new Cell(8, 8))); // the freed space can be built on again
    }

    [Fact]
    public void Land_expansion_needs_its_unlock_and_the_money()
    {
        var w = GameWorld.Create();
        w.City.GrantFunds(D(100_000));
        Assert.False(w.City.ExpandLand()); // locked
        w.City.GrantUnlock("land-2");
        var before = w.City.TreasuryBalance;
        Assert.True(w.City.ExpandLand());
        Assert.Equal(1, w.City.State.LandTier);
        Assert.Equal(before - Land.Tiers[0].Cost, w.City.TreasuryBalance);
        Assert.Equal(Land.Tiers[0].Area.W, w.City.Grid.Buildable.W);
    }
}

public class ClockTests
{
    [Fact]
    public void Advances_in_fixed_hourly_ticks_at_the_configured_speed()
    {
        var clock = new GameClock();
        int ticks = 0;
        clock.Tick += _ => ticks++;
        clock.SetSpeed(1);
        // 0.25 s at 1x = 0.05 days = 1.2 hours: one tick, with 0.2 hours left over
        Assert.Equal(1, clock.Advance(0.25));
        Assert.Equal(GameClock.SimStart + GameClock.HourMs, clock.Time);
        clock.SetSpeed(10);
        Assert.Equal(12, clock.Advance(0.25));
        Assert.Equal(13, ticks);
    }

    [Fact]
    public void Does_not_burst_after_a_very_long_frame_and_ignores_pause_and_bad_dt()
    {
        var clock = new GameClock();
        clock.SetSpeed(10);
        int long_ = clock.Advance(3600); // a hidden tab: capped at MaxRealDtSeconds
        Assert.Equal(clock.Advance(0.25), long_);
        clock.SetSpeed(0);
        long t = clock.Time;
        Assert.Equal(0, clock.Advance(1));
        clock.SetSpeed(1);
        Assert.Equal(0, clock.Advance(0));
        Assert.Equal(0, clock.Advance(-5));
        Assert.Equal(0, clock.Advance(double.NaN));
        Assert.Equal(t, clock.Time);
    }
}

public class RecreationAndModifierTests
{
    [Fact]
    public void Park_raises_rent_only_for_finished_buildings_in_range()
    {
        var (clock, g) = C.Make();
        C.Place(g, "park", 8, 8); // 2x2: cells 8-9
        C.Place(g, "small-shop", 11, 8); // gap of 1 cell: in range
        C.Place(g, "small-shop", 15, 8); // gap of 5 cells: out of range
        C.FinishConstruction(clock, "park", "small-shop");
        var all = g.State.Buildings;
        int pct = Buildings.Get("park").Levels[0].NeighborBonus.Pct;
        Assert.Equal(pct, CityEconomy.RentBonusPct(all[1], all));
        Assert.Equal(0, CityEconomy.RentBonusPct(all[2], all));
        C.RunDays(clock, 31);
        var income = Buildings.Get("small-shop").Levels[0].Income;
        Assert.Equal(income.Scale(1 + pct / 100.0), g.State.Buildings[1].LifetimeIncome);
        Assert.Equal(income, g.State.Buildings[2].LifetimeIncome);
        C.ExpectLedgerConsistent(g);
    }

    [Fact]
    public void Parks_do_not_stack_and_get_no_bonus_themselves()
    {
        var (clock, g) = C.Make(1_000_000);
        C.Place(g, "park", 6, 8);
        C.Place(g, "park", 6, 12);
        C.Place(g, "small-shop", 9, 10);
        C.FinishConstruction(clock, "park", "small-shop");
        var all = g.State.Buildings;
        Assert.Equal(Buildings.Get("park").Levels[0].NeighborBonus.Pct, CityEconomy.RentBonusPct(all[2], all));
        Assert.Equal(0, CityEconomy.RentBonusPct(all[0], all));
    }

    [Fact]
    public void A_boom_raises_rent_and_a_panic_raises_upkeep()
    {
        var (clock, g) = C.Make(1_000_000);
        C.Place(g, "small-shop", 8, 8);
        C.Place(g, "park", 16, 16); // far from the shop: no park bonus mixes into the figures
        C.FinishConstruction(clock, "small-shop", "park");
        var all = g.State.Buildings;
        var shop = all[0];
        var park = all[1];

        g.SetTradingRecord(new TradingRecord { Trades = 10, RecentTrades = 10, RecentWins = 7, RecentNet = D(100) });
        Assert.Equal(Mood.Booming, g.State.Mood);
        var boom = CityEconomy.ModsFor(shop, all, g.Context());
        Assert.Equal(3, boom.IncomePct);
        Assert.Equal(0, CityEconomy.ModsFor(park, all, g.Context()).IncomePct); // parks earn nothing, so no rent change

        g.SetTradingRecord(new TradingRecord { Trades = 10, RecentTrades = 10, RecentWins = 2, RecentNet = D(-100) });
        Assert.Equal(Mood.Panic, g.State.Mood);
        Assert.Equal(8, CityEconomy.ModsFor(shop, all, g.Context()).ExpensePct);
        Assert.Equal(8, CityEconomy.ModsFor(park, all, g.Context()).ExpensePct); // everyone's upkeep

        // a mood needs enough trades and the money to agree
        g.SetTradingRecord(new TradingRecord { Trades = 4, RecentTrades = 4, RecentWins = 4, RecentNet = D(100) });
        Assert.Equal(Mood.Steady, g.State.Mood);
        g.SetTradingRecord(new TradingRecord { Trades = 10, RecentTrades = 10, RecentWins = 7, RecentNet = D(-5) });
        Assert.Equal(Mood.Steady, g.State.Mood);
    }

    [Fact]
    public void Pays_the_adjusted_amounts_and_the_projection_matches()
    {
        var (clock, g) = C.Make(1_000_000);
        C.Place(g, "small-shop", 8, 8);
        C.FinishConstruction(clock, "small-shop");
        g.SetTradingRecord(new TradingRecord { Trades = 10, RecentTrades = 10, RecentWins = 7, RecentNet = D(100) });
        var projected = CityEconomy.ProjectedNetPer30Days(g.State.Buildings, false, g.Context());
        var before = g.State.Balance;
        C.RunDays(clock, 31);
        Assert.Equal(projected, g.State.Balance - before);
        Assert.Equal(Buildings.Get("small-shop").Levels[0].Income.Scale(1.03), g.State.Buildings[0].LifetimeIncome);
    }

    [Fact]
    public void Policies_switch_with_a_notice_and_locked_ones_need_their_unlock()
    {
        var (_, g) = C.Make();
        g.SetPolicy("nope", true);
        Assert.Empty(g.State.Policies);
        g.SetPolicy("marketing", true);
        Assert.Equal(new[] { "marketing" }, g.State.Policies.ToArray());
        Assert.Contains("switched on", g.State.Notice.Text);
        g.SetPolicy("marketing", false);
        Assert.Empty(g.State.Policies);
        g.SetPolicy("austerity", true); // locked until the challenge is won
        Assert.Empty(g.State.Policies);
        Assert.Equal(NoticeKind.Error, g.State.Notice.Kind);
        g.GrantUnlock("policy-austerity");
        g.SetPolicy("austerity", true);
        Assert.Single(g.State.Policies);
    }

    [Fact]
    public void Marketing_helps_commercial_buildings_and_costs_them_upkeep()
    {
        var (clock, g) = C.Make(1_000_000);
        C.Place(g, "small-shop", 8, 8);
        C.FinishConstruction(clock, "small-shop");
        g.SetPolicy("marketing", true);
        var m = CityEconomy.ModsFor(g.State.Buildings[0], g.State.Buildings, g.Context());
        Assert.Equal((8.0, 12.0), (m.IncomePct, m.ExpensePct));
    }

    [Fact]
    public void Green_city_strengthens_the_park_bonus()
    {
        var (clock, g) = C.Make(1_000_000);
        C.Place(g, "park", 8, 8);
        C.Place(g, "small-shop", 11, 8);
        C.FinishConstruction(clock, "park", "small-shop");
        g.GrantUnlock("policy-green");
        g.SetPolicy("green", true);
        var all = g.State.Buildings;
        Assert.Equal(10 * 1.5, CityEconomy.RentBonusPct(all[1], all, g.State.Policies));
        Assert.Equal(40, CityEconomy.ModsFor(all[0], all, g.Context()).ExpensePct); // the park costs 40% more
    }

    [Fact]
    public void Synergies_count_once_need_finished_close_neighbours_and_are_capped()
    {
        var (clock, g) = C.Make(10_000_000);
        g.UnlockEverything();
        C.Place(g, "restaurant", 8, 8);
        C.Place(g, "small-office", 11, 8); // an office within 3 cells
        C.Place(g, "small-office", 8, 11); // a second office: the rule still counts once
        C.FinishConstruction(clock, "restaurant", "small-office");
        var all = g.State.Buildings;
        var syn = CityEconomy.SynergiesFor(all[0], all);
        Assert.Single(syn);
        Assert.Equal(8, syn[0].Pct);
        // a far away office does not help
        var (clock2, g2) = C.Make(10_000_000);
        g2.UnlockEverything();
        C.Place(g2, "restaurant", 6, 6);
        C.Place(g2, "small-office", 14, 14);
        C.FinishConstruction(clock2, "restaurant", "small-office");
        Assert.Empty(CityEconomy.SynergiesFor(g2.State.Buildings[0], g2.State.Buildings));
        // park + synergy cannot exceed the cap
        var (clock3, g3) = C.Make(10_000_000);
        g3.UnlockEverything();
        C.Place(g3, "plaza", 6, 6); // +15%
        C.Place(g3, "restaurant", 10, 6);
        C.Place(g3, "small-office", 12, 9); // +8% synergy: 23, then a hotel +6% = 29, capped at 25
        C.Place(g3, "hotel", 6, 10);
        C.FinishConstruction(clock3, "plaza", "restaurant", "small-office", "hotel");
        var a3 = g3.State.Buildings;
        var m = CityEconomy.ModsFor(a3[1], a3, g3.Context());
        Assert.True(m.Neighbor <= Modifiers.MaxNeighborBonusPct);
    }
}
