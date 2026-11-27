using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// Result of <see cref="RiskEngine.CheckNewTrade"/> (RSK-003 to RSK-006, RSK-010): allowed, or
/// the first failing reason with the measured value and the limit it was compared with.
/// </summary>
public sealed class RiskCheckResult
{
    internal RiskCheckResult(RiskReason reason, double measured, double limit, string detail)
    {
        Reason = reason;
        Measured = measured;
        Limit = limit;
        Detail = detail;
    }

    /// <summary>True when every check passed.</summary>
    public bool IsAllowed => Reason == RiskReason.None;

    /// <summary>The first failing check, or <see cref="RiskReason.None"/>.</summary>
    public RiskReason Reason { get; }

    /// <summary>The measured value that failed (money, or a count); zero when allowed.</summary>
    public double Measured { get; }

    /// <summary>The limit the measured value was compared with; zero when allowed.</summary>
    public double Limit { get; }

    /// <summary>Short invariant text naming what failed, for example <c>USD short</c>; empty when allowed.</summary>
    public string Detail { get; }
}

/// <summary>
/// Money lost if every bot position and pending order were closed at its stop, without
/// commission (RSK-004), and the bot trades that have no stop loss.
/// </summary>
public sealed class OpenRiskReport
{
    internal OpenRiskReport(double riskMoney, IReadOnlyList<long> positionIdsWithoutStop, IReadOnlyList<long> orderIdsWithoutStop)
    {
        RiskMoney = riskMoney;
        PositionIdsWithoutStop = positionIdsWithoutStop;
        OrderIdsWithoutStop = orderIdsWithoutStop;
    }

    /// <summary>Total risk at stop in the account currency. A trade without a stop counts with its full sizing-rule risk.</summary>
    public double RiskMoney { get; }

    /// <summary>Ids of bot positions without a stop loss, ascending. Each is a violation.</summary>
    public IReadOnlyList<long> PositionIdsWithoutStop { get; }

    /// <summary>Ids of bot pending orders without a stop loss, ascending. Each is a violation.</summary>
    public IReadOnlyList<long> OrderIdsWithoutStop { get; }

    /// <summary>True when any bot position or pending order has no stop loss.</summary>
    public bool HasViolation => PositionIdsWithoutStop.Count > 0 || OrderIdsWithoutStop.Count > 0;
}

/// <summary>Decision of the drawdown kill switch (RSK-008).</summary>
public sealed class KillSwitchDecision
{
    internal KillSwitchDecision(bool halt, double peak, double equity, double drawdownMoney, double limitMoney)
    {
        Halt = halt;
        Peak = peak;
        Equity = equity;
        DrawdownMoney = drawdownMoney;
        LimitMoney = limitMoney;
    }

    /// <summary>True when the bot must enter Halted: (Peak - equity) / Peak &gt;= MaxDrawdownPercent, or Peak is not positive.</summary>
    public bool Halt { get; }

    /// <summary>Highest end-of-trade balance since RiskEpochDate, not less than the balance at RiskEpochDate.</summary>
    public double Peak { get; }

    /// <summary>Current equity.</summary>
    public double Equity { get; }

    /// <summary>Peak minus equity.</summary>
    public double DrawdownMoney { get; }

    /// <summary>Peak x MaxDrawdownPercent / 100.</summary>
    public double LimitMoney { get; }
}

/// <summary>Action decided by the FTMO emergency guard (RSK-009).</summary>
public enum EmergencyAction
{
    /// <summary>Equity is above both floors.</summary>
    None,

    /// <summary>Close all positions, cancel orders and halt.</summary>
    CloseAllAndHalt,
}

/// <summary>Decision of the FTMO emergency guard (RSK-009), with the floor or floors that were reached.</summary>
public sealed class EmergencyDecision
{
    internal EmergencyDecision(bool dayStartBalanceKnown, bool dailyFloorReached, bool overallFloorReached, double equity, double dailyFloor, double overallFloor, FtmoMaxLossMode maxLossMode, double peak)
    {
        DayStartBalanceKnown = dayStartBalanceKnown;
        DailyFloorReached = dailyFloorReached;
        OverallFloorReached = overallFloorReached;
        Equity = equity;
        DailyFloor = dailyFloor;
        OverallFloor = overallFloor;
        MaxLossMode = maxLossMode;
        Peak = peak;
    }

    /// <summary>The FtmoMaxLossMode the decision was made in (RSK-021).</summary>
    public FtmoMaxLossMode MaxLossMode { get; }

    /// <summary>
    /// P of RSK-021: the larger of InitialCapital and the kill switch's peak balance, from which the
    /// trailing overall floor was placed; NaN in static mode and when P could not be computed.
    /// </summary>
    public double Peak { get; }

    /// <summary>
    /// True when the overall floor is the trailing one, P - (FtmoMaxLossPercent -
    /// FtmoGuardBufferPercent) x InitialCapital / 100 (RSK-021); false when it is the static floor
    /// of RSK-009, in static mode or because P could not be computed.
    /// </summary>
    public bool TrailingFloorUsed => !double.IsNaN(Peak);

    /// <summary><see cref="EmergencyAction.CloseAllAndHalt"/> when either floor is reached, otherwise <see cref="EmergencyAction.None"/>.</summary>
    public EmergencyAction Action => DailyFloorReached || OverallFloorReached ? EmergencyAction.CloseAllAndHalt : EmergencyAction.None;

    /// <summary>
    /// False when M could not be computed from the broker data; then only the static floor was
    /// checked, <see cref="DailyFloor"/> is NaN and <see cref="DailyFloorReached"/> is false.
    /// </summary>
    public bool DayStartBalanceKnown { get; }

    /// <summary>True when equity is at or below <see cref="DailyFloor"/>.</summary>
    public bool DailyFloorReached { get; }

    /// <summary>True when equity is at or below <see cref="OverallFloor"/>.</summary>
    public bool OverallFloorReached { get; }

    /// <summary>Current equity.</summary>
    public double Equity { get; }

    /// <summary>M - (FtmoDailyLossPercent - FtmoGuardBufferPercent) x InitialCapital / 100; NaN when M is unknown.</summary>
    public double DailyFloor { get; }

    /// <summary>
    /// InitialCapital - (FtmoMaxLossPercent - FtmoGuardBufferPercent) x InitialCapital / 100; with the
    /// trailing floor of RSK-021 (<see cref="TrailingFloorUsed"/>) P in place of the first InitialCapital.
    /// </summary>
    public double OverallFloor { get; }
}

/// <summary>State of the daily risk budget without a new trade (RSK-006).</summary>
public sealed class DailyBudgetStatus
{
    internal DailyBudgetStatus(double dayStartBalance, double worstCaseEquity, double limit)
    {
        DayStartBalance = dayStartBalance;
        WorstCaseEquity = worstCaseEquity;
        Used = dayStartBalance - worstCaseEquity;
        Limit = limit;
        Exhausted = PriceMath.GreaterThan(Used, limit, PriceMath.DefaultTolerance);
    }

    /// <summary>
    /// True when M - W is beyond the budget (above it by more than the comparison tolerance).
    /// Exactly the budget is not exhausted, matching the "M - W &lt;= budget" rule for new trades.
    /// </summary>
    public bool Exhausted { get; }

    /// <summary>M: the balance at 00:00 CE(S)T (RSK-007).</summary>
    public double DayStartBalance { get; }

    /// <summary>W: worst-case equity if every open bot position and pending order closed at its stop, commission included.</summary>
    public double WorstCaseEquity { get; }

    /// <summary>M - W.</summary>
    public double Used { get; }

    /// <summary>DailyLossLimitPercent x InitialCapital / 100.</summary>
    public double Limit { get; }
}

/// <summary>
/// State of the monthly loss limit (RSK-020): S, the balance at 00:00 CE(S)T on the first day of
/// the current CE(S)T calendar month rebuilt from history, the current balance, the closed loss
/// S - balance, the limit MonthlyLossLimitPercent x InitialCapital / 100 and whether it is reached.
/// </summary>
public sealed class MonthlyLossStatus
{
    internal MonthlyLossStatus(DateTime monthStartUtc, double monthStartBalance, double balance, double limit)
    {
        MonthStartUtc = monthStartUtc;
        MonthStartBalance = monthStartBalance;
        Balance = balance;
        Loss = monthStartBalance - balance;
        Limit = limit;

        // Off (0) is never reached. On: equal counts (PriceMath tolerance), and a not-positive S counts as reached (fail closed).
        Reached = limit > 0.0 && (!(monthStartBalance > 0.0) || !PriceMath.LessThan(Loss, limit, PriceMath.DefaultTolerance));
    }

    /// <summary>00:00 CE(S)T on the first day of the current CE(S)T month, kind UTC.</summary>
    public DateTime MonthStartUtc { get; }

    /// <summary>S: the balance at <see cref="MonthStartUtc"/> (RSK-007's rule applied to the month).</summary>
    public double MonthStartBalance { get; }

    /// <summary>The current account balance.</summary>
    public double Balance { get; }

    /// <summary>S - balance: the closed loss of the month (negative for a profit).</summary>
    public double Loss { get; }

    /// <summary>MonthlyLossLimitPercent x InitialCapital / 100; 0 when the limit is off.</summary>
    public double Limit { get; }

    /// <summary>True when the limit is on and the loss is at least the limit, or S is not positive.</summary>
    public bool Reached { get; }
}
