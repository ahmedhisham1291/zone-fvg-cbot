using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZoneFvg.Core.News;

/// <summary>
/// Calendar history for backtests (NWS-010). The adapter embeds the history file and passes its
/// text; this class only reads that text. No network call is involved.
/// </summary>
/// <remarks>
/// <para>Format: one event per line, four comma-separated fields:
/// <c>yyyy-MM-dd HH:mm,CUR,Impact,Title</c>. The time is UTC; CUR is three upper-case letters;
/// Impact is High, Medium, Low or Holiday, spelled exactly; the title is the rest of the line
/// and may contain commas. Example: <c>2026-11-10 13:30,USD,High,CPI m/m</c>.</para>
/// <para>Empty lines and lines starting with <c>#</c> are ignored. One malformed line rejects
/// the whole history, quoting the line, so the backtest runs with the calendar unavailable
/// instead of with a silently shortened history.</para>
/// <para>Coverage: from Monday 00:00 UTC of the week of the earliest event to the Monday after
/// the week of the latest event. Outside it the calendar is unavailable (NWS-008).</para>
/// </remarks>
public static class EmbeddedHistoryProvider
{
    /// <summary>Source name of the history events (NWS-010).</summary>
    public const string SourceName = "EmbeddedHistory";

    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Parses the embedded history (NWS-010).</summary>
    /// <param name="text">The text of the history file. Null or empty is rejected.</param>
    /// <returns>The events with their coverage, or the rejection with its reason.</returns>
    public static NewsParseResult Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return NewsParseResult.Rejected("The embedded history is empty.");
        }

        string[] lines = text.Split('\n');
        var events = new List<NewsEvent>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            NewsEvent? newsEvent = ParseLine(line);
            if (newsEvent == null)
            {
                return NewsParseResult.RejectedMalformedEvent("Embedded history line " + (i + 1).ToString(CultureInfo.InvariantCulture)
                    + " is malformed: \"" + line + "\". Expected \"yyyy-MM-dd HH:mm,CUR,Impact,Title\".");
            }

            events.Add(newsEvent);
        }

        if (events.Count == 0)
        {
            return NewsParseResult.Rejected("The embedded history contains no events.");
        }

        DateTime first = events[0].TimeUtc!.Value;
        DateTime last = first;
        foreach (NewsEvent newsEvent in events)
        {
            DateTime time = newsEvent.TimeUtc!.Value;
            first = time < first ? time : first;
            last = time > last ? time : last;
        }

        string summary = events.Count.ToString(CultureInfo.InvariantCulture) + " events";
        return NewsParseResult.Accepted(events, NewsWeek.StartUtc(first), NewsWeek.EndUtc(last), summary);
    }

    private static NewsEvent? ParseLine(string line)
    {
        string[] fields = line.Split(',', 4);
        if (fields.Length != 4)
        {
            return null;
        }

        if (!DateTime.TryParseExact(fields[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return null;
        }

        string currency = fields[1];
        if (currency.Length != 3 || !IsUpperLetter(currency[0]) || !IsUpperLetter(currency[1]) || !IsUpperLetter(currency[2]))
        {
            return null;
        }

        NewsImpact? impact = ImpactOf(fields[2]);
        if (impact == null)
        {
            return null;
        }

        return new NewsEvent(DateTime.SpecifyKind(time, DateTimeKind.Utc), currency, impact.Value, fields[3], SourceName);
    }

    private static bool IsUpperLetter(char c)
    {
        return c >= 'A' && c <= 'Z';
    }

    private static NewsImpact? ImpactOf(string text)
    {
        switch (text)
        {
            case "High":
                return NewsImpact.High;
            case "Medium":
                return NewsImpact.Medium;
            case "Low":
                return NewsImpact.Low;
            case "Holiday":
                return NewsImpact.Holiday;
            default:
                return null;
        }
    }
}
