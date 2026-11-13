using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-010, entry counts: entries per symbol per CE(S)T day &lt;= 2 and per account per day
/// &lt;= 6, counted from broker history and open orders, including the new trade. An entry is a
/// bot position (open, or closed in history) whose entry time is in the current CE(S)T day, or a
/// bot pending order created in it. Partial closes of one position count once.
/// </summary>
/// <remarks>
/// The current day began 2026-01-13 23:00 UTC (<see cref="RiskData.DayStart"/>). Closed trades
/// here have a net result of 0, so the daily budget is untouched.
/// </remarks>
public class EntryLimitTests
{
    private static ClosedTradeInfo EnteredToday(long positionId, string symbol)
    {
        return RiskData.Closed(positionId, symbol, RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 0.0, 100000.0);
    }

    private static PlannedTrade NewLong(string symbol)
    {
        if (symbol == "USDJPY")
        {
            return RiskData.Trade(symbol, Side.Buy, 125.0, 124.8, 10000.0);
        }

        return RiskData.Trade(symbol, Side.Buy, 1.1, 1.098, 10000.0);
    }

    private static RiskCheckResult Check(PlannedTrade trade, IEnumerable<ClosedTradeInfo>? history = null, IEnumerable<PositionInfo>? positions = null, IEnumerable<PendingOrderInfo>? orders = null)
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0), positions, orders, history);
        return RiskData.Engine.CheckNewTrade(snapshot, trade);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_SecondEntryOnASymbol_IsAllowed()
    {
        // One EURUSD entry today (closed) + the new one = 2.
        Assert.True(Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD") }).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_ThirdEntryOnASymbol_IsBlocked()
    {
        // EURUSD today: one closed trade and one pending order. The new one would be the third.
        PendingOrderInfo order = RiskData.Order(20, "EURUSD", Side.Buy, 10000.0, 1.1, 1.098);

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD") }, orders: new[] { order });

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.SymbolEntryLimitReached, result.Reason);
        Assert.Equal(3.0, result.Measured);
        Assert.Equal(2.0, result.Limit);
        Assert.Equal("EURUSD", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_OpenPositionEnteredToday_Counts()
    {
        PositionInfo open = RiskData.Position(30, "EURUSD", Side.Sell, 10000.0, 1.1, 1.102);

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD") }, new[] { open });

        Assert.Equal(RiskReason.SymbolEntryLimitReached, result.Reason);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_EntriesBeforeTheDayStart_DoNotCount()
    {
        // Entered one second before 00:00 CET (closed today), and a pending order and a position from yesterday.
        DateTime yesterday = RiskData.DayStart.AddSeconds(-1.0);
        ClosedTradeInfo lateYesterday = RiskData.Closed(2, "EURUSD", yesterday, RiskData.Now.AddHours(-2.0), 0.0, 100000.0);
        PendingOrderInfo oldOrder = RiskData.Order(20, "EURUSD", Side.Buy, 10000.0, 1.1, 1.098, yesterday, RiskData.BotLabel("EURUSD"));
        PositionInfo oldPosition = RiskData.Position(30, "EURUSD", Side.Sell, 10000.0, 1.1, 1.102, yesterday, RiskData.BotLabel("EURUSD"));

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD"), lateYesterday }, new[] { oldPosition }, new[] { oldOrder });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_EntryExactlyAtTheDayStart_Counts()
    {
        ClosedTradeInfo atMidnight = RiskData.Closed(2, "EURUSD", RiskData.DayStart, RiskData.Now.AddHours(-2.0), 0.0, 100000.0);

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD"), atMidnight });

        Assert.Equal(RiskReason.SymbolEntryLimitReached, result.Reason);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_PartialClosesAndTheRemainingPosition_CountAsOneEntry()
    {
        // Position 1 was partly closed twice and is still open: one entry. The new trade is the second.
        ClosedTradeInfo part1 = RiskData.Closed(1, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 0.0, 100000.0);
        ClosedTradeInfo part2 = RiskData.Closed(1, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-1.5), 0.0, 100000.0);
        PositionInfo rest = RiskData.Position(1, "EURUSD", Side.Buy, 10000.0, 1.1, 1.098, RiskData.Now.AddHours(-3.0), RiskData.BotLabel("EURUSD"));

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { part1, part2 }, new[] { rest });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_ManualEntries_DoNotCount()
    {
        ClosedTradeInfo manual = RiskData.Closed(2, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 0.0, 100000.0, "manual");
        PendingOrderInfo manualOrder = RiskData.Order(20, "EURUSD", Side.Buy, 10000.0, 1.1, 1.098, RiskData.Now, "manual");

        RiskCheckResult result = Check(NewLong("EURUSD"), new[] { EnteredToday(1, "EURUSD"), manual }, orders: new[] { manualOrder });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_SixthEntryOnTheAccount_IsAllowed()
    {
        // Five entries today on four symbols (EURUSD 2, GBPUSD 1, AUDUSD 1, USDJPY 1); the new USDJPY is the sixth.
        ClosedTradeInfo[] history =
        {
            EnteredToday(1, "EURUSD"), EnteredToday(2, "EURUSD"), EnteredToday(3, "GBPUSD"),
            EnteredToday(4, "AUDUSD"), EnteredToday(5, "USDJPY"),
        };

        Assert.True(Check(NewLong("USDJPY"), history).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_SeventhEntryOnTheAccount_IsBlocked()
    {
        // Six entries today (EURUSD 2, GBPUSD 2, AUDUSD 1, USDJPY 1); the new USDJPY would be the seventh
        // on the account but only the second on USDJPY.
        ClosedTradeInfo[] history =
        {
            EnteredToday(1, "EURUSD"), EnteredToday(2, "EURUSD"), EnteredToday(3, "GBPUSD"),
            EnteredToday(4, "GBPUSD"), EnteredToday(5, "AUDUSD"), EnteredToday(6, "USDJPY"),
        };

        RiskCheckResult result = Check(NewLong("USDJPY"), history);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.AccountEntryLimitReached, result.Reason);
        Assert.Equal(7.0, result.Measured);
        Assert.Equal(6.0, result.Limit);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckNewTrade_PendingOrdersCountTowardTheAccountLimit()
    {
        // Four closed entries plus two pending orders created today = 6; the new one would be the seventh.
        // The new trade sells USDJPY so that USD stays within the currency cap (long USD 2 from the two sells).
        ClosedTradeInfo[] history = { EnteredToday(1, "EURUSD"), EnteredToday(2, "EURUSD"), EnteredToday(3, "GBPUSD"), EnteredToday(4, "AUDUSD") };
        PendingOrderInfo[] orders =
        {
            RiskData.Order(20, "GBPUSD", Side.Sell, 10000.0, 1.1, 1.102),
            RiskData.Order(21, "AUDUSD", Side.Sell, 10000.0, 1.1, 1.102),
        };

        RiskCheckResult result = Check(RiskData.Trade("USDJPY", Side.Sell, 125.0, 125.2, 10000.0), history, orders: orders);

        Assert.Equal(RiskReason.AccountEntryLimitReached, result.Reason);
    }
}
