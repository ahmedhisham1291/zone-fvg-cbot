using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-015 (owner decision D of 2026-11-17): XAUUSD is handled by RSK-005 like a pair with base XAU
/// and quote USD, taken from the broker's symbol data. A gold buy is long XAU and short USD, a gold
/// sell short XAU and long USD, so gold counts towards the USD limit together with the forex
/// pairs. MaxPositionsPerCurrency is 2.
/// </summary>
/// <remarks>
/// Forex trades are 100,000 units with a 20-pip stop (risk 200); gold trades are 10 ounces of the
/// example gold symbol (<see cref="GoldData"/>) at 2,650.00 with an 800-pip stop of 8.00 (risk
/// 10 x 800 x 0.01 = 80), so the open-risk cap and the daily budget never interfere.
/// </remarks>
public class GoldCurrencyCapTests
{
    private const string Gold = GoldData.Symbol;

    private static IEnumerable<SymbolInfo> Symbols => RiskData.AllSymbols.Concat(new[] { GoldData.Gold() });

    private static PositionInfo Position(long id, string symbol, Side side)
    {
        (double entry, double stopDistance, double volume) = Plan(symbol);
        double stop = side == Side.Buy ? entry - stopDistance : entry + stopDistance;
        return RiskData.Position(id, symbol, side, volume, entry, stop);
    }

    private static PlannedTrade NewTrade(string symbol, Side side)
    {
        (double entry, double stopDistance, double volume) = Plan(symbol);
        double stop = side == Side.Buy ? entry - stopDistance : entry + stopDistance;
        return RiskData.Trade(symbol, side, entry, stop, volume);
    }

    private static (double Entry, double StopDistance, double Volume) Plan(string symbol)
    {
        return symbol == Gold ? (2650.0, 8.0, 10.0) : (1.1, 0.002, 100000.0);
    }

    private static RiskCheckResult Check(IEnumerable<PositionInfo> positions, PlannedTrade trade)
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), positions, symbols: Symbols);
        return RiskData.Engine.CheckNewTrade(snapshot, trade);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_BuysOnEurUsdAndGbpUsdThenGoldBuy_RefusedForUsdShort()
    {
        // Short USD: EURUSD buy, GBPUSD buy, XAUUSD buy = 3 > 2.
        RiskCheckResult result = Check(new[] { Position(1, "EURUSD", Side.Buy), Position(2, "GBPUSD", Side.Buy) }, NewTrade(Gold, Side.Buy));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal(3.0, result.Measured);
        Assert.Equal(2.0, result.Limit);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_BuysOnEurUsdAndGbpUsdThenGoldSell_AllowedAsLongUsd()
    {
        // The gold sell is long USD (1) and short XAU (1); short USD stays at 2.
        RiskCheckResult result = Check(new[] { Position(1, "EURUSD", Side.Buy), Position(2, "GBPUSD", Side.Buy) }, NewTrade(Gold, Side.Sell));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_BuysOnGoldAndEurUsdThenGbpUsdBuy_RefusedForUsdShort()
    {
        RiskCheckResult result = Check(new[] { Position(1, Gold, Side.Buy), Position(2, "EURUSD", Side.Buy) }, NewTrade("GBPUSD", Side.Buy));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal(3.0, result.Measured);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_GoldBuyAndEurUsdSellThenGbpUsdBuy_AllowedWithUsdShortTwo()
    {
        // Short USD: XAUUSD buy and the new GBPUSD buy = 2; the EURUSD sell is long USD.
        RiskCheckResult result = Check(new[] { Position(1, Gold, Side.Buy), Position(2, "EURUSD", Side.Sell) }, NewTrade("GBPUSD", Side.Buy));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_TwoGoldBuysThenAThird_RefusedForXauLong()
    {
        // XAU long and USD short both reach 3; the base currency is checked first.
        RiskCheckResult result = Check(new[] { Position(1, Gold, Side.Buy), Position(2, Gold, Side.Buy) }, NewTrade(Gold, Side.Buy));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("XAU long", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_CurrenciesComeFromTheBrokerSymbolData()
    {
        // A broker that reported gold with another quote currency would move the count: USD is then untouched.
        SymbolInfo goldInEur = GoldData.Gold() with { QuoteCurrency = "EUR" };
        RiskSnapshot snapshot = RiskData.Snapshot(
            RiskData.Account(100000.0),
            new[] { Position(1, "EURUSD", Side.Buy), Position(2, "GBPUSD", Side.Buy) },
            symbols: RiskData.AllSymbols.Concat(new[] { goldInEur }));

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, NewTrade(Gold, Side.Buy));

        Assert.True(result.IsAllowed);
    }
}
