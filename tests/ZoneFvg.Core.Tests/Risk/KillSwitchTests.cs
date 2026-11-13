using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-008: Peak = highest end-of-trade balance since RiskEpochDate, from history, and not less
/// than the balance at RiskEpochDate. If (Peak - equity) / Peak &gt;= MaxDrawdownPercent (8%)
/// the decision is Halt. The balance at RiskEpochDate is rebuilt like the day start of RSK-007:
/// current balance minus the net result of the trades closed since the epoch.
/// </summary>
public class KillSwitchTests
{
    private static DateTime Day(int day, int hour)
    {
        return new DateTime(2026, 1, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter)
    {
        return RiskData.Closed(id, "EURUSD", closeUtc.AddHours(-1.0), closeUtc, net, balanceAfter);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_NoHistory_PeakIsTheCurrentBalance_799PercentDoesNotHalt()
    {
        // Peak 100,000; equity 92,010: (100,000 - 92,010) / 100,000 = 7.99% < 8%.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 92010.0, 90000.0));

        KillSwitchDecision decision = RiskData.Engine.KillSwitch(snapshot);

        Assert.False(decision.Halt);
        Assert.Equal(100000.0, decision.Peak);
        Assert.Equal(7990.0, decision.DrawdownMoney, 1e-6);
        Assert.Equal(8000.0, decision.LimitMoney, 1e-6);
        Assert.Equal(92010.0, decision.Equity);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_Exactly800Percent_Halts()
    {
        // Equity 92,000: 8,000 / 100,000 = 8.00%.
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 92000.0, 90000.0));

        KillSwitchDecision decision = RiskData.Engine.KillSwitch(snapshot);

        Assert.True(decision.Halt);
        Assert.Equal(8000.0, decision.DrawdownMoney, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_PeakFromHistory_IsTheHighestEndOfTradeBalance()
    {
        // Since the epoch: +3,000 -> 103,000, then -1,000 -> 102,000 (current balance). Balance at epoch 100,000.
        // Peak 103,000; limit 8% x 103,000 = 8,240. Equity 94,760: drawdown 8,240 -> halt. 94,760.01 -> no halt.
        // A higher balance of 110,000 before the epoch is ignored.
        ClosedTradeInfo[] history =
        {
            Closed(1, new DateTime(2025, 12, 20, 12, 0, 0, DateTimeKind.Utc), 10000.0, 110000.0),
            Closed(2, Day(5, 12), 3000.0, 103000.0),
            Closed(3, Day(8, 12), -1000.0, 102000.0),
        };
        RiskSnapshot atLimit = RiskData.Snapshot(RiskData.Account(102000.0, 94760.0, 90000.0), history: history);
        RiskSnapshot inside = RiskData.Snapshot(RiskData.Account(102000.0, 94760.01, 90000.0), history: history);

        KillSwitchDecision halted = RiskData.Engine.KillSwitch(atLimit);
        KillSwitchDecision running = RiskData.Engine.KillSwitch(inside);

        Assert.Equal(103000.0, halted.Peak);
        Assert.Equal(8240.0, halted.LimitMoney, 1e-6);
        Assert.True(halted.Halt);
        Assert.False(running.Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_OnlyLossesSinceEpoch_PeakIsTheBalanceAtTheEpoch()
    {
        // Two losses of 1,000 since the epoch: balance 98,000. Balance at epoch = 98,000 + 2,000 = 100,000,
        // above both end-of-trade balances (99,000 and 98,000). Equity 92,000 -> 8% of 100,000 -> halt.
        // (With Peak taken as 99,000 the drawdown would be 7.07% and the switch would wrongly stay off.)
        ClosedTradeInfo[] history = { Closed(1, Day(5, 12), -1000.0, 99000.0), Closed(2, Day(6, 12), -1000.0, 98000.0) };
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(98000.0, 92000.0, 90000.0), history: history);

        KillSwitchDecision decision = RiskData.Engine.KillSwitch(snapshot);

        Assert.Equal(100000.0, decision.Peak, 1e-9);
        Assert.True(decision.Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_TradeClosedExactlyAtTheEpoch_Counts()
    {
        // Epoch date 2026-01-01 = 00:00 CET = 2025-12-31 23:00 UTC. A trade closed at that instant with balance 105,000;
        // current balance 105,000. Peak 105,000; limit 8,400; equity 96,600 -> drawdown 8,400 -> halt.
        ClosedTradeInfo atEpoch = Closed(1, RiskData.EpochStart, 5000.0, 105000.0);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(105000.0, 96600.0, 90000.0), history: new[] { atEpoch });

        KillSwitchDecision decision = RiskData.Engine.KillSwitch(snapshot);

        Assert.Equal(105000.0, decision.Peak);
        Assert.True(decision.Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_AfterRestartWithARebuiltSnapshot_GivesTheSameDecision()
    {
        // The same broker data, returned in another order, read by a new engine instance.
        ClosedTradeInfo[] history = { Closed(1, Day(5, 12), 3000.0, 103000.0), Closed(2, Day(8, 12), -1000.0, 102000.0) };
        RiskSnapshot before = RiskData.Snapshot(RiskData.Account(102000.0, 94760.0, 90000.0), history: history);
        RiskSnapshot after = RiskData.Snapshot(RiskData.Account(102000.0, 94760.0, 90000.0), history: history.Reverse().ToArray());

        KillSwitchDecision first = RiskData.Engine.KillSwitch(before);
        KillSwitchDecision second = new RiskEngine(RiskData.Parameters).KillSwitch(after);

        Assert.True(first.Halt);
        Assert.Equal(first.Halt, second.Halt);
        Assert.Equal(first.Peak, second.Peak);
        Assert.Equal(first.DrawdownMoney, second.DrawdownMoney);
        Assert.Equal(first.LimitMoney, second.LimitMoney);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_NewEpochAfterTheDrawdown_ResetsThePeak()
    {
        // With the epoch moved past the 103,000 high (spec 4.2: resumes only with a new RiskEpochDate),
        // Peak = balance at the new epoch = 102,000; equity 94,760 is a 7.1% drawdown -> no halt.
        ClosedTradeInfo[] history = { Closed(1, Day(5, 12), 3000.0, 103000.0), Closed(2, Day(8, 12), -1000.0, 102000.0) };
        RiskEngine engine = new(RiskData.Parameters with { RiskEpochDate = Day(9, 0) });
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(102000.0, 94760.0, 90000.0), history: history);

        KillSwitchDecision decision = engine.KillSwitch(snapshot);

        Assert.Equal(102000.0, decision.Peak);
        Assert.False(decision.Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_PeakNotPositive_HaltsFailClosed()
    {
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(-10.0, -10.0, 0.0));

        Assert.True(RiskData.Engine.KillSwitch(snapshot).Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_HistoryNotReachingTheEpoch_Throws()
    {
        RiskSnapshot snapshot = RiskData.SnapshotAt(RiskData.Now, RiskData.Epoch.AddDays(1.0), RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());

        Assert.Throws<ArgumentException>(() => RiskData.Engine.KillSwitch(snapshot));
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_TradeClosedOneSecondBeforeMidnightCetOfTheEpochDate_DoesNotCount()
    {
        // Epoch date 2026-01-01 starts at 2025-12-31 23:00 UTC. A balance of 120,000 at 22:59:59 UTC is before it.
        // Peak = current balance 100,000; equity 92,010 -> 7.99%, no halt. (Counting it would give 23.3% and halt.)
        ClosedTradeInfo before = Closed(1, RiskData.EpochStart.AddSeconds(-1.0), 0.0, 120000.0);
        RiskSnapshot snapshot = RiskData.SnapshotAt(RiskData.Now, RiskData.EpochStart.AddDays(-1.0), RiskData.Account(100000.0, 92010.0, 90000.0), new[] { before });

        KillSwitchDecision decision = RiskData.Engine.KillSwitch(snapshot);

        Assert.Equal(100000.0, decision.Peak);
        Assert.False(decision.Halt);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_SummerEpochDate_StartsAtMidnightCest()
    {
        // Epoch date 2026-07-01 = 00:00 CEST = 2026-06-30 22:00 UTC. The trade closed at 21:59:59 UTC (balance 110,000)
        // is before it; the trade closed at 22:00:00 UTC (+5,000, balance 105,000) counts. Current balance 105,000.
        // Balance at epoch = 105,000 - 5,000 = 100,000; Peak = 105,000; limit 8,400. Equity 96,600 -> halt.
        DateTime epochStart = new(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc);
        RiskEngine engine = new(RiskData.Parameters with { RiskEpochDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) });
        ClosedTradeInfo[] history = { Closed(1, epochStart.AddSeconds(-1.0), 0.0, 110000.0), Closed(2, epochStart, 5000.0, 105000.0) };
        DateTime now = new(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

        KillSwitchDecision decision = engine.KillSwitch(RiskData.SnapshotAt(now, epochStart, RiskData.Account(105000.0, 96600.0, 90000.0), history));

        Assert.Equal(105000.0, decision.Peak);
        Assert.Equal(8400.0, decision.LimitMoney, 1e-6);
        Assert.True(decision.Halt);
        Assert.Throws<ArgumentException>(() => engine.KillSwitch(RiskData.SnapshotAt(now, epochStart.AddSeconds(1.0), RiskData.Account(105000.0, 96600.0, 90000.0), history)));
    }
}
