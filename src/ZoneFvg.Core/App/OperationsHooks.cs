using System;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.App;

/// <summary>What a bot alert is about (spec 11 alert list).</summary>
public enum BotAlertKind
{
    /// <summary>An alert of the execution layer; see <see cref="BotAlert.ExecutionKind"/>.</summary>
    Execution,

    /// <summary>The recovery report after a start or reconnect (REC-010).</summary>
    RecoveryReport,

    /// <summary>Bar history could not be completed; the bot stays in Recovering (REC-003).</summary>
    DataIncomplete,

    /// <summary>A bar series has a gap while running; entries are blocked until recovery restores the data (REC-003).</summary>
    DataGap,

    /// <summary>A labelled position whose zone or parameters are unknown is kept as a stranger (REC-007).</summary>
    Stranger,

    /// <summary>A bot position opened (spec 11).</summary>
    TradeOpened,

    /// <summary>A bot position closed (spec 11, OPS-002).</summary>
    TradeClosed,

    /// <summary>The bot state changed (REC-001).</summary>
    StateChanged,

    /// <summary>The drawdown kill switch halted the bot (RSK-008).</summary>
    KillSwitch,

    /// <summary>The emergency guard closed everything and halted the bot (RSK-009).</summary>
    EmergencyGuard,

    /// <summary>The daily risk budget is exhausted; entries blocked until the next CE(S)T day (RSK-006).</summary>
    DailyLimit,

    /// <summary>The request limit halted the bot (RSK-010).</summary>
    CircuitBreaker,

    /// <summary>Too many unhandled errors in an hour halted the bot (RSK-010, REC-011).</summary>
    ErrorStorm,

    /// <summary>An unhandled error was caught at the event boundary (REC-011).</summary>
    Error,

    /// <summary>The news calendar is unavailable; entries blocked (NWS fail-safe, spec 6.3).</summary>
    CalendarUnavailable,

    /// <summary>A download from a news source failed; raised once per outage of the source (owner decision on pull request #20).</summary>
    NewsFeedDown,

    /// <summary>A news download contained a malformed event (WP05 decision).</summary>
    NewsFeedMalformed,

    /// <summary>Risk data could not be read; no new entries (architecture section 10, "Bad broker data").</summary>
    RiskDataUnavailable,
}

/// <summary>An alert raised by the bot core (spec 11).</summary>
/// <param name="Kind">What the alert is about.</param>
/// <param name="ExecutionKind">For <see cref="BotAlertKind.Execution"/>, the execution alert kind; otherwise null.</param>
/// <param name="Symbol">Symbol of the instance.</param>
/// <param name="Message">Short invariant text.</param>
/// <param name="AtUtc">Server time, kind UTC.</param>
public sealed record BotAlert(BotAlertKind Kind, AlertKind? ExecutionKind, string Symbol, string Message, DateTime AtUtc);

/// <summary>
/// An observer of <see cref="BotCore"/>'s alerts and timer duties. The work itself is done by WP09's
/// <c>Operations</c>, which <see cref="BotCore"/> builds and calls; this observer sees the same events
/// (every alert as a <see cref="BotAlert"/>, the heartbeat and the inactivity check in every state)
/// and lets tests check them. The public constructor uses an observer that does nothing.
/// </summary>
public interface IOperationsHooks
{
    /// <summary>Sees an alert (spec 11) after it was handed to WP09's alert service or journal.</summary>
    /// <param name="alert">The alert.</param>
    void Alert(BotAlert alert);

    /// <summary>Sees the one-minute timer, called in every state before WP09's heartbeat and alert pump (OPS-005).</summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="state">Current bot state.</param>
    void Heartbeat(DateTime serverTimeUtc, BotState state);

    /// <summary>Sees the inactivity check, called once a minute in every state (OPS-006).</summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    void Inactivity(DateTime serverTimeUtc);
}

/// <summary>The observer of the public constructor: it does nothing; WP09's <c>Operations</c> does the work.</summary>
internal sealed class NoOperationsHooks : IOperationsHooks
{
    public void Alert(BotAlert alert)
    {
    }

    public void Heartbeat(DateTime serverTimeUtc, BotState state)
    {
    }

    public void Inactivity(DateTime serverTimeUtc)
    {
    }
}
