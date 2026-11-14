using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>NWS-012: events without a clock time (all-day, tentative, holidays) create no window and are listed for the log.</summary>
public class NewsEventsWithoutWindowTests
{
    private static readonly DateTime T = Utc(2026, 11, 10, 13, 30);

    [Fact]
    [Trait("Req", "NWS-012")]
    public void AllDayHighEvent_NoWindowAndListed()
    {
        NewsEvent allDay = Event("USD", null, NewsImpact.High, "Bank Holiday");
        NewsCalendar calendar = Calendar(allDay, Event("USD", T));

        Assert.Equal(new[] { allDay }, calendar.EventsWithoutWindow);
        Assert.Single(calendar.WindowsFor("EURUSD"));
        Assert.False(allDay.HasClockTime);
    }

    [Fact]
    [Trait("Req", "NWS-012")]
    public void HolidayWithATime_NoWindowAndListed()
    {
        NewsEvent holiday = Event("USD", T, NewsImpact.Holiday, "Bank Holiday");
        NewsCalendar calendar = Calendar(holiday);

        Assert.Equal(new[] { holiday }, calendar.EventsWithoutWindow);
        Assert.Empty(calendar.WindowsFor("EURUSD"));
        Assert.False(calendar.IsInWindow("EURUSD", T));
    }

    [Fact]
    [Trait("Req", "NWS-012")]
    public void RealSample_ThreeHolidaysListed()
    {
        IReadOnlyList<NewsEvent> events = new ForexFactoryParser().Parse(Sample, SampleWednesday).Events;
        var calendar = new NewsCalendar(new[] { events }, Array.Empty<NewsEvent>(), 30, 30);

        Assert.Equal(
            new[] { "French Bank Holiday", "Bank Holiday", "Bank Holiday" },
            calendar.EventsWithoutWindow.Select(e => e.Title).ToArray());
        Assert.Equal(new[] { "EUR", "CAD", "USD" }, calendar.EventsWithoutWindow.Select(e => e.Currency).ToArray());
    }

    [Fact]
    [Trait("Req", "NWS-012")]
    public void TimedEvents_AreNotListed()
    {
        NewsCalendar calendar = Calendar(Event("USD", T), Event("USD", T, NewsImpact.Low));

        Assert.Empty(calendar.EventsWithoutWindow);
        Assert.True(Event("USD", T, NewsImpact.Low).HasClockTime);
    }

    [Fact]
    [Trait("Req", "NWS-012")]
    public void Events_WithoutTimeComeFirstThenInTimeOrder()
    {
        NewsEvent late = Event("USD", T.AddHours(1));
        NewsEvent early = Event("USD", T);
        NewsEvent untimed = Event("USD", null);

        Assert.Equal(new[] { untimed, early, late }, Calendar(late, untimed, early).Events);
    }
}
