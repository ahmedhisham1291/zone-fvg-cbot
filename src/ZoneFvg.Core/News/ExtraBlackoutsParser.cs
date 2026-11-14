using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZoneFvg.Core.News;

/// <summary>
/// Parser of the <c>ExtraBlackouts</c> parameter (NWS-011): a semicolon-separated list of
/// <c>yyyy-MM-dd HH:mm CUR</c> entries in UTC, each a high-impact event. Spaces around an entry
/// are ignored. An empty or blank parameter is an empty list. Anything else that does not match
/// exactly, including an empty entry between two semicolons, is malformed.
/// </summary>
public static class ExtraBlackoutsParser
{
    /// <summary>Source name of the events created from the parameter (NWS-011).</summary>
    public const string SourceName = "ExtraBlackouts";

    /// <summary>Title of the events created from the parameter (NWS-011).</summary>
    public const string EventTitle = "Extra blackout";

    private const string TimeFormat = "yyyy-MM-dd HH:mm";
    private const int EntryLength = 20;

    /// <summary>
    /// Parses the parameter (NWS-011). Stops at the first malformed entry and quotes it in the
    /// error.
    /// </summary>
    /// <param name="text">The parameter value. Null is malformed.</param>
    /// <returns>The events, or the error for the first malformed entry.</returns>
    public static ExtraBlackoutsResult Parse(string? text)
    {
        if (text == null)
        {
            return ExtraBlackoutsResult.Invalid("ExtraBlackouts must not be null.");
        }

        if (text.Trim().Length == 0)
        {
            return ExtraBlackoutsResult.Valid(Array.Empty<NewsEvent>());
        }

        string[] entries = text.Split(';');
        var events = new List<NewsEvent>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i].Trim();
            NewsEvent? newsEvent = ParseEntry(entry);
            if (newsEvent == null)
            {
                return ExtraBlackoutsResult.Invalid("ExtraBlackouts entry " + (i + 1).ToString(CultureInfo.InvariantCulture)
                    + " is malformed: \"" + entry + "\". Expected \"yyyy-MM-dd HH:mm CUR\" in UTC, entries separated by"
                    + " semicolons, for example \"2026-11-10 13:30 USD;2026-11-12 07:00 GBP\".");
            }

            events.Add(newsEvent);
        }

        return ExtraBlackoutsResult.Valid(events);
    }

    private static NewsEvent? ParseEntry(string entry)
    {
        if (entry.Length != EntryLength || entry[16] != ' ')
        {
            return null;
        }

        for (int i = 17; i < EntryLength; i++)
        {
            if (entry[i] < 'A' || entry[i] > 'Z')
            {
                return null;
            }
        }

        if (!DateTime.TryParseExact(entry.Substring(0, 16), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return null;
        }

        return new NewsEvent(DateTime.SpecifyKind(time, DateTimeKind.Utc), entry.Substring(17), NewsImpact.High, EventTitle, SourceName);
    }
}

/// <summary>Result of <see cref="ExtraBlackoutsParser.Parse"/> (NWS-011).</summary>
public sealed class ExtraBlackoutsResult
{
    private ExtraBlackoutsResult(bool isValid, string error, IReadOnlyList<NewsEvent> events)
    {
        IsValid = isValid;
        Error = error;
        Events = events;
    }

    /// <summary>True when every entry is well formed (NWS-011).</summary>
    public bool IsValid { get; }

    /// <summary>The message for the first malformed entry, quoting it; empty when valid (NWS-011).</summary>
    public string Error { get; }

    /// <summary>One high-impact event per entry, in parameter order; empty when invalid (NWS-011).</summary>
    public IReadOnlyList<NewsEvent> Events { get; }

    internal static ExtraBlackoutsResult Valid(IReadOnlyList<NewsEvent> events)
    {
        return new ExtraBlackoutsResult(true, string.Empty, events);
    }

    internal static ExtraBlackoutsResult Invalid(string error)
    {
        return new ExtraBlackoutsResult(false, error, Array.Empty<NewsEvent>());
    }
}
