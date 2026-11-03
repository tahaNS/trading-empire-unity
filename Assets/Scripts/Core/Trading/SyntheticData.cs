using System;
using System.Collections.Generic;

namespace TradingEmpire.Core
{
    /// <summary>
    /// SYNTHETIC EUR/USD-like candles for development and testing: a seeded random walk, NOT real market data, and it
    /// must always be labelled as synthetic in the UI. Weekend gaps and Monday-open price gaps are included.
    /// Identical to the web version's generator for the same seed (mulberry32).
    /// </summary>
    public static class SyntheticData
    {
        const long M15 = 15 * 60000;

        sealed class Rng
        {
            uint _a;
            public Rng(uint seed) { _a = seed; }
            public double Next()
            {
                unchecked
                {
                    _a += 0x6D2B79F5u;
                    uint t = _a;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            }
        }

        // JavaScript's Math.round: halves round up (toward +infinity), unlike C#'s banker's rounding.
        static long JsRound(double x) { return (long)Math.Floor(x + 0.5); }

        public static List<Candle> Generate(uint seed = 42, int count = 3000, long timeframeMs = M15)
        {
            var rand = new Rng(seed);
            var output = new List<Candle>();
            long price = 110000; // points (1.10000)
            // Volatility per candle scales with the square root of the candle length (M15 = 35 points).
            double scaleVol = Math.Sqrt(timeframeMs / (double)M15);
            double vol = 35 * scaleVol;
            long t = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            for (int i = 0; i < count; i++)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(t);
                int day = (int)when.DayOfWeek;
                if (day == 6 || (day == 0 && when.Hour < 22))
                {
                    t += timeframeMs;
                    i--;
                    continue;
                }
                if (output.Count > 0 && t - output[output.Count - 1].Time > 3 * 3600000L)
                    price += JsRound((rand.Next() - 0.5) * 120); // weekend gap
                vol = Math.Max(15 * scaleVol, Math.Min(90 * scaleVol, vol * 0.97 + 35 * scaleVol * 0.03 + (rand.Next() - 0.5) * 12 * scaleVol));
                long open = price;
                double hi = open, lo = open, p = open;
                for (int k = 0; k < 4; k++)
                {
                    p += (rand.Next() + rand.Next() + rand.Next() - 1.5) * vol * 1.2;
                    hi = Math.Max(hi, p);
                    lo = Math.Min(lo, p);
                }
                long close = JsRound(p);
                long high = JsRound(Math.Max(hi, Math.Max(open, close)));
                long low = JsRound(Math.Min(lo, Math.Min(open, close)));
                var c = new Candle(t, open, high, low, close) { Volume = JsRound(200 + rand.Next() * 800) };
                output.Add(c);
                price = close;
                t += timeframeMs;
            }
            return output;
        }
    }
}
