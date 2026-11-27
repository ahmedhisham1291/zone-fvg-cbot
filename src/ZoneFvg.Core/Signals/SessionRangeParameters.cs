using System;
using ZoneFvg.Core.Domain;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The parameters of the SessionRange entry mode (spec section 3.8, SIG-031, SIG-032) with their
/// documented defaults. The four times are New York times of day, read in the order of the
/// trading day (SIG-030). Instances are immutable; use a <c>with</c> expression to derive a
/// changed copy.
/// </summary>
public sealed record SessionRangeParameters
{
    /// <summary>Smallest number of range bars for a setup, and of M15 bars in the range window (SIG-031, SIG-032).</summary>
    public const int MinRangeBars = 16;

    private static readonly TimeSpan BarLength = TimeSpan.FromMinutes(15.0);

    /// <summary>Start of the range window as a New York time of day (SIG-031, spec RangeStartNy). Default 19:00.</summary>
    public TimeSpan RangeStartNy { get; init; } = TimeSpan.FromHours(19.0);

    /// <summary>End of the range window as a New York time of day (SIG-031, spec RangeEndNy). Default 03:00.</summary>
    public TimeSpan RangeEndNy { get; init; } = TimeSpan.FromHours(3.0);

    /// <summary>
    /// Latest evaluation time and order expiry as a New York time of day (SIG-031, SIG-032, spec
    /// RangeLastEntryNy). Default 12:00.
    /// </summary>
    public TimeSpan LastEntryNy { get; init; } = TimeSpan.FromHours(12.0);

    /// <summary>Time of the timed exit as a New York time of day (SIG-032, spec RangeExitNy). Default 16:30.</summary>
    public TimeSpan ExitNy { get; init; } = new TimeSpan(16, 30, 0);

    /// <summary>Smallest range width in multiples of ATR(D1) (SIG-031, spec RangeMinWidthAtr). Default 0.5.</summary>
    public double MinWidthAtr { get; init; } = 0.5;

    /// <summary>Stop distance from the entry in multiples of ATR(H4) (SIG-032, spec RangeStopAtr). Default 1.0.</summary>
    public double StopAtrMult { get; init; } = 1.0;

    /// <summary>
    /// Period of the fast D1 EMA that sets the side of the trading day's setup (SIG-042, spec 3.8
    /// and row AY of v0.17, spec RangeTrendFastEma). Above 0: buy when D1 EMA(this) is above D1
    /// EMA(BiasEmaPeriod) on the last closed D1 bar, sell when below, no setup when equal. 0, the
    /// core default, switches it off: the side comes from the bias of spec 3.1 as before. Allowed:
    /// 0 or at least 2 here; that it is below BiasEmaPeriod is checked by
    /// <c>BotParameters.Validate</c>, which sees both values.
    /// </summary>
    public int RangeTrendFastEma { get; init; }

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in this order
    /// (SIG-032): RangeStartNy, RangeEndNy, LastEntryNy and ExitNy each a time of day from 00:00
    /// up to but not including 24:00 on a 15-minute boundary; then, in the order of the trading
    /// day (SIG-030), RangeEndNy after RangeStartNy, LastEntryNy not before RangeEndNy, ExitNy
    /// after LastEntryNy; the window from RangeStartNy to RangeEndNy at least
    /// <see cref="MinRangeBars"/> M15 bars long; MinWidthAtr finite and not negative;
    /// StopAtrMult finite and greater than zero; RangeTrendFastEma 0 or at least 2 (SIG-042). The message names the parameter, its rule and
    /// the rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequireQuarterHour(RangeStartNy, nameof(RangeStartNy));
        RequireQuarterHour(RangeEndNy, nameof(RangeEndNy));
        RequireQuarterHour(LastEntryNy, nameof(LastEntryNy));
        RequireQuarterHour(ExitNy, nameof(ExitNy));

        TimeSpan start = NewYorkClock.PlaceInTradingDay(RangeStartNy);
        TimeSpan end = NewYorkClock.PlaceInTradingDay(RangeEndNy);
        TimeSpan lastEntry = NewYorkClock.PlaceInTradingDay(LastEntryNy);
        TimeSpan exit = NewYorkClock.PlaceInTradingDay(ExitNy);
        if (end <= start)
        {
            throw OutOfRange(nameof(RangeEndNy), "must be after RangeStartNy (" + Text(RangeStartNy) + ") in the order of the trading day", Text(RangeEndNy));
        }

        if (lastEntry < end)
        {
            throw OutOfRange(nameof(LastEntryNy), "must not be before RangeEndNy (" + Text(RangeEndNy) + ") in the order of the trading day", Text(LastEntryNy));
        }

        if (exit <= lastEntry)
        {
            throw OutOfRange(nameof(ExitNy), "must be after LastEntryNy (" + Text(LastEntryNy) + ") in the order of the trading day", Text(ExitNy));
        }

        if (end - start < MinRangeBars * BarLength)
        {
            string rule = "must leave a window of at least " + Text(MinRangeBars) + " M15 bars after RangeStartNy (" + Text(RangeStartNy) + ")";
            throw OutOfRange(nameof(RangeEndNy), rule, Text(RangeEndNy));
        }

        if (!double.IsFinite(MinWidthAtr) || MinWidthAtr < 0.0)
        {
            throw OutOfRange(nameof(MinWidthAtr), "must be a finite number that is not negative", Text(MinWidthAtr));
        }

        if (!double.IsFinite(StopAtrMult) || StopAtrMult <= 0.0)
        {
            throw OutOfRange(nameof(StopAtrMult), "must be a finite number greater than zero", Text(StopAtrMult));
        }

        if (RangeTrendFastEma < 0 || RangeTrendFastEma == 1)
        {
            throw OutOfRange(nameof(RangeTrendFastEma), "must be 0 (off) or at least 2", Text(RangeTrendFastEma));
        }
    }

    private static void RequireQuarterHour(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromHours(24.0) || value.Ticks % BarLength.Ticks != 0)
        {
            throw OutOfRange(name, "must be a time of day from 00:00 up to but not including 24:00 on a 15-minute boundary", Text(value));
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, string rule, string actual)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + " but was " + actual + ".");
    }
}
