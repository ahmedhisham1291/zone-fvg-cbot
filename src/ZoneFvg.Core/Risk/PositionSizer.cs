using System;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// Position size per trade (RSK-001) and the sanity caps on it (RSK-002). A pure function of
/// its inputs; the only field is the validated parameter set (RSK-011).
/// </summary>
public sealed class PositionSizer
{
    private readonly RiskParameters _parameters;

    /// <summary>Creates a sizer. The parameters are validated here (RSK-001).</summary>
    /// <param name="parameters">The risk parameters.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public PositionSizer(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
    }

    /// <summary>
    /// Sizes a trade (RSK-001) and applies the sanity caps (RSK-002), in this order, stopping at
    /// the first failure: input check (fail closed), stop on the loss side of entry, target on
    /// the profit side of entry, minimum volume, MaxVolumeUnits, computed risk at most 0.55% of
    /// balance.
    /// </summary>
    /// <remarks>
    /// riskAmount = balance x RiskPercent / 100; volume = riskAmount / (stopPips x pip value per
    /// unit), rounded down to the volume step with <see cref="PriceMath.RoundDownToStep"/>. The
    /// computed risk of RSK-002 is the risk of the rounded volume: volume x stopPips x pip value.
    /// </remarks>
    /// <param name="account">Account values from the broker port; the balance is used.</param>
    /// <param name="plan">Symbol, side, entry, stop and target of the trade.</param>
    /// <param name="symbol">Symbol data from the broker port for the plan's symbol.</param>
    /// <returns>The allowed volume, or a skip with its reason and measured value.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SizingResult Size(AccountSnapshot account, TradePlan plan, SymbolInfo symbol)
    {
        return Size(account, plan, symbol, _parameters.RiskPercent);
    }

    /// <summary>
    /// Sizes a trade like <see cref="Size(AccountSnapshot, TradePlan, SymbolInfo)"/> but with the
    /// given risk percent in place of the fixed RiskPercent: the percent of the next entry from
    /// <see cref="LadderSizer.NextRiskPercent"/> (RSK-013, spec 4.1). The checks and their order
    /// are the same (RSK-001, RSK-002); a risk percent that is not a finite number greater than 0
    /// and at most 100 is an invalid input (fail closed).
    /// </summary>
    /// <param name="account">Account values from the broker port; the balance is used.</param>
    /// <param name="plan">Symbol, side, entry, stop and target of the trade.</param>
    /// <param name="symbol">Symbol data from the broker port for the plan's symbol.</param>
    /// <param name="riskPercent">Risk of the trade in percent of balance.</param>
    /// <returns>The allowed volume, or a skip with its reason and measured value.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SizingResult Size(AccountSnapshot account, TradePlan plan, SymbolInfo symbol, double riskPercent)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(symbol);

        string? invalid = !double.IsFinite(riskPercent) || riskPercent <= 0.0 || riskPercent > 100.0
            ? "risk percent must be greater than 0 and at most 100"
            : InvalidInput(account, plan, symbol);
        if (invalid != null)
        {
            return SizingResult.Skipped(RiskReason.InvalidInput, 0.0, 0.0, 0.0, 0.0, invalid);
        }

        if (!RiskMath.IsOnLossSide(plan.Side, plan.Entry, plan.StopLoss))
        {
            return SizingResult.Skipped(RiskReason.StopOnWrongSide, 0.0, 0.0, plan.StopLoss, plan.Entry, "stop " + Text(plan.StopLoss) + " entry " + Text(plan.Entry));
        }

        if (!RiskMath.IsOnProfitSide(plan.Side, plan.Entry, plan.TakeProfit))
        {
            return SizingResult.Skipped(RiskReason.TargetOnWrongSide, 0.0, 0.0, plan.TakeProfit, plan.Entry, "target " + Text(plan.TakeProfit) + " entry " + Text(plan.Entry));
        }

        double stopPips = PriceMath.ToPips(Math.Abs(plan.Entry - plan.StopLoss), symbol.PipSize);
        double riskAmount = account.Balance * riskPercent / 100.0;
        double rawVolume = riskAmount / (stopPips * symbol.PipValuePerUnit);
        if (!double.IsFinite(rawVolume))
        {
            return SizingResult.Skipped(RiskReason.InvalidInput, riskAmount, stopPips, 0.0, 0.0, "volume is not a finite number");
        }

        double volume = PriceMath.RoundDownToStep(rawVolume, symbol.VolumeStepUnits);
        if (PriceMath.LessThan(volume, symbol.VolumeMinUnits, PriceMath.DefaultTolerance))
        {
            return SizingResult.Skipped(RiskReason.BelowMinimumVolume, riskAmount, stopPips, volume, symbol.VolumeMinUnits, "volume " + Text(volume) + " below minimum " + Text(symbol.VolumeMinUnits));
        }

        if (PriceMath.GreaterThan(volume, _parameters.MaxVolumeUnits, PriceMath.DefaultTolerance))
        {
            return SizingResult.Skipped(RiskReason.VolumeAboveCap, riskAmount, stopPips, volume, _parameters.MaxVolumeUnits, "volume " + Text(volume) + " above MaxVolumeUnits " + Text(_parameters.MaxVolumeUnits));
        }

        double riskMoney = volume * stopPips * symbol.PipValuePerUnit;
        double sanityCap = account.Balance * RiskParameters.MaxComputedRiskPercent / 100.0;
        if (PriceMath.GreaterThan(riskMoney, sanityCap, PriceMath.DefaultTolerance))
        {
            return SizingResult.Skipped(RiskReason.RiskAboveSanityCap, riskAmount, stopPips, riskMoney, sanityCap, "risk " + Text(riskMoney) + " above 0.55% of balance " + Text(sanityCap));
        }

        return SizingResult.Allowed(volume, riskMoney, riskAmount, stopPips);
    }

    private static string? InvalidInput(AccountSnapshot account, TradePlan plan, SymbolInfo symbol)
    {
        if (!RiskMath.IsPositive(account.Balance))
        {
            return "balance must be a finite number greater than zero";
        }

        if (!RiskMath.IsDefined(plan.Side))
        {
            return "side is not Buy or Sell";
        }

        if (!RiskMath.IsPositive(plan.Entry) || !RiskMath.IsPositive(plan.StopLoss) || !RiskMath.IsPositive(plan.TakeProfit))
        {
            return "entry, stop and target must be finite prices greater than zero";
        }

        if (!string.Equals(plan.Symbol, symbol.Name, StringComparison.Ordinal))
        {
            return "plan symbol does not match the symbol data";
        }

        return RiskMath.InvalidSymbolData(symbol);
    }

    private static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
