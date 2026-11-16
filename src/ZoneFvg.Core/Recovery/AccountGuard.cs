using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// Result of the account guards at one moment (RSK-006, RSK-008, RSK-009), read from the broker
/// port. A null decision means it could not be computed; then new entries are not allowed.
/// </summary>
public sealed class GuardStatus
{
    internal GuardStatus(DateTime atUtc, EmergencyDecision? emergency, KillSwitchDecision? killSwitch, DailyBudgetStatus? budget, string? dataProblem)
    {
        AtUtc = atUtc;
        Emergency = emergency;
        KillSwitch = killSwitch;
        Budget = budget;
        DataProblem = dataProblem;
    }

    /// <summary>Server time of the evaluation, kind UTC.</summary>
    public DateTime AtUtc { get; }

    /// <summary>The emergency guard (RSK-009), or null when not even equity could be read.</summary>
    public EmergencyDecision? Emergency { get; }

    /// <summary>The kill switch (RSK-008), or null when the risk snapshot could not be built.</summary>
    public KillSwitchDecision? KillSwitch { get; }

    /// <summary>The daily risk budget (RSK-006), or null when the risk snapshot could not be built.</summary>
    public DailyBudgetStatus? Budget { get; }

    /// <summary>Why the broker data could not be used, or null when it could (architecture section 10, "Bad broker data").</summary>
    public string? DataProblem { get; }

    /// <summary>True when the emergency guard decides CloseAllAndHalt (RSK-009).</summary>
    public bool EmergencyFires => Emergency != null && Emergency.Action == EmergencyAction.CloseAllAndHalt;

    /// <summary>True when the kill switch decides Halt (RSK-008).</summary>
    public bool KillSwitchFires => KillSwitch != null && KillSwitch.Halt;

    /// <summary>True when the daily budget is exhausted (RSK-006).</summary>
    public bool BudgetExhausted => Budget != null && Budget.Exhausted;

    /// <summary>
    /// True when every guard could be computed. When false no new entry is placed: fail closed
    /// (architecture section 10, "Bad broker data").
    /// </summary>
    public bool EntriesAllowed => DataProblem == null && Emergency != null && KillSwitch != null && Budget != null;

    /// <summary>A one-line invariant summary for the journal and the recovery report (REC-010).</summary>
    public string Text
    {
        get
        {
            string emergency = Emergency == null
                ? "unknown"
                : (EmergencyFires ? "CLOSE-ALL" : "ok") + " equity " + Number(Emergency.Equity) + " daily floor " + Number(Emergency.DailyFloor) + " overall floor " + Number(Emergency.OverallFloor);
            string kill = KillSwitch == null
                ? "unknown"
                : (KillSwitchFires ? "HALT" : "ok") + " peak " + Number(KillSwitch.Peak) + " drawdown " + Number(KillSwitch.DrawdownMoney) + " limit " + Number(KillSwitch.LimitMoney);
            string budget = Budget == null
                ? "unknown"
                : (BudgetExhausted ? "EXHAUSTED" : "ok") + " used " + Number(Budget.Used) + " limit " + Number(Budget.Limit);
            return "emergency=" + emergency + "; kill-switch=" + kill + "; daily-budget=" + budget + "; data=" + (DataProblem ?? "ok");
        }
    }

    private static string Number(double value)
    {
        return double.IsNaN(value) ? "unknown" : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Evaluates the account guards from the broker port (RSK-006, RSK-008, RSK-009): builds a
/// <see cref="RiskSnapshot"/> and asks the <see cref="RiskEngine"/>. Keeps nothing between calls.
/// </summary>
/// <remarks>
/// When the snapshot cannot be built or a risk call fails on the data (an
/// <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/>, for example
/// history that cannot be read or holds a value that is not finite), the emergency guard still
/// runs from equity alone with <see cref="RiskEngine.EmergencyGuardFromEquity"/>, and new entries
/// are not allowed (architecture section 10, "Bad broker data"). Any other exception is a
/// programming error and is thrown to the event boundary (REC-011).
/// </remarks>
public sealed class AccountGuard
{
    private readonly string _symbol;
    private readonly RiskParameters _parameters;
    private readonly RiskEngine _engine;
    private readonly IBroker _broker;

    /// <summary>Creates the guards of one instance (RSK-006, RSK-008, RSK-009).</summary>
    /// <param name="symbol">Symbol of this instance; its symbol data is always read.</param>
    /// <param name="parameters">Risk parameters; validated here.</param>
    /// <param name="broker">Broker port.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public AccountGuard(string symbol, RiskParameters parameters, IBroker broker)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentException("The symbol must not be empty.", nameof(symbol));
        }

        ArgumentNullException.ThrowIfNull(parameters);
        _engine = new RiskEngine(parameters);
        _symbol = symbol;
        _parameters = parameters;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <summary>
    /// Earliest close time of the history the guards need: the earlier of 00:00 CE(S)T of the
    /// current day and of RiskEpochDate (RSK-007, RSK-008).
    /// </summary>
    /// <param name="parameters">Risk parameters.</param>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The start of the needed history, kind UTC.</returns>
    public static DateTime HistoryStartUtc(RiskParameters parameters, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        DateTime dayStart = CestClock.StartOfCestDayUtc(nowUtc);
        DateTime epochStart = CestClock.StartOfCestDateUtc(parameters.RiskEpochDate);
        return epochStart < dayStart ? epochStart : dayStart;
    }

    /// <summary>
    /// Evaluates the emergency guard, the kill switch and the daily budget at the server time
    /// (RSK-006, RSK-008, RSK-009), reading account, positions, orders, history and symbol data
    /// from the broker port.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The decisions; a decision that could not be computed is null.</returns>
    public GuardStatus Evaluate(DateTime nowUtc)
    {
        AccountSnapshot? account = null;
        RiskSnapshot? snapshot = null;
        string? problem = null;
        try
        {
            account = _broker.GetAccount();
            snapshot = BuildSnapshot(nowUtc, account);
        }
        catch (Exception exception) when (IsDataProblem(exception))
        {
            problem = Describe(exception);
        }

        EmergencyDecision? emergency = null;
        if (snapshot != null)
        {
            emergency = _engine.EmergencyGuard(snapshot);
        }
        else if (account != null && double.IsFinite(account.Equity))
        {
            emergency = _engine.EmergencyGuardFromEquity(account.Equity);
        }

        KillSwitchDecision? killSwitch = null;
        DailyBudgetStatus? budget = null;
        if (snapshot != null)
        {
            try
            {
                killSwitch = _engine.KillSwitch(snapshot);
                budget = _engine.DailyBudgetExhausted(snapshot);
            }
            catch (Exception exception) when (IsDataProblem(exception))
            {
                problem = Describe(exception);
                killSwitch = null;
                budget = null;
            }
        }

        return new GuardStatus(nowUtc, emergency, killSwitch, budget, problem);
    }

    private RiskSnapshot BuildSnapshot(DateTime nowUtc, AccountSnapshot account)
    {
        DateTime since = HistoryStartUtc(_parameters, nowUtc);
        IReadOnlyList<PositionInfo> positions = _broker.GetPositions() ?? throw new InvalidOperationException("The broker returned no position list.");
        IReadOnlyList<PendingOrderInfo> orders = _broker.GetPendingOrders() ?? throw new InvalidOperationException("The broker returned no order list.");
        IReadOnlyList<ClosedTradeInfo> history = _broker.GetHistory(since) ?? throw new InvalidOperationException("The broker returned no history.");

        // Symbol data for this instance and for every symbol with a bot position or order (RSK-004, RSK-005).
        var names = new SortedSet<string>(StringComparer.Ordinal) { _symbol };
        foreach (PositionInfo position in positions.Where(p => p != null && RiskEngine.IsBotLabel(p.Label) && p.Symbol != null))
        {
            names.Add(position.Symbol);
        }

        foreach (PendingOrderInfo order in orders.Where(o => o != null && RiskEngine.IsBotLabel(o.Label) && o.Symbol != null))
        {
            names.Add(order.Symbol);
        }

        List<SymbolInfo> symbols = names.Select(name => _broker.GetSymbol(name)).ToList();
        return new RiskSnapshot(nowUtc, account, positions, orders, history, since, symbols);
    }

    private static bool IsDataProblem(Exception exception)
    {
        return exception is ArgumentException || exception is InvalidOperationException;
    }

    private static string Describe(Exception exception)
    {
        return exception.GetType().Name + ": " + exception.Message;
    }
}
