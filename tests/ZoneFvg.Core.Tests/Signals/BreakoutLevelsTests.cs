using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout levels (SIG-024). Worked example of <see cref="BreakoutData"/>: ATR(M15) 0.0010,
/// ATR(H4) 0.0040, EntryBufferAtr 0.1, StopAtrMult 1.0, RewardRisk 2.0.
/// </summary>
public class BreakoutLevelsTests
{
    private static Bar BuyBar()
    {
        return M15(Utc(2026, 3, 4, 8, 0), 1.0845, 1.0862, 1.0843, 1.0858);
    }

    private static Bar SellBar()
    {
        return M15(Utc(2026, 3, 4, 9, 30), 1.0785, 1.0788, 1.0768, 1.0772);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_BuyConfirmation_ReturnsTheLevelsOfTheWorkedExample()
    {
        // Entry 1.0862 + 0.1 x 0.0010 = 1.0863; stop 1.0863 - 1.0 x 0.0040 = 1.0823;
        // target 1.0863 + 2.0 x 0.0040 = 1.0943.
        BreakoutSetup setup = Require(Feed(Armed(), BuyBar(), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        AssertPrice(1.0863, setup.Entry, "entry");
        AssertPrice(1.0823, setup.Stop, "stop");
        AssertPrice(1.0943, setup.Target, "target");
        AssertPrice(1.0850, setup.Level, "level");
        Assert.Equal(Utc(2026, 3, 4, 8, 15), setup.ConfirmationCloseUtc);
        Assert.Equal(DateTimeKind.Utc, setup.ConfirmationCloseUtc.Kind);
        AssertPrice(0.0040, setup.AtrH4, "ATR(H4)");
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_SellConfirmation_ReturnsTheMirroredLevels()
    {
        // Entry 1.0768 - 0.0001 = 1.0767; stop 1.0767 + 0.0040 = 1.0807; target 1.0767 - 0.0080 = 1.0687.
        BreakoutSetup setup = Require(Feed(Armed(), SellBar(), Bias.Short));

        Assert.Equal(Side.Sell, setup.Side);
        AssertPrice(1.0767, setup.Entry, "entry");
        AssertPrice(1.0807, setup.Stop, "stop");
        AssertPrice(1.0687, setup.Target, "target");
        AssertPrice(1.0780, setup.Level, "level");
        Assert.Equal(Utc(2026, 3, 4, 9, 45), setup.ConfirmationCloseUtc);
        AssertPrice(0.0040, setup.AtrH4, "ATR(H4)");
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_EntryBufferZero_BuyEntryIsTheBarHigh()
    {
        BreakoutSetup setup = Require(Feed(Armed(new BreakoutParameters { EntryBufferAtr = 0.0 }), BuyBar(), Bias.Long));

        AssertPrice(1.0862, setup.Entry, "entry");
        AssertPrice(1.0822, setup.Stop, "stop");
        AssertPrice(1.0942, setup.Target, "target");
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_StopAtrMultOneAndAHalf_WidensStopAndTarget()
    {
        // Stop 1.0863 - 1.5 x 0.0040 = 1.0803; target 1.0863 + 2.0 x 0.0060 = 1.0983.
        BreakoutSetup setup = Require(Feed(Armed(new BreakoutParameters { StopAtrMult = 1.5 }), BuyBar(), Bias.Long));

        AssertPrice(1.0863, setup.Entry, "entry");
        AssertPrice(1.0803, setup.Stop, "stop");
        AssertPrice(1.0983, setup.Target, "target");
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_RewardRiskThree_MovesTheTarget()
    {
        // Target 1.0863 + 3.0 x 0.0040 = 1.0983.
        BreakoutSetup setup = Require(Feed(Armed(rewardRisk: 3.0), BuyBar(), Bias.Long));

        AssertPrice(1.0863, setup.Entry, "entry");
        AssertPrice(1.0823, setup.Stop, "stop");
        AssertPrice(1.0983, setup.Target, "target");
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData("M15", null)]
    [InlineData("M15", 0.0)]
    [InlineData("M15", -0.001)]
    [InlineData("M15", double.NaN)]
    [InlineData("M15", double.PositiveInfinity)]
    [InlineData("M15", double.NegativeInfinity)]
    [InlineData("H4", null)]
    [InlineData("H4", 0.0)]
    [InlineData("H4", -0.004)]
    [InlineData("H4", double.NaN)]
    [InlineData("H4", double.PositiveInfinity)]
    [InlineData("H4", double.NegativeInfinity)]
    public void OnM15Closed_AtrMissingOrInvalid_ReturnsNullAndFinishesTheReference(string which, double? value)
    {
        BreakoutTracker tracker = Armed();
        double? atrM15 = which == "M15" ? value : AtrM15;
        double? atrH4 = which == "H4" ? value : AtrH4;

        Assert.Null(tracker.OnM15Closed(BuyBar(), Bias.Long, atrM15, atrH4));
        AssertDone(tracker);

        // A reference finished without a result is not armed again.
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 15), 1.0870), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_SellEntryNotAboveZero_ReturnsNullAndFinishesTheReference()
    {
        // Entry 1.0768 - 0.1 x 20.0 = -0.9232.
        BreakoutTracker tracker = Armed();

        Assert.Null(tracker.OnM15Closed(SellBar(), Bias.Short, 20.0, AtrH4));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_BuyStopNotAboveZero_ReturnsNullAndFinishesTheReference()
    {
        // Entry 1.0863; stop 1.0863 - 1.0 x 2.0 = -0.9137.
        BreakoutTracker tracker = Armed();

        Assert.Null(tracker.OnM15Closed(BuyBar(), Bias.Long, AtrM15, 2.0));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_SellStopNotFinite_ReturnsNullAndFinishesTheReference()
    {
        // Stop 1.0767 + 1.5 x double.MaxValue is infinite.
        BreakoutTracker tracker = Armed(new BreakoutParameters { StopAtrMult = 1.5 });

        Assert.Null(tracker.OnM15Closed(SellBar(), Bias.Short, AtrM15, double.MaxValue));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void OnM15Closed_SellTargetNotAboveZero_ReturnsNullAndFinishesTheReference()
    {
        // Entry 1.0767; stop 1.0767 + 0.6 = 1.6767; target 1.0767 - 2.0 x 0.6 = -0.1233.
        BreakoutTracker tracker = Armed();

        Assert.Null(tracker.OnM15Closed(SellBar(), Bias.Short, AtrM15, 0.6));
        AssertDone(tracker);
    }
}
