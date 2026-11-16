using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-001 and OPS-004: the execution hook journals every skip and raises every execution alert;
/// news results raise the calendar and malformed-feed alerts.
/// </summary>
public class OpsEventsTests
{
    private const string Malformed = "[{\"title\":\"X\",\"country\":\"USD\",\"date\":\"2026-11-11T08:30:00-05:00\"}]";

    private static (OpsEvents Events, AlertSetup Alerts, FakeBroker Broker) Create()
    {
        AlertSetup alerts = OpsData.Alerts();
        FakeBroker broker = ExecutionHarness.DefaultBroker();
        return (new OpsEvents(alerts.Journal, alerts.Service, broker), alerts, broker);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Skipped_Record_IsJournaledAndCounted()
    {
        (OpsEvents events, AlertSetup alerts, _) = Create();

        events.Skipped(new SkipRecord("EURUSD", "D-1", SkipReason.NewsWindow, 0.0, 0.0, "inside window", OpsData.T0));

        Assert.Equal(new[] { "2026-01-14T10:00:00Z skip symbol=EURUSD zone=D-1 reason=NewsWindow measured=0.0000 limit=0.0000 detail=inside\\swindow" }, alerts.Sink.Of("skip"));
        Assert.Equal(1, alerts.Journal.CountersFor(new DateTime(2026, 1, 13, 23, 0, 0, DateTimeKind.Utc)).Skips);
        Assert.Throws<ArgumentNullException>(() => events.Skipped(null!));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Alert_ExecutionAlert_IsQueuedAtServerTimeWithItsKindInTheText()
    {
        (OpsEvents events, AlertSetup alerts, FakeBroker broker) = Create();
        broker.AdvanceTo(OpsData.T0.AddMinutes(7.0));

        events.Alert(new ExecutionAlert(AlertKind.CancelFailed, "EURUSD", "order 5 cancel Unclear"));

        Assert.Equal(new[] { "ZFVG EURUSD OrderProblem: EURUSD CancelFailed: order 5 cancel Unclear" }, alerts.Service.PendingTexts);
        Assert.StartsWith("2026-01-14T10:07:00Z alert event=queued kind=OrderProblem ", alerts.Sink.Of("alert").Single(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => events.Alert(null!));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void NewsUpdated_MalformedWithoutData_RaisesCalendarAndFeedAlerts()
    {
        (OpsEvents events, AlertSetup alerts, _) = Create();
        NewsUpdate update = LiveService(new MemoryStore(), new FakeTransport(Ok(Malformed))).Refresh(SampleWednesday);

        Assert.Equal(2, events.NewsUpdated(update, SampleWednesday));

        Assert.Equal("ZFVG EURUSD CalendarUnavailable: No trusted news data covers the current time; new entries are blocked.", alerts.Service.PendingTexts[0]);
        Assert.StartsWith("ZFVG EURUSD NewsFeedDown: Download from ForexFactory rejected as malformed; earlier data kept. Download rejected: Event ", alerts.Service.PendingTexts[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void NewsUpdated_GoodOrPlainFailedDownload_RaisesNothing()
    {
        (OpsEvents events, AlertSetup alerts, _) = Create();

        Assert.Equal(0, events.NewsUpdated(LiveService(new MemoryStore(), new FakeTransport(Ok(Sample))).Refresh(SampleWednesday), SampleWednesday));
        Assert.Equal(0, alerts.Service.QueueLength);
        Assert.Throws<ArgumentNullException>(() => events.NewsUpdated(null!, SampleWednesday));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void NewsUpdated_FirstFailureWithoutData_RaisesCalendarAlertOnce()
    {
        (OpsEvents events, AlertSetup alerts, _) = Create();
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Failed()));

        Assert.Equal(1, events.NewsUpdated(service.Refresh(SampleWednesday), SampleWednesday));
        Assert.Equal(0, events.NewsUpdated(service.Refresh(SampleWednesday.AddHours(1.0)), SampleWednesday.AddHours(1.0)));
        Assert.Single(alerts.Service.PendingTexts);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Constructor_NullArguments_Throw()
    {
        AlertSetup a = OpsData.Alerts();
        FakeBroker broker = ExecutionHarness.DefaultBroker();

        Assert.Throws<ArgumentNullException>(() => new OpsEvents(null!, a.Service, broker));
        Assert.Throws<ArgumentNullException>(() => new OpsEvents(a.Journal, null!, broker));
        Assert.Throws<ArgumentNullException>(() => new OpsEvents(a.Journal, a.Service, null!));
    }
}
