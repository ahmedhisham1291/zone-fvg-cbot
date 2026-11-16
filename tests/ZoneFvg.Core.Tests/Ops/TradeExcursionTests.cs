using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-002: the largest adverse and favourable moves in R of a closed trade, from the closed M15
/// bars that overlap its life and its exit price. Buy at 1.1 with stop 1.098 (risk 0.002), filled
/// at 10:20 and closed at 10:50; bars open at 10:00 to 11:00.
/// </summary>
public class TradeExcursionTests
{
    private static readonly DateTime T = OpsData.T0;
    private static readonly DateTime Fill = T.AddMinutes(20.0);
    private static readonly DateTime Close = T.AddMinutes(50.0);

    private static IReadOnlyList<Bar> Bars()
    {
        // 10:00 ends before the fill and 11:00 opens after the close: both are outside the life.
        return new[]
        {
            BarBuilder.One(TimeFrame.M15, T, 1.1, 1.1100, 1.0900, 1.1),
            BarBuilder.One(TimeFrame.M15, T.AddMinutes(15.0), 1.1, 1.1010, 1.0995, 1.1005),
            BarBuilder.One(TimeFrame.M15, T.AddMinutes(30.0), 1.1005, 1.1030, 1.1000, 1.1020),
            BarBuilder.One(TimeFrame.M15, T.AddMinutes(45.0), 1.1020, 1.1025, 1.1010, 1.1015),
            BarBuilder.One(TimeFrame.M15, Close.AddMinutes(10.0), 1.1015, 1.1200, 1.0800, 1.1),
        };
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_Buy_UsesOnlyTheBarsOfItsLife()
    {
        (double AdverseR, double FavourableR) moves = TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, 1.1015, Bars())!.Value;

        // Adverse 1.1 - 1.0995 = 0.25 R; favourable 1.1030 - 1.1 = 1.5 R.
        Assert.Equal(0.25, moves.AdverseR, 9);
        Assert.Equal(1.5, moves.FavourableR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_Sell_SwapsTheSides()
    {
        (double AdverseR, double FavourableR) moves = TradeExcursion.ForTrade(Side.Sell, 1.1, 1.102, Fill, Close, null, Bars())!.Value;

        Assert.Equal(1.5, moves.AdverseR, 9);
        Assert.Equal(0.25, moves.FavourableR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_ExitBeyondTheBars_CountsTheExitPrice()
    {
        // The bar of the close has not closed yet: the stop fill at 1.098 is the extreme, 1.0 R.
        IReadOnlyList<Bar> bars = Bars().Take(3).ToList();

        (double AdverseR, double FavourableR) moves = TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, 1.098, bars)!.Value;

        Assert.Equal(1.0, moves.AdverseR, 9);
        Assert.Equal(1.5, moves.FavourableR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_ExitPriceNotFinite_IsIgnored()
    {
        (double AdverseR, double FavourableR) moves = TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, double.NaN, Bars())!.Value;

        Assert.Equal(0.25, moves.AdverseR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_BarsStartAfterTheFill_GiveNull()
    {
        IReadOnlyList<Bar> late = Bars().Skip(2).ToList();

        Assert.Null(TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, 1.1015, late));
        Assert.Null(TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, 1.1015, Array.Empty<Bar>()));
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_FillAtABarOpen_StartsAtThatBar()
    {
        // Filled exactly at 10:15: the 10:00 bar ends at the fill and does not count.
        (double AdverseR, double FavourableR) moves = TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, T.AddMinutes(15.0), Close, null, Bars())!.Value;

        Assert.Equal(0.25, moves.AdverseR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void ForTrade_NoBarInTheLifeAndNoExitOrBadStop_GivesNull()
    {
        IReadOnlyList<Bar> first = Bars().Take(1).ToList();

        Assert.Null(TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, null, first));
        Assert.Null(TradeExcursion.ForTrade(Side.Buy, 1.1, 1.1, Fill, Close, 1.1015, Bars()));
        Assert.Throws<ArgumentNullException>(() => TradeExcursion.ForTrade(Side.Buy, 1.1, 1.098, Fill, Close, null, null!));
    }
}
