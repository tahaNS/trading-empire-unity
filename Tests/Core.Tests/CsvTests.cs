using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class CsvTests
{
    static readonly ParseOptions Opts = new ParseOptions { Digits = 5 };

    [Fact]
    public void Simple_csv_parses_a_valid_file()
    {
        var r = CsvParser.Parse("timestamp,open,high,low,close,volume,spread\n2024-01-02T00:00:00Z,1.10420,1.10480,1.10390,1.10450,512,8\n2024-01-02T00:15:00Z,1.10450,1.10500,1.10440,1.10490,300,9\n", Opts);
        Assert.True(r.Ok);
        Assert.Equal(CsvFormat.Simple, r.Format);
        Assert.Equal(2, r.Candles.Count);
        Assert.Equal(Utc(2024, 1, 2), r.Candles[0].Time);
        Assert.Equal(110420, r.Candles[0].Open);
        Assert.Equal(512, r.Candles[0].Volume);
        Assert.Equal(8, r.Candles[0].SpreadPts);
    }

    [Fact]
    public void Zone_less_timestamps_are_utc_and_offsets_are_honoured()
    {
        var r = CsvParser.Parse("timestamp,open,high,low,close\n2024-01-02 00:00,1.1,1.1,1.1,1.1\n2024-01-02T02:00:00+02:00,1.1,1.1,1.1,1.1\n", Opts);
        Assert.Single(r.Candles); // both are 00:00 UTC => duplicate
        Assert.Equal(1, r.DuplicatesDropped);
        Assert.Equal(Utc(2024, 1, 2), r.Candles[0].Time);
    }

    [Fact]
    public void Rejects_bad_rows_with_line_numbers_and_keeps_good_ones()
    {
        var r = CsvParser.Parse(string.Join("\n",
            "timestamp,open,high,low,close",
            "2024-01-02T00:00:00Z,1.10000,1.10100,1.09900,1.10050", // ok
            "2024-01-02T00:15:00Z,1.10000,1.09900,1.09800,1.09950", // high < open
            "2024-01-02T00:30:00Z,1.10000,1.10100,1.10010,1.10050", // low > open
            "2024-01-02T00:45:00Z,abc,1.10100,1.09900,1.10050", // bad price
            "not-a-date,1.1,1.1,1.1,1.1",
            "2024-01-02T01:15:00Z,1.1,1.1", // missing fields
            "2024-01-02T01:30:00Z,0,0,0,0"), Opts); // non-positive
        Assert.True(r.Ok);
        Assert.Single(r.Candles);
        Assert.Equal(6, r.RowsRejected);
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8 }, r.Errors.Select(e => e.Line).ToArray());
    }

    [Fact]
    public void Sorts_unsorted_data_and_drops_duplicates()
    {
        var r = CsvParser.Parse("timestamp,open,high,low,close\n2024-01-02T00:15:00Z,1.2,1.2,1.2,1.2\n2024-01-02T00:00:00Z,1.1,1.1,1.1,1.1\n2024-01-02T00:15:00Z,1.3,1.3,1.3,1.3\n", Opts);
        Assert.True(r.WasUnsorted);
        Assert.Equal(new[] { P("1.1"), P("1.2") }, r.Candles.Select(c => c.Open).ToArray());
        Assert.Equal(1, r.DuplicatesDropped);
    }

    [Fact]
    public void Fails_cleanly_on_missing_columns_empty_and_garbage_input()
    {
        Assert.False(CsvParser.Parse("", Opts).Ok);
        Assert.Contains("Missing required column", CsvParser.Parse("foo,bar\n1,2\n", Opts).Fatal);
        var none = CsvParser.Parse("timestamp,open,high,low,close\nx,y,z,w,v\n", Opts);
        Assert.False(none.Ok);
        Assert.Contains("No valid candles", none.Fatal);
        CsvParser.Parse("\u0000\u0001\u0002", Opts); // must not throw
    }

    const string Mt5Header = "<DATE>\t<TIME>\t<OPEN>\t<HIGH>\t<LOW>\t<CLOSE>\t<TICKVOL>\t<VOL>\t<SPREAD>";

    [Fact]
    public void Mt5_tab_separated_format()
    {
        var r = CsvParser.Parse(Mt5Header + "\n2024.01.02\t00:00:00\t1.10420\t1.10480\t1.10390\t1.10450\t512\t0\t8\n2024.01.02\t00:15:00\t1.10450\t1.10500\t1.10440\t1.10490\t300\t0\t9\n", Opts);
        Assert.Equal(CsvFormat.Mt5, r.Format);
        Assert.Equal(2, r.Candles.Count);
        Assert.Equal(Utc(2024, 1, 2, 0, 0), r.Candles[0].Time);
        Assert.Equal(512, r.Candles[0].Volume);
        Assert.Equal(8, r.Candles[0].SpreadPts);
        Assert.Equal(Utc(2024, 1, 2, 0, 15), r.Candles[1].Time);
    }

    [Fact]
    public void Mt5_server_time_is_converted_with_the_offset()
    {
        var r = CsvParser.Parse(Mt5Header + "\n2024.01.02\t02:00:00\t1.1\t1.1\t1.1\t1.1\t1\t0\t8\n", new ParseOptions { Digits = 5, Mt5ServerUtcOffsetHours = 2 });
        Assert.Equal(Utc(2024, 1, 2, 0, 0), r.Candles[0].Time);
    }

    [Fact]
    public void Mt5_daily_export_without_time_column()
    {
        var r = CsvParser.Parse("<DATE>\t<OPEN>\t<HIGH>\t<LOW>\t<CLOSE>\t<TICKVOL>\t<VOL>\t<SPREAD>\n2024.01.02\t1.1\t1.2\t1.0\t1.1\t5\t0\t8\n", Opts);
        Assert.Equal(Utc(2024, 1, 2), r.Candles[0].Time);
    }

    [Fact]
    public void Mt5_rejects_malformed_rows()
    {
        var r = CsvParser.Parse(Mt5Header + "\n2024-01-02\t00:00\t1.1\t1.1\t1.1\t1.1\t1\t0\t8\n2024.01.02\t00:00:00\t1.1\t1.1\t1.1\t1.1\t1\t0\t8\n", Opts);
        Assert.Equal(1, r.RowsRejected);
        Assert.Single(r.Candles);
    }

    const string Mt4File = "2024.01.02,00:00,1.10420,1.10480,1.10390,1.10450,512\r\n2024.01.02,00:15,1.10450,1.10500,1.10440,1.10490,300\r\n";

    [Fact]
    public void Mt4_has_no_header_row()
    {
        var r = CsvParser.Parse(Mt4File, Opts);
        Assert.True(r.Ok);
        Assert.Equal(CsvFormat.Mt4, r.Format);
        Assert.Equal(2, r.RowsRead);
        Assert.Equal(2, r.Candles.Count);
        var c = r.Candles[0];
        Assert.Equal(Utc(2024, 1, 2, 0, 0), c.Time);
        Assert.Equal(new long[] { 110420, 110480, 110390, 110450 }, new[] { c.Open, c.High, c.Low, c.Close });
        Assert.Equal(512, c.Volume);
    }

    [Fact]
    public void Mt4_subtracts_the_broker_offset_and_reports_bad_rows()
    {
        var r = CsvParser.Parse(Mt4File, new ParseOptions { Digits = 5, Mt5ServerUtcOffsetHours = 2 });
        Assert.Equal(Utc(2024, 1, 1, 22, 0), r.Candles[0].Time);
        var bad = CsvParser.Parse(Mt4File + "2024.01.02,00:30,1.1,1.0,1.2,1.1,5\n", Opts);
        Assert.Equal(1, bad.RowsRejected);
        Assert.Equal(3, bad.Errors[0].Line);
        Assert.Equal(CsvFormat.Simple, CsvParser.Parse("timestamp,open,high,low,close\n2024-01-02T00:00:00Z,1,1,1,1\n", Opts).Format);
    }

    [Fact]
    public void Reads_a_jpy_file_with_three_decimals()
    {
        var r = CsvParser.Parse("timestamp,open,high,low,close\n2024-01-02T00:00:00Z,150.123,150.200,150.050,150.180\n", new ParseOptions { Digits = 3 });
        Assert.Equal(150123, r.Candles[0].Open);
    }
}
