using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Fair value gap detection (SIG-016). Every case runs twice: as written for a demand zone and a
/// bullish gap, and mirrored around 200.0 (p becomes 200 - p) for a supply zone and a bearish gap.
/// </summary>
/// <remarks>
/// <para>Reference demand zone: proximal 100.2, distal 99.5, touched by the M15 bar opening at
/// T0 = 2026-01-06 00:00 UTC. M15 bar k of a buffer opens at T0 + k x 15 minutes.</para>
/// <para>Reference three-bar gap, the touch bar being c3:
/// c3 = (100.0, 100.4, 99.9, 100.3), c2 = (100.3, 100.9, 100.3, 100.8) bullish,
/// c1 = (100.8, 101.0, 100.6, 100.9). low(c1) 100.6 > high(c3) 100.4, so the gap runs from
/// 100.4 to 100.6 and is 0.2 high. With ATR(M15) 0.4 the minimum gap is 0.25 x 0.4 = 0.1; with
/// ATR(H4) 1.0 the lower edge may be at most 100.2 + 0.5 x 1.0 = 100.7.</para>
/// <para>Mirrored: supply zone proximal 99.8, distal 100.5; c3 = (100.0, 100.1, 99.6, 99.7),
/// c2 = (99.7, 99.7, 99.1, 99.2) bearish, c1 = (99.2, 99.4, 99.0, 99.1); the gap runs from
/// high(c1) 99.4 to low(c3) 99.6, and the upper edge must be at least 99.8 - 0.5 x ATR(H4).</para>
/// </remarks>
public class FvgDetectorTests
{
    private const double Pivot = 200.0;
    private const double Tick = 0.00001;

    private static readonly DateTime T0 = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SignalParameters Defaults = new();

    // ------------------------------------------------------------------ normal case

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_MinimalThreeBarGap_ReturnsTheGap(bool mirrored)
    {
        Fvg? fvg = FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, Defaults);

        AssertGap(fvg, mirrored, 100.6, 100.4, T0);
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_GapAfterBarsWithoutGap_UsesTheThreeNewestBars(bool mirrored)
    {
        // Touch bar and two flat bars (100.0, 100.1, 99.9, 100.0) first, then the reference
        // c3, c2, c1 at bars 3, 4, 5: barsSinceTouch = 5.
        IReadOnlyList<Bar> bars = Buffer(3, ReferenceC3, ReferenceC2, ReferenceC1);

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 5, 0.4, 1.0, Defaults);

        AssertGap(fvg, mirrored, 100.6, 100.4, T0.AddMinutes(45));
    }

    // ------------------------------------------------------------------ gap exists: low(c1) > high(c3)

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 100.4, false)] // low(c1) equal to high(c3): no gap
    [InlineData(false, 100.39999, false)] // one tick into c3: overlap, no gap
    [InlineData(false, 100.40001, true)] // one tick above high(c3): a gap of one tick
    [InlineData(true, 100.4, false)]
    [InlineData(true, 100.39999, false)]
    [InlineData(true, 100.40001, true)]
    public void Find_C1LowAroundC3High_GapOnlyWhenStrictlyAbove(bool mirrored, double c1Low, bool kept)
    {
        // ATR(M15) 0 makes the minimum gap 0, so only the existence rule decides.
        IReadOnlyList<Bar> bars = Buffer(0, ReferenceC3, ReferenceC2, Prices(100.8, 101.0, c1Low, 100.9));

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.0, 1.0, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    // ------------------------------------------------------------------ gap height >= MinFvgAtr x ATR(M15)

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 0.8, true)] // minimum 0.25 x 0.8 = 0.2 = gap: kept
    [InlineData(false, 0.79996, true)] // minimum 0.19999: gap one tick above it, kept
    [InlineData(false, 0.80004, false)] // minimum 0.20001: gap one tick under it, rejected
    [InlineData(true, 0.8, true)]
    [InlineData(true, 0.79996, true)]
    [InlineData(true, 0.80004, false)]
    public void Find_GapHeightAroundMinimum_KeptFromTheMinimum(bool mirrored, double atrM15, bool kept)
    {
        Fvg? fvg = FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, atrM15, 1.0, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 100.6, true)] // gap 0.2 = 0.25 x 0.8: kept
    [InlineData(false, 100.59999, false)] // gap 0.19999: one tick under, rejected
    [InlineData(false, 100.60001, true)] // gap 0.20001: kept
    [InlineData(true, 100.6, true)]
    [InlineData(true, 100.59999, false)]
    [InlineData(true, 100.60001, true)]
    public void Find_C1LowMovesGapAroundMinimum_KeptFromTheMinimum(bool mirrored, double c1Low, bool kept)
    {
        IReadOnlyList<Bar> bars = Buffer(0, ReferenceC3, ReferenceC2, Prices(100.8, 101.0, c1Low, 100.9));

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.8, 1.0, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_CustomMinFvgAtr_IsApplied(bool mirrored)
    {
        // MinFvgAtr 0.5 with ATR(M15) 0.4: minimum 0.2 = gap, kept; with ATR(M15) 0.40002 the
        // minimum is 0.20001 and the gap is rejected.
        SignalParameters parameters = Defaults with { MinFvgAtr = 0.5 };

        Assert.NotNull(FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, parameters));
        Assert.Null(FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, 0.40002, 1.0, parameters));
    }

    // ------------------------------------------------------------------ c2 closes above its open

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_C2Bearish_IsRejected(bool mirrored)
    {
        // c2 = (100.8, 100.9, 100.3, 100.3): closes below its open; the gap itself is unchanged.
        IReadOnlyList<Bar> bars = Buffer(0, ReferenceC3, Prices(100.8, 100.9, 100.3, 100.3), ReferenceC1);

        Assert.Null(FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, Defaults));
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_C2CloseEqualToOpen_IsRejected(bool mirrored)
    {
        // c2 = (100.5, 100.9, 100.3, 100.5): close equal to open is not "above".
        IReadOnlyList<Bar> bars = Buffer(0, ReferenceC3, Prices(100.5, 100.9, 100.3, 100.5), ReferenceC1);

        Assert.Null(FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, Defaults));
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_C2CloseOneTickAboveOpen_IsKept(bool mirrored)
    {
        IReadOnlyList<Bar> bars = Buffer(0, ReferenceC3, Prices(100.5, 100.9, 100.3, 100.50001), ReferenceC1);

        Assert.NotNull(FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, Defaults));
    }

    // ------------------------------------------------------------------ all three bars at or after the touch bar

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_C3OpenedBeforeTheTouchBar_IsRejected(bool mirrored)
    {
        // The zone was touched by c2 (opening T0 + 15 minutes), so c3 opened before the touch bar.
        Zone zone = ReferenceZone(T0.AddMinutes(15), mirrored);

        Assert.Null(FvgDetector.Find(OnSide(ReferenceBars(), mirrored), zone, 1, 0.4, 1.0, Defaults));
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_C3IsTheTouchBar_IsKept(bool mirrored)
    {
        // Earliest possible confirmation: c3 is the touch bar, c1 is two bars after it.
        Assert.NotNull(FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 1.0, Defaults));
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_TouchOneSecondAfterC3Opened_IsRejected(bool mirrored)
    {
        Zone zone = ReferenceZone(T0.AddSeconds(1), mirrored);

        Assert.Null(FvgDetector.Find(OnSide(ReferenceBars(), mirrored), zone, 2, 0.4, 1.0, Defaults));
    }

    // ------------------------------------------------------------------ within ConfirmWindowBars of the touch bar

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 15, true)]
    [InlineData(false, 16, true)] // c1 is bar 16 after the touch bar: the last bar of the window
    [InlineData(false, 17, false)] // one bar later
    [InlineData(true, 15, true)]
    [InlineData(true, 16, true)]
    [InlineData(true, 17, false)]
    public void Find_ConfirmationAroundWindowEnd_KeptUpToConfirmWindowBars(bool mirrored, int barsSinceTouch, bool kept)
    {
        // Touch bar 0 and flat bars, then c3, c2, c1, c1 being bar barsSinceTouch.
        IReadOnlyList<Bar> bars = Buffer(barsSinceTouch - 2, ReferenceC3, ReferenceC2, ReferenceC1);

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), barsSinceTouch, 0.4, 1.0, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 4, true)]
    [InlineData(false, 5, false)]
    [InlineData(true, 4, true)]
    [InlineData(true, 5, false)]
    public void Find_CustomConfirmWindowBars_IsApplied(bool mirrored, int barsSinceTouch, bool kept)
    {
        SignalParameters parameters = Defaults with { ConfirmWindowBars = 4 };
        IReadOnlyList<Bar> bars = Buffer(barsSinceTouch - 2, ReferenceC3, ReferenceC2, ReferenceC1);

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), barsSinceTouch, 0.4, 1.0, parameters);

        Assert.Equal(kept, fvg is not null);
    }

    // ------------------------------------------------------------------ lower edge <= proximal + 0.5 x ATR(H4)

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 0.4, true)] // limit 100.2 + 0.2 = 100.4 = high(c3): kept
    [InlineData(false, 0.40002, true)] // limit 100.40001: edge one tick under it, kept
    [InlineData(false, 0.39998, false)] // limit 100.39999: edge one tick above it, rejected
    [InlineData(true, 0.4, true)]
    [InlineData(true, 0.40002, true)]
    [InlineData(true, 0.39998, false)]
    public void Find_LowerEdgeAroundLimit_KeptUpToTheLimit(bool mirrored, double atrH4, bool kept)
    {
        Fvg? fvg = FvgDetector.Find(OnSide(ReferenceBars(), mirrored), ReferenceZone(T0, mirrored), 2, 0.4, atrH4, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(false, 100.4, true)] // edge exactly at the limit 100.4
    [InlineData(false, 100.39999, true)]
    [InlineData(false, 100.40001, false)] // edge above the limit
    [InlineData(true, 100.4, true)]
    [InlineData(true, 100.39999, true)]
    [InlineData(true, 100.40001, false)]
    public void Find_C3HighAroundLimit_KeptUpToTheLimit(bool mirrored, double c3High, bool kept)
    {
        // ATR(H4) 0.4: limit 100.4. ATR(M15) 0.4: minimum gap 0.1, which every case meets.
        IReadOnlyList<Bar> bars = Buffer(0, Prices(100.0, c3High, 99.9, 100.3), ReferenceC2, ReferenceC1);

        Fvg? fvg = FvgDetector.Find(OnSide(bars, mirrored), ReferenceZone(T0, mirrored), 2, 0.4, 0.4, Defaults);

        Assert.Equal(kept, fvg is not null);
    }

    // ------------------------------------------------------------------ too few bars and invalid input

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Find_FewerThanThreeBars_ReturnsNull(int count)
    {
        IReadOnlyList<Bar> bars = ReferenceBars().FirstBars(count);

        Assert.Null(FvgDetector.Find(bars, ReferenceZone(T0, false), count, 0.4, 1.0, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-016")]
    public void Find_ZoneNotTouched_Throws()
    {
        Zone untouched = ReferenceZone(T0, false) with { State = ZoneState.Active, TouchBarOpenUtc = null, ExtremeSinceTouch = null };

        Assert.Throws<ArgumentException>("zone", () => FvgDetector.Find(ReferenceBars(), untouched, 2, 0.4, 1.0, Defaults));
    }

    [Theory]
    [Trait("Req", "SIG-016")]
    [InlineData(double.NaN, 1.0)]
    [InlineData(-0.1, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.4, double.NaN)]
    [InlineData(0.4, -0.1)]
    public void Find_InvalidAtr_Throws(double atrM15, double atrH4)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FvgDetector.Find(ReferenceBars(), ReferenceZone(T0, false), 2, atrM15, atrH4, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-016")]
    public void Find_NegativeBarsSinceTouch_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("barsSinceTouch", () => FvgDetector.Find(ReferenceBars(), ReferenceZone(T0, false), -1, 0.4, 1.0, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-016")]
    public void Find_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("m15Buffer", () => FvgDetector.Find(null!, ReferenceZone(T0, false), 2, 0.4, 1.0, Defaults));
        Assert.Throws<ArgumentNullException>("zone", () => FvgDetector.Find(ReferenceBars(), null!, 2, 0.4, 1.0, Defaults));
        Assert.Throws<ArgumentNullException>("parameters", () => FvgDetector.Find(ReferenceBars(), ReferenceZone(T0, false), 2, 0.4, 1.0, null!));
    }

    [Fact]
    [Trait("Req", "SIG-016")]
    public void Find_InvalidParameters_Throws()
    {
        SignalParameters invalid = Defaults with { MinFvgAtr = 0.0 };

        Assert.Throws<ArgumentOutOfRangeException>("MinFvgAtr", () => FvgDetector.Find(ReferenceBars(), ReferenceZone(T0, false), 2, 0.4, 1.0, invalid));
    }

    // ------------------------------------------------------------------ helpers

    private static Bar ReferenceC3 => Prices(100.0, 100.4, 99.9, 100.3);

    private static Bar ReferenceC2 => Prices(100.3, 100.9, 100.3, 100.8);

    private static Bar ReferenceC1 => Prices(100.8, 101.0, 100.6, 100.9);

    private static IReadOnlyList<Bar> ReferenceBars()
    {
        return Buffer(0, ReferenceC3, ReferenceC2, ReferenceC1);
    }

    /// <summary>
    /// M15 bars from T0: <paramref name="flatBars"/> bars (100.0, 100.1, 99.9, 100.0), then c3, c2
    /// and c1 with the prices of the given bars.
    /// </summary>
    private static IReadOnlyList<Bar> Buffer(int flatBars, Bar c3, Bar c2, Bar c1)
    {
        return new BarBuilder(TimeFrame.M15)
            .At(T0)
            .Repeat(flatBars, (b, _) => b.Ohlc(100.0, 100.1, 99.9, 100.0))
            .Ohlc(c3.Open, c3.High, c3.Low, c3.Close)
            .Ohlc(c2.Open, c2.High, c2.Low, c2.Close)
            .Ohlc(c1.Open, c1.High, c1.Low, c1.Close)
            .Build();
    }

    /// <summary>A bar used only to carry four prices; its time is ignored.</summary>
    private static Bar Prices(double open, double high, double low, double close)
    {
        return new Bar(T0, open, high, low, close);
    }

    private static IReadOnlyList<Bar> OnSide(IReadOnlyList<Bar> bars, bool mirrored)
    {
        return mirrored ? bars.Mirror(Pivot) : bars;
    }

    /// <summary>The reference zone, Touched by the bar opening at <paramref name="touchBarOpenUtc"/>.</summary>
    private static Zone ReferenceZone(DateTime touchBarOpenUtc, bool mirrored)
    {
        var demand = new Zone("D-20260105-0800", ZoneType.Demand, 100.2, 99.5, T0, 1.0, ZoneState.Touched, touchBarOpenUtc, 99.9);
        if (!mirrored)
        {
            return demand;
        }

        return demand with
        {
            Id = "S-20260105-0800",
            Type = ZoneType.Supply,
            Proximal = Pivot - 100.2,
            Distal = Pivot - 99.5,
            ExtremeSinceTouch = Pivot - 99.9,
        };
    }

    /// <summary>Checks a gap given in demand coordinates; for the mirrored case the edges are reflected and swapped.</summary>
    private static void AssertGap(Fvg? fvg, bool mirrored, double upper, double lower, DateTime c3OpenUtc)
    {
        Assert.NotNull(fvg);
        Assert.Equal(mirrored ? Side.Sell : Side.Buy, fvg!.Side);
        double expectedUpper = mirrored ? Pivot - lower : upper;
        double expectedLower = mirrored ? Pivot - upper : lower;
        Assert.True(PriceMath.AlmostEqual(expectedUpper, fvg.Upper, PriceMath.DefaultTolerance), "Upper " + fvg.Upper.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(PriceMath.AlmostEqual(expectedLower, fvg.Lower, PriceMath.DefaultTolerance), "Lower " + fvg.Lower.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(c3OpenUtc, fvg.C3OpenUtc);
        Assert.Equal(c3OpenUtc.AddMinutes(15), fvg.C2OpenUtc);
        Assert.Equal(c3OpenUtc.AddMinutes(30), fvg.C1OpenUtc);
    }
}
