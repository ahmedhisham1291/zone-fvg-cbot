using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>The broker port data records carry exactly the values they were built with.</summary>
public class PortRecordTests
{
    private static readonly DateTime Time = new(2026, 1, 14, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void AccountSnapshot_KeepsItsValues()
    {
        var account = new AccountSnapshot(100000.0, 99500.0, 80000.0, "USD");

        Assert.Equal(100000.0, account.Balance);
        Assert.Equal(99500.0, account.Equity);
        Assert.Equal(80000.0, account.FreeMargin);
        Assert.Equal("USD", account.Currency);
    }

    [Fact]
    public void SymbolInfo_KeepsItsValues()
    {
        var symbol = new SymbolInfo("EURUSD", "EUR", "USD", 0.0001, 0.0001, 1000.0, 100.0, 5000000.0, 0.00012, false, 0.00005);

        Assert.Equal("EURUSD", symbol.Name);
        Assert.Equal("EUR", symbol.BaseCurrency);
        Assert.Equal("USD", symbol.QuoteCurrency);
        Assert.Equal(0.0001, symbol.PipSize);
        Assert.Equal(0.0001, symbol.PipValuePerUnit);
        Assert.Equal(1000.0, symbol.VolumeMinUnits);
        Assert.Equal(100.0, symbol.VolumeStepUnits);
        Assert.Equal(5000000.0, symbol.VolumeMaxUnits);
        Assert.Equal(0.00012, symbol.Spread);
        Assert.False(symbol.IsMarketOpen);
        Assert.Equal(0.00005, symbol.RoundTripCommissionPerUnit);
    }

    [Fact]
    public void PositionInfo_KeepsItsValues()
    {
        var position = new PositionInfo(7, "GBPUSD", Side.Sell, 25000.0, 1.3, 1.302, 1.296, "ZFVG|v1|GBPUSD|S-1", "e=1.3", Time);

        Assert.Equal(7L, position.Id);
        Assert.Equal("GBPUSD", position.Symbol);
        Assert.Equal(Side.Sell, position.Side);
        Assert.Equal(25000.0, position.VolumeUnits);
        Assert.Equal(1.3, position.EntryPrice);
        Assert.Equal(1.302, position.StopLoss);
        Assert.Equal(1.296, position.TakeProfit);
        Assert.Equal("ZFVG|v1|GBPUSD|S-1", position.Label);
        Assert.Equal("e=1.3", position.Comment);
        Assert.Equal(Time, position.OpenTimeUtc);
    }

    [Fact]
    public void PendingOrderInfo_KeepsItsValues()
    {
        var order = new PendingOrderInfo(8, "AUDUSD", Side.Buy, 30000.0, 0.65, null, null, "ZFVG|v1|AUDUSD|D-1", "", Time);

        Assert.Equal(8L, order.Id);
        Assert.Equal("AUDUSD", order.Symbol);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(30000.0, order.VolumeUnits);
        Assert.Equal(0.65, order.EntryPrice);
        Assert.Null(order.StopLoss);
        Assert.Null(order.TakeProfit);
        Assert.Equal("ZFVG|v1|AUDUSD|D-1", order.Label);
        Assert.Equal(string.Empty, order.Comment);
        Assert.Equal(Time, order.CreatedTimeUtc);
    }

    [Fact]
    public void ClosedTradeInfo_KeepsItsValues()
    {
        var trade = new ClosedTradeInfo(9, "USDJPY", "manual", Time.AddHours(-1.0), Time, -310.0, -7.0, -3.0, 99690.0);

        Assert.Equal(9L, trade.PositionId);
        Assert.Equal("USDJPY", trade.Symbol);
        Assert.Equal("manual", trade.Label);
        Assert.Equal(Time.AddHours(-1.0), trade.EntryTimeUtc);
        Assert.Equal(Time, trade.CloseTimeUtc);
        Assert.Equal(-310.0, trade.NetProfit);
        Assert.Equal(-7.0, trade.Commission);
        Assert.Equal(-3.0, trade.Swap);
        Assert.Equal(99690.0, trade.BalanceAfterClose);
    }
}
