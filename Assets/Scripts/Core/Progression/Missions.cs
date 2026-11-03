using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Metrics are computed from REAL game state (see <see cref="Progress"/>), never from UI clicks. Money metrics are compared in cents.</summary>
    public enum MissionMetric { TradesClosed, ProfitableTrades, Transferred, Buildings, Residential, Commercial, IncomeEarned, NetPer30, PeakTreasury }

    /// <summary>Rewards are unlocks only; missions never create money.</summary>
    public sealed class MissionDefinition
    {
        public string Id, Title, Description, Hint;
        public MissionMetric Metric;
        public long Target;
        /// <summary>Unlock ids granted on completion (buildings by id, land as "land-N").</summary>
        public string[] Unlocks;
    }

    public static class Missions
    {
        static MissionDefinition M(string id, string title, string desc, string hint, MissionMetric metric, long target, params string[] unlocks)
        {
            return new MissionDefinition { Id = id, Title = title, Description = desc, Hint = hint, Metric = metric, Target = target, Unlocks = unlocks };
        }
        static long D(double dollars) { return Money.FromDollars(dollars).Cents; }

        public static readonly IReadOnlyList<MissionDefinition> All = new[]
        {
            M("first-trade", "First trade", "Close your first trade in the terminal.", "Open the Terminal, place a buy or sell with a stop-loss, then close it.", MissionMetric.TradesClosed, 1),
            M("green-trade", "In the green", "Close a trade with a net profit.", "Set a stop-loss and a take-profit, step the chart forward, and close in profit.", MissionMetric.ProfitableTrades, 1),
            M("first-transfer", "Fund the city", "Transfer $3,000 from trading to the city treasury.", "Open Transfer, type the amount and press To city. Only realized money can move.", MissionMetric.Transferred, D(3000)),
            M("first-build", "Break ground", "Construct your first building.", "Pick a building you can afford in the build bar, click the map, and wait for it to finish.", MissionMetric.Buildings, 1),
            M("first-rent", "First rent", "Earn $50 in building income.", "Let time run (try a faster speed). The first rent arrives about a month after the building finishes.", MissionMetric.IncomeEarned, D(50)),
            M("reinvest", "Reinvest", "Own 2 finished buildings.", "Build a second property. If the treasury is short, move more profit over from trading.", MissionMetric.Buildings, 2),
            M("rental-income", "Landlord income", "Earn $800 in total building income.", "Keep time running and keep buildings earning. Parks raise the rent around them.", MissionMetric.IncomeEarned, D(800), "restaurant"),
            M("portfolio", "Growing portfolio", "Own 4 buildings.", "Keep building. A new land district and the Fountain Plaza unlock when you get there.", MissionMetric.Buildings, 4, "land-2", "plaza"),
            M("trading-desk", "Open a trading desk", "Close 10 trades in the terminal.", "Keep trading. Every closed trade counts, win or lose. The Trading HQ landmark unlocks.", MissionMetric.TradesClosed, 10, "trading-hq"),
            M("housing", "Housing market", "Own 2 residential buildings.", "Build homes: Houses, Apartments and towers all count as residential.", MissionMetric.Residential, 2, "luxury-tower", "hotel"),
            M("high-street", "High street", "Own 3 commercial buildings.", "Build shops, restaurants and hotels. They all count as commercial.", MissionMetric.Commercial, 3, "corporate-office"),
            M("steady-income", "Steady income", "Reach $2,000 net city income per 30 days.", "Upgrade buildings and build more. Watch the net income figure at the top.", MissionMetric.NetPer30, D(2000), "land-3"),
            M("tycoon", "Tycoon", "Hold $200,000 in the city treasury at once.", "Move trading profits into the city and let rent pile up. Hold it all at once.", MissionMetric.PeakTreasury, D(200000), "land-4"),
        };

        public static MissionDefinition Get(string id)
        {
            var m = All.FirstOrDefault(x => x.Id == id);
            if (m == null) throw new ArgumentException("Unknown mission: " + id);
            return m;
        }

        /// <summary>The mission that unlocks something, for "locked" explanations in the UI.</summary>
        public static MissionDefinition Unlocking(string unlockId) { return All.FirstOrDefault(m => m.Unlocks.Contains(unlockId)); }

        public static bool IsMoney(MissionMetric m)
        {
            return m == MissionMetric.Transferred || m == MissionMetric.IncomeEarned || m == MissionMetric.NetPer30 || m == MissionMetric.PeakTreasury;
        }
    }
}
