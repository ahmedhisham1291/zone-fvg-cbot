using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bias tests (SIG-005) with EMA period 3: the window is 12 daily closes and alpha is 0.5.
/// The first eleven closes are fixed. After them the EMA stands at 19.4296875:
/// <code>
/// seed = (10 + 11 + 12) / 3 = 11
/// 13 -> 12, 15 -> 13.5, 14 -> 13.75, 16 -> 14.875, 18 -> 16.4375,
/// 17 -> 16.71875, 19 -> 17.859375, 21 -> 19.4296875
/// </code>
/// so for a twelfth close X the EMA is 0.5 x X + 9.71484375, and
/// close - EMA = 0.5 x (X - 19.4296875).
/// </summary>
public class BiasCalculatorTests
{
    private const int Period = 3;
    private const double EmaBeforeLastClose = 19.4296875;

    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly double[] FirstEleven = { 10.0, 11.0, 12.0, 13.0, 15.0, 14.0, 16.0, 18.0, 17.0, 19.0, 21.0 };

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_LastCloseAboveEma_IsLong()
    {
        // X = 20: EMA = 10 + 9.71484375 = 19.71484375, and 20 is above it.
        Assert.Equal(Bias.Long, BiasCalculator.Compute(DailyEndingWith(20.0), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_LastCloseBelowEma_IsShort()
    {
        // X = 19: EMA = 9.5 + 9.71484375 = 19.21484375, and 19 is below it.
        Assert.Equal(Bias.Short, BiasCalculator.Compute(DailyEndingWith(19.0), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_LastCloseExactlyEqualToEma_IsNeutral()
    {
        // X = 19.4296875: EMA = 9.71484375 + 9.71484375 = 19.4296875 exactly, the same as the close.
        Assert.Equal(Bias.Neutral, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_LastCloseOneTickAwayFromEquality_IsLongOrShort()
    {
        // X one tick (0.00001) above or below the equality point: close - EMA = +/-0.000005.
        Assert.Equal(Bias.Long, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose + 0.00001), Period));
        Assert.Equal(Bias.Short, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose - 0.00001), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_DifferenceInsideThePriceTolerance_IsNeutral()
    {
        // X = equality point +/- 1e-9: close - EMA = +/-5e-10, inside the PriceMath tolerance
        // of 1e-9, so close and EMA count as equal.
        Assert.Equal(Bias.Neutral, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose + 1e-9), Period));
        Assert.Equal(Bias.Neutral, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose - 1e-9), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_DifferenceJustOutsideThePriceTolerance_IsLongOrShort()
    {
        // X = equality point +/- 4e-9: close - EMA = +/-2e-9, outside the tolerance of 1e-9.
        Assert.Equal(Bias.Long, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose + 4e-9), Period));
        Assert.Equal(Bias.Short, BiasCalculator.Compute(DailyEndingWith(EmaBeforeLastClose - 4e-9), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_WindowOneBarShort_IsNeutral()
    {
        // Eleven closes: EMA(3) needs twelve. The last close (21) is far above every average,
        // but without a full window the bias must stay Neutral.
        Assert.Equal(Bias.Neutral, BiasCalculator.Compute(Daily(FirstEleven), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_NoBars_IsNeutral()
    {
        Assert.Equal(Bias.Neutral, BiasCalculator.Compute(new List<Bar>(), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_OlderBarsOutsideTheWindow_DoNotChangeTheBias()
    {
        // Five very high older closes come first. An EMA over all bars would sit far above the
        // last close (Short); the EMA over the last 12 bars is 19.71484375, below the close of 20 (Long).
        var closes = new List<double> { 100000.0, 100000.0, 100000.0, 100000.0, 100000.0 };
        closes.AddRange(FirstEleven);
        closes.Add(20.0);

        Assert.Equal(Bias.Long, BiasCalculator.Compute(Daily(closes.ToArray()), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_MirroredBars_SwapLongAndShort()
    {
        IReadOnlyList<Bar> longBars = DailyEndingWith(20.0);
        IReadOnlyList<Bar> shortBars = DailyEndingWith(19.0);

        Assert.Equal(Bias.Short, BiasCalculator.Compute(longBars.Mirror(100.0), Period));
        Assert.Equal(Bias.Long, BiasCalculator.Compute(shortBars.Mirror(100.0), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_FromStoredBars_ChangesOnlyWhenAD1BarCloses()
    {
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = Period });
        foreach (Bar bar in DailyEndingWith(20.0))
        {
            store.Add(TimeFrame.D1, bar);
        }

        Assert.Equal(Bias.Long, BiasCalculator.Compute(store.Bars(TimeFrame.D1), Period));

        // H4 and M15 bars close far below every daily value. They are not D1 closes, so the bias stays Long.
        DateTime intraday = Start.AddDays(12.0);
        foreach (Bar bar in new BarBuilder(TimeFrame.H4).At(intraday).Closes(1.0, 1.0, 1.0, 1.0, 1.0, 1.0).Build())
        {
            store.Add(TimeFrame.H4, bar);
        }

        foreach (Bar bar in new BarBuilder(TimeFrame.M15).At(intraday).Closes(1.0, 1.0, 1.0, 1.0, 1.0, 1.0).Build())
        {
            store.Add(TimeFrame.M15, bar);
        }

        Assert.Equal(Bias.Long, BiasCalculator.Compute(store.Bars(TimeFrame.D1), Period));

        // The next D1 bar closes at 5. The window is now the closes
        // 11, 12, 13, 15, 14, 16, 18, 17, 19, 21, 20, 5:
        // seed = (11 + 12 + 13) / 3 = 12; 15 -> 13.5; 14 -> 13.75; 16 -> 14.875; 18 -> 16.4375;
        // 17 -> 16.71875; 19 -> 17.859375; 21 -> 19.4296875; 20 -> 19.71484375; 5 -> 12.357421875.
        // The close (5) is below the EMA (12.357421875): Short.
        store.Add(TimeFrame.D1, new Bar(Start.AddDays(12.0), 5.0, 5.0, 5.0, 5.0));

        Assert.Equal(Bias.Short, BiasCalculator.Compute(store.Bars(TimeFrame.D1), Period));
    }

    [Fact]
    [Trait("Req", "SIG-005")]
    public void Compute_NullBars_Throws()
    {
        Assert.Throws<ArgumentNullException>("d1Bars", () => BiasCalculator.Compute(null!, Period));
    }

    [Theory]
    [Trait("Req", "SIG-005")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_EmaPeriodBelowOne_Throws(int emaPeriod)
    {
        Assert.Throws<ArgumentOutOfRangeException>("emaPeriod", () => BiasCalculator.Compute(DailyEndingWith(20.0), emaPeriod));
    }

    private static IReadOnlyList<Bar> DailyEndingWith(double lastClose)
    {
        var closes = new List<double>(FirstEleven) { lastClose };
        return Daily(closes.ToArray());
    }

    private static IReadOnlyList<Bar> Daily(params double[] closes)
    {
        return new BarBuilder(TimeFrame.D1).At(Start).Closes(closes).Build();
    }
}
