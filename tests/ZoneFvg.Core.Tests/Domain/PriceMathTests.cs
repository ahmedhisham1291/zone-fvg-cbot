using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Boundary tests use values that binary floating point stores exactly (0.25, 0.5, 1.25, ...),
/// so "exactly at the tolerance" really is exact. Math.BitIncrement gives the next
/// representable number, which is the smallest possible step past a boundary.
/// </summary>
public class PriceMathTests
{
    private const double Quarter = 0.25;

    [Fact]
    public void DefaultTolerance_IsOneBillionth()
    {
        double tolerance = PriceMath.DefaultTolerance;

        Assert.Equal(1e-9, tolerance);
    }

    [Theory]
    [InlineData(1.0, 1.0, true)] // identical
    [InlineData(1.0, 1.125, true)] // inside the tolerance: |1.0 - 1.125| = 0.125
    [InlineData(1.0, 1.25, true)] // exactly at the tolerance: |1.0 - 1.25| = 0.25
    [InlineData(1.25, 1.0, true)] // exactly at the tolerance, other direction
    [InlineData(1.0, 1.5, false)] // outside: 0.5
    [InlineData(1.5, 1.0, false)] // outside, other direction
    [InlineData(-1.0, -1.25, true)] // negative values, exactly at the tolerance
    [InlineData(-0.125, 0.125, true)] // across zero: 0.25
    public void AlmostEqual_WithQuarterTolerance_IsInclusiveAtTheTolerance(double a, double b, bool expected)
    {
        Assert.Equal(expected, PriceMath.AlmostEqual(a, b, Quarter));
    }

    [Fact]
    public void AlmostEqual_SmallestStepPastTheTolerance_IsFalse()
    {
        // 1.25 + one unit in the last place: the difference to 1.0 is just above 0.25.
        double justOutside = Math.BitIncrement(1.25);

        Assert.False(PriceMath.AlmostEqual(1.0, justOutside, Quarter));
        Assert.False(PriceMath.AlmostEqual(justOutside, 1.0, Quarter));
    }

    [Fact]
    public void AlmostEqual_DefaultTolerance_AbsorbsFloatingPointNoiseButNotOneTick()
    {
        // 0.1 + 0.2 is 0.30000000000000004 in binary floating point.
        Assert.True(PriceMath.AlmostEqual(0.1 + 0.2, 0.3, PriceMath.DefaultTolerance));

        // One tick of a five-digit symbol (0.00001) is a real difference.
        Assert.False(PriceMath.AlmostEqual(1.10000, 1.10001, PriceMath.DefaultTolerance));
    }

    [Fact]
    public void AlmostEqual_DefaultToleranceBoundary_IsInclusive()
    {
        // 0 - 1e-9 is exactly -1e-9, so this difference equals the tolerance exactly.
        Assert.True(PriceMath.AlmostEqual(0.0, PriceMath.DefaultTolerance, PriceMath.DefaultTolerance));
        Assert.False(PriceMath.AlmostEqual(0.0, Math.BitIncrement(PriceMath.DefaultTolerance), PriceMath.DefaultTolerance));
    }

    [Fact]
    public void AlmostEqual_ZeroTolerance_IsExactComparison()
    {
        Assert.True(PriceMath.AlmostEqual(1.5, 1.5, 0.0));
        Assert.False(PriceMath.AlmostEqual(1.5, Math.BitIncrement(1.5), 0.0));
    }

    [Theory]
    [InlineData(1.5, 1.0, true)] // 0.5 above: beyond the tolerance
    [InlineData(1.25, 1.0, false)] // exactly the tolerance above: still equal, not greater
    [InlineData(1.125, 1.0, false)] // inside the tolerance
    [InlineData(1.0, 1.0, false)] // identical
    [InlineData(1.0, 1.5, false)] // below
    [InlineData(-1.0, -1.5, true)] // negative values: -1.0 is 0.5 above -1.5
    public void GreaterThan_WithQuarterTolerance_NeedsMoreThanTheTolerance(double a, double b, bool expected)
    {
        Assert.Equal(expected, PriceMath.GreaterThan(a, b, Quarter));
    }

    [Fact]
    public void GreaterThan_SmallestStepPastTheTolerance_IsTrue()
    {
        Assert.True(PriceMath.GreaterThan(Math.BitIncrement(1.25), 1.0, Quarter));
    }

    [Theory]
    [InlineData(1.0, 1.5, true)] // 0.5 below: beyond the tolerance
    [InlineData(1.0, 1.25, false)] // exactly the tolerance below: still equal, not less
    [InlineData(1.0, 1.125, false)] // inside the tolerance
    [InlineData(1.0, 1.0, false)] // identical
    [InlineData(1.5, 1.0, false)] // above
    [InlineData(-1.5, -1.0, true)] // negative values: -1.5 is 0.5 below -1.0
    public void LessThan_WithQuarterTolerance_NeedsMoreThanTheTolerance(double a, double b, bool expected)
    {
        Assert.Equal(expected, PriceMath.LessThan(a, b, Quarter));
    }

    [Fact]
    public void LessThan_SmallestStepPastTheTolerance_IsTrue()
    {
        Assert.True(PriceMath.LessThan(1.0, Math.BitIncrement(1.25), Quarter));
    }

    [Fact]
    public void GreaterThanAndLessThan_OneTickApartWithDefaultTolerance_SeeTheTick()
    {
        Assert.True(PriceMath.GreaterThan(1.10001, 1.10000, PriceMath.DefaultTolerance));
        Assert.False(PriceMath.GreaterThan(1.10000, 1.10001, PriceMath.DefaultTolerance));
        Assert.True(PriceMath.LessThan(1.10000, 1.10001, PriceMath.DefaultTolerance));
        Assert.False(PriceMath.LessThan(1.10001, 1.10000, PriceMath.DefaultTolerance));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.0, 1.125)]
    [InlineData(1.0, 1.25)]
    [InlineData(1.0, 1.5)]
    [InlineData(1.25, 1.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(-3.0, 3.0)]
    [InlineData(0.0, 0.0)]
    public void Comparisons_AnyPair_ExactlyOneOfEqualGreaterLessIsTrue(double a, double b)
    {
        int holding = 0;
        holding += PriceMath.AlmostEqual(a, b, Quarter) ? 1 : 0;
        holding += PriceMath.GreaterThan(a, b, Quarter) ? 1 : 0;
        holding += PriceMath.LessThan(a, b, Quarter) ? 1 : 0;

        Assert.Equal(1, holding);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Comparisons_FirstValueNotFinite_Throw(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>("a", () => PriceMath.AlmostEqual(bad, 1.0, Quarter));
        Assert.Throws<ArgumentOutOfRangeException>("a", () => PriceMath.GreaterThan(bad, 1.0, Quarter));
        Assert.Throws<ArgumentOutOfRangeException>("a", () => PriceMath.LessThan(bad, 1.0, Quarter));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Comparisons_SecondValueNotFinite_Throw(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>("b", () => PriceMath.AlmostEqual(1.0, bad, Quarter));
        Assert.Throws<ArgumentOutOfRangeException>("b", () => PriceMath.GreaterThan(1.0, bad, Quarter));
        Assert.Throws<ArgumentOutOfRangeException>("b", () => PriceMath.LessThan(1.0, bad, Quarter));
    }

    [Theory]
    [InlineData(-0.25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Comparisons_ToleranceNegativeOrNotFinite_Throw(double tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => PriceMath.AlmostEqual(1.0, 1.0, tolerance));
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => PriceMath.GreaterThan(1.0, 1.0, tolerance));
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => PriceMath.LessThan(1.0, 1.0, tolerance));
    }

    [Theory]
    [InlineData(123456.7, 1000.0, 123000.0)] // 123.4567 steps -> 123 whole steps
    [InlineData(3000.0, 1000.0, 3000.0)] // exactly 3 steps stays
    [InlineData(2999.999, 1000.0, 2000.0)] // just under 3 steps -> 2 steps
    [InlineData(3000.001, 1000.0, 3000.0)] // just over 3 steps -> 3 steps
    [InlineData(999.99, 1000.0, 0.0)] // less than one step -> nothing
    [InlineData(0.0, 1000.0, 0.0)] // zero stays zero
    [InlineData(7.75, 0.25, 7.75)] // 31 steps exactly
    [InlineData(7.8, 0.25, 7.75)] // 31.2 steps -> 31 steps = 7.75
    public void RoundDownToStep_ExactlyRepresentableResults_AreRoundedDown(double value, double step, double expected)
    {
        Assert.Equal(expected, PriceMath.RoundDownToStep(value, step));
    }

    [Fact]
    public void RoundDownToStep_MultipleHiddenByFloatingPointNoise_IsKept()
    {
        // 0.3 / 0.1 is 2.9999999999999996 in binary floating point; a plain floor would give
        // 2 steps (0.2). The value is 3 steps, so the result must be 0.3.
        double rounded = PriceMath.RoundDownToStep(0.3, 0.1);

        Assert.Equal(0.3, rounded, 1e-12);
    }

    [Fact]
    public void RoundDownToStep_SnapWindow_IsOneBillionthOfAStep()
    {
        // 3000 - 0.0000001 is 2.9999999999 steps: within 1e-9 of 3 steps, treated as 3 steps.
        Assert.Equal(3000.0, PriceMath.RoundDownToStep(3000.0 - 1e-7, 1000.0));

        // 3000 - 0.00001 is 2.99999999 steps: 1e-8 short of 3 steps, so it rounds down to 2 steps.
        Assert.Equal(2000.0, PriceMath.RoundDownToStep(3000.0 - 1e-5, 1000.0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1000.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RoundDownToStep_ValueNegativeOrNotFinite_Throws(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => PriceMath.RoundDownToStep(value, 1000.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1000.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RoundDownToStep_StepNotPositiveOrNotFinite_Throws(double step)
    {
        Assert.Throws<ArgumentOutOfRangeException>("step", () => PriceMath.RoundDownToStep(5000.0, step));
    }

    [Fact]
    public void RoundDownToStep_StepCountOverflows_Throws()
    {
        // The largest double divided by the smallest positive double is not a finite number.
        Assert.Throws<ArgumentOutOfRangeException>("step", () => PriceMath.RoundDownToStep(double.MaxValue, double.Epsilon));
    }

    [Theory]
    [InlineData(0.0012, 0.0001, 12.0)] // five-digit symbol: 0.0012 / 0.0001 = 12 pips
    [InlineData(0.12, 0.01, 12.0)] // JPY symbol: 0.12 / 0.01 = 12 pips
    [InlineData(0.00125, 0.0001, 12.5)] // half pips are kept
    [InlineData(-0.0012, 0.0001, -12.0)] // the sign of the distance is kept
    [InlineData(0.0, 0.0001, 0.0)]
    public void ToPips_PriceDistance_IsDividedByThePipSize(double priceDistance, double pipSize, double expectedPips)
    {
        Assert.Equal(expectedPips, PriceMath.ToPips(priceDistance, pipSize), 1e-9);
    }

    [Fact]
    public void ToPips_ExactlyRepresentableInput_IsExact()
    {
        // 3.0 / 0.25 = 12 with no rounding at all.
        Assert.Equal(12.0, PriceMath.ToPips(3.0, 0.25));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ToPips_DistanceNotFinite_Throws(double priceDistance)
    {
        Assert.Throws<ArgumentOutOfRangeException>("priceDistance", () => PriceMath.ToPips(priceDistance, 0.0001));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToPips_PipSizeNotPositiveOrNotFinite_Throws(double pipSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>("pipSize", () => PriceMath.ToPips(0.0012, pipSize));
    }

    [Fact]
    public void ToPips_ResultOverflows_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("pipSize", () => PriceMath.ToPips(double.MaxValue, double.Epsilon));
    }
}
