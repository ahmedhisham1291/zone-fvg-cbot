using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// Account-level risk limits (RSK-003 to RSK-009, RSK-020, RSK-021) and the entry counts of RSK-010, as pure
/// functions of a <see cref="RiskSnapshot"/> and the parameters (RSK-011). The engine never calls
/// the broker and keeps nothing between calls; its only field is the validated parameter set.
/// </summary>
/// <remarks>
/// A bot position, order or trade is one whose label starts with <see cref="BotLabelPrefix"/>.
/// Money is compared with <see cref="PriceMath"/> and <see cref="PriceMath.DefaultTolerance"/>:
/// a value within one billionth of a limit counts as at the limit.
/// </remarks>
public sealed class RiskEngine
{
    /// <summary>Label prefix of every bot position and order (spec 5.2, WP04 decision).</summary>
    public const string BotLabelPrefix = "ZFVG|";

    private readonly RiskParameters _parameters;

    /// <summary>Creates an engine. The parameters are validated here (RSK-011).</summary>
    /// <param name="parameters">The risk parameters.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public RiskEngine(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
    }

    /// <summary>True when the label marks a bot position, order or trade: it starts with <c>ZFVG|</c> (ordinal).</summary>
    /// <param name="label">The label, possibly null.</param>
    /// <returns>True for a bot label.</returns>
    public static bool IsBotLabel(string? label)
    {
        return label != null && label.StartsWith(BotLabelPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Balance at 00:00 CE(S)T of the current day (RSK-007): current balance minus the net result
    /// of every account trade closed at or after that instant. No stored value is used.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The day-start balance M.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The history does not reach back to the earlier of RiskEpochDate and the day start.</exception>
    public double DayStartBalance(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireHistory(snapshot);
        return DayStart(snapshot);
    }

    /// <summary>
    /// Money lost if every bot position and pending order were stopped out, without commission
    /// (RSK-004). A trade whose stop is in profit counts as zero. A trade without a stop loss
    /// counts with its full sizing-rule risk (balance x RiskPercent / 100) and is reported.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The total and the trades without a stop loss.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Symbol data for a bot trade with a stop loss is missing or invalid.</exception>
    public OpenRiskReport OpenRiskAtStop(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        double sizingRisk = SizingRuleRisk(snapshot);
        double total = 0.0;
        var positionsWithoutStop = new List<long>();
        var ordersWithoutStop = new List<long>();
        foreach (PositionInfo position in snapshot.Positions)
        {
            if (IsBotLabel(position.Label))
            {
                total += TradeRisk(snapshot, position.Id, position.Symbol, position.Side, position.EntryPrice, position.StopLoss, position.VolumeUnits, sizingRisk, positionsWithoutStop);
            }
        }

        foreach (PendingOrderInfo order in snapshot.PendingOrders)
        {
            if (IsBotLabel(order.Label))
            {
                total += TradeRisk(snapshot, order.Id, order.Symbol, order.Side, order.EntryPrice, order.StopLoss, order.VolumeUnits, sizingRisk, ordersWithoutStop);
            }
        }

        return new OpenRiskReport(total, positionsWithoutStop.AsReadOnly(), ordersWithoutStop.AsReadOnly());
    }

    /// <summary>
    /// Checks a new trade against the account limits, in this order, and returns the first
    /// failure: input check, history and symbol data (fail closed), free margin (RSK-003), open
    /// risk (RSK-004, only with OpenRiskCapEnabled), currency exposure (RSK-005, only with
    /// CurrencyCapEnabled), daily risk budget (RSK-006), entries per symbol and per account in the
    /// CE(S)T day (RSK-010), and last the monthly loss limit (RSK-020, only with
    /// MonthlyLossLimitPercent above 0).
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="trade">The sized trade with the broker's margin estimate.</param>
    /// <returns>Allowed, or the first failing reason with the measured value and the limit.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RiskCheckResult CheckNewTrade(RiskSnapshot snapshot, PlannedTrade trade)
    {
        return Check(snapshot, trade, true);
    }

    /// <summary>
    /// <see cref="CheckNewTrade"/> without the daily risk budget (RSK-006), for the backtest mode of
    /// REC-014 (owner decision C of 2026-11-17) only: every other check runs in the same order with
    /// the same values. Live code never calls it; a backtest calls it after
    /// <see cref="CheckNewTrade"/> refused a trade for the budget alone.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="trade">The sized trade with the broker's margin estimate.</param>
    /// <returns>Allowed, or the first failing reason other than the daily budget.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RiskCheckResult CheckNewTradeWithoutDailyBudget(RiskSnapshot snapshot, PlannedTrade trade)
    {
        return Check(snapshot, trade, false);
    }

    private RiskCheckResult Check(RiskSnapshot snapshot, PlannedTrade trade, bool dailyBudget)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(trade);

        RiskCheckResult? failure = CheckInputs(snapshot, trade);
        failure ??= CheckMargin(snapshot, trade);
        if (_parameters.OpenRiskCapEnabled)
        {
            // RSK-004: only when switched on (owner decision W of 2026-11-24); the open risk is still reported by OpenRiskAtStop.
            failure ??= CheckOpenRisk(snapshot, trade);
        }

        if (_parameters.CurrencyCapEnabled)
        {
            // RSK-005, RSK-015: only when switched on (owner decision W of 2026-11-24).
            failure ??= CheckCurrencyExposure(snapshot, trade);
        }

        if (dailyBudget)
        {
            failure ??= CheckDailyBudget(snapshot, trade);
        }

        failure ??= CheckEntryCounts(snapshot, trade);
        if (_parameters.MonthlyLossLimitPercent > 0.0)
        {
            // RSK-020: only when switched on (0 = off), after every check that existed before.
            failure ??= CheckMonthlyLoss(snapshot);
        }

        return failure ?? new RiskCheckResult(RiskReason.None, 0.0, 0.0, string.Empty);
    }

    /// <summary>
    /// Drawdown kill switch (RSK-008). Peak = highest end-of-trade balance since RiskEpochDate,
    /// and not less than the balance at RiskEpochDate (current balance minus the net result of
    /// trades closed at or after the epoch). The epoch starts at 00:00 CE(S)T of RiskEpochDate. Halt when (Peak - equity) / Peak &gt;=
    /// MaxDrawdownPercent, and also when Peak is not positive (fail closed). Recomputing from the
    /// same broker data gives the same decision.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The decision with peak, drawdown and limit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The history does not reach back to the earlier of RiskEpochDate and the day start.</exception>
    public KillSwitchDecision KillSwitch(RiskSnapshot snapshot)
    {
        double peak = PeakBalance(snapshot);
        double equity = snapshot.Account.Equity;
        double limit = peak * _parameters.MaxDrawdownPercent / 100.0;
        double drawdown = peak - equity;
        bool halt = !(peak > 0.0) || !PriceMath.LessThan(drawdown, limit, PriceMath.DefaultTolerance);
        return new KillSwitchDecision(halt, peak, equity, drawdown, limit);
    }

    /// <summary>
    /// The peak balance of the kill switch (RSK-008), also used by the drawdown brake (RSK-019): the
    /// highest end-of-trade balance of the trades closed since 00:00 CE(S)T of RiskEpochDate, and
    /// not less than the balance at that instant (current balance minus the net result of the
    /// trades closed at or after it). <see cref="KillSwitch"/> uses exactly this value.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The peak balance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The history does not reach back to the earlier of RiskEpochDate and the day start.</exception>
    public double PeakBalance(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireHistory(snapshot);
        DateTime epoch = EpochStartUtc();
        double peak = BalanceAt(snapshot, epoch);
        foreach (ClosedTradeInfo trade in snapshot.History)
        {
            if (trade.CloseTimeUtc >= epoch)
            {
                peak = Math.Max(peak, trade.BalanceAfterClose);
            }
        }

        return peak;
    }

    /// <summary>
    /// FTMO emergency guard (RSK-009, RSK-021): CloseAllAndHalt when equity is at or below
    /// M - (FtmoDailyLossPercent - FtmoGuardBufferPercent) x InitialCapital / 100, or at or below the
    /// overall floor. In static mode (RSK-009) the overall floor is
    /// InitialCapital - (FtmoMaxLossPercent - FtmoGuardBufferPercent) x InitialCapital / 100
    /// (which equals InitialCapital x (1 - (FtmoMaxLossPercent - FtmoGuardBufferPercent) / 100));
    /// with defaults: M - 8,000 and 182,000 on 200,000 (RSK-009, RSK-018). With
    /// <see cref="FtmoMaxLossMode.TrailingBalance"/> (RSK-021) it is P - (FtmoMaxLossPercent -
    /// FtmoGuardBufferPercent) x InitialCapital / 100, with P the larger of InitialCapital and
    /// <see cref="PeakBalance"/>; when P cannot be computed (not a positive finite number) the
    /// static floor applies, which is never looser. When the history does not reach back far
    /// enough to compute M, only the static floor is checked and the daily floor is reported as
    /// unknown; new trades are refused in that case by <see cref="CheckNewTrade"/>.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The decision and which floor was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public EmergencyDecision EmergencyGuard(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        double equity = snapshot.Account.Equity;
        if (!HistoryCovers(snapshot))
        {
            return StaticFloorOnly(equity);
        }

        double capital = _parameters.InitialCapital;
        double dailyFloor = DayStart(snapshot) - ((_parameters.FtmoDailyLossPercent - _parameters.FtmoGuardBufferPercent) * capital / 100.0);
        double peak = TrailingPeak(snapshot);
        double overallFloor = double.IsNaN(peak) ? OverallFloor() : OverallFloorBelow(peak);
        bool daily = !PriceMath.GreaterThan(equity, dailyFloor, PriceMath.DefaultTolerance);
        bool overall = !PriceMath.GreaterThan(equity, overallFloor, PriceMath.DefaultTolerance);
        return new EmergencyDecision(true, daily, overall, equity, dailyFloor, overallFloor, _parameters.FtmoMaxLossMode, peak);
    }

    /// <summary>
    /// FTMO emergency guard when no usable <see cref="RiskSnapshot"/> can be built because broker
    /// data is missing or invalid (RSK-009): only the static floor InitialCapital x (1 -
    /// (FtmoMaxLossPercent - FtmoGuardBufferPercent) / 100) is checked, from equity alone. The
    /// caller places no new trade in that case.
    /// </summary>
    /// <param name="equity">Account equity read from the broker port.</param>
    /// <returns>The decision; the daily floor is reported as unknown.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="equity"/> is not a finite number.</exception>
    public EmergencyDecision EmergencyGuardFromEquity(double equity)
    {
        if (!double.IsFinite(equity))
        {
            throw new ArgumentOutOfRangeException(nameof(equity), "The equity must be a finite number.");
        }

        return StaticFloorOnly(equity);
    }

    /// <summary>
    /// State of the daily risk budget without a new trade (RSK-006): M, the worst-case equity W
    /// of the open bot positions and pending orders (commission included) and whether M - W is
    /// beyond DailyLossLimitPercent x InitialCapital / 100.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The budget state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The history does not reach back to the earlier of RiskEpochDate and the day start.</exception>
    /// <exception cref="InvalidOperationException">Symbol data for a bot trade is missing or invalid.</exception>
    public DailyBudgetStatus DailyBudgetExhausted(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireHistory(snapshot);
        return new DailyBudgetStatus(DayStart(snapshot), WorstCaseEquity(snapshot), DailyLimit());
    }

    /// <summary>
    /// State of the monthly loss limit (RSK-020): S = current balance minus the net result (profit,
    /// commission, swap) of every account trade closed at or after 00:00 CE(S)T on the first day of
    /// the current CE(S)T calendar month, as RSK-007 does for the day; nothing stored. The limit is
    /// MonthlyLossLimitPercent x InitialCapital / 100, and is reached when S - balance is at least
    /// the limit (equal counts) or S is not positive. With the limit 0 it is never reached.
    /// </summary>
    /// <param name="snapshot">The snapshot; its history must reach back to the month start.</param>
    /// <returns>The month status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The history does not reach back to the month start.</exception>
    public MonthlyLossStatus MonthlyLoss(RiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DateTime monthStart = CestClock.StartOfCestMonthUtc(snapshot.ServerTimeUtc);
        if (snapshot.HistorySinceUtc > monthStart)
        {
            throw new ArgumentException("The snapshot's history must reach back to " + monthStart.ToString("o", CultureInfo.InvariantCulture) + ".", nameof(snapshot));
        }

        return MonthStatus(snapshot, monthStart);
    }

    private MonthlyLossStatus MonthStatus(RiskSnapshot snapshot, DateTime monthStart)
    {
        double limit = _parameters.InitialCapital * _parameters.MonthlyLossLimitPercent / 100.0;
        return new MonthlyLossStatus(monthStart, BalanceAt(snapshot, monthStart), snapshot.Account.Balance, limit);
    }

    private static double DayStart(RiskSnapshot snapshot)
    {
        return BalanceAt(snapshot, CestClock.StartOfCestDayUtc(snapshot.ServerTimeUtc));
    }

    private static double BalanceAt(RiskSnapshot snapshot, DateTime sinceUtc)
    {
        double net = 0.0;
        foreach (ClosedTradeInfo trade in snapshot.History)
        {
            if (trade.CloseTimeUtc >= sinceUtc)
            {
                net += trade.NetProfit;
            }
        }

        return snapshot.Account.Balance - net;
    }

    private static SymbolInfo Symbol(RiskSnapshot snapshot, string name)
    {
        // Every use of symbol data goes through here, so missing or invalid data always stops the computation.
        string? problem = SymbolProblem(snapshot, name);
        if (problem != null)
        {
            throw new InvalidOperationException("Risk cannot be computed: " + problem + ".");
        }

        return snapshot.FindSymbol(name)!;
    }

    private static double RiskAtStop(Side side, double entry, double stop, double volume, SymbolInfo symbol)
    {
        return Math.Max(0.0, -RiskMath.ResultAt(side, entry, stop, volume, symbol));
    }

    private static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static double TradeRisk(RiskSnapshot snapshot, long id, string symbol, Side side, double entry, double? stop, double volume, double sizingRisk, List<long> withoutStop)
    {
        if (!stop.HasValue)
        {
            withoutStop.Add(id);
            return sizingRisk;
        }

        return RiskAtStop(side, entry, stop.Value, volume, Symbol(snapshot, symbol));
    }

    private double SizingRuleRisk(RiskSnapshot snapshot)
    {
        return snapshot.Account.Balance * _parameters.RiskPercent / 100.0;
    }

    private EmergencyDecision StaticFloorOnly(double equity)
    {
        double overallFloor = OverallFloor();
        bool overall = !PriceMath.GreaterThan(equity, overallFloor, PriceMath.DefaultTolerance);
        return new EmergencyDecision(false, false, overall, equity, double.NaN, overallFloor, _parameters.FtmoMaxLossMode, double.NaN);
    }

    private double OverallFloor()
    {
        return OverallFloorBelow(_parameters.InitialCapital);
    }

    private double OverallFloorBelow(double reference)
    {
        double capital = _parameters.InitialCapital;
        return reference - ((_parameters.FtmoMaxLossPercent - _parameters.FtmoGuardBufferPercent) * capital / 100.0);
    }

    /// <summary>
    /// P of RSK-021 in trailing mode: the larger of InitialCapital and <see cref="PeakBalance"/>, the
    /// same function the kill switch uses. NaN in static mode, and NaN when the peak is not a positive
    /// finite number, so that the caller falls back to the static floor (fail closed).
    /// </summary>
    private double TrailingPeak(RiskSnapshot snapshot)
    {
        if (_parameters.FtmoMaxLossMode != FtmoMaxLossMode.TrailingBalance)
        {
            return double.NaN;
        }

        double peak = PeakBalance(snapshot);
        return double.IsFinite(peak) && peak > 0.0 ? Math.Max(_parameters.InitialCapital, peak) : double.NaN;
    }

    private double DailyLimit()
    {
        return _parameters.InitialCapital * _parameters.DailyLossLimitPercent / 100.0;
    }

    /// <summary>
    /// W of RSK-006 without a new trade: balance + result at stop of each open bot position -
    /// risk at stop of each pending bot order - round-trip commission of each. A trade without a
    /// stop counts with its full sizing-rule risk.
    /// </summary>
    private double WorstCaseEquity(RiskSnapshot snapshot)
    {
        double sizingRisk = SizingRuleRisk(snapshot);
        double worst = snapshot.Account.Balance;
        foreach (PositionInfo position in snapshot.Positions)
        {
            if (IsBotLabel(position.Label))
            {
                SymbolInfo symbol = Symbol(snapshot, position.Symbol);
                double commission = symbol.RoundTripCommissionPerUnit * position.VolumeUnits;
                double atStop = -sizingRisk;
                if (position.StopLoss.HasValue)
                {
                    atStop = RiskMath.ResultAt(position.Side, position.EntryPrice, position.StopLoss.Value, position.VolumeUnits, symbol);
                }

                worst += atStop - commission;
            }
        }

        foreach (PendingOrderInfo order in snapshot.PendingOrders)
        {
            if (IsBotLabel(order.Label))
            {
                SymbolInfo symbol = Symbol(snapshot, order.Symbol);
                double commission = symbol.RoundTripCommissionPerUnit * order.VolumeUnits;
                double risk = sizingRisk;
                if (order.StopLoss.HasValue)
                {
                    risk = RiskAtStop(order.Side, order.EntryPrice, order.StopLoss.Value, order.VolumeUnits, symbol);
                }

                worst -= risk + commission;
            }
        }

        return worst;
    }

    private void RequireHistory(RiskSnapshot snapshot)
    {
        if (!HistoryCovers(snapshot))
        {
            throw new ArgumentException("The snapshot's history must reach back to " + RequiredHistoryStart(snapshot).ToString("o", CultureInfo.InvariantCulture) + ".", nameof(snapshot));
        }
    }

    private bool HistoryCovers(RiskSnapshot snapshot)
    {
        return snapshot.HistorySinceUtc <= RequiredHistoryStart(snapshot);
    }

    private DateTime RequiredHistoryStart(RiskSnapshot snapshot)
    {
        DateTime dayStart = CestClock.StartOfCestDayUtc(snapshot.ServerTimeUtc);
        DateTime epochStart = EpochStartUtc();
        return epochStart < dayStart ? epochStart : dayStart;
    }

    private DateTime EpochStartUtc()
    {
        return CestClock.StartOfCestDateUtc(_parameters.RiskEpochDate);
    }

    private static string? BotSymbolProblem(RiskSnapshot snapshot)
    {
        foreach (PositionInfo position in snapshot.Positions)
        {
            string? problem = IsBotLabel(position.Label) ? SymbolProblem(snapshot, position.Symbol) : null;
            if (problem != null)
            {
                return problem;
            }
        }

        foreach (PendingOrderInfo order in snapshot.PendingOrders)
        {
            string? problem = IsBotLabel(order.Label) ? SymbolProblem(snapshot, order.Symbol) : null;
            if (problem != null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? SymbolProblem(RiskSnapshot snapshot, string name)
    {
        SymbolInfo? symbol = snapshot.FindSymbol(name);
        return symbol == null ? "no symbol data for " + name : RiskMath.InvalidSymbolData(symbol);
    }

    private RiskCheckResult? CheckInputs(RiskSnapshot snapshot, PlannedTrade trade)
    {
        string? invalid = InvalidTrade(snapshot, trade);
        if (invalid != null)
        {
            return new RiskCheckResult(RiskReason.InvalidInput, 0.0, 0.0, invalid);
        }

        if (!RiskMath.IsOnLossSide(trade.Side, trade.Entry, trade.StopLoss))
        {
            return new RiskCheckResult(RiskReason.StopOnWrongSide, trade.StopLoss, trade.Entry, "stop " + Text(trade.StopLoss) + " entry " + Text(trade.Entry));
        }

        if (!HistoryCovers(snapshot))
        {
            return new RiskCheckResult(RiskReason.HistoryIncomplete, 0.0, 0.0, "history must reach back to " + RequiredHistoryStart(snapshot).ToString("o", CultureInfo.InvariantCulture));
        }

        string? symbolProblem = SymbolProblem(snapshot, trade.Symbol) ?? BotSymbolProblem(snapshot);
        return symbolProblem == null ? null : new RiskCheckResult(RiskReason.MissingSymbolInfo, 0.0, 0.0, symbolProblem);
    }

    private static string? InvalidTrade(RiskSnapshot snapshot, PlannedTrade trade)
    {
        if (!RiskMath.IsPositive(snapshot.Account.Balance) || !RiskMath.IsPositive(snapshot.Account.Equity))
        {
            return "balance and equity must be greater than zero";
        }

        if (string.IsNullOrEmpty(trade.Symbol) || !RiskMath.IsDefined(trade.Side))
        {
            return "symbol and side must be set";
        }

        if (!RiskMath.IsPositive(trade.Entry) || !RiskMath.IsPositive(trade.StopLoss) || !RiskMath.IsPositive(trade.VolumeUnits))
        {
            return "entry, stop and volume must be finite numbers greater than zero";
        }

        return RiskMath.IsNonNegative(trade.EstimatedMargin) ? null : "estimated margin must be a finite number that is not negative";
    }

    private static RiskCheckResult? CheckMargin(RiskSnapshot snapshot, PlannedTrade trade)
    {
        double freeAfter = snapshot.Account.FreeMargin - trade.EstimatedMargin;
        double minimum = snapshot.Account.Equity * RiskParameters.MinFreeMarginPercentOfEquity / 100.0;
        if (PriceMath.LessThan(freeAfter, minimum, PriceMath.DefaultTolerance))
        {
            return new RiskCheckResult(RiskReason.FreeMarginTooLow, freeAfter, minimum, "free margin after " + Text(freeAfter) + " below 50% of equity " + Text(minimum));
        }

        return null;
    }

    private RiskCheckResult? CheckOpenRisk(RiskSnapshot snapshot, PlannedTrade trade)
    {
        double newRisk = RiskAtStop(trade.Side, trade.Entry, trade.StopLoss, trade.VolumeUnits, Symbol(snapshot, trade.Symbol));
        double total = OpenRiskAtStop(snapshot).RiskMoney + newRisk;
        double cap = _parameters.InitialCapital * _parameters.MaxOpenRiskPercent / 100.0;
        if (PriceMath.GreaterThan(total, cap, PriceMath.DefaultTolerance))
        {
            return new RiskCheckResult(RiskReason.OpenRiskCapExceeded, total, cap, "open risk " + Text(total) + " above " + Text(cap));
        }

        return null;
    }

    private RiskCheckResult? CheckCurrencyExposure(RiskSnapshot snapshot, PlannedTrade trade)
    {
        // Same-side counts per currency, keyed such as "USD short".
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (PositionInfo position in snapshot.Positions)
        {
            if (IsBotLabel(position.Label))
            {
                AddExposure(counts, Symbol(snapshot, position.Symbol), position.Side);
            }
        }

        foreach (PendingOrderInfo order in snapshot.PendingOrders)
        {
            if (IsBotLabel(order.Label))
            {
                AddExposure(counts, Symbol(snapshot, order.Symbol), order.Side);
            }
        }

        SymbolInfo tradeSymbol = Symbol(snapshot, trade.Symbol);
        AddExposure(counts, tradeSymbol, trade.Side);

        // Only the two currencies of the new trade, on the side the trade takes in each, are checked (RSK-005).
        string[] touched = { ExposureKey(tradeSymbol.BaseCurrency, trade.Side == Side.Buy), ExposureKey(tradeSymbol.QuoteCurrency, trade.Side != Side.Buy) };
        foreach (string key in touched)
        {
            int count = counts[key];
            if (count > _parameters.MaxPositionsPerCurrency)
            {
                return new RiskCheckResult(RiskReason.CurrencyExposureCapExceeded, count, _parameters.MaxPositionsPerCurrency, key);
            }
        }

        return null;
    }

    private static void AddExposure(SortedDictionary<string, int> counts, SymbolInfo symbol, Side side)
    {
        Increment(counts, ExposureKey(symbol.BaseCurrency, side == Side.Buy));
        Increment(counts, ExposureKey(symbol.QuoteCurrency, side != Side.Buy));
    }

    private static string ExposureKey(string currency, bool isLong)
    {
        return currency.ToUpperInvariant() + (isLong ? " long" : " short");
    }

    private static void Increment(SortedDictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    private RiskCheckResult? CheckDailyBudget(RiskSnapshot snapshot, PlannedTrade trade)
    {
        SymbolInfo symbol = Symbol(snapshot, trade.Symbol);
        double newCost = RiskAtStop(trade.Side, trade.Entry, trade.StopLoss, trade.VolumeUnits, symbol) + (symbol.RoundTripCommissionPerUnit * trade.VolumeUnits);
        double used = DayStart(snapshot) - (WorstCaseEquity(snapshot) - newCost);
        double limit = DailyLimit();
        if (PriceMath.GreaterThan(used, limit, PriceMath.DefaultTolerance))
        {
            return new RiskCheckResult(RiskReason.DailyBudgetExceeded, used, limit, "worst-case daily loss " + Text(used) + " above " + Text(limit));
        }

        return null;
    }

    private RiskCheckResult? CheckMonthlyLoss(RiskSnapshot snapshot)
    {
        DateTime monthStart = CestClock.StartOfCestMonthUtc(snapshot.ServerTimeUtc);
        if (snapshot.HistorySinceUtc > monthStart)
        {
            return new RiskCheckResult(RiskReason.HistoryIncomplete, 0.0, 0.0, "history must reach back to " + monthStart.ToString("o", CultureInfo.InvariantCulture));
        }

        MonthlyLossStatus status = MonthStatus(snapshot, monthStart);
        if (!(status.MonthStartBalance > 0.0))
        {
            // Fail closed: a month-start balance that is not positive cannot be right.
            return new RiskCheckResult(RiskReason.InvalidInput, status.MonthStartBalance, 0.0, "month-start balance " + Text(status.MonthStartBalance) + " must be greater than zero");
        }

        if (status.Reached)
        {
            return new RiskCheckResult(RiskReason.MonthlyLossLimitReached, status.Loss, status.Limit, "monthly closed loss " + Text(status.Loss) + " at or above " + Text(status.Limit) + ", month-start balance " + Text(status.MonthStartBalance));
        }

        return null;
    }

    private RiskCheckResult? CheckEntryCounts(RiskSnapshot snapshot, PlannedTrade trade)
    {
        DateTime dayStart = CestClock.StartOfCestDayUtc(snapshot.ServerTimeUtc);

        // Bot positions entered today, keyed by position id so that partial closes count once.
        var positionSymbols = new SortedDictionary<long, string>();
        foreach (ClosedTradeInfo closed in snapshot.History)
        {
            if (IsBotLabel(closed.Label) && closed.EntryTimeUtc >= dayStart)
            {
                positionSymbols[closed.PositionId] = closed.Symbol;
            }
        }

        foreach (PositionInfo position in snapshot.Positions)
        {
            if (IsBotLabel(position.Label) && position.OpenTimeUtc >= dayStart)
            {
                positionSymbols[position.Id] = position.Symbol;
            }
        }

        int symbolEntries = 1;
        int accountEntries = 1 + positionSymbols.Count;
        foreach (string symbol in positionSymbols.Values)
        {
            symbolEntries += string.Equals(symbol, trade.Symbol, StringComparison.Ordinal) ? 1 : 0;
        }

        foreach (PendingOrderInfo order in snapshot.PendingOrders)
        {
            if (IsBotLabel(order.Label) && order.CreatedTimeUtc >= dayStart)
            {
                accountEntries++;
                symbolEntries += string.Equals(order.Symbol, trade.Symbol, StringComparison.Ordinal) ? 1 : 0;
            }
        }

        if (symbolEntries > _parameters.MaxEntriesPerSymbolPerDay)
        {
            return new RiskCheckResult(RiskReason.SymbolEntryLimitReached, symbolEntries, _parameters.MaxEntriesPerSymbolPerDay, trade.Symbol);
        }

        if (accountEntries > _parameters.MaxEntriesPerDay)
        {
            return new RiskCheckResult(RiskReason.AccountEntryLimitReached, accountEntries, _parameters.MaxEntriesPerDay, "account entries " + Text(accountEntries));
        }

        return null;
    }
}
