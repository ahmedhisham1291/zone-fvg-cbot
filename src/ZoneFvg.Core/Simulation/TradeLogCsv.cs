using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Reader and writer of the trade log format (SIM-001): comma-separated, invariant culture, one
/// header row followed by one row per closed trade. Fields are never quoted; a field cannot
/// contain a comma. Times are UTC in the form <c>yyyy-MM-ddTHH:mm:ssZ</c>; numbers use a dot as
/// decimal separator, an optional sign and an optional exponent, without spaces or thousands
/// separators.
/// </summary>
/// <remarks>
/// Row numbers count from 1 at the header, so the first trade is row 2. Lines end with a line
/// feed; a carriage return before it, a byte-order mark at the start and a missing final line
/// feed are accepted when reading. Reading stops at the first invalid row.
/// </remarks>
public static class TradeLogCsv
{
    /// <summary>The exact header row (SIM-001).</summary>
    public const string Header = "symbol,zone_id,open_time_utc,close_time_utc,side,volume_units,entry,stop,target,exit_price,exit_reason,result_money,result_r,commission,swap,risk_money";

    /// <summary>Format of the time columns (SIM-001).</summary>
    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private const int ColumnCount = 16;

    private const NumberStyles NumberFormat = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// Reads a trade log (SIM-001). Rejects the whole log at the first invalid row, with the row
    /// number and the column in the error.
    /// </summary>
    /// <param name="text">The file content. Null is invalid.</param>
    /// <returns>The records in file order, or the error.</returns>
    public static TradeLogReadResult Read(string? text)
    {
        if (text == null)
        {
            return TradeLogReadResult.Invalid("The trade log text is null.");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text.Substring(1);
        }

        string[] lines = text.Split('\n');
        int count = lines.Length;
        if (lines[count - 1].Length == 0)
        {
            count--;
        }

        if (count == 0 || !string.Equals(WithoutCarriageReturn(lines[0]), Header, StringComparison.Ordinal))
        {
            return TradeLogReadResult.Invalid("Row 1: the header row must be exactly \"" + Header + "\".");
        }

        var records = new List<TradeRecord>(count - 1);
        for (int i = 1; i < count; i++)
        {
            string? error = ParseRow(WithoutCarriageReturn(lines[i]), out TradeRecord? record);
            if (error != null || record == null)
            {
                return TradeLogReadResult.Invalid("Row " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + error);
            }

            records.Add(record);
        }

        return TradeLogReadResult.Valid(records);
    }

    /// <summary>
    /// Writes a trade log (SIM-001): the header and one row per record, in the given order, with
    /// line feeds and round-trip number formatting, so <see cref="Read"/> returns equal records.
    /// </summary>
    /// <param name="records">The records.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    public static string Write(IEnumerable<TradeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (TradeRecord record in records)
        {
            text.Append(record.Symbol).Append(',')
                .Append(record.ZoneId).Append(',')
                .Append(Time(record.OpenTimeUtc)).Append(',')
                .Append(Time(record.CloseTimeUtc)).Append(',')
                .Append(record.Side == Side.Buy ? "Buy" : "Sell").Append(',')
                .Append(Number(record.VolumeUnits)).Append(',')
                .Append(Number(record.Entry)).Append(',')
                .Append(Number(record.Stop)).Append(',')
                .Append(Number(record.Target)).Append(',')
                .Append(Number(record.ExitPrice)).Append(',')
                .Append(record.ExitReason).Append(',')
                .Append(Number(record.ResultMoney)).Append(',')
                .Append(Number(record.ResultR)).Append(',')
                .Append(Number(record.Commission)).Append(',')
                .Append(Number(record.Swap)).Append(',')
                .Append(Number(record.RiskMoney)).Append('\n');
        }

        return text.ToString();
    }

    private static string? ParseRow(string line, out TradeRecord? record)
    {
        record = null;
        if (line.Length == 0)
        {
            return "the row is empty.";
        }

        string[] f = line.Split(',');
        if (f.Length != ColumnCount)
        {
            return "expected " + ColumnCount.ToString(CultureInfo.InvariantCulture) + " columns but found " + f.Length.ToString(CultureInfo.InvariantCulture) + ".";
        }

        string? error = null;
        DateTime open = ParseTime(f, 2, ref error);
        DateTime close = ParseTime(f, 3, ref error);
        Side side = ParseSide(f, 4, ref error);
        double volume = ParseNumber(f, 5, ref error);
        double entry = ParseNumber(f, 6, ref error);
        double stop = ParseNumber(f, 7, ref error);
        double target = ParseNumber(f, 8, ref error);
        double exit = ParseNumber(f, 9, ref error);
        double money = ParseNumber(f, 11, ref error);
        double r = ParseNumber(f, 12, ref error);
        double commission = ParseNumber(f, 13, ref error);
        double swap = ParseNumber(f, 14, ref error);
        double risk = ParseNumber(f, 15, ref error);

        // Symbol and zone id are checked first (with valid placeholders for the other values),
        // then whether each typed column could be read, then every value of the record.
        string? problem = TradeRecord.Problem(f[0], f[1], DateTime.UnixEpoch, DateTime.UnixEpoch, Side.Buy, 1.0, 2.0, 1.0, 3.0, 1.0, "x", 1.0, 1.0, 0.0, 0.0, 1.0);
        problem ??= error;
        problem ??= TradeRecord.Problem(f[0], f[1], open, close, side, volume, entry, stop, target, exit, f[10], money, r, commission, swap, risk);
        if (problem != null)
        {
            return problem;
        }

        record = new TradeRecord(f[0], f[1], open, close, side, volume, entry, stop, target, exit, f[10], money, r, commission, swap, risk);
        return null;
    }

    private static DateTime ParseTime(string[] fields, int index, ref string? error)
    {
        if (DateTime.TryParseExact(fields[index], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }

        error ??= ColumnName(index) + " \"" + fields[index] + "\" is not a UTC time in the form yyyy-MM-ddTHH:mm:ssZ.";
        return DateTime.UnixEpoch;
    }

    private static Side ParseSide(string[] fields, int index, ref string? error)
    {
        if (string.Equals(fields[index], "Buy", StringComparison.Ordinal))
        {
            return Side.Buy;
        }

        if (string.Equals(fields[index], "Sell", StringComparison.Ordinal))
        {
            return Side.Sell;
        }

        error ??= ColumnName(index) + " \"" + fields[index] + "\" must be Buy or Sell.";
        return Side.Buy;
    }

    private static double ParseNumber(string[] fields, int index, ref string? error)
    {
        if (double.TryParse(fields[index], NumberFormat, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            return value;
        }

        error ??= ColumnName(index) + " \"" + fields[index] + "\" is not a finite number.";
        return 0.0;
    }

    private static string ColumnName(int index)
    {
        return Header.Split(',')[index];
    }

    private static string WithoutCarriageReturn(string line)
    {
        return line.EndsWith('\r') ? line.Substring(0, line.Length - 1) : line;
    }

    private static string Time(DateTime value)
    {
        return value.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    private static string Number(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>Result of <see cref="TradeLogCsv.Read"/> (SIM-001).</summary>
public sealed class TradeLogReadResult
{
    private TradeLogReadResult(bool isValid, string error, IReadOnlyList<TradeRecord> records)
    {
        IsValid = isValid;
        Error = error;
        Records = records;
    }

    /// <summary>True when every row is valid (SIM-001).</summary>
    public bool IsValid { get; }

    /// <summary>The error for the first invalid row, starting with "Row N: "; empty when valid (SIM-001).</summary>
    public string Error { get; }

    /// <summary>The records in file order; empty when invalid (SIM-001).</summary>
    public IReadOnlyList<TradeRecord> Records { get; }

    internal static TradeLogReadResult Valid(IReadOnlyList<TradeRecord> records)
    {
        return new TradeLogReadResult(true, string.Empty, records);
    }

    internal static TradeLogReadResult Invalid(string error)
    {
        return new TradeLogReadResult(false, error, Array.Empty<TradeRecord>());
    }
}
