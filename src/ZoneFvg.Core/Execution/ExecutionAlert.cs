namespace ZoneFvg.Core.Execution;

/// <summary>What an execution alert is about (spec 11, RSK-002, EXE-009 to EXE-011, EXE-013).</summary>
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
}

/// <summary>An alert raised by the execution layer (spec 11). A data record.</summary>
/// <param name="Kind">What the alert is about.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Message">Short invariant text with the ids and values.</param>
public sealed record ExecutionAlert(AlertKind Kind, string Symbol, string Message);
