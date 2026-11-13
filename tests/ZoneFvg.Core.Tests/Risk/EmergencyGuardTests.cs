using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-009: CloseAllAndHalt if equity &lt;= M - (FtmoDailyLossPercent - FtmoGuardBufferPercent)%
/// of InitialCapital, or equity &lt;= InitialCapital x (1 - (FtmoMaxLossPercent -
/// FtmoGuardBufferPercent) / 100). With defaults: M - 4,000 and 91,000 on 100,000.
/// </summary>
public class EmergencyGuardTests
{
    private static ClosedTradeInfo RealisedToday(double net, double balanceAfter)
    {
        return RiskData.Closed(50, "EURUSD", RiskData.Now.AddHours(-5.0), RiskData.Now.AddHours(-4.0), net, balanceAfter, "manual");
    }

    private static EmergencyDecision Guard(double balance, double equity, double? realisedToday = null)
    {
        ClosedTradeInfo[] history = realisedToday.HasValue ? new[] { RealisedToday(realisedToday.Value, balance) } : Array.Empty<ClosedTradeInfo>();
        return RiskData.Engine.EmergencyGuard(RiskData.Snapshot(RiskData.Account(balance, equity, 0.0), history: history));
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_OneCentAboveTheDailyFloor_DoesNothing()
    {
        // M = 100,000: daily floor 100,000 - 4% x 100,000 = 96,000. Equity 96,000.01 is above it.
        EmergencyDecision decision = Guard(100000.0, 96000.01);

        Assert.Equal(EmergencyAction.None, decision.Action);
        Assert.False(decision.DailyFloorReached);
        Assert.False(decision.OverallFloorReached);
        Assert.Equal(96000.0, decision.DailyFloor, 1e-6);
        Assert.Equal(91000.0, decision.OverallFloor, 1e-6);
        Assert.Equal(96000.01, decision.Equity);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_ExactlyAtTheDailyFloor_ClosesAllAndHalts()
    {
        EmergencyDecision decision = Guard(100000.0, 96000.0);

        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
        Assert.True(decision.DailyFloorReached);
        Assert.False(decision.OverallFloorReached);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_At91000_ReachesBothFloors()
    {
        EmergencyDecision decision = Guard(100000.0, 91000.0);

        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
        Assert.True(decision.DailyFloorReached);
        Assert.True(decision.OverallFloorReached);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_OverallFloorAlone_ClosesAllAndHalts()
    {
        // A loss of 6,000 yesterday: M = 94,000, so the daily floor is 90,000 and the overall floor (91,000) is the
        // higher one. Equity 91,000.01 -> nothing; 91,000 -> overall floor only.
        ClosedTradeInfo yesterday = RiskData.Closed(50, "EURUSD", RiskData.DayStart.AddHours(-3.0), RiskData.DayStart.AddHours(-2.0), -6000.0, 94000.0, "manual");
        RiskSnapshot above = RiskData.Snapshot(RiskData.Account(94000.0, 91000.01, 0.0), history: new[] { yesterday });
        RiskSnapshot at = RiskData.Snapshot(RiskData.Account(94000.0, 91000.0, 0.0), history: new[] { yesterday });

        EmergencyDecision none = RiskData.Engine.EmergencyGuard(above);
        EmergencyDecision overall = RiskData.Engine.EmergencyGuard(at);

        Assert.Equal(EmergencyAction.None, none.Action);
        Assert.Equal(90000.0, none.DailyFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, overall.Action);
        Assert.False(overall.DailyFloorReached);
        Assert.True(overall.OverallFloorReached);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_DailyFloorMovesWithTheDayStartBalance()
    {
        // A profit of 2,000 yesterday: M = 102,000, daily floor 98,000. Equity 98,000.01 -> nothing; 98,000 -> act.
        ClosedTradeInfo yesterday = RiskData.Closed(50, "EURUSD", RiskData.DayStart.AddHours(-3.0), RiskData.DayStart.AddHours(-2.0), 2000.0, 102000.0, "manual");

        EmergencyDecision none = RiskData.Engine.EmergencyGuard(RiskData.Snapshot(RiskData.Account(102000.0, 98000.01, 0.0), history: new[] { yesterday }));
        EmergencyDecision act = RiskData.Engine.EmergencyGuard(RiskData.Snapshot(RiskData.Account(102000.0, 98000.0, 0.0), history: new[] { yesterday }));

        Assert.Equal(98000.0, none.DailyFloor, 1e-6);
        Assert.Equal(EmergencyAction.None, none.Action);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, act.Action);
        Assert.True(act.DailyFloorReached);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_LossRealisedToday_DoesNotMoveTheDailyFloor()
    {
        // A loss of 2,000 today: balance 98,000 but M is still 100,000, so the daily floor stays at 96,000.
        EmergencyDecision decision = Guard(98000.0, 96000.0, -2000.0);

        Assert.Equal(96000.0, decision.DailyFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_OtherBuffer_MovesBothFloors()
    {
        // Buffer 2%: daily floor M - 3% = 97,000; overall floor 100,000 x (1 - 8/100) = 92,000.
        RiskEngine engine = new(RiskData.Parameters with { FtmoGuardBufferPercent = 2.0 });

        EmergencyDecision decision = engine.EmergencyGuard(RiskData.Snapshot(RiskData.Account(100000.0, 97000.0, 0.0)));

        Assert.Equal(97000.0, decision.DailyFloor, 1e-6);
        Assert.Equal(92000.0, decision.OverallFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_HistoryIncomplete_Throws()
    {
        RiskSnapshot snapshot = RiskData.SnapshotAt(RiskData.Now, RiskData.Now, RiskData.Account(100000.0), Array.Empty<ClosedTradeInfo>());

        Assert.Throws<ArgumentException>(() => RiskData.Engine.EmergencyGuard(snapshot));
    }
}
