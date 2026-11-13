using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-006: M = balance at 00:00 CE(S)T; W = worst-case equity if every open bot position and
/// pending order, including the new trade, were closed at its stop. A new trade is allowed only
/// if M - W &lt;= DailyLossLimitPercent (1.5%) of InitialCapital = 1,500 USD.
/// </summary>
/// <remarks>
/// W = current balance + result at stop of each open bot position (negative, or positive when
/// the stop is in profit) - risk at stop of each pending bot order - risk at stop of the new
/// trade - round-trip commission of each of them (WP04 prompt, "Decisions already made").
/// The realised losses below are closed trades without the bot label, so they count for M
/// (RSK-007 uses all account trades) but not for the entry limits of RSK-010.
/// </remarks>
public class DailyBudgetTests
{
    private static ClosedTradeInfo RealisedToday(double net, double balanceAfter)
    {
        return RiskData.Closed(50, "EURUSD", RiskData.Now.AddHours(-5.0), RiskData.Now.AddHours(-4.0), net, balanceAfter, "manual");
    }

    private static PlannedTrade GbpUsdLong(double volume)
    {
        // 20-pip stop: risk = 20 x 0.0001 x volume.
        return RiskData.Trade("GBPUSD", Side.Buy, 1.3, 1.298, volume);
    }

    private static PositionInfo EurUsdLong(double volume)
    {
        return RiskData.Position(1, "EURUSD", Side.Buy, volume, 1.1, 1.098);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_BudgetExactlyUsed_IsAllowed()
    {
        // Realised today -300: balance 99,700, M = 100,000. Open EURUSD long 250,000 at stop: -500.
        // New GBPUSD 350,000 x 20 pips = 700. W = 99,700 - 500 - 700 = 98,500; M - W = 1,500 = budget.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), new[] { EurUsdLong(250000.0) }, history: new[] { RealisedToday(-300.0, 99700.0) });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(350000.0));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_BudgetExceeded_IsBlocked()
    {
        // As above with 351,000 units: 702; M - W = 300 + 500 + 702 = 1,502 > 1,500. Open risk 1,202 is fine.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), new[] { EurUsdLong(250000.0) }, history: new[] { RealisedToday(-300.0, 99700.0) });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(351000.0));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.DailyBudgetExceeded, result.Reason);
        Assert.Equal(1502.0, result.Measured, 1e-6);
        Assert.Equal(1500.0, result.Limit, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_SellTrades_MirrorTheBuyCase()
    {
        // EURUSD short 250,000 at 1.1000, stop 1.1020: -500. New GBPUSD short at 1.3000, stop 1.3020.
        PositionInfo eurUsdShort = RiskData.Position(1, "EURUSD", Side.Sell, 250000.0, 1.1, 1.102);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), new[] { eurUsdShort }, history: new[] { RealisedToday(-300.0, 99700.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, RiskData.Trade("GBPUSD", Side.Sell, 1.3, 1.302, 350000.0)).IsAllowed);
        Assert.Equal(RiskReason.DailyBudgetExceeded, RiskData.Engine.CheckNewTrade(snapshot, RiskData.Trade("GBPUSD", Side.Sell, 1.3, 1.302, 351000.0)).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_PendingOrdersCount()
    {
        // The EURUSD risk of 500 sits in a pending order: same result as the open position.
        PendingOrderInfo order = RiskData.Order(20, "EURUSD", Side.Buy, 250000.0, 1.1, 1.098);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), orders: new[] { order }, history: new[] { RealisedToday(-300.0, 99700.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(350000.0)).IsAllowed);
        Assert.Equal(RiskReason.DailyBudgetExceeded, RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(351000.0)).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_CommissionIsIncluded()
    {
        // Round-trip commission 0.00005 USD per unit (5 USD per lot) on both pairs.
        // Realised -270: balance 99,730, M = 100,000. EURUSD long 200,000: -400 - 10 commission = -410.
        // New GBPUSD 400,000: -800 - 20 = -820. M - W = 270 + 410 + 820 = 1,500 (allowed).
        // New GBPUSD 401,000: -802 - 20.05 = -822.05. M - W = 1,502.05 (blocked). Open risk 1,202 is fine.
        SymbolInfo[] symbols = { RiskData.EurUsd with { RoundTripCommissionPerUnit = 0.00005 }, RiskData.GbpUsd with { RoundTripCommissionPerUnit = 0.00005 } };
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99730.0), new[] { EurUsdLong(200000.0) }, history: new[] { RealisedToday(-270.0, 99730.0) }, symbols: symbols);

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(400000.0)).IsAllowed);
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(401000.0));
        Assert.Equal(RiskReason.DailyBudgetExceeded, beyond.Reason);
        Assert.Equal(1502.05, beyond.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_OpenPositionLossAtStopCounts_NotItsCurrentFloatingResult()
    {
        // Realised -500: balance 99,500, M = 100,000. The open EURUSD long shows +100 floating (equity 99,600)
        // but counts at its stop: -500. New 250,000: M - W = 500 + 500 + 500 = 1,500 (allowed);
        // new 251,000: 1,502 (blocked). Ignoring the open position would give 1,002 and allow it.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99500.0, 99600.0, 99500.0), new[] { EurUsdLong(250000.0) }, history: new[] { RealisedToday(-500.0, 99500.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(250000.0)).IsAllowed);
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(251000.0));
        Assert.Equal(RiskReason.DailyBudgetExceeded, beyond.Reason);
        Assert.Equal(1502.0, beyond.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_StopInProfit_FreesBudget()
    {
        // Realised -1,000: balance 99,000, M = 100,000. EURUSD long 250,000 at 1.1000 with the stop at 1.1010:
        // +10 pips x 0.0001 x 250,000 = +250 at the stop. New GBPUSD 375,000: -750.
        // M - W = 1,000 - 250 + 750 = 1,500 (allowed); 376,000: 1,502 (blocked).
        PositionInfo lockedIn = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, 1.101);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99000.0), new[] { lockedIn }, history: new[] { RealisedToday(-1000.0, 99000.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(375000.0)).IsAllowed);
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(376000.0));
        Assert.Equal(RiskReason.DailyBudgetExceeded, beyond.Reason);
        Assert.Equal(1502.0, beyond.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_PositionWithoutStop_CountsItsFullSizingRuleRisk()
    {
        // Balance 99,700: sizing-rule risk = 0.5% x 99,700 = 498.50. M - W = 300 + 498.50 + 700 = 1,498.50 (allowed);
        // with 351,000 units: 300 + 498.50 + 702 = 1,500.50 (blocked).
        PositionInfo noStop = RiskData.Position(1, "EURUSD", Side.Buy, 250000.0, 1.1, null);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), new[] { noStop }, history: new[] { RealisedToday(-300.0, 99700.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(350000.0)).IsAllowed);
        RiskCheckResult beyond = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(351000.0));
        Assert.Equal(RiskReason.DailyBudgetExceeded, beyond.Reason);
        Assert.Equal(1500.5, beyond.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_PendingOrderWithoutStop_CountsItsFullSizingRuleRisk()
    {
        PendingOrderInfo noStop = RiskData.Order(20, "EURUSD", Side.Buy, 250000.0, 1.1, null);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99700.0), orders: new[] { noStop }, history: new[] { RealisedToday(-300.0, 99700.0) });

        Assert.True(RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(350000.0)).IsAllowed);
        Assert.Equal(1500.5, RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(351000.0)).Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_BudgetAlreadyUsedUp_BlocksEvenTheSmallestTrade()
    {
        // Realised -1,500: M - W is already 1,500; a 1,000-unit trade adds 2 -> 1,502.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98500.0), history: new[] { RealisedToday(-1500.0, 98500.0) });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, GbpUsdLong(1000.0));

        Assert.Equal(RiskReason.DailyBudgetExceeded, result.Reason);
        Assert.Equal(1502.0, result.Measured, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudgetExhausted_ExactlyUsed_IsNotExhausted()
    {
        // Realised -1,500, nothing open: M = 100,000, W = 98,500, used 1,500 = budget, not beyond it.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98500.0), history: new[] { RealisedToday(-1500.0, 98500.0) });

        DailyBudgetStatus status = RiskData.Engine.DailyBudgetExhausted(snapshot);

        Assert.False(status.Exhausted);
        Assert.Equal(100000.0, status.DayStartBalance, 1e-6);
        Assert.Equal(98500.0, status.WorstCaseEquity, 1e-6);
        Assert.Equal(1500.0, status.Used, 1e-6);
        Assert.Equal(1500.0, status.Limit, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudgetExhausted_OneCentBeyond_IsExhausted()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98499.99), history: new[] { RealisedToday(-1500.01, 98499.99) });

        DailyBudgetStatus status = RiskData.Engine.DailyBudgetExhausted(snapshot);

        Assert.True(status.Exhausted);
        Assert.Equal(1500.01, status.Used, 1e-6);
    }

    [Theory]
    [Trait("Req", "RSK-006")]
    [InlineData(Side.Buy, 1.098)]
    [InlineData(Side.Sell, 1.102)]
    public void DailyBudgetExhausted_OpenPositionAtStopPushesBeyond_IsExhausted(Side side, double stop)
    {
        // Realised -1,200 plus an open position losing 500 at its stop: 1,700 > 1,500.
        PositionInfo open = RiskData.Position(1, "EURUSD", side, 250000.0, 1.1, stop);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98800.0), new[] { open }, history: new[] { RealisedToday(-1200.0, 98800.0) });

        DailyBudgetStatus status = RiskData.Engine.DailyBudgetExhausted(snapshot);

        Assert.True(status.Exhausted);
        Assert.Equal(1700.0, status.Used, 1e-6);
        Assert.Equal(98300.0, status.WorstCaseEquity, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudgetExhausted_PendingOrderPushesBeyond_IsExhausted()
    {
        // Realised -1,000.01 plus a pending order risking 500: 1,500.01 > 1,500.
        PendingOrderInfo order = RiskData.Order(20, "EURUSD", Side.Buy, 250000.0, 1.1, 1.098);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98999.99), orders: new[] { order }, history: new[] { RealisedToday(-1000.01, 98999.99) });

        Assert.True(RiskData.Engine.DailyBudgetExhausted(snapshot).Exhausted);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudgetExhausted_NoLossToday_IsNotExhausted()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0));

        DailyBudgetStatus status = RiskData.Engine.DailyBudgetExhausted(snapshot);

        Assert.False(status.Exhausted);
        Assert.Equal(0.0, status.Used, 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudgetExhausted_BotTradeOnASymbolWithoutData_Throws()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { EurUsdLong(1000.0) }, symbols: new[] { RiskData.GbpUsd });

        Assert.Throws<InvalidOperationException>(() => RiskData.Engine.DailyBudgetExhausted(snapshot));
    }
}
