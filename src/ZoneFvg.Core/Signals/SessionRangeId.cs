using System;
using System.Globalization;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// Reads the id of a session range setup (SIG-032): <c>RH-yyyyMMdd</c> for a buy at the range
/// high and <c>RL-yyyyMMdd</c> for a sell at the range low, from the date that names the trading
/// day. The timed exit (EXE-025) finds the trading day of a position with it.
/// </summary>
public static class SessionRangeId
{
    /// <summary>Prefix of the id of a buy setup at the range high (SIG-032).</summary>
    public const string BuyPrefix = "RH-";

    /// <summary>Prefix of the id of a sell setup at the range low (SIG-032).</summary>
    public const string SellPrefix = "RL-";

    private const string DateFormat = "yyyyMMdd";

    /// <summary>
    /// Reads the trading date from a session range id (SIG-032, EXE-025). The id must be exactly
    /// a prefix <see cref="BuyPrefix"/> or <see cref="SellPrefix"/> followed by eight digits that
    /// form a valid date in the invariant format <c>yyyyMMdd</c>, from year 2 to year 9998 so
    /// that the instants of that trading day exist. Every other text, the ids of the other modes
    /// included, gives false.
    /// </summary>
    /// <param name="id">The id, for example the zone id of a label; null gives false.</param>
    /// <param name="tradingDate">The date that names the trading day, at 00:00 with kind Unspecified; the smallest date when the result is false.</param>
    /// <returns>True when the id is a session range id.</returns>
    public static bool TryTradingDate(string? id, out DateTime tradingDate)
    {
        tradingDate = DateTime.MinValue;
        if (id is null || id.Length != BuyPrefix.Length + DateFormat.Length)
        {
            return false;
        }

        bool prefixed = id.StartsWith(BuyPrefix, StringComparison.Ordinal) || id.StartsWith(SellPrefix, StringComparison.Ordinal);
        string digits = id.Substring(BuyPrefix.Length);
        if (!prefixed || !IsDigits(digits))
        {
            return false;
        }

        if (!DateTime.TryParseExact(digits, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) || date.Year < 2 || date.Year > 9998)
        {
            return false;
        }

        tradingDate = date;
        return true;
    }

    private static bool IsDigits(string text)
    {
        foreach (char c in text)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
