using System;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Everything that makes up one game. Pure C#: no engine types. Wires the clock, city, trading account and transfers together.</summary>
    public sealed class GameWorld
    {
        public readonly GameClock Clock;
        public readonly CityGame City;
        public readonly TradingSession Trading;
        public readonly CapitalTransferService Transfers;
        public readonly ProgressionService Progression;
        /// <summary>Raises game over when the player is broke; see <see cref="Progress.IsBroke"/>.</summary>
        public readonly GameOverMonitor GameOver;
        /// <summary>Results of the trading challenges (they have their own accounts, apart from the game).</summary>
        public readonly ChallengeStore Challenges;
        /// <summary>Milestones the player has reached (saved).</summary>
        public readonly AchievementTracker Achievements;

        /// <summary>M1 candles, so every timeframe from 1m up can be charted.</summary>
        public const uint DefaultSeed = 42;
        public const int DefaultCount = 30000;
        public const string DefaultName = "SYNTHETIC EUR/USD M1 (seeded random walk)";

        GameWorld(GameClock clock, CityGame city, TradingSession trading, CapitalTransferService transfers, ProgressionService progression, GameOverMonitor gameOver,
            ChallengeStore challenges, AchievementTracker achievements)
        {
            Clock = clock; City = city; Trading = trading; Transfers = transfers; Progression = progression; GameOver = gameOver; Challenges = challenges; Achievements = achievements;
        }

        /// <summary>Wire the systems together. The centre plot is the player's starting land; the treasury starts empty (funded only by transfers).</summary>
        public static GameWorld Create()
        {
            var clock = new GameClock();
            var city = new CityGame(new CityGrid(Land.WorldSize, Land.WorldSize, 1f, Land.StartingArea), clock);
            city.Grid.RoadRule = Roads.IsRoadCell;
            clock.Tick += city.Tick;
            var trading = new TradingSession();
            var transfers = TransferWiring.Create(trading, city);
            // the progression service completes missions and feeds the trading record to the city (mood, landmarks)
            var progression = new ProgressionService(city, trading, transfers);
            var gameOver = new GameOverMonitor(city, trading, progression, clock);
            var challenges = new ChallengeStore();
            var achievements = new AchievementTracker();
            var world = new GameWorld(clock, city, trading, transfers, progression, gameOver, challenges, achievements);
            Action check = world.CheckAchievements;
            city.Changed += check;
            progression.Changed += check;
            challenges.Changed += check;
            achievements.OnEarned = a => city.Announce(NoticeKind.Success, "Achievement: " + a.Name);
            return world;
        }

        void CheckAchievements()
        {
            var c = City.State;
            var res = Challenges.Results.Values.ToList();
            Achievements.Evaluate(new AchievementFacts
            {
                Trades = c.Record.Trades, NetPnl = c.Record.NetPnl, Buildings = c.Buildings.Count, LandTier = c.LandTier,
                MissionsDone = Progression.Completed.Count, MissionsTotal = Missions.All.Count, PeakTreasury = Progression.PeakTreasury,
                ChallengeWins = res.Count(r => r.Wins > 0), ChallengeStars = res.Sum(r => r.Stars), ChallengesTotal = TradingEmpire.Core.Challenges.All.Count,
                PoliciesOn = c.Policies.Count, Boom = c.Mood == Mood.Booming,
            });
        }

        /// <summary>Begin a fresh game on the labelled synthetic dataset.</summary>
        public void StartNewGame()
        {
            Trading.LoadDataset(SyntheticData.Generate(DefaultSeed, DefaultCount, 60000), DefaultName, true, null, DefaultSeed);
        }

        /// <summary>Called every frame: advances the city clock (income, construction, events) and the trading replay.</summary>
        public void Advance(double realDtSeconds, bool tradingOpen)
        {
            Clock.Advance(realDtSeconds);
            if (tradingOpen && Trading.State.Status == SessionStatus.Ready) Trading.Advance(realDtSeconds);
        }
    }
}
