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
    /// </summary>
    /// <param name="signal">Signal parameters.</param>
    /// <param name="execution">Execution parameters.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string Canonical(SignalParameters signal, ExecutionParameters execution)
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
        Add(text, "MaxSpreadPips", execution.MaxSpreadPips);
        Add(text, "MinStopPips", execution.MinStopPips);
        Add(text, "MaxStopAtr", execution.MaxStopAtr);
        Add(text, "CostToRiskMax", execution.CostToRiskMax);
        Add(text, "MinFvgSpreadMult", execution.MinFvgSpreadMult);
        Add(text, "MinRoomR", execution.MinRoomR);
        Add(text, "EntryExpiryBars", execution.EntryExpiryBars);
        return text.ToString();
    }

    /// <summary>The 8-character hash of the parameters (EXE-008).</summary>
    /// <param name="signal">Signal parameters.</param>
    /// <param name="execution">Execution parameters.</param>
    /// <returns>8 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string Compute(SignalParameters signal, ExecutionParameters execution)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(signal, execution)));
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
