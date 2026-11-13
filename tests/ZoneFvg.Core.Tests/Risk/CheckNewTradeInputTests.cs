using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-011, fail closed: <see cref="RiskEngine.CheckNewTrade"/> decides only on complete, valid
/// input. Missing or broken data gives a refusal with its own reason, never an allowed trade.
/// </summary>
public class CheckNewTradeInputTests
{
    private static readonly PlannedTrade Valid = RiskData.Trade("EURUSD", Side.Buy, 1.1, 1.098, 10000.0);

    private static RiskCheckResult Check(PlannedTrade trade, RiskSnapshot? snapshot = null)
    {
        return RiskData.Engine.CheckNewTrade(snapshot ?? RiskData.Snapshot(RiskData.Account(100000.0)), trade);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_ValidSmallTrade_IsAllowed()
    {
        RiskCheckResult result = Check(Valid);

        Assert.True(result.IsAllowed);
        Assert.Equal(RiskReason.None, result.Reason);
        Assert.Equal(string.Empty, result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_InvalidTradeValues_AreRefusedAsInvalidInput()
    {
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { Symbol = "" }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { Symbol = null! }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { Side = (Side)5 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { Entry = double.NaN }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { StopLoss = 0.0 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { VolumeUnits = 0.0 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, Check(Valid with { VolumeUnits = double.PositiveInfinity }).Reason);
    }

    [Theory]
    [Trait("Req", "RSK-011")]
    [InlineData(0.0, 100000.0)]
    [InlineData(-5.0, 100000.0)]
    [InlineData(100000.0, 0.0)]
    public void CheckNewTrade_BalanceOrEquityNotPositive_IsRefusedAsInvalidInput(double balance, double equity)
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(balance, equity, 100000.0));

        Assert.Equal(RiskReason.InvalidInput, Check(Valid, snapshot).Reason);
    }

    [Theory]
    [Trait("Req", "RSK-011")]
    [InlineData(Side.Buy, 1.102)]
    [InlineData(Side.Buy, 1.1)]
    [InlineData(Side.Sell, 1.098)]
    [InlineData(Side.Sell, 1.1)]
    public void CheckNewTrade_StopNotOnTheLossSide_IsRefused(Side side, double stop)
    {
        // A stop at or beyond entry on the profit side would make the trade's risk look like zero.
        RiskCheckResult result = Check(RiskData.Trade("EURUSD", side, 1.1, stop, 10000.0));

        Assert.Equal(RiskReason.StopOnWrongSide, result.Reason);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_HistoryNotReachingBackFarEnough_IsRefused()
    {
        RiskSnapshot snapshot = RiskData.SnapshotAt(RiskData.Now, RiskData.EpochStart.AddSeconds(1.0), RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());

        RiskCheckResult result = Check(Valid, snapshot);

        Assert.Equal(RiskReason.HistoryIncomplete, result.Reason);
        // The epoch date 2026-01-01 means 00:00 CET = 2025-12-31 23:00 UTC.
        Assert.Equal("history must reach back to 2025-12-31T23:00:00.0000000Z", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_NoSymbolDataForTheNewTrade_IsRefused()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), symbols: new[] { RiskData.GbpUsd });

        RiskCheckResult result = Check(Valid, snapshot);

        Assert.Equal(RiskReason.MissingSymbolInfo, result.Reason);
        Assert.Equal("no symbol data for EURUSD", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_NoSymbolDataForAnOpenBotPosition_IsRefused()
    {
        PositionInfo audPosition = RiskData.Position(1, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.648);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { audPosition }, symbols: new[] { RiskData.EurUsd });

        RiskCheckResult result = Check(Valid, snapshot);

        Assert.Equal(RiskReason.MissingSymbolInfo, result.Reason);
        Assert.Equal("no symbol data for AUDUSD", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_NoSymbolDataForAPendingBotOrder_IsRefused()
    {
        PendingOrderInfo audOrder = RiskData.Order(1, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.648);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), orders: new[] { audOrder }, symbols: new[] { RiskData.EurUsd });

        Assert.Equal(RiskReason.MissingSymbolInfo, Check(Valid, snapshot).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_ManualTradeOnASymbolWithoutData_IsIgnored()
    {
        PositionInfo manual = RiskData.Position(1, "XAUUSD", Side.Buy, 1.0, 2000.0, 1990.0, RiskData.Now, "manual");
        PendingOrderInfo manualOrder = RiskData.Order(2, "XAUUSD", Side.Buy, 1.0, 2000.0, 1990.0, RiskData.Now, "manual");
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { manual }, new[] { manualOrder });

        Assert.True(Check(Valid, snapshot).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void CheckNewTrade_InvalidSymbolData_IsRefused()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), symbols: new[] { RiskData.EurUsd with { PipValuePerUnit = 0.0 } });

        Assert.Equal(RiskReason.MissingSymbolInfo, Check(Valid, snapshot).Reason);
    }
}
