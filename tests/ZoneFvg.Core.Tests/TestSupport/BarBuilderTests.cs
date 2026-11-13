using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

public class BarBuilderTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TimeFrame.M15, 15.0)]
    [InlineData(TimeFrame.H4, 240.0)]
    [InlineData(TimeFrame.D1, 1440.0)]
    public void Build_SeveralBars_TimesAdvanceByTheTimeFrameLength(TimeFrame timeFrame, double minutes)
    {
        IReadOnlyList<Bar> bars = new BarBuilder(timeFrame).At(Start).Closes(1.0, 2.0, 3.0).Build();

        Assert.Equal(3, bars.Count);
        Assert.Equal(Start, bars[0].OpenTimeUtc);
        Assert.Equal(Start.AddMinutes(minutes), bars[1].OpenTimeUtc);
        Assert.Equal(Start.AddMinutes(2.0 * minutes), bars[2].OpenTimeUtc);
        Assert.Equal(DateTimeKind.Utc, bars[2].OpenTimeUtc.Kind);
        Assert.Equal(TimeSpan.FromMinutes(minutes), BarBuilder.LengthOf(timeFrame));
    }

    [Fact]
    public void Bullish_ThenBearish_BuildsBarsWithoutWicks()
    {
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(Start)
            .Bullish(1.1000, 1.1040)
            .Then()
            .Bearish(1.1040, 1.1010)
            .Build();

        Assert.Equal(2, bars.Count);
        Assert.Equal(new Bar(Start, 1.1000, 1.1040, 1.1000, 1.1040), bars[0]);
        Assert.Equal(new Bar(Start.AddHours(4), 1.1040, 1.1040, 1.1010, 1.1010), bars[1]);
    }

    [Fact]
    public void Range_AfterABar_ReplacesItsLowAndHigh()
    {
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.M15)
            .At(Start)
            .Bullish(1.1000, 1.1040).Range(1.0990, 1.1050)
            .Build();

        Bar bar = Assert.Single(bars);
        Assert.Equal(new Bar(Start, 1.1000, 1.1050, 1.0990, 1.1040), bar);
    }

    [Fact]
    public void Ohlc_AndDoji_UseTheGivenPrices()
    {
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.D1)
            .At(Start)
            .Ohlc(10.0, 12.0, 9.0, 11.0)
            .Doji(11.0)
            .Build();

        Assert.Equal(2, bars.Count);
        Assert.Equal(new Bar(Start, 10.0, 12.0, 9.0, 11.0), bars[0]);
        Assert.Equal(new Bar(Start.AddDays(1), 11.0, 11.0, 11.0, 11.0), bars[1]);
    }

    [Fact]
    public void One_GivenTimeAndPrices_BuildsThatSingleBar()
    {
        Bar bar = BarBuilder.One(TimeFrame.H4, Start, 10.0, 12.0, 9.0, 11.0);

        Assert.Equal(new Bar(Start, 10.0, 12.0, 9.0, 11.0), bar);
    }

    [Fact]
    public void At_InTheMiddle_JumpsToTheNewTime()
    {
        DateTime afterWeekend = Start.AddDays(7);

        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(Start)
            .Doji(1.0)
            .At(afterWeekend)
            .Doji(2.0)
            .Doji(3.0)
            .Build();

        Assert.Equal(3, bars.Count);
        Assert.Equal(Start, bars[0].OpenTimeUtc);
        Assert.Equal(afterWeekend, bars[1].OpenTimeUtc);
        Assert.Equal(afterWeekend.AddHours(4), bars[2].OpenTimeUtc);
    }

    [Fact]
    public void Repeat_WithIndex_DefinesOneBarPerIndex()
    {
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.M15)
            .At(Start)
            .Repeat(4, (builder, index) => builder.Doji(10.0 + index))
            .Build();

        Assert.Equal(4, bars.Count);
        Assert.Equal(10.0, bars[0].Close);
        Assert.Equal(13.0, bars[3].Close);
        Assert.Equal(Start.AddMinutes(45), bars[3].OpenTimeUtc);
    }

    [Fact]
    public void Build_CalledTwice_ReturnsSeparateCopies()
    {
        var builder = new BarBuilder(TimeFrame.M15).At(Start).Doji(1.0);

        IReadOnlyList<Bar> first = builder.Build();
        IReadOnlyList<Bar> second = builder.Doji(2.0).Build();

        Assert.Single(first);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void Build_WithoutBars_ReturnsEmptyList()
    {
        Assert.Empty(new BarBuilder(TimeFrame.M15).At(Start).Build());
    }

    [Fact]
    public void Build_BarDefinedBeforeAt_Throws()
    {
        var builder = new BarBuilder(TimeFrame.M15).Doji(1.0);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void At_NonUtcTime_Throws()
    {
        var local = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>("openTimeUtc", () => new BarBuilder(TimeFrame.M15).At(local));
    }

    [Fact]
    public void Range_WithoutABar_Throws()
    {
        var builder = new BarBuilder(TimeFrame.M15).At(Start);

        Assert.Throws<InvalidOperationException>(() => builder.Range(1.0, 2.0));
    }

    [Fact]
    public void Then_WithoutABar_Throws()
    {
        var builder = new BarBuilder(TimeFrame.M15).At(Start);

        Assert.Throws<InvalidOperationException>(() => builder.Then());
    }

    [Fact]
    public void Bullish_CloseNotAboveOpen_Throws()
    {
        var builder = new BarBuilder(TimeFrame.M15).At(Start);

        Assert.Throws<ArgumentException>("close", () => builder.Bullish(1.0, 1.0));
    }

    [Fact]
    public void Bearish_CloseNotBelowOpen_Throws()
    {
        var builder = new BarBuilder(TimeFrame.M15).At(Start);

        Assert.Throws<ArgumentException>("close", () => builder.Bearish(1.0, 1.0));
    }

    [Fact]
    public void LengthOf_UnknownTimeFrame_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarBuilder.LengthOf((TimeFrame)99));
    }
}
