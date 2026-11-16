using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Turns a saved cTrader backtest log into a trade log (SIM-001). The bot journals one line
/// <c>... tradelog seq=&lt;n&gt; row=&lt;row&gt; problem=&lt;text&gt;</c> per closed trade in a
/// backtest; this class finds those lines anywhere in the text (whatever the platform put before
/// them on the same line), checks that the trades are numbered 1, 2, 3 ... without a gap, and
/// writes the rows in that order under the trade-log header. Every other line is ignored.
/// </summary>
/// <remarks>
/// A line pasted twice is accepted once. The same number with a different row means the file
/// holds more than one run, and is rejected. A missing number means the log is incomplete, and
/// is rejected. A line whose <c>problem</c> is not <c>none</c> is rejected with the bot's reason.
/// The result is accepted by <see cref="TradeLogCsv.Read"/>.
/// </remarks>
public static class BacktestLogExtract
{
    /// <summary>The text that starts a trade line (SIM-001).</summary>
    public const string Marker = "tradelog seq=";

    /// <summary>Extracts the trade log from a saved backtest log (SIM-001).</summary>
    /// <param name="logText">The whole saved log. Null is invalid.</param>
    /// <returns>The trade log text and the number of trades, or the error.</returns>
    public static BacktestLogResult Extract(string? logText)
    {
        if (logText == null)
        {
            return BacktestLogResult.Invalid("The log text is null.");
        }

        var rows = new SortedDictionary<int, string>();
        string[] lines = logText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string? error = ReadLine(lines[i], i + 1, rows);
            if (error != null)
            {
                return BacktestLogResult.Invalid(error);
            }
        }

        if (rows.Count == 0)
        {
            return BacktestLogResult.Invalid("No trade lines (\"" + Marker + "\") were found. Copy the Log tab of a finished backtest of this cBot; a backtest without any trade has nothing to simulate.");
        }

        return Assemble(rows);
    }

    private static string? ReadLine(string line, int lineNumber, SortedDictionary<int, string> rows)
    {
        int start = FindMarker(line);
        if (start < 0)
        {
            return null;
        }

        string where = "Line " + Text(lineNumber) + ": ";
        string[] tokens = line.Substring(start).Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4 || !tokens[1].StartsWith("seq=", StringComparison.Ordinal) || !tokens[2].StartsWith("row=", StringComparison.Ordinal) || !tokens[3].StartsWith("problem=", StringComparison.Ordinal))
        {
            return where + "a trade line must read \"tradelog seq=<n> row=<row> problem=<text>\".";
        }

        string seqText = tokens[1].Substring(4);
        if (!int.TryParse(seqText, NumberStyles.None, CultureInfo.InvariantCulture, out int seq) || seq < 1)
        {
            return where + "the trade number \"" + seqText + "\" is not a whole number from 1.";
        }

        string problem = tokens[3].Substring(8);
        if (!string.Equals(problem, "none", StringComparison.Ordinal))
        {
            return where + "the bot could not write trade " + Text(seq) + ": " + problem.Replace("\\s", " ", StringComparison.Ordinal);
        }

        string row = tokens[2].Substring(4);
        if (rows.TryGetValue(seq, out string? known))
        {
            return string.Equals(known, row, StringComparison.Ordinal)
                ? null
                : where + "trade " + Text(seq) + " appears twice with different content, so the file holds more than one backtest run. Save the log of one run only.";
        }

        rows.Add(seq, row);
        return null;
    }

    private static int FindMarker(string line)
    {
        int index = line.IndexOf(Marker, StringComparison.Ordinal);
        while (index > 0 && line[index - 1] != ' ' && line[index - 1] != '\t')
        {
            // Only a whole word counts, so "xtradelog seq=" is not a trade line.
            index = line.IndexOf(Marker, index + 1, StringComparison.Ordinal);
        }

        return index;
    }

    private static BacktestLogResult Assemble(SortedDictionary<int, string> rows)
    {
        var text = new StringBuilder();
        text.Append(TradeLogCsv.Header).Append('\n');
        int expected = 1;
        foreach (KeyValuePair<int, string> pair in rows)
        {
            if (pair.Key != expected)
            {
                return BacktestLogResult.Invalid("Trade " + Text(expected) + " is missing (the next one found is " + Text(pair.Key) + "), so the log is incomplete. Save the whole Log tab, or run a shorter period.");
            }

            TradeLogReadResult check = TradeLogCsv.Read(TradeLogCsv.Header + "\n" + pair.Value + "\n");
            if (!check.IsValid)
            {
                return BacktestLogResult.Invalid("Trade " + Text(pair.Key) + ": " + check.Error.Substring(check.Error.IndexOf(':') + 2));
            }

            text.Append(pair.Value).Append('\n');
            expected++;
        }

        return BacktestLogResult.Valid(text.ToString(), rows.Count);
    }
}

/// <summary>Result of <see cref="BacktestLogExtract.Extract"/> (SIM-001).</summary>
public sealed class BacktestLogResult
{
    private BacktestLogResult(bool isValid, string error, string tradeLog, int trades)
    {
        IsValid = isValid;
        Error = error;
        TradeLog = tradeLog;
        Trades = trades;
    }

    /// <summary>True when a trade log was produced (SIM-001).</summary>
    public bool IsValid { get; }

    /// <summary>Why no trade log was produced; empty when valid (SIM-001).</summary>
    public string Error { get; }

    /// <summary>The trade log in the format of <see cref="TradeLogCsv"/>; empty when invalid (SIM-001).</summary>
    public string TradeLog { get; }

    /// <summary>Number of trades in the trade log; 0 when invalid (SIM-001).</summary>
    public int Trades { get; }

    internal static BacktestLogResult Valid(string tradeLog, int trades)
    {
        return new BacktestLogResult(true, string.Empty, tradeLog, trades);
    }

    internal static BacktestLogResult Invalid(string error)
    {
        return new BacktestLogResult(false, error, string.Empty, 0);
    }
}
