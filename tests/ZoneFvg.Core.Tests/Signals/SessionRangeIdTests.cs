using Xunit;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-025: the timed exit finds the trading day of a position from its signal id, RH- or RL-
/// followed by the date that names the trading day (SIG-032). Every other text is not a session
/// range id, so positions of the other modes are never touched.
/// </summary>
public class SessionRangeIdTests
{
    [Theory]
    [Trait("Req", "EXE-025")]
    [InlineData("RH-20260304", 2026, 3, 4)]
    [InlineData("RL-20260313", 2026, 3, 13)]
    [InlineData("RH-20240229", 2024, 2, 29)]
    [InlineData("RL-00020101", 2, 1, 1)]
    [InlineData("RH-99981231", 9998, 12, 31)]
    public void TryTradingDate_SessionRangeId_TheDateThatNamesTheTradingDay(string id, int year, int month, int day)
    {
        Assert.True(SessionRangeId.TryTradingDate(id, out DateTime date));

        Assert.Equal(new DateTime(year, month, day), date);
        Assert.Equal(TimeSpan.Zero, date.TimeOfDay);
    }

    [Theory]
    [Trait("Req", "EXE-025")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BH-20260303-0000")]
    [InlineData("BL-20260303-0000")]
    [InlineData("D-20260303-1600")]
    [InlineData("S-20260303-1600")]
    [InlineData("RH-2026030")]
    [InlineData("RH-202603041")]
    [InlineData("RX-20260304")]
    [InlineData("rh-20260304")]
    [InlineData("RH_20260304")]
    [InlineData("RH-2026030x")]
    [InlineData("RH-+2026030")]
    [InlineData("RH- 2026030")]
    [InlineData("RH-20260230")]
    [InlineData("RH-20261301")]
    [InlineData("RH-00010101")]
    [InlineData("RH-99991231")]
    [InlineData("RH-٢٠٢٦٠٣٠٤")]
    public void TryTradingDate_AnyOtherText_False(string? id)
    {
        Assert.False(SessionRangeId.TryTradingDate(id, out DateTime date));

        Assert.Equal(DateTime.MinValue, date);
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void Prefixes_AreThoseOfTheTracker()
    {
        Assert.Equal("RH-", SessionRangeId.BuyPrefix);
        Assert.Equal("RL-", SessionRangeId.SellPrefix);
    }
}
