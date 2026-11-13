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
    public long PositionId { get; } = PositionId;

    /// <summary>Symbol name.</summary>
    public string Symbol { get; } = Symbol;

    /// <summary>Label of the closed position; a bot trade's label starts with <c>ZFVG|</c>.</summary>
    public string Label { get; } = Label;

    /// <summary>Time the position was opened, in UTC (RSK-010).</summary>
    public DateTime EntryTimeUtc { get; } = EntryTimeUtc;

    /// <summary>Time the trade closed, in UTC (RSK-007).</summary>
    public DateTime CloseTimeUtc { get; } = CloseTimeUtc;

    /// <summary>
    /// Net result of the trade in the account currency: gross profit plus commission plus swap,
    /// as the broker books it to the balance (RSK-007).
    /// </summary>
    public double NetProfit { get; } = NetProfit;

    /// <summary>Commission of the trade in the account currency; already contained in <see cref="NetProfit"/>.</summary>
    public double Commission { get; } = Commission;

    /// <summary>Swap of the trade in the account currency; already contained in <see cref="NetProfit"/>.</summary>
    public double Swap { get; } = Swap;

    /// <summary>Account balance right after the trade closed (RSK-008).</summary>
    public double BalanceAfterClose { get; } = BalanceAfterClose;
}
