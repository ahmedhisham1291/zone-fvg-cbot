using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-014 (owner decision C of 2026-11-17): in a backtest with BacktestHaltsLogOnly the daily
/// budget check of a new trade (RSK-006) does not refuse it; the failed check is returned for the
/// would-block line. Without the flag the check is exactly <see cref="RiskEngine.CheckNewTrade"/>.
/// Data as in <see cref="DailyBudgetTests"/>: M = 100,000, realised -300, open EURUSD risk 500,
/// new GBPUSD 351,000 x 20 pips = 702; M - W = 1,502 above 1,500.
/// </summary>
public class DailyBudgetWaiverTests
{
    private static readonly ClosedTradeInfo RealisedToday = RiskData.Closed(50, "EURUSD", RiskData.Now.AddHours(-5.0), RiskData.Now.AddHours(-4.0), -300.0, 99700.0, "manual");

    private static RiskSnapshot Snapshot(params ClosedTradeInfo[] extra)
    {
        PositionInfo eurUsd = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, 1.098);
        return RiskData.Snapshot(RiskData.Account(99700.0), new[] { eurUsd }, history: new[] { RealisedToday }.Concat(extra));
    }

    private static PlannedTrade GbpUsdLong(double volume)
    {
        return RiskData.Trade("GBPUSD", Side.Buy, 1.3, 1.298, volume);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithWaiver_FlagOff_RefusesExactlyAsCheckNewTrade()
    {
        RiskCheckResult live = RiskData.Engine.CheckNewTrade(Snapshot(), GbpUsdLong(351000.0));
        RiskCheckResult result = RiskData.Engine.CheckNewTradeWithWaiver(Snapshot(), GbpUsdLong(351000.0), false, out RiskCheckResult? waived);

        Assert.Equal(RiskReason.DailyBudgetExceeded, result.Reason);
        Assert.Equal(live.Reason, result.Reason);
        Assert.Equal(live.Measured, result.Measured);
        Assert.Equal(live.Limit, result.Limit);
        Assert.Equal(live.Detail, result.Detail);
        Assert.Null(waived);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithWaiver_FlagOnAboveTheBudget_AllowedAndTheCheckReturned()
    {
        RiskCheckResult result = RiskData.Engine.CheckNewTradeWithWaiver(Snapshot(), GbpUsdLong(351000.0), true, out RiskCheckResult? waived);

        Assert.True(result.IsAllowed);
        Assert.NotNull(waived);
        Assert.Equal(RiskReason.DailyBudgetExceeded, waived!.Reason);
        Assert.Equal(1502.0, waived.Measured, 1e-6);
        Assert.Equal(1500.0, waived.Limit, 1e-6);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithWaiver_FlagOnWithinTheBudget_NothingWaived()
    {
        RiskCheckResult result = RiskData.Engine.CheckNewTradeWithWaiver(Snapshot(), GbpUsdLong(350000.0), true, out RiskCheckResult? waived);

        Assert.True(result.IsAllowed);
        Assert.Null(waived);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithWaiver_FlagOnAboveTheBudgetAndThirdEntry_StillRefusedByTheEntryLimit()
    {
        ClosedTradeInfo first = RiskData.Closed(2, "GBPUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 0.0, 99700.0);
        ClosedTradeInfo second = RiskData.Closed(3, "GBPUSD", RiskData.Now.AddHours(-2.0), RiskData.Now.AddHours(-1.0), 0.0, 99700.0);

        RiskCheckResult result = RiskData.Engine.CheckNewTradeWithWaiver(Snapshot(first, second), GbpUsdLong(351000.0), true, out RiskCheckResult? waived);

        Assert.Equal(RiskReason.SymbolEntryLimitReached, result.Reason);
        Assert.NotNull(waived);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithWaiver_FlagOnAndAnEarlierCheckFails_RefusedAndNothingWaived()
    {
        // Free margin: the margin estimate leaves less than half of the equity free (RSK-003).
        PlannedTrade trade = GbpUsdLong(351000.0) with { EstimatedMargin = 90000.0 };

        RiskCheckResult result = RiskData.Engine.CheckNewTradeWithWaiver(Snapshot(), trade, true, out RiskCheckResult? waived);

        Assert.False(result.IsAllowed);
        Assert.NotEqual(RiskReason.DailyBudgetExceeded, result.Reason);
        Assert.Null(waived);
    }
}
