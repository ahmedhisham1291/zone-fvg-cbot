using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// A closed bar (SIG-001). The core is only ever given bars that have finished, so a bar
/// carries nothing that could mark it as still forming.
/// </summary>
/// <remarks>
/// The values are checked when the bar is constructed and cannot be changed afterwards, so an
/// invalid bar cannot exist. Invalid input throws <see cref="ArgumentException"/>.
/// </remarks>
/// <param name="OpenTimeUtc">Open time of the bar in UTC. Its kind must be UTC.</param>
/// <param name="Open">Open price. Must be a finite number.</param>
/// <param name="High">Highest price. Must be finite, not below the low, and not below the open or the close.</param>
/// <param name="Low">Lowest price. Must be finite and not above the open or the close.</param>
/// <param name="Close">Close price. Must be a finite number.</param>
public sealed record Bar(DateTime OpenTimeUtc, double Open, double High, double Low, double Close)
{
    /// <summary>Open time of the bar in UTC.</summary>
    public DateTime OpenTimeUtc { get; } = Validated(OpenTimeUtc, Open, High, Low, Close);

    /// <summary>Open price.</summary>
    public double Open { get; } = Open;

    /// <summary>Highest price of the bar.</summary>
    public double High { get; } = High;

    /// <summary>Lowest price of the bar.</summary>
    public double Low { get; } = Low;

    /// <summary>Close price.</summary>
    public double Close { get; } = Close;

    private static DateTime Validated(DateTime openTimeUtc, double open, double high, double low, double close)
    {
        if (openTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The open time of a bar must be a UTC time.", nameof(OpenTimeUtc));
        }

        RequireFinite(open, nameof(Open));
        RequireFinite(high, nameof(High));
        RequireFinite(low, nameof(Low));
        RequireFinite(close, nameof(Close));

        if (high < low)
        {
            throw new ArgumentException("The high of a bar must not be below its low.", nameof(High));
        }

        if (high < Math.Max(open, close))
        {
            throw new ArgumentException("The high of a bar must not be below its open or its close.", nameof(High));
        }

        if (low > Math.Min(open, close))
        {
            throw new ArgumentException("The low of a bar must not be above its open or its close.", nameof(Low));
        }

        return openTimeUtc;
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("The " + name + " price of a bar must be a finite number.", name);
        }
    }
}
