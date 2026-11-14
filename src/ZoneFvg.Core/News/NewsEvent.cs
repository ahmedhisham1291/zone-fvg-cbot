using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// One calendar event in UTC (spec 9.2, NWS-003, NWS-010, NWS-011). The values are checked on
/// construction and cannot be changed afterwards. Invalid input throws
/// <see cref="ArgumentException"/>.
/// </summary>
/// <param name="TimeUtc">Event time in UTC with kind UTC, or null when the event has no clock time (NWS-012).</param>
/// <param name="Currency">Currency code the event belongs to, for example USD.</param>
/// <param name="Impact">Impact rating; only <see cref="NewsImpact.High"/> opens a window (NWS-001).</param>
/// <param name="Title">Event title as the source gives it.</param>
/// <param name="Source">Name of the source, for example ForexFactory; merged events name both sources (NWS-002).</param>
public sealed record NewsEvent(DateTime? TimeUtc, string Currency, NewsImpact Impact, string Title, string Source)
{
    /// <summary>Event time in UTC, or null when the event has no clock time (NWS-012).</summary>
    public DateTime? TimeUtc { get; } = Validated(TimeUtc, Currency, Title, Source);

    /// <summary>Currency code the event belongs to, for example USD.</summary>
    public string Currency { get; } = Currency;

    /// <summary>Impact rating (NWS-001).</summary>
    public NewsImpact Impact { get; } = Impact;

    /// <summary>Event title as the source gives it.</summary>
    public string Title { get; } = Title;

    /// <summary>Name of the source (NWS-002).</summary>
    public string Source { get; } = Source;

    /// <summary>
    /// True when the event has a usable clock time: a time is given and the event is not a
    /// holiday. Only such events can open a window; the others are reported (NWS-012).
    /// </summary>
    public bool HasClockTime => TimeUtc.HasValue && Impact != NewsImpact.Holiday;

    private static DateTime? Validated(DateTime? timeUtc, string currency, string title, string source)
    {
        if (timeUtc.HasValue && timeUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time of a news event must be a UTC time.", nameof(TimeUtc));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("The currency of a news event must not be empty.", nameof(Currency));
        }

        if (title == null)
        {
            throw new ArgumentException("The title of a news event must not be null.", nameof(Title));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("The source of a news event must not be empty.", nameof(Source));
        }

        return timeUtc;
    }
}
