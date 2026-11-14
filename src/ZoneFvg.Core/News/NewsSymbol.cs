using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// The relevant currencies of a symbol (NWS-001): the first three and the last three of its first
/// six letters. A broker suffix after the sixth character is ignored, so EURUSD.r gives EUR and
/// USD.
/// </summary>
public static class NewsSymbol
{
    private const int PairLength = 6;

    /// <summary>The base currency: letters one to three, upper case (NWS-001).</summary>
    /// <param name="symbol">Symbol name; its first six characters must be ASCII letters.</param>
    /// <returns>The base currency code.</returns>
    /// <exception cref="ArgumentException">The symbol does not start with six ASCII letters.</exception>
    public static string BaseCurrency(string symbol)
    {
        return Pair(symbol).Substring(0, 3);
    }

    /// <summary>The quote currency: letters four to six, upper case (NWS-001).</summary>
    /// <param name="symbol">Symbol name; its first six characters must be ASCII letters.</param>
    /// <returns>The quote currency code.</returns>
    /// <exception cref="ArgumentException">The symbol does not start with six ASCII letters.</exception>
    public static string QuoteCurrency(string symbol)
    {
        return Pair(symbol).Substring(3, 3);
    }

    /// <summary>The first six letters of the symbol in upper case, for example EURUSD for eurusd.pro (NWS-001).</summary>
    /// <param name="symbol">Symbol name; its first six characters must be ASCII letters.</param>
    /// <returns>The six-letter pair.</returns>
    /// <exception cref="ArgumentException">The symbol does not start with six ASCII letters.</exception>
    public static string Pair(string symbol)
    {
        if (symbol == null || symbol.Length < PairLength)
        {
            throw new ArgumentException("A symbol must start with six letters, for example EURUSD.", nameof(symbol));
        }

        string pair = symbol.Substring(0, PairLength).ToUpperInvariant();
        foreach (char c in pair)
        {
            if (c < 'A' || c > 'Z')
            {
                throw new ArgumentException("A symbol must start with six letters, for example EURUSD, but was \"" + symbol + "\".", nameof(symbol));
            }
        }

        return pair;
    }
}
