using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-005: buying a pair is long its base and short its quote currency; selling is the mirror.
/// For each currency, the number of bot positions and pending orders on the same side, including
/// the new trade, must not exceed MaxPositionsPerCurrency (2).
/// </summary>
/// <remarks>
/// All trades here are 100,000 units with a 20-pip stop (risk 200 each), so the open-risk cap
/// (1,500) and the daily budget never interfere.
/// </remarks>
public class CurrencyExposureTests
{
    private static readonly SymbolInfo EurGbp = RiskData.Major("EURGBP", "EUR", "GBP");
    private static readonly SymbolInfo EurJpy = new("EURJPY", "EUR", "JPY", 0.01, 0.00008, 1000.0, 1000.0, 10000000.0, 0.01, true, 0.0);

    private static IEnumerable<SymbolInfo> Symbols => RiskData.AllSymbols.Concat(new[] { EurGbp, EurJpy });

    private static PositionInfo Long(long id, string symbol)
    {
        return RiskData.Position(id, symbol, Side.Buy, 100000.0, Entry(symbol), Entry(symbol) - Twenty(symbol));
    }

    private static PositionInfo Short(long id, string symbol)
    {
        return RiskData.Position(id, symbol, Side.Sell, 100000.0, Entry(symbol), Entry(symbol) + Twenty(symbol));
    }

    private static PlannedTrade NewLong(string symbol)
    {
        return RiskData.Trade(symbol, Side.Buy, Entry(symbol), Entry(symbol) - Twenty(symbol), 100000.0);
    }

    private static PlannedTrade NewShort(string symbol)
    {
        return RiskData.Trade(symbol, Side.Sell, Entry(symbol), Entry(symbol) + Twenty(symbol), 100000.0);
    }

    private static double Entry(string symbol)
    {
        return symbol.EndsWith("JPY", StringComparison.Ordinal) ? 125.0 : 1.1;
    }

    private static double Twenty(string symbol)
    {
        return symbol.EndsWith("JPY", StringComparison.Ordinal) ? 0.2 : 0.002;
    }

    private static RiskCheckResult Check(IEnumerable<PositionInfo> positions, PlannedTrade trade, IEnumerable<PendingOrderInfo>? orders = null)
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), positions, orders, symbols: Symbols);
        return RiskData.Engine.CheckNewTrade(snapshot, trade);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_LongEurUsdGbpUsdThenAudUsd_IsBlockedOnUsd()
    {
        // Short USD: EURUSD long, GBPUSD long, AUDUSD long = 3 > 2.
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "GBPUSD") }, NewLong("AUDUSD"));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal(3.0, result.Measured);
        Assert.Equal(2.0, result.Limit);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_LongEurUsdGbpUsdThenUsdJpy_IsAllowed()
    {
        // Short USD 2 (EURUSD, GBPUSD), long USD 1 (USDJPY): nothing above 2.
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "GBPUSD") }, NewLong("USDJPY"));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_ShortEurUsdGbpUsdThenAudUsd_IsBlockedOnUsd()
    {
        // Mirror: long USD 3 > 2.
        RiskCheckResult result = Check(new[] { Short(1, "EURUSD"), Short(2, "GBPUSD") }, NewShort("AUDUSD"));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("USD long", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_ShortEurUsdGbpUsdThenShortUsdJpy_IsAllowed()
    {
        // Mirror: long USD 2, short USD 1.
        RiskCheckResult result = Check(new[] { Short(1, "EURUSD"), Short(2, "GBPUSD") }, NewShort("USDJPY"));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_PendingOrdersCount()
    {
        // The GBPUSD long is a pending order: still 3 short USD.
        PendingOrderInfo gbpOrder = RiskData.Order(20, "GBPUSD", Side.Buy, 100000.0, 1.1, 1.098);

        RiskCheckResult result = Check(new[] { Long(1, "EURUSD") }, NewLong("AUDUSD"), new[] { gbpOrder });

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal(3.0, result.Measured);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_BaseCurrency_CountsToo()
    {
        // Long EUR: EURUSD long, EURGBP long, EURJPY long = 3.
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "EURGBP") }, NewLong("EURJPY"));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("EUR long", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_OppositeSidesOfOneCurrency_DoNotAddUp()
    {
        // USD: short (EURUSD long), long (USDJPY long), short (GBPUSD long) -> short 2, long 1.
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "USDJPY") }, NewLong("GBPUSD"));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_ManualPositions_AreIgnored()
    {
        PositionInfo manual = RiskData.Position(3, "GBPUSD", Side.Buy, 100000.0, 1.1, 1.098, RiskData.Now, "manual");

        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), manual }, NewLong("AUDUSD"));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_AnyCurrencyAlreadyAboveTheCap_BlocksEvenAnUnrelatedTrade()
    {
        // Literal reading of "for each currency ... including the new trade": three short-USD bot trades
        // already exist (EURUSD, GBPUSD, AUDUSD), so the account breaks the rule whatever the new trade is.
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "GBPUSD"), Long(3, "AUDUSD") }, NewShort("EURJPY"));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_SeveralCurrenciesAboveTheCap_ReportsTheFirstInOrdinalOrder()
    {
        // Long EUR 3 and short USD 3 at once: "EUR long" sorts before "USD short".
        RiskCheckResult result = Check(new[] { Long(1, "EURUSD"), Long(2, "EURUSD") }, NewLong("EURUSD"));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("EUR long", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_LowerCaseCurrencyCodes_CountAsTheSameCurrency()
    {
        // The broker might report "usd" for one symbol and "USD" for another; both are the same currency.
        SymbolInfo audLower = RiskData.Major("AUDUSD", "aud", "usd");
        RiskSnapshot snapshot = RiskData.Snapshot(
            RiskData.Account(100000.0),
            new[] { Long(1, "EURUSD"), Long(2, "GBPUSD") },
            symbols: new[] { RiskData.EurUsd, RiskData.GbpUsd, audLower, RiskData.UsdJpy });

        RiskCheckResult result = RiskData.Engine.CheckNewTrade(snapshot, NewLong("AUDUSD"));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_HigherCap_AllowsMore()
    {
        RiskParameters three = RiskData.Parameters with { MaxPositionsPerCurrency = 3 };
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), new[] { Long(1, "EURUSD"), Long(2, "GBPUSD") }, symbols: Symbols);

        Assert.True(new RiskEngine(three).CheckNewTrade(snapshot, NewLong("AUDUSD")).IsAllowed);
    }
}
