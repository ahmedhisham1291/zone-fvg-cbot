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
/// Operations hooks called by <see cref="BotCore"/> (prompt WP07: "Hooks are interfaces here; WP09
/// supplies the implementations"): alerts, the heartbeat and the inactivity check. Every alert is
/// also journaled by the core. The heartbeat is called on every timer tick in every state.
/// </summary>
public interface IOperationsHooks
{
    /// <summary>Sends an alert (spec 11). Best effort; must not block (OPS-004).</summary>
    /// <param name="alert">The alert.</param>
    void Alert(BotAlert alert);

    /// <summary>Heartbeat, alert sending and daily summary pump, called once a minute in every state (OPS-003 to OPS-005).</summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="state">Current bot state.</param>
    void Heartbeat(DateTime serverTimeUtc, BotState state);

    /// <summary>Inactivity check, called once a minute in every state (OPS-006).</summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    void Inactivity(DateTime serverTimeUtc);
}

/// <summary>Hooks that do nothing, used until WP09's implementations are connected. Alerts are still journaled by the core.</summary>
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
