using System;
using System.Globalization;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// The invariant-culture formatting and parsing used in journal lines, reasons and stored values
/// (AGENTS.md rule 7, architecture section 5). One place instead of a private copy per file.
/// </summary>
public static class Invariant
{
    /// <summary>A whole number, for example <c>-12</c> (AGENTS.md rule 7).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A whole number, for example an order id (AGENTS.md rule 7).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Text(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A number in round-trip form, for example <c>1.0995</c> (AGENTS.md rule 7).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A time of day or duration in the constant form, for example <c>07:00:00</c> (AGENTS.md rule 7).</summary>
    /// <param name="value">The time span.</param>
    /// <returns>The text.</returns>
    public static string Text(TimeSpan value)
    {
        return value.ToString("c", CultureInfo.InvariantCulture);
    }

    /// <summary>A time to the minute, <c>yyyy-MM-dd HH:mm</c> (AGENTS.md rule 7).</summary>
    /// <param name="value">The time.</param>
    /// <returns>The text.</returns>
    public static string Minute(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>A time to the second, <c>yyyy-MM-dd HH:mm:ss</c> (AGENTS.md rule 7).</summary>
    /// <param name="value">The time.</param>
    /// <returns>The text.</returns>
    public static string Second(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a time stored in round-trip form (<c>o</c>) and accepts it only with kind UTC
    /// (AGENTS.md rule 7, REC-012).
    /// </summary>
    /// <param name="text">The stored text, possibly null.</param>
    /// <param name="utc">The time when the result is true.</param>
    /// <returns>True when the text is a round-trip UTC time.</returns>
    public static bool TryParseUtc(string? text, out DateTime utc)
    {
        return DateTime.TryParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc) && utc.Kind == DateTimeKind.Utc;
    }
}
