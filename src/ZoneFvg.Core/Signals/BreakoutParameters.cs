using System;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The parameters of the Breakout entry mode (spec section 3.7, SIG-022, SIG-024, SIG-028,
/// SIG-029) with their documented defaults. The defaults of the two filters are the values that
/// switch them off, so <c>new BreakoutParameters()</c> is the rule before 23 November 2026.
/// Instances are immutable; use a <c>with</c> expression to derive a changed copy.
/// </summary>
public sealed record BreakoutParameters
{
    // SIG-029: null until LastConfirmUtc is set; then LastConfirmUtc reads WindowEndUtc (off).
    private readonly TimeSpan? _lastConfirmUtc;

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
    /// Period of the slow D1 EMA of the breakout trend filter (SIG-028). With a value greater than
    /// zero a buy is confirmed only when EMA(BiasEmaPeriod) is above EMA(TrendSlowEmaPeriod) on the
    /// last closed D1 bar, and a sell only when it is below. Default 0: the filter is off.
    /// </summary>
    public int TrendSlowEmaPeriod { get; init; }

    /// <summary>
    /// Latest confirmation time as a UTC time of day (SIG-029): a checked M15 bar whose close time
    /// of day is at or after this time confirms nothing and finishes the reference for that day.
    /// Until it is set it reads <see cref="WindowEndUtc"/>, the value that switches the rule off
    /// (17:00 with the default window).
    /// </summary>
    public TimeSpan LastConfirmUtc
    {
        get => _lastConfirmUtc ?? WindowEndUtc;
        init => _lastConfirmUtc = value;
    }

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in this order
    /// (SIG-022, SIG-024, SIG-028, SIG-029): EntryBufferAtr finite and not negative; StopAtrMult
    /// finite and greater than zero; WindowStartUtc and WindowEndUtc each from 00:00 up to but not
    /// including 24:00; WindowEndUtc after WindowStartUtc; TrendSlowEmaPeriod 0 or from 2 to
    /// <see cref="SignalParameters.MaxBarCount"/>; LastConfirmUtc after WindowStartUtc and not
    /// after WindowEndUtc. The message names the parameter, its rule and the rejected value in
    /// invariant formatting.
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

        if (TrendSlowEmaPeriod != 0 && (TrendSlowEmaPeriod < 2 || TrendSlowEmaPeriod > SignalParameters.MaxBarCount))
        {
            throw OutOfRange(nameof(TrendSlowEmaPeriod), "must be 0 (off) or between 2 and " + Text(SignalParameters.MaxBarCount), Text(TrendSlowEmaPeriod));
        }

        if (LastConfirmUtc <= WindowStartUtc || LastConfirmUtc > WindowEndUtc)
        {
            string rule = "must be after WindowStartUtc (" + Text(WindowStartUtc) + ") and not after WindowEndUtc (" + Text(WindowEndUtc) + ")";
            throw OutOfRange(nameof(LastConfirmUtc), rule, Text(LastConfirmUtc));
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
