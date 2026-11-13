using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-007: the balance at 00:00 CE(S)T = current balance minus the net result (profit,
/// commission, swap) of all account trades closed since that moment, from broker history.
/// "Since that moment" includes a trade closed exactly at 00:00 CE(S)T.
/// </summary>
public class DayStartBalanceTests
{
    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo ClosedAt(long id, DateTime closeUtc, double net, string label)
    {
        return RiskData.Closed(id, "EURUSD", closeUtc.AddHours(-1.0), closeUtc, net, 100000.0, label);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_WinterMidnightBoundary_CountsOnlyTradesClosedSinceMidnightCet()
    {
        // Server time 2026-01-15 00:30 UTC = 01:30 CET; the day began 2026-01-14 23:00 UTC.
        // 22:59:59 UTC (+200) belongs to the previous day; 23:00:00 (-300) and 00:10 (+50, a manual trade) count.
        // M = 100,000 - (-300 + 50) = 100,250.
        DateTime now = Utc(2026, 1, 15, 0, 30, 0);
        ClosedTradeInfo[] history =
        {
            ClosedAt(1, Utc(2026, 1, 14, 22, 59, 59), 200.0, RiskData.BotLabel("EURUSD")),
            ClosedAt(2, Utc(2026, 1, 14, 23, 0, 0), -300.0, RiskData.BotLabel("EURUSD")),
            ClosedAt(3, Utc(2026, 1, 15, 0, 10, 0), 50.0, "manual"),
        };
        RiskSnapshot snapshot = RiskData.SnapshotAt(now, RiskData.EpochStart, RiskData.Account(100000.0), history);

        Assert.Equal(100250.0, RiskData.Engine.DayStartBalance(snapshot), 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_SummerMidnightBoundary_CountsOnlyTradesClosedSinceMidnightCest()
    {
        // Server time 2026-07-15 00:30 UTC = 02:30 CEST; the day began 2026-07-14 22:00 UTC.
        // 21:59:59 UTC (+200) is the previous day; 22:00:00 (-300) and 22:30 (+50) count. M = 100,250.
        DateTime now = Utc(2026, 7, 15, 0, 30, 0);
        ClosedTradeInfo[] history =
        {
            ClosedAt(1, Utc(2026, 7, 14, 21, 59, 59), 200.0, RiskData.BotLabel("EURUSD")),
            ClosedAt(2, Utc(2026, 7, 14, 22, 0, 0), -300.0, RiskData.BotLabel("EURUSD")),
            ClosedAt(3, Utc(2026, 7, 14, 22, 30, 0), 50.0, "manual"),
        };
        RiskSnapshot snapshot = RiskData.SnapshotAt(now, RiskData.EpochStart, RiskData.Account(100000.0), history);

        Assert.Equal(100250.0, RiskData.Engine.DayStartBalance(snapshot), 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_OneSecondBeforeSummerMidnight_StillUsesThePreviousDayStart()
    {
        // Server time 2026-07-14 21:59:59 UTC = 23:59:59 CEST; the day began 2026-07-13 22:00 UTC.
        // Closed 2026-07-13 21:59:59 (-100) is the day before; closed 2026-07-14 21:00 (+200) counts. M = 100,000 - 200.
        DateTime now = Utc(2026, 7, 14, 21, 59, 59);
        ClosedTradeInfo[] history =
        {
            ClosedAt(1, Utc(2026, 7, 13, 21, 59, 59), -100.0, RiskData.BotLabel("EURUSD")),
            ClosedAt(2, Utc(2026, 7, 14, 21, 0, 0), 200.0, RiskData.BotLabel("EURUSD")),
        };
        RiskSnapshot snapshot = RiskData.SnapshotAt(now, RiskData.EpochStart, RiskData.Account(100000.0), history);

        Assert.Equal(99800.0, RiskData.Engine.DayStartBalance(snapshot), 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_UsesNetProfitWhichAlreadyContainsCommissionAndSwap()
    {
        // Net -310 = gross -300, commission -7, swap -3. The broker booked -310 to the balance, so M = 99,690 + 310 = 100,000.
        var trade = new ClosedTradeInfo(1, "EURUSD", RiskData.BotLabel("EURUSD"), RiskData.Now.AddHours(-2.0), RiskData.Now.AddHours(-1.0), -310.0, -7.0, -3.0, 99690.0);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99690.0), history: new[] { trade });

        Assert.Equal(100000.0, RiskData.Engine.DayStartBalance(snapshot), 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_NoTradesToday_IsTheCurrentBalance()
    {
        // A trade closed yesterday (before 2026-01-13 23:00 UTC) does not count.
        ClosedTradeInfo yesterday = ClosedAt(1, RiskData.DayStart.AddSeconds(-1.0), -500.0, RiskData.BotLabel("EURUSD"));
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(99500.0), history: new[] { yesterday });

        Assert.Equal(99500.0, RiskData.Engine.DayStartBalance(snapshot));
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_RebuiltSnapshotWithHistoryInAnotherOrder_GivesTheSameValueExactly()
    {
        // Nets 0.1, 0.2 and 0.3 sum to different doubles in different orders; the snapshot's fixed order removes that.
        ClosedTradeInfo a = ClosedAt(1, RiskData.Now.AddHours(-3.0), 0.1, "manual");
        ClosedTradeInfo b = ClosedAt(2, RiskData.Now.AddHours(-2.0), 0.2, "manual");
        ClosedTradeInfo c = ClosedAt(3, RiskData.Now.AddHours(-1.0), 0.3, "manual");

        double first = RiskData.Engine.DayStartBalance(RiskData.Snapshot(RiskData.Account(100000.6), history: new[] { a, b, c }));
        double second = new RiskEngine(RiskData.Parameters).DayStartBalance(RiskData.Snapshot(RiskData.Account(100000.6), history: new[] { c, a, b }));

        Assert.Equal(first, second);
        Assert.Equal(100000.0, first, 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_HistoryNotReachingBackToTheEarlierOfEpochAndDayStart_Throws()
    {
        // The epoch (2026-01-01 00:00 CET = 2025-12-31 23:00 UTC) is earlier than the day start, so history must reach back to it.
        RiskSnapshot late = RiskData.SnapshotAt(RiskData.Now, RiskData.EpochStart.AddSeconds(1.0), RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());

        Assert.Throws<ArgumentException>(() => RiskData.Engine.DayStartBalance(late));
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_EpochAfterDayStart_NeedsHistoryOnlySinceTheDayStart()
    {
        // Epoch date 2026-01-15 starts at 2026-01-14 23:00 UTC, after the current day start (2026-01-13 23:00 UTC).
        RiskEngine engine = new(RiskData.Parameters with { RiskEpochDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc) });
        RiskSnapshot sinceDayStart = RiskData.SnapshotAt(RiskData.Now, RiskData.DayStart, RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());
        RiskSnapshot oneSecondLate = RiskData.SnapshotAt(RiskData.Now, RiskData.DayStart.AddSeconds(1.0), RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());

        Assert.Equal(100000.0, engine.DayStartBalance(sinceDayStart));
        Assert.Throws<ArgumentException>(() => engine.DayStartBalance(oneSecondLate));
    }

    [Fact]
    [Trait("Req", "RSK-007")]
    public void DayStartBalance_NullSnapshot_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RiskData.Engine.DayStartBalance(null!));
    }
}
