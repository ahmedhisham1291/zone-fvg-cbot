using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-001: riskAmount = balance x RiskPercent / 100; volume = riskAmount / (stopPips x
/// pipValuePerUnit), rounded down to the volume step; below the minimum volume the trade is
/// skipped. RSK-002: skip when the computed risk exceeds 0.55% of balance, the volume exceeds
/// MaxVolumeUnits, or the stop or target is on the wrong side of entry.
/// </summary>
/// <remarks>
/// Prices such as 1.10000 - 1.09800 are not exact in binary floating point (the distance comes
/// out as 20.000000000000018 pips), so money and volume are compared with a small tolerance.
/// </remarks>
public class PositionSizerTests
{
    private const double Money = 1e-6;
    private const double Units = 1e-6;

    private static PositionSizer Sizer(RiskParameters? parameters = null)
    {
        return new PositionSizer(parameters ?? RiskData.Parameters);
    }

    private static TradePlan Buy(string symbol, double entry, double stop, double target)
    {
        return new TradePlan(symbol, Side.Buy, entry, stop, target);
    }

    private static TradePlan Sell(string symbol, double entry, double stop, double target)
    {
        return new TradePlan(symbol, Side.Sell, entry, stop, target);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_SpecExample10000Balance20Pips_Is25000Units()
    {
        // Spec 4.1: risk = 10,000 x 0.5% = 50 USD; volume = 50 / (20 x 0.0001) = 25,000 units (0.25 lot).
        SizingResult result = Sizer().Size(RiskData.Account(10000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(RiskReason.None, result.Reason);
        Assert.Equal(25000.0, result.VolumeUnits, Units);
        Assert.Equal(50.0, result.RiskAmount, Money);
        Assert.Equal(50.0, result.RiskMoney, Money);
        Assert.Equal(20.0, result.StopPips, 1e-9);
        Assert.False(result.AlertRequired);
        Assert.Equal(string.Empty, result.Detail);
        Assert.Equal(0.0, result.Measured);
        Assert.Equal(0.0, result.Limit);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_SellMirrorOfTheSpecExample_Is25000Units()
    {
        // Sell at 1.10000 with the stop 20 pips above: the same 25,000 units.
        SizingResult result = Sizer().Size(RiskData.Account(10000.0), Sell("EURUSD", 1.10000, 1.10200, 1.09600), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(25000.0, result.VolumeUnits, Units);
        Assert.Equal(50.0, result.RiskMoney, Money);
    }

    [Theory]
    [Trait("Req", "RSK-001")]
    // Spec 4.1 table at 100,000 USD: risk 500 USD.
    // 12 pips: 500 / (12 x 0.0001) = 416,666.67 -> rounded down to 416,000 (4.16 lots); risk 416,000 x 12 x 0.0001 = 499.20.
    [InlineData(1.09880, 416000.0, 499.2)]
    // 20 pips: 500 / (20 x 0.0001) = 250,000 (2.5 lots); risk 500.
    [InlineData(1.09800, 250000.0, 500.0)]
    // 50 pips: 500 / (50 x 0.0001) = 100,000 (1 lot); risk 500.
    [InlineData(1.09500, 100000.0, 500.0)]
    public void Size_SpecTableAt100000_MatchesTheTable(double stop, double expectedVolume, double expectedRisk)
    {
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, stop, 1.13000), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(expectedVolume, result.VolumeUnits, Units);
        Assert.Equal(expectedRisk, result.RiskMoney, Money);
        Assert.Equal(500.0, result.RiskAmount, Money);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_UsesCurrentBalanceNotInitialCapital()
    {
        // Balance 50,000: risk 250; 250 / (20 x 0.0001) = 125,000 units.
        SizingResult result = Sizer().Size(RiskData.Account(50000.0, 120000.0, 50000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(125000.0, result.VolumeUnits, Units);
        Assert.Equal(250.0, result.RiskAmount, Money);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_UsdJpyStylePipValue_RoundsDownToTheStep()
    {
        // USDJPY: pip size 0.01, pip value 0.00008 USD per unit. Stop 125.000 - 124.800 = 20 pips.
        // 500 / (20 x 0.00008) = 500 / 0.0016 = 312,500 -> rounded down to 312,000; risk 312,000 x 20 x 0.00008 = 499.20.
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Buy("USDJPY", 125.000, 124.800, 125.400), RiskData.UsdJpy);

        Assert.True(result.IsAllowed);
        Assert.Equal(312000.0, result.VolumeUnits, Units);
        Assert.Equal(499.2, result.RiskMoney, Money);
        Assert.Equal(20.0, result.StopPips, 1e-9);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_BelowMinimumVolume_IsSkipped()
    {
        // Spec 4.1: balance 599, 30-pip stop: risk 2.995; 2.995 / (30 x 0.0001) = 998.33 -> 0 after rounding down,
        // below the 1,000-unit minimum. Never rounded up.
        SizingResult result = Sizer().Size(RiskData.Account(599.0), Buy("EURUSD", 1.10000, 1.09700, 1.10600), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.BelowMinimumVolume, result.Reason);
        Assert.Equal(0.0, result.VolumeUnits);
        Assert.Equal(0.0, result.Measured, Units);
        Assert.Equal(1000.0, result.Limit);
        Assert.False(result.AlertRequired);
        Assert.Equal("volume 0 below minimum 1000", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_ExactlyTheMinimumVolume_IsAllowed()
    {
        // Spec 4.1: balance 600, 30-pip stop: risk 3; 3 / (30 x 0.0001) = 1,000 units, exactly the minimum.
        SizingResult result = Sizer().Size(RiskData.Account(600.0), Buy("EURUSD", 1.10000, 1.09700, 1.10600), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(1000.0, result.VolumeUnits, Units);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_WholeStepsUpToFloatingPointNoise_KeepsTheFullMultiple()
    {
        // Symbol with pip size 1, pip value 0.1, step 0.1, minimum 0.1. Balance 60: risk 60 x 0.5 / 100 = 0.3;
        // stop 110 - 100 = 10 pips; volume = 0.3 / (10 x 0.1) = 0.3, which is exactly 3 steps of 0.1.
        // In binary floating point 0.3 / 0.1 is 2.9999999999999996; a plain floor would give 0.2.
        SymbolInfo fine = Fine();
        SizingResult result = Sizer().Size(RiskData.Account(60.0), Buy("FINE", 110.0, 100.0, 130.0), fine);

        Assert.True(result.IsAllowed);
        Assert.Equal(0.3, result.VolumeUnits, 1e-12);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_OneMillionthOfAStepBelowAMultiple_RoundsDownToTheMultipleBelow()
    {
        // Balance 59.99998: risk 0.2999999 = 3 steps of 0.1 minus one millionth of a step (1e-7).
        // 2.999999 steps round down to 2 steps: 0.2.
        SizingResult result = Sizer().Size(RiskData.Account(59.99998), Buy("FINE", 110.0, 100.0, 130.0), Fine());

        Assert.True(result.IsAllowed);
        Assert.Equal(0.2, result.VolumeUnits, 1e-12);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_SkippedResult_CarriesNoVolume()
    {
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.10200, 1.10400), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(0.0, result.VolumeUnits);
        Assert.Equal(0.0, result.RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_ComputedRisk056Percent_IsSkippedWithAlert()
    {
        // RiskPercent 0.56 on 100,000: 560 / (20 x 0.0001) = 280,000 units; risk 560 > 0.55% x 100,000 = 550.
        RiskParameters parameters = RiskData.Parameters with { RiskPercent = 0.56 };
        SizingResult result = Sizer(parameters).Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.RiskAboveSanityCap, result.Reason);
        Assert.Equal(560.0, result.Measured, Money);
        Assert.Equal(550.0, result.Limit, Money);
        Assert.True(result.AlertRequired);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_ComputedRiskExactly055Percent_IsAllowed()
    {
        // RiskPercent 0.55 on 100,000: 550 / (20 x 0.0001) = 275,000 units; risk 550, exactly the cap.
        RiskParameters parameters = RiskData.Parameters with { RiskPercent = 0.55 };
        SizingResult result = Sizer(parameters).Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(275000.0, result.VolumeUnits, Units);
        Assert.Equal(550.0, result.RiskMoney, Money);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_ComputedRiskJustAbove055Percent_IsSkipped()
    {
        // Volume step 1 so rounding cannot hide the excess. RiskPercent 0.5501 on 100,000: risk 550.10;
        // 550.10 / (20 x 0.0001) = 275,050 units; risk 550.10 > 550.
        RiskParameters parameters = RiskData.Parameters with { RiskPercent = 0.5501 };
        SymbolInfo stepOne = new("EURUSD", "EUR", "USD", 0.0001, 0.0001, 1.0, 1.0, 10000000.0, 0.00001, true, 0.0);
        SizingResult result = Sizer(parameters).Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), stepOne);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.RiskAboveSanityCap, result.Reason);
        Assert.Equal(550.1, result.Measured, Money);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_VolumeExactlyMaxVolumeUnits_IsAllowed()
    {
        // 10-pip stop on 100,000: 500 / (10 x 0.0001) = 500,000 units, exactly MaxVolumeUnits.
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.09900, 1.10200), RiskData.EurUsd);

        Assert.True(result.IsAllowed);
        Assert.Equal(500000.0, result.VolumeUnits, Units);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_VolumeOneStepAboveMaxVolumeUnits_IsSkippedWithAlert()
    {
        // Balance 100,200: risk 501; 501 / (10 x 0.0001) = 501,000 units > 500,000.
        SizingResult result = Sizer().Size(RiskData.Account(100200.0), Buy("EURUSD", 1.10000, 1.09900, 1.10200), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.VolumeAboveCap, result.Reason);
        Assert.Equal(501000.0, result.Measured, Units);
        Assert.Equal(500000.0, result.Limit);
        Assert.True(result.AlertRequired);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_SellVolumeAboveMaxVolumeUnits_IsSkipped()
    {
        // 8-pip stop on a sell: 500 / (8 x 0.0001) = 625,000 units > 500,000.
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Sell("EURUSD", 1.10000, 1.10080, 1.09840), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.VolumeAboveCap, result.Reason);
        Assert.Equal(625000.0, result.Measured, Units);
    }

    [Theory]
    [Trait("Req", "RSK-002")]
    [InlineData(Side.Buy, 1.10200)] // buy stop above entry
    [InlineData(Side.Buy, 1.10000)] // buy stop at entry
    [InlineData(Side.Sell, 1.09800)] // sell stop below entry
    [InlineData(Side.Sell, 1.10000)] // sell stop at entry
    public void Size_StopNotOnTheLossSideOfEntry_IsSkippedWithAlert(Side side, double stop)
    {
        double target = side == Side.Buy ? 1.10400 : 1.09600;
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), new TradePlan("EURUSD", side, 1.10000, stop, target), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.StopOnWrongSide, result.Reason);
        Assert.True(result.AlertRequired);
    }

    [Theory]
    [Trait("Req", "RSK-002")]
    [InlineData(Side.Buy, 1.09900)] // buy target below entry
    [InlineData(Side.Buy, 1.10000)] // buy target at entry
    [InlineData(Side.Sell, 1.10100)] // sell target above entry
    [InlineData(Side.Sell, 1.10000)] // sell target at entry
    public void Size_TargetNotOnTheProfitSideOfEntry_IsSkippedWithAlert(Side side, double target)
    {
        double stop = side == Side.Buy ? 1.09800 : 1.10200;
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), new TradePlan("EURUSD", side, 1.10000, stop, target), RiskData.EurUsd);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.TargetOnWrongSide, result.Reason);
        Assert.True(result.AlertRequired);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_InvalidAccountOrPlan_IsSkippedAsInvalidInput()
    {
        PositionSizer sizer = Sizer();
        TradePlan plan = Buy("EURUSD", 1.10000, 1.09800, 1.10400);

        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(0.0), plan, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(double.NaN), plan, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { Entry = double.NaN }, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { Entry = 0.0 }, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { StopLoss = double.PositiveInfinity }, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { TakeProfit = -1.0 }, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { Side = (Side)7 }, RiskData.EurUsd).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { Symbol = "GBPUSD" }, RiskData.EurUsd).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_InvalidSymbolData_IsSkippedAsInvalidInput()
    {
        PositionSizer sizer = Sizer();
        TradePlan plan = Buy("EURUSD", 1.10000, 1.09800, 1.10400);
        SymbolInfo valid = RiskData.EurUsd;

        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { PipSize = 0.0 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { PipValuePerUnit = double.NaN }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { VolumeMinUnits = 0.0 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { VolumeStepUnits = -1000.0 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { BaseCurrency = "" }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { QuoteCurrency = "" }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan, valid with { RoundTripCommissionPerUnit = -0.00001 }).Reason);
        Assert.Equal(RiskReason.InvalidInput, sizer.Size(RiskData.Account(100000.0), plan with { Symbol = "" }, valid with { Name = "" }).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_VolumeThatIsNotFinite_IsSkippedAsInvalidInput()
    {
        // A pip value so small that risk / (stopPips x pipValue) overflows to infinity.
        SymbolInfo tiny = RiskData.EurUsd with { PipValuePerUnit = 1e-320 };
        SizingResult result = Sizer().Size(RiskData.Account(100000.0), Buy("EURUSD", 1.10000, 1.09800, 1.10400), tiny);

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.InvalidInput, result.Reason);
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Size_NullArguments_Throw()
    {
        PositionSizer sizer = Sizer();
        TradePlan plan = Buy("EURUSD", 1.10000, 1.09800, 1.10400);

        Assert.Throws<ArgumentNullException>(() => sizer.Size(null!, plan, RiskData.EurUsd));
        Assert.Throws<ArgumentNullException>(() => sizer.Size(RiskData.Account(100000.0), null!, RiskData.EurUsd));
        Assert.Throws<ArgumentNullException>(() => sizer.Size(RiskData.Account(100000.0), plan, null!));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void Constructor_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PositionSizer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionSizer(new RiskParameters()));
    }

    private static SymbolInfo Fine()
    {
        return new SymbolInfo("FINE", "AAA", "USD", 1.0, 0.1, 0.1, 0.1, 1000.0, 0.0, true, 0.0);
    }
}
