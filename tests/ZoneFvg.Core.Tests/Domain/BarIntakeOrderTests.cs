using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

public class BarIntakeOrderTests
{
    // Three bars that close at the same instant, 2026-01-06 00:00 UTC:
    // the D1 bar opened 24 hours earlier, the H4 bar 4 hours earlier, the M15 bar 15 minutes earlier.
    private static readonly DateTime CloseTime = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Bar Daily = BarBuilder.One(TimeFrame.D1, CloseTime.AddDays(-1.0), 1.1000, 1.1100, 1.0950, 1.1050);
    private static readonly Bar FourHour = BarBuilder.One(TimeFrame.H4, CloseTime.AddHours(-4.0), 1.1040, 1.1060, 1.1030, 1.1050);
    private static readonly Bar QuarterHour = BarBuilder.One(TimeFrame.M15, CloseTime.AddMinutes(-15.0), 1.1048, 1.1052, 1.1046, 1.1050);

    [Fact]
    [Trait("Req", "SIG-004")]
    public void SameInstant_ListsD1ThenH4ThenM15()
    {
        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, BarIntakeOrder.SameInstant.ToArray());
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Rank_OfEachTimeFrame_IsItsPositionInTheProcessingOrder()
    {
        Assert.Equal(0, BarIntakeOrder.Rank(TimeFrame.D1));
        Assert.Equal(1, BarIntakeOrder.Rank(TimeFrame.H4));
        Assert.Equal(2, BarIntakeOrder.Rank(TimeFrame.M15));
    }

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.D1, TimeFrame.H4, TimeFrame.M15)]
    [InlineData(TimeFrame.D1, TimeFrame.M15, TimeFrame.H4)]
    [InlineData(TimeFrame.H4, TimeFrame.D1, TimeFrame.M15)]
    [InlineData(TimeFrame.H4, TimeFrame.M15, TimeFrame.D1)]
    [InlineData(TimeFrame.M15, TimeFrame.D1, TimeFrame.H4)]
    [InlineData(TimeFrame.M15, TimeFrame.H4, TimeFrame.D1)]
    public void Sort_ThreeBarsInAnyArrivalOrder_ReturnsD1ThenH4ThenM15(TimeFrame first, TimeFrame second, TimeFrame third)
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)>
        {
            (first, BarOf(first)),
            (second, BarOf(second)),
            (third, BarOf(third)),
        };

        IReadOnlyList<(TimeFrame TimeFrame, Bar Bar)> ordered = BarIntakeOrder.Sort(arrival);

        Assert.Equal(3, ordered.Count);
        Assert.Equal(TimeFrame.D1, ordered[0].TimeFrame);
        Assert.Same(Daily, ordered[0].Bar);
        Assert.Equal(TimeFrame.H4, ordered[1].TimeFrame);
        Assert.Same(FourHour, ordered[1].Bar);
        Assert.Equal(TimeFrame.M15, ordered[2].TimeFrame);
        Assert.Same(QuarterHour, ordered[2].Bar);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_OnlyH4AndM15_ReturnsH4First()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)>
        {
            (TimeFrame.M15, QuarterHour),
            (TimeFrame.H4, FourHour),
        };

        IReadOnlyList<(TimeFrame TimeFrame, Bar Bar)> ordered = BarIntakeOrder.Sort(arrival);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(TimeFrame.H4, ordered[0].TimeFrame);
        Assert.Same(FourHour, ordered[0].Bar);
        Assert.Equal(TimeFrame.M15, ordered[1].TimeFrame);
        Assert.Same(QuarterHour, ordered[1].Bar);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_OnlyD1AndM15_ReturnsD1First()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)>
        {
            (TimeFrame.M15, QuarterHour),
            (TimeFrame.D1, Daily),
        };

        IReadOnlyList<(TimeFrame TimeFrame, Bar Bar)> ordered = BarIntakeOrder.Sort(arrival);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(TimeFrame.D1, ordered[0].TimeFrame);
        Assert.Equal(TimeFrame.M15, ordered[1].TimeFrame);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_SingleBar_ReturnsIt()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)> { (TimeFrame.M15, QuarterHour) };

        (TimeFrame TimeFrame, Bar Bar) only = Assert.Single(BarIntakeOrder.Sort(arrival));

        Assert.Equal(TimeFrame.M15, only.TimeFrame);
        Assert.Same(QuarterHour, only.Bar);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_NoBars_ReturnsEmptyList()
    {
        Assert.Empty(BarIntakeOrder.Sort(new List<(TimeFrame TimeFrame, Bar Bar)>()));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_TwoBarsOfTheSameTimeFrame_Throws()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)>
        {
            (TimeFrame.H4, FourHour),
            (TimeFrame.H4, FourHour),
        };

        Assert.Throws<ArgumentException>("closedTogether", () => BarIntakeOrder.Sort(arrival));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_MissingBar_Throws()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)> { (TimeFrame.H4, null!) };

        Assert.Throws<ArgumentException>("closedTogether", () => BarIntakeOrder.Sort(arrival));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_NullSequence_Throws()
    {
        Assert.Throws<ArgumentNullException>("closedTogether", () => BarIntakeOrder.Sort(null!));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Sort_UnknownTimeFrame_Throws()
    {
        var arrival = new List<(TimeFrame TimeFrame, Bar Bar)> { ((TimeFrame)99, QuarterHour) };

        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarIntakeOrder.Sort(arrival));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Rank_UnknownTimeFrame_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarIntakeOrder.Rank((TimeFrame)99));
    }

    private static Bar BarOf(TimeFrame timeFrame)
    {
        return timeFrame switch
        {
            TimeFrame.D1 => Daily,
            TimeFrame.H4 => FourHour,
            _ => QuarterHour,
        };
    }
}
