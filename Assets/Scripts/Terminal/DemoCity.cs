using TradingEmpire.Core;

namespace TradingEmpire.Terminal
{
    /// <summary>A ready-made small town for screenshots and checks (never used in normal play).</summary>
    public static class DemoCity
    {
        static void Place(CityGame city, string defId, int x, int y)
        {
            city.StartPlacement(defId);
            city.TryPlace(new Cell(x, y));
        }

        public static void Populate(GameWorld w)
        {
            w.City.GrantFunds(Money.FromDollars(2_000_000));
            w.City.UnlockEverything();
            Place(w.City, "apartment", 10, 10); Place(w.City, "house", 12, 10); Place(w.City, "house", 13, 10); Place(w.City, "house", 14, 10);
            Place(w.City, "park", 12, 12); Place(w.City, "small-shop", 10, 13); Place(w.City, "small-shop", 15, 12);
            Place(w.City, "small-office", 17, 10); Place(w.City, "restaurant", 17, 13); Place(w.City, "luxury-tower", 18, 17); Place(w.City, "hotel", 11, 17);
            Place(w.City, "plaza", 13, 18);
            w.Clock.SkipDays(12);                         // most of it is finished...
            Place(w.City, "house", 20, 11);               // ...and one site is still going up
            w.City.SetPolicy("marketing", true);
            w.City.SetTradingRecord(new TradingRecord { Trades = 12, RecentTrades = 10, RecentWins = 7, RecentNet = Money.FromDollars(300), NetPnl = Money.FromDollars(300) });
            w.Clock.SkipDays(2);
            w.City.CancelPlacement();
        }
    }
}
