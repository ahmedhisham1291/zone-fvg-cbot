using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Nominal bar lengths and close times (SIG-004; owner decision "Close time" of 2026-11-13): the
/// close time of a bar is its open time plus 15 minutes, 4 hours or 24 hours.
/// </summary>
public class BarCloseTimeTests
{
    private static readonly DateTime Open = new(2026, 1, 5, 20, 0, 0, DateTimeKind.Utc);

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.M15, 15)]
    [InlineData(TimeFrame.H4, 240)]
    [InlineData(TimeFrame.D1, 1440)]
    public void Length_OfEachTimeFrame_IsItsNominalLength(TimeFrame timeFrame, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), BarIntakeOrder.Length(timeFrame));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Length_UnknownTimeFrame_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarIntakeOrder.Length((TimeFrame)99));
    }

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.M15, 2026, 1, 5, 20, 15)] // 20:00 + 15 minutes
    [InlineData(TimeFrame.H4, 2026, 1, 6, 0, 0)] // 20:00 + 4 hours = next day 00:00
    [InlineData(TimeFrame.D1, 2026, 1, 6, 20, 0)] // 20:00 + 24 hours
    public void CloseTimeUtc_IsOpenTimePlusLength(TimeFrame timeFrame, int year, int month, int day, int hour, int minute)
    {
        Bar bar = BarBuilder.One(timeFrame, Open, 1.0, 1.0, 1.0, 1.0);

        DateTime close = BarIntakeOrder.CloseTimeUtc(timeFrame, bar);

        Assert.Equal(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc), close);
        Assert.Equal(DateTimeKind.Utc, close.Kind);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void CloseTimeUtc_BarsThatCloseTogether_HaveTheSameCloseTime()
    {
        // D1 from 2026-01-05 00:00, H4 from 20:00 and M15 from 23:45 all close at 2026-01-06 00:00.
        var close = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        Bar d1 = BarBuilder.One(TimeFrame.D1, close.AddDays(-1), 1.0, 1.0, 1.0, 1.0);
        Bar h4 = BarBuilder.One(TimeFrame.H4, close.AddHours(-4), 1.0, 1.0, 1.0, 1.0);
        Bar m15 = BarBuilder.One(TimeFrame.M15, close.AddMinutes(-15), 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(close, BarIntakeOrder.CloseTimeUtc(TimeFrame.D1, d1));
        Assert.Equal(close, BarIntakeOrder.CloseTimeUtc(TimeFrame.H4, h4));
        Assert.Equal(close, BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, m15));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void CloseTimeUtc_InvalidArguments_Throw()
    {
        Bar bar = BarBuilder.One(TimeFrame.M15, Open, 1.0, 1.0, 1.0, 1.0);

        Assert.Throws<ArgumentNullException>("bar", () => BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, null!));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarIntakeOrder.CloseTimeUtc((TimeFrame)99, bar));
    }
}
