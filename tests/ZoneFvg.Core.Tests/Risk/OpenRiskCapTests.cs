using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-004: the sum of risk at stop of every bot position and pending order, plus the new trade,
/// must not exceed MaxOpenRiskPercent (1.5%) of InitialCapital: 1,500 USD with the defaults.
/// Risk at stop = stop distance in pips x pip value per unit x volume (spec 4.2), without
/// commission.
/// </summary>
/// <remarks>
/// Worked numbers used below: EURUSD 250,000 units, entry 1.1000, stop 1.0980 = 20 x 0.0001 x
/// 250,000 = 500. USDJPY 312,500 units, entry 125.00, stop 124.80 = 20 x 0.00008 x 312,500 = 500.
/// GBPUSD 250,000 units, 20 pips = 500; 251,000 units = 502. The three pairs are chosen so that
/// no currency has more than two same-side trades (RSK-005 stays quiet).
/// </remarks>
public class OpenRiskCapTests
{
    private static PositionInfo EurUsdLong => RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, 1.098);

    private static PositionInfo UsdJpyLong => RiskData.Position(2, "USDJPY", Side.Buy, 312500.0, 125.0, 124.8);

    private static PlannedTrade GbpUsdLong(double volume)
    {
        return RiskData.Trade("GBPUSD", Side.Buy, 1.3, 1.298, volume);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_ThirdTradeAtHalfPercent_IsAllowedExactlyAtTheCap()
    {
        // 500 + 500 + 500 = 1,500 = 1.5% of 100,000.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong, UsdJpyLong });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_FourthTradeAtHalfPercent_IsBlocked()
    {
        // A third open trade: AUDUSD sell 250,000 at 0.6500, stop 0.6520 = 500. Total with the new trade: 2,000.
        PositionInfo audUsdShort = RiskData.Position(3, "AUDUSD", Side.Sell, 250000.0, 0.65, 0.652);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong, UsdJpyLong, audUsdShort });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.OpenRiskCapExceeded, result.Reason);
        Assert.Equal(2000.0, result.Measured, 1e-6);
        Assert.Equal(1500.0, result.Limit, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_OneStepAboveTheCap_IsBlocked()
    {
        // 500 + 500 + 502 = 1,502 > 1,500.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong, UsdJpyLong });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(251000.0));

        Assert.Equal(RiskReason.OpenRiskCapExceeded, result.Reason);
        Assert.Equal(1502.0, result.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_PendingOrdersCount()
    {
        // The USDJPY risk now sits in a pending order instead of a position: same totals.
        PendingOrderInfo usdJpyOrder = RiskData.Order(20, "USDJPY", Side.Buy, 312500.0, 125.0, 124.8);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong }, new[] { usdJpyOrder });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0)).IsAllowed);
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(251000.0));
        Assert.Equal(RiskReason.OpenRiskCapExceeded, beyond.Reason);
        Assert.Equal(1502.0, beyond.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_SellTrades_CountTheSame()
    {
        // Mirror: EURUSD sell 250,000 at 1.1000, stop 1.1020 = 500; USDJPY sell 312,500 at 125.00, stop 125.20 = 500;
        // new GBPUSD sell at 1.3000, stop 1.3020: 250,000 = 500 (allowed), 251,000 = 502 (blocked).
        PositionInfo eurUsdShort = RiskData.Position(1, "EURUSD", Side.Sell, 250000.0, 1.1, 1.102);
        PositionInfo usdJpyShort = RiskData.Position(2, "USDJPY", Side.Sell, 312500.0, 125.0, 125.2);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { eurUsdShort, usdJpyShort });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, RiskData.Trade("GBPUSD", Side.Sell, 1.3, 1.302, 250000.0)).IsAllowed);
        Assert.Equal(RiskReason.OpenRiskCapExceeded, RiskData.Engine.CheckNewTrade(snapshot, RiskData.Trade("GBPUSD", Side.Sell, 1.3, 1.302, 251000.0)).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_PositionsWithoutTheBotLabel_AreIgnored()
    {
        // A manual (not bot-labelled) position on EURUSD with a large risk (1,000,000 units x 50 pips = 5,000) does not count.
        PositionInfo manual = RiskData.Position(9, "EURUSD", Side.Buy, 1000000.0, 1.1, 1.095, RiskData.Now.AddHours(-1.0), "manual");
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong, UsdJpyLong, manual });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0)).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_PositionWithoutStop_CountsItsFullSizingRuleRisk()
    {
        // The EURUSD position has no stop: it counts as 0.5% of the balance 100,000 = 500.
        PositionInfo noStop = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, null);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { noStop, UsdJpyLong });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0)).IsAllowed);
        Assert.Equal(RiskReason.OpenRiskCapExceeded, RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(251000.0)).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_SumsBotPositionsAndOrders_AndReportsMissingStops()
    {
        // Positions: EURUSD 500 (stop), GBPUSD without stop -> 0.5% x 100,000 = 500, manual ignored.
        // Orders: USDJPY 500 (stop), AUDUSD without stop -> 500. Total 2,000.
        PositionInfo gbpNoStop = RiskData.Position(4, "GBPUSD", Side.Sell, 100000.0, 1.3, null);
        PositionInfo manual = RiskData.Position(5, "EURUSD", Side.Buy, 100000.0, 1.1, 1.09, RiskData.Now, "manual");
        PendingOrderInfo usdJpyOrder = RiskData.Order(20, "USDJPY", Side.Buy, 312500.0, 125.0, 124.8);
        PendingOrderInfo audNoStop = RiskData.Order(21, "AUDUSD", Side.Buy, 100000.0, 0.65, null);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong, gbpNoStop, manual }, new[] { usdJpyOrder, audNoStop });

        OpenRiskReport report = RiskData.Engine.OpenRiskAtStop(snapshot);

        Assert.Equal(2000.0, report.RiskMoney, 1e-6);
        Assert.Equal(new long[] { 4 }, report.PositionIdsWithoutStop.ToArray());
        Assert.Equal(new long[] { 21 }, report.OrderIdsWithoutStop.ToArray());
        Assert.True(report.HasViolation);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_StopInProfit_CountsAsNoRisk()
    {
        // A buy at 1.1000 with the stop moved to 1.1010 cannot lose at the stop: risk 0, not -250.
        PositionInfo lockedIn = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, 1.101);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { lockedIn, UsdJpyLong });

        OpenRiskReport report = RiskData.Engine.OpenRiskAtStop(snapshot);

        Assert.Equal(500.0, report.RiskMoney, 1e-6);
        Assert.False(report.HasViolation);
        Assert.Empty(report.PositionIdsWithoutStop);
        Assert.Empty(report.OrderIdsWithoutStop);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_NoBotTrades_IsZero()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0));

        Assert.Equal(0.0, RiskData.Engine.OpenRiskAtStop(snapshot).RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_BotTradeOnASymbolWithoutData_Throws()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong }, symbols: new[] { RiskData.UsdJpy });

        Assert.Throws<InvalidOperationException>(() => RiskData.Engine.OpenRiskAtStop(snapshot));
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_BotTradeOnASymbolWithInvalidData_Throws()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong }, symbols: new[] { RiskData.EurUsd with { PipSize = 0.0 } });

        Assert.Throws<InvalidOperationException>(() => RiskData.Engine.OpenRiskAtStop(snapshot));
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void OpenRiskAtStop_BotTradeWithoutStopOnASymbolWithoutData_CountsTheSizingRuleRisk()
    {
        // Without a stop the risk does not depend on symbol data: 0.5% x 100,000 = 500, reported as a violation.
        PositionInfo noStop = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, null);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { noStop }, symbols: new[] { RiskData.UsdJpy });

        OpenRiskReport report = RiskData.Engine.OpenRiskAtStop(snapshot);

        Assert.Equal(500.0, report.RiskMoney, 1e-9);
        Assert.True(report.HasViolation);
    }
}
