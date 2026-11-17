using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// The recovery procedure of spec 8.3 (REC-002 to REC-010). The same procedure runs at every
/// start and after every reconnect. Each step is a private method named after the step whose
/// result goes into the <see cref="RecoveryReport"/>.
/// </summary>
/// <remarks>
/// <para>Recovery never places an entry order. Its only broker writes are protection repair
/// (EXE-011), cancels of pending orders that may not be kept (REC-006), and the catch-up duties
/// (REC-009). Running it twice in a row therefore changes nothing the second time (REC-002).</para>
/// <para>Bars are read through <see cref="IBarHistory"/>: for each timeframe the larger of the
/// load of spec 8.3 (300 D1, 500 H4, 3,000 M15) and the warm-up requirement, which is the least
/// that must be present (SIG-020). A series with a problem or a gap is requested once more; if a
/// problem other than a gap remains, the bot stays in Recovering and only protection repair runs.
/// A gap that remains is the broker's data and is accepted and reported (REC-003, owner decision
/// A of 2026-11-17).</para>
/// <para>A zone is known when the rebuilt signal layer has it among its non-terminal zones, or
/// produced a signal for it during the replay. A zone that signalled is Used at once and leaves
/// the zone book (architecture section 10, "Terminal zones"), so the replayed signals are how the
/// rebuilt book identifies the zone of an existing order or position.</para>
/// </remarks>
public sealed class RecoveryService
{
    /// <summary>Key-value store key of the last server time the bot was seen running, written by the bot (REC-010 downtime).</summary>
    public const string LastSeenKey = "app.lastSeenUtc";

    /// <summary>Number of newest M15 bars handed to the bot with the findings (EXE-009 resolution).</summary>
    public const int RecentM15Count = 32;

    private readonly string _symbol;
    private readonly SignalParameters _signalParameters;
    private readonly ExecutionParameters _executionParameters;
    private readonly bool _newsEnabled;
    private readonly IBroker _broker;
    private readonly IKeyValueStore _store;
    private readonly RequestCounter _counter;
    private readonly IExecutionEvents _events;
    private readonly Func<RecoveryFindings, BotState> _enterState;
    private readonly IReadOnlyCollection<MonthDay> _marketHolidays;
    private readonly IReadOnlyDictionary<TimeFrame, int> _warmup;
    private readonly string _parameterHash;
    private readonly TradeManager _trades;
    private readonly AccountGuard _guard;
    private readonly SafetyActions _safety;
    private readonly TradeJournal _journal;
    private readonly bool _haltsRecordOnly;

    /// <summary>Creates the recovery procedure of one instance (REC-002).</summary>
    /// <param name="symbol">Symbol of this instance.</param>
    /// <param name="signalParameters">Signal parameters; validated here.</param>
    /// <param name="executionParameters">Execution parameters; validated here.</param>
    /// <param name="riskParameters">Risk parameters; validated here.</param>
    /// <param name="newsEnabled">The value of NewsEnabled, for the report.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="store">Key-value store (downtime and journaled trades).</param>
    /// <param name="tradeEvents">Receiver of the trade events found by the journal backfill (REC-008).</param>
    /// <param name="counter">Request counter of this instance (RSK-010).</param>
    /// <param name="events">Hook for skip records and alerts.</param>
    /// <param name="enterState">Called at step 10 with the findings; enters the next state and returns it.</param>
    /// <param name="marketHolidays">Market holidays, which are not data gaps (REC-003); null means none.</param>
    /// <param name="haltsRecordOnly">
    /// REC-014, backtests only: when true the catch-up step closes and cancels nothing for the
    /// emergency guard, the kill switch or the daily budget; the bot records them instead. Default
    /// false (live).
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public RecoveryService(
        string symbol,
        SignalParameters signalParameters,
        ExecutionParameters executionParameters,
        RiskParameters riskParameters,
        bool newsEnabled,
        IBroker broker,
        IKeyValueStore store,
        ITradeEvents tradeEvents,
        RequestCounter counter,
        IExecutionEvents events,
        Func<RecoveryFindings, BotState> enterState,
        IReadOnlyCollection<MonthDay>? marketHolidays = null,
        bool haltsRecordOnly = false)
    {
        ArgumentNullException.ThrowIfNull(signalParameters);
        ArgumentNullException.ThrowIfNull(executionParameters);
        ArgumentNullException.ThrowIfNull(riskParameters);
        signalParameters.Validate();
        executionParameters.Validate();
        riskParameters.Validate();
        _symbol = symbol;
        _signalParameters = signalParameters;
        _executionParameters = executionParameters;
        _newsEnabled = newsEnabled;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _enterState = enterState ?? throw new ArgumentNullException(nameof(enterState));
        _marketHolidays = marketHolidays ?? Array.Empty<MonthDay>();
        _haltsRecordOnly = haltsRecordOnly;
        _warmup = WarmupCalculator.For(signalParameters);
        _parameterHash = ParameterHash.Compute(signalParameters, executionParameters);
        _trades = new TradeManager(symbol, executionParameters, broker, counter, events);
        _guard = new AccountGuard(symbol, riskParameters, broker);
        _safety = new SafetyActions(symbol, executionParameters, broker, counter, events);
        _journal = new TradeJournal(symbol, riskParameters, broker, store, tradeEvents);
    }

    /// <summary>
    /// The number of closed bars recovery reads for a timeframe: the larger of the spec 8.3 load
    /// (300 D1, 500 H4, 3,000 M15) and the warm-up requirement (REC-003, SIG-020).
    /// </summary>
    /// <param name="timeFrame">D1, H4 or M15.</param>
    /// <returns>The number of bars.</returns>
    public int LoadCount(TimeFrame timeFrame)
    {
        int spec = timeFrame switch
        {
            TimeFrame.D1 => 300,
            TimeFrame.H4 => 500,
            _ => 3000,
        };
        return Math.Max(spec, _warmup[timeFrame]);
    }

    /// <summary>
    /// Runs the recovery procedure of spec 8.3 (REC-002 to REC-010) at the broker's server time
    /// and returns its report. Never places an entry order.
    /// </summary>
    /// <param name="history">Closed-bar history port.</param>
    /// <returns>The report, with the state entered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="history"/> is null.</exception>
    public RecoveryReport Run(IBarHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        DateTime now = ServerTime();
        TimeSpan? downtime = Downtime(now);
        DataStep data = WaitForConnectionAndData(history, now);
        if (!data.Complete)
        {
            // Spec 8.3 step 1: stay in Recovering. Protection repair is still required (prompt WP07).
            IReadOnlyList<TradeAction> repaired = _broker.IsConnected ? _trades.EnsureProtection() : Array.Empty<TradeAction>();
            var incomplete = new RecoveryFindings(now, false, data.Problems, null, new Bar?[3], Array.Empty<Bar>(), null, false, Array.Empty<string>(), Array.Empty<ReplayedSignal>());
            BotState waiting = _enterState(incomplete);
            return new RecoveryReport
            {
                Symbol = _symbol,
                AtUtc = now,
                Downtime = downtime,
                DataComplete = false,
                DataProblems = data.Problems,
                Protection = Describe(repaired),
                State = waiting,
                NewsEnabled = _newsEnabled,
                ParameterHash = _parameterHash,
            };
        }

        ReplayStep replay = RebuildSignalLayer(data, now);
        BrokerStep view = ReadBroker();
        AdoptStep adopt = AdoptPositions(view, replay.KnownZones);
        OrderStep orders = CheckPendingOrders(view, replay.KnownZones, now);
        IReadOnlyList<string> strangers = HandleStrangers(adopt);
        JournalStep journaled = BackfillJournal(now);
        GuardStatus guard = RecalculateLimits(now);
        CatchUpStep catchUp = CatchUpDuties(now, guard);
        IReadOnlyList<Bar> m15 = data.Bars[TimeFrame.M15];
        IReadOnlyList<Bar> recent = m15.Skip(Math.Max(0, m15.Count - RecentM15Count)).ToList().AsReadOnly();
        var findings = new RecoveryFindings(now, true, Array.Empty<string>(), replay.Engine, data.LastBars, recent, guard, catchUp.WeekendClosePending, strangers, replay.Signals);
        BotState state = ReportAndResume(findings);
        return new RecoveryReport
        {
            Symbol = _symbol,
            AtUtc = now,
            Downtime = downtime,
            DataComplete = true,
            AcceptedGaps = data.AcceptedGaps,
            ActiveZones = replay.Engine.Zones.Select(zone => zone.Id).ToList().AsReadOnly(),
            ReplaySignals = replay.Signals.Count,
            PositionsAdopted = adopt.Adopted,
            Strangers = strangers,
            WithoutTakeProfit = adopt.WithoutTakeProfit,
            Protection = Describe(adopt.Protection),
            OrdersKept = orders.Kept,
            OrdersCancelled = orders.Cancelled,
            JournaledTrades = journaled.Known,
            JournalProblem = journaled.Problem,
            Risk = guard.Text,
            CatchUp = Describe(catchUp.Actions),
            WeekendClosePending = catchUp.WeekendClosePending,
            State = state,
            NewsEnabled = _newsEnabled,
            ParameterHash = _parameterHash,
        };
    }

    // Step 1 (REC-003, owner decision A of 2026-11-17): connection, bar counts against the warm-up
    // requirement, missing, unclosed or disordered bars and recency fail recovery; a gap that is still
    // there after the one reload is the broker's data and is accepted.
    private DataStep WaitForConnectionAndData(IBarHistory history, DateTime now)
    {
        if (!_broker.IsConnected)
        {
            return new DataStep(false, new[] { "broker not connected" }, new Dictionary<TimeFrame, IReadOnlyList<Bar>>(), new Bar?[3], Array.Empty<AcceptedGapList>());
        }

        var bars = new Dictionary<TimeFrame, IReadOnlyList<Bar>>();
        var problems = new List<string>();
        var accepted = new List<AcceptedGapList>();
        foreach (TimeFrame timeFrame in BarIntakeOrder.SameInstant)
        {
            IReadOnlyList<Bar> series = history.ClosedBars(timeFrame, LoadCount(timeFrame));
            if (Problem(timeFrame, series, now) != null || Gaps(timeFrame, series).Count > 0)
            {
                // Request more once, then read again (prompt WP07).
                history.RequestMore(timeFrame, LoadCount(timeFrame));
                series = history.ClosedBars(timeFrame, LoadCount(timeFrame));
            }

            string? problem = Problem(timeFrame, series, now);
            if (problem != null)
            {
                problems.Add(problem);
            }
            else
            {
                IReadOnlyList<BarGap> gaps = Gaps(timeFrame, series);
                if (gaps.Count > 0)
                {
                    IReadOnlyList<BarGap> newest = gaps.Reverse().Take(AcceptedGapList.Listed).ToList().AsReadOnly();
                    accepted.Add(new AcceptedGapList(timeFrame, gaps.Count, newest));
                }
            }

            bars[timeFrame] = series;
        }

        var lastBars = new Bar?[3];
        if (problems.Count == 0)
        {
            foreach (TimeFrame timeFrame in BarIntakeOrder.SameInstant)
            {
                IReadOnlyList<Bar> series = bars[timeFrame];
                lastBars[BarIntakeOrder.Rank(timeFrame)] = series.Count > 0 ? series[series.Count - 1] : null;
            }
        }

        return new DataStep(problems.Count == 0, problems.AsReadOnly(), bars, lastBars, accepted.AsReadOnly());
    }

    // Step 2 (REC-004): replay in close-time order, D1 then H4 then M15 at equal close times, with trading off.
    private ReplayStep RebuildSignalLayer(DataStep data, DateTime now)
    {
        var feed = new List<(TimeFrame TimeFrame, Bar Bar)>();
        foreach (TimeFrame timeFrame in BarIntakeOrder.SameInstant)
        {
            feed.AddRange(data.Bars[timeFrame].Select(bar => (TimeFrame: timeFrame, Bar: bar)));
        }

        var engine = new SignalEngine(_signalParameters);
        var known = new SortedSet<string>(StringComparer.Ordinal);
        var replayed = new List<ReplayedSignal>();
        IEnumerable<(TimeFrame TimeFrame, Bar Bar)> ordered = feed
            .OrderBy(item => BarIntakeOrder.CloseTimeUtc(item.TimeFrame, item.Bar))
            .ThenBy(item => BarIntakeOrder.Rank(item.TimeFrame));
        foreach ((TimeFrame timeFrame, Bar bar) in ordered)
        {
            foreach (Signal signal in engine.OnBarClosed(timeFrame, bar, true))
            {
                known.Add(signal.ZoneId);
                string detail = "replayed signal confirmed at " + Minute(signal.ConfirmationCloseUtc) + " is not executable";
                var skip = new SkipRecord(_symbol, signal.ZoneId, SkipReason.SignalFromReplay, 0.0, 0.0, detail, now);
                _events.Skipped(skip);
                replayed.Add(new ReplayedSignal(signal, engine.Bias, skip));
            }
        }

        known.UnionWith(engine.Zones.Select(zone => zone.Id));
        return new ReplayStep(engine, known, replayed.AsReadOnly());
    }

    // Step 3: the bot's positions and pending orders on this symbol, from the broker only.
    private BrokerStep ReadBroker()
    {
        List<PositionInfo> positions = _broker.GetPositions()
            .Where(p => RiskEngine.IsBotLabel(p.Label) && string.Equals(p.Symbol, _symbol, StringComparison.Ordinal))
            .OrderBy(p => p.Id)
            .ToList();
        List<PendingOrderInfo> orders = _broker.GetPendingOrders()
            .Where(o => RiskEngine.IsBotLabel(o.Label) && string.Equals(o.Symbol, _symbol, StringComparison.Ordinal))
            .OrderBy(o => o.Id)
            .ToList();
        return new BrokerStep(positions, orders);
    }

    // Step 4 (REC-005, REC-007): match each position to its zone and plan; repair a missing stop (EXE-011).
    private AdoptStep AdoptPositions(BrokerStep view, SortedSet<string> knownZones)
    {
        var adopted = new List<long>();
        var strangers = new List<string>();
        var withoutTarget = new List<long>();
        foreach (PositionInfo position in view.Positions)
        {
            string? why = StrangerReason(position.Label, position.Comment, knownZones);
            if (why == null)
            {
                adopted.Add(position.Id);
            }
            else
            {
                strangers.Add(Text(position.Id) + " (" + why + ")");
            }

            if (!position.TakeProfit.HasValue)
            {
                withoutTarget.Add(position.Id);
            }
        }

        IReadOnlyList<TradeAction> protection = _trades.EnsureProtection();
        return new AdoptStep(adopted.AsReadOnly(), strangers.AsReadOnly(), withoutTarget.AsReadOnly(), protection);
    }

    // Step 5 (REC-006): keep an order only with stop, target and expiry and a known zone; cancel the others.
    private OrderStep CheckPendingOrders(BrokerStep view, SortedSet<string> knownZones, DateTime now)
    {
        var kept = new List<long>();
        var cancelled = new List<string>();
        foreach (PendingOrderInfo order in view.Orders)
        {
            string? why = CancelReason(order, knownZones);
            if (why == null)
            {
                kept.Add(order.Id);
                continue;
            }

            OrderResult result = _broker.CancelOrder(order.Id);
            _counter.Record(now);
            if (result.Outcome == OrderOutcome.Success)
            {
                cancelled.Add(Text(order.Id) + " (" + why + ")");
                continue;
            }

            cancelled.Add(Text(order.Id) + " (" + why + ", cancel " + result.Outcome + ")");
            _events.Alert(new ExecutionAlert(AlertKind.CancelFailed, _symbol, "order " + Text(order.Id) + " (" + why + ") cancel " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
        }

        return new OrderStep(kept.AsReadOnly(), cancelled.AsReadOnly());
    }

    // Step 6 (REC-007): strangers keep their stop and target, count in risk (by their bot label) and
    // in the Friday close (TradeManager acts on every bot label of the symbol); they are reported.
    private static IReadOnlyList<string> HandleStrangers(AdoptStep adopt)
    {
        return adopt.Strangers;
    }

    // Step 7 (REC-008): journal trades that opened or closed while the bot was away, once each. History
    // that cannot be read does not stop recovery: the later steps still protect the account, and the
    // backfill runs again at the next trade event or recovery.
    private JournalStep BackfillJournal(DateTime now)
    {
        try
        {
            TradeJournalResult result = _journal.Record(now);
            string? ignored = result.Ignored.Count == 0 ? null : "unreadable stored entries ignored: " + string.Join(",", result.Ignored);
            return new JournalStep(result.Known, ignored);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            return new JournalStep(0, exception.GetType().Name + ": " + exception.Message);
        }
    }

    // Step 8 (RSK-006, RSK-008, RSK-009): limits recalculated from broker history.
    private GuardStatus RecalculateLimits(DateTime now)
    {
        return _guard.Evaluate(now);
    }

    // Step 9 (REC-009): emergency close, cancels on a breached limit, and the weekend close. In a backtest
    // (REC-014) the guards close and cancel nothing; the bot records them as would-halt or would-block.
    private CatchUpStep CatchUpDuties(DateTime now, GuardStatus guard)
    {
        var actions = new List<TradeAction>();
        if (!_haltsRecordOnly)
        {
            actions.AddRange(GuardActions(guard));
        }

        bool pending = false;
        if (_safety.InWeekendWindow(now) && _safety.HasSymbolExposure())
        {
            if (_safety.MarketOpen())
            {
                actions.AddRange(_safety.CloseSymbolTrades("weekend close"));
            }

            pending = _safety.HasSymbolExposure();
        }

        return new CatchUpStep(actions.AsReadOnly(), pending);
    }

    private IReadOnlyList<TradeAction> GuardActions(GuardStatus guard)
    {
        if (guard.EmergencyFires)
        {
            return _safety.CloseAllBotTrades("emergency guard");
        }

        if (guard.KillSwitchFires)
        {
            return _trades.CancelEntryOrders("kill switch");
        }

        return guard.BudgetExhausted ? _trades.CancelEntryOrders("daily budget exhausted") : Array.Empty<TradeAction>();
    }

    // Step 10 (REC-010): the bot enters Running, Blocked or Halted from the findings; the caller journals and alerts the report.
    private BotState ReportAndResume(RecoveryFindings findings)
    {
        return _enterState(findings);
    }

    // REC-003 (owner decision A): every problem except a gap.
    private string? Problem(TimeFrame timeFrame, IReadOnlyList<Bar>? series, DateTime now)
    {
        return BarSeriesCheck.ProblemOtherThanGap(timeFrame, series, _warmup[timeFrame], now, _executionParameters.FridayCloseUtc, _marketHolidays);
    }

    private IReadOnlyList<BarGap> Gaps(TimeFrame timeFrame, IReadOnlyList<Bar>? series)
    {
        return BarSeriesCheck.Gaps(timeFrame, series, _executionParameters.FridayCloseUtc, _marketHolidays);
    }

    private string? StrangerReason(string label, string comment, SortedSet<string> knownZones)
    {
        if (!OrderLabel.TryParse(label, out string symbol, out string zoneId) || !string.Equals(symbol, _symbol, StringComparison.Ordinal))
        {
            return "label not readable";
        }

        CommentPlan? plan = OrderComment.TryParse(comment);
        if (plan == null)
        {
            return "comment not readable";
        }

        if (!knownZones.Contains(zoneId))
        {
            return "zone " + zoneId + " unknown";
        }

        return string.Equals(plan.ParameterHash, _parameterHash, StringComparison.Ordinal) ? null : "parameter hash " + plan.ParameterHash + " differs";
    }

    private static string? CancelReason(PendingOrderInfo order, SortedSet<string> knownZones)
    {
        if (!order.StopLoss.HasValue)
        {
            return "no stop loss";
        }

        if (!order.TakeProfit.HasValue)
        {
            return "no take profit";
        }

        if (!order.ExpiryUtc.HasValue)
        {
            return "no expiry";
        }

        if (!OrderLabel.TryParse(order.Label, out string _, out string zoneId))
        {
            return "label not readable";
        }

        return knownZones.Contains(zoneId) ? null : "zone " + zoneId + " unknown or ended";
    }

    private TimeSpan? Downtime(DateTime now)
    {
        string? text = _store.Get(LastSeenKey);
        if (!TryParseUtc(text, out DateTime lastSeen))
        {
            return null;
        }

        return lastSeen <= now ? now - lastSeen : null;
    }

    private DateTime ServerTime()
    {
        DateTime now = _broker.ServerTimeUtc;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The broker's server time is not UTC (REC-012).");
        }

        return now;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<TradeAction> actions)
    {
        return actions.Select(action => Text(action.Id) + " " + action.Outcome + " (" + action.Detail + ")").ToList().AsReadOnly();
    }

    private sealed record DataStep(bool Complete, IReadOnlyList<string> Problems, Dictionary<TimeFrame, IReadOnlyList<Bar>> Bars, Bar?[] LastBars, IReadOnlyList<AcceptedGapList> AcceptedGaps);

    private sealed record ReplayStep(SignalEngine Engine, SortedSet<string> KnownZones, IReadOnlyList<ReplayedSignal> Signals);

    private sealed record BrokerStep(IReadOnlyList<PositionInfo> Positions, IReadOnlyList<PendingOrderInfo> Orders);

    private sealed record AdoptStep(IReadOnlyList<long> Adopted, IReadOnlyList<string> Strangers, IReadOnlyList<long> WithoutTakeProfit, IReadOnlyList<TradeAction> Protection);

    private sealed record OrderStep(IReadOnlyList<long> Kept, IReadOnlyList<string> Cancelled);

    private sealed record JournalStep(int Known, string? Problem);

    private sealed record CatchUpStep(IReadOnlyList<TradeAction> Actions, bool WeekendClosePending);
}
