using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The backtest trade-log line (SIM-001). In a backtest the bot journals, for every closed bot
/// trade, one line <c>&lt;UTC time&gt; tradelog seq=&lt;n&gt; row=&lt;row&gt; problem=&lt;text&gt;</c>:
/// <c>seq</c> counts the lines of the run from 1, <c>row</c> is the trade as one row of the trade
/// log format of <see cref="TradeLogCsv"/> (no spaces), and <c>problem</c> is <c>none</c>. When a
/// value is missing or invalid, <c>row</c> is <c>NA</c> and <c>problem</c> says why, so the
/// <c>sim extract</c> command can name the trade instead of dropping it silently.
/// </summary>
/// <remarks>
/// Journal data only: building the line reads the closed trade and the plan kept at the open and
/// changes nothing, and it never throws for bad data.
/// </remarks>
public static class TradeLogLine
{
    /// <summary>Journal category of the line (SIM-001).</summary>
    public const string Category = "tradelog";

    /// <summary>Value of <c>problem</c> when the row is valid (SIM-001).</summary>
    public const string NoProblem = "none";

    /// <summary>
    /// Builds the trade-log record of a closed bot trade (SIM-001). Symbol, open and close time,
    /// money, commission and swap come from the broker's closed trade; the zone id from its label;
    /// volume, exit price and exit reason from the adapter's closing data; side and entry (fill)
    /// price from the position at its open; stop, target and risk money from the order comment
    /// (EXE-008). Times are cut to whole seconds. Result in R is the net result divided by the
    /// risk money of the comment, as in OPS-002.
    /// </summary>
    /// <param name="trade">The closed trade, partial closes summed.</param>
    /// <param name="side">Direction of the position, or null when not known.</param>
    /// <param name="entry">Fill price of the position, or null when not known.</param>
    /// <param name="comment">Order comment of the position, or null when not known.</param>
    /// <returns>The record, or the reason why none can be built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trade"/> is null.</exception>
    public static TradeLogRow Build(ClosedTradeInfo trade, Side? side, double? entry, string? comment)
    {
        ArgumentNullException.ThrowIfNull(trade);
        CommentPlan? plan = OrderComment.TryParse(comment);
        string? problem = Missing(trade, side, entry, plan, out string zoneId);
        if (problem != null)
        {
            return new TradeLogRow(null, problem);
        }

        DateTime open = WholeSeconds(trade.EntryTimeUtc);
        DateTime close = WholeSeconds(trade.CloseTimeUtc);
        double riskMoney = plan!.RiskMoney;
        double resultR = trade.NetProfit / riskMoney;
        string exitReason = trade.ExitReason.ToString();
        problem = TradeRecord.Problem(trade.Symbol, zoneId, open, close, side!.Value, trade.VolumeUnits!.Value, entry!.Value, plan.Stop, plan.Target, trade.ClosingPrice!.Value, exitReason, trade.NetProfit, resultR, trade.Commission, trade.Swap, riskMoney);
        if (problem != null)
        {
            return new TradeLogRow(null, problem);
        }

        var record = new TradeRecord(trade.Symbol, zoneId, open, close, side.Value, trade.VolumeUnits.Value, entry.Value, plan.Stop, plan.Target, trade.ClosingPrice.Value, exitReason, trade.NetProfit, resultR, trade.Commission, trade.Swap, riskMoney);
        return new TradeLogRow(record, null);
    }

    /// <summary>The fields of the journal line, in order: seq, row, problem (SIM-001).</summary>
    /// <param name="sequence">Number of the line in the run, from 1.</param>
    /// <param name="row">The record or the problem.</param>
    /// <returns>The fields.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public static IReadOnlyList<(string Key, string? Value)> Fields(int sequence, TradeLogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new (string, string?)[]
        {
            ("seq", sequence.ToString(CultureInfo.InvariantCulture)),
            ("row", row.Record != null ? TradeLogCsv.Row(row.Record) : JournalFormat.NotAvailable),
            ("problem", row.Problem ?? NoProblem),
        };
    }

    private static string? Missing(ClosedTradeInfo trade, Side? side, double? entry, CommentPlan? plan, out string zoneId)
    {
        if (!OrderLabel.TryParse(trade.Label, out string _, out zoneId))
        {
            return "the label \"" + trade.Label + "\" is not a bot label.";
        }

        if (!side.HasValue || !entry.HasValue)
        {
            return "the side and fill price of the position are not known (no plan was kept at its open).";
        }

        if (plan == null || !(plan.RiskMoney > 0.0))
        {
            return "the order comment is missing or has no risk money greater than zero.";
        }

        if (!trade.VolumeUnits.HasValue || !trade.ClosingPrice.HasValue)
        {
            return "the broker did not report the closed volume or the closing price.";
        }

        return null;
    }

    private static DateTime WholeSeconds(DateTime value)
    {
        return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);
    }
}

/// <summary>A trade-log record of a closed trade, or why none could be built (SIM-001).</summary>
/// <param name="Record">The record, or null.</param>
/// <param name="Problem">Why there is no record, or null when there is one.</param>
public sealed record TradeLogRow(TradeRecord? Record, string? Problem);
