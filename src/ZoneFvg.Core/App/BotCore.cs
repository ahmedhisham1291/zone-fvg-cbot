using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// The only class the cTrader adapter talks to (docs/architecture.md section 3). It wires the
/// signal engine, the news filter, the risk guards, the execution layer and recovery, and owns
/// the bot state (REC-001).
/// </summary>
/// <remarks>
/// <para>Every public method is wrapped: an exception is journaled, counted in the error counter
/// of RSK-010 and alerted, and the method returns normally; the sixth error in an hour halts the
/// bot (REC-011). The core never stops its process.</para>
/// <para>All time comes from <see cref="IBroker.ServerTimeUtc"/> or from bar times (REC-012).</para>
/// <para>States (spec 8.5): Recovering while recovery has not completed or the connection is
/// lost; Halted after the kill switch, the emergency guard, the request limit or an error storm;
/// Blocked while the calendar is unavailable, the daily budget is exhausted (until the next CE(S)T
/// day) or a bar series has a gap; otherwise Running. Only Running places entries.</para>
/// </remarks>
public sealed class BotCore
{
    private static readonly TimeSpan RecoveryRetryInterval = TimeSpan.FromMinutes(15.0);
    private static readonly DateTime FallbackTimeUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The news filter is switched off by a calendar that covers 2000 to 2099 with low-impact events
    // only: no window, never Unavailable, no download (owner decision of 2026-11-16, NewsEnabled).
    private const string NoNewsHistory =
        "2000-01-03 00:00,USD,Low,News filter disabled\n2099-12-28 00:00,USD,Low,News filter disabled\n";

    private readonly IBroker _broker;
    private readonly IKeyValueStore _store;
    private readonly IJournalSink _journal;
    private readonly IOperationsHooks _hooks;
    private readonly bool _isBacktest;
    private readonly string _symbol;
    private readonly bool _newsEnabled;
    private readonly TimeSpan _fridayCloseUtc;
    private readonly RiskParameters _risk;
    private readonly string _parameterHash;
    private readonly NewsService _news;
    private readonly RequestCounter _requests;
    private readonly ErrorCounter _errors;
    private readonly CoreEvents _events;
    private readonly PreTradeFilters _filters;
    private readonly OrderManager _orders;
    private readonly TradeManager _trades;
    private readonly AccountGuard _guard;
    private readonly SafetyActions _safety;
    private readonly TradeJournal _tradeJournal;
    private readonly RecoveryService _recovery;
    private readonly BotStateMachine _machine = new BotStateMachine();
    private readonly Bar?[] _lastBars = new Bar?[3];
    private readonly List<Bar> _recentM15 = new List<Bar>();

    private SignalEngine? _engine;
    private IBarHistory? _history;
    private bool _disconnected;
    private bool _recoveryPending = true;
    private bool _dataGap;
    private bool _dataAlertSent;
    private bool _riskDataAlertSent;
    private bool _calendarUnavailable;
    private bool _weekendClosePending;
    private DateTime? _budgetBlockedDayUtc;
    private DateTime? _lastRecoveryAttemptUtc;
    private DateTime? _lastKnownServerTimeUtc;
    private bool _hasFed;
    private DateTime _lastFedCloseUtc;
    private int _lastFedRank;
    private int _unjournaledErrors;

    /// <summary>
    /// Creates the core of one instance (docs/architecture.md section 3). Validates every
    /// parameter and throws on the first invalid one, so the adapter can stop with a message.
    /// </summary>
    /// <param name="parameters">All parameters of the instance.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="store">Key-value store (news cache, journaled trades, last-seen time).</param>
    /// <param name="http">HTTP port for the news feed.</param>
    /// <param name="journal">Journal port.</param>
    /// <param name="isBacktest">True in a backtest: news comes from the embedded history (spec 6.4).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A parameter is invalid.</exception>
    public BotCore(BotParameters parameters, IBroker broker, IKeyValueStore store, IHttpTransport http, IJournalSink journal, bool isBacktest)
        : this(parameters, broker, store, http, journal, isBacktest, new NoOperationsHooks())
    {
    }

    internal BotCore(BotParameters parameters, IBroker broker, IKeyValueStore store, IHttpTransport http, IJournalSink journal, bool isBacktest, IOperationsHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(http);
        parameters.Validate(isBacktest);
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _isBacktest = isBacktest;
        _symbol = parameters.Symbol;
        _newsEnabled = parameters.News.NewsEnabled;
        _fridayCloseUtc = parameters.Execution.FridayCloseUtc;
        _risk = parameters.Risk;
        _parameterHash = ParameterHash.Compute(parameters.Signal, parameters.Execution);
        _news = CreateNews(parameters, store, http, isBacktest);
        _requests = new RequestCounter(parameters.Risk);
        _errors = new ErrorCounter(parameters.Risk);
        _events = new CoreEvents(this);
        _filters = new PreTradeFilters(parameters.Execution, parameters.Signal, parameters.Risk, broker, _news, _events);
        _orders = new OrderManager(broker, _requests, _events);
        _trades = new TradeManager(_symbol, parameters.Execution, broker, _requests, _events);
        _guard = new AccountGuard(_symbol, parameters.Risk, broker);
        _safety = new SafetyActions(_symbol, parameters.Execution, broker, _requests, _events);
        _tradeJournal = new TradeJournal(_symbol, parameters.Risk, broker, store, journal);
        _recovery = new RecoveryService(_symbol, parameters.Signal, parameters.Execution, parameters.Risk, _newsEnabled, broker, store, journal, _requests, _events, EnterStateAfterRecovery);
    }

    /// <summary>The current state (REC-001).</summary>
    internal BotState State => _machine.State;

    /// <summary>The causes of the current halt (REC-001).</summary>
    internal IReadOnlyList<HaltCause> HaltCauses => _machine.HaltCauses;

    /// <summary>The non-terminal zones of the live signal layer; empty before recovery completes (REC-004).</summary>
    internal IReadOnlyList<Zone> Zones => _engine == null ? Array.Empty<Zone>() : _engine.Zones;

    /// <summary>The bias of the live signal layer (REC-004).</summary>
    internal Bias Bias => _engine == null ? Bias.Neutral : _engine.Bias;

    /// <summary>The report of the last recovery run (REC-010).</summary>
    internal RecoveryReport? LastReport { get; private set; }

    /// <summary>True when the weekend close waits for the market to reopen (REC-009).</summary>
    internal bool WeekendClosePending => _weekendClosePending;

    /// <summary>True while the daily budget blocks entries (RSK-006).</summary>
    internal bool BudgetBlocked => _budgetBlockedDayUtc.HasValue;

    /// <summary>True while a data gap blocks entries (REC-003).</summary>
    internal bool DataGap => _dataGap;

    /// <summary>Errors whose journal line could not be written (REC-011).</summary>
    internal int UnjournaledErrors => _unjournaledErrors;

    /// <summary>
    /// Start (spec 9.3): journals the start-up summary, loads the news calendar when NewsEnabled is
    /// true, and runs recovery (REC-002).
    /// </summary>
    /// <param name="history">Closed-bar history port.</param>
    public void OnStart(IBarHistory history)
    {
        Guard(nameof(OnStart), () => Start(history, "start"));
    }

    /// <summary>Reconnect (spec 8.4): runs the same recovery as the start (REC-002).</summary>
    /// <param name="history">Closed-bar history port.</param>
    public void OnConnected(IBarHistory history)
    {
        Guard(nameof(OnConnected), () => Start(history, "reconnect"));
    }

    /// <summary>Disconnect (spec 8.4): marks the state Recovering and stops sending requests until the next recovery (REC-002).</summary>
    public void OnDisconnected()
    {
        Guard(nameof(OnDisconnected), Disconnect);
    }

    /// <summary>
    /// A closed bar (spec 9.3). Ignored in Recovering. Otherwise the bar is fed to the signal
    /// engine; at each M15 close the protection check (EXE-011), the risk guards (RSK-006, RSK-008,
    /// RSK-009), the weekend duty, the resolution of unclear placements (EXE-009) and the filters
    /// and placement of new signals follow.
    /// </summary>
    /// <param name="timeFrame">Timeframe of the bar.</param>
    /// <param name="bar">The bar that has just closed.</param>
    public void OnBarClosed(TimeFrame timeFrame, Bar bar)
    {
        Guard(nameof(OnBarClosed), () => BarClosed(timeFrame, bar));
    }

    /// <summary>
    /// Once a minute (spec 9.3): heartbeat hook in every state; then, while connected, news refresh,
    /// recovery retry, Friday close (EXE-013), weekend close (REC-009), state update and the
    /// inactivity hook.
    /// </summary>
    public void OnTimer()
    {
        Guard(nameof(OnTimer), Timer);
    }

    /// <summary>Any position or order event (spec 9.3): protection check (EXE-011), weekend duty and the trade journal (REC-008).</summary>
    public void OnTradeEvent()
    {
        Guard(nameof(OnTradeEvent), TradeEvent);
    }

    /// <summary>An error reported by the platform: journaled, counted and alerted like a handler error (REC-011).</summary>
    /// <param name="exception">The exception.</param>
    public void OnException(Exception exception)
    {
        Guard(nameof(OnException), () => RecordError("platform", exception ?? new ArgumentNullException(nameof(exception))));
    }

    private static NewsService CreateNews(BotParameters parameters, IKeyValueStore store, IHttpTransport http, bool isBacktest)
    {
        if (!parameters.News.NewsEnabled)
        {
            NewsParameters off = parameters.News with { ExtraBlackouts = string.Empty };
            return new NewsService(off, Array.Empty<NewsFeed>(), store, http, true, NoNewsHistory);
        }

        if (isBacktest)
        {
            return new NewsService(parameters.News, Array.Empty<NewsFeed>(), store, http, true, parameters.EmbeddedNewsHistory);
        }

        var feed = new NewsFeed(parameters.NewsFeedUrl, new ForexFactoryParser());
        return new NewsService(parameters.News, new[] { feed }, store, http, false, string.Empty);
    }

    // ------------------------------------------------------------------ handlers

    private void Start(IBarHistory history, string reason)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _disconnected = false;
        DateTime now = Now();
        Write("start", "event=" + reason + " symbol=" + _symbol + " version=" + BuildInfo.Version + " hash=" + _parameterHash
            + " news=" + (_newsEnabled ? "enabled" : "disabled") + " backtest=" + (_isBacktest ? "yes" : "no")
            + " risk-epoch=" + _risk.RiskEpochDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (_newsEnabled)
        {
            RefreshNews(now);
        }

        RunRecovery(now, reason);
        CheckRequestLimit(now);
    }

    private void Disconnect()
    {
        _disconnected = true;
        DateTime now = Now();
        Write("connection", "event=disconnected");
        ApplyState("disconnected", now);
    }

    private void BarClosed(TimeFrame timeFrame, Bar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (_disconnected || _recoveryPending || _engine == null)
        {
            // Recovering: the bar is part of the history the next recovery replays (prompt WP07).
            return;
        }

        DateTime now = Now();
        int rank = BarIntakeOrder.Rank(timeFrame);
        DateTime close = BarIntakeOrder.CloseTimeUtc(timeFrame, bar);
        if (close > now)
        {
            throw new InvalidOperationException("The " + timeFrame + " bar opening " + Stamp(bar.OpenTimeUtc) + " has not closed at server time " + Stamp(now) + " (closed bars only).");
        }

        if (_hasFed && !(close > _lastFedCloseUtc || (close == _lastFedCloseUtc && rank > _lastFedRank)))
        {
            Write("bar", "event=ignored timeframe=" + timeFrame + " open=" + Stamp(bar.OpenTimeUtc) + " reason=already processed");
            return;
        }

        CheckLiveGap(timeFrame, bar, now);
        IReadOnlyList<Signal> signals = _engine.OnBarClosed(timeFrame, bar, false);
        _lastBars[rank] = bar;
        _hasFed = true;
        _lastFedCloseUtc = close;
        _lastFedRank = rank;
        if (timeFrame != TimeFrame.M15)
        {
            // D1 and H4 bars close together with an M15 bar, which comes last (SIG-004); the duties run once, there.
            return;
        }

        AddRecent(bar);
        ClearExpiredCauses(now);
        JournalActions("protection", _trades.EnsureProtection());
        GuardStatus guard = _guard.Evaluate(now);
        ApplyGuard(guard, now, false);
        if (_machine.State == BotState.Halted)
        {
            _trades.CancelEntryOrders("halted");
        }

        WeekendDuty(now);
        UpdateCalendar(now);
        ApplyState("M15 bar closed at " + Stamp(close), now);
        ResolvePending(guard, now);
        PlaceSignals(signals, guard, now);
        CheckRequestLimit(now);
        TouchLastSeen(now);
    }

    private void Timer()
    {
        DateTime now = Now();
        _hooks.Heartbeat(now, _machine.State);
        if (_disconnected)
        {
            return;
        }

        if (_newsEnabled)
        {
            RefreshNews(now);
        }

        if ((_recoveryPending || _dataGap) && _history != null && RecoveryDue(now))
        {
            RunRecovery(now, _dataGap ? "data gap" : "waiting for data");
        }

        ClearExpiredCauses(now);
        if (_machine.State != BotState.Recovering)
        {
            JournalActions("friday-close", _trades.FridayClose(now));
            WeekendDuty(now);
        }

        UpdateCalendar(now);
        ApplyState("timer", now);
        _hooks.Inactivity(now);
        CheckRequestLimit(now);
        TouchLastSeen(now);
    }

    private void TradeEvent()
    {
        if (_disconnected)
        {
            return;
        }

        DateTime now = Now();
        JournalActions("protection", _trades.EnsureProtection());
        if (_machine.State != BotState.Recovering)
        {
            WeekendDuty(now);
        }

        RecordTrades(now);
        CheckRequestLimit(now);
    }

    private void RecordTrades(DateTime now)
    {
        try
        {
            _tradeJournal.Record(now);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            // Broker history that cannot be read is a data problem, not a programming error: journaled, and the
            // trades are journaled at a later event (REC-008, architecture section 10 "Bad broker data").
            Write("trade", "event=journal-unavailable reason=" + exception.GetType().Name + ": " + OneLine(exception.Message));
        }
    }

    // ------------------------------------------------------------------ recovery and state

    private void RunRecovery(DateTime now, string reason)
    {
        if (_machine.State == BotState.Running || _machine.State == BotState.Blocked)
        {
            Record(_machine.Transition(BotState.Recovering, reason, now));
        }

        _recoveryPending = true;
        _lastRecoveryAttemptUtc = now;
        RecoveryReport report = _recovery.Run(_history!);
        LastReport = report;
        Write("recovery", "reason=" + reason + " " + report.Text);
        if (!report.DataComplete)
        {
            if (!_dataAlertSent)
            {
                _dataAlertSent = true;
                Alert(BotAlertKind.DataIncomplete, "recovery waits for data: " + string.Join("; ", report.DataProblems), now);
            }

            return;
        }

        _dataAlertSent = false;
        Alert(BotAlertKind.RecoveryReport, report.Text, now);
        if (report.Strangers.Count > 0)
        {
            Alert(BotAlertKind.Stranger, "kept with their stop and target: " + string.Join(", ", report.Strangers), now);
        }
    }

    private BotState EnterStateAfterRecovery(RecoveryFindings findings)
    {
        DateTime now = findings.AtUtc;
        if (!findings.DataComplete || findings.Engine == null)
        {
            _engine = null;
            _recoveryPending = true;
            _dataGap = false;
            ApplyState("recovery waits for data", now);
            return _machine.State;
        }

        _engine = findings.Engine;
        _hasFed = false;
        foreach (TimeFrame timeFrame in BarIntakeOrder.SameInstant)
        {
            Bar? last = findings.LastBar(timeFrame);
            int rank = BarIntakeOrder.Rank(timeFrame);
            _lastBars[rank] = last;
            if (last != null)
            {
                DateTime close = BarIntakeOrder.CloseTimeUtc(timeFrame, last);
                if (!_hasFed || close > _lastFedCloseUtc || (close == _lastFedCloseUtc && rank > _lastFedRank))
                {
                    _hasFed = true;
                    _lastFedCloseUtc = close;
                    _lastFedRank = rank;
                }
            }
        }

        _recentM15.Clear();
        _recentM15.AddRange(findings.RecentM15);
        _recoveryPending = false;
        _dataGap = false;
        _weekendClosePending = findings.WeekendClosePending;
        if (findings.Guard != null)
        {
            ApplyGuard(findings.Guard, now, true);
        }

        UpdateCalendar(now);
        ApplyState("recovery complete", now);
        return _machine.State;
    }

    private void ApplyGuard(GuardStatus guard, DateTime now, bool fromRecovery)
    {
        if (guard.EmergencyFires)
        {
            if (!fromRecovery)
            {
                JournalActions("emergency", _safety.CloseAllBotTrades("emergency guard"));
            }

            EnterHalt(HaltCause.EmergencyGuard, "emergency guard: " + guard.Text, now);
        }
        else if (guard.KillSwitchFires)
        {
            EnterHalt(HaltCause.KillSwitch, "kill switch: " + guard.Text, now);
        }

        if (guard.BudgetExhausted)
        {
            if (!fromRecovery)
            {
                JournalActions("daily-budget", _trades.CancelEntryOrders("daily budget exhausted"));
            }

            DateTime day = CestClock.StartOfCestDayUtc(now);
            if (_budgetBlockedDayUtc != day)
            {
                _budgetBlockedDayUtc = day;
                Alert(BotAlertKind.DailyLimit, "daily risk budget exhausted, no entries until the next CE(S)T day: " + guard.Text, now);
            }
        }

        if (guard.EntriesAllowed)
        {
            _riskDataAlertSent = false;
            return;
        }

        Write("risk", "event=unavailable " + guard.Text);
        if (!_riskDataAlertSent)
        {
            _riskDataAlertSent = true;
            Alert(BotAlertKind.RiskDataUnavailable, "no new entries: " + guard.Text, now);
        }
    }

    private void EnterHalt(HaltCause cause, string detail, DateTime now)
    {
        bool known = _machine.HaltCauses.Contains(cause);
        StateTransition? transition = _machine.Halt(cause, detail, now);
        if (transition != null)
        {
            Record(transition);
        }

        if (!known)
        {
            Alert(AlertFor(cause), detail, now);
        }

        // Spec 8.5: in Halted the pending orders are cancelled.
        JournalActions("halt", _trades.CancelEntryOrders("halted: " + cause));
    }

    private void ApplyState(string reason, DateTime now)
    {
        if (_machine.State == BotState.Halted)
        {
            if (_machine.CanReleaseHalt(now))
            {
                Record(_machine.ReleaseHalt(DesiredState(), "error storm ended: next CE(S)T day", now));
            }

            return;
        }

        BotState desired = DesiredState();
        if (desired != _machine.State)
        {
            Record(_machine.Transition(desired, reason + Causes(desired), now));
        }
    }

    private BotState DesiredState()
    {
        if (_disconnected || _recoveryPending || _engine == null)
        {
            return BotState.Recovering;
        }

        return _calendarUnavailable || _budgetBlockedDayUtc.HasValue || _dataGap ? BotState.Blocked : BotState.Running;
    }

    private string Causes(BotState state)
    {
        if (state != BotState.Blocked)
        {
            return string.Empty;
        }

        var causes = new List<string>();
        if (_calendarUnavailable)
        {
            causes.Add("calendar unavailable");
        }

        if (_budgetBlockedDayUtc.HasValue)
        {
            causes.Add("daily budget exhausted");
        }

        if (_dataGap)
        {
            causes.Add("data gap");
        }

        return " (" + string.Join(", ", causes) + ")";
    }

    private void ClearExpiredCauses(DateTime now)
    {
        if (_budgetBlockedDayUtc.HasValue && CestClock.StartOfCestDayUtc(now) > _budgetBlockedDayUtc.Value)
        {
            _budgetBlockedDayUtc = null;
            Write("risk", "event=daily-budget-reset day=" + Stamp(CestClock.StartOfCestDayUtc(now)));
        }
    }

    private void UpdateCalendar(DateTime now)
    {
        _calendarUnavailable = _newsEnabled && _news.StatusAt(now) == NewsStatus.Unavailable;
    }

    private bool RecoveryDue(DateTime now)
    {
        return !_lastRecoveryAttemptUtc.HasValue || now - _lastRecoveryAttemptUtc.Value >= RecoveryRetryInterval || _dataGap;
    }

    private void CheckLiveGap(TimeFrame timeFrame, Bar bar, DateTime now)
    {
        Bar? previous = _lastBars[BarIntakeOrder.Rank(timeFrame)];
        if (previous == null || !BarSeriesCheck.IsGap(timeFrame, previous, bar, _fridayCloseUtc))
        {
            return;
        }

        string detail = timeFrame + " bars missing between " + Stamp(previous.OpenTimeUtc) + " and " + Stamp(bar.OpenTimeUtc);
        Write("bar", "event=gap " + detail);
        if (!_dataGap)
        {
            _dataGap = true;
            Alert(BotAlertKind.DataGap, detail + "; entries blocked until recovery reloads the history", now);
        }
    }

    // ------------------------------------------------------------------ duties

    private void WeekendDuty(DateTime now)
    {
        bool exposure = _safety.HasSymbolExposure();
        if (_safety.InWeekendWindow(now) && exposure)
        {
            _weekendClosePending = true;
        }

        if (!_weekendClosePending)
        {
            return;
        }

        if (!exposure)
        {
            _weekendClosePending = false;
            return;
        }

        if (!_safety.MarketOpen())
        {
            return;
        }

        JournalActions("weekend-close", _safety.CloseSymbolTrades("weekend close"));
        _weekendClosePending = _safety.HasSymbolExposure();
    }

    private void ResolvePending(GuardStatus guard, DateTime now)
    {
        if (_orders.UnresolvedCount == 0 || !_machine.AllowsNewOrders || !guard.EntriesAllowed)
        {
            // A resend is a new order: only in Running (REC-001). Until then the placement waits, and the
            // order manager abandons it once the signal is too old (EXE-009).
            return;
        }

        foreach (PlaceResult result in _orders.ResolvePending(_recentM15))
        {
            Write("order", "event=resolved label=" + result.Label + " outcome=" + result.Outcome + " detail=" + result.Detail);
            if (result.Outcome == PlaceOutcome.RequestLimitReached)
            {
                EnterHalt(HaltCause.RequestLimit, "request limit: " + result.Detail, now);
            }
        }
    }

    private void PlaceSignals(IReadOnlyList<Signal> signals, GuardStatus guard, DateTime now)
    {
        foreach (Signal signal in SignalOrder.ForEvaluation(signals))
        {
            if (!guard.EntriesAllowed)
            {
                string why = "risk data unavailable: " + (guard.DataProblem ?? "a guard could not be computed");
                _events.Skipped(new SkipRecord(_symbol, signal.ZoneId, SkipReason.BrokerDataInvalid, 0.0, 0.0, why, now));
                continue;
            }

            var context = new PreTradeContext(_symbol, _machine.State, _engine!.Zones, _orders.HasUnresolved(_symbol));
            PreTradeResult result = _filters.Evaluate(signal, context);
            if (!result.IsAllowed)
            {
                continue;
            }

            CircuitBreakerDecision check = _requests.CheckBeforeRequest(now);
            if (check.Halt)
            {
                // RSK-010: the request that would exceed the limit is never sent; the instance halts.
                _events.Skipped(new SkipRecord(_symbol, signal.ZoneId, SkipReason.RequestLimitReached, check.LastHourCount, check.HourLimit, check.Detail, now));
                EnterHalt(HaltCause.RequestLimit, "request limit: " + check.Detail, now);
                continue;
            }

            PlaceResult placed = _orders.TryPlace(result.Order!);
            Write("order", "event=placement label=" + placed.Label + " outcome=" + placed.Outcome + " detail=" + placed.Detail);
            if (placed.Outcome == PlaceOutcome.RequestLimitReached)
            {
                EnterHalt(HaltCause.RequestLimit, "request limit: " + placed.Detail, now);
            }
        }
    }

    private void CheckRequestLimit(DateTime now)
    {
        CircuitBreakerDecision decision = _requests.Evaluate(now);
        if (decision.Halt)
        {
            EnterHalt(HaltCause.RequestLimit, "request limit: " + decision.Detail, now);
        }
    }

    private void RefreshNews(DateTime now)
    {
        NewsUpdate update = _news.Refresh(now);
        foreach (NewsFeedReport report in update.Reports)
        {
            Write("news", "source=" + report.Source + " outcome=" + report.Outcome + " detail=" + report.Detail);
        }

        if (update.AlertRaised)
        {
            Alert(BotAlertKind.CalendarUnavailable, "news calendar unavailable: no new entries until a feed returns", now);
        }

        if (update.MalformedDownloadAlert)
        {
            Alert(BotAlertKind.NewsFeedMalformed, "a news download contained a malformed event and was rejected", now);
        }

        if (update.Recovered)
        {
            Write("news", "event=calendar-available");
        }
    }

    private void AddRecent(Bar bar)
    {
        _recentM15.Add(bar);
        if (_recentM15.Count > RecoveryService.RecentM15Count)
        {
            _recentM15.RemoveAt(0);
        }
    }

    private void TouchLastSeen(DateTime now)
    {
        _store.Set(RecoveryService.LastSeenKey, now.ToString("o", CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------ errors, journal, alerts

    private void Guard(string handler, Action body)
    {
        try
        {
            body();
        }
        catch (Exception exception)
        {
            RecordError(handler, exception);
        }
    }

    private void RecordError(string handler, Exception exception)
    {
        // Never throws: this is the last line of defence of every handler (REC-011).
        DateTime now = SafeNow();
        CircuitBreakerDecision decision = _errors.Record(now);
        string text = "handler=" + handler + " type=" + exception.GetType().Name + " message=" + OneLine(exception.Message)
            + " errors-last-hour=" + decision.LastHourCount.ToString(CultureInfo.InvariantCulture);
        TryWrite("error", text);
        TryAlert(BotAlertKind.Error, text, now);
        if (!decision.Halt)
        {
            return;
        }

        try
        {
            EnterHalt(HaltCause.ErrorLimit, "error storm: " + decision.Detail, now);
        }
        catch (Exception haltException)
        {
            // The state is Halted already (the machine changes before the cancels); the failed cancels are journaled.
            TryWrite("error", "handler=halt type=" + haltException.GetType().Name + " message=" + OneLine(haltException.Message));
        }
    }

    private DateTime Now()
    {
        DateTime now = _broker.ServerTimeUtc;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The broker's server time is not UTC (REC-012).");
        }

        _lastKnownServerTimeUtc = now;
        return now;
    }

    private DateTime SafeNow()
    {
        try
        {
            return Now();
        }
        catch (Exception exception)
        {
            // Used only while recording another error: the time falls back to the last one known, and the
            // failure is journaled with it.
            TryWrite("error", "handler=time type=" + exception.GetType().Name + " message=" + OneLine(exception.Message));
            return _lastKnownServerTimeUtc ?? FallbackTimeUtc;
        }
    }

    private void Record(StateTransition transition)
    {
        Write("state", "from=" + transition.From + " to=" + transition.To + " reason=" + transition.Reason);
        Alert(BotAlertKind.StateChanged, transition.From + " -> " + transition.To + ": " + transition.Reason, transition.AtUtc);
    }

    private void JournalActions(string what, IReadOnlyList<TradeAction> actions)
    {
        foreach (TradeAction action in actions)
        {
            Write("action", "duty=" + what + " id=" + action.Id.ToString(CultureInfo.InvariantCulture) + " outcome=" + action.Outcome + " detail=" + action.Detail);
        }
    }

    private void Alert(BotAlertKind kind, string message, DateTime now, AlertKind? executionKind = null)
    {
        var alert = new BotAlert(kind, executionKind, _symbol, message, now);
        Write("alert", "kind=" + kind + (executionKind.HasValue ? "/" + executionKind.Value : string.Empty) + " message=" + message);
        _hooks.Alert(alert);
    }

    private void TryAlert(BotAlertKind kind, string message, DateTime now)
    {
        try
        {
            Alert(kind, message, now);
        }
        catch (Exception exception)
        {
            TryWrite("error", "handler=alert type=" + exception.GetType().Name + " message=" + OneLine(exception.Message));
        }
    }

    private void Write(string category, string line)
    {
        _journal.Write(category, line);
    }

    private void TryWrite(string category, string line)
    {
        try
        {
            _journal.Write(category, line);
        }
        catch (Exception)
        {
            // The journal itself failed while an error was being recorded; it is counted, not lost silently.
            _unjournaledErrors++;
        }
    }

    private static BotAlertKind AlertFor(HaltCause cause)
    {
        switch (cause)
        {
            case HaltCause.KillSwitch:
                return BotAlertKind.KillSwitch;
            case HaltCause.EmergencyGuard:
                return BotAlertKind.EmergencyGuard;
            case HaltCause.RequestLimit:
                return BotAlertKind.CircuitBreaker;
            default:
                return BotAlertKind.ErrorStorm;
        }
    }

    private static string OneLine(string? text)
    {
        return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Connects the execution layer's skip records and alerts to the journal and the alert hook (EXE-014, spec 11).</summary>
    private sealed class CoreEvents : IExecutionEvents
    {
        private readonly BotCore _core;

        internal CoreEvents(BotCore core)
        {
            _core = core;
        }

        public void Skipped(SkipRecord skip)
        {
            _core.Write(
                "skip",
                "zone=" + skip.ZoneId + " reason=" + skip.Reason
                + " measured=" + skip.Measured.ToString("R", CultureInfo.InvariantCulture)
                + " limit=" + skip.Limit.ToString("R", CultureInfo.InvariantCulture)
                + " at=" + Stamp(skip.AtUtc) + " detail=" + skip.Detail);
        }

        public void Alert(ExecutionAlert alert)
        {
            _core.Alert(BotAlertKind.Execution, alert.Message, _core.SafeNow(), alert.Kind);
        }
    }
}
