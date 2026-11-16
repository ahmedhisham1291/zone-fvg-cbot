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
