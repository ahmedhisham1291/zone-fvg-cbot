using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// The 8-character parameter hash written into every order comment (EXE-008): the first 8
/// lowercase hex characters of the SHA-256 of <see cref="Canonical"/>, encoded as UTF-8. A trade
/// placed under other parameters carries another hash, so it can be recognised after a restart.
/// </summary>
public static class ParameterHash
{
    /// <summary>Number of hex characters of the hash (EXE-008).</summary>
    public const int Length = 8;

    /// <summary>
    /// The canonical text of all signal and execution parameters, in this fixed order, each as
    /// <c>Name=value;</c>: BiasTimeFrame, ZoneTimeFrame, BiasEmaPeriod, AtrPeriod, SwingStrength,
    /// ImpulseAtrMult, MaxImpulseBars, MaxBaseBars, RequireBos, MinZoneAtr, MaxZoneAtr,
    /// MaxZoneAgeBars, ConfirmWindowBars, MinFvgAtr, EntryLevelPct, StopBufferAtr, RewardRisk,
    /// SessionStartUtc, SessionEndUtc, FridayNoEntryUtc, FridayCloseUtc, MaxSpreadPips,
    /// MinStopPips, MaxStopAtr, CostToRiskMax, MinFvgSpreadMult, MinRoomR, EntryExpiryBars.
    /// Numbers use the invariant round-trip format ("R"), times of day the invariant "c" format
    /// (for example 07:00:00), booleans "true" or "false", timeframes their names.
    /// With breakout parameters (Breakout mode, spec v0.10) the text is followed by EntryMode=Breakout,
    /// BreakoutEntryBufferAtr, BreakoutStopAtr, BreakoutWindowStartUtc and BreakoutWindowEndUtc in
    /// the same formats (EXE-018); without them it is exactly the text of v0.9. After the breakout
    /// part come TrendSlowEmaPeriod (SIG-028) when it is not 0 and BreakoutLastConfirmUtc (SIG-029)
    /// when it differs from the window end, so that parameters with both filters off keep the hash
    /// they had before 23 November 2026.
    /// With session range parameters (SessionRange mode, spec 3.8) the text ends with RangeStartNy,
    /// RangeEndNy, RangeLastEntryNy, RangeExitNy, RangeMinWidthAtr and RangeStopAtr in the same
    /// formats (SIG-034); without them nothing is appended, so the hashes of the other modes do not
    /// change. After RangeStopAtr comes RangeTrendFastEma (SIG-042) when it is not 0, so that
    /// session range parameters with the direction of spec 3.1 keep the hash they had before
    /// 27 November 2026 (3f0f30a3 for the defaults).
    /// With Choch parameters (the Choch entry model of ZoneFvg mode, REC-020) the text ends, after
    /// every field above, with EntryModel=Choch, RequireSweep, ChochWindowBars, ChochUseBias,
    /// ChochStopAtDistal, ChochTargetBufferAtr, ChochMinStopPips and MinRewardRisk in the same
    /// formats; without them nothing is appended, so the hash of the Fvg model does not change.
    /// With Overnight parameters (Overnight mode, spec 3.10, SIG-043) the text ends, after every field
    /// above, with NightEntryNy, NightLatestNy, NightExitNy, NightEntryBufferAtr, NightStopAtr,
    /// NightTargetR and NightOrderMinutes in the same formats and with no field for the mode, as in
    /// SessionRange mode; without them nothing is appended, so no other mode's hash changes. The
    /// default is <c>c13379a6</c>.
    /// SessionEndUtc 24:00 (EXE-029) is written <c>1.00:00:00</c>.
    /// With a symbol profile (EXE-032, spec 7.1) an automatic MaxSpreadPips, MinStopPips or
    /// ChochMinStopPips is written in its usual place as <c>auto:</c>, the profile's value and its
    /// unit (for example <c>auto:0.4USD</c> for gold, <c>auto:1.5pips</c> for a currency pair) in place
    /// of the number, so the hash does not depend on the pip size the broker reports. Nothing is
    /// appended. Values set by hand, profile None and a null profile are written as before.
    /// </summary>
    /// <param name="signal">Signal parameters.</param>
    /// <param name="execution">Execution parameters.</param>
    /// <param name="breakout">Breakout parameters in Breakout mode, or null in the other modes.</param>
    /// <param name="sessionRange">Session range parameters in SessionRange mode, or null in the other modes.</param>
    /// <param name="choch">Choch parameters in the Choch model, or null in the other models and modes.</param>
    /// <param name="symbolProfile">The symbol profile of the instance (EXE-032), or null.</param>
    /// <param name="overnight">Overnight parameters in Overnight mode (SIG-043), or null in the other modes.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signal"/> or <paramref name="execution"/> is null.</exception>
    public static string Canonical(SignalParameters signal, ExecutionParameters execution, BreakoutParameters? breakout = null, SessionRangeParameters? sessionRange = null, ChochParameters? choch = null, SymbolProfileResult? symbolProfile = null, OvernightParameters? overnight = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(execution);
        var text = new StringBuilder();
        Add(text, "BiasTimeFrame", signal.BiasTimeFrame.ToString());
        Add(text, "ZoneTimeFrame", signal.ZoneTimeFrame.ToString());
        Add(text, "BiasEmaPeriod", signal.BiasEmaPeriod);
        Add(text, "AtrPeriod", signal.AtrPeriod);
        Add(text, "SwingStrength", signal.SwingStrength);
        Add(text, "ImpulseAtrMult", signal.ImpulseAtrMult);
        Add(text, "MaxImpulseBars", signal.MaxImpulseBars);
        Add(text, "MaxBaseBars", signal.MaxBaseBars);
        Add(text, "RequireBos", signal.RequireBos ? "true" : "false");
        Add(text, "MinZoneAtr", signal.MinZoneAtr);
        Add(text, "MaxZoneAtr", signal.MaxZoneAtr);
        Add(text, "MaxZoneAgeBars", signal.MaxZoneAgeBars);
        Add(text, "ConfirmWindowBars", signal.ConfirmWindowBars);
        Add(text, "MinFvgAtr", signal.MinFvgAtr);
        Add(text, "EntryLevelPct", signal.EntryLevelPct);
        Add(text, "StopBufferAtr", signal.StopBufferAtr);
        Add(text, "RewardRisk", signal.RewardRisk);
        Add(text, "SessionStartUtc", execution.SessionStartUtc);
        Add(text, "SessionEndUtc", execution.SessionEndUtc);
        Add(text, "FridayNoEntryUtc", execution.FridayNoEntryUtc);
        Add(text, "FridayCloseUtc", execution.FridayCloseUtc);
        AddProfiled(text, SymbolProfileValues.MaxSpreadPips, execution.MaxSpreadPips, symbolProfile);
        AddProfiled(text, SymbolProfileValues.MinStopPips, execution.MinStopPips, symbolProfile);
        Add(text, "MaxStopAtr", execution.MaxStopAtr);
        Add(text, "CostToRiskMax", execution.CostToRiskMax);
        Add(text, "MinFvgSpreadMult", execution.MinFvgSpreadMult);
        Add(text, "MinRoomR", execution.MinRoomR);
        Add(text, "EntryExpiryBars", execution.EntryExpiryBars);
        if (breakout != null)
        {
            Add(text, "EntryMode", "Breakout");
            Add(text, "BreakoutEntryBufferAtr", breakout.EntryBufferAtr);
            Add(text, "BreakoutStopAtr", breakout.StopAtrMult);
            Add(text, "BreakoutWindowStartUtc", breakout.WindowStartUtc);
            Add(text, "BreakoutWindowEndUtc", breakout.WindowEndUtc);
            if (breakout.TrendSlowEmaPeriod != 0)
            {
                Add(text, "TrendSlowEmaPeriod", breakout.TrendSlowEmaPeriod);
            }

            if (breakout.LastConfirmUtc != breakout.WindowEndUtc)
            {
                Add(text, "BreakoutLastConfirmUtc", breakout.LastConfirmUtc);
            }
        }

        if (sessionRange != null)
        {
            // SIG-034: the names of spec section 7, which differ from the core's for three of them.
            Add(text, "RangeStartNy", sessionRange.RangeStartNy);
            Add(text, "RangeEndNy", sessionRange.RangeEndNy);
            Add(text, "RangeLastEntryNy", sessionRange.LastEntryNy);
            Add(text, "RangeExitNy", sessionRange.ExitNy);
            Add(text, "RangeMinWidthAtr", sessionRange.MinWidthAtr);
            Add(text, "RangeStopAtr", sessionRange.StopAtrMult);

            // SIG-042, row AY: only when it is on, so that a configuration with 0 keeps its hash (3f0f30a3).
            if (sessionRange.RangeTrendFastEma != 0)
            {
                Add(text, "RangeTrendFastEma", sessionRange.RangeTrendFastEma);
            }
        }

        if (choch != null)
        {
            // REC-020: after every existing field, and only in the Choch model.
            Add(text, "EntryModel", "Choch");
            Add(text, "RequireSweep", choch.RequireSweep ? "true" : "false");
            Add(text, "ChochWindowBars", choch.ChochWindowBars);
            Add(text, "ChochUseBias", choch.ChochUseBias ? "true" : "false");
            Add(text, "ChochStopAtDistal", choch.ChochStopAtDistal ? "true" : "false");
            Add(text, "ChochTargetBufferAtr", choch.ChochTargetBufferAtr);
            AddProfiled(text, SymbolProfileValues.ChochMinStopPips, choch.ChochMinStopPips, symbolProfile);
            Add(text, "MinRewardRisk", choch.MinRewardRisk);
        }

        if (overnight != null)
        {
            // SIG-043: after every existing field, and only in Overnight mode.
            Add(text, "NightEntryNy", overnight.NightEntryNy);
            Add(text, "NightLatestNy", overnight.NightLatestNy);
            Add(text, "NightExitNy", overnight.NightExitNy);
            Add(text, "NightEntryBufferAtr", overnight.NightEntryBufferAtr);
            Add(text, "NightStopAtr", overnight.NightStopAtr);
            Add(text, "NightTargetR", overnight.NightTargetR);
            Add(text, "NightOrderMinutes", overnight.NightOrderMinutes);
        }

        return text.ToString();
    }

    /// <summary>The 8-character hash of the parameters (EXE-008, EXE-018, SIG-034, SIG-042, SIG-043, REC-020).</summary>
    /// <param name="signal">Signal parameters.</param>
    /// <param name="execution">Execution parameters.</param>
    /// <param name="breakout">Breakout parameters in Breakout mode, or null in the other modes.</param>
    /// <param name="sessionRange">Session range parameters in SessionRange mode, or null in the other modes.</param>
    /// <param name="choch">Choch parameters in the Choch model, or null in the other models and modes (REC-020).</param>
    /// <param name="symbolProfile">The symbol profile of the instance (EXE-032), or null.</param>
    /// <param name="overnight">Overnight parameters in Overnight mode (SIG-043), or null in the other modes.</param>
    /// <returns>8 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signal"/> or <paramref name="execution"/> is null.</exception>
    public static string Compute(SignalParameters signal, ExecutionParameters execution, BreakoutParameters? breakout = null, SessionRangeParameters? sessionRange = null, ChochParameters? choch = null, SymbolProfileResult? symbolProfile = null, OvernightParameters? overnight = null)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(signal, execution, breakout, sessionRange, choch, symbolProfile, overnight)));
        var hex = new StringBuilder(Length);
        for (int i = 0; i < Length / 2; i++)
        {
            hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static void Add(StringBuilder text, string name, string value)
    {
        text.Append(name).Append('=').Append(value).Append(';');
    }

    // EXE-032: an automatic value as its profile text, any other value as the number.
    private static void AddProfiled(StringBuilder text, string name, double value, SymbolProfileResult? symbolProfile)
    {
        string? automatic = symbolProfile?.HashText(name);
        if (automatic == null)
        {
            Add(text, name, value);
            return;
        }

        Add(text, name, automatic);
    }

    private static void Add(StringBuilder text, string name, int value)
    {
        Add(text, name, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Add(StringBuilder text, string name, double value)
    {
        Add(text, name, value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void Add(StringBuilder text, string name, TimeSpan value)
    {
        Add(text, name, value.ToString("c", CultureInfo.InvariantCulture));
    }
}
