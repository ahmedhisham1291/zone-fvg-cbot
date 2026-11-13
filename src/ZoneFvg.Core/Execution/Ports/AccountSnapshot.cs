namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// The account values read from the broker port at one moment (docs/architecture.md section 3).
/// A data record without behaviour; the risk module checks the values before it uses them
/// (RSK-011).
/// </summary>
/// <param name="Balance">Account balance in the account currency.</param>
/// <param name="Equity">Account equity in the account currency, including open trades.</param>
/// <param name="FreeMargin">Free margin in the account currency (RSK-003).</param>
/// <param name="Currency">Account currency code, for example USD.</param>
public sealed record AccountSnapshot(double Balance, double Equity, double FreeMargin, string Currency)
{
    /// <summary>Account balance in the account currency (RSK-001, RSK-007).</summary>
    public double Balance { get; init; } = Balance;

    /// <summary>Account equity in the account currency, including open trades (RSK-008, RSK-009).</summary>
    public double Equity { get; init; } = Equity;

    /// <summary>Free margin in the account currency (RSK-003).</summary>
    public double FreeMargin { get; init; } = FreeMargin;

    /// <summary>Account currency code, for example USD.</summary>
    public string Currency { get; init; } = Currency;
}
