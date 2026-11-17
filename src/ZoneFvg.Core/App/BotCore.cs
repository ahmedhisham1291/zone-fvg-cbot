using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Domain.Invariant;

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
/// <para>In a backtest with <see cref="BotParameters.BacktestHaltsLogOnly"/> (REC-014) the halts and
/// the daily budget block are only journaled as would-halt or would-block; live this never applies.</para>
/// </remarks>
public sealed class BotCore
{
    /// <summary>
    /// Key-value store key of the CE(S)T day on which the daily budget was found exhausted (RSK-006,
    /// owner decision of 2026-11-16). The value can only add a block until the next CE(S)T day; a
    /// missing, unreadable or other-day value is ignored and the budget is recomputed.
    /// </summary>
    internal const string BudgetExhaustedKey = "risk.budgetExhaustedDay";

    /// <summary>Key-value store key of the start times of the last eight days, for the daily summary (OPS-003).</summary>
    internal const string StartsKey = "app.starts";

    /// <summary>
    /// Key-value store key of the server time of the last inactivity alert (OPS-006, changed on
    /// 2026-11-17). After a restart no inactivity alert is raised within 3 days of it; a lost or
    /// unreadable value only removes this limit.
    /// </summary>
    internal const string InactivityAlertKey = "ops.lastInactivityAlertUtc";

    private const double InactivityHistoryDays = 30.0;

    // OPS-002: M15 bars read for the moves of one closed trade, at most 30 days. A longer trade gets NA.
    private const int MaxExcursionBars = 2880;

    // REC-014 journal values.
    private const string WouldHalt = "would-halt";
    private const string WouldBlock = "would-block";
    private const string DailyBudgetCause = "DailyBudget";

    private static readonly TimeSpan RecoveryRetryInterval = TimeSpan.FromMinutes(15.0);
    private static readonly IReadOnlyList<Zone> NoZones = Array.AsReadOnly(Array.Empty<Zone>());
    private static readonly IReadOnlyList<Fvg> NoFvgs = Array.AsReadOnly(Array.Empty<Fvg>());
    private static readonly DateTime FallbackTimeUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The news filter is switched off by a calendar that covers 2000 to 2099 with low-impact events
    // only: no window, never Unavailable, no download (owner decision of 2026-11-16, NewsEnabled).
    private const string NoNewsHistory =
        "2000-01-03 00:00,USD,Low,News filter disabled\n2099-12-28 00:00,USD,Low,News filter disabled\n";

    private readonly IBroker _broker;
    private readonly IKeyValueStore _store;
    private readonly Operations _ops;
    private readonly DateTime _startedUtc;
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
    private readonly LogRepeatLimits _limits = new LogRepeatLimits();
    private readonly ZoneOutcomeCounts _zoneCounts = new ZoneOutcomeCounts();
    private readonly Bar?[] _lastBars = new Bar?[3];
    private readonly List<Bar> _recentM15 = new List<Bar>();
    private readonly SortedSet<string> _newsSourcesDown = new SortedSet<string>(StringComparer.Ordinal);
    private readonly IReadOnlyList<MonthDay> _marketHolidays;
    private readonly bool _writeTradeLog;
    private readonly bool _haltsLogOnly;

    // REC-014: per "event cause", the CE(S)T day of the last would-halt or would-block line.
    private readonly SortedDictionary<string, DateTime> _recordedDays = new SortedDictionary<string, DateTime>(StringComparer.Ordinal);

    private SignalEngine? _engine;
    private IBarHistory? _history;
    private bool _disconnected;
    private bool _recoveryPending = true;
    private bool _dataGap;
    private bool _dataAlertSent;
    private bool _riskDataAlertSent;
    private bool _inactivityAlertRestored;
    private bool _calendarUnavailable;
    private bool _weekendClosePending;
    private DateTime? _budgetBlockedDayUtc;
    private DateTime? _lastRecoveryAttemptUtc;
    private DateTime? _lastKnownServerTimeUtc;
    private bool _hasFed;
    private DateTime _lastFedCloseUtc;
    private int _lastFedRank;
    private int _tradeLogLines;
    private DateTime? _fridayCloseDeferredDay;

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
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(http);
        parameters.Validate(isBacktest);
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(journal);
        _startedUtc = broker.ServerTimeUtc;
        _lastKnownServerTimeUtc = _startedUtc;
        _ops = new Operations(parameters.Ops, parameters.Risk, parameters.Symbol, broker, http, journal, _startedUtc);
        _ops.Inactivity.CountNoEarlierThan(parameters.Risk.RiskEpochDate);
        _isBacktest = isBacktest;
        _symbol = parameters.Symbol;
        _newsEnabled = parameters.News.NewsEnabled;
        _fridayCloseUtc = parameters.Execution.FridayCloseUtc;
        _marketHolidays = MonthDay.ParseList(parameters.MarketHolidays);
        _writeTradeLog = parameters.WriteTradeLog;
        _haltsLogOnly = parameters.BacktestHaltsLogOnly;
        _risk = parameters.Risk;
        _parameterHash = ParameterHash.Compute(parameters.Signal, parameters.Execution);
        _news = CreateNews(parameters, store, http, isBacktest);
        _requests = new RequestCounter(parameters.Risk);
        _errors = new ErrorCounter(parameters.Risk);
        _events = new CoreEvents(this);
        _filters = new PreTradeFilters(parameters.Execution, parameters.Signal, parameters.Risk, broker, _news, _events, _haltsLogOnly);
        _orders = new OrderManager(broker, _requests, _events, _haltsLogOnly);
        _trades = new TradeManager(_symbol, parameters.Execution, broker, _requests, _events);
        _guard = new AccountGuard(_symbol, parameters.Risk, broker);
        _safety = new SafetyActions(_symbol, parameters.Execution, broker, _requests, _events);
        var tradeEvents = new CoreTradeEvents(this);
        _tradeJournal = new TradeJournal(_symbol, parameters.Risk, broker, store, tradeEvents);
        _recovery = new RecoveryService(_symbol, parameters.Signal, parameters.Execution, parameters.Risk, _newsEnabled, broker, store, tradeEvents, _requests, _events, EnterStateAfterRecovery, _marketHolidays, _haltsLogOnly);
    }

    /// <summary>The current state (REC-001).</summary>
    internal BotState State => _machine.State;

    /// <summary>The causes of the current halt (REC-001).</summary>
    internal IReadOnlyList<HaltCause> HaltCauses => _machine.HaltCauses;

    /// <summary>The non-terminal zones of the live signal layer; empty before recovery completes (REC-004).</summary>
    internal IReadOnlyList<Zone> Zones => _engine == null ? Array.Empty<Zone>() : _engine.Zones;

    /// <summary>
    /// Read-only copy of the non-terminal zones of the live signal layer, ordered by id, for chart
    /// drawing (PLT-005). Empty before the first recovery completes. Reading it changes nothing;
    /// the copy does not change when later bars arrive.
    /// </summary>
    public IReadOnlyList<Zone> ZoneSnapshot => _engine == null ? NoZones : Array.AsReadOnly(_engine.Zones.ToArray());

    /// <summary>
    /// Read-only copy of the fair value gaps confirmed in the last 24 hours of bar time by the live
    /// signal layer, for chart drawing (PLT-005; see <see cref="SignalEngine.RecentFvgs"/>). Empty
    /// before the first recovery completes. Reading it changes nothing.
    /// </summary>
    public IReadOnlyList<Fvg> FvgSnapshot => _engine == null ? NoFvgs : _engine.RecentFvgs;

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
    internal int UnjournaledErrors => _ops.Journal.WriteFailures;

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
    /// Once a minute (spec 9.3): while connected, news refresh, recovery retry, and in every
    /// connected state, Recovering included (REC-015), the emergency guard (RSK-009), Friday close
    /// (EXE-013) and weekend close (REC-009); then the state update; then in every state the daily summary,
    /// the inactivity check, the heartbeat and alert sending (OPS-003 to OPS-006).
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

        // OPS-008: the first failed recovery after a start or a reconnect is written again.
        _limits.ResetRecovery();
        DateTime now = Now();
        Write(
            "start",
            ("event", reason),
            ("symbol", _symbol),
            ("version", BuildInfo.Version),
            ("hash", _parameterHash),
            ("news", _newsEnabled ? "enabled" : "disabled"),
            ("backtest", _isBacktest ? "yes" : "no"),
            ("riskEpoch", _risk.RiskEpochDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("holidays", string.Join(",", _marketHolidays)));
        if (reason == "start")
        {
            RecordStart(now);
        }
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
        Write("connection", ("event", "disconnected"));
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
            throw new InvalidOperationException("The " + timeFrame + " bar opening " + Minute(bar.OpenTimeUtc) + " has not closed at server time " + Minute(now) + " (closed bars only).");
        }

        if (_hasFed && !(close > _lastFedCloseUtc || (close == _lastFedCloseUtc && rank > _lastFedRank)))
        {
            Write("bar", ("event", "ignored"), ("timeframe", timeFrame.ToString()), ("open", Minute(bar.OpenTimeUtc)), ("reason", "already processed"));
            return;
        }

        CheckLiveGap(timeFrame, bar, now);
        IReadOnlyList<Zone> zonesBefore = _engine.Zones;
        IReadOnlyList<Signal> signals = _engine.OnBarClosed(timeFrame, bar, false);
        _lastBars[rank] = bar;
        _hasFed = true;
        _lastFedCloseUtc = close;
        _lastFedRank = rank;
        if (timeFrame != TimeFrame.M15)
        {
            // D1 and H4 bars close together with an M15 bar, which comes last (SIG-004); the duties run once, there.
            CountZoneOutcomes(timeFrame, close, _engine.LastActivity);
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

        WeekendDuty();
        UpdateCalendar(now);
        ApplyState("M15 bar closed at " + Minute(close), now);
        ResolvePending(guard, now);
        PlaceSignals(signals, zonesBefore, guard, now);

        // OPS-009 is journal only: counted after the bar state is updated and its signals are placed, so a failure here cannot lose a signal.
        CountZoneOutcomes(timeFrame, close, _engine.LastActivity);
        CheckRequestLimit(now);
        TouchLastSeen(now);
    }

    // OPS-009, journal only: at the first live M15 bar of a later CE(S)T day the earlier days are written,
    // before this bar's own counts are added. Bars replayed by recovery never come here.
    private void CountZoneOutcomes(TimeFrame timeFrame, DateTime close, ZoneActivity activity)
    {
        if (timeFrame == TimeFrame.M15)
        {
            foreach ((string Key, string? Value)[] fields in _zoneCounts.TakeDaysBefore(close))
            {
                Write("zones", fields);
            }
        }

        _zoneCounts.Add(close, activity);
    }

    private void Timer()
    {
        DateTime now = Now();
        try
        {
            if (!_disconnected)
            {
                ConnectedTimerDuties(now);
            }

            // In every state (WP09): the daily summary and the inactivity check, whose alerts go out in the pump below.
            _ops.Summary.Pump(now, () => SummaryInput(now));
            CheckInactivity(now);
        }
        finally
        {
            // Heartbeat and alert sending in every state, also after an exception above (OPS-004, OPS-005).
            _ops.OnTimer(now, _machine.State);
        }
    }

    private void ConnectedTimerDuties(DateTime now)
    {
        if (_newsEnabled)
        {
            RefreshNews(now);
        }

        if ((_recoveryPending || _dataGap) && _history != null && RecoveryDue(now))
        {
            RunRecovery(now, _dataGap ? "data gap" : "waiting for data");
        }

        ClearExpiredCauses(now);

        // In every connected state, Recovering included (REC-015, owner decision B of 2026-11-17): these duties
        // read only broker data. The emergency guard is checked every minute as well as at M15 closes (owner
        // decision of 2026-11-16). Friday close against a closed market is deferred to the reopen (EXE-016).
        CheckEmergencyGuard(now);
        FridayCloseDuty(now);
        WeekendDuty();

        UpdateCalendar(now);
        ApplyState("timer", now);
        CheckRequestLimit(now);
        TouchLastSeen(now);
    }

    private DailySummaryInput SummaryInput(DateTime now)
    {
        NewsStatus? calendar = _newsEnabled ? _news.StatusAt(now) : null;
        return new DailySummaryInput(_guard.TrySnapshot(now), calendar, now - _startedUtc, StartsInPreviousDay(now));
    }

    private void CheckInactivity(DateTime now)
    {
        // At least 30 days of history, so the 21-day warning has data (OPS-006, WP09).
        DateTime since = now.AddDays(-InactivityHistoryDays);
        RestoreLastInactivityAlert(now);
        InactivityStatus status;
        try
        {
            status = _ops.Inactivity.Check(now, _broker.GetHistory(since), since, _broker.GetPositions());
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            Write("ops", ("event", "inactivity-unavailable"), ("reason", exception.GetType().Name + ": " + OneLine(exception.Message)));
            return;
        }

        if (status.AlertRaised)
        {
            _store.Set(InactivityAlertKey, now.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    private void RestoreLastInactivityAlert(DateTime now)
    {
        // OPS-006: read once after a start; a missing value means no limit, an unreadable one (or one later than the
        // server time) is journaled once and ignored.
        if (_inactivityAlertRestored)
        {
            return;
        }

        _inactivityAlertRestored = true;
        string? text = _store.Get(InactivityAlertKey);
        if (text == null)
        {
            return;
        }

        if (TryParseUtc(text, out DateTime lastAlert) && lastAlert <= now)
        {
            _ops.Inactivity.RestoreLastAlert(lastAlert);
            return;
        }

        Write("ops", ("event", "stored-inactivity-alert-ignored"), ("value", OneLine(text)), ("reason", "unreadable"));
    }

    private void RecordStart(DateTime now)
    {
        // Start times of the last eight days, for the restarts in the daily summary (OPS-003).
        var starts = new List<string>();
        string? stored = _store.Get(StartsKey);
        if (!string.IsNullOrEmpty(stored))
        {
            foreach (string token in stored.Split(','))
            {
                if (TryParseUtc(token, out DateTime time) && time > now.AddDays(-8.0) && time <= now)
                {
                    starts.Add(token);
                }
            }
        }

        starts.Add(now.ToString("o", CultureInfo.InvariantCulture));
        _store.Set(StartsKey, string.Join(",", starts));
    }

    private int? StartsInPreviousDay(DateTime now)
    {
        DateTime today = CestClock.StartOfCestDayUtc(now);
        DateTime previous = CestClock.StartOfCestDayUtc(today.AddTicks(-1));
        string? stored = _store.Get(StartsKey);
        if (stored == null)
        {
            return null;
        }

        int count = 0;
        foreach (string token in stored.Split(','))
        {
            if (TryParseUtc(token, out DateTime time) && time >= previous && time < today)
            {
                count++;
            }
        }

        return count;
    }

    private void TradeEvent()
    {
        if (_disconnected)
        {
            return;
        }

        DateTime now = Now();
        JournalActions("protection", _trades.EnsureProtection());

        // In every connected state, Recovering included (REC-015).
        WeekendDuty();
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
            Write("trade", ("event", "journal-unavailable"), ("reason", exception.GetType().Name + ": " + OneLine(exception.Message)));
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
        JournalRecovery(report, now, reason);
        AlertKind recovered = reason == "start" ? AlertKind.Started : AlertKind.Recovered;
        if (!report.DataComplete)
        {
            if (!_dataAlertSent)
            {
                _dataAlertSent = true;
                Alert(recovered, "recovery waits for data: " + string.Join("; ", report.DataProblems), now);
            }

            return;
        }

        _dataAlertSent = false;
        Alert(recovered, report.Text, now);
        if (report.Strangers.Count > 0)
        {
            Alert(AlertKind.Stranger, "kept with their stop and target: " + string.Join(", ", report.Strangers), now);
        }
    }

    // OPS-008 (1): a completed run is always written; a failed run only when the repeat limit lets it through.
    private void JournalRecovery(RecoveryReport report, DateTime now, string reason)
    {
        int? failedRuns = report.DataComplete ? null : _limits.FailedRecovery(now);
        if (report.DataComplete)
        {
            _limits.ResetRecovery();
        }
        else if (!failedRuns.HasValue)
        {
            return;
        }

        _ops.Journal.Recovery(new RecoveryJournalEntry(
            now,
            _symbol,
            reason,
            report.PositionsAdopted.Count,
            report.OrdersKept.Count,
            report.OrdersCancelled.Count,
            report.Strangers.Count,
            report.ActiveZones.Count,
            report.Downtime,
            report.State)
        {
            FailedRuns = failedRuns,
        });
        if (failedRuns.HasValue)
        {
            Write("report", ("trigger", reason), ("text", report.Text), ("failed-runs", Text(failedRuns.Value)));
        }
        else
        {
            Write("report", ("trigger", reason), ("text", report.Text));
        }

        foreach (AcceptedGapList gaps in report.AcceptedGaps)
        {
            // REC-003 (owner decision A of 2026-11-17): one line per timeframe with gaps kept after the reload.
            // Written with the run's report, so a failed run's gap lines follow the OPS-008 limit too.
            Write("recovery", ("event", "gaps-accepted"), ("timeframe", gaps.TimeFrame.ToString()), ("count", Text(gaps.Count)), ("newest", gaps.NewestText));
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
        if (findings.Replayed.Count > 0)
        {
            SymbolInfo? info = SymbolOrNull();
            NewsStatus? news = NewsStatusFor(now);
            foreach (ReplayedSignal replayed in findings.Replayed)
            {
                _ops.Journal.Signal(SignalJournalEntry.Skipped(replayed.Signal, _symbol, replayed.Bias, null, replayed.Skip, info, news));
            }
        }

        _recoveryPending = false;
        _dataGap = false;
        _weekendClosePending = findings.WeekendClosePending;
        if (findings.Guard != null)
        {
            ApplyGuard(findings.Guard, now, true);
        }

        ApplyStoredBudgetBlock(now);

        UpdateCalendar(now);
        ApplyState("recovery complete", now);
        return _machine.State;
    }

    private void ApplyGuard(GuardStatus guard, DateTime now, bool fromRecovery)
    {
        if (guard.EmergencyFires)
        {
            string detail = "emergency guard: " + guard.Text;
            if (!HaltRecordedOnly(HaltCause.EmergencyGuard, detail, now))
            {
                if (!fromRecovery)
                {
                    JournalActions("emergency", _safety.CloseAllBotTrades("emergency guard"));
                }

                EnterHalt(HaltCause.EmergencyGuard, detail, now);
            }
        }
        else if (guard.KillSwitchFires)
        {
            EnterHalt(HaltCause.KillSwitch, "kill switch: " + guard.Text, now);
        }

        if (guard.BudgetExhausted && !RecordedOnly(WouldBlock, DailyBudgetCause, "daily risk budget exhausted: " + guard.Text, now))
        {
            if (!fromRecovery)
            {
                JournalActions("daily-budget", _trades.CancelEntryOrders("daily budget exhausted"));
            }

            DateTime day = CestClock.StartOfCestDayUtc(now);
            if (_budgetBlockedDayUtc != day)
            {
                _budgetBlockedDayUtc = day;
                _store.Set(BudgetExhaustedKey, day.ToString("o", CultureInfo.InvariantCulture));
                Alert(AlertKind.DailyLimit, "daily risk budget exhausted, no entries until the next CE(S)T day: " + guard.Text, now);
            }
        }

        if (guard.EntriesAllowed)
        {
            _riskDataAlertSent = false;

            // OPS-008 (3), owner answer of 2026-11-17: a new outage is written again, whatever its text.
            _limits.RiskAvailable();
            return;
        }

        if (_limits.RiskUnavailable(guard.Text, now))
        {
            // OPS-008 (3): written when the text changes or 60 minutes after the last written line.
            Write("risk", ("event", "unavailable"), ("guards", guard.Text));
        }

        if (!_riskDataAlertSent)
        {
            _riskDataAlertSent = true;
            Alert(AlertKind.RiskDataUnavailable, "no new entries: " + guard.Text, now);
        }
    }

    private void CheckEmergencyGuard(DateTime now)
    {
        GuardStatus guard = _guard.Evaluate(now);
        if (guard.EmergencyFires)
        {
            string detail = "emergency guard: " + guard.Text;
            if (!HaltRecordedOnly(HaltCause.EmergencyGuard, detail, now))
            {
                JournalActions("emergency", _safety.CloseAllBotTrades("emergency guard"));
                EnterHalt(HaltCause.EmergencyGuard, detail, now);
            }
        }
    }

    // RSK-006 (owner decision of 2026-11-16): a day stored as exhausted blocks until the next CE(S)T day,
    // also when the recomputed budget would allow entries after a restart. It never allows anything.
    private void ApplyStoredBudgetBlock(DateTime now)
    {
        string? text = _store.Get(BudgetExhaustedKey);
        if (text == null)
        {
            return;
        }

        DateTime today = CestClock.StartOfCestDayUtc(now);
        bool readable = TryParseUtc(text, out DateTime stored);
        if (!readable || stored != today)
        {
            Write("risk", ("event", "stored-budget-day-ignored"), ("value", OneLine(text)), ("reason", readable ? "other day" : "unreadable"));
            return;
        }

        if (_budgetBlockedDayUtc == today || RecordedOnly(WouldBlock, DailyBudgetCause, "daily risk budget was exhausted earlier today", now))
        {
            return;
        }

        _budgetBlockedDayUtc = today;
        JournalActions("daily-budget", _trades.CancelEntryOrders("daily budget exhausted earlier today"));
        Alert(AlertKind.DailyLimit, "daily risk budget was exhausted earlier today; no entries until the next CE(S)T day", now);
    }

    private void EnterHalt(HaltCause cause, string detail, DateTime now)
    {
        if (HaltRecordedOnly(cause, detail, now))
        {
            return;
        }

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

    // REC-014 (owner decision C of 2026-11-17): the one decision point for halts and the daily budget block.
    // Live, and in tests without the flag, it returns false and the caller applies the halt or block. With
    // BacktestHaltsLogOnly (backtests only, refused live by BotParameters.Validate) it writes
    // "risk event=<would-halt|would-block> cause=<cause> detail=<text>" the first time the cause fires in a
    // CE(S)T day and returns true: the caller then does nothing else.
    private bool RecordedOnly(string eventName, string cause, string detail, DateTime now)
    {
        if (!_haltsLogOnly)
        {
            return false;
        }

        string key = eventName + " " + cause;
        DateTime day = CestClock.StartOfCestDayUtc(now);
        if (!_recordedDays.TryGetValue(key, out DateTime last) || last != day)
        {
            _recordedDays[key] = day;
            Write("risk", ("event", eventName), ("cause", cause), ("detail", detail));
        }

        return true;
    }

    private bool HaltRecordedOnly(HaltCause cause, string detail, DateTime now)
    {
        return RecordedOnly(WouldHalt, cause.ToString(), detail, now);
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
            Write("risk", ("event", "daily-budget-reset"), ("day", Minute(CestClock.StartOfCestDayUtc(now))));
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
        if (previous == null || !BarSeriesCheck.IsGap(timeFrame, previous, bar, _fridayCloseUtc, _marketHolidays))
        {
            return;
        }

        string detail = timeFrame + " bars missing between " + Minute(previous.OpenTimeUtc) + " and " + Minute(bar.OpenTimeUtc);
        Write("bar", ("event", "gap"), ("detail", detail));
        if (!_dataGap)
        {
            _dataGap = true;
            Alert(AlertKind.DataGap, detail + "; entries blocked until recovery reloads the history", now);
        }
    }

    // ------------------------------------------------------------------ duties

    private void WeekendDuty()
    {
        // Set by recovery after a start or reconnect in the weekend window (owner decision of 2026-11-16);
        // the Friday close of a bot that keeps running is EXE-013.
        if (!_weekendClosePending)
        {
            return;
        }

        if (!_safety.HasSymbolExposure())
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

    // EXE-016: against a closed market no close or cancel request is sent; the weekend close does it at the reopen.
    private void FridayCloseDuty(DateTime now)
    {
        if (!_trades.FridayCloseDue(now) || _safety.MarketOpen())
        {
            JournalActions("friday-close", _trades.FridayClose(now));
            return;
        }

        if (!_safety.HasSymbolExposure())
        {
            return;
        }

        _weekendClosePending = true;
        if (_fridayCloseDeferredDay != now.Date)
        {
            // One line per Friday, not per minute.
            _fridayCloseDeferredDay = now.Date;
            Write("friday-close", ("event", "deferred"), ("reason", "market-closed"));
        }
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
            Write("order", ("event", "resolved"), ("label", result.Label), ("outcome", result.Outcome.ToString()), ("detail", result.Detail));
            if (result.Outcome == PlaceOutcome.RequestLimitReached)
            {
                EnterHalt(HaltCause.RequestLimit, "request limit: " + result.Detail, now);
            }
        }
    }

    private void PlaceSignals(IReadOnlyList<Signal> signals, IReadOnlyList<Zone> zonesBefore, GuardStatus guard, DateTime now)
    {
        foreach (Signal signal in SignalOrder.ForEvaluation(signals))
        {
            Zone? zone = zonesBefore.FirstOrDefault(z => string.Equals(z.Id, signal.ZoneId, StringComparison.Ordinal));
            SkipRecord? skip = PlaceSignal(signal, guard, now, out PlannedOrder? order);
            SymbolInfo? info = SymbolOrNull();
            NewsStatus? news = NewsStatusFor(now);
            Bias bias = _engine!.Bias;
            SignalJournalEntry entry = skip != null
                ? SignalJournalEntry.Skipped(signal, _symbol, bias, zone, skip, info, news)
                : SignalJournalEntry.Taken(order!, bias, zone, info, news);
            _ops.Journal.Signal(entry);
        }
    }

    // One signal (OPS-001): returns its skip record, or null when it became an order.
    private SkipRecord? PlaceSignal(Signal signal, GuardStatus guard, DateTime now, out PlannedOrder? order)
    {
        order = null;
        if (!guard.EntriesAllowed)
        {
            string why = "risk data unavailable: " + (guard.DataProblem ?? "a guard could not be computed");
            var skip = new SkipRecord(_symbol, signal.ZoneId, SkipReason.BrokerDataInvalid, 0.0, 0.0, why, now);
            _events.Skipped(skip);
            return skip;
        }

        var context = new PreTradeContext(_symbol, _machine.State, _engine!.Zones, _orders.HasUnresolved(_symbol));
        PreTradeResult result = _filters.EvaluateWithWaiver(signal, context, out string? waivedBudget);
        if (!result.IsAllowed)
        {
            return result.Skip;
        }

        if (waivedBudget != null)
        {
            // REC-014: only possible with BacktestHaltsLogOnly; the trade is placed and the block recorded.
            RecordedOnly(WouldBlock, DailyBudgetCause, "daily risk budget: " + waivedBudget, now);
        }

        CircuitBreakerDecision check = _requests.CheckBeforeRequest(now);
        if (check.Halt && !HaltRecordedOnly(HaltCause.RequestLimit, "request limit: " + check.Detail, now))
        {
            // RSK-010: the request that would exceed the limit is never sent; the instance halts.
            var skip = new SkipRecord(_symbol, signal.ZoneId, SkipReason.RequestLimitReached, check.LastHourCount, check.HourLimit, check.Detail, now);
            _events.Skipped(skip);
            EnterHalt(HaltCause.RequestLimit, "request limit: " + check.Detail, now);
            return skip;
        }

        order = result.Order!;
        PlaceResult placed = _orders.TryPlace(order);
        Write("order", ("event", "placement"), ("label", placed.Label), ("outcome", placed.Outcome.ToString()), ("detail", placed.Detail));
        if (placed.Outcome == PlaceOutcome.RequestLimitReached)
        {
            EnterHalt(HaltCause.RequestLimit, "request limit: " + placed.Detail, now);
        }

        return null;
    }

    private SymbolInfo? SymbolOrNull()
    {
        try
        {
            return _broker.GetSymbol(_symbol);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            Write("ops", ("event", "symbol-unavailable"), ("reason", exception.GetType().Name + ": " + OneLine(exception.Message)));
            return null;
        }
    }

    private NewsStatus? NewsStatusFor(DateTime now)
    {
        return _newsEnabled ? _news.StatusAt(now) : null;
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
            Write("news", ("source", report.Source), ("outcome", report.Outcome.ToString()), ("detail", report.Detail));
            if (report.Outcome == NewsFeedOutcome.DownloadAccepted)
            {
                _newsSourcesDown.Remove(report.Source);
            }
            else if (report.Outcome == NewsFeedOutcome.DownloadRejected && _newsSourcesDown.Add(report.Source))
            {
                // Once per outage of the source (owner decision on pull request #20).
                Alert(AlertKind.NewsFeedDown, "news source " + report.Source + " download failed: " + report.Detail, now);
            }
        }

        // Calendar unavailable and malformed downloads are raised by WP09 (OpsEvents.NewsUpdated).
        _ops.Events.NewsUpdated(update, now);
        if (update.Recovered)
        {
            Write("news", ("event", "calendar-available"));
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

    // OPS-002: the largest adverse and favourable moves in R over the closed M15 bars of the trade's life
    // and its exit price. Journal only: reads bars, changes nothing. Null (NA) when the plan, its comment
    // or the bars are missing.
    private (double AdverseR, double FavourableR)? Excursion(ClosedTradeInfo trade, OpenedTradePlan? plan)
    {
        CommentPlan? levels = OrderComment.TryParse(plan?.Comment);
        DateTime now = _lastKnownServerTimeUtc ?? trade.CloseTimeUtc;
        double bars = Math.Ceiling((now - trade.EntryTimeUtc).TotalMinutes / 15.0) + 2.0;
        if (plan == null || levels == null || _history == null || !(bars >= 1.0))
        {
            return null;
        }

        try
        {
            IReadOnlyList<Bar> m15 = _history.ClosedBars(TimeFrame.M15, (int)Math.Min(bars, MaxExcursionBars));
            return TradeExcursion.ForTrade(plan.Side, plan.EntryPrice, levels.Stop, trade.EntryTimeUtc, trade.CloseTimeUtc, trade.ClosingPrice, m15);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            // Journal only: the trade line is still written, with mae and mfe NA.
            Write("trade", ("event", "excursion-unavailable"), ("reason", exception.GetType().Name + ": " + OneLine(exception.Message)));
            return null;
        }
    }

    // SIM-001: one machine-readable line per closed bot trade for the backtest trade log. Journal only.
    private void WriteTradeLogLine(ClosedTradeInfo trade, OpenedTradePlan? plan)
    {
        _tradeLogLines++;
        TradeLogRow row = TradeLogLine.Build(trade, plan?.Side, plan?.EntryPrice, plan?.Comment);
        _ops.Journal.Write(trade.CloseTimeUtc, TradeLogLine.Category, TradeLogLine.Fields(_tradeLogLines, row));
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
        // Never throws: this is the last line of defence of every handler (REC-011). WP09's journal never throws.
        DateTime now = SafeNow(out bool timeKnown);
        CircuitBreakerDecision decision = _errors.Record(now);
        JournalError(handler, exception, now, timeKnown, decision);
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
            _ops.Journal.Error(now, "halt", haltException);
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

    // OPS-008 (2): the error is counted for RSK-010 above in every case; a repeat of the group's last written
    // error within 60 minutes is neither journaled nor alerted. When the server time could not be read the
    // interval cannot be measured, so the error is always written.
    private void JournalError(string handler, Exception exception, DateTime now, bool timeKnown, CircuitBreakerDecision decision)
    {
        int? leftOut = timeKnown ? _limits.Error(handler, exception, now) : 0;
        if (!leftOut.HasValue)
        {
            return;
        }

        string text = "handler=" + handler + " type=" + exception.GetType().Name + " message=" + OneLine(exception.Message)
            + " errors-last-hour=" + decision.LastHourCount.ToString(CultureInfo.InvariantCulture);
        if (leftOut.Value > 0)
        {
            _ops.Journal.Error(now, handler, exception, leftOut.Value);
            text += " left-out=" + Text(leftOut.Value);
        }
        else
        {
            _ops.Journal.Error(now, handler, exception);
        }

        TryAlert(AlertKind.Error, text, now);
    }

    private DateTime SafeNow(out bool known)
    {
        known = true;
        try
        {
            return Now();
        }
        catch (Exception exception)
        {
            // Used only while recording another error: the time falls back to the last one known, and the
            // failure is journaled with it.
            DateTime fallback = _lastKnownServerTimeUtc ?? FallbackTimeUtc;
            _ops.Journal.Error(fallback, "time", exception);
            known = false;
            return fallback;
        }
    }

    private void Record(StateTransition transition)
    {
        _ops.Journal.State(transition.AtUtc, transition.From, transition.To, transition.Reason);
        Alert(AlertKind.StateChanged, transition.From + " -> " + transition.To + ": " + transition.Reason, transition.AtUtc);
    }

    private void JournalActions(string what, IReadOnlyList<TradeAction> actions)
    {
        foreach (TradeAction action in actions)
        {
            Write("action", ("duty", what), ("id", action.Id.ToString(CultureInfo.InvariantCulture)), ("outcome", action.Outcome.ToString()), ("detail", action.Detail));
        }
    }

    private void Alert(AlertKind kind, string message, DateTime now)
    {
        if (AlertKinds.Telegram(kind).HasValue)
        {
            _ops.Alerts.Raise(kind, message, now);
        }
        else
        {
            // Journal only by the Telegram table (spec 11); the notice is journaled.
            Write("notice", ("kind", kind.ToString()), ("message", message));
        }
    }

    private void TryAlert(AlertKind kind, string message, DateTime now)
    {
        try
        {
            Alert(kind, message, now);
        }
        catch (Exception exception)
        {
            _ops.Journal.Error(now, "alert", exception);
        }
    }

    private void Write(string category, params (string Key, string? Value)[] fields)
    {
        _ops.Journal.Write(_lastKnownServerTimeUtc ?? FallbackTimeUtc, category, fields);
    }

    private static AlertKind AlertFor(HaltCause cause)
    {
        switch (cause)
        {
            case HaltCause.KillSwitch:
                return AlertKind.KillSwitch;
            case HaltCause.EmergencyGuard:
                return AlertKind.EmergencyGuard;
            case HaltCause.RequestLimit:
                return AlertKind.CircuitBreaker;
            default:
                return AlertKind.ErrorStorm;
        }
    }

    private static string OneLine(string? text)
    {
        return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>
    /// The execution hook: forwards skip records and alerts to WP09's <see cref="OpsEvents"/>
    /// (journal and Telegram, OPS-001, OPS-004).
    /// </summary>
    private sealed class CoreEvents : IExecutionEvents
    {
        private readonly BotCore _core;

        internal CoreEvents(BotCore core)
        {
            _core = core;
        }

        public void Skipped(SkipRecord skip)
        {
            _core._ops.Events.Skipped(skip);
        }

        public void Alert(ExecutionAlert alert)
        {
            _core._ops.Events.Alert(alert);
        }
    }

    /// <summary>
    /// Trade events found by the trade journal (REC-008), each exactly once: an opening is journaled and
    /// alerted as TradeOpened; a closing goes to WP09's trade journal (OPS-002) and is alerted as TradeClosed.
    /// </summary>
    private sealed class CoreTradeEvents : ITradeEvents
    {
        private readonly BotCore _core;

        internal CoreTradeEvents(BotCore core)
        {
            _core = core;
        }

        public void Opened(PositionInfo position, DateTime atUtc)
        {
            _core.Write(
                "position",
                ("event", "opened"),
                ("symbol", position.Symbol),
                ("position", Text(position.Id)),
                ("label", position.Label),
                ("side", position.Side.ToString()),
                ("volume", Text(position.VolumeUnits)),
                ("entry", Text(position.EntryPrice)),
                ("stop", position.StopLoss.HasValue ? Text(position.StopLoss.Value) : null),
                ("target", position.TakeProfit.HasValue ? Text(position.TakeProfit.Value) : null),
                ("opened", position.OpenTimeUtc.ToString("o", CultureInfo.InvariantCulture)));
            string text = "position " + Text(position.Id) + " " + position.Side + " " + Text(position.VolumeUnits) + " at " + Text(position.EntryPrice);
            _core.Alert(AlertKind.TradeOpened, text, atUtc);
        }

        public void Closed(ClosedTradeInfo trade, OpenedTradePlan? plan, DateTime atUtc)
        {
            double? pipSize = _core.SymbolOrNull()?.PipSize;
            (double AdverseR, double FavourableR)? moves = _core.Excursion(trade, plan);
            _core._ops.Journal.Trade(new TradeJournalEntry(trade, plan?.Comment, plan?.Side, plan?.EntryPrice, pipSize, trade.ExitReason, moves?.AdverseR, moves?.FavourableR));
            if (_core._writeTradeLog)
            {
                _core.WriteTradeLogLine(trade, plan);
            }

            string text = "position " + Text(trade.PositionId) + " closed, net " + trade.NetProfit.ToString("0.00", CultureInfo.InvariantCulture);
            _core.Alert(AlertKind.TradeClosed, text, atUtc);
        }
    }
}
