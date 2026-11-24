using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using D = ZoneFvg.Core.Tests.RecoverySizingData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-016 and RSK-002: <see cref="PositionSizer.SizeAmount"/> sizes with the Recovery amount R as the
/// risk amount of RSK-001, without rounding R, with the checks of RSK-002 in their order, on the
/// defaults of spec v0.12 (MaxVolumeUnits 3,000,000, sanity cap off). Reference buy: entry 1.10000,
/// stop 1.09800 (20 pips, volume R / 0.002) or 1.09880 (12 pips, volume R / 0.0012).
/// </summary>
public class SizeAmountTests
{
    private static SizingResult SizeAmount(RiskParameters parameters, double amount, double stop = 1.098, double target = 1.104)
    {
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, stop, target);
        return new PositionSizer(parameters).SizeAmount(RiskData.Account(D.Capital), plan, RiskData.EurUsd, amount);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void SizeAmount_RecoveryCapOf2560OnTheDefaults_AllowedBySizer()
    {
        SizingResult result = SizeAmount(D.Recovery, 2560.0);

        Assert.True(result.IsAllowed);
        Assert.Equal(1280000.0, result.VolumeUnits);
        Assert.Equal(2560.0, result.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void SizeAmount_SanityCapOnAt128Percent_2560AllowedAndOneCentAboveRefused()
    {
        RiskParameters on = D.Recovery with { SanityCapEnabled = true, SanityCapPercent = 1.28 };

        // 2,560.01 / 0.002 = 1,280,005 -> 1,280,000 units, risk 2,560: the rounding keeps it inside, so the cap is checked on 2,562.
        SizingResult at = SizeAmount(on, 2560.0);
        SizingResult above = SizeAmount(on, 2562.0);

        Assert.True(at.IsAllowed);
        Assert.Equal(RiskReason.RiskAboveSanityCap, above.Reason);
        Assert.Equal(2562.0, above.Measured, 6);
        Assert.Equal(2560.0, above.Limit, 6);
        Assert.Contains(" above 1.28% of balance 2560", above.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void SizeAmount_SanityCapOnAt128PercentWithAOneUnitStep_OneCentAboveRefused()
    {
        RiskParameters on = D.Recovery with { SanityCapEnabled = true, SanityCapPercent = 1.28 };
        SymbolInfo fineStep = RiskData.EurUsd with { VolumeStepUnits = 1.0, VolumeMinUnits = 1.0 };
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, 1.098, 1.104);

        // Step 1 unit: 2,560.01 / 0.002 = 1,280,005 units, risk 2,560.01 above 2,560.
        SizingResult above = new PositionSizer(on).SizeAmount(RiskData.Account(D.Capital), plan, fineStep, 2560.01);

        Assert.Equal(RiskReason.RiskAboveSanityCap, above.Reason);
        Assert.Equal(2560.01, above.Measured, 6);
    }

    [Theory]
    [Trait("Req", "RSK-016")]
    [InlineData(2560.0, 2133000.0)]
    [InlineData(3600.0, 3000000.0)]
    [InlineData(3601.19, 3000000.0)]
    public void SizeAmount_TwelvePipStopWithTheDefaultVolumeCap_Allowed(double amount, double volume)
    {
        SizingResult result = SizeAmount(D.Recovery, amount, 1.0988, 1.1024);

        Assert.True(result.IsAllowed, result.Detail);
        Assert.Equal(volume, result.VolumeUnits);
        Assert.Equal(amount, result.RiskAmount);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void SizeAmount_TwelvePipStopAbove3000000Units_SkippedNotReduced()
    {
        SizingResult result = SizeAmount(D.Recovery, 3601.2, 1.0988, 1.1024);

        Assert.Equal(RiskReason.VolumeAboveCap, result.Reason);
        Assert.Equal(3001000.0, result.Measured);
        Assert.Equal(3000000.0, result.Limit);
        Assert.Equal(0.0, result.VolumeUnits);
    }

    [Theory]
    [Trait("Req", "RSK-016")]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SizeAmount_AmountNotPositiveAndFinite_InvalidInput(double amount)
    {
        SizingResult result = SizeAmount(D.Recovery, amount);

        Assert.Equal(RiskReason.InvalidInput, result.Reason);
        Assert.Equal("risk amount must be a finite number greater than 0", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void SizeAmount_NullArguments_Throw()
    {
        var sizer = new PositionSizer(D.Recovery);
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, 1.098, 1.104);

        Assert.Throws<ArgumentNullException>(() => sizer.SizeAmount(null!, plan, RiskData.EurUsd, 100.0));
        Assert.Throws<ArgumentNullException>(() => sizer.SizeAmount(RiskData.Account(D.Capital), null!, RiskData.EurUsd, 100.0));
        Assert.Throws<ArgumentNullException>(() => sizer.SizeAmount(RiskData.Account(D.Capital), plan, null!, 100.0));
        Assert.Equal(RiskReason.InvalidInput, sizer.SizeAmount(RiskData.Account(0.0), plan, RiskData.EurUsd, 100.0).Reason);
    }
}
