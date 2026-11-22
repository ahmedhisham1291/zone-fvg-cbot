using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout identity (SIG-025): the id comes from the UTC open time of the reference D1 bar,
/// and a reference produces at most one result. Worked example of <see cref="BreakoutData"/>.
/// </summary>
public class BreakoutIdentityTests
{
    [Fact]
    [Trait("Req", "SIG-025")]
    public void OnM15Closed_Buy_IdIsBhWithTheReferenceOpenTime()
    {
        BreakoutSetup setup = Require(Feed(Armed(), M15(Utc(2026, 3, 4, 8, 0), 1.0845, 1.0862, 1.0843, 1.0858), Bias.Long));

        Assert.Equal("BH-20260302-2200", setup.Id);
    }

    [Fact]
    [Trait("Req", "SIG-025")]
    public void OnM15Closed_Sell_IdIsBlWithTheReferenceOpenTime()
    {
        BreakoutSetup setup = Require(Feed(Armed(), M15(Utc(2026, 3, 4, 9, 30), 1.0785, 1.0788, 1.0768, 1.0772), Bias.Short));

        Assert.Equal("BL-20260302-2200", setup.Id);
    }

    [Fact]
    [Trait("Req", "SIG-025")]
    public void OnM15Closed_ReferenceOpenedAt2100_IdCarriesThatTime()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), RewardRisk);
        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 11, 5, 21, 0), 1.1000, 1.1050, 1.0980, 1.1040));

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 11, 9, 8, 0), 1.1060), Bias.Long));

        Assert.Equal("BH-20261105-2100", setup.Id);
    }

    [Fact]
    [Trait("Req", "SIG-025")]
    public void OnM15Closed_ThreeClosesAboveTheHighInOneDay_ExactlyOneResult()
    {
        BreakoutTracker tracker = Armed();
        var results = new List<BreakoutSetup>();
        foreach (int minute in new[] { 0, 15, 30 })
        {
            BreakoutSetup? setup = Feed(tracker, Flat(Utc(2026, 3, 4, 8, minute), 1.0858 + (minute * 0.00001)), Bias.Long);
            if (setup is not null)
            {
                results.Add(setup);
            }
        }

        BreakoutSetup only = Assert.Single(results);
        Assert.Equal(Utc(2026, 3, 4, 8, 15), only.ConfirmationCloseUtc);
    }
}
