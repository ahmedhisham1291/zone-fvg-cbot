using System;
using System.Globalization;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The id of an Overnight setup (SIG-043, spec 3.10): <c>ON-yyyyMMdd</c>, from the date that
/// names the trading day. The timed exit (EXE-033) finds the trading day of a position with it,
/// and the two instances of one symbol tell their trades apart by it (row BA).
/// </summary>
public static class OvernightId
{
    /// <summary>Prefix of the id of an Overnight setup (SIG-043).</summary>
    public const string Prefix = "ON-";

    private const string DateFormat = "yyyyMMdd";

    /// <summary>The id of the trading day named by <paramref name="tradingDate"/> (SIG-043).</summary>
    /// <param name="tradingDate">The date that names the trading day; its time of day is ignored.</param>
    /// <returns><c>ON-yyyyMMdd</c> in the invariant format.</returns>
    public static string For(DateTime tradingDate)
    {
        return Prefix + tradingDate.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the trading date from an Overnight id (SIG-043, EXE-033). The id must be exactly
    /// <see cref="Prefix"/> followed by eight digits that form a valid date in the invariant format
    /// <c>yyyyMMdd</c>, from year 2 to year 9998. Every other text, the ids of the other modes
    /// included, gives false.
    /// </summary>
    /// <param name="id">The id, for example the zone id of a label; null gives false.</param>
    /// <param name="tradingDate">The date that names the trading day, at 00:00 with kind Unspecified; the smallest date when the result is false.</param>
    /// <returns>True when the id is an Overnight id.</returns>
    public static bool TryTradingDate(string? id, out DateTime tradingDate)
    {
        tradingDate = DateTime.MinValue;
        if (id is null || id.Length != Prefix.Length + DateFormat.Length || !id.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string digits = id.Substring(Prefix.Length);
        foreach (char c in digits)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        if (!DateTime.TryParseExact(digits, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) || date.Year < 2 || date.Year > 9998)
        {
            return false;
        }

        tradingDate = date;
        return true;
    }
}
