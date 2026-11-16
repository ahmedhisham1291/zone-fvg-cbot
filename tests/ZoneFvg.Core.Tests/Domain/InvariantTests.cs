using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The shared invariant formatting and parsing helpers (AGENTS.md rule 7, review finding L5). The
/// outputs are the ones of the private copies they replace.
/// </summary>
public class InvariantTests
{
    [Fact]
    public void Text_NumbersAndTimeSpans_UseTheInvariantForms()
    {
        Assert.Equal("-12", Invariant.Text(-12));
        Assert.Equal("9007199254740993", Invariant.Text(9007199254740993L));
        Assert.Equal("1.0995", Invariant.Text(1.0995));
        Assert.Equal("0.30000000000000004", Invariant.Text(0.1 + 0.2));
        Assert.Equal("07:00:00", Invariant.Text(TimeSpan.FromHours(7.0)));
        Assert.Equal("1.02:03:04", Invariant.Text(new TimeSpan(1, 2, 3, 4)));
    }

    [Fact]
    public void MinuteAndSecond_FormatTheTime()
    {
        var time = new DateTime(2026, 3, 4, 8, 45, 30, DateTimeKind.Utc);

        Assert.Equal("2026-03-04 08:45", Invariant.Minute(time));
        Assert.Equal("2026-03-04 08:45:30", Invariant.Second(time));
    }

    [Fact]
    public void TryParseUtc_RoundTripUtc_IsReadBack()
    {
        var time = new DateTime(2026, 3, 4, 8, 45, 30, 123, DateTimeKind.Utc);

        Assert.True(Invariant.TryParseUtc(time.ToString("o", CultureInfo.InvariantCulture), out DateTime parsed));
        Assert.Equal(time, parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-03-04 08:45")]
    [InlineData("2026-03-04T08:45:30.0000000")]
    [InlineData("2026-03-04T08:45:30.0000000+01:00")]
    public void TryParseUtc_NotARoundTripUtcTime_IsRefused(string? text)
    {
        Assert.False(Invariant.TryParseUtc(text, out DateTime _));
    }
}
