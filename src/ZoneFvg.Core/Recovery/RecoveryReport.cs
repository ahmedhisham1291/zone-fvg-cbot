using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Recovery;

/// <summary>A signal produced during the replay, with the bias at that moment and its skip record (REC-004, OPS-001).</summary>
/// <param name="Signal">The signal, marked FromReplay.</param>
/// <param name="Bias">D1 bias when the signal was confirmed.</param>
/// <param name="Skip">The skip record with reason SignalFromReplay.</param>
public sealed record ReplayedSignal(Signal Signal, Bias Bias, SkipRecord Skip);

/// <summary>
/// What recovery found, handed to the bot before it enters its next state (REC-001, REC-009,
/// spec 8.3 step 10).
/// </summary>
public sealed class RecoveryFindings
{
    private readonly Bar?[] _lastBars;

    internal RecoveryFindings(DateTime atUtc, bool dataComplete, IReadOnlyList<string> dataProblems, SignalEngine? engine, Bar?[] lastBars, IReadOnlyList<Bar> recentM15, GuardStatus? guard, bool weekendClosePending, IReadOnlyList<string> strangers, IReadOnlyList<ReplayedSignal> replayed)
    {
        Replayed = replayed;
        RecentM15 = recentM15;
        AtUtc = atUtc;
        DataComplete = dataComplete;
        DataProblems = dataProblems;
        Engine = engine;
        _lastBars = lastBars;
        Guard = guard;
        WeekendClosePending = weekendClosePending;
        Strangers = strangers;
    }

    /// <summary>Server time of the recovery, kind UTC.</summary>
    public DateTime AtUtc { get; }

    /// <summary>True when every bar series was complete and the signal layer was rebuilt (REC-003, REC-004).</summary>
    public bool DataComplete { get; }

    /// <summary>The problems of the bar series; empty when complete (REC-003).</summary>
    public IReadOnlyList<string> DataProblems { get; }

    /// <summary>The rebuilt signal engine, or null when the data was incomplete (REC-004).</summary>
    public SignalEngine? Engine { get; }

    /// <summary>The signals produced during the replay, in order; none was executed (REC-004).</summary>
    public IReadOnlyList<ReplayedSignal> Replayed { get; }

    /// <summary>The newest replayed M15 bars, oldest first, for resolving unclear placements (EXE-009).</summary>
    public IReadOnlyList<Bar> RecentM15 { get; }

    /// <summary>The account guards, or null when the data was incomplete (RSK-006, RSK-008, RSK-009).</summary>
    public GuardStatus? Guard { get; }

    /// <summary>True when bot trades of the symbol must be closed at the market's reopen (REC-009, owner decision of 2026-11-16).</summary>
    public bool WeekendClosePending { get; }

    /// <summary>The strangers found, each as "id (reason)" (REC-007).</summary>
    public IReadOnlyList<string> Strangers { get; }

    /// <summary>The newest replayed bar of the timeframe, or null (REC-004).</summary>
    /// <param name="timeFrame">D1, H4 or M15.</param>
    /// <returns>The bar, or null.</returns>
    public Bar? LastBar(TimeFrame timeFrame)
    {
        return _lastBars[BarIntakeOrder.Rank(timeFrame)];
    }
}

/// <summary>
/// The report of one recovery run (REC-010, spec 8.3 step 10): downtime, positions adopted,
/// orders kept and cancelled, strangers, active zones and the state entered, plus the data,
/// protection, risk and catch-up results of the other steps. It is journaled and sent as an
/// alert by the bot.
/// </summary>
public sealed class RecoveryReport
{
    /// <summary>Symbol of the instance.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Server time of the recovery, kind UTC.</summary>
    public DateTime AtUtc { get; init; }

    /// <summary>Time since the bot was last seen running, or null when unknown (REC-010).</summary>
    public TimeSpan? Downtime { get; init; }

    /// <summary>True when the bar series were complete (REC-003).</summary>
    public bool DataComplete { get; init; }

    /// <summary>Problems of the bar series (REC-003).</summary>
    public IReadOnlyList<string> DataProblems { get; init; } = Array.Empty<string>();

    /// <summary>Ids of the non-terminal zones after the replay, ordered by id (REC-004, REC-010).</summary>
    public IReadOnlyList<string> ActiveZones { get; init; } = Array.Empty<string>();

    /// <summary>Signals produced during the replay; none was executed (REC-004).</summary>
    public int ReplaySignals { get; init; }

    /// <summary>Bot positions adopted, by id (REC-005).</summary>
    public IReadOnlyList<long> PositionsAdopted { get; init; } = Array.Empty<long>();

    /// <summary>Strangers, each as "id (reason)" (REC-007).</summary>
    public IReadOnlyList<string> Strangers { get; init; } = Array.Empty<string>();

    /// <summary>Bot positions without a take profit, by id (REC-005: verified and reported).</summary>
    public IReadOnlyList<long> WithoutTakeProfit { get; init; } = Array.Empty<long>();

    /// <summary>Protection actions of EXE-011, each as "id outcome" (REC-005).</summary>
    public IReadOnlyList<string> Protection { get; init; } = Array.Empty<string>();

    /// <summary>Bot pending orders kept, by id (REC-006).</summary>
    public IReadOnlyList<long> OrdersKept { get; init; } = Array.Empty<long>();

    /// <summary>Bot pending orders cancelled, each as "id (reason)"; a failed cancel is marked (REC-006).</summary>
    public IReadOnlyList<string> OrdersCancelled { get; init; } = Array.Empty<string>();

    /// <summary>Trade events known to be journaled (REC-008).</summary>
    public int JournaledTrades { get; init; }

    /// <summary>Why the journal backfill could not read the broker history, or null (REC-008).</summary>
    public string? JournalProblem { get; init; }

    /// <summary>Summary of the account guards, or "not evaluated" (RSK-006, RSK-008, RSK-009).</summary>
    public string Risk { get; init; } = "not evaluated";

    /// <summary>Catch-up actions, each as "id outcome (reason)" (REC-009).</summary>
    public IReadOnlyList<string> CatchUp { get; init; } = Array.Empty<string>();

    /// <summary>True when the weekend close waits for the market to reopen (REC-009).</summary>
    public bool WeekendClosePending { get; init; }

    /// <summary>The state entered after recovery (REC-010).</summary>
    public BotState State { get; init; }

    /// <summary>The value of NewsEnabled, shown in the start-up summary (owner decision of 2026-11-16).</summary>
    public bool NewsEnabled { get; init; }

    /// <summary>The 8-character parameter hash of this instance (EXE-008).</summary>
    public string ParameterHash { get; init; } = string.Empty;

    /// <summary>The report as one invariant line; the same inputs give the same text (REC-002, REC-010).</summary>
    public string Text =>
        "symbol=" + Symbol
        + " at=" + AtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        + " downtime=" + (Downtime.HasValue ? Downtime.Value.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) + "min" : "unknown")
        + " data=" + (DataComplete ? "complete" : "incomplete") + List(DataProblems)
        + " zones=" + List(ActiveZones)
        + " replay-signals=" + ReplaySignals.ToString(CultureInfo.InvariantCulture)
        + " adopted=" + List(PositionsAdopted)
        + " strangers=" + List(Strangers)
        + " without-target=" + List(WithoutTakeProfit)
        + " protection=" + List(Protection)
        + " orders-kept=" + List(OrdersKept)
        + " orders-cancelled=" + List(OrdersCancelled)
        + " journaled=" + (JournalProblem == null ? JournaledTrades.ToString(CultureInfo.InvariantCulture) : "failed(" + JournalProblem + ")")
        + " risk={" + Risk + "}"
        + " catch-up=" + List(CatchUp)
        + " weekend-close-pending=" + (WeekendClosePending ? "yes" : "no")
        + " state=" + State
        + " news=" + (NewsEnabled ? "enabled" : "disabled")
        + " hash=" + ParameterHash
        + " version=" + BuildInfo.Version;

    private static string List(IReadOnlyList<string> items)
    {
        return "[" + string.Join(",", items) + "]";
    }

    private static string List(IReadOnlyList<long> items)
    {
        var texts = new List<string>(items.Count);
        foreach (long item in items)
        {
            texts.Add(item.ToString(CultureInfo.InvariantCulture));
        }

        return List(texts);
    }
}
