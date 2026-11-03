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

        /// <summary>M1 candles, so every timeframe from 1m up can be charted.</summary>
        public const uint DefaultSeed = 42;
        public const int DefaultCount = 30000;
        public const string DefaultName = "SYNTHETIC EUR/USD M1 (seeded random walk)";

        GameWorld(GameClock clock, CityGame city, TradingSession trading, CapitalTransferService transfers)
        {
            Clock = clock; City = city; Trading = trading; Transfers = transfers;
        }

        /// <summary>Wire the systems together. The centre plot is the player's starting land; the treasury starts empty (funded only by transfers).</summary>
        public static GameWorld Create()
        {
            var clock = new GameClock();
            var city = new CityGame(new CityGrid(Land.WorldSize, Land.WorldSize, 1f, Land.StartingArea), clock);
            city.Grid.RoadRule = Roads.IsRoadCell;
            clock.Tick += city.Tick;
            // the trading record feeds the city's mood and landmarks whenever the trading side changes
            var trading = new TradingSession();
            trading.Changed += () => city.SetTradingRecord(TradingRecord.From(trading.Account.Closed));
            var transfers = TransferWiring.Create(trading, city);
            return new GameWorld(clock, city, trading, transfers);
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
