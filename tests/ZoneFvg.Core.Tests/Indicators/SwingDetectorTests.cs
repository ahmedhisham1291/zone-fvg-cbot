using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Swing tests (SIG-006). Bar i of every series opens at Start + i x 4 hours. With seven bars
/// and strength 3 only bar 3 has three bars on each side, so it is the only possible swing.
/// </summary>
public class SwingDetectorTests
{
    private const int Strength = 3;

    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BarThree = Start.AddHours(12.0);

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_HighAboveThreeBarsOnEachSide_IsASwingHigh()
    {
        IReadOnlyList<Bar> bars = WithHighs(10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0);

        Swing swing = Assert.Single(SwingDetector.Find(bars, Strength));

        Assert.Equal(new Swing(SwingKind.High, BarThree, 15.0), swing);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_LowBelowThreeBarsOnEachSide_IsASwingLow()
    {
        IReadOnlyList<Bar> bars = WithLows(10.0, 9.0, 8.0, 5.0, 8.0, 9.0, 10.0);

        Swing swing = Assert.Single(SwingDetector.Find(bars, Strength));

        Assert.Equal(new Swing(SwingKind.Low, BarThree, 5.0), swing);
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Find_EqualHighWithinThreeBars_IsNotASwingHigh(int offset)
    {
        double[] highs = { 10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0 };
        highs[3 + offset] = 15.0;

        Assert.Empty(SwingDetector.Find(WithHighs(highs), Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Find_EqualLowWithinThreeBars_IsNotASwingLow(int offset)
    {
        double[] lows = { 10.0, 9.0, 8.0, 5.0, 8.0, 9.0, 10.0 };
        lows[3 + offset] = 5.0;

        Assert.Empty(SwingDetector.Find(WithLows(lows), Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Find_HigherHighWithinThreeBars_IsNotASwingHigh(int offset)
    {
        double[] highs = { 10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0 };
        highs[3 + offset] = 16.0;

        Assert.Empty(SwingDetector.Find(WithHighs(highs), Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Find_LowerLowWithinThreeBars_IsNotASwingLow(int offset)
    {
        double[] lows = { 10.0, 9.0, 8.0, 5.0, 8.0, 9.0, 10.0 };
        lows[3 + offset] = 4.0;

        Assert.Empty(SwingDetector.Find(WithLows(lows), Strength));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_HighAboveNeighbourByOneTick_IsASwingHigh()
    {
        // 15.00000 against 14.99999: one tick of a five-digit symbol is strictly greater.
        IReadOnlyList<Bar> bars = WithHighs(10.0, 11.0, 14.99999, 15.0, 12.0, 11.0, 10.0);

        Swing swing = Assert.Single(SwingDetector.Find(bars, Strength));

        Assert.Equal(new Swing(SwingKind.High, BarThree, 15.0), swing);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_HighAboveNeighbourByLessThanTheTolerance_IsNotASwingHigh()
    {
        // 15.0000000005 against 15: the difference (5e-10) is inside the PriceMath tolerance
        // (1e-9), so the two highs count as equal and equal highs are not a swing.
        IReadOnlyList<Bar> bars = WithHighs(10.0, 11.0, 15.0, 15.0000000005, 12.0, 11.0, 10.0);

        Assert.Empty(SwingDetector.Find(bars, Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(4, 0)] // bar 3 has no bar to its right yet
    [InlineData(5, 0)] // one bar to the right
    [InlineData(6, 0)] // two bars to the right
    [InlineData(7, 1)] // the third bar to the right has closed: confirmed
    [InlineData(8, 1)] // and it stays confirmed
    public void Find_SwingHigh_IsConfirmedOnlyWhenTheThirdBarToItsRightHasClosed(int closedBars, int expectedSwings)
    {
        IReadOnlyList<Bar> all = WithHighs(10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0, 9.0);

        IReadOnlyList<Swing> swings = SwingDetector.Find(all.FirstBars(closedBars), Strength);

        Assert.Equal(expectedSwings, swings.Count);
        Assert.All(swings, swing => Assert.Equal(new Swing(SwingKind.High, BarThree, 15.0), swing));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    public void Find_SwingLow_IsConfirmedOnlyWhenTheThirdBarToItsRightHasClosed(int closedBars, int expectedSwings)
    {
        IReadOnlyList<Bar> all = WithLows(10.0, 9.0, 8.0, 5.0, 8.0, 9.0, 10.0, 11.0);

        IReadOnlyList<Swing> swings = SwingDetector.Find(all.FirstBars(closedBars), Strength);

        Assert.Equal(expectedSwings, swings.Count);
        Assert.All(swings, swing => Assert.Equal(new Swing(SwingKind.Low, BarThree, 5.0), swing));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_SwingHighOnTheHundredthMostRecentBar_IsRetained()
    {
        // 103 bars: the swing bar is bar 3, and 103 - 3 = 100, so it is the 100th most recent bar.
        // The three bars to its left are older than the last 100 bars and are still compared.
        IReadOnlyList<Bar> bars = PeakThenDecline(103);

        Swing swing = Assert.Single(SwingDetector.Find(bars, Strength));

        Assert.Equal(new Swing(SwingKind.High, BarThree, 15.0), swing);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_SwingHighOnTheHundredAndFirstMostRecentBar_IsDropped()
    {
        // 104 bars: 104 - 3 = 101, so the swing bar is now the 101st most recent bar.
        IReadOnlyList<Bar> bars = PeakThenDecline(104);

        Assert.Empty(SwingDetector.Find(bars, Strength));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_SwingLowOnTheHundredthMostRecentBar_IsRetained()
    {
        IReadOnlyList<Bar> bars = TroughThenRise(103);

        Swing swing = Assert.Single(SwingDetector.Find(bars, Strength));

        Assert.Equal(new Swing(SwingKind.Low, BarThree, 5.0), swing);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_SwingLowOnTheHundredAndFirstMostRecentBar_IsDropped()
    {
        IReadOnlyList<Bar> bars = TroughThenRise(104);

        Assert.Empty(SwingDetector.Find(bars, Strength));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void LookbackBars_IsOneHundred()
    {
        int lookback = SwingDetector.LookbackBars;

        Assert.Equal(100, lookback);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_ZigZag_ReturnsHighsAndLowsOldestFirst()
    {
        // Lows are the highs minus 3, so the series has one shape:
        // bar:    0   1   2   3   4   5   6   7   8   9  10  11  12  13  14
        // high:  10  11  12  15  12  11  10   9  10  11  12  16  12  11  10
        // low:    7   8   9  12   9   8   7   6   7   8   9  13   9   8   7
        // Bars 3 to 11 have three bars on each side.
        // Swing highs: bar 3 (15 above 10, 11, 12 and 12, 11, 10) and bar 11 (16 above 10, 11, 12 and 12, 11, 10).
        // Swing low: bar 7 (6 below 9, 8, 7 and 7, 8, 9).
        // Bar 9 (high 11) is not a swing high because bar 10 is higher; bar 5 (low 8) is not a
        // swing low because bar 6 is lower.
        double[] highs = { 10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0, 9.0, 10.0, 11.0, 12.0, 16.0, 12.0, 11.0, 10.0 };
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        foreach (double high in highs)
        {
            builder.Doji(high - 1.5).Range(high - 3.0, high);
        }

        IReadOnlyList<Swing> swings = SwingDetector.Find(builder.Build(), Strength);

        Assert.Equal(3, swings.Count);
        Assert.Equal(new Swing(SwingKind.High, Start.AddHours(12.0), 15.0), swings[0]);
        Assert.Equal(new Swing(SwingKind.Low, Start.AddHours(28.0), 6.0), swings[1]);
        Assert.Equal(new Swing(SwingKind.High, Start.AddHours(44.0), 16.0), swings[2]);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_BarThatIsBothHighestAndLowest_ReturnsTheHighThenTheLow()
    {
        // Bar 3 has the highest high (15) and the lowest low (1) of the seven bars.
        double[] highs = { 10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0 };
        double[] lows = { 5.0, 4.0, 3.0, 1.0, 3.0, 4.0, 5.0 };
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        for (int i = 0; i < highs.Length; i++)
        {
            builder.Doji(7.0).Range(lows[i], highs[i]);
        }

        IReadOnlyList<Swing> swings = SwingDetector.Find(builder.Build(), Strength);

        Assert.Equal(2, swings.Count);
        Assert.Equal(new Swing(SwingKind.High, BarThree, 15.0), swings[0]);
        Assert.Equal(new Swing(SwingKind.Low, BarThree, 1.0), swings[1]);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_StrengthOne_NeedsOneBarOnEachSide()
    {
        IReadOnlyList<Bar> bars = WithHighs(8.0, 9.0, 8.0);

        Swing swing = Assert.Single(SwingDetector.Find(bars, 1));

        Assert.Equal(new Swing(SwingKind.High, Start.AddHours(4.0), 9.0), swing);
        Assert.Empty(SwingDetector.Find(bars, 2));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_StrengthTwo_NeedsTwoBarsOnEachSide()
    {
        IReadOnlyList<Bar> bars = WithHighs(8.0, 9.0, 10.0, 9.0, 8.0);

        Swing swing = Assert.Single(SwingDetector.Find(bars, 2));

        Assert.Equal(new Swing(SwingKind.High, Start.AddHours(8.0), 10.0), swing);
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_FewerBarsThanOneSwingNeeds_ReturnsEmpty()
    {
        Assert.Empty(SwingDetector.Find(new List<Bar>(), Strength));
        Assert.Empty(SwingDetector.Find(WithHighs(10.0, 11.0, 12.0, 15.0, 12.0, 11.0), Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(7)]
    [InlineData(20260105)]
    [InlineData(123456)]
    public void Find_MirroredSeededHistory_SwapsHighsAndLowsAndRespectsBothLimits(int seed)
    {
        // The seed is the theory argument, so a failure names it. Prices are whole numbers,
        // which makes the reflection (p becomes 3000 - p) exact.
        const int barCount = 160;
        const double pivot = 3000.0;
        var generator = new SeededGenerator(seed);
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        for (int i = 0; i < barCount; i++)
        {
            double low = 1000.0 + generator.Next(60);
            double high = low + 1.0 + generator.Next(25);
            builder.Doji(low).Range(low, high);
        }

        IReadOnlyList<Bar> bars = builder.Build();

        IReadOnlyList<Swing> original = SwingDetector.Find(bars, Strength);
        IReadOnlyList<Swing> mirrored = SwingDetector.Find(bars.Mirror(pivot), Strength);

        Swing[] expectedMirror = original
            .Select(swing => new Swing(Opposite(swing.Kind), swing.BarOpenTimeUtc, pivot - swing.Price))
            .OrderBy(swing => swing.BarOpenTimeUtc)
            .ThenBy(swing => swing.Kind)
            .ToArray();
        Assert.Contains(original, swing => swing.Kind == SwingKind.High);
        Assert.Contains(original, swing => swing.Kind == SwingKind.Low);
        Assert.Equal(expectedMirror, mirrored.ToArray());

        // Retained: only the last 100 bars (bars 60 to 159). Confirmed: three closed bars to the right (up to bar 156).
        DateTime oldestAllowed = bars[barCount - 100].OpenTimeUtc;
        DateTime newestAllowed = bars[barCount - 1 - Strength].OpenTimeUtc;
        Assert.All(original, swing => Assert.InRange(swing.BarOpenTimeUtc, oldestAllowed, newestAllowed));
    }

    [Fact]
    [Trait("Req", "SIG-006")]
    public void Find_NullBars_Throws()
    {
        Assert.Throws<ArgumentNullException>("bars", () => SwingDetector.Find(null!, Strength));
    }

    [Theory]
    [Trait("Req", "SIG-006")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Find_StrengthBelowOne_Throws(int strength)
    {
        IReadOnlyList<Bar> bars = WithHighs(10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0);

        Assert.Throws<ArgumentOutOfRangeException>("strength", () => SwingDetector.Find(bars, strength));
    }

    private static SwingKind Opposite(SwingKind kind)
    {
        return kind == SwingKind.High ? SwingKind.Low : SwingKind.High;
    }

    /// <summary>Bars whose highs vary while every low is 5 and every open and close is 7: no swing low can appear.</summary>
    private static IReadOnlyList<Bar> WithHighs(params double[] highs)
    {
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        foreach (double high in highs)
        {
            builder.Doji(7.0).Range(5.0, high);
        }

        return builder.Build();
    }

    /// <summary>Bars whose lows vary while every high is 15 and every open and close is 13: no swing high can appear.</summary>
    private static IReadOnlyList<Bar> WithLows(params double[] lows)
    {
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        foreach (double low in lows)
        {
            builder.Doji(13.0).Range(low, 15.0);
        }

        return builder.Build();
    }

    /// <summary>
    /// Bars 0 to 6 hold a peak at bar 3 (highs 10, 11, 12, 15, 12, 11, 10). Every later bar has a
    /// high 0.01 below the bar before it (9.99, 9.98, ...), so each has a higher bar to its left
    /// and cannot be a swing high. All lows are 5, so there is no swing low.
    /// </summary>
    private static IReadOnlyList<Bar> PeakThenDecline(int totalBars)
    {
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        foreach (double high in new[] { 10.0, 11.0, 12.0, 15.0, 12.0, 11.0, 10.0 })
        {
            builder.Doji(7.0).Range(5.0, high);
        }

        builder.Repeat(totalBars - 7, (bars, index) => bars.Doji(7.0).Range(5.0, 9.99 - (0.01 * index)));
        return builder.Build();
    }

    /// <summary>Mirror image of <see cref="PeakThenDecline"/>: a trough at bar 3, then lows rising by 0.01 per bar.</summary>
    private static IReadOnlyList<Bar> TroughThenRise(int totalBars)
    {
        var builder = new BarBuilder(TimeFrame.H4).At(Start);
        foreach (double low in new[] { 10.0, 9.0, 8.0, 5.0, 8.0, 9.0, 10.0 })
        {
            builder.Doji(13.0).Range(low, 15.0);
        }

        builder.Repeat(totalBars - 7, (bars, index) => bars.Doji(13.0).Range(10.01 + (0.01 * index), 15.0));
        return builder.Build();
    }
}
