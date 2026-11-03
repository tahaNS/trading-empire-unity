using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TradingEmpire.Core
{
    /// <summary>
    /// Money: integer cents of the base currency (USD). Rounding happens only in
    /// <see cref="MoneyMath.RoundHalfEven"/> (round half to even); never use floating point for balances.
    /// </summary>
    public readonly struct Money : IComparable<Money>, IEquatable<Money>
    {
        public readonly long Cents;

        public Money(long cents) { Cents = cents; }

        public static readonly Money Zero = new Money(0);

        public static Money FromCents(long cents) { return new Money(cents); }

        /// <summary>Dollars as a number, rounded half-even to cents. For config literals; prefer ParseDollars for input.</summary>
        public static Money FromDollars(double dollars)
        {
            return new Money(MoneyMath.RoundHalfEven(dollars * 100));
        }

        /// <summary>Parse "1234.56" or "-0.5" exactly (no float math).</summary>
        public static Money ParseDollars(string text)
        {
            var m = Regex.Match(text.Trim(), @"^(-?)(\d+)(?:\.(\d{1,2}))?$");
            if (!m.Success) throw new FormatException("Invalid dollar amount: \"" + text + "\"");
            long whole = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            long frac = m.Groups[3].Success ? long.Parse(m.Groups[3].Value.PadRight(2, '0'), CultureInfo.InvariantCulture) : 0;
            long total = whole * 100 + frac;
            return new Money(m.Groups[1].Value == "-" ? -total : total);
        }

        /// <summary>Scale by a dimensionless factor (rate, ratio), rounding half-even once.</summary>
        public Money Scale(double factor) { return new Money(MoneyMath.RoundHalfEven(Cents * factor)); }

        public bool IsNegative { get { return Cents < 0; } }

        public static Money operator +(Money a, Money b) { return new Money(checked(a.Cents + b.Cents)); }
        public static Money operator -(Money a, Money b) { return new Money(checked(a.Cents - b.Cents)); }
        public static Money operator -(Money a) { return new Money(-a.Cents); }
        public static bool operator <(Money a, Money b) { return a.Cents < b.Cents; }
        public static bool operator >(Money a, Money b) { return a.Cents > b.Cents; }
        public static bool operator <=(Money a, Money b) { return a.Cents <= b.Cents; }
        public static bool operator >=(Money a, Money b) { return a.Cents >= b.Cents; }
        public static bool operator ==(Money a, Money b) { return a.Cents == b.Cents; }
        public static bool operator !=(Money a, Money b) { return a.Cents != b.Cents; }

        public int CompareTo(Money other) { return Cents.CompareTo(other.Cents); }
        public bool Equals(Money other) { return Cents == other.Cents; }
        public override bool Equals(object obj) { return obj is Money m && m.Cents == Cents; }
        public override int GetHashCode() { return Cents.GetHashCode(); }

        public static Money Max(Money a, Money b) { return a >= b ? a : b; }
        public static Money Min(Money a, Money b) { return a <= b ? a : b; }

        /// <summary>"$1,234.56" (negative: "-$1,234.56").</summary>
        public override string ToString()
        {
            long abs = Math.Abs(Cents);
            string body = "$" + (abs / 100).ToString("N0", CultureInfo.InvariantCulture) + "." + (abs % 100).ToString("00", CultureInfo.InvariantCulture);
            return Cents < 0 ? "-" + body : body;
        }

        /// <summary>Signed format for profit/loss: always shows + or - so colour is never the only cue.</summary>
        public string ToSignedString()
        {
            if (Cents == 0) return "$0.00";
            long abs = Math.Abs(Cents);
            string body = "$" + (abs / 100).ToString("N0", CultureInfo.InvariantCulture) + "." + (abs % 100).ToString("00", CultureInfo.InvariantCulture);
            return Cents > 0 ? "+" + body : "−" + body;
        }
    }

    public static class MoneyMath
    {
        /// <summary>Round half to even. Exact ties (x.5) go to the nearest even integer.</summary>
        public static long RoundHalfEven(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException("x", "Cannot round non-finite value: " + x);
            double floor = Math.Floor(x);
            double diff = x - floor;
            if (diff < 0.5) return (long)floor;
            if (diff > 0.5) return (long)floor + 1;
            return (long)(floor % 2 == 0 ? floor : floor + 1);
        }
    }
}
