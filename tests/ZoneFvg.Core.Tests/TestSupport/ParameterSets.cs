using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>Builds signal parameter sets that differ from the defaults in one named value.</summary>
public static class ParameterSets
{
    /// <summary>
    /// Default parameters with one numeric parameter replaced. Whole-number parameters take the
    /// value converted to int, so pass whole numbers for them.
    /// </summary>
    public static SignalParameters With(string parameter, double value)
    {
        var defaults = new SignalParameters();
        return parameter switch
        {
            "BiasEmaPeriod" => defaults with { BiasEmaPeriod = (int)value },
            "AtrPeriod" => defaults with { AtrPeriod = (int)value },
            "SwingStrength" => defaults with { SwingStrength = (int)value },
            "ImpulseAtrMult" => defaults with { ImpulseAtrMult = value },
            "MaxImpulseBars" => defaults with { MaxImpulseBars = (int)value },
            "MaxBaseBars" => defaults with { MaxBaseBars = (int)value },
            "MinZoneAtr" => defaults with { MinZoneAtr = value },
            "MaxZoneAtr" => defaults with { MaxZoneAtr = value },
            "MaxZoneAgeBars" => defaults with { MaxZoneAgeBars = (int)value },
            "ConfirmWindowBars" => defaults with { ConfirmWindowBars = (int)value },
            "MinFvgAtr" => defaults with { MinFvgAtr = value },
            "EntryLevelPct" => defaults with { EntryLevelPct = value },
            "StopBufferAtr" => defaults with { StopBufferAtr = value },
            "RewardRisk" => defaults with { RewardRisk = value },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown parameter name."),
        };
    }
}
