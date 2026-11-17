using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-014 (owner decision C of 2026-11-17): in a backtest with BacktestHaltsLogOnly the daily
/// budget check of a new trade (RSK-006) does not refuse it: after <see cref="RiskEngine.CheckNewTrade"/>
/// refused for the budget alone, <see cref="RiskEngine.CheckNewTradeWithoutDailyBudget"/> decides with
/// every other check. Live code calls only <see cref="RiskEngine.CheckNewTrade"/>.
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
    public void CheckNewTradeWithoutDailyBudget_AboveTheBudget_AllowedWhileCheckNewTradeRefuses()
    {
        RiskCheckResult live = RiskData.Engine.CheckNewTrade(Snapshot(), GbpUsdLong(351000.0));
        RiskCheckResult backtest = RiskData.Engine.CheckNewTradeWithoutDailyBudget(Snapshot(), GbpUsdLong(351000.0));

        Assert.Equal(RiskReason.DailyBudgetExceeded, live.Reason);
        Assert.Equal(1502.0, live.Measured, 1e-6);
        Assert.Equal(1500.0, live.Limit, 1e-6);
        Assert.True(backtest.IsAllowed);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithoutDailyBudget_ThirdEntry_StillRefusedByTheEntryLimit()
    {
        ClosedTradeInfo first = RiskData.Closed(2, "GBPUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 0.0, 99700.0);
        ClosedTradeInfo second = RiskData.Closed(3, "GBPUSD", RiskData.Now.AddHours(-2.0), RiskData.Now.AddHours(-1.0), 0.0, 99700.0);

        Assert.Equal(RiskReason.DailyBudgetExceeded, RiskData.Engine.CheckNewTrade(Snapshot(first, second), GbpUsdLong(351000.0)).Reason);
        Assert.Equal(RiskReason.SymbolEntryLimitReached, RiskData.Engine.CheckNewTradeWithoutDailyBudget(Snapshot(first, second), GbpUsdLong(351000.0)).Reason);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithoutDailyBudget_AnEarlierCheckFails_SameResultAsCheckNewTrade()
    {
        // Free margin: the margin estimate leaves less than half of the equity free (RSK-003).
        PlannedTrade trade = GbpUsdLong(351000.0) with { EstimatedMargin = 90000.0 };

        RiskCheckResult live = RiskData.Engine.CheckNewTrade(Snapshot(), trade);
        RiskCheckResult backtest = RiskData.Engine.CheckNewTradeWithoutDailyBudget(Snapshot(), trade);

        Assert.False(live.IsAllowed);
        Assert.NotEqual(RiskReason.DailyBudgetExceeded, live.Reason);
        Assert.Equal(live.Reason, backtest.Reason);
        Assert.Equal(live.Measured, backtest.Measured);
        Assert.Equal(live.Limit, backtest.Limit);
        Assert.Equal(live.Detail, backtest.Detail);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void CheckNewTradeWithoutDailyBudget_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => RiskData.Engine.CheckNewTradeWithoutDailyBudget(null!, GbpUsdLong(1000.0)));
        Assert.Throws<ArgumentNullException>(() => RiskData.Engine.CheckNewTradeWithoutDailyBudget(Snapshot(), null!));
    }
}
