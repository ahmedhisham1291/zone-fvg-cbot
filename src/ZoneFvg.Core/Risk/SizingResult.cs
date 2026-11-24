namespace ZoneFvg.Core.Risk;

/// <summary>
/// Result of <see cref="PositionSizer.Size(Execution.Ports.AccountSnapshot, TradePlan, Execution.Ports.SymbolInfo, double)"/> (RSK-001, RSK-002, RSK-013): either an allowed volume, or
/// a skip with exactly one reason and the measured value that failed.
/// </summary>
/// <remarks>
/// A skipped result always has <see cref="VolumeUnits"/> and <see cref="RiskMoney"/> equal to
/// zero, so a caller that ignores <see cref="IsAllowed"/> still cannot send a volume.
/// </remarks>
public sealed class SizingResult
{
    private SizingResult(RiskReason reason, double volumeUnits, double riskMoney, double riskAmount, double stopPips, double measured, double limit, string detail)
    {
        Reason = reason;
        VolumeUnits = volumeUnits;
        RiskMoney = riskMoney;
        RiskAmount = riskAmount;
        StopPips = stopPips;
        Measured = measured;
        Limit = limit;
        Detail = detail;
    }

    /// <summary>True when the trade may be placed with <see cref="VolumeUnits"/> (RSK-001).</summary>
    public bool IsAllowed => Reason == RiskReason.None;

    /// <summary>Why the trade was skipped, or <see cref="RiskReason.None"/> when allowed.</summary>
    public RiskReason Reason { get; }

    /// <summary>Volume in units, rounded down to the volume step (RSK-001). Zero when skipped.</summary>
    public double VolumeUnits { get; }

    /// <summary>Money lost at the stop with <see cref="VolumeUnits"/>: volume x stop pips x pip value per unit. Zero when skipped.</summary>
    public double RiskMoney { get; }

    /// <summary>Target risk: balance x risk percent / 100, the percent being RiskPercent (RSK-001) or the ladder percent (RSK-013), or the Recovery amount R itself (RSK-016). Zero when the input was invalid.</summary>
    public double RiskAmount { get; }

    /// <summary>Stop distance in pips. Zero when it could not be computed.</summary>
    public double StopPips { get; }

    /// <summary>The value that failed the check (for example the volume or the computed risk); zero when allowed.</summary>
    public double Measured { get; }

    /// <summary>The limit the measured value was compared with; zero when allowed.</summary>
    public double Limit { get; }

    /// <summary>Short invariant text naming the failing value; empty when allowed.</summary>
    public string Detail { get; }

    /// <summary>
    /// True when the skip is one of the sanity caps of RSK-002 (risk above SanityCapPercent when on, volume above
    /// MaxVolumeUnits, stop or target on the wrong side), for which spec 4.3 requires an alert.
    /// </summary>
    public bool AlertRequired
    {
        get
        {
            switch (Reason)
            {
                case RiskReason.RiskAboveSanityCap:
                case RiskReason.VolumeAboveCap:
                case RiskReason.StopOnWrongSide:
                case RiskReason.TargetOnWrongSide:
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static SizingResult Allowed(double volumeUnits, double riskMoney, double riskAmount, double stopPips)
    {
        return new SizingResult(RiskReason.None, volumeUnits, riskMoney, riskAmount, stopPips, 0.0, 0.0, string.Empty);
    }

    internal static SizingResult Skipped(RiskReason reason, double riskAmount, double stopPips, double measured, double limit, string detail)
    {
        return new SizingResult(reason, 0.0, 0.0, riskAmount, stopPips, measured, limit, detail);
    }
}
