using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum BuildStatus { Building, Complete }

    public sealed class BuildingState
    {
        public string Id, DefId;
        public Cell Origin;
        public int Rotation;
        public int Level;
        /// <summary>Historical purchase cost (what was actually paid at construction).</summary>
        public Money PurchaseCost;
        public long BuiltAt;
        /// <summary>Building until the clock reaches CompletesAt; only Complete buildings earn or count.</summary>
        public BuildStatus Status;
        public long CompletesAt;
        /// <summary>Next scheduled income/expense settlement (sim time).</summary>
        public long NextPayoutAt;
        public Money LifetimeIncome, LifetimeExpense;

        public BuildingState Clone() { return (BuildingState)MemberwiseClone(); }
    }

    public sealed class Payout
    {
        /// <summary>Scheduled payout time (not the tick time), so results don't depend on tick size.</summary>
        public long Time;
        public Money Income, Expense;
    }

    public sealed class SynergyHit
    {
        public string Label;
        public int Pct;
    }

    /// <summary>Everything that changes a building's income and upkeep beyond its base numbers: the mood, the policies on, and events.</summary>
    public sealed class CityContext
    {
        public Mood Mood = Mood.Steady;
        public IReadOnlyList<string> Policies = new string[0];
        /// <summary>Fire, storm or festival going on now.</summary>
        public IReadOnlyList<CityEvent> Events = new CityEvent[0];
        public static CityContext Neutral { get { return new CityContext(); } }
    }

    public sealed class Mods
    {
        /// <summary>Total change of the building's income, in percent.</summary>
        public double IncomePct;
        /// <summary>Total change of its upkeep, in percent.</summary>
        public double ExpensePct;
        public double Park;
        public List<SynergyHit> Synergies = new List<SynergyHit>();
        /// <summary>Park bonus plus synergies after the cap.</summary>
        public double Neighbor;
        public double Mood, MoodExpense, PolicyIncome, PolicyExpense;
        /// <summary>Income change from a running event (a fire closes the building: -100).</summary>
        public double Event;
        public bool Closed;

        public static Mods None { get { return new Mods(); } }
    }

    public static class CityEconomy
    {
        static BuildingLevel LevelOf(BuildingState b, out BuildingDefinition def)
        {
            def = Buildings.Get(b.DefId);
            if (b.Level < 1 || b.Level > def.Levels.Length) throw new InvalidOperationException("Building " + b.Id + " has invalid level " + b.Level);
            return def.Levels[b.Level - 1];
        }

        public static RectI BuildingRect(BuildingState b)
        {
            var fp = CityGrid.Rotated(Buildings.Get(b.DefId).Footprint, b.Rotation);
            return new RectI(b.Origin.X, b.Origin.Y, fp.W, fp.H);
        }

        /// <summary>Empty cells between two rectangles of cells (0 when they touch).</summary>
        public static int RectGap(RectI p, RectI q)
        {
            int dx = Math.Max(Math.Max(q.X - (p.X + p.W), p.X - (q.X + q.W)), 0);
            int dy = Math.Max(Math.Max(q.Y - (p.Y + p.H), p.Y - (q.Y + q.H)), 0);
            return Math.Max(dx, dy);
        }

        static int Gap(BuildingState a, BuildingState b) { return RectGap(BuildingRect(a), BuildingRect(b)); }

        static double ParkMultiplier(IReadOnlyList<string> policies)
        {
            double m = 1;
            foreach (var id in policies) { var p = Modifiers.GetPolicy(id); m *= p != null ? p.ParkBonusMult : 1; }
            return m;
        }

        /// <summary>Percent added to a building's income by finished recreation buildings in range. Only the strongest counts.</summary>
        public static double RentBonusPct(BuildingState b, IReadOnlyList<BuildingState> all, IReadOnlyList<string> policies = null)
        {
            LevelOf(b, out var def);
            if (def.Category == BuildingCategory.Recreation || b.Status != BuildStatus.Complete) return 0;
            int best = 0;
            foreach (var o in all)
            {
                if (o.Id == b.Id || o.Status != BuildStatus.Complete) continue;
                var bonus = LevelOf(o, out var _).NeighborBonus;
                if (bonus != null && bonus.Pct > best && Gap(b, o) <= bonus.Radius) best = bonus.Pct;
            }
            return best * ParkMultiplier(policies ?? new string[0]);
        }

        /// <summary>Synergies a finished building enjoys: each rule counts once, however many neighbours help.</summary>
        public static List<SynergyHit> SynergiesFor(BuildingState b, IReadOnlyList<BuildingState> all)
        {
            var output = new List<SynergyHit>();
            if (b.Status != BuildStatus.Complete) return output;
            LevelOf(b, out var def);
            foreach (var rule in Modifiers.Synergies)
            {
                if (!rule.To.Matches(def)) continue;
                bool helped = all.Any(o => o.Id != b.Id && o.Status == BuildStatus.Complete && rule.From.Matches(Buildings.Get(o.DefId)) && Gap(b, o) <= rule.Radius);
                if (helped) output.Add(new SynergyHit { Label = rule.Label, Pct = rule.Pct });
            }
            return output;
        }

        /// <summary>The total effect on one building. Buildings still under construction have none.</summary>
        public static Mods ModsFor(BuildingState b, IReadOnlyList<BuildingState> all, CityContext ctx = null)
        {
            ctx = ctx ?? CityContext.Neutral;
            if (b.Status != BuildStatus.Complete) return Mods.None;
            var level = LevelOf(b, out var def);
            bool earns = level.Income.Cents > 0 && def.Category != BuildingCategory.Recreation && def.Category != BuildingCategory.Landmark;
            double park = RentBonusPct(b, all, ctx.Policies);
            var synergies = SynergiesFor(b, all);
            double neighbor = Math.Min(park + synergies.Sum(x => x.Pct), Modifiers.MaxNeighborBonusPct);
            var moodEffect = Modifiers.EffectOf(ctx.Mood);
            double mood = earns ? moodEffect.IncomePct : 0;
            double moodExpense = moodEffect.ExpensePct;
            double policyIncome = 0, policyExpense = 0;
            foreach (var id in ctx.Policies)
            {
                var policy = Modifiers.GetPolicy(id);
                if (policy == null) continue;
                foreach (var e in policy.Effects)
                {
                    if (e.Target.HasValue && e.Target.Value != def.Category) continue;
                    if (earns) policyIncome += e.IncomePct;
                    policyExpense += e.ExpensePct;
                }
            }
            double shield = ctx.Policies.Sum(id => { var p = Modifiers.GetPolicy(id); return p != null ? p.EventShield : 0; });
            var ev = CityEvents.IncomePct(ctx.Events, b.Id, shield);
            double incomePct = earns ? (ev.closed ? -100 : Math.Max(neighbor + mood + policyIncome + ev.pct, -100)) : 0;
            double expensePct = Math.Max(moodExpense + policyExpense, -100);
            return new Mods
            {
                IncomePct = incomePct, ExpensePct = expensePct, Park = park, Synergies = synergies, Neighbor = neighbor,
                Mood = mood, MoodExpense = moodExpense, PolicyIncome = policyIncome, PolicyExpense = policyExpense,
                Event = earns ? ev.pct : 0, Closed = ev.closed,
            };
        }

        static Money Boosted(Money income, double pct) { return pct != 0 ? income.Scale(1 + pct / 100) : income; }
        static Money Adjusted(Money expense, double pct) { return pct != 0 ? expense.Scale(1 + pct / 100) : expense; }

        public static Money NetPerInterval(BuildingState b, Mods mods = null)
        {
            var level = LevelOf(b, out var _);
            mods = mods ?? Mods.None;
            return Boosted(level.Income, mods.IncomePct) - Adjusted(level.Expense, mods.ExpensePct);
        }

        /// <summary>Net income normalised to 30 days, for display.</summary>
        public static Money NetPer30Days(BuildingState b, Mods mods = null)
        {
            LevelOf(b, out var def);
            return NetPerInterval(b, mods).Scale(30.0 / def.IncomeIntervalDays);
        }

        /// <summary>
        /// Projected net per 30 days, including recreation bonuses, synergies, the city's mood and the policies on.
        /// Buildings under construction earn nothing yet, so they are left out unless <paramref name="includePlanned"/>.
        /// </summary>
        public static Money ProjectedNetPer30Days(IReadOnlyList<BuildingState> buildings, bool includePlanned = false, CityContext ctx = null)
        {
            Money sum = Money.Zero;
            foreach (var b in buildings)
            {
                if (!includePlanned && b.Status != BuildStatus.Complete) continue;
                sum = sum + NetPer30Days(b, ModsFor(b, buildings, ctx));
            }
            return sum;
        }

        /// <summary>All payouts due at or before <paramref name="now"/>, using the building's CURRENT level.</summary>
        public static (List<Payout> payouts, long nextPayoutAt) CollectDuePayouts(BuildingState b, long now, Mods mods = null)
        {
            var level = LevelOf(b, out var def);
            long interval = def.IncomeIntervalDays * GameClock.DayMs;
            mods = mods ?? Mods.None;
            var payouts = new List<Payout>();
            long next = b.NextPayoutAt;
            while (next <= now)
            {
                payouts.Add(new Payout { Time = next, Income = Boosted(level.Income, mods.IncomePct), Expense = Adjusted(level.Expense, mods.ExpensePct) });
                next += interval;
            }
            return (payouts, next);
        }
    }
}
