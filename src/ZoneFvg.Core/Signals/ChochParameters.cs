using System;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The parameters of the Choch entry model of ZoneFvg mode (spec sections 3.9 and 7, SIG-035) with
/// their documented defaults. Each property has the name cTrader shows for it, so a refusal of
/// <see cref="Validate"/> names the parameter as the owner sees it. Instances are immutable; use a
/// <c>with</c> expression to derive a changed copy.
/// </summary>
public sealed record ChochParameters
{
    /// <summary>Smallest ChochWindowBars <see cref="Validate"/> accepts (SIG-035).</summary>
    public const int MinWindowBars = 4;

    /// <summary>Largest ChochWindowBars <see cref="Validate"/> accepts (SIG-035).</summary>
    public const int MaxWindowBars = 96;

    /// <summary>Largest MinRewardRisk <see cref="Validate"/> accepts (SIG-035).</summary>
    public const double MaxMinRewardRisk = 10.0;

    /// <summary>
    /// Whether a change of character counts only after a sweep of the most recent M15 swing low
    /// (buy) or high (sell) since the touch (SIG-038). Default false.
    /// </summary>
    public bool RequireSweep { get; init; }

    /// <summary>
    /// Confirmation window of a touched zone in M15 bars: the touch bar is bar 0 and the window
    /// ends after this bar (SIG-036). Default 32 (8 hours).
    /// </summary>
    public int ChochWindowBars { get; init; } = 32;

    /// <summary>
    /// Whether the D1 bias must match the zone type (SIG-036). Default false: the zone type alone
    /// gives the side.
    /// </summary>
    public bool ChochUseBias { get; init; }

    /// <summary>
    /// Whether the stop is placed beyond the lower (buy) or higher (sell) of the extreme since the
    /// touch and the zone's distal line (SIG-039). Default false: beyond the extreme alone.
    /// </summary>
    public bool ChochStopAtDistal { get; init; }

    /// <summary>
    /// Distance of the target before the opposing level, in multiples of ATR(H4) (SIG-040).
    /// Default 0.1.
    /// </summary>
    public double ChochTargetBufferAtr { get; init; } = 0.1;

    /// <summary>
    /// Smallest stop distance in pips of a Choch signal (SIG-035); the execution layer uses it in
    /// place of MinStopPips (EXE-028). Default 5 (the gold parameter file uses 3).
    /// </summary>
    public double ChochMinStopPips { get; init; } = 5.0;

    /// <summary>
    /// Smallest reward to risk of a Choch signal on its own levels (SIG-035); the execution layer
    /// checks it (EXE-028). Default 2.0, the owner's minimum of 1:2.
    /// </summary>
    public double MinRewardRisk { get; init; } = 2.0;

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in the order the
    /// parameters are declared (SIG-035): ChochWindowBars a whole number from
    /// <see cref="MinWindowBars"/> to <see cref="MaxWindowBars"/>; ChochTargetBufferAtr finite and
    /// from 0 to 1; ChochMinStopPips finite and greater than zero; MinRewardRisk finite and from 1
    /// to <see cref="MaxMinRewardRisk"/>. The message names the parameter, its rule and the rejected
    /// value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        if (ChochWindowBars < MinWindowBars || ChochWindowBars > MaxWindowBars)
        {
            throw OutOfRange(nameof(ChochWindowBars), "must be a whole number from " + Text(MinWindowBars) + " to " + Text(MaxWindowBars), Text(ChochWindowBars));
        }

        if (!double.IsFinite(ChochTargetBufferAtr) || ChochTargetBufferAtr < 0.0 || ChochTargetBufferAtr > 1.0)
        {
            throw OutOfRange(nameof(ChochTargetBufferAtr), "must be a finite number from 0 to 1", Text(ChochTargetBufferAtr));
        }

        if (!double.IsFinite(ChochMinStopPips) || ChochMinStopPips <= 0.0)
        {
            throw OutOfRange(nameof(ChochMinStopPips), "must be a finite number greater than zero", Text(ChochMinStopPips));
        }

        if (!double.IsFinite(MinRewardRisk) || MinRewardRisk < 1.0 || MinRewardRisk > MaxMinRewardRisk)
        {
            throw OutOfRange(nameof(MinRewardRisk), "must be a finite number from 1 to " + Text(MaxMinRewardRisk), Text(MinRewardRisk));
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, string rule, string actual)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + " but was " + actual + ".");
    }
}
