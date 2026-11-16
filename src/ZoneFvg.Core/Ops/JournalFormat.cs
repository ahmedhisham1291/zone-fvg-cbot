using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// Invariant formatting of journal values (OPS-001, OPS-002): times, numbers, escaped text and
/// the short text hash used for alerts (OPS-004, OPS-007).
/// </summary>
/// <remarks>
/// A journal line is <c>&lt;time&gt; &lt;category&gt; key=value key=value ...</c>. Values never
/// contain a space or a line break: <see cref="Escape"/> writes a backslash as <c>\\</c>, a space
/// as <c>\s</c>, a line feed as <c>\n</c>, a carriage return as <c>\r</c>, a tab as <c>\t</c> and
/// any other control character as <c>\uXXXX</c>. A missing or not finite value is written as
/// <see cref="NotAvailable"/>.
/// </remarks>
public static class JournalFormat
{
    /// <summary>The value written for a missing or not finite number (OPS-001, OPS-002).</summary>
    public const string NotAvailable = "NA";

    /// <summary>A UTC time as <c>yyyy-MM-ddTHH:mm:ssZ</c>; fractions of a second are cut off (OPS-001).</summary>
    /// <param name="utc">The time; its kind is not checked.</param>
    /// <returns>The formatted time.</returns>
    public static string Time(DateTime utc)
    {
        return utc.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A number with a fixed count of decimals, rounded as .NET's invariant <c>F</c> format does,
    /// never written as negative zero (OPS-001, OPS-002).
    /// </summary>
    /// <param name="value">The value, possibly null.</param>
    /// <param name="decimals">Decimals, 0 to 10.</param>
    /// <returns>The formatted value, or <see cref="NotAvailable"/> for null, NaN or infinity.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimals"/> is outside 0 to 10.</exception>
    public static string Fixed(double? value, int decimals)
    {
        if (decimals < 0 || decimals > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(decimals), "The decimals must be between 0 and 10.");
        }

        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return NotAvailable;
        }

        return NoNegativeZero(value.Value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    /// <summary>A price with up to eight decimals and no trailing zeros, for example <c>1.1</c> (OPS-001, OPS-002).</summary>
    /// <param name="value">The price, possibly null.</param>
    /// <returns>The formatted price, or <see cref="NotAvailable"/> for null, NaN or infinity.</returns>
    public static string Price(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return NotAvailable;
        }

        return NoNegativeZero(value.Value.ToString("0.########", CultureInfo.InvariantCulture));
    }

    /// <summary>A whole number in invariant formatting (OPS-001).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The formatted value.</returns>
    public static string Integer(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A duration in the invariant constant format <c>[-][d.]hh:mm:ss</c>, cut to whole seconds (OPS-002, OPS-003).</summary>
    /// <param name="value">The duration, possibly null.</param>
    /// <returns>The formatted duration, or <see cref="NotAvailable"/> for null.</returns>
    public static string Duration(TimeSpan? value)
    {
        if (!value.HasValue)
        {
            return NotAvailable;
        }

        TimeSpan whole = TimeSpan.FromTicks(value.Value.Ticks - (value.Value.Ticks % TimeSpan.TicksPerSecond));
        return whole.ToString("c", CultureInfo.InvariantCulture);
    }

    /// <summary>Text escaped so that it holds no space and no line break (OPS-001). See the remarks of <see cref="JournalFormat"/>.</summary>
    /// <param name="value">The text, possibly null.</param>
    /// <returns>The escaped text; <see cref="NotAvailable"/> for null.</returns>
    public static string Escape(string? value)
    {
        if (value == null)
        {
            return NotAvailable;
        }

        var text = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    text.Append("\\\\");
                    break;
                case ' ':
                    text.Append("\\s");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c) || char.IsWhiteSpace(c))
                    {
                        text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The first eight lowercase hex characters of the SHA-256 of the UTF-8 text (OPS-004,
    /// OPS-007). Identifies an alert in the journal without writing it out.
    /// </summary>
    /// <param name="text">The text, possibly null (hashed as empty).</param>
    /// <returns>Eight hex characters.</returns>
    public static string Hash8(string? text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var hex = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
        {
            hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static string NoNegativeZero(string text)
    {
        if (text.Length > 1 && text[0] == '-')
        {
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] != '0' && text[i] != '.')
                {
                    return text;
                }
            }

            return text.Substring(1);
        }

        return text;
    }
}
