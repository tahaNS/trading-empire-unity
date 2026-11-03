using System;
using TradingEmpire.Core;
using Xunit;

public class MoneyTests
{
    [Fact]
    public void RoundHalfEven_rounds_ties_to_even()
    {
        Assert.Equal(0, MoneyMath.RoundHalfEven(0.5));
        Assert.Equal(2, MoneyMath.RoundHalfEven(1.5));
        Assert.Equal(2, MoneyMath.RoundHalfEven(2.5));
        Assert.Equal(0, MoneyMath.RoundHalfEven(-0.5));
        Assert.Equal(-2, MoneyMath.RoundHalfEven(-1.5));
        Assert.Equal(2, MoneyMath.RoundHalfEven(2.4));
        Assert.Equal(3, MoneyMath.RoundHalfEven(2.6));
    }

    [Fact]
    public void RoundHalfEven_rejects_non_finite_input()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyMath.RoundHalfEven(double.NaN));
    }

    [Fact]
    public void Adds_without_float_drift_and_subtracts()
    {
        Assert.Equal(30, (Money.ParseDollars("0.10") + Money.ParseDollars("0.20")).Cents);
        Assert.Equal(-200, (Money.FromCents(500) - Money.FromCents(700)).Cents);
        Assert.Equal(0, (-Money.Zero).Cents);
    }

    [Fact]
    public void Parses_dollar_strings_exactly()
    {
        Assert.Equal(123456, Money.ParseDollars("1234.56").Cents);
        Assert.Equal(-50, Money.ParseDollars("-0.5").Cents);
        Assert.Equal(700, Money.ParseDollars("7").Cents);
        Assert.Throws<FormatException>(() => Money.ParseDollars("1.234"));
        Assert.Throws<FormatException>(() => Money.ParseDollars("abc"));
        Assert.Equal(1999, Money.FromDollars(19.99).Cents);
    }

    [Fact]
    public void Scales_by_a_factor_rounding_once()
    {
        Assert.Equal(502, Money.FromCents(1005).Scale(0.5).Cents); // 502.5 -> even
        Assert.Equal(508, Money.FromCents(1015).Scale(0.5).Cents); // 507.5 -> even
    }

    [Fact]
    public void Formats_currency_and_signed_values()
    {
        Assert.Equal("$1,234.56", Money.FromCents(123456).ToString());
        Assert.Equal("-$2.50", Money.FromCents(-250).ToString());
        Assert.Equal("+$2.50", Money.FromCents(250).ToSignedString());
        Assert.Equal("−$2.50", Money.FromCents(-250).ToSignedString());
        Assert.Equal("$0.00", Money.Zero.ToSignedString());
    }

    [Fact]
    public void Ids_continue_after_restored_state()
    {
        var ids = new IdSequence("p");
        Assert.Equal("p1", ids.Next());
        Assert.Equal("p2", ids.Next());
        ids.ResetAfter(new[] { "p7", "p3", "x99", "p" });
        Assert.Equal("p8", ids.Next());
    }
}

public class UnitsTests
{
    [Fact]
    public void Parses_and_formats_prices_exactly()
    {
        Assert.Equal(110432, Units.ParsePrice("1.10432", 5));
        Assert.Equal(110000, Units.ParsePrice("1.1", 5));
        Assert.Equal(150500, Units.ParsePrice("150.5", 3));
        Assert.Equal("1.10432", Units.FormatPrice(110432, 5));
        Assert.Equal("0.00005", Units.FormatPrice(5, 5));
        Assert.Throws<FormatException>(() => Units.ParsePrice("abc", 5));
        Assert.Throws<FormatException>(() => Units.ParsePrice("-1.1", 5));
    }

    [Fact]
    public void Rounds_extra_price_digits_half_even()
    {
        Assert.Equal(110432, Units.ParsePrice("1.104325", 5)); // tie, 110432 even -> down
        Assert.Equal(110434, Units.ParsePrice("1.104335", 5)); // tie, 110433 odd -> up
        Assert.Equal(110433, Units.ParsePrice("1.1043251", 5));
    }

    [Fact]
    public void Parses_lots()
    {
        Assert.Equal(25, Units.ParseLots("0.25"));
        Assert.Equal(100, Units.ParseLots("1"));
        Assert.Null(Units.ParseLots("0.001"));
        Assert.Null(Units.ParseLots("-1"));
        Assert.Equal("1.50", Units.FormatLots(150));
    }
}
