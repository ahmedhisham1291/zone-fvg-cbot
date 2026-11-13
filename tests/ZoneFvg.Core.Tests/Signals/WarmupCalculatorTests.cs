using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Warm-up tests (SIG-020). The formulas, from the WP01 prompt:
/// <code>
/// D1  = 4 x BiasEmaPeriod + 1
/// H4  = MaxZoneAgeBars + 100 + 2 x SwingStrength + MaxImpulseBars + MaxBaseBars + AtrPeriod + 2
/// M15 = (MaxZoneAgeBars + 1) x 16 + ConfirmWindowBars + AtrPeriod + 2
/// </code>
/// With the defaults (BiasEmaPeriod 50, MaxZoneAgeBars 120, SwingStrength 3, MaxImpulseBars 3,
/// MaxBaseBars 3, AtrPeriod 14, ConfirmWindowBars 16):
/// <code>
/// D1  = 4 x 50 + 1                          = 201
/// H4  = 120 + 100 + 6 + 3 + 3 + 14 + 2      = 248
/// M15 = 121 x 16 + 16 + 14 + 2 = 1936 + 32  = 1968
/// </code>
/// </summary>
public class WarmupCalculatorTests
{
    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_DefaultParameters_ReturnsTheHandComputedBarCounts()
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(new SignalParameters());

        Assert.Equal(3, warmup.Count);
        Assert.Equal(201, warmup[TimeFrame.D1]);
        Assert.Equal(248, warmup[TimeFrame.H4]);
        Assert.Equal(1968, warmup[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_DefaultParameters_StaysWithinTheSpecLimits()
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(new SignalParameters());

        // Spec 8.3 and SIG-020: at most 300 D1, 500 H4 and 3,000 M15 bars with default parameters.
        Assert.InRange(warmup[TimeFrame.D1], 1, 300);
        Assert.InRange(warmup[TimeFrame.H4], 1, 500);
        Assert.InRange(warmup[TimeFrame.M15], 1, 3000);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_BiasEmaPeriod200_D1RequirementGrowsBeyondTheDefaultLimit()
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(new SignalParameters { BiasEmaPeriod = 200 });

        // D1 = 4 x 200 + 1 = 801, which is more than the 300 bars loaded by default (spec 8.3:
        // "loading more history if needed"). H4 and M15 do not depend on the EMA period.
        Assert.Equal(801, warmup[TimeFrame.D1]);
        Assert.Equal(248, warmup[TimeFrame.H4]);
        Assert.Equal(1968, warmup[TimeFrame.M15]);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData("BiasEmaPeriod", 100.0, 401, 248, 1968)] // D1 = 4 x 100 + 1
    [InlineData("MaxZoneAgeBars", 121.0, 201, 249, 1984)] // H4 + 1; M15 = 122 x 16 + 32 = 1952 + 32
    [InlineData("SwingStrength", 4.0, 201, 250, 1968)] // H4 + 2 (one more bar on each side)
    [InlineData("MaxImpulseBars", 4.0, 201, 249, 1968)] // H4 + 1
    [InlineData("MaxBaseBars", 4.0, 201, 249, 1968)] // H4 + 1
    [InlineData("AtrPeriod", 15.0, 201, 249, 1969)] // H4 + 1 and M15 + 1
    [InlineData("ConfirmWindowBars", 17.0, 201, 248, 1969)] // M15 + 1
    public void For_OneParameterChanged_ChangesOnlyTheTimeFramesThatUseIt(string parameter, double value, int expectedD1, int expectedH4, int expectedM15)
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(ParameterSets.With(parameter, value));

        Assert.Equal(expectedD1, warmup[TimeFrame.D1]);
        Assert.Equal(expectedH4, warmup[TimeFrame.H4]);
        Assert.Equal(expectedM15, warmup[TimeFrame.M15]);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData("ImpulseAtrMult", 3.0)]
    [InlineData("MinZoneAtr", 0.3)]
    [InlineData("MaxZoneAtr", 2.0)]
    [InlineData("MinFvgAtr", 0.45)]
    [InlineData("EntryLevelPct", 100.0)]
    [InlineData("StopBufferAtr", 0.2)]
    [InlineData("RewardRisk", 3.0)]
    public void For_ParameterThatIsNotABarCount_DoesNotChangeTheRequirement(string parameter, double value)
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(ParameterSets.With(parameter, value));

        Assert.Equal(201, warmup[TimeFrame.D1]);
        Assert.Equal(248, warmup[TimeFrame.H4]);
        Assert.Equal(1968, warmup[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_SmallestParameters_ReturnsTheHandComputedBarCounts()
    {
        var smallest = new SignalParameters
        {
            BiasEmaPeriod = 1,
            AtrPeriod = 1,
            SwingStrength = 1,
            MaxImpulseBars = 1,
            MaxBaseBars = 1,
            MaxZoneAgeBars = 1,
            ConfirmWindowBars = 1,
        };

        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(smallest);

        // D1 = 4 x 1 + 1 = 5; H4 = 1 + 100 + 2 + 1 + 1 + 1 + 2 = 108; M15 = 2 x 16 + 1 + 1 + 2 = 36.
        Assert.Equal(5, warmup[TimeFrame.D1]);
        Assert.Equal(108, warmup[TimeFrame.H4]);
        Assert.Equal(36, warmup[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_LargestAllowedParameters_DoesNotOverflow()
    {
        var largest = new SignalParameters
        {
            BiasEmaPeriod = 10000,
            AtrPeriod = 10000,
            SwingStrength = 10000,
            MaxImpulseBars = 10000,
            MaxBaseBars = 10000,
            MaxZoneAgeBars = 10000,
            ConfirmWindowBars = 10000,
        };

        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(largest);

        // D1 = 4 x 10000 + 1 = 40001.
        // H4 = 10000 + 100 + 20000 + 10000 + 10000 + 10000 + 2 = 60102.
        // M15 = 10001 x 16 + 10000 + 10000 + 2 = 160016 + 20002 = 180018.
        Assert.Equal(40001, warmup[TimeFrame.D1]);
        Assert.Equal(60102, warmup[TimeFrame.H4]);
        Assert.Equal(180018, warmup[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_SameParameters_ReturnsEqualRequirementsEachTime()
    {
        IReadOnlyDictionary<TimeFrame, int> first = WarmupCalculator.For(new SignalParameters());
        IReadOnlyDictionary<TimeFrame, int> second = WarmupCalculator.For(new SignalParameters());

        foreach (TimeFrame timeFrame in Enum.GetValues<TimeFrame>())
        {
            Assert.Equal(first[timeFrame], second[timeFrame]);
        }
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void M15BarsPerH4Bar_IsSixteen()
    {
        // One H4 bar spans 240 minutes = 16 bars of 15 minutes.
        int barsPerH4 = WarmupCalculator.M15BarsPerH4Bar;

        Assert.Equal(16, barsPerH4);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_InvalidParameters_ThrowsAndNamesTheParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WarmupCalculator.For(new SignalParameters { AtrPeriod = 0 }));

        Assert.Equal("AtrPeriod", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void For_NullParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>("parameters", () => WarmupCalculator.For(null!));
    }
}
