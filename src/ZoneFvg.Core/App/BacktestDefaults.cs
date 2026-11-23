using System;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.App;

/// <summary>
/// The two backtest-only defaults (PLT-014, architecture section 10, "Backtest defaults"): the
/// risk epoch for an empty RiskEpochDate in a backtest, and whether the news filter is on when the
/// embedded news history has no events. The adapter calls them at start. Live, nothing changes: an
/// empty RiskEpochDate refuses the start, and NewsEnabled is passed on unchanged.
/// </summary>
public static class BacktestDefaults
{
    /// <summary>The message of the refusal for an empty RiskEpochDate outside a backtest (PLT-014, as before).</summary>
    public const string EmptyRiskEpochMessage = "RiskEpochDate must be a date written as yyyy-MM-dd, for example 2026-12-01.";

    /// <summary>The text of the start log line of <see cref="NewsFilterOffReason"/> (PLT-014).</summary>
    public const string NewsFilterOffMessage = "news filter off for this backtest: the embedded news history has no events";

    /// <summary>
    /// The risk epoch of the run (PLT-014): the configured date when one is given, in both modes;
    /// otherwise, in a backtest, the CE(S)T date of the server time at start, so that the epoch
    /// starts at or before that time; otherwise (live, empty) a refusal.
    /// </summary>
    /// <param name="configured">The date the owner set, or null when RiskEpochDate is empty.</param>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <param name="serverTimeUtc">The broker's server time at start, kind UTC. Read only for an empty date in a backtest.</param>
    /// <returns>The configured date unchanged, or the CE(S)T date of <paramref name="serverTimeUtc"/> at 00:00 with kind UTC.</returns>
    /// <exception cref="ArgumentException">
    /// Live with no configured date (parameter name <c>RiskEpochDate</c>), or, in a backtest with no
    /// configured date, a server time whose kind is not UTC (parameter name <c>serverTimeUtc</c>).
    /// </exception>
    public static DateTime RiskEpoch(DateTime? configured, bool isBacktest, DateTime serverTimeUtc)
    {
        if (configured.HasValue)
        {
            return configured.Value;
        }

        if (!isBacktest)
        {
            // Live: RiskEpochDate must be set by hand (spec section 7); fail closed.
            throw new ArgumentException(EmptyRiskEpochMessage, "RiskEpochDate");
        }

        if (serverTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The server time must be a UTC time (kind UTC).", nameof(serverTimeUtc));
        }

        DateTime localWallClock = serverTimeUtc + CestClock.UtcOffset(serverTimeUtc);
        return DateTime.SpecifyKind(localWallClock.Date, DateTimeKind.Utc);
    }

    /// <summary>
    /// Whether the news filter is on for the run (PLT-014): off when NewsEnabled is false; off in a
    /// backtest whose embedded news history has no events; otherwise on, so live the filter
    /// follows NewsEnabled and blocks entries without a trusted calendar.
    /// </summary>
    /// <param name="newsEnabled">The NewsEnabled parameter.</param>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <param name="embeddedEventCount">The number of events of the embedded news history, from <see cref="ZoneFvg.Core.News.EmbeddedHistoryProvider.CountEvents(string)"/>; 0 or more.</param>
    /// <returns>True when the news filter is on.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="embeddedEventCount"/> is negative.</exception>
    public static bool NewsFilterOn(bool newsEnabled, bool isBacktest, int embeddedEventCount)
    {
        if (embeddedEventCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddedEventCount), embeddedEventCount, "The number of events cannot be negative.");
        }

        if (!newsEnabled)
        {
            return false;
        }

        return !isBacktest || embeddedEventCount > 0;
    }

    /// <summary>
    /// The one start log line that says why the news filter is off although NewsEnabled is true
    /// (PLT-014); null when there is nothing to say. The adapter prints it with its own prefix.
    /// </summary>
    /// <param name="newsEnabled">The NewsEnabled parameter.</param>
    /// <param name="newsFilterOn">The result of <see cref="NewsFilterOn"/> for the run.</param>
    /// <returns><see cref="NewsFilterOffMessage"/> when NewsEnabled is true and the filter is off; otherwise null.</returns>
    public static string? NewsFilterOffReason(bool newsEnabled, bool newsFilterOn)
    {
        return newsEnabled && !newsFilterOn ? NewsFilterOffMessage : null;
    }
}
