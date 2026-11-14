using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.News;

/// <summary>
/// Result of parsing one document of a news source (NWS-003, NWS-010): the events and the time
/// range they cover, or a rejection with its reason. A rejected result holds no events.
/// </summary>
public sealed class NewsParseResult
{
    private NewsParseResult(bool isValid, string reason, IReadOnlyList<NewsEvent> events, DateTime coverageStartUtc, DateTime coverageEndUtc, bool hasMalformedEvent)
    {
        IsValid = isValid;
        HasMalformedEvent = hasMalformedEvent;
        Reason = reason;
        Events = events;
        CoverageStartUtc = coverageStartUtc;
        CoverageEndUtc = coverageEndUtc;
    }

    /// <summary>True when the document passed validation (NWS-003, NWS-005).</summary>
    public bool IsValid { get; }

    /// <summary>
    /// True when the document was rejected because one of its events is malformed. Such a
    /// download raises an alert (NWS-005, owner decision of 2026-11-14).
    /// </summary>
    public bool HasMalformedEvent { get; }

    /// <summary>Why the document was rejected, in plain words; a short summary when it was accepted.</summary>
    public string Reason { get; }

    /// <summary>The events in document order, times in UTC; empty when rejected.</summary>
    public IReadOnlyList<NewsEvent> Events { get; }

    /// <summary>First instant the document covers, UTC (NWS-005). Meaningful only when valid.</summary>
    public DateTime CoverageStartUtc { get; }

    /// <summary>End, exclusive, of the time the document covers, UTC (NWS-005). Meaningful only when valid.</summary>
    public DateTime CoverageEndUtc { get; }

    /// <summary>
    /// Creates an accepted result (NWS-003, NWS-010).
    /// </summary>
    /// <param name="events">The events; at least one.</param>
    /// <param name="coverageStartUtc">First covered instant, kind UTC.</param>
    /// <param name="coverageEndUtc">End of coverage, exclusive, kind UTC, after the start.</param>
    /// <param name="summary">Short description for the log.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">The events are empty or contain null, or the coverage is not a UTC range.</exception>
    public static NewsParseResult Accepted(IReadOnlyList<NewsEvent> events, DateTime coverageStartUtc, DateTime coverageEndUtc, string summary)
    {
        if (events == null || events.Count == 0)
        {
            throw new ArgumentException("An accepted document must contain at least one event.", nameof(events));
        }

        var copy = new NewsEvent[events.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = events[i] ?? throw new ArgumentException("An event must not be null.", nameof(events));
        }

        NewsGuard.RequireUtc(coverageStartUtc, nameof(coverageStartUtc));
        NewsGuard.RequireUtc(coverageEndUtc, nameof(coverageEndUtc));
        if (coverageEndUtc <= coverageStartUtc)
        {
            throw new ArgumentException("The coverage must end after it starts.", nameof(coverageEndUtc));
        }

        return new NewsParseResult(true, summary ?? string.Empty, copy, coverageStartUtc, coverageEndUtc, false);
    }

    /// <summary>Creates a rejected result with its reason (NWS-003, NWS-005, NWS-010).</summary>
    /// <param name="reason">Why the document was rejected.</param>
    /// <returns>The result.</returns>
    public static NewsParseResult Rejected(string reason)
    {
        return new NewsParseResult(false, reason ?? string.Empty, Array.Empty<NewsEvent>(), default, default, false);
    }

    /// <summary>
    /// Creates a rejected result for a document with a malformed event: the whole document is
    /// rejected and the rejection raises an alert (NWS-005).
    /// </summary>
    /// <param name="reason">Which event is malformed and why.</param>
    /// <returns>The result, with <see cref="HasMalformedEvent"/> true.</returns>
    public static NewsParseResult RejectedMalformedEvent(string reason)
    {
        return new NewsParseResult(false, reason ?? string.Empty, Array.Empty<NewsEvent>(), default, default, true);
    }

    /// <summary>True when the result is valid and its coverage contains the instant: start at or before it, end after it (NWS-005, NWS-008).</summary>
    /// <param name="utc">The instant.</param>
    /// <returns>True when covered.</returns>
    public bool Covers(DateTime utc)
    {
        return IsValid && CoverageStartUtc <= utc && utc < CoverageEndUtc;
    }
}
