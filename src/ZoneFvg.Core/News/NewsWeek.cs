using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// The "current week" used to validate a Forex Factory download (NWS-005): Monday 00:00 UTC up
/// to, not including, the next Monday 00:00 UTC.
/// </summary>
public static class NewsWeek
{
    /// <summary>Monday 00:00 UTC of the week that contains the instant (NWS-005).</summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>The week start, with kind UTC.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static DateTime StartUtc(DateTime utc)
    {
        NewsGuard.RequireUtc(utc, nameof(utc));
        int daysSinceMonday = ((int)utc.DayOfWeek + 6) % 7;
        return utc.Date.AddDays(-daysSinceMonday);
    }

    /// <summary>The next Monday 00:00 UTC after the week start of the instant: the end, exclusive, of its week (NWS-005).</summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>The week end, with kind UTC.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static DateTime EndUtc(DateTime utc)
    {
        return StartUtc(utc).AddDays(7);
    }
}
