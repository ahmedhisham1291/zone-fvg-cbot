using System;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The parameters of the Breakout entry mode (spec section 3.7, SIG-022, SIG-024) with their
/// documented defaults. Instances are immutable; use a <c>with</c> expression to derive a
/// changed copy.
/// </summary>
public sealed record BreakoutParameters
{
    /// <summary>Entry buffer beyond the confirming bar, in multiples of ATR(M15) (SIG-024). Default 0.1.</summary>
    public double EntryBufferAtr { get; init; } = 0.1;

    /// <summary>Stop distance from the entry, in multiples of ATR(H4) (SIG-024). Default 1.0.</summary>
    public double StopAtrMult { get; init; } = 1.0;

    /// <summary>
    /// Start of the trading window as a UTC time of day (SIG-022). An M15 bar is checked only when
    /// the time of day of its close is at or after this time. Default 07:00.
    /// </summary>
    public TimeSpan WindowStartUtc { get; init; } = TimeSpan.FromHours(7.0);

    /// <summary>
    /// End of the trading window as a UTC time of day (SIG-022). An M15 bar is checked only when
    /// the time of day of its close is before this time. Default 17:00.
    /// </summary>
    public TimeSpan WindowEndUtc { get; init; } = TimeSpan.FromHours(17.0);

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in this order
    /// (SIG-022, SIG-024): EntryBufferAtr finite and not negative; StopAtrMult finite and greater
    /// than zero; WindowStartUtc and WindowEndUtc each from 00:00 up to but not including 24:00;
    /// WindowEndUtc after WindowStartUtc. The message names the parameter, its rule and the
    /// rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        if (!double.IsFinite(EntryBufferAtr) || EntryBufferAtr < 0.0)
        {
            throw OutOfRange(nameof(EntryBufferAtr), "must be a finite number that is not negative", Text(EntryBufferAtr));
        }

        if (!double.IsFinite(StopAtrMult) || StopAtrMult <= 0.0)
        {
            throw OutOfRange(nameof(StopAtrMult), "must be a finite number greater than zero", Text(StopAtrMult));
        }

        RequireTimeOfDay(WindowStartUtc, nameof(WindowStartUtc));
        RequireTimeOfDay(WindowEndUtc, nameof(WindowEndUtc));
        if (WindowEndUtc <= WindowStartUtc)
        {
            throw OutOfRange(nameof(WindowEndUtc), "must be after WindowStartUtc (" + Text(WindowStartUtc) + ")", Text(WindowEndUtc));
        }
    }

    private static void RequireTimeOfDay(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromHours(24.0))
        {
            throw OutOfRange(name, "must be a time of day from 00:00 up to but not including 24:00", Text(value));
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, string rule, string actual)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + " but was " + actual + ".");
    }
}
