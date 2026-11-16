using System;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// One closed trade from the broker's history (docs/architecture.md section 3). A data record
/// without behaviour; the risk module checks the values before it uses them (RSK-011).
/// </summary>
/// <param name="PositionId">Id of the position the trade closed. Partial closes of one position share it.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Label">Label of the closed position; a bot trade's label starts with <c>ZFVG|</c>.</param>
/// <param name="EntryTimeUtc">Time the position was opened, in UTC (RSK-010).</param>
/// <param name="CloseTimeUtc">Time the trade closed, in UTC (RSK-007).</param>
/// <param name="NetProfit">Net result of the trade in the account currency: gross profit plus commission plus swap, as the broker books it to the balance (RSK-007).</param>
/// <param name="Commission">Commission of the trade in the account currency; already contained in <c>NetProfit</c>.</param>
/// <param name="Swap">Swap of the trade in the account currency; already contained in <c>NetProfit</c>.</param>
/// <param name="BalanceAfterClose">Account balance right after the trade closed (RSK-008).</param>
public sealed record ClosedTradeInfo(
    long PositionId,
    string Symbol,
    string Label,
    DateTime EntryTimeUtc,
    DateTime CloseTimeUtc,
    double NetProfit,
    double Commission,
    double Swap,
    double BalanceAfterClose)
{
    /// <summary>Id of the position the trade closed. Partial closes of one position share it.</summary>
    public long PositionId { get; init; } = PositionId;

    /// <summary>Symbol name.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Label of the closed position; a bot trade's label starts with <c>ZFVG|</c>.</summary>
    public string Label { get; init; } = Label;

    /// <summary>Time the position was opened, in UTC (RSK-010).</summary>
    public DateTime EntryTimeUtc { get; init; } = EntryTimeUtc;

    /// <summary>Time the trade closed, in UTC (RSK-007).</summary>
    public DateTime CloseTimeUtc { get; init; } = CloseTimeUtc;

    /// <summary>
    /// Net result of the trade in the account currency: gross profit plus commission plus swap,
    /// as the broker books it to the balance (RSK-007).
    /// </summary>
    public double NetProfit { get; init; } = NetProfit;

    /// <summary>Commission of the trade in the account currency; already contained in <see cref="NetProfit"/>.</summary>
    public double Commission { get; init; } = Commission;

    /// <summary>Swap of the trade in the account currency; already contained in <see cref="NetProfit"/>.</summary>
    public double Swap { get; init; } = Swap;

    /// <summary>Account balance right after the trade closed (RSK-008).</summary>
    public double BalanceAfterClose { get; init; } = BalanceAfterClose;

    /// <summary>
    /// Volume closed by this trade in units, or null when the broker port did not report it
    /// (SIM-001, OPS-002). Journal data only; no trading decision reads it.
    /// </summary>
    public double? VolumeUnits { get; init; }

    /// <summary>
    /// Execution price of the closing deal, or null when the broker port did not report it
    /// (SIM-001, OPS-002). Journal data only; no trading decision reads it.
    /// </summary>
    public double? ClosingPrice { get; init; }

    /// <summary>
    /// Why the position closed, as the adapter saw it; <see cref="TradeExitReason.Unknown"/> when
    /// the adapter did not see the close, for example while the bot was stopped (SIM-001, OPS-002).
    /// Journal data only; no trading decision reads it.
    /// </summary>
    public TradeExitReason ExitReason { get; init; } = TradeExitReason.Unknown;
}
