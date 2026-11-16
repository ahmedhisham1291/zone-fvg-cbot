using System;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The Telegram table of the alert kinds (OPS-004, spec 11): for each <see cref="AlertKind"/> the
/// kind it is sent as by Telegram, or none when it is journal only.
/// </summary>
public static class AlertKinds
{
    /// <summary>
    /// The Telegram kind of an alert (OPS-004). The kinds of spec 11 are sent under their own name;
    /// the detailed execution kinds under a broader one (for example a failed cancel as
    /// <see cref="AlertKind.OrderProblem"/>); <see cref="AlertKind.DataGap"/>,
    /// <see cref="AlertKind.Stranger"/>, <see cref="AlertKind.Error"/> and
    /// <see cref="AlertKind.RiskDataUnavailable"/> are journal only and give null.
    /// </summary>
    /// <param name="kind">The alert kind.</param>
    /// <returns>The kind to send, or null when the alert is journal only.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined kind.</exception>
    public static AlertKind? Telegram(AlertKind kind)
    {
        switch (kind)
        {
            case AlertKind.OrderMismatch:
            case AlertKind.OrderNotFound:
            case AlertKind.CancelFailed:
            case AlertKind.CloseFailed:
                return AlertKind.OrderProblem;
            case AlertKind.PositionClosedWithoutStop:
                return AlertKind.StopLossMissing;
            case AlertKind.StopLossRestored:
                return AlertKind.StopLossRepaired;
            case AlertKind.RequestLimitReached:
                return AlertKind.CircuitBreaker;
            case AlertKind.SanityCap:
            case AlertKind.OrderRejected:
            case AlertKind.StopLossMissing:
            case AlertKind.Started:
            case AlertKind.Recovered:
            case AlertKind.StateChanged:
            case AlertKind.TradeOpened:
            case AlertKind.TradeClosed:
            case AlertKind.OrderProblem:
            case AlertKind.StopLossRepaired:
            case AlertKind.KillSwitch:
            case AlertKind.EmergencyGuard:
            case AlertKind.DailyLimit:
            case AlertKind.NewsFeedDown:
            case AlertKind.CalendarUnavailable:
            case AlertKind.CircuitBreaker:
            case AlertKind.ErrorStorm:
            case AlertKind.Inactivity:
            case AlertKind.DailySummary:
                return kind;
            case AlertKind.DataGap:
            case AlertKind.Stranger:
            case AlertKind.Error:
            case AlertKind.RiskDataUnavailable:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Not a defined alert kind.");
        }
    }
}
