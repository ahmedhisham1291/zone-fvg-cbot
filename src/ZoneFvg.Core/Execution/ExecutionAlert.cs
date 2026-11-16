namespace ZoneFvg.Core.Execution;

/// <summary>
/// What an alert is about: the single list of alert kinds of the bot (spec 11, OPS-004, RSK-002,
/// EXE-009 to EXE-011, EXE-013, REC-003, REC-011). Some kinds are sent by Telegram under their own
/// name, some under a broader Telegram kind, and some are journal only; <c>Ops.AlertKinds.Telegram</c>
/// is the table.
/// </summary>
public enum AlertKind
{
    /// <summary>A trade was refused by a sanity cap: risk above 0.55%, volume above MaxVolumeUnits, or stop or target on the wrong side (RSK-002).</summary>
    SanityCap,

    /// <summary>The broker clearly rejected an entry order (EXE-009, spec 11).</summary>
    OrderRejected,

    /// <summary>The order read back after placement differs from the request in stop, target or expiry and was cancelled (EXE-010).</summary>
    OrderMismatch,

    /// <summary>The broker reported success but the order cannot be found (EXE-010).</summary>
    OrderNotFound,

    /// <summary>A cancel request did not succeed (EXE-010, EXE-013).</summary>
    CancelFailed,

    /// <summary>A bot position has no stop loss (EXE-011).</summary>
    StopLossMissing,

    /// <summary>The stop loss of a position was set again from its comment (EXE-011).</summary>
    StopLossRestored,

    /// <summary>A position whose stop loss could not be set was closed (EXE-011).</summary>
    PositionClosedWithoutStop,

    /// <summary>A close request did not succeed (EXE-011, EXE-013).</summary>
    CloseFailed,

    /// <summary>An entry order was not sent because the request limit would be exceeded (RSK-010).</summary>
    RequestLimitReached,

    /// <summary>The bot started and finished recovery (spec 11: bot started).</summary>
    Started,

    /// <summary>The bot recovered after a reconnect (spec 11: bot recovered).</summary>
    Recovered,

    /// <summary>The bot state changed (spec 11: state change).</summary>
    StateChanged,

    /// <summary>A trade was opened (spec 11).</summary>
    TradeOpened,

    /// <summary>A trade was closed (spec 11).</summary>
    TradeClosed,

    /// <summary>An order did not match after placement, was not found, or a cancel or close request failed (EXE-010, EXE-013).</summary>
    OrderProblem,

    /// <summary>The stop loss of a position was set again (spec 11: stop loss repaired, EXE-011).</summary>
    StopLossRepaired,

    /// <summary>The drawdown kill switch fired (spec 11, RSK-008).</summary>
    KillSwitch,

    /// <summary>The FTMO emergency guard closed everything (RSK-009).</summary>
    EmergencyGuard,

    /// <summary>The daily risk budget is used up (spec 11: daily limit, RSK-006).</summary>
    DailyLimit,

    /// <summary>A news feed is down or its download was rejected (spec 11, NWS-005).</summary>
    NewsFeedDown,

    /// <summary>The news calendar is unavailable (spec 11, NWS-008).</summary>
    CalendarUnavailable,

    /// <summary>A circuit breaker was reached (spec 11, RSK-010).</summary>
    CircuitBreaker,

    /// <summary>The error storm limit was reached (spec 11, RSK-010).</summary>
    ErrorStorm,

    /// <summary>No trade was opened for 21 days or more (OPS-006).</summary>
    Inactivity,

    /// <summary>The daily summary (OPS-003).</summary>
    DailySummary,

    /// <summary>A bar series has a gap; entries are blocked until recovery reloads it (REC-003). Journal only.</summary>
    DataGap,

    /// <summary>Recovery kept a bot-labelled position whose zone is unknown (REC-007). Journal only.</summary>
    Stranger,

    /// <summary>A handler or the platform reported an exception (REC-011). Journal only.</summary>
    Error,

    /// <summary>A risk guard could not be computed from broker data, so no entry is placed (RSK-009, architecture section 10 "Bad broker data"). Journal only.</summary>
    RiskDataUnavailable,
}

/// <summary>An alert raised by the execution layer (spec 11). A data record.</summary>
/// <param name="Kind">What the alert is about.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Message">Short invariant text with the ids and values.</param>
public sealed record ExecutionAlert(AlertKind Kind, string Symbol, string Message);
