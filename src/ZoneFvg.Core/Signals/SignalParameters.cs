using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal-layer parameters of spec section 7 with their documented defaults. Instances are
/// immutable; use a <c>with</c> expression to derive a changed copy.
/// </summary>
/// <remarks>
/// Parameters of spec section 7 that need broker data (spread, pips, sessions, risk, news)
/// belong to the execution, risk and news layers and are not held here.
/// </remarks>
public sealed record SignalParameters
{
    /// <summary>
    /// Largest value <see cref="Validate"/> accepts for a parameter that counts bars. The cap keeps
    /// the warm-up requirement (SIG-020) and the bar buffers bounded and free of overflow.
    /// </summary>
    public const int MaxBarCount = 10000;

    /// <summary>Trend filter timeframe (SIG-005). Fixed: only D1 is supported.</summary>
    public TimeFrame BiasTimeFrame { get; init; } = TimeFrame.D1;

    /// <summary>Zone timeframe (SIG-006 onward). Fixed: only H4 is supported.</summary>
    public TimeFrame ZoneTimeFrame { get; init; } = TimeFrame.H4;

    /// <summary>Period of the D1 trend EMA (SIG-005). Default 50.</summary>
    public int BiasEmaPeriod { get; init; } = 50;

    /// <summary>Period of every ATR value (SIG-003). Default 14.</summary>
    public int AtrPeriod { get; init; } = 14;

    /// <summary>Bars required on each side of a swing (SIG-006). Default 3.</summary>
    public int SwingStrength { get; init; } = 3;

    /// <summary>Minimum net move of an impulse leg, in multiples of ATR(H4) (SIG-007). Default 2.0.</summary>
    public double ImpulseAtrMult { get; init; } = 2.0;

    /// <summary>Largest number of bars in an impulse leg (SIG-007). Default 3.</summary>
    public int MaxImpulseBars { get; init; } = 3;

    /// <summary>Largest number of bars in a base (SIG-009). Default 3.</summary>
    public int MaxBaseBars { get; init; } = 3;

    /// <summary>Whether an impulse leg must break structure (SIG-008). Default true.</summary>
    public bool RequireBos { get; init; } = true;

    /// <summary>Smallest zone height, in multiples of ATR(H4) (SIG-011). Default 0.2.</summary>
    public double MinZoneAtr { get; init; } = 0.2;

    /// <summary>Largest zone height, in multiples of ATR(H4) (SIG-011). Default 1.5.</summary>
    public double MaxZoneAtr { get; init; } = 1.5;

    /// <summary>Number of closed H4 bars after which a zone expires (SIG-014). Default 120.</summary>
    public int MaxZoneAgeBars { get; init; } = 120;

    /// <summary>Number of M15 bars allowed for the fair value gap after a touch (SIG-014, SIG-016). Default 16.</summary>
    public int ConfirmWindowBars { get; init; } = 16;

    /// <summary>Smallest fair value gap height, in multiples of ATR(M15) (SIG-016). Default 0.25.</summary>
    public double MinFvgAtr { get; init; } = 0.25;

    /// <summary>Entry depth into the gap in percent, 0 to 100 (SIG-017). Default 50.</summary>
    public double EntryLevelPct { get; init; } = 50.0;

    /// <summary>Stop buffer, in multiples of ATR(M15) (SIG-017). Default 0.1.</summary>
    public double StopBufferAtr { get; init; } = 0.1;

    /// <summary>Target distance in multiples of the stop distance (SIG-017). Default 2.0.</summary>
    public double RewardRisk { get; init; } = 2.0;

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in the order the
    /// parameters are declared. The message names the parameter, its allowed range and the
    /// rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequireTimeFrame(BiasTimeFrame, TimeFrame.D1, nameof(BiasTimeFrame));
        RequireTimeFrame(ZoneTimeFrame, TimeFrame.H4, nameof(ZoneTimeFrame));
        RequireBarCount(BiasEmaPeriod, nameof(BiasEmaPeriod));
        RequireBarCount(AtrPeriod, nameof(AtrPeriod));
        RequireBarCount(SwingStrength, nameof(SwingStrength));
        RequirePositive(ImpulseAtrMult, nameof(ImpulseAtrMult));
        RequireBarCount(MaxImpulseBars, nameof(MaxImpulseBars));
        RequireBarCount(MaxBaseBars, nameof(MaxBaseBars));
        RequirePositive(MinZoneAtr, nameof(MinZoneAtr));
        RequirePositive(MaxZoneAtr, nameof(MaxZoneAtr));
        if (MaxZoneAtr < MinZoneAtr)
        {
            throw OutOfRange(nameof(MaxZoneAtr), "must not be below MinZoneAtr (" + Text(MinZoneAtr) + ")", Text(MaxZoneAtr));
        }

        RequireBarCount(MaxZoneAgeBars, nameof(MaxZoneAgeBars));
        RequireBarCount(ConfirmWindowBars, nameof(ConfirmWindowBars));
        RequirePositive(MinFvgAtr, nameof(MinFvgAtr));
        if (!double.IsFinite(EntryLevelPct) || EntryLevelPct < 0.0 || EntryLevelPct > 100.0)
        {
            throw OutOfRange(nameof(EntryLevelPct), "must be between 0 and 100", Text(EntryLevelPct));
        }

        if (!double.IsFinite(StopBufferAtr) || StopBufferAtr < 0.0)
        {
            throw OutOfRange(nameof(StopBufferAtr), "must be a finite number that is not negative", Text(StopBufferAtr));
        }

        RequirePositive(RewardRisk, nameof(RewardRisk));
    }

    private static void RequireTimeFrame(TimeFrame value, TimeFrame required, string name)
    {
        if (value != required)
        {
            throw OutOfRange(name, "must be " + required, value.ToString());
        }
    }

    private static void RequireBarCount(int value, string name)
    {
        if (value < 1 || value > MaxBarCount)
        {
            throw OutOfRange(name, "must be between 1 and " + Text(MaxBarCount), Text(value));
        }
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw OutOfRange(name, "must be a finite number greater than zero", Text(value));
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, string rule, string actual)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + " but was " + actual + ".");
    }
}

/// <summary>
/// Computes the number of closed bars each timeframe needs before the signal layer is ready
/// (SIG-020). The requirement is derived from the parameters only.
/// </summary>
public static class WarmupCalculator
{
    /// <summary>Number of M15 bars in one H4 bar (240 minutes / 15 minutes).</summary>
    public const int M15BarsPerH4Bar = 16;

    /// <summary>
    /// Bars needed per timeframe (SIG-020):
    /// D1 = 4 x BiasEmaPeriod + 1;
    /// H4 = MaxZoneAgeBars + swing lookback (100) + 2 x SwingStrength + MaxImpulseBars + MaxBaseBars + AtrPeriod + 2;
    /// M15 = (MaxZoneAgeBars + 1) x 16 + ConfirmWindowBars + AtrPeriod + 2.
    /// With the default parameters this is 201 D1, 248 H4 and 1968 M15 bars.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <returns>
    /// The requirement for D1, H4 and M15 as a read-only dictionary. It always enumerates in the
    /// order D1, H4, M15, so nothing can come to depend on an unspecified iteration order.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static IReadOnlyDictionary<TimeFrame, int> For(SignalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        int d1 = (Ema.WindowMultiple * parameters.BiasEmaPeriod) + 1;
        int h4 = parameters.MaxZoneAgeBars + SwingDetector.LookbackBars + (2 * parameters.SwingStrength) + parameters.MaxImpulseBars + parameters.MaxBaseBars + parameters.AtrPeriod + 2;
        int m15 = ((parameters.MaxZoneAgeBars + 1) * M15BarsPerH4Bar) + parameters.ConfirmWindowBars + parameters.AtrPeriod + 2;

        // Sorted by descending enum value, which is D1, H4, M15: a defined enumeration order.
        var bars = new SortedDictionary<TimeFrame, int>(Comparer<TimeFrame>.Create((left, right) => ((int)right).CompareTo((int)left)))
        {
            [TimeFrame.D1] = d1,
            [TimeFrame.H4] = h4,
            [TimeFrame.M15] = m15,
        };
        return new ReadOnlyDictionary<TimeFrame, int>(bars);
    }
}
