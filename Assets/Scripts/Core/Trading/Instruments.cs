using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public sealed class InstrumentDefinition
    {
        public string Symbol;
        public string Display;
        public string Base;
        public string Quote;
        /// <summary>Price decimals (EUR/USD 5, USD/JPY 3).</summary>
        public int Digits;
        /// <summary>Points per pip (10 for 5-digit EUR/USD and 3-digit USD/JPY).</summary>
        public int PipPoints;
        /// <summary>Units of base currency per 1.00 lot.</summary>
        public int ContractSize;
        public long MinLots, MaxLots, StepLots;
        /// <summary>Sanity range of real prices, to catch files read with the wrong symbol or decimals.</summary>
        public double PlausibleLow, PlausibleHigh;
        /// <summary>Assumed spread (points) when the dataset has no SPREAD column.</summary>
        public long FixedSpreadPts;
        /// <summary>Commission per 1.00 lot, charged per side (open and close).</summary>
        public Money CommissionPerLotPerSide;

        public InstrumentDefinition With(Action<InstrumentDefinition> change)
        {
            var copy = (InstrumentDefinition)MemberwiseClone();
            change(copy);
            return copy;
        }
    }

    /// <summary>The catalogue of tradable symbols. Pairs without USD (crosses) are not supported.</summary>
    public static class Instruments
    {
        public static readonly InstrumentDefinition EURUSD = new InstrumentDefinition
        {
            Symbol = "EURUSD", Display = "EUR/USD", Base = "EUR", Quote = "USD",
            Digits = 5, PipPoints = 10, ContractSize = 100000,
            MinLots = 1, MaxLots = 10000, StepLots = 1,
            PlausibleLow = 0.5, PlausibleHigh = 2.5,
            FixedSpreadPts = 8, // 0.8 pip: an assumed, configurable cost model, not historical truth
            CommissionPerLotPerSide = Money.FromDollars(3.5),
        };

        static InstrumentDefinition FxUsd(string baseCcy, double low, double high, long spread)
        {
            return EURUSD.With(i =>
            {
                i.Symbol = baseCcy + "USD"; i.Display = baseCcy + "/USD"; i.Base = baseCcy;
                i.PlausibleLow = low; i.PlausibleHigh = high; i.FixedSpreadPts = spread;
            });
        }

        static InstrumentDefinition UsdFx(string quote, int digits, double low, double high, long spread)
        {
            return EURUSD.With(i =>
            {
                i.Symbol = "USD" + quote; i.Display = "USD/" + quote; i.Base = "USD"; i.Quote = quote;
                i.Digits = digits; i.PlausibleLow = low; i.PlausibleHigh = high; i.FixedSpreadPts = spread;
            });
        }

        public static readonly InstrumentDefinition GBPUSD = FxUsd("GBP", 0.8, 2.5, 12);
        public static readonly InstrumentDefinition AUDUSD = FxUsd("AUD", 0.3, 1.3, 10);
        public static readonly InstrumentDefinition NZDUSD = FxUsd("NZD", 0.3, 1.2, 14);
        public static readonly InstrumentDefinition USDJPY = UsdFx("JPY", 3, 50, 250, 10);
        public static readonly InstrumentDefinition USDCHF = UsdFx("CHF", 5, 0.5, 2.0, 12);
        public static readonly InstrumentDefinition USDCAD = UsdFx("CAD", 5, 0.8, 1.8, 14);

        /// <summary>Gold: 100 oz per lot, quoted to 2 decimals (1 point = $0.01, a pip is 10 points).</summary>
        public static readonly InstrumentDefinition XAUUSD = EURUSD.With(i =>
        {
            i.Symbol = "XAUUSD"; i.Display = "XAU/USD (gold)"; i.Base = "XAU";
            i.Digits = 2; i.ContractSize = 100; i.PlausibleLow = 200; i.PlausibleHigh = 6000; i.FixedSpreadPts = 30;
        });

        public static readonly IReadOnlyList<InstrumentDefinition> All = new[]
        {
            EURUSD, GBPUSD, USDJPY, AUDUSD, NZDUSD, USDCHF, USDCAD, XAUUSD,
        };

        public static InstrumentDefinition Find(string symbol)
        {
            return All.FirstOrDefault(i => i.Symbol == symbol);
        }

        /// <summary>Price decimals for a trade's symbol (it may be from an earlier dataset), else <paramref name="fallback"/>.</summary>
        public static int DigitsFor(string symbol, int fallback)
        {
            var i = Find(symbol);
            return i != null ? i.Digits : fallback;
        }

        /// <summary>The instrument named in a file name ("EURUSD_M1.csv", "eur-usd.csv", "XAU_USD", "gold"); null if none.</summary>
        public static InstrumentDefinition Detect(string fileName)
        {
            var letters = new string(fileName.ToUpperInvariant().Where(c => c >= 'A' && c <= 'Z').ToArray());
            if (letters.Contains("GOLD")) return XAUUSD;
            return All.FirstOrDefault(i => letters.Contains(i.Symbol));
        }

        /// <summary>Null when the prices are plausible for the instrument, else a message for the player.</summary>
        public static string CheckPrices(InstrumentDefinition inst, IReadOnlyList<long> closes, int? digits = null)
        {
            if (closes.Count == 0) return null;
            int d = digits ?? inst.Digits;
            var sorted = closes.OrderBy(x => x).ToList();
            double median = sorted[sorted.Count / 2] / Math.Pow(10, d);
            if (median >= inst.PlausibleLow && median <= inst.PlausibleHigh) return null;
            var match = All.FirstOrDefault(i => median >= i.PlausibleLow && median <= i.PlausibleHigh);
            return "These prices (around " + median.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") don't look like " + inst.Display + "."
                + (match != null ? " Did you mean " + match.Display + "?" : "")
                + " Pick the right symbol in Load data.";
        }
    }
}
