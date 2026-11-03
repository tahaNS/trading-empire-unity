using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TradingEmpire.Core
{
    public enum CsvFormat { Simple, Mt4, Mt5, Unknown }

    public sealed class ParseOptions
    {
        public int Digits;
        public double Mt5ServerUtcOffsetHours;
        public int MaxReportedErrors = 50;
    }

    public sealed class RowError
    {
        public int Line;
        public string Message;
    }

    public sealed class ParseResult
    {
        public bool Ok;
        public CsvFormat Format = CsvFormat.Unknown;
        public List<Candle> Candles = new List<Candle>();
        public int RowsRead, RowsRejected, DuplicatesDropped;
        public bool WasUnsorted;
        /// <summary>First errors only (capped); RowsRejected has the full count.</summary>
        public List<RowError> Errors = new List<RowError>();
        public string Fatal;
    }

    /// <summary>
    /// Candle CSV loader (pure, no Unity types). Supported formats:
    /// 1. Simple CSV: timestamp,open,high,low,close[,volume][,spread] (ISO 8601, UTC by default).
    /// 2. MetaTrader 4 history export (no header row): 2024.01.02,00:00,open,high,low,close,volume.
    /// 3. MetaTrader 5 history export (tab separated, header with &lt;DATE&gt; &lt;TIME&gt; ...). MT5 and MT4 times are
    ///    broker SERVER time, converted to UTC by subtracting the server offset (default 0).
    /// </summary>
    public static class CsvParser
    {
        static readonly Regex IsoRe = new Regex(@"^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?)?(Z|[+-]\d{2}:?\d{2})?$");

        static long ParseIso(string text)
        {
            string t = text.Trim();
            var m = IsoRe.Match(t);
            if (!m.Success) throw new FormatException("Invalid timestamp \"" + text + "\"");
            string s = t.Replace(' ', 'T');
            if (!s.Contains("T")) s += "T00:00:00";
            string zone = m.Groups[1].Success ? m.Groups[1].Value : "";
            if (zone == "") s += "Z"; // no zone given: documented default is UTC
            else if (zone != "Z" && !zone.Contains(":")) s = s.Substring(0, s.Length - 2) + ":" + zone.Substring(zone.Length - 2);
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                throw new FormatException("Invalid timestamp \"" + text + "\"");
            return dto.ToUnixTimeMilliseconds();
        }

        static long ParseMt5(string date, string time, double offsetHours)
        {
            var d = Regex.Match(date.Trim(), @"^(\d{4})\.(\d{2})\.(\d{2})$");
            if (!d.Success) throw new FormatException("Invalid date \"" + date + "\"");
            int h = 0, mi = 0, sec = 0;
            if (time != null)
            {
                var t = Regex.Match(time.Trim(), @"^(\d{2}):(\d{2})(?::(\d{2}))?$");
                if (!t.Success) throw new FormatException("Invalid time \"" + time + "\"");
                h = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                mi = int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture);
                if (t.Groups[3].Success) sec = int.Parse(t.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            DateTime utc;
            try
            {
                utc = new DateTime(int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture), h, mi, sec, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FormatException("Invalid date/time \"" + date + " " + time + "\"");
            }
            return new DateTimeOffset(utc).ToUnixTimeMilliseconds() - (long)Math.Round(offsetHours * 3600000.0);
        }

        static string NormalizeName(string raw)
        {
            return raw.Replace("<", "").Replace(">", "").Replace("\"", "").Trim().ToLowerInvariant();
        }

        public static ParseResult Parse(string input, ParseOptions options)
        {
            var result = new ParseResult();
            ParseResult Fail(string fatal) { result.Fatal = fatal; return result; }

            string text = input.Length > 0 && input[0] == '﻿' ? input.Substring(1) : input;
            string[] lines = Regex.Split(text, "\r?\n");
            int headerIdx = Array.FindIndex(lines, l => l.Trim() != "");
            if (headerIdx < 0) return Fail("The file is empty.");

            string headerLine = lines[headerIdx];
            char delim = headerLine.Contains("\t") ? '\t' : headerLine.Contains(";") ? ';' : ',';
            string[] firstFields = headerLine.Split(delim);
            // MetaTrader 4 files have no header row: the first line is already a candle that starts with a
            // dotted date, then the time, open, high, low, close and volume.
            bool isMt4 = Regex.IsMatch(firstFields[0].Trim(), @"^\d{4}\.\d{2}\.\d{2}$") && firstFields.Length >= 6;
            string[] names = isMt4
                ? new[] { "date", "time", "open", "high", "low", "close", "volume" }
                : firstFields.Select(NormalizeName).ToArray();
            bool isMt5 = (headerLine.Contains("<") && names.Contains("date")) || isMt4;
            result.Format = isMt4 ? CsvFormat.Mt4 : isMt5 ? CsvFormat.Mt5 : CsvFormat.Simple;

            int Col(params string[] aliases) { return Array.FindIndex(names, n => aliases.Contains(n)); }
            int cDate = isMt5 ? Col("date") : Col("timestamp", "time", "datetime", "date");
            int cTime = isMt5 ? Col("time") : -1;
            int cOpen = Col("open"), cHigh = Col("high"), cLow = Col("low"), cClose = Col("close");
            int cVol = Col("volume", "tickvol", "vol");
            int cSpread = Col("spread");
            var missing = new List<string>();
            if (cDate == -1) missing.Add("timestamp/date");
            if (cOpen == -1) missing.Add("open");
            if (cHigh == -1) missing.Add("high");
            if (cLow == -1) missing.Add("low");
            if (cClose == -1) missing.Add("close");
            if (missing.Count > 0)
                return Fail("Missing required column(s): " + string.Join(", ", missing) + ". Expected a header row (see docs/DATA_FORMATS.md).");

            double offset = options.Mt5ServerUtcOffsetHours;
            void Reject(int line, string message)
            {
                result.RowsRejected++;
                if (result.Errors.Count < options.MaxReportedErrors) result.Errors.Add(new RowError { Line = line, Message = message });
            }

            var candles = new List<Candle>();
            if (!isMt4) headerIdx++; // an MT4 file starts with data, not a header
            for (int i = headerIdx; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw.Trim() == "") continue;
                result.RowsRead++;
                string[] f = raw.Split(delim);
                try
                {
                    string Get(int idx, string name)
                    {
                        if (idx >= f.Length || f[idx].Trim() == "") throw new FormatException("Missing " + name);
                        return f[idx];
                    }
                    long time = isMt5
                        ? ParseMt5(Get(cDate, "date"), cTime >= 0 ? Get(cTime, "time") : null, offset)
                        : ParseIso(Get(cDate, "timestamp"));
                    long open = Units.ParsePrice(Get(cOpen, "open"), options.Digits);
                    long high = Units.ParsePrice(Get(cHigh, "high"), options.Digits);
                    long low = Units.ParsePrice(Get(cLow, "low"), options.Digits);
                    long close = Units.ParsePrice(Get(cClose, "close"), options.Digits);
                    if (open <= 0) throw new ArgumentOutOfRangeException("open", "Non-positive open price");
                    if (high <= 0) throw new ArgumentOutOfRangeException("high", "Non-positive high price");
                    if (low <= 0) throw new ArgumentOutOfRangeException("low", "Non-positive low price");
                    if (close <= 0) throw new ArgumentOutOfRangeException("close", "Non-positive close price");
                    if (high < Math.Max(open, close)) throw new ArgumentOutOfRangeException("high", "High is below open/close");
                    if (low > Math.Min(open, close)) throw new ArgumentOutOfRangeException("low", "Low is above open/close");
                    var candle = new Candle(time, open, high, low, close);
                    if (cVol >= 0 && cVol < f.Length && f[cVol].Trim() != ""
                        && double.TryParse(f[cVol], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0)
                        candle.Volume = v;
                    if (cSpread >= 0 && cSpread < f.Length && f[cSpread].Trim() != ""
                        && double.TryParse(f[cSpread], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s >= 0 && Math.Floor(s) == s)
                        candle.SpreadPts = (long)s;
                    candles.Add(candle);
                }
                catch (Exception e) when (e is FormatException || e is ArgumentOutOfRangeException || e is OverflowException)
                {
                    string message = e is ArgumentOutOfRangeException ae ? ae.Message.Split('\n')[0].Replace(" (Parameter '" + ae.ParamName + "')", "") : e.Message;
                    Reject(i + 1, message);
                }
            }

            // Sort chronologically (stable) and drop duplicate timestamps, keeping the first occurrence.
            for (int i = 1; i < candles.Count; i++)
            {
                if (candles[i].Time < candles[i - 1].Time) { result.WasUnsorted = true; break; }
            }
            if (result.WasUnsorted) candles = candles.OrderBy(c => c.Time).ToList(); // LINQ OrderBy is stable
            foreach (var c in candles)
            {
                var last = result.Candles.Count > 0 ? result.Candles[result.Candles.Count - 1] : null;
                if (last != null && last.Time == c.Time) result.DuplicatesDropped++;
                else result.Candles.Add(c);
            }

            if (result.Candles.Count == 0) return Fail("No valid candles found in the file.");
            result.Ok = true;
            return result;
        }
    }
}
