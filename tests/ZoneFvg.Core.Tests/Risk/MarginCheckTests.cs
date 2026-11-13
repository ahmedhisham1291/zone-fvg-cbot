using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-003: a trade is skipped if estimated free margin after the trade would be below 50% of
/// equity. The margin estimate is the caller's value from the broker port
/// (<c>IBroker.EstimateMargin</c>), carried in <see cref="PlannedTrade.EstimatedMargin"/>.
/// </summary>
public class MarginCheckTests
{
    private static PlannedTrade WithMargin(Side side, double margin)
    {
        double stop = side == Side.Buy ? 1.098 : 1.102;
        return new PlannedTrade("EURUSD", side, 1.1, stop, 250000.0, margin);
    }

    [Theory]
    [Trait("Req", "RSK-003")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void CheckNewTrade_FreeMarginAfterExactlyHalfOfEquity_IsAllowed(Side side)
    {
        // Equity 100,000, free margin 90,000, margin 40,000: 90,000 - 40,000 = 50,000 = 50% of 100,000.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 100000.0, 90000.0));

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, WithMargin(side, 40000.0));

        Assert.True(result.IsAllowed);
        Assert.Equal(RiskReason.None, result.Reason);
    }

    [Theory]
    [Trait("Req", "RSK-003")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void CheckNewTrade_FreeMarginAfterJustBelowHalfOfEquity_IsBlocked(Side side)
    {
        // Margin 40,000.01: free margin after = 49,999.99 < 50,000.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 100000.0, 90000.0));

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, WithMargin(side, 40000.01));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.FreeMarginTooLow, result.Reason);
        Assert.Equal(49999.99, result.Measured, 1e-6);
        Assert.Equal(50000.0, result.Limit, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-003")]
    public void CheckNewTrade_MarginCheck_UsesEquityNotBalance()
    {
        // Balance 100,000 but equity 80,000 and free margin 70,000. Limit 50% x 80,000 = 40,000.
        // Margin 30,000 leaves 40,000 (allowed); 30,000.01 leaves 39,999.99 (blocked).
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 80000.0, 70000.0));

        RiskCheckResult atLimit = RiskData.Engine.CheckNewTrade(snapshot, WithMargin(Side.Buy, 30000.0));
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, WithMargin(Side.Buy, 30000.01));

        Assert.True(atLimit.IsAllowed);
        Assert.Equal(RiskReason.FreeMarginTooLow, beyond.Reason);
        Assert.Equal(40000.0, beyond.Limit, 1e-6);
    }

    [Theory]
    [Trait("Req", "RSK-003")]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CheckNewTrade_MarginEstimateMissingOrNegative_IsBlockedAsInvalidInput(double margin)
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0));

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, WithMargin(Side.Buy, margin));

        Assert.Equal(RiskReason.InvalidInput, result.Reason);
    }
}
