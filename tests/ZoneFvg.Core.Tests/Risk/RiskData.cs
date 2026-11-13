using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-written broker data for the risk tests. Every number is chosen so that the expected
/// results can be worked out on paper.
/// </summary>
/// <remarks>
/// Pip values: EURUSD, GBPUSD and AUDUSD have pip size 0.0001 and a pip value of 0.0001 USD per
/// unit (a USD-quoted pair on a USD account). USDJPY has pip size 0.01 and a pip value of
/// 0.00008 USD per unit, which is 0.01 / 125.00 at a USDJPY price of 125.00.
/// </remarks>
public static class RiskData
{
    /// <summary>A Wednesday in winter: 2026-01-14 10:00 UTC is 11:00 CET.</summary>
    public static readonly DateTime Now = new(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Start of the CE(S)T day containing <see cref="Now"/>: 2026-01-14 00:00 CET.</summary>
    public static readonly DateTime DayStart = new(2026, 1, 13, 23, 0, 0, DateTimeKind.Utc);

    /// <summary>The risk epoch used by default in these tests.</summary>
    public static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static RiskParameters Parameters => new() { RiskEpochDate = Epoch };

    public static SymbolInfo EurUsd => Major("EURUSD", "EUR", "USD");

    public static SymbolInfo GbpUsd => Major("GBPUSD", "GBP", "USD");

    public static SymbolInfo AudUsd => Major("AUDUSD", "AUD", "USD");

    public static SymbolInfo UsdJpy => new("USDJPY", "USD", "JPY", 0.01, 0.00008, 1000.0, 1000.0, 10000000.0, 0.01, true, 0.0);

    public static IReadOnlyList<SymbolInfo> AllSymbols => new[] { EurUsd, GbpUsd, AudUsd, UsdJpy };

    public static SymbolInfo Major(string name, string baseCurrency, string quoteCurrency)
    {
        return new SymbolInfo(name, baseCurrency, quoteCurrency, 0.0001, 0.0001, 1000.0, 1000.0, 10000000.0, 0.00001, true, 0.0);
    }

    public static AccountSnapshot Account(double balance)
    {
        return new AccountSnapshot(balance, balance, balance, "USD");
    }

    public static AccountSnapshot Account(double balance, double equity, double freeMargin)
    {
        return new AccountSnapshot(balance, equity, freeMargin, "USD");
    }

    public static string BotLabel(string symbol)
    {
        return "ZFVG|v1|" + symbol + "|D-20260113-0800";
    }

    public static PositionInfo Position(long id, string symbol, Side side, double volume, double entry, double? stop)
    {
        return Position(id, symbol, side, volume, entry, stop, Now.AddHours(-1.0), BotLabel(symbol));
    }

    public static PositionInfo Position(long id, string symbol, Side side, double volume, double entry, double? stop, DateTime openTimeUtc, string label)
    {
        return new PositionInfo(id, symbol, side, volume, entry, stop, null, label, "e=;s=;t=", openTimeUtc);
    }

    public static PendingOrderInfo Order(long id, string symbol, Side side, double volume, double entry, double? stop)
    {
        return Order(id, symbol, side, volume, entry, stop, Now.AddMinutes(-30.0), BotLabel(symbol));
    }

    public static PendingOrderInfo Order(long id, string symbol, Side side, double volume, double entry, double? stop, DateTime createdTimeUtc, string label)
    {
        return new PendingOrderInfo(id, symbol, side, volume, entry, stop, null, label, "e=;s=;t=", createdTimeUtc);
    }

    public static ClosedTradeInfo Closed(long positionId, string symbol, DateTime entryTimeUtc, DateTime closeTimeUtc, double netProfit, double balanceAfterClose)
    {
        return Closed(positionId, symbol, entryTimeUtc, closeTimeUtc, netProfit, balanceAfterClose, BotLabel(symbol));
    }

    public static ClosedTradeInfo Closed(long positionId, string symbol, DateTime entryTimeUtc, DateTime closeTimeUtc, double netProfit, double balanceAfterClose, string label)
    {
        return new ClosedTradeInfo(positionId, symbol, label, entryTimeUtc, closeTimeUtc, netProfit, 0.0, 0.0, balanceAfterClose);
    }

    /// <summary>A snapshot at <see cref="Now"/> with history since the epoch and all four symbols.</summary>
    public static RiskSnapshot Snapshot(
        AccountSnapshot account,
        IEnumerable<PositionInfo>? positions = null,
        IEnumerable<PendingOrderInfo>? orders = null,
        IEnumerable<ClosedTradeInfo>? history = null,
        IEnumerable<SymbolInfo>? symbols = null)
    {
        return new RiskSnapshot(
            Now,
            account,
            positions ?? Array.Empty<PositionInfo>(),
            orders ?? Array.Empty<PendingOrderInfo>(),
            history ?? Array.Empty<ClosedTradeInfo>(),
            Epoch,
            symbols ?? AllSymbols);
    }

    /// <summary>A snapshot at an explicit server time with history since <paramref name="historySinceUtc"/>.</summary>
    public static RiskSnapshot SnapshotAt(DateTime serverTimeUtc, DateTime historySinceUtc, AccountSnapshot account, IEnumerable<ClosedTradeInfo> history)
    {
        return new RiskSnapshot(
            serverTimeUtc,
            account,
            Array.Empty<PositionInfo>(),
            Array.Empty<PendingOrderInfo>(),
            history,
            historySinceUtc,
            AllSymbols);
    }
}
