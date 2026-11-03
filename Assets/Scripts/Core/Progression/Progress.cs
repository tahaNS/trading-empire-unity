using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Facts derived from real game state. Missions read these; nothing here counts UI clicks.</summary>
    public sealed class ProgressStats
    {
        public long TradesClosed, ProfitableTrades, Buildings, Residential, Commercial;
        public Money Transferred, IncomeEarned, NetPer30, PeakTreasury;

        public long Get(MissionMetric m)
        {
            switch (m)
            {
                case MissionMetric.TradesClosed: return TradesClosed;
                case MissionMetric.ProfitableTrades: return ProfitableTrades;
                case MissionMetric.Transferred: return Transferred.Cents;
                case MissionMetric.Buildings: return Buildings;
                case MissionMetric.Residential: return Residential;
                case MissionMetric.Commercial: return Commercial;
                case MissionMetric.IncomeEarned: return IncomeEarned.Cents;
                case MissionMetric.NetPer30: return NetPer30.Cents;
                default: return PeakTreasury.Cents;
            }
        }
    }

    public sealed class NextGoal
    {
        public MissionDefinition Mission;
        public long Value, Target;
    }

    public static class Progress
    {
        public static ProgressStats Compute(IReadOnlyList<BuildingState> buildings, IReadOnlyList<Position> closed, IReadOnlyList<TransferRecord> transfers, Money peakTreasury, CityContext ctx = null)
        {
            int residential = 0, commercial = 0;
            Money income = Money.Zero;
            int built = 0;
            foreach (var b in buildings)
            {
                if (b.Status != BuildStatus.Complete) continue;
                built++;
                var cat = Buildings.Get(b.DefId).Category;
                if (cat == BuildingCategory.Residential) residential++;
                if (cat == BuildingCategory.Commercial) commercial++;
                income = income + b.LifetimeIncome;
            }
            // NET money moved into the city: cash taken back out is subtracted, so moving the same money back and forth can't farm "Fund the city".
            Money transferred = Money.Zero;
            foreach (var t in transfers) transferred = t.Direction == TransferDirection.ToCity ? transferred + t.Amount : transferred - t.Amount;
            if (transferred.Cents < 0) transferred = Money.Zero;
            return new ProgressStats
            {
                TradesClosed = closed.Count,
                ProfitableTrades = closed.Count(p => p.Exit != null && p.Exit.Net.Cents > 0),
                Transferred = transferred, Buildings = built, Residential = residential, Commercial = commercial, IncomeEarned = income,
                NetPer30 = CityEconomy.ProjectedNetPer30Days(buildings, false, ctx), PeakTreasury = peakTreasury,
            };
        }

        /// <summary>The first mission (in listed order) that is not complete yet, with its measured progress.</summary>
        public static NextGoal NextGoalOf(IReadOnlyDictionary<string, long> completed, ProgressStats stats)
        {
            var mission = Missions.All.FirstOrDefault(m => !completed.ContainsKey(m.Id));
            if (mission == null) return null;
            return new NextGoal { Mission = mission, Value = Math.Min(stats.Get(mission.Metric), mission.Target), Target = mission.Target };
        }

        /// <summary>The player has lost when they cannot trade and the city cannot save them (see GameOverMonitor).</summary>
        public static bool IsBroke(Money tradingBalance, int openPositions, Money treasuryCash, Money cityNetPer30, Money minTradeMargin)
        {
            if (openPositions > 0) return false;
            if (cityNetPer30.Cents > 0) return false;
            long liquid = Math.Max(tradingBalance.Cents, 0) + Math.Max(treasuryCash.Cents, 0);
            return liquid < minTradeMargin.Cents;
        }
    }

    /// <summary>
    /// Watches real gameplay state (closed trades, transfers, buildings, income, treasury) and completes missions when the
    /// measured values reach their targets. Rewards are unlocks only. Also feeds the trading record to the city.
    /// </summary>
    public sealed class ProgressionService
    {
        readonly CityGame _city;
        readonly TradingSession _session;
        readonly CapitalTransferService _transfers;
        Dictionary<string, long> _completed = new Dictionary<string, long>();
        Money _peak = Money.Zero;
        bool _running;
        int _suspended;
        object[] _lastRefs = new object[0];

        public IReadOnlyDictionary<string, long> Completed { get { return _completed; } }
        public Money PeakTreasury { get { return _peak; } }
        public ProgressStats Stats { get; private set; }
        public event Action Changed;

        public ProgressionService(CityGame city, TradingSession session, CapitalTransferService transfers)
        {
            _city = city; _session = session; _transfers = transfers;
            city.MissionTitleFor = id => { var m = Missions.Unlocking(id); return m != null ? m.Title : null; };
            Action go = Evaluate;
            city.Changed += go;
            session.Changed += go;
            transfers.Changed += go;
            Stats = BuildStats();
            Evaluate();
        }

        ProgressStats BuildStats()
        {
            return Progress.Compute(_city.State.Buildings, _session.State.Closed, _transfers.History, _peak, _city.Context());
        }

        static string UnlockName(string id)
        {
            var tier = Land.Tiers.FirstOrDefault(t => t.UnlockId == id);
            if (tier != null) return tier.Name;
            try { return Buildings.Get(id).Name; } catch (ArgumentException) { return id; }
        }

        /// <summary>Run <paramref name="fn"/> with evaluation paused, so partially restored state can't complete missions.</summary>
        public void WithSuspended(Action fn)
        {
            _suspended++;
            try { fn(); } finally { _suspended--; }
        }

        /// <summary>Replace progression from a validated save (no announcements, no re-granting).</summary>
        public void Restore(IReadOnlyDictionary<string, long> completed, Money peakTreasury)
        {
            _completed = new Dictionary<string, long>(completed);
            _peak = peakTreasury;
            _lastRefs = new object[0];
            Stats = BuildStats();
            if (Changed != null) Changed();
            Evaluate();
        }

        /// <summary>Re-measure and complete missions. Cheap early-out when none of the inputs changed.</summary>
        public void Evaluate()
        {
            if (_running || _suspended > 0) return;
            var cs = _city.State;
            var refs = new object[] { cs.Buildings, _session.State.Closed, _transfers.History, cs.Balance, cs.Mood, cs.Policies };
            bool same = _lastRefs.Length == refs.Length;
            for (int i = 0; same && i < refs.Length; i++) same = Equals(refs[i], _lastRefs[i]) || ReferenceEquals(refs[i], _lastRefs[i]);
            if (same) return;
            _lastRefs = refs;
            _running = true;
            try
            {
                _city.SetTradingRecord(TradingRecord.From(_session.State.Closed));
                bool again = true;
                while (again)
                {
                    again = false;
                    _peak = Money.Max(_peak, _city.State.Balance);
                    var stats = BuildStats();
                    foreach (var m in Missions.All)
                    {
                        if (_completed.ContainsKey(m.Id)) continue;
                        if (stats.Get(m.Metric) < m.Target) continue;
                        _completed = new Dictionary<string, long>(_completed) { [m.Id] = _city.Clock.Time };
                        var fresh = m.Unlocks.Where(u => _city.GrantUnlock(u)).Select(UnlockName).ToList();
                        _city.Announce(NoticeKind.Success, "Mission complete: " + m.Title + (fresh.Count > 0 ? " — unlocked " + string.Join(", ", fresh) : ""));
                        again = true;
                    }
                }
                Stats = BuildStats();
                if (Changed != null) Changed();
            }
            finally { _running = false; }
        }
    }

    /// <summary>A summary of the run, shown on the game-over screen.</summary>
    public sealed class GameOverState
    {
        public long Days;
        public int Buildings, TradesClosed, MissionsDone;
        public Money NetTradingPnl;
    }

    /// <summary>
    /// Watches the two accounts and raises "game over" when the player is broke. It never changes or deletes anything: it pauses
    /// time, and the player chooses to start over, so a wrong detection can never destroy a save.
    /// </summary>
    public sealed class GameOverMonitor
    {
        readonly CityGame _city;
        readonly TradingSession _trading;
        readonly ProgressionService _progression;
        readonly GameClock _clock;
        int _suspended;

        public GameOverState State { get; private set; }
        public event Action Changed;

        public GameOverMonitor(CityGame city, TradingSession trading, ProgressionService progression, GameClock clock)
        {
            _city = city; _trading = trading; _progression = progression; _clock = clock;
            city.Changed += Evaluate;
            trading.Changed += Evaluate;
            Evaluate();
        }

        /// <summary>Run <paramref name="fn"/> with checking paused (e.g. while a save is half-applied). Re-checks afterwards.</summary>
        public void WithSuspended(Action fn)
        {
            _suspended++;
            try { fn(); } finally { _suspended--; }
            Evaluate();
        }

        public void Evaluate()
        {
            if (_suspended > 0 || _trading.State.Dataset == null) return; // nothing to judge before a game exists
            var c = _city.State;
            var t = _trading.State;
            bool broke = Progress.IsBroke(_trading.Account.Balance, _trading.Account.Open.Count, _city.TreasuryBalance,
                // a building still under construction is income on its way, so it keeps the player alive
                CityEconomy.ProjectedNetPer30Days(c.Buildings, true, _city.Context()), _trading.MinimumTradeMargin());
            bool was = State != null;
            if (!broke)
            {
                if (was) { State = null; if (Changed != null) Changed(); }
                return;
            }
            if (was) return;
            // Record the loss FIRST: pausing below publishes updates that run this check again.
            State = new GameOverState
            {
                Days = Math.Max(0, (_clock.Time - GameClock.SimStart) / GameClock.DayMs), Buildings = c.Buildings.Count, TradesClosed = t.Stats.TotalTrades,
                NetTradingPnl = t.Stats.NetPnl, MissionsDone = _progression.Completed.Count,
            };
            if (Changed != null) Changed();
            _clock.SetSpeed(0);
            _trading.Pause();
        }
    }
}
