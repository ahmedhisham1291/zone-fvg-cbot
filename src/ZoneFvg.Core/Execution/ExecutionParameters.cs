using System;
using System.Globalization;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// The execution parameters of spec sections 3.6, 5 and 7 with their documented defaults
/// (EXE-002 to EXE-006, EXE-013, EXE-015). Instances are immutable; use a <c>with</c> expression
/// to derive a changed copy.
/// </summary>
public sealed record ExecutionParameters
{
    /// <summary>
    /// Shortest life an entry order may have when it is placed, in minutes (EXE-015, spec 6.1:
    /// "if that leaves under 15 minutes, the signal is skipped"). Fixed by the spec; not a parameter.
    /// </summary>
    public const int MinOrderLifeMinutes = 15;

    /// <summary>
    /// Largest age of a signal, in minutes, measured from its confirmation close to server time:
    /// one M15 bar (EXE-001). Fixed by the spec; not a parameter.
    /// </summary>
    public const int MaxSignalAgeMinutes = 15;

    /// <summary>Start of the trading window, UTC time of day, included (EXE-002). Default 07:00.</summary>
    public TimeSpan SessionStartUtc { get; init; } = TimeSpan.FromHours(7.0);

    /// <summary>End of the trading window, UTC time of day, excluded (EXE-002). Default 17:00.</summary>
    public TimeSpan SessionEndUtc { get; init; } = TimeSpan.FromHours(17.0);

    /// <summary>On Fridays no entry at or after this UTC time of day (EXE-002). Default 12:00.</summary>
    public TimeSpan FridayNoEntryUtc { get; init; } = TimeSpan.FromHours(12.0);

    /// <summary>On Fridays, at or after this UTC time of day, positions are closed and entry orders cancelled (EXE-013). Default 20:00.</summary>
    public TimeSpan FridayCloseUtc { get; init; } = TimeSpan.FromHours(20.0);

    /// <summary>Largest spread in pips at which an order is placed (EXE-002). Default 1.5.</summary>
    public double MaxSpreadPips { get; init; } = 1.5;

    /// <summary>Smallest stop distance in pips (EXE-003). Default 12.</summary>
    public double MinStopPips { get; init; } = 12.0;

    /// <summary>Largest stop distance in multiples of the signal's ATR(H4) (EXE-003). Default 2.0.</summary>
    public double MaxStopAtr { get; init; } = 2.0;

    /// <summary>Largest share of the stop distance that spread plus round-trip commission may take (EXE-004). Default 0.10.</summary>
    public double CostToRiskMax { get; init; } = 0.10;

    /// <summary>Smallest gap height in multiples of the current spread (EXE-005). Default 3.</summary>
    public double MinFvgSpreadMult { get; init; } = 3.0;

    /// <summary>Clear space to an opposing zone's proximal line, in multiples of the stop distance (EXE-006). Default 1.5.</summary>
    public double MinRoomR { get; init; } = 1.5;

    /// <summary>Life of an entry order in M15 bars from placement, before news shortening (EXE-015). Default 8.</summary>
    public int EntryExpiryBars { get; init; } = 8;

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in the order the
    /// parameters are declared. The message names the parameter, its allowed range and the
    /// rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequireTimeOfDay(SessionStartUtc, nameof(SessionStartUtc));
        RequireTimeOfDay(SessionEndUtc, nameof(SessionEndUtc));
        if (SessionEndUtc <= SessionStartUtc)
        {
            throw OutOfRange(nameof(SessionEndUtc), "must be after SessionStartUtc", Text(SessionEndUtc));
        }

        RequireTimeOfDay(FridayNoEntryUtc, nameof(FridayNoEntryUtc));
        RequireTimeOfDay(FridayCloseUtc, nameof(FridayCloseUtc));
        RequirePositive(MaxSpreadPips, nameof(MaxSpreadPips));
        RequirePositive(MinStopPips, nameof(MinStopPips));
        RequirePositive(MaxStopAtr, nameof(MaxStopAtr));
        if (!double.IsFinite(CostToRiskMax) || CostToRiskMax <= 0.0 || CostToRiskMax > 1.0)
        {
            throw OutOfRange(nameof(CostToRiskMax), "must be greater than 0 and at most 1", Text(CostToRiskMax));
        }

        RequirePositive(MinFvgSpreadMult, nameof(MinFvgSpreadMult));
        RequirePositive(MinRoomR, nameof(MinRoomR));
        if (EntryExpiryBars < 1 || EntryExpiryBars > 96)
        {
            throw OutOfRange(nameof(EntryExpiryBars), "must be between 1 and 96", EntryExpiryBars.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void RequireTimeOfDay(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromHours(24.0))
        {
            throw OutOfRange(name, "must be a UTC time of day from 00:00 up to but not including 24:00", Text(value));
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
