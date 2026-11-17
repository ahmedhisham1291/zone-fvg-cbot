using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-006 as changed on 2026-11-17: without any trade the count starts at the later of the
/// history start and 00:00 CE(S)T of RiskEpochDate; the time of the last alert is stored, and
/// after a restart no alert is raised within 3 days of it. CET is UTC+1 in winter.
/// </summary>
public class InactivityEpochTests
{
    private static readonly DateTime Now = new(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private static InactivityMonitor Create(out AlertSetup alerts)
    {
        alerts = OpsData.Alerts();
        return new InactivityMonitor(alerts.Service, alerts.Journal);
    }

    private static InactivityStatus CheckAt(InactivityMonitor monitor, DateTime now)
    {
        return monitor.Check(now, Array.Empty<ClosedTradeInfo>(), now.AddDays(-30.0), Array.Empty<PositionInfo>());
    }

    private static DateTime Date(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NoTradeAndARecentEpoch_CountsFromTheEpochStart()
    {
        InactivityMonitor monitor = Create(out AlertSetup alerts);
        monitor.CountNoEarlierThan(Date(2026, 3, 1));

        InactivityStatus first = CheckAt(monitor, Now);
        InactivityStatus before = CheckAt(monitor, new DateTime(2026, 3, 21, 22, 59, 0, DateTimeKind.Utc));
        InactivityStatus at = CheckAt(monitor, new DateTime(2026, 3, 21, 23, 0, 0, DateTimeKind.Utc));

        // 2026-03-01 starts at 2026-02-28 23:00 UTC; 21 days later is 2026-03-21 23:00 UTC.
        Assert.Equal(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), first.LastTradeUtc);
        Assert.False(first.LastTradeKnown);
        Assert.False(first.AlertRaised);
        Assert.False(before.AlertRaised);
        Assert.True(at.AlertRaised);
        Assert.Single(alerts.Sink.Of("inactivity"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NoTradeAndAnOldEpoch_CountsFromTheHistoryStartAsBefore()
    {
        InactivityMonitor monitor = Create(out AlertSetup alerts);
        monitor.CountNoEarlierThan(Date(2026, 1, 1));

        InactivityStatus status = CheckAt(monitor, Now);

        Assert.Equal(Now.AddDays(-30.0), status.LastTradeUtc);
        Assert.True(status.AlertRaised);
        Assert.Single(alerts.Sink.Of("inactivity"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_RestoredAlertWithinThreeDays_NoAlertUntilThreeDaysAfterIt()
    {
        DateTime last = Now.AddHours(-1.0);
        InactivityMonitor monitor = Create(out AlertSetup alerts);
        monitor.RestoreLastAlert(last);

        Assert.False(CheckAt(monitor, Now).AlertRaised);
        Assert.False(CheckAt(monitor, last.AddDays(3.0).AddTicks(-1)).AlertRaised);
        Assert.True(CheckAt(monitor, last.AddDays(3.0)).AlertRaised);
        Assert.Single(alerts.Sink.Of("inactivity"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_RestoredAlertThreeDaysAgo_AlertsAtOnce()
    {
        InactivityMonitor monitor = Create(out _);
        monitor.RestoreLastAlert(Now.AddDays(-3.0));

        Assert.True(CheckAt(monitor, Now).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void RestoreLastAlert_NearDateTimeMaxValue_DoesNotThrowAndAlertsNoMore()
    {
        InactivityMonitor monitor = Create(out _);

        monitor.RestoreLastAlert(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        monitor.RestoreLastAlert(DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-2.0), DateTimeKind.Utc));

        Assert.False(CheckAt(monitor, Now).AlertRaised);
    }

    // ------------------------------------------------------------------ BotCore

    private static readonly DateTime A = RecoveryData.Activation;

    private static int InactivityLines(BotHarness h)
    {
        return h.Journal.Lines("inactivity").Count;
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Restart_WithinThreeDaysOfTheStoredAlert_NoAlert()
    {
        var h = new BotHarness();
        h.Start(A);
        DateTime alerted = A.AddMinutes(1.0);
        h.Timer(alerted);
        Assert.Equal(1, InactivityLines(h));
        Assert.Equal(alerted.ToString("o", System.Globalization.CultureInfo.InvariantCulture), h.Store.Values[BotCore.InactivityAlertKey]);

        h.Start(alerted.AddHours(1.0));
        h.Timer(alerted.AddHours(1.0));
        Assert.Equal(1, InactivityLines(h));

        h.Start(alerted.AddDays(3.0));
        h.Timer(alerted.AddDays(3.0));
        Assert.Equal(2, InactivityLines(h));
        Assert.Equal(2, h.Alerts(AlertKind.Inactivity));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Restart_WithAnEmptyStore_AlertsAtOnce()
    {
        var h = new BotHarness();
        h.Start(A);

        h.Timer(A.AddMinutes(1.0));

        Assert.Equal(1, InactivityLines(h));
        Assert.Equal(0, h.Journal.Count("ops", "stored-inactivity-alert-ignored"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Restart_WithAnUnreadableStoredValue_AlertsAtOnceAndJournalsItOnce()
    {
        var h = new BotHarness();
        h.Store.Values[BotCore.InactivityAlertKey] = "not a time";
        h.Start(A);

        h.Timer(A.AddMinutes(1.0));
        h.Timer(A.AddMinutes(2.0));

        Assert.Equal(1, InactivityLines(h));
        Assert.Equal(1, h.Journal.Count("ops", "event=stored-inactivity-alert-ignored value=not\\sa\\stime reason=unreadable"));
    }

    [Theory]
    [Trait("Req", "OPS-006")]
    [InlineData("2026-03-05T08:00:00.0000000Z")]
    [InlineData("2026-03-04T08:01:00.0000001Z")]
    [InlineData("9999-12-31T23:59:59.9999999Z")]
    public void Restart_WithAStoredTimeLaterThanTheServerTime_TreatedAsUnreadable(string stored)
    {
        var h = new BotHarness();
        h.Store.Values[BotCore.InactivityAlertKey] = stored;
        h.Start(A);

        h.Timer(A.AddMinutes(1.0));
        h.Timer(A.AddMinutes(2.0));

        Assert.Equal(1, InactivityLines(h));
        Assert.Equal(1, h.Journal.Count("ops", "event=stored-inactivity-alert-ignored value=" + stored + " reason=unreadable"));
        Assert.Empty(h.Journal.Lines("error"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Restart_WithAStoredTimeEqualToTheServerTime_NoAlert()
    {
        var h = new BotHarness();
        h.Store.Values[BotCore.InactivityAlertKey] = A.AddMinutes(1.0).ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        h.Start(A);

        h.Timer(A.AddMinutes(1.0));

        Assert.Equal(0, InactivityLines(h));
        Assert.Equal(0, h.Journal.Count("ops", "stored-inactivity-alert-ignored"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Backtest_StartedOnTheEpochWithoutTrades_NoAlertBeforeTwentyOneDays()
    {
        var h = new BotHarness();
        BotParameters parameters = RecoveryData.Parameters() with { Risk = RiskData.Parameters with { RiskEpochDate = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc) } };
        h.Start(A, parameters);

        h.Timer(A.AddMinutes(1.0));
        h.Timer(new DateTime(2026, 3, 24, 22, 59, 0, DateTimeKind.Utc));
        Assert.Equal(0, InactivityLines(h));

        // 2026-03-04 starts at 2026-03-03 23:00 UTC; 21 days later is 2026-03-24 23:00 UTC.
        h.Timer(new DateTime(2026, 3, 24, 23, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, InactivityLines(h));
    }
}
