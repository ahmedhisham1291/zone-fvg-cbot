using System;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Ops;

/// <summary>What a Telegram alert is about: the list of spec section 11 plus the inactivity warning and the daily summary (OPS-003, OPS-004, OPS-006).</summary>
public enum OpsAlertKind
{
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

    /// <summary>The broker rejected an order (spec 11, EXE-009).</summary>
    OrderRejected,

    /// <summary>An order did not match after placement, was not found, or a cancel or close request failed (EXE-010, EXE-013).</summary>
    OrderProblem,

    /// <summary>A trade was refused by a sanity cap (RSK-002).</summary>
    SanityCap,

    /// <summary>A position has no stop loss, or was closed because its stop could not be set (spec 11, EXE-011).</summary>
    StopLossMissing,

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
}

/// <summary>Mapping of execution alerts to Telegram alert kinds (OPS-004, spec 11).</summary>
public static class OpsAlertKinds
{
    /// <summary>
    /// The Telegram alert kind of an execution alert (OPS-004): rejection to OrderRejected; order
    /// mismatch, order not found, failed cancel and failed close to OrderProblem; sanity cap to
    /// SanityCap; missing stop and position closed without a stop to StopLossMissing; restored
    /// stop to StopLossRepaired; request limit to CircuitBreaker.
    /// </summary>
    /// <param name="kind">The execution alert kind.</param>
    /// <returns>The Telegram alert kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public static OpsAlertKind From(AlertKind kind)
    {
        switch (kind)
        {
            case AlertKind.SanityCap:
                return OpsAlertKind.SanityCap;
            case AlertKind.OrderRejected:
                return OpsAlertKind.OrderRejected;
            case AlertKind.OrderMismatch:
            case AlertKind.OrderNotFound:
            case AlertKind.CancelFailed:
            case AlertKind.CloseFailed:
                return OpsAlertKind.OrderProblem;
            case AlertKind.StopLossMissing:
            case AlertKind.PositionClosedWithoutStop:
                return OpsAlertKind.StopLossMissing;
            case AlertKind.StopLossRestored:
                return OpsAlertKind.StopLossRepaired;
            case AlertKind.RequestLimitReached:
                return OpsAlertKind.CircuitBreaker;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Not a defined execution alert kind.");
        }
    }
}
