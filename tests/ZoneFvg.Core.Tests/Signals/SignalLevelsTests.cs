using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Signal levels (SIG-017), worked by hand.
/// </summary>
/// <remarks>
/// <para>Buy: demand zone proximal 100.2, distal 99.5; bullish gap from high(c3) 100.4 to low(c1)
/// 100.6, height 0.2; ATR(M15) 0.4, so the stop buffer is 0.1 x 0.4 = 0.04; RewardRisk 2.0.
/// Entry = 100.6 - pct/100 x 0.2: 100.6 at 0 %, 100.5 at 50 %, 100.4 at 100 %.
/// With the extreme since touch 99.9 (above the distal line) the stop is
/// min(99.5, 99.9) - 0.04 = 99.46; the target is entry + 2 x (entry - 99.46).</para>
/// <para>Sell, the mirror image: supply zone proximal 99.8, distal 100.5; bearish gap from
/// high(c1) 99.4 to low(c3) 99.6. Entry = 99.4 + pct/100 x 0.2. With the extreme since touch
/// 100.1 the stop is max(100.5, 100.1) + 0.04 = 100.54; the target is entry - 2 x (100.54 - entry).</para>
/// <para>c3, c2 and c1 open at 10:00, 10:15 and 10:30 UTC on 2026-01-06, so c1 closes at 10:45.</para>
/// </remarks>
public class SignalLevelsTests
{
    private static readonly DateTime C3Open = new(2026, 1, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime C1Close = new(2026, 1, 6, 10, 45, 0, DateTimeKind.Utc);
    private static readonly SignalParameters Defaults = new();

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(0.0, 100.6, 99.46, 102.88)] // risk 100.6 - 99.46 = 1.14; target 100.6 + 2.28
    [InlineData(50.0, 100.5, 99.46, 102.58)] // risk 1.04; target 100.5 + 2.08
    [InlineData(100.0, 100.4, 99.46, 102.28)] // risk 0.94; target 100.4 + 1.88
    public void BuildSignal_Buy_LevelsFromTheWorkedExample(double entryLevelPct, double entry, double stop, double target)
    {
        SignalParameters parameters = Defaults with { EntryLevelPct = entryLevelPct };

        Signal signal = FvgDetector.BuildSignal(Demand(99.9), BullishGap(), 0.4, parameters, false);

        Assert.Equal(Side.Buy, signal.Side);
        AssertLevels(signal, entry, stop, target);
    }

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(0.0, 99.4, 100.54, 97.12)] // risk 100.54 - 99.4 = 1.14; target 99.4 - 2.28
    [InlineData(50.0, 99.5, 100.54, 97.42)] // risk 1.04; target 99.5 - 2.08
    [InlineData(100.0, 99.6, 100.54, 97.72)] // risk 0.94; target 99.6 - 1.88
    public void BuildSignal_Sell_LevelsFromTheWorkedExample(double entryLevelPct, double entry, double stop, double target)
    {
        SignalParameters parameters = Defaults with { EntryLevelPct = entryLevelPct };

        Signal signal = FvgDetector.BuildSignal(Supply(100.1), BearishGap(), 0.4, parameters, false);

        Assert.Equal(Side.Sell, signal.Side);
        AssertLevels(signal, entry, stop, target);
    }

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(99.9, 99.46)] // extreme above distal: min(99.5, 99.9) = 99.5, minus 0.04
    [InlineData(99.5, 99.46)] // extreme equal to distal
    [InlineData(99.3, 99.26)] // extreme below distal: min(99.5, 99.3) = 99.3, minus 0.04
    public void BuildSignal_Buy_StopUsesTheLowerOfDistalAndExtreme(double extremeSinceTouch, double stop)
    {
        Signal signal = FvgDetector.BuildSignal(Demand(extremeSinceTouch), BullishGap(), 0.4, Defaults, false);

        // Entry 100.5 at 50 %; target 100.5 + 2 x (100.5 - stop).
        AssertLevels(signal, 100.5, stop, 100.5 + (2.0 * (100.5 - stop)));
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_Buy_ExtremeBelowDistal_WorkedTarget()
    {
        // Stop 99.3 - 0.04 = 99.26; risk 100.5 - 99.26 = 1.24; target 100.5 + 2.48 = 102.98.
        Signal signal = FvgDetector.BuildSignal(Demand(99.3), BullishGap(), 0.4, Defaults, false);

        AssertLevels(signal, 100.5, 99.26, 102.98);
    }

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(100.1, 100.54)] // extreme below distal: max(100.5, 100.1) = 100.5, plus 0.04
    [InlineData(100.7, 100.74)] // extreme above distal: max(100.5, 100.7) = 100.7, plus 0.04
    public void BuildSignal_Sell_StopUsesTheHigherOfDistalAndExtreme(double extremeSinceTouch, double stop)
    {
        Signal signal = FvgDetector.BuildSignal(Supply(extremeSinceTouch), BearishGap(), 0.4, Defaults, false);

        AssertLevels(signal, 99.5, stop, 99.5 - (2.0 * (stop - 99.5)));
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_Sell_ExtremeAboveDistal_WorkedTarget()
    {
        // Stop 100.7 + 0.04 = 100.74; risk 100.74 - 99.5 = 1.24; target 99.5 - 2.48 = 97.02.
        Signal signal = FvgDetector.BuildSignal(Supply(100.7), BearishGap(), 0.4, Defaults, false);

        AssertLevels(signal, 99.5, 100.74, 97.02);
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_CustomBufferAndRewardRisk_AreApplied()
    {
        // StopBufferAtr 0.25 x ATR 0.4 = 0.1: stop 99.5 - 0.1 = 99.4; risk 100.5 - 99.4 = 1.1;
        // RewardRisk 1.5: target 100.5 + 1.65 = 102.15.
        SignalParameters parameters = Defaults with { StopBufferAtr = 0.25, RewardRisk = 1.5 };

        Signal signal = FvgDetector.BuildSignal(Demand(99.9), BullishGap(), 0.4, parameters, false);

        AssertLevels(signal, 100.5, 99.4, 102.15);
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_ZeroBuffer_StopAtTheLowerLine()
    {
        // StopBufferAtr 0: stop 99.5; risk 1.0; target 102.5.
        SignalParameters parameters = Defaults with { StopBufferAtr = 0.0 };

        Signal signal = FvgDetector.BuildSignal(Demand(99.9), BullishGap(), 0.4, parameters, false);

        AssertLevels(signal, 100.5, 99.5, 102.5);
    }

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildSignal_CarriesZoneGapTimesAndReplayFlag(bool fromReplay)
    {
        Fvg gap = BullishGap();

        Signal signal = FvgDetector.BuildSignal(Demand(99.9), gap, 0.4, Defaults, fromReplay);

        Assert.Equal("D-20260105-0800", signal.ZoneId);
        Assert.Same(gap, signal.Fvg);
        Assert.Equal(C3Open, signal.Fvg.C3OpenUtc);
        Assert.Equal(C3Open.AddMinutes(15), signal.Fvg.C2OpenUtc);
        Assert.Equal(C3Open.AddMinutes(30), signal.Fvg.C1OpenUtc);
        Assert.Equal(C1Close, signal.ConfirmationCloseUtc);
        Assert.Equal(DateTimeKind.Utc, signal.ConfirmationCloseUtc.Kind);
        Assert.Equal(fromReplay, signal.FromReplay);
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_StopDistanceInPrice_IsEntryMinusStop()
    {
        // The stop distance is not a separate field of the Signal contract; it is |entry - stop|:
        // 100.5 - 99.46 = 1.04 for the buy and 100.54 - 99.5 = 1.04 for the sell.
        Signal buy = FvgDetector.BuildSignal(Demand(99.9), BullishGap(), 0.4, Defaults, false);
        Signal sell = FvgDetector.BuildSignal(Supply(100.1), BearishGap(), 0.4, Defaults, false);

        Assert.True(PriceMath.AlmostEqual(1.04, buy.Entry - buy.Stop, PriceMath.DefaultTolerance));
        Assert.True(PriceMath.AlmostEqual(1.04, sell.Stop - sell.Entry, PriceMath.DefaultTolerance));
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_ZoneWithoutExtreme_Throws()
    {
        Zone untouched = Demand(99.9) with { ExtremeSinceTouch = null };

        Assert.Throws<ArgumentException>("zone", () => FvgDetector.BuildSignal(untouched, BullishGap(), 0.4, Defaults, false));
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_GapSideDoesNotMatchZoneType_Throws()
    {
        Assert.Throws<ArgumentException>("fvg", () => FvgDetector.BuildSignal(Demand(99.9), BearishGap(), 0.4, Defaults, false));
        Assert.Throws<ArgumentException>("fvg", () => FvgDetector.BuildSignal(Supply(100.1), BullishGap(), 0.4, Defaults, false));
    }

    [Theory]
    [Trait("Req", "SIG-017")]
    [InlineData(double.NaN)]
    [InlineData(-0.4)]
    [InlineData(double.PositiveInfinity)]
    public void BuildSignal_InvalidAtr_Throws(double atrM15)
    {
        Assert.Throws<ArgumentOutOfRangeException>("atrM15", () => FvgDetector.BuildSignal(Demand(99.9), BullishGap(), atrM15, Defaults, false));
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void BuildSignal_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("zone", () => FvgDetector.BuildSignal(null!, BullishGap(), 0.4, Defaults, false));
        Assert.Throws<ArgumentNullException>("fvg", () => FvgDetector.BuildSignal(Demand(99.9), null!, 0.4, Defaults, false));
        Assert.Throws<ArgumentNullException>("parameters", () => FvgDetector.BuildSignal(Demand(99.9), BullishGap(), 0.4, null!, false));
    }

    private static Zone Demand(double extremeSinceTouch)
    {
        return new Zone("D-20260105-0800", ZoneType.Demand, 100.2, 99.5, C3Open, 1.0, ZoneState.Touched, C3Open, extremeSinceTouch);
    }

    private static Zone Supply(double extremeSinceTouch)
    {
        return new Zone("S-20260105-0800", ZoneType.Supply, 99.8, 100.5, C3Open, 1.0, ZoneState.Touched, C3Open, extremeSinceTouch);
    }

    private static Fvg BullishGap()
    {
        return new Fvg(Side.Buy, 100.6, 100.4, C3Open, C3Open.AddMinutes(15), C3Open.AddMinutes(30));
    }

    private static Fvg BearishGap()
    {
        return new Fvg(Side.Sell, 99.6, 99.4, C3Open, C3Open.AddMinutes(15), C3Open.AddMinutes(30));
    }

    private static void AssertLevels(Signal signal, double entry, double stop, double target)
    {
        AssertNear(entry, signal.Entry, "Entry");
        AssertNear(stop, signal.Stop, "Stop");
        AssertNear(target, signal.Target, "Target");
    }

    private static void AssertNear(double expected, double actual, string name)
    {
        string message = name + ": expected " + expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + " but was " + actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(PriceMath.AlmostEqual(expected, actual, PriceMath.DefaultTolerance), message);
    }
}
