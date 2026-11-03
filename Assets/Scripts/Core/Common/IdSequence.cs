using System.Collections.Generic;
using System.Globalization;

namespace TradingEmpire.Core
{
    /// <summary>Deterministic id source ("b1", "b2", ...) that can resume after restoring saved state.</summary>
    public sealed class IdSequence
    {
        readonly string _prefix;
        readonly int _start;
        int _next;

        public IdSequence(string prefix, int start = 1)
        {
            _prefix = prefix;
            _start = start;
            _next = start;
        }

        public string Next() { return _prefix + (_next++).ToString(CultureInfo.InvariantCulture); }

        /// <summary>Continue numbering after the highest numeric suffix found among <paramref name="ids"/> (used when loading a save).</summary>
        public void ResetAfter(IEnumerable<string> ids)
        {
            int max = _start - 1;
            foreach (var id in ids)
            {
                if (!id.StartsWith(_prefix, System.StringComparison.Ordinal)) continue;
                if (int.TryParse(id.Substring(_prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > max) max = n;
            }
            _next = max + 1;
        }
    }
}
