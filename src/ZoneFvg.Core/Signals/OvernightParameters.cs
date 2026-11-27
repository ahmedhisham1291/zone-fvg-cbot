using System;
using ZoneFvg.Core.Domain;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The parameters of the Overnight entry mode (spec section 3.10 and 7, SIG-043) with their
/// documented defaults. The three times are New York times of day, read in the order of the
/// trading day (SIG-030). The property names are the cBot's and the spec's, so a refusal names
/// the parameter as cTrader shows it. Instances are immutable; use a <c>with</c> expression to
/// derive a changed copy.
/// </summary>
public sealed record OvernightParameters
{
    /// <summary>Smallest value of <see cref="NightOrderMinutes"/> (SIG-043).</summary>
    public const int MinOrderMinutes = 1;

    /// <summary>Largest value of <see cref="NightOrderMinutes"/> (SIG-043).</summary>
    public const int MaxOrderMinutes = 180;

    /// <summary>
    /// SIG-043: the evaluation bar is the first closed M15 bar of the trading day whose close time
    /// is at or after this New York time of day. Default 18:15.
    /// </summary>
    public TimeSpan NightEntryNy { get; init; } = new TimeSpan(18, 15, 0);

    /// <summary>
    /// SIG-043: the evaluation bar must close before this New York time of day; without such a bar
    /// the trading day gives no setup. Default 19:00.
    /// </summary>
    public TimeSpan NightLatestNy { get; init; } = TimeSpan.FromHours(19.0);

    /// <summary>SIG-043, EXE-033: an open Overnight position is closed at this New York time of day. Default 02:45.</summary>
    public TimeSpan NightExitNy { get; init; } = new TimeSpan(2, 45, 0);

    /// <summary>SIG-043: the buy stop lies this many ATR(M15) above the evaluation bar's close plus the spread. Default 0.03.</summary>
    public double NightEntryBufferAtr { get; init; } = 0.03;

    /// <summary>SIG-043: stop distance below the entry, in ATR(D1). Default 1.0.</summary>
    public double NightStopAtr { get; init; } = 1.0;

    /// <summary>SIG-043: safety target above the entry, in multiples of the stop distance. Default 5.0.</summary>
    public double NightTargetR { get; init; } = 5.0;

    /// <summary>SIG-043: life of the buy stop after the close of the evaluation bar, in minutes. Default 60.</summary>
    public int NightOrderMinutes { get; init; } = 60;

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in this order
    /// (SIG-043): NightEntryNy, NightLatestNy and NightExitNy each a time of day from 00:00 up to
    /// but not including 24:00; then, in the order of the trading day (SIG-030), NightEntryNy
    /// before NightLatestNy and NightLatestNy before NightExitNy; NightEntryBufferAtr,
    /// NightStopAtr and NightTargetR each finite and greater than zero; NightOrderMinutes from
    /// <see cref="MinOrderMinutes"/> to <see cref="MaxOrderMinutes"/>. The message names the
    /// parameter, its rule and the rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequireTimeOfDay(NightEntryNy, nameof(NightEntryNy));
        RequireTimeOfDay(NightLatestNy, nameof(NightLatestNy));
        RequireTimeOfDay(NightExitNy, nameof(NightExitNy));

        TimeSpan entry = NewYorkClock.PlaceInTradingDay(NightEntryNy);
        TimeSpan latest = NewYorkClock.PlaceInTradingDay(NightLatestNy);
        TimeSpan exit = NewYorkClock.PlaceInTradingDay(NightExitNy);
        if (latest <= entry)
        {
            throw OutOfRange(nameof(NightLatestNy), "must be after NightEntryNy (" + Text(NightEntryNy) + ") in the order of the trading day", Text(NightLatestNy));
        }

        if (exit <= latest)
        {
            throw OutOfRange(nameof(NightExitNy), "must be after NightLatestNy (" + Text(NightLatestNy) + ") in the order of the trading day", Text(NightExitNy));
        }

        RequirePositive(NightEntryBufferAtr, nameof(NightEntryBufferAtr));
        RequirePositive(NightStopAtr, nameof(NightStopAtr));
        RequirePositive(NightTargetR, nameof(NightTargetR));
        if (NightOrderMinutes < MinOrderMinutes || NightOrderMinutes > MaxOrderMinutes)
        {
            throw OutOfRange(nameof(NightOrderMinutes), "must be from " + Text(MinOrderMinutes) + " to " + Text(MaxOrderMinutes), Text(NightOrderMinutes));
        }
    }

    private static void RequireTimeOfDay(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromHours(24.0))
        {
            throw OutOfRange(name, "must be a time of day from 00:00 up to but not including 24:00", Text(value));
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
