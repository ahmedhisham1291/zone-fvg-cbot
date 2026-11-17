using System;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// Price precision of a symbol (EXE-008, EXE-010, EXE-017). Since EXE-017 the order levels are
/// rounded to the broker's tick size (<see cref="RoundToTick"/>) and written with the broker's
/// digits (<see cref="IsUsableGrid"/>). The older pip-size functions, which derived the digits as
/// the decimals of the pip size plus one, stay for the pip-size check of
/// <see cref="IsUsable"/> and for their tests (docs/architecture.md section 10).
/// </summary>
public static class PriceGrid
{
    /// <summary>Largest number of price digits accepted from the broker (EXE-017).</summary>
    public const int MaxDigits = 10;

    private const int MaxPipDecimals = 8;

    // Above this size a price cannot be divided by a tick of 10^-10 in decimal without overflow.
    private const double MaxGridPrice = 1e15;

    // EXE-017: the tick size must be within this fraction of a step of a whole multiple of 10^-Digits.
    private const double GridTolerance = 1e-9;

    /// <summary>
    /// True when the broker's price grid is usable (EXE-017): digits from 0 to 10, and a tick size
    /// that is finite, above zero and a whole multiple (at least one) of 10^-digits, within one
    /// billionth of that step.
    /// </summary>
    /// <param name="digits">Number of price digits, cTrader <c>Symbol.Digits</c>.</param>
    /// <param name="tickSize">Tick size, cTrader <c>Symbol.TickSize</c>.</param>
    /// <returns>True when entry, stop and target can be put on this grid.</returns>
    public static bool IsUsableGrid(int digits, double tickSize)
    {
        if (digits < 0 || digits > MaxDigits || !double.IsFinite(tickSize) || tickSize <= 0.0)
        {
            return false;
        }

        double steps = tickSize * Math.Pow(10.0, digits);
        double whole = Math.Round(steps);
        return whole >= 1.0 && Math.Abs(steps - whole) <= GridTolerance;
    }

    /// <summary>
    /// Rounds a price half away from zero to a whole multiple of the tick size (EXE-017). The
    /// division and rounding are done in <see cref="decimal"/>, so a price written with the
    /// symbol's digits is rounded as written (1.0834550 to 1.08346 on a tick of 0.00001).
    /// </summary>
    /// <param name="price">The price. Must be finite and at most 10^15 in size.</param>
    /// <param name="tickSize">The tick size. Must be finite, at least 10^-11 and at most 10^15 (see <see cref="IsUsableGrid"/>).</param>
    /// <returns>The rounded price.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The price or the tick size is out of range.</exception>
    public static double RoundToTick(double price, double tickSize)
    {
        if (!double.IsFinite(price) || Math.Abs(price) > MaxGridPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "The price must be a finite number of at most 10^15 in size.");
        }

        if (!double.IsFinite(tickSize) || tickSize < 1e-11 || tickSize > MaxGridPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(tickSize), "The tick size must be a finite number from 10^-11 to 10^15.");
        }

        decimal tick = (decimal)tickSize;
        decimal steps = Math.Round((decimal)price / tick, 0, MidpointRounding.AwayFromZero);
        return (double)(steps * tick);
    }

    /// <summary>True when <see cref="Digits"/> accepts the pip size: finite, greater than zero, at most 1 and with at most 8 decimals (EXE-008).</summary>
    /// <param name="pipSize">Pip size.</param>
    /// <returns>True when the pip size is usable.</returns>
    public static bool IsUsable(double pipSize)
    {
        return double.IsFinite(pipSize) && pipSize > 0.0 && pipSize <= 1.0 && DecimalsOrMinusOne(pipSize) >= 0;
    }

    /// <summary>Number of price digits of a symbol with this pip size: decimals of the pip size plus one (EXE-008).</summary>
    /// <param name="pipSize">Pip size, for example 0.0001. Must be finite, greater than zero and have at most 8 decimals.</param>
    /// <returns>The number of digits, 1 to 9.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The pip size is not usable.</exception>
    internal static int Digits(double pipSize)
    {
        if (!double.IsFinite(pipSize) || pipSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(pipSize), "The pip size must be a finite number greater than zero.");
        }

        int decimals = DecimalsOrMinusOne(pipSize);
        if (decimals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pipSize), "The pip size must have at most 8 decimals.");
        }

        return decimals + 1;
    }

    /// <summary>Smallest price step of a symbol with this pip size: 10 to the power of minus <see cref="Digits"/> (EXE-010).</summary>
    /// <param name="pipSize">Pip size.</param>
    /// <returns>The price step, for example 0.00001 for EURUSD.</returns>
    internal static double Tick(double pipSize)
    {
        return Math.Pow(10.0, -Digits(pipSize));
    }

    private static int DecimalsOrMinusOne(double pipSize)
    {
        double scale = 1.0;
        for (int decimals = 0; decimals <= MaxPipDecimals; decimals++)
        {
            double scaled = pipSize * scale;
            if (scaled >= 0.5 && Math.Abs(scaled - Math.Round(scaled)) <= 1e-6)
            {
                return decimals;
            }

            scale *= 10.0;
        }

        return -1;
    }

    /// <summary>
    /// Rounds a price to the symbol's digits, half away from zero (EXE-008). Order levels are put on
    /// this grid before they are checked, sized and sent, so the broker stores exactly the prices
    /// that were checked.
    /// </summary>
    /// <param name="price">The price. Must be finite.</param>
    /// <param name="pipSize">Pip size of the symbol.</param>
    /// <returns>The rounded price.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The price is not finite or the pip size is not usable.</exception>
    internal static double Round(double price, double pipSize)
    {
        if (!double.IsFinite(price))
        {
            throw new ArgumentOutOfRangeException(nameof(price), "The price must be a finite number.");
        }

        return Math.Round(price, Digits(pipSize), MidpointRounding.AwayFromZero);
    }
}
