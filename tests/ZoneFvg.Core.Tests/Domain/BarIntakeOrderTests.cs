using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

public class BarIntakeOrderTests
{
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

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Rank_UnknownTimeFrame_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => BarIntakeOrder.Rank((TimeFrame)99));
    }
}
