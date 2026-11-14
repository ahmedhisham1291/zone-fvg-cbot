using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZoneFvg.Core.News;

/// <summary>
/// JSON form of the values the news module keeps in the key-value store (NWS-006, NWS-007).
/// Times are written in the round-trip ("o") format with invariant culture. Reading is strict:
/// any missing or malformed value makes the whole value unreadable, with a reason.
/// </summary>
internal static class NewsCache
{
    internal const string KeyPrefix = "news.";

    internal static string DataKey(string source)
    {
        return KeyPrefix + source + ".data";
    }

    internal static string AttemptKey(string source)
    {
        return KeyPrefix + source + ".lastAttempt";
    }

    internal static string WriteAttempt(DateTime attemptUtc)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("attemptUtc", Time(attemptUtc));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string? ReadAttempt(string text, out DateTime attemptUtc)
    {
        attemptUtc = default;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return "not valid JSON: " + exception.Message;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "not a JSON object.";
            }

            return ReadTime(document.RootElement, "attemptUtc", out attemptUtc);
        }
    }

    internal static string WriteData(CachedFeedData data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("fetchedUtc", Time(data.FetchedUtc));
            writer.WriteString("coverageStartUtc", Time(data.CoverageStartUtc));
            writer.WriteString("coverageEndUtc", Time(data.CoverageEndUtc));
            writer.WriteStartArray("events");
            foreach (NewsEvent newsEvent in data.Events)
            {
                writer.WriteStartObject();
                if (newsEvent.TimeUtc.HasValue)
                {
                    writer.WriteString("timeUtc", Time(newsEvent.TimeUtc.Value));
                }
                else
                {
                    writer.WriteNull("timeUtc");
                }

                writer.WriteString("currency", newsEvent.Currency);
                writer.WriteString("impact", newsEvent.Impact.ToString());
                writer.WriteString("title", newsEvent.Title);
                writer.WriteString("source", newsEvent.Source);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string? ReadData(string text, out CachedFeedData? data)
    {
        data = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return "not valid JSON: " + exception.Message;
        }

        using (document)
        {
            return ReadData(document.RootElement, out data);
        }
    }

    private static string? ReadData(JsonElement root, out CachedFeedData? data)
    {
        data = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "not a JSON object.";
        }

        string? error = ReadTime(root, "fetchedUtc", out DateTime fetched);
        if (error != null)
        {
            return error;
        }

        error = ReadTime(root, "coverageStartUtc", out DateTime start);
        if (error != null)
        {
            return error;
        }

        error = ReadTime(root, "coverageEndUtc", out DateTime end);
        if (error != null)
        {
            return error;
        }

        if (end <= start)
        {
            return "the coverage ends before it starts.";
        }

        if (!root.TryGetProperty("events", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return "the field \"events\" is missing or not an array.";
        }

        var events = new List<NewsEvent>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            error = ReadEvent(item, out NewsEvent? newsEvent);
            if (error != null)
            {
                return "event " + events.Count.ToString(CultureInfo.InvariantCulture) + ": " + error;
            }

            events.Add(newsEvent!);
        }

        if (events.Count == 0)
        {
            return "it contains no events.";
        }

        data = new CachedFeedData(events, fetched, start, end);
        return null;
    }

    private static string? ReadEvent(JsonElement item, out NewsEvent? newsEvent)
    {
        newsEvent = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return "not a JSON object.";
        }

        DateTime? time = null;
        if (!item.TryGetProperty("timeUtc", out JsonElement timeElement))
        {
            return "the field \"timeUtc\" is missing.";
        }

        if (timeElement.ValueKind != JsonValueKind.Null)
        {
            string? timeError = ReadTime(item, "timeUtc", out DateTime value);
            if (timeError != null)
            {
                return timeError;
            }

            time = value;
        }

        string? currency = ReadText(item, "currency");
        string? impactText = ReadText(item, "impact");
        string? title = ReadText(item, "title");
        string? source = ReadText(item, "source");
        if (currency == null || impactText == null || title == null || source == null)
        {
            return "a text field is missing.";
        }

        NewsImpact? impact = ImpactOf(impactText);
        if (impact == null || currency.Trim().Length == 0 || source.Trim().Length == 0)
        {
            return "a field has an invalid value.";
        }

        newsEvent = new NewsEvent(time, currency, impact.Value, title, source);
        return null;
    }

    private static string? ReadText(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string? ReadTime(JsonElement item, string name, out DateTime utc)
    {
        utc = default;
        string? text = ReadText(item, name);
        if (text == null)
        {
            return "the field \"" + name + "\" is missing or not a string.";
        }

        if (!DateTime.TryParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed) || parsed.Kind != DateTimeKind.Utc)
        {
            return "the field \"" + name + "\" is not a UTC time in round-trip format.";
        }

        utc = parsed;
        return null;
    }

    private static NewsImpact? ImpactOf(string text)
    {
        foreach (NewsImpact impact in new[] { NewsImpact.High, NewsImpact.Medium, NewsImpact.Low, NewsImpact.Holiday, NewsImpact.Unknown })
        {
            if (string.Equals(text, impact.ToString(), StringComparison.Ordinal))
            {
                return impact;
            }
        }

        return null;
    }

    private static string Time(DateTime utc)
    {
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }
}

/// <summary>The accepted data of one source with its fetch time and coverage (NWS-005, NWS-007).</summary>
internal sealed class CachedFeedData
{
    internal CachedFeedData(IReadOnlyList<NewsEvent> events, DateTime fetchedUtc, DateTime coverageStartUtc, DateTime coverageEndUtc)
    {
        Events = events;
        FetchedUtc = fetchedUtc;
        CoverageStartUtc = coverageStartUtc;
        CoverageEndUtc = coverageEndUtc;
    }

    internal IReadOnlyList<NewsEvent> Events { get; }

    internal DateTime FetchedUtc { get; }

    internal DateTime CoverageStartUtc { get; }

    internal DateTime CoverageEndUtc { get; }
}
