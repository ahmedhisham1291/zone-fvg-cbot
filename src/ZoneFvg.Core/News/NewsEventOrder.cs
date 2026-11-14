using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.News;

/// <summary>
/// The fixed order of events in the calendar and its reports: events without a time first, then
/// by time, currency, impact, title and source, all compared ordinally. Makes every list
/// independent of input order (determinism).
/// </summary>
public sealed class NewsEventOrder : IComparer<NewsEvent>
{
    private NewsEventOrder()
    {
    }

    /// <summary>The single comparer (it holds no state).</summary>
    public static NewsEventOrder Instance { get; } = new NewsEventOrder();

    /// <summary>Compares two events in the calendar order (NWS-002, NWS-012).</summary>
    /// <param name="x">First event.</param>
    /// <param name="y">Second event.</param>
    /// <returns>Negative, zero or positive as usual.</returns>
    public int Compare(NewsEvent? x, NewsEvent? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x == null)
        {
            return -1;
        }

        if (y == null)
        {
            return 1;
        }

        int result = Nullable.Compare(x.TimeUtc, y.TimeUtc);
        if (result == 0)
        {
            result = string.CompareOrdinal(x.Currency, y.Currency);
        }

        if (result == 0)
        {
            result = ((int)x.Impact).CompareTo((int)y.Impact);
        }

        if (result == 0)
        {
            result = string.CompareOrdinal(x.Title, y.Title);
        }

        return result == 0 ? string.CompareOrdinal(x.Source, y.Source) : result;
    }
}
