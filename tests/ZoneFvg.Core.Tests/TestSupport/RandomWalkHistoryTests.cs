using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>Checks of the property-test generator and of the aggregation helper.</summary>
public class RandomWalkHistoryTests
{
    [Fact]
    public void Aggregate_M15IntoH4AndD1_UsesFixedUtcBoundaries()
    {
        // Eight M15 bars from Friday 23:00 UTC: 23:00 to 23:45 Friday, then 00:00 to 00:45 Monday
        // (the weekend has no bars). Prices are 1 to 8, each bar (p, p + 0.5, p - 0.5, p).
        var friday = new DateTime(2026, 1, 9, 23, 0, 0, DateTimeKind.Utc);
        var monday = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<Bar> m15 = new BarBuilder(TimeFrame.M15)
            .At(friday)
            .Repeat(4, (b, i) => b.Ohlc(1.0 + i, 1.5 + i, 0.5 + i, 1.0 + i))
            .At(monday)
            .Repeat(4, (b, i) => b.Ohlc(5.0 + i, 5.5 + i, 4.5 + i, 5.0 + i))
            .Build();

        IReadOnlyList<Bar> h4 = BarAggregator.Aggregate(m15, TimeFrame.H4);
        IReadOnlyList<Bar> d1 = BarAggregator.Aggregate(m15, TimeFrame.D1);

        Assert.Equal(new[] { new Bar(friday.AddHours(-3), 1.0, 4.5, 0.5, 4.0), new Bar(monday, 5.0, 8.5, 4.5, 8.0) }, h4);
        Assert.Equal(new[] { new Bar(friday.AddHours(-23), 1.0, 4.5, 0.5, 4.0), new Bar(monday, 5.0, 8.5, 4.5, 8.0) }, d1);
    }

    [Fact]
    public void Generate_TenWeekdays_HasConsistentTimeFrames()
    {
        RandomWalkHistory history = RandomWalkHistory.Generate(42, 10);

        Assert.Equal(960, history.M15.Count);
        Assert.Equal(60, history.H4.Count);
        Assert.Equal(10, history.D1.Count);
        Assert.Equal(1030, history.Feed.Count);
        Assert.All(history.D1, bar => Assert.True(bar.OpenTimeUtc.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday));
        Assert.Equal(history.M15[95].Close, history.D1[0].Close);
        Assert.Equal(history.M15.Take(16).Max(bar => bar.High), history.H4[0].High);
    }

    [Fact]
    public void Generate_SameSeedTwice_SameBars()
    {
        Assert.Equal(RandomWalkHistory.Generate(7, 5).M15, RandomWalkHistory.Generate(7, 5).M15);
        Assert.NotEqual(RandomWalkHistory.Generate(7, 5).M15, RandomWalkHistory.Generate(8, 5).M15);
    }
}
