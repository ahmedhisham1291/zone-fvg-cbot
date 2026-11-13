using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// Pure helpers for price and volume arithmetic (docs/architecture.md section 5). Prices are
/// doubles, so computed values are compared through these helpers with an explicit tolerance
/// and never with raw equality.
/// </summary>
/// <remarks>
/// Every helper throws <see cref="ArgumentOutOfRangeException"/> for NaN, infinite or
/// out-of-range input instead of returning an answer that could let an order through.
/// </remarks>
public static class PriceMath
{
    /// <summary>Default comparison tolerance. It is far below one tick of any traded symbol.</summary>
    public const double DefaultTolerance = 1e-9;

    /// <summary>
    /// True when the two values differ by at most the tolerance. A difference of exactly the
    /// tolerance counts as equal.
    /// </summary>
    /// <param name="a">First value. Must be finite.</param>
    /// <param name="b">Second value. Must be finite.</param>
    /// <param name="tolerance">Largest difference that still counts as equal. Must be finite and not negative.</param>
    /// <returns>True when the values are equal within the tolerance.</returns>
    public static bool AlmostEqual(double a, double b, double tolerance)
    {
        return Math.Abs(Difference(a, b, tolerance)) <= tolerance;
    }

    /// <summary>
    /// True when <paramref name="a"/> is above <paramref name="b"/> by more than the tolerance.
    /// </summary>
    /// <param name="a">First value. Must be finite.</param>
    /// <param name="b">Second value. Must be finite.</param>
    /// <param name="tolerance">Difference that still counts as equal. Must be finite and not negative.</param>
    /// <returns>True when a is greater than b beyond the tolerance.</returns>
    public static bool GreaterThan(double a, double b, double tolerance)
    {
        return Difference(a, b, tolerance) > tolerance;
    }

    /// <summary>
    /// True when <paramref name="a"/> is below <paramref name="b"/> by more than the tolerance.
    /// </summary>
    /// <param name="a">First value. Must be finite.</param>
    /// <param name="b">Second value. Must be finite.</param>
    /// <param name="tolerance">Difference that still counts as equal. Must be finite and not negative.</param>
    /// <returns>True when a is less than b beyond the tolerance.</returns>
    public static bool LessThan(double a, double b, double tolerance)
    {
        return Difference(a, b, tolerance) < -tolerance;
    }

    /// <summary>
    /// Rounds a non-negative value down to a whole number of steps, for example a volume down
    /// to the symbol's volume step. A value within one billionth of a step below a multiple is
    /// treated as that multiple, so floating-point noise cannot cost a whole step.
    /// </summary>
    /// <param name="value">Value to round. Must be finite and not negative.</param>
    /// <param name="step">Step size. Must be finite and greater than zero.</param>
    /// <returns>The largest multiple of the step that is not above the value.</returns>
    public static double RoundDownToStep(double value, double step)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The value to round must be a finite number that is not negative.");
        }

        if (!double.IsFinite(step) || step <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The step must be a finite number greater than zero.");
        }

        double steps = Math.Floor((value / step) + DefaultTolerance);
        if (!double.IsFinite(steps))
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The step is too small for the value: the number of steps is not finite.");
        }

        return steps * step;
    }

    /// <summary>Converts a price distance to pips. The sign of the distance is kept.</summary>
    /// <param name="priceDistance">Distance in price units. Must be finite.</param>
    /// <param name="pipSize">Size of one pip in price units. Must be finite and greater than zero.</param>
    /// <returns>The distance in pips.</returns>
    public static double ToPips(double priceDistance, double pipSize)
    {
        if (!double.IsFinite(priceDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(priceDistance), "The price distance must be a finite number.");
        }

        if (!double.IsFinite(pipSize) || pipSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(pipSize), "The pip size must be a finite number greater than zero.");
        }

        double pips = priceDistance / pipSize;
        if (!double.IsFinite(pips))
        {
            throw new ArgumentOutOfRangeException(nameof(pipSize), "The pip size is too small for the distance: the result is not finite.");
        }

        return pips;
    }

    private static double Difference(double a, double b, double tolerance)
    {
        if (!double.IsFinite(a))
        {
            throw new ArgumentOutOfRangeException(nameof(a), "Compared values must be finite numbers.");
        }

        if (!double.IsFinite(b))
        {
            throw new ArgumentOutOfRangeException(nameof(b), "Compared values must be finite numbers.");
        }

        if (!double.IsFinite(tolerance) || tolerance < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "The tolerance must be a finite number that is not negative.");
        }

        return a - b;
    }
}
