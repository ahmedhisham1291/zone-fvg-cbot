using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-006: an alert when 21 days have passed since the account's last opened trade, repeated
/// every 3 days, reset by a new trade. The last trade opened at <see cref="Opened"/>.
/// </summary>
public class InactivityMonitorTests
{
    /// <summary>The last trade opened at 2026-01-14 10:00 UTC.</summary>
    private static readonly DateTime Opened = OpsData.T0;

    private static readonly DateTime HistorySince = OpsData.T0.AddDays(-60.0);

    private static ClosedTradeInfo Trade(DateTime entryUtc, string label = "ZFVG|v1|EURUSD|D-20260114-0400")
    {
        return new ClosedTradeInfo(1001, "EURUSD", label, entryUtc, entryUtc.AddHours(2.0), 100.0, -7.0, 0.0, 100100.0);
    }

    private static (InactivityMonitor Monitor, AlertSetup Alerts) Create()
    {
        AlertSetup alerts = OpsData.Alerts();
        return (new InactivityMonitor(alerts.Service, alerts.Journal), alerts);
    }

    private static InactivityStatus CheckAt(InactivityMonitor monitor, DateTime now, params ClosedTradeInfo[] history)
    {
        return monitor.Check(now, history, HistorySince, Array.Empty<PositionInfo>());
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_TwentyDays_NoAlert()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();

        InactivityStatus status = CheckAt(monitor, Opened.AddDays(20.0), Trade(Opened));

        Assert.False(status.AlertRaised);
        Assert.Equal(TimeSpan.FromDays(20.0), status.Inactive);
        Assert.True(status.LastTradeKnown);
        Assert.Equal(0, alerts.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_OneSecondBeforeTwentyOneDays_NoAlertButExactlyTwentyOneDaysAlerts()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();

        Assert.False(CheckAt(monitor, Opened.AddDays(21.0).AddSeconds(-1.0), Trade(Opened)).AlertRaised);
        Assert.True(CheckAt(monitor, Opened.AddDays(21.0), Trade(Opened)).AlertRaised);

        Assert.Equal(
            new[] { "ZFVG EURUSD Inactivity: No trade opened for 21.0 days (last opened 2026-01-14T10:00:00Z). FTMO may close an account after 30 days without a trade." },
            alerts.Service.PendingTexts);
        Assert.Equal(
            new[] { "2026-02-04T10:00:00Z inactivity days=21.0 last=2026-01-14T10:00:00Z known=true step=0" },
            alerts.Sink.Of("inactivity"));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_AfterFirstAlert_RepeatsEveryThreeDaysOnly()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();
        var raisedAt = new List<double>();
        for (int hour = 20 * 24; hour <= 31 * 24; hour++)
        {
            if (CheckAt(monitor, Opened.AddHours(hour), Trade(Opened)).AlertRaised)
            {
                raisedAt.Add(hour / 24.0);
            }
        }

        Assert.Equal(new[] { 21.0, 24.0, 27.0, 30.0 }, raisedAt);
        Assert.False(CheckAt(monitor, Opened.AddDays(33.0).AddSeconds(-1.0), Trade(Opened)).AlertRaised);
        Assert.True(CheckAt(monitor, Opened.AddDays(33.0), Trade(Opened)).AlertRaised);
        Assert.Equal(5, alerts.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NewTrade_ResetsTheCount()
    {
        (InactivityMonitor monitor, _) = Create();
        Assert.True(CheckAt(monitor, Opened.AddDays(21.0), Trade(Opened)).AlertRaised);
        DateTime newer = Opened.AddDays(22.0);

        Assert.False(CheckAt(monitor, Opened.AddDays(24.0), Trade(Opened), Trade(newer)).AlertRaised);
        Assert.False(CheckAt(monitor, newer.AddDays(21.0).AddSeconds(-1.0), Trade(Opened), Trade(newer)).AlertRaised);
        Assert.True(CheckAt(monitor, newer.AddDays(21.0), Trade(Opened), Trade(newer)).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_OpenPositionNewerThanHistory_CountsAsLastTrade()
    {
        (InactivityMonitor monitor, _) = Create();
        DateTime positionOpened = Opened.AddDays(5.0);
        var position = new PositionInfo(2001, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.29, 1.32, "manual", "manual", positionOpened);

        InactivityStatus status = monitor.Check(Opened.AddDays(21.0), new[] { Trade(Opened) }, HistorySince, new[] { position });

        Assert.Equal(positionOpened, status.LastTradeUtc);
        Assert.False(status.AlertRaised);
        Assert.True(monitor.Check(positionOpened.AddDays(21.0), new[] { Trade(Opened) }, HistorySince, new[] { position }).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_TradeOfAnyLabel_Counts()
    {
        (InactivityMonitor monitor, _) = Create();

        InactivityStatus status = CheckAt(monitor, Opened.AddDays(21.0), Trade(Opened.AddDays(-10.0)), Trade(Opened, "manual"));

        Assert.Equal(Opened, status.LastTradeUtc);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_StartedLateInTheInactivity_RaisesOnceForTheCurrentStep()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();

        Assert.True(CheckAt(monitor, Opened.AddDays(25.0), Trade(Opened)).AlertRaised);
        Assert.False(CheckAt(monitor, Opened.AddDays(26.0), Trade(Opened)).AlertRaised);
        Assert.True(CheckAt(monitor, Opened.AddDays(27.0), Trade(Opened)).AlertRaised);
        Assert.Equal(2, alerts.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NoTradeAtAll_UsesHistoryStartAndWarns()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();

        InactivityStatus status = CheckAt(monitor, OpsData.T0);

        Assert.True(status.AlertRaised);
        Assert.False(status.LastTradeKnown);
        Assert.Equal(HistorySince, status.LastTradeUtc);
        Assert.Equal(
            "ZFVG EURUSD Inactivity: No trade found since 2025-11-15T10:00:00Z (60.0 days). FTMO may close an account after 30 days without a trade.",
            alerts.Service.PendingTexts.Single());
    }

    /// <summary>The way BotCore calls the check: the history covers the last 30 days before now.</summary>
    private static InactivityStatus CheckMovingWindow(InactivityMonitor monitor, DateTime now, params ClosedTradeInfo[] trades)
    {
        DateTime since = now.AddDays(-30.0);
        ClosedTradeInfo[] visible = trades.Where(t => t.CloseTimeUtc >= since).ToArray();
        return monitor.Check(now, visible, since, Array.Empty<PositionInfo>());
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NoHistoryCalledEveryMinuteForThreeDays_AlertsOncePerStep()
    {
        // Backtest log of 2026-11-17: with no trade and a history start that moves with the clock,
        // the alert and the journal line were repeated every minute.
        (InactivityMonitor monitor, AlertSetup alerts) = Create();
        var raisedAtMinute = new List<int>();
        for (int minute = 0; minute <= 3 * 24 * 60; minute++)
        {
            InactivityStatus status = CheckMovingWindow(monitor, OpsData.T0.AddMinutes(minute));
            Assert.Equal(OpsData.T0.AddDays(-30.0), status.LastTradeUtc);
            Assert.False(status.LastTradeKnown);
            if (status.AlertRaised)
            {
                raisedAtMinute.Add(minute);
            }
        }

        Assert.Equal(new[] { 0, 3 * 24 * 60 }, raisedAtMinute);
        Assert.Equal(
            new[]
            {
                "2026-01-14T10:00:00Z inactivity days=30.0 last=2025-12-15T10:00:00Z known=false step=3",
                "2026-01-17T10:00:00Z inactivity days=33.0 last=2025-12-15T10:00:00Z known=false step=4",
            },
            alerts.Sink.Of("inactivity"));
        Assert.Equal(2, alerts.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NoHistoryOneMinuteBeforeRepeat_NoSecondAlert()
    {
        (InactivityMonitor monitor, _) = Create();
        Assert.True(CheckMovingWindow(monitor, OpsData.T0).AlertRaised);

        Assert.False(CheckMovingWindow(monitor, OpsData.T0.AddDays(3.0).AddSeconds(-1.0)).AlertRaised);
        Assert.True(CheckMovingWindow(monitor, OpsData.T0.AddDays(3.0)).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_LastTradeDropsOutOfTheHistoryWindow_DoesNotRearm()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();
        ClosedTradeInfo trade = Trade(Opened);
        var raisedAt = new List<double>();
        for (int hour = 20 * 24; hour <= 34 * 24; hour++)
        {
            InactivityStatus status = CheckMovingWindow(monitor, Opened.AddHours(hour), trade);
            Assert.Equal(Opened, status.LastTradeUtc);
            Assert.True(status.LastTradeKnown);
            if (status.AlertRaised)
            {
                raisedAt.Add(hour / 24.0);
            }
        }

        // The trade closed at Opened + 2 h and leaves the 30-day window after day 30 + 2 h.
        Assert.Equal(new[] { 21.0, 24.0, 27.0, 30.0, 33.0 }, raisedAt);
        Assert.Equal(5, alerts.Service.QueueLength);
        Assert.All(alerts.Sink.Of("inactivity"), line => Assert.Contains("last=2026-01-14T10:00:00Z known=true", line, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_NewTradeAfterTheOldOneLeftTheWindow_RearmsTheCount()
    {
        (InactivityMonitor monitor, _) = Create();
        ClosedTradeInfo old = Trade(Opened);
        Assert.True(CheckMovingWindow(monitor, Opened.AddDays(21.0), old).AlertRaised);
        Assert.Equal(Opened, CheckMovingWindow(monitor, Opened.AddDays(31.0), old).LastTradeUtc);
        DateTime newer = Opened.AddDays(32.0);
        ClosedTradeInfo next = Trade(newer);

        InactivityStatus status = CheckMovingWindow(monitor, Opened.AddDays(33.0), old, next);

        Assert.False(status.AlertRaised);
        Assert.Equal(newer, status.LastTradeUtc);
        Assert.False(CheckMovingWindow(monitor, newer.AddDays(21.0).AddSeconds(-1.0), old, next).AlertRaised);
        Assert.True(CheckMovingWindow(monitor, newer.AddDays(21.0), old, next).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_FirstTradeAfterNoHistory_RearmsTheCountAndIsKnown()
    {
        (InactivityMonitor monitor, _) = Create();
        Assert.True(CheckMovingWindow(monitor, OpsData.T0).AlertRaised);
        DateTime opened = OpsData.T0.AddHours(1.0);
        var position = new PositionInfo(2001, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, 1.12, "manual", "manual", opened);

        InactivityStatus status = monitor.Check(OpsData.T0.AddHours(2.0), Array.Empty<ClosedTradeInfo>(), OpsData.T0.AddDays(-30.0), new[] { position });

        Assert.False(status.AlertRaised);
        Assert.True(status.LastTradeKnown);
        Assert.Equal(opened, status.LastTradeUtc);
        Assert.False(CheckMovingWindow(monitor, opened.AddDays(21.0).AddSeconds(-1.0), Trade(opened)).AlertRaised);
        Assert.True(CheckMovingWindow(monitor, opened.AddDays(21.0), Trade(opened)).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_TradeOlderThanTheReference_DoesNotMoveItBack()
    {
        (InactivityMonitor monitor, _) = Create();
        Assert.False(CheckAt(monitor, Opened.AddDays(1.0), Trade(Opened)).AlertRaised);

        InactivityStatus status = CheckAt(monitor, Opened.AddDays(2.0), Trade(Opened.AddDays(-30.0)));

        Assert.Equal(Opened, status.LastTradeUtc);
        Assert.False(status.AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_RestartWithNoHistory_RaisesTheCurrentStepOnceMore()
    {
        (InactivityMonitor before, AlertSetup alertsBefore) = Create();
        Assert.True(CheckMovingWindow(before, OpsData.T0).AlertRaised);
        Assert.False(CheckMovingWindow(before, OpsData.T0.AddMinutes(1.0)).AlertRaised);
        (InactivityMonitor after, AlertSetup alertsAfter) = Create();

        Assert.True(CheckMovingWindow(after, OpsData.T0.AddMinutes(2.0)).AlertRaised);
        Assert.False(CheckMovingWindow(after, OpsData.T0.AddMinutes(3.0)).AlertRaised);
        Assert.Equal(1, alertsBefore.Service.QueueLength);
        Assert.Equal(1, alertsAfter.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void Check_LastTradeInTheFuture_NoAlert()
    {
        (InactivityMonitor monitor, _) = Create();

        Assert.False(CheckAt(monitor, Opened.AddDays(-1.0), Trade(Opened)).AlertRaised);
    }

    [Fact]
    [Trait("Req", "OPS-006")]
    public void NullArguments_Throw()
    {
        (InactivityMonitor monitor, AlertSetup alerts) = Create();

        Assert.Throws<ArgumentNullException>(() => monitor.Check(OpsData.T0, null!, HistorySince, Array.Empty<PositionInfo>()));
        Assert.Throws<ArgumentNullException>(() => monitor.Check(OpsData.T0, Array.Empty<ClosedTradeInfo>(), HistorySince, null!));
        Assert.Throws<ArgumentNullException>(() => new InactivityMonitor(null!, alerts.Journal));
        Assert.Throws<ArgumentNullException>(() => new InactivityMonitor(alerts.Service, null!));
    }
}
