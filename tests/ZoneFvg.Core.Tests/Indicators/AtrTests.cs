using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// ATR tests (SIG-003). All prices are multiples of 0.5, which binary floating point stores
/// exactly, so the hand-computed results below are exact and are compared without tolerance.
/// </summary>
public class AtrTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_FifteenBarHandExample_IsMeanOfFourteenTrueRanges()
    {
        IReadOnlyList<Bar> bars = HandExample().Build();

        double? atr = Atr.Value(bars, 14);

        // Sum of the 14 true ranges = 28 (see HandExample), so ATR(14) = 28 / 14 = 2.
        Assert.Equal(2.0, atr);
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_DefaultPeriod_IsFourteen()
    {
        IReadOnlyList<Bar> bars = HandExample().Build();
        int defaultPeriod = Atr.DefaultPeriod;

        Assert.Equal(14, defaultPeriod);
        Assert.Equal(2.0, Atr.Value(bars, 14));
        Assert.Equal(2.0, Atr.Value(bars, 14, 14));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_FourteenBars_IsNull()
    {
        // ATR(14) needs 15 bars: 14 true ranges plus the close before the first of them.
        IReadOnlyList<Bar> fourteen = HandExample().Build().FirstBars(14);

        Assert.Null(Atr.Value(fourteen, 13));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_EndIndexWithOnlyFourteenBarsUpToIt_IsNull()
    {
        // The list holds 15 bars, but only 14 of them are at or before index 13.
        IReadOnlyList<Bar> bars = HandExample().Build();

        Assert.Null(Atr.Value(bars, 13));
        Assert.Null(Atr.Value(bars, 0));
        Assert.Null(Atr.Value(bars, -1));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_NoBars_IsNull()
    {
        Assert.Null(Atr.Value(new List<Bar>(), -1));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_BarsOlderThanTheWindowAltered_IsUnchanged()
    {
        // Five older bars with huge ranges, then the 15-bar example. The window of ATR(14) at
        // index 19 is bars 5 to 19, so the older bars must not matter.
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(Start.AddHours(-20.0))
            .Ohlc(500.0, 900.0, 100.0, 700.0)
            .Ohlc(700.0, 950.0, 50.0, 60.0)
            .Ohlc(60.0, 800.0, 10.0, 400.0)
            .Ohlc(400.0, 400.0, 20.0, 30.0)
            .Ohlc(30.0, 990.0, 5.0, 100.0)
            .Build();
        var all = new List<Bar>(bars);
        all.AddRange(HandExample().Build());

        double? before = Atr.Value(all, 19);
        IReadOnlyList<Bar> altered = all
            .ReplaceAt(0, 1.0, 2.0, 0.5, 1.5)
            .ReplaceAt(1, 2000.0, 9000.0, 1000.0, 5000.0)
            .ReplaceAt(2, 3.0, 3.0, 3.0, 3.0)
            .ReplaceAt(3, 70.0, 80.0, 60.0, 75.0)
            .ReplaceAt(4, 0.5, 4000.0, 0.25, 2000.0);
        double? after = Atr.Value(altered, 19);

        Assert.Equal(2.0, before);
        Assert.Equal(2.0, after);
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_OldestWindowBar_ContributesOnlyItsClose()
    {
        IReadOnlyList<Bar> bars = HandExample().Build();

        // Same close (101), different open, high and low: nothing changes.
        IReadOnlyList<Bar> sameClose = bars.ReplaceAt(0, 101.0, 150.0, 50.0, 101.0);

        // Close moved from 101 to 99.5: the true range of bar 1 (high 103, low 100) becomes
        // max(103 - 100, |103 - 99.5|, |100 - 99.5|) = max(3, 3.5, 0.5) = 3.5 instead of 3,
        // so the sum is 28.5 and ATR(14) = 28.5 / 14.
        IReadOnlyList<Bar> lowerClose = bars.ReplaceAt(0, 100.0, 102.0, 99.0, 99.5);

        Assert.Equal(2.0, Atr.Value(sameClose, 14));
        Assert.Equal(28.5 / 14.0, Expect.Value(Atr.Value(lowerClose, 14)), 1e-12);
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_BarsAfterEndIndex_AreIgnored()
    {
        // Three later bars with huge ranges follow the example; the value as of index 14 must not see them.
        IReadOnlyList<Bar> bars = HandExample()
            .Ohlc(104.5, 900.0, 1.0, 500.0)
            .Ohlc(500.0, 800.0, 2.0, 3.0)
            .Ohlc(3.0, 700.0, 3.0, 600.0)
            .Build();

        Assert.Equal(2.0, Atr.Value(bars, 14));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_PeriodTwo_UsesTheLastTwoTrueRanges()
    {
        IReadOnlyList<Bar> bars = HandExample().Build();

        // True ranges of bars 1 and 2 are 3 and 2 (see HandExample): (3 + 2) / 2 = 2.5.
        Assert.Equal(2.5, Atr.Value(bars, 2, 2));

        // True ranges of bars 13 and 14 are 2 and 1.5: (2 + 1.5) / 2 = 1.75.
        Assert.Equal(1.75, Atr.Value(bars, 14, 2));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_MirroredBars_GiveTheSameAtr()
    {
        // Reflecting every price (p becomes 300 - p) keeps every range, so ATR is unchanged.
        IReadOnlyList<Bar> mirrored = HandExample().Build().Mirror(300.0);

        Assert.Equal(2.0, Atr.Value(mirrored, 14));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_EndIndexBeyondTheList_Throws()
    {
        IReadOnlyList<Bar> bars = HandExample().Build();

        Assert.Throws<ArgumentOutOfRangeException>("endIndex", () => Atr.Value(bars, 15));
    }

    [Theory]
    [Trait("Req", "SIG-003")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Value_PeriodBelowOne_Throws(int period)
    {
        IReadOnlyList<Bar> bars = HandExample().Build();

        Assert.Throws<ArgumentOutOfRangeException>("period", () => Atr.Value(bars, 14, period));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_NullBars_Throws()
    {
        Assert.Throws<ArgumentNullException>("bars", () => Atr.Value(null!, 14));
    }

    /// <summary>
    /// Fifteen H4 bars. True range of bar i = max(high - low, |high - previous close|, |low - previous close|).
    /// <code>
    /// bar   open   high   low    close | prev close | high-low  |high-pc|  |low-pc|  | true range
    ///  0    100    102     99    101   |
    ///  1    101    103    100    102   |   101      |   3         2         1       |  3
    ///  2    102.5  104    102.5  103.5 |   102      |   1.5       2         0.5     |  2    (gap up)
    ///  3    103    103    101    101.5 |   103.5    |   2         0.5       2.5     |  2.5  (gap down)
    ///  4    104    105    104    104.5 |   101.5    |   1         3.5       2.5     |  3.5  (whole bar above pc)
    ///  5    102    102    101    101.5 |   104.5    |   1         2.5       3.5     |  3.5  (whole bar below pc)
    ///  6    101.5  102    101    102   |   101.5    |   1         0.5       0.5     |  1
    ///  7    102    103    101.5  102.5 |   102      |   1.5       1         0.5     |  1.5
    ///  8    102.5  102.5  102.5  102.5 |   102.5    |   0         0         0       |  0    (flat bar)
    ///  9    102.5  104    102    103   |   102.5    |   2         1.5       0.5     |  2
    /// 10    103    103.5  102.5  103   |   103      |   1         0.5       0.5     |  1
    /// 11    103    106    103    105.5 |   103      |   3         3         0       |  3
    /// 12    105.5  106    104.5  105   |   105.5    |   1.5       0.5       1       |  1.5
    /// 13    105    105.5  103.5  104   |   105      |   2         0.5       1.5     |  2
    /// 14    104    105    103.5  104.5 |   104      |   1.5       1         0.5     |  1.5
    /// </code>
    /// Sum of true ranges 1 to 14 = 3 + 2 + 2.5 + 3.5 + 3.5 + 1 + 1.5 + 0 + 2 + 1 + 3 + 1.5 + 2 + 1.5 = 28.
    /// The plain ranges (high - low) sum to 22, so an ATR that ignored the previous close would give 22 / 14.
    /// </summary>
    private static BarBuilder HandExample()
    {
        return new BarBuilder(TimeFrame.H4)
            .At(Start)
            .Ohlc(100.0, 102.0, 99.0, 101.0)
            .Ohlc(101.0, 103.0, 100.0, 102.0)
            .Ohlc(102.5, 104.0, 102.5, 103.5)
            .Ohlc(103.0, 103.0, 101.0, 101.5)
            .Ohlc(104.0, 105.0, 104.0, 104.5)
            .Ohlc(102.0, 102.0, 101.0, 101.5)
            .Ohlc(101.5, 102.0, 101.0, 102.0)
            .Ohlc(102.0, 103.0, 101.5, 102.5)
            .Ohlc(102.5, 102.5, 102.5, 102.5)
            .Ohlc(102.5, 104.0, 102.0, 103.0)
            .Ohlc(103.0, 103.5, 102.5, 103.0)
            .Ohlc(103.0, 106.0, 103.0, 105.5)
            .Ohlc(105.5, 106.0, 104.5, 105.0)
            .Ohlc(105.0, 105.5, 103.5, 104.0)
            .Ohlc(104.0, 105.0, 103.5, 104.5);
    }
}
