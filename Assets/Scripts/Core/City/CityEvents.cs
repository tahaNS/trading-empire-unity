using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Something that is happening to the city right now. Saved with the game.</summary>
    public sealed class CityEvent
    {
        public int Seq;
        public EventKind Kind;
        public long StartsAt, EndsAt;
        /// <summary>Fire: the building that is closed.</summary>
        public string BuildingId;
    }

    public sealed class RolledEvent
    {
        public EventKind Kind;
        public string BuildingId;
    }

    public static class CityEvents
    {
        /// <summary>A number in [0, 1) that depends only on the seed, the day and a salt (no hidden state).</summary>
        public static double Hash01(uint seed, long day, int salt)
        {
            unchecked
            {
                uint h = seed ^ ((uint)(day + 1) * 0x9e3779b1u) ^ ((uint)(salt + 7) * 0x85ebca6bu);
                h = (h ^ (h >> 16)) * 0x45d9f3bu;
                h = (h ^ (h >> 16)) * 0x45d9f3bu;
                return (h ^ (h >> 16)) / 4294967296.0;
            }
        }

        /// <summary>Decide whether something happens on <paramref name="day"/>. Pure: same seed and day, same answer.</summary>
        public static RolledEvent Roll(uint seed, long day, IReadOnlyList<string> candidates)
        {
            if (candidates.Count < EventConfig.MinBuildings) return null;
            if (Hash01(seed, day, 1) >= EventConfig.DailyChance) return null;
            int total = EventConfig.Events.Sum(e => e.Weight);
            double pick = Hash01(seed, day, 2) * total;
            foreach (var e in EventConfig.Events)
            {
                pick -= e.Weight;
                if (pick < 0)
                {
                    if (e.Kind != EventKind.Fire) return new RolledEvent { Kind = e.Kind };
                    int i = (int)Math.Floor(Hash01(seed, day, 3) * candidates.Count);
                    return i >= 0 && i < candidates.Count ? new RolledEvent { Kind = EventKind.Fire, BuildingId = candidates[i] } : null;
                }
            }
            return null;
        }

        public static long EndsAt(EventKind kind, long now)
        {
            var def = EventConfig.Get(kind);
            return now + (def != null ? def.Days : 0) * GameClock.DayMs;
        }

        /// <summary>The share (0 to 1) of a bad event that the city's insurance absorbs.</summary>
        public static double ShieldFor(EventKind kind, double shield) { return Math.Min(shield, 1) * EventConfig.InsuranceShield(kind); }

        /// <summary>Repair bill for a fire, after insurance.</summary>
        public static Money RepairCost(Money purchaseCost, double shield)
        {
            var def = EventConfig.Get(EventKind.Fire);
            var baseCost = purchaseCost.Scale((def != null ? def.RepairPct : 0) / 100.0);
            return baseCost.Scale(1 - ShieldFor(EventKind.Fire, shield));
        }

        /// <summary>Income change (percent) a running event gives an earning building.</summary>
        public static (double pct, bool closed) IncomePct(IEnumerable<CityEvent> events, string buildingId, double shield)
        {
            double pct = 0;
            foreach (var ev in events)
            {
                var def = EventConfig.Get(ev.Kind);
                if (def == null) continue;
                if (ev.Kind == EventKind.Fire)
                {
                    if (ev.BuildingId == buildingId) return (-100, true);
                    continue;
                }
                double baseP = def.IncomePct;
                pct += baseP < 0 ? baseP * (1 - ShieldFor(ev.Kind, shield)) : baseP;
            }
            return (pct, false);
        }
    }

    /// <summary>The part of the trading record that landmarks and the city's mood read. Closed trades only, derived, never saved.</summary>
    public sealed class TradingRecord
    {
        public int Trades;
        public Money NetPnl;
        public double? ProfitFactor;
        /// <summary>The latest closed trades (see Modifiers.MoodWindow): how many, how many won, and their net result.</summary>
        public int RecentTrades, RecentWins;
        public Money RecentNet;

        public static readonly TradingRecord None = new TradingRecord();

        public bool SameAs(TradingRecord o)
        {
            return Trades == o.Trades && NetPnl == o.NetPnl && ProfitFactor == o.ProfitFactor && RecentTrades == o.RecentTrades && RecentWins == o.RecentWins && RecentNet == o.RecentNet;
        }

        /// <summary>The record for closed trades: totals from the statistics, plus the recent window by exit time.</summary>
        public static TradingRecord From(IEnumerable<Position> closed)
        {
            var list = closed.Where(p => p.Exit != null).ToList();
            var stats = TradingStatistics.Compute(list);
            var last = list.OrderBy(p => p.Exit.Time).ToList();
            if (last.Count > Modifiers.MoodWindow) last = last.Skip(last.Count - Modifiers.MoodWindow).ToList();
            Money net = Money.Zero;
            int wins = 0;
            foreach (var p in last) { net = net + p.Exit.Net; if (p.Exit.Net.Cents > 0) wins++; }
            return new TradingRecord { Trades = stats.TotalTrades, NetPnl = stats.NetPnl, ProfitFactor = stats.ProfitFactor, RecentTrades = last.Count, RecentWins = wins, RecentNet = net };
        }
    }

    public sealed class RequirementLine
    {
        public string Label;
        public bool Met;
    }

    public static class Landmarks
    {
        /// <summary>
        /// The city's mood from your latest closed trades. It needs a minimum number of trades before it reacts, then a
        /// hot streak (high win rate AND a net profit) is a boom and a rout (low win rate AND a net loss) is a panic.
        /// </summary>
        public static Mood MoodOf(TradingRecord record)
        {
            int n = record.RecentTrades;
            if (n < Modifiers.MoodMinTrades) return Mood.Steady;
            double winRate = (double)record.RecentWins / n;
            if (winRate >= Modifiers.BoomWinRate && record.RecentNet.Cents > 0) return Mood.Booming;
            if (winRate <= Modifiers.PanicWinRate && record.RecentNet.Cents < 0) return Mood.Panic;
            return Mood.Steady;
        }

        static bool Meets(TradeLevelRule rule, TradingRecord r)
        {
            if (r.Trades < rule.MinTrades) return false;
            if (rule.MinNetPnl.HasValue && !(r.NetPnl > rule.MinNetPnl.Value)) return false;
            if (rule.MinProfitFactor.HasValue && !((r.ProfitFactor ?? 0) >= rule.MinProfitFactor.Value)) return false;
            return true;
        }

        /// <summary>The highest level whose rule is met, and at least 1 (it was built, so level 1 is kept even if the record falls).</summary>
        public static int TradeLevelOf(BuildingDefinition def, TradingRecord record)
        {
            if (def.TradeLevels == null) return 1;
            int level = 1;
            for (int i = 0; i < def.TradeLevels.Length; i++) if (Meets(def.TradeLevels[i], record)) level = i + 1;
            return level;
        }

        /// <summary>The level to draw and describe: trade-based for landmarks, the paid-for level otherwise.</summary>
        public static int EffectiveLevel(int paidLevel, BuildingDefinition def, TradingRecord record)
        {
            return def.TradeLevels != null ? TradeLevelOf(def, record) : paidLevel;
        }

        /// <summary>What the next level still needs, as checklist lines; null at the top level.</summary>
        public static (string name, List<RequirementLine> lines)? NextLevelRequirements(BuildingDefinition def, TradingRecord record)
        {
            if (def.TradeLevels == null) return null;
            int current = TradeLevelOf(def, record);
            if (current >= def.TradeLevels.Length) return null;
            var rule = def.TradeLevels[current];
            var level = def.Levels[current];
            var lines = new List<RequirementLine> { new RequirementLine { Label = record.Trades + " / " + rule.MinTrades + " closed trades", Met = record.Trades >= rule.MinTrades } };
            if (rule.MinNetPnl.HasValue)
                lines.Add(new RequirementLine { Label = "net P/L above " + rule.MinNetPnl.Value + " (now " + record.NetPnl + ")", Met = record.NetPnl > rule.MinNetPnl.Value });
            if (rule.MinProfitFactor.HasValue)
                lines.Add(new RequirementLine
                {
                    Label = "profit factor " + rule.MinProfitFactor.Value.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " or better (now "
                        + (record.ProfitFactor.HasValue ? record.ProfitFactor.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "n/a") + ")",
                    Met = (record.ProfitFactor ?? 0) >= rule.MinProfitFactor.Value,
                });
            return (level.Name, lines);
        }
    }
}
