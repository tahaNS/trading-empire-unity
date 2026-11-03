using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Everything an achievement can look at. Plain numbers, so the rules stay easy to test.</summary>
    public sealed class AchievementFacts
    {
        public int Trades, Buildings, LandTier, MissionsDone, MissionsTotal, ChallengeWins, ChallengeStars, ChallengesTotal, PoliciesOn;
        public Money NetPnl, PeakTreasury;
        public bool Boom;
    }

    public sealed class AchievementDefinition
    {
        public string Id, Name, Text;
        public Func<AchievementFacts, bool> Test;
    }

    public static class Achievements
    {
        static AchievementDefinition A(string id, string name, string text, Func<AchievementFacts, bool> test) { return new AchievementDefinition { Id = id, Name = name, Text = text, Test = test }; }
        static Money D(double d) { return Money.FromDollars(d); }

        public static readonly IReadOnlyList<AchievementDefinition> All = new[]
        {
            A("first-trade", "First steps", "Close your first trade.", f => f.Trades >= 1),
            A("ten-trades", "Getting going", "Close 10 trades.", f => f.Trades >= 10),
            A("fifty-trades", "Market regular", "Close 50 trades.", f => f.Trades >= 50),
            A("in-profit", "In the green", "Have $1,000 of net trading profit.", f => f.NetPnl >= D(1000)),
            A("first-building", "Ground broken", "Build your first building.", f => f.Buildings >= 1),
            A("ten-buildings", "Small town", "Have 10 buildings.", f => f.Buildings >= 10),
            A("skyline", "Skyline", "Have 25 buildings.", f => f.Buildings >= 25),
            A("more-land", "Room to grow", "Buy more land.", f => f.LandTier >= 1),
            A("treasury-100k", "Deep pockets", "Reach $100,000 in the city treasury.", f => f.PeakTreasury >= D(100000)),
            A("five-missions", "On a mission", "Complete 5 missions.", f => f.MissionsDone >= 5),
            A("all-missions", "Mission complete", "Complete every mission.", f => f.MissionsTotal > 0 && f.MissionsDone >= f.MissionsTotal),
            A("challenge-win", "Challenger", "Win a trading challenge.", f => f.ChallengeWins >= 1),
            A("challenge-all", "Full set", "Win every trading challenge.", f => f.ChallengesTotal > 0 && f.ChallengeWins >= f.ChallengesTotal),
            A("challenge-stars", "Star trader", "Earn 6 stars in challenges.", f => f.ChallengeStars >= 6),
            A("policy", "City hall", "Switch on a city policy.", f => f.PoliciesOn >= 1),
            A("boom", "Boom town", "Put the city in a trading boom.", f => f.Boom),
        };
    }

    /// <summary>Earned achievements are kept for good (and saved), even if the numbers fall again later.</summary>
    public sealed class AchievementTracker
    {
        List<string> _earned = new List<string>();
        bool _paused;

        public IReadOnlyList<string> Earned { get { return _earned; } }
        /// <summary>Called with each newly earned achievement.</summary>
        public Action<AchievementDefinition> OnEarned;
        public event Action Changed;

        /// <summary>Run <paramref name="fn"/> without judging anything, so a half-restored game can't earn or announce.</summary>
        public void WithPaused(Action fn)
        {
            _paused = true;
            try { fn(); } finally { _paused = false; }
        }

        public void Evaluate(AchievementFacts facts)
        {
            if (_paused) return;
            var fresh = Achievements.All.Where(a => !_earned.Contains(a.Id) && a.Test(facts)).ToList();
            if (fresh.Count == 0) return;
            _earned = _earned.Concat(fresh.Select(a => a.Id)).ToList();
            if (Changed != null) Changed();
            if (OnEarned != null) foreach (var a in fresh) OnEarned(a);
        }

        public void Restore(IEnumerable<string> ids)
        {
            var known = new HashSet<string>(Achievements.All.Select(a => a.Id));
            _earned = ids.Distinct().Where(known.Contains).ToList();
            if (Changed != null) Changed();
        }
    }
}
