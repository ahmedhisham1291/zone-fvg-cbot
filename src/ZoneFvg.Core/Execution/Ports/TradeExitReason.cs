namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// Why a trade ended, as the adapter reports it (OPS-002, SIM-001, spec 11). It is a broker data
/// value: the adapter fills it into <see cref="ClosedTradeInfo.ExitReason"/>.
/// </summary>
public enum TradeExitReason
{
    /// <summary>The take profit filled at the broker.</summary>
    TakeProfit,

    /// <summary>The stop loss filled at the broker.</summary>
    StopLoss,

    /// <summary>The broker closed the position for lack of margin.</summary>
    StopOut,

    /// <summary>The bot closed the position (Friday close, emergency guard, no stop loss).</summary>
    ClosedByBot,

    /// <summary>The position was closed by someone else, for example by hand.</summary>
    ClosedOutside,

    /// <summary>The reason is not known, for example for a trade backfilled after an outage.</summary>
    Unknown,
}
