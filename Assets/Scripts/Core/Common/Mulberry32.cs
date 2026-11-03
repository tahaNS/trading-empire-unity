namespace TradingEmpire.Core
{
    /// <summary>Small deterministic PRNG (mulberry32), the same sequence as the web version for the same seed.</summary>
    public sealed class Mulberry32
    {
        uint _a;
        public Mulberry32(uint seed) { _a = seed; }

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
}
