using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace ZoneFvg.Core.News;

/// <summary>
/// Parser of the Forex Factory weekly JSON export (NWS-003), built against the owner's real
/// sample. The document is a JSON array of objects with the string fields <c>title</c>,
/// <c>country</c> (a currency code), <c>date</c> (ISO 8601 with an offset, for example
/// <c>2026-11-10T08:30:00-05:00</c>) and <c>impact</c> (High, Medium, Low, Holiday). Other
/// fields are ignored.
/// </summary>
/// <remarks>
/// <para>Validation (NWS-003, NWS-005): the document must be valid JSON, not an HTML page, an
/// array with at least one event, every event must have the four fields as strings with a date
/// that has an offset, and at least one event must fall in the current week (Monday 00:00 UTC to
/// the next Monday). One malformed event rejects the whole document, so a damaged download can
/// never silently lose an event; such a rejection raises an alert (NWS-005).</para>
/// <para>Impact values other than High, Medium, Low and Holiday (compared ignoring case) are kept
/// as <see cref="NewsImpact.Unknown"/>, which is not high (NWS-003).</para>
/// </remarks>
public sealed class ForexFactoryParser : INewsFeedParser
{
    /// <summary>Source name of this feed (NWS-003).</summary>
    public const string SourceName = "ForexFactory";

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const int ExcerptLength = 60;

    /// <inheritdoc/>
    public string Source => SourceName;

    /// <summary>
    /// Parses one download (NWS-003). The coverage of an accepted document is the current week
    /// of <paramref name="nowUtc"/> (NWS-005).
    /// </summary>
    /// <param name="body">The downloaded document.</param>
    /// <param name="nowUtc">Server time of the download, kind UTC.</param>
    /// <returns>The events in UTC with the coverage, or a rejection with its reason.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="nowUtc"/> is not UTC.</exception>
    public NewsParseResult Parse(string body, DateTime nowUtc)
    {
        NewsGuard.RequireUtc(nowUtc, nameof(nowUtc));
        string text = (body ?? string.Empty).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.Length == 0)
        {
            return NewsParseResult.Rejected("The document is empty.");
        }

        if (text[0] == '<')
        {
            return NewsParseResult.Rejected("The document is an HTML page, not JSON: \"" + Excerpt(text) + "\".");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return NewsParseResult.Rejected("The document is not valid JSON: " + exception.Message);
        }

        using (document)
        {
            return ParseRoot(document.RootElement, nowUtc);
        }
    }

    private static NewsParseResult ParseRoot(JsonElement root, DateTime nowUtc)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return NewsParseResult.Rejected("The document is not a JSON array of events.");
        }

        var events = new List<NewsEvent>();
        int index = 0;
        foreach (JsonElement item in root.EnumerateArray())
        {
            string? error = ReadEvent(item, out NewsEvent? newsEvent);
            if (error != null)
            {
                return NewsParseResult.RejectedMalformedEvent("Event " + index.ToString(CultureInfo.InvariantCulture) + " is malformed: " + error);
            }

            events.Add(newsEvent!);
            index++;
        }

        if (events.Count == 0)
        {
            return NewsParseResult.Rejected("The document contains no events.");
        }

        DateTime weekStart = NewsWeek.StartUtc(nowUtc);
        DateTime weekEnd = NewsWeek.EndUtc(nowUtc);
        if (!events.Exists(e => e.TimeUtc >= weekStart && e.TimeUtc < weekEnd))
        {
            return NewsParseResult.Rejected("The document has no event in the current week from "
                + weekStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.");
        }

        string summary = events.Count.ToString(CultureInfo.InvariantCulture) + " events";
        return NewsParseResult.Accepted(events, weekStart, weekEnd, summary);
    }

    private static string? ReadEvent(JsonElement item, out NewsEvent? newsEvent)
    {
        newsEvent = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return "it is not a JSON object.";
        }

        string[] names = { "title", "country", "date", "impact" };
        var values = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string? error = ReadString(item, names[i], out values[i]);
            if (error != null)
            {
                return error;
            }
        }

        string title = values[0];
        string country = values[1];
        string date = values[2];
        string impact = values[3];

        if (country.Trim().Length == 0)
        {
            return "the field \"country\" is empty.";
        }

        if (!DateTimeOffset.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time))
        {
            return "the date \"" + date + "\" is not ISO 8601 with an offset (yyyy-MM-ddTHH:mm:ss+hh:mm).";
        }

        newsEvent = new NewsEvent(time.UtcDateTime, country.Trim(), ImpactOf(impact), title, SourceName);
        return null;
    }

    private static string? ReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out JsonElement element))
        {
            return "the field \"" + name + "\" is missing.";
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return "the field \"" + name + "\" is not a string.";
        }

        value = element.GetString() ?? string.Empty;
        return null;
    }

    private static NewsImpact ImpactOf(string impact)
    {
        string trimmed = impact.Trim();
        if (string.Equals(trimmed, "High", StringComparison.OrdinalIgnoreCase))
        {
            return NewsImpact.High;
        }

        if (string.Equals(trimmed, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            return NewsImpact.Medium;
        }

        if (string.Equals(trimmed, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return NewsImpact.Low;
        }

        return string.Equals(trimmed, "Holiday", StringComparison.OrdinalIgnoreCase) ? NewsImpact.Holiday : NewsImpact.Unknown;
    }

    private static string Excerpt(string text)
    {
        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return flat.Length <= ExcerptLength ? flat : flat.Substring(0, ExcerptLength) + "...";
    }
}
