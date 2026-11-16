using System;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// The label of every bot order and position: <c>ZFVG|v1|&lt;symbol&gt;|&lt;zoneId&gt;</c> (EXE-008,
/// docs/architecture.md section 7). It identifies the bot, its version and the zone, and is how
/// placement stays idempotent (EXE-009).
/// </summary>
public static class OrderLabel
{
    /// <summary>First field of every bot label (spec 5.2).</summary>
    public const string BotName = "ZFVG";

    /// <summary>Second field: the label format version (spec 5.2).</summary>
    public const string Version = "v1";

    /// <summary>Longest label built or accepted, in characters (EXE-008).</summary>
    public const int MaxLength = 100;

    private const char Separator = '|';

    /// <summary>Builds the label of a zone's order (EXE-008).</summary>
    /// <param name="symbol">Symbol name; not empty and without '|'.</param>
    /// <param name="zoneId">Zone id; not empty and without '|'.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentException">A field is empty or contains '|', or the label would be longer than <see cref="MaxLength"/>.</exception>
    public static string Build(string symbol, string zoneId)
    {
        RequireField(symbol, nameof(symbol));
        RequireField(zoneId, nameof(zoneId));
        string label = BotName + Separator + Version + Separator + symbol + Separator + zoneId;
        if (label.Length > MaxLength)
        {
            throw new ArgumentException("The label would be longer than 100 characters.", nameof(zoneId));
        }

        return label;
    }

    /// <summary>Reads symbol and zone id from a label built by <see cref="Build"/> (EXE-008).</summary>
    /// <param name="label">The label, possibly null.</param>
    /// <param name="symbol">The symbol, or empty when the label is not a v1 bot label.</param>
    /// <param name="zoneId">The zone id, or empty when the label is not a v1 bot label.</param>
    /// <returns>True when the label is a valid v1 bot label.</returns>
    public static bool TryParse(string? label, out string symbol, out string zoneId)
    {
        symbol = string.Empty;
        zoneId = string.Empty;
        if (label == null || label.Length > MaxLength)
        {
            return false;
        }

        string[] parts = label.Split(Separator);
        if (parts.Length != 4 || parts[0] != BotName || parts[1] != Version || parts[2].Length == 0 || parts[3].Length == 0)
        {
            return false;
        }

        symbol = parts[2];
        zoneId = parts[3];
        return true;
    }

    private static void RequireField(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(Separator) >= 0)
        {
            throw new ArgumentException("A label field must not be empty or contain '|'.", name);
        }
    }
}
