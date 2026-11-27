using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The values of one consistency readout (OPS-012, spec v0.20 row BK). Information only: no
/// trading decision reads them.
/// </summary>
/// <param name="SinceDate">The start date of the count (RewardPeriodStartDate, else RiskEpochDate); the count starts at 00:00 CE(S)T on it.</param>
/// <param name="Total">The sum of the net profit of every trade closed since the start.</param>
/// <param name="BestDay">The CE(S)T date with the largest summed net profit (the earliest on a tie), or null when no trade closed.</param>
/// <param name="BestDayProfit">The net profit of <paramref name="BestDay"/>, or null when no trade closed.</param>
/// <param name="RatioPercent">BestDayProfit / Total x 100, or null when Total is not above 0.</param>
/// <param name="LimitPercent">ConsistencyMaxPercent.</param>
/// <param name="Met">True only when Total is above 0 and BestDayProfit is at most Total x LimitPercent / 100 (PriceMath tolerance).</param>
public sealed record ConsistencyResult(DateTime SinceDate, double Total, DateTime? BestDay, double? BestDayProfit, double? RatioPercent, double LimitPercent, bool Met);

/// <summary>
/// The consistency readout of OPS-012 as a pure function of the closed trades, the start date and
/// the limit, and the fields of its journal line. Days are the CE(S)T dates of the close times;
/// profit is the net profit as the broker reports it (commission and swap included).
/// </summary>
public static class ConsistencyReadout
{
    /// <summary>The journal category of the line (OPS-012).</summary>
    public const string Category = "consistency";

    /// <summary>
    /// Computes the readout (OPS-012) from every trade closed at or after 00:00 CE(S)T of
    /// <paramref name="sinceDate"/>; earlier trades in the list are ignored.
    /// </summary>
    /// <param name="trades">The account's closed trades, bot or not, in any order.</param>
    /// <param name="sinceDate">The start date, a date of kind UTC (RewardPeriodStartDate or RiskEpochDate).</param>
    /// <param name="limitPercent">ConsistencyMaxPercent.</param>
    /// <returns>The readout values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry is null, has a close time that is not UTC or a net profit that is not finite.</exception>
    public static ConsistencyResult Compute(IEnumerable<ClosedTradeInfo> trades, DateTime sinceDate, double limitPercent)
    {
        ArgumentNullException.ThrowIfNull(trades);
        DateTime start = CestClock.StartOfCestDateUtc(sinceDate);

        // Keyed by CE(S)T date; a sorted map, so the result does not depend on the order of the list.
        var days = new SortedDictionary<DateTime, double>();
        double total = 0.0;
        foreach (ClosedTradeInfo trade in trades)
        {
            Check(trade);
            if (trade.CloseTimeUtc < start)
            {
                continue;
            }

            DateTime day = (trade.CloseTimeUtc + CestClock.UtcOffset(trade.CloseTimeUtc)).Date;
            days.TryGetValue(day, out double sum);
            days[day] = sum + trade.NetProfit;
            total += trade.NetProfit;
        }

        DateTime? bestDay = null;
        double? bestProfit = null;
        foreach (KeyValuePair<DateTime, double> day in days)
        {
            if (!bestProfit.HasValue || day.Value > bestProfit.Value)
            {
                bestDay = day.Key;
                bestProfit = day.Value;
            }
        }

        if (bestProfit is not double best || !(total > 0.0))
        {
            // No trade, or a total that is not above 0: no ratio, never met (OPS-012).
            return new ConsistencyResult(sinceDate.Date, total, bestDay, bestProfit, null, limitPercent, false);
        }

        bool met = !PriceMath.GreaterThan(best, total * limitPercent / 100.0, PriceMath.DefaultTolerance);
        return new ConsistencyResult(sinceDate.Date, total, bestDay, best, best / total * 100.0, limitPercent, met);
    }

    /// <summary>
    /// The fields of the journal line (OPS-012), in this order: <c>since=yyyy-MM-dd total=&lt;USD&gt;
    /// bestDay=yyyy-MM-dd bestDayProfit=&lt;USD&gt; ratio=&lt;percent or NA&gt; limit=&lt;percent&gt;
    /// met=&lt;yes|no&gt;</c>. Numbers in the invariant round-trip ("R") format: money and the ratio
    /// after rounding to 2 decimals (half away from zero), the limit as given; NA for a missing value.
    /// </summary>
    /// <param name="result">The readout.</param>
    /// <returns>The keys and values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public static IReadOnlyList<(string Key, string? Value)> Fields(ConsistencyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new (string Key, string? Value)[]
        {
            ("since", Date(result.SinceDate)),
            ("total", Rounded(result.Total)),
            ("bestDay", result.BestDay.HasValue ? Date(result.BestDay.Value) : JournalFormat.NotAvailable),
            ("bestDayProfit", result.BestDayProfit.HasValue ? Rounded(result.BestDayProfit.Value) : JournalFormat.NotAvailable),
            ("ratio", result.RatioPercent.HasValue ? Rounded(result.RatioPercent.Value) : JournalFormat.NotAvailable),
            ("limit", result.LimitPercent.ToString("R", CultureInfo.InvariantCulture)),
            ("met", result.Met ? "yes" : "no"),
        };
    }

    /// <summary>The fields of the line when the history could not be read (OPS-012): <c>since=yyyy-MM-dd status=unavailable</c>.</summary>
    /// <param name="sinceDate">The start date of the count.</param>
    /// <returns>The keys and values.</returns>
    public static IReadOnlyList<(string Key, string? Value)> UnavailableFields(DateTime sinceDate)
    {
        return new (string Key, string? Value)[] { ("since", Date(sinceDate)), ("status", "unavailable") };
    }

    private static void Check(ClosedTradeInfo trade)
    {
        if (trade == null)
        {
            throw new ArgumentException("The history holds a null entry.", "trades");
        }

        if (trade.CloseTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A close time is not a UTC time (kind UTC).", "trades");
        }

        if (!double.IsFinite(trade.NetProfit))
        {
            throw new ArgumentException("A net profit is not a finite number.", "trades");
        }
    }

    private static string Rounded(double value)
    {
        // Rounded to 2 decimals, then the round-trip format; a rounded -0 is written as 0.
        double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Date(DateTime date)
    {
        return date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
    }
}
