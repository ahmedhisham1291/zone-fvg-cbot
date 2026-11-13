using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EMA tests (SIG-003). With period 3 the factor alpha = 2 / (3 + 1) = 0.5, and with whole-number
/// closes every intermediate value is a binary fraction, so the hand-computed results are exact.
/// </summary>
public class EmaTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Twelve closes = exactly the window of EMA(3) (4 x 3 bars).
    /// Seed = simple average of the first 3 closes = (10 + 11 + 12) / 3 = 11.
    /// Then ema = 0.5 x close + 0.5 x previous ema for the remaining 9 closes:
    /// <code>
    /// close 13 -> 6.5  + 5.5         = 12
    /// close 15 -> 7.5  + 6           = 13.5
    /// close 14 -> 7    + 6.75        = 13.75
    /// close 16 -> 8    + 6.875       = 14.875
    /// close 18 -> 9    + 7.4375      = 16.4375
    /// close 17 -> 8.5  + 8.21875     = 16.71875
    /// close 19 -> 9.5  + 8.359375    = 17.859375
    /// close 21 -> 10.5 + 8.9296875   = 19.4296875
    /// close 20 -> 10   + 9.71484375  = 19.71484375
    /// </code>
    /// </summary>
    private static readonly double[] HandCloses = { 10.0, 11.0, 12.0, 13.0, 15.0, 14.0, 16.0, 18.0, 17.0, 19.0, 21.0, 20.0 };

    private const double HandResult = 19.71484375;

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_PeriodThreeHandExample_IsSeededWithTheAverageOfTheFirstThreeCloses()
    {
        IReadOnlyList<Bar> bars = Daily(HandCloses);

        Assert.Equal(HandResult, Ema.Value(bars, 11, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_PeriodTwoHandExample_UsesAlphaTwoThirds()
    {
        // Window = 4 x 2 = 8 closes, alpha = 2 / (2 + 1) = 2/3.
        // Seed = (3 + 5) / 2 = 4.
        // close 4  -> 2/3 x 4  + 1/3 x 4      = 4
        // close 7  -> 14/3     + 4/3          = 6
        // close 10 -> 20/3     + 6/3          = 26/3
        // close 13 -> 26/3     + 26/9         = 104/9
        // close 16 -> 32/3     + 104/27       = 392/27
        // close 19 -> 38/3     + 392/81       = 1026/81 + 392/81 = 1418/81
        IReadOnlyList<Bar> bars = Daily(3.0, 5.0, 4.0, 7.0, 10.0, 13.0, 16.0, 19.0);

        Assert.Equal(1418.0 / 81.0, Expect.Value(Ema.Value(bars, 7, 2)), 1e-12);
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_PeriodOne_IsTheLastClose()
    {
        // Window = 4 closes, alpha = 2 / 2 = 1: seed 3, then each step replaces the value by the close.
        IReadOnlyList<Bar> bars = Daily(3.0, 9.0, 4.0, 7.0);

        Assert.Equal(7.0, Ema.Value(bars, 3, 1));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_WindowOneBarShort_IsNull()
    {
        // EMA(3) needs 12 closes; 11 are not enough.
        IReadOnlyList<Bar> eleven = Daily(HandCloses).FirstBars(11);

        Assert.Null(Ema.Value(eleven, 10, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_EndIndexWithWindowOneBarShort_IsNull()
    {
        // The list holds 12 bars, but only 11 of them are at or before index 10.
        IReadOnlyList<Bar> bars = Daily(HandCloses);

        Assert.Null(Ema.Value(bars, 10, 3));
        Assert.Null(Ema.Value(bars, 0, 3));
        Assert.Null(Ema.Value(bars, -1, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_NoBars_IsNull()
    {
        Assert.Null(Ema.Value(new List<Bar>(), -1, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_BarsOlderThanTheWindowAltered_IsUnchanged()
    {
        // Eight older closes, then the 12-close example. The window of EMA(3) at index 19 is
        // bars 8 to 19, so the older bars must not matter, whatever they are.
        var closes = new List<double> { 1000.0, 2000.0, 500.0, 4000.0, 3000.0, 100.0, 9000.0, 7000.0 };
        closes.AddRange(HandCloses);
        IReadOnlyList<Bar> bars = Daily(closes.ToArray());

        IReadOnlyList<Bar> altered = bars;
        for (int i = 0; i < 8; i++)
        {
            altered = altered.ReplaceAt(i, 0.25, 0.25, 0.25, 0.25);
        }

        Assert.Equal(HandResult, Ema.Value(bars, 19, 3));
        Assert.Equal(HandResult, Ema.Value(altered, 19, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_OldestWindowBarAltered_Changes()
    {
        // The first close of the window is part of the seed. Changing it from 10 to 13 gives
        // seed = (13 + 11 + 12) / 3 = 12, then with alpha 0.5:
        // 13 -> 12.5, 15 -> 13.75, 14 -> 13.875, 16 -> 14.9375, 18 -> 16.46875,
        // 17 -> 16.734375, 19 -> 17.8671875, 21 -> 19.43359375, 20 -> 19.716796875.
        IReadOnlyList<Bar> bars = Daily(HandCloses).ReplaceAt(0, 13.0, 13.0, 13.0, 13.0);

        Assert.Equal(19.716796875, Ema.Value(bars, 11, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_BarsAfterEndIndex_AreIgnored()
    {
        var closes = new List<double>(HandCloses) { 5000.0, 1.0, 9000.0 };
        IReadOnlyList<Bar> bars = Daily(closes.ToArray());

        Assert.Equal(HandResult, Ema.Value(bars, 11, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_UsesOnlyTheCloseOfEachBar()
    {
        // Same closes as the hand example, but every bar has a wide range and a different open.
        var builder = new BarBuilder(TimeFrame.D1).At(Start);
        foreach (double close in HandCloses)
        {
            builder.Ohlc(close - 3.0, close + 50.0, close - 40.0, close);
        }

        Assert.Equal(HandResult, Ema.Value(builder.Build(), 11, 3));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void WindowMultiple_IsFour()
    {
        int multiple = Ema.WindowMultiple;

        Assert.Equal(4, multiple);
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_EndIndexBeyondTheList_Throws()
    {
        IReadOnlyList<Bar> bars = Daily(HandCloses);

        Assert.Throws<ArgumentOutOfRangeException>("endIndex", () => Ema.Value(bars, 12, 3));
    }

    [Theory]
    [Trait("Req", "SIG-003")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Value_PeriodBelowOne_Throws(int period)
    {
        IReadOnlyList<Bar> bars = Daily(HandCloses);

        Assert.Throws<ArgumentOutOfRangeException>("period", () => Ema.Value(bars, 11, period));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_PeriodSoLargeThatTheWindowCannotFit_IsNull()
    {
        // 4 x 1,000,000,000 bars is more than any list can hold; the answer is "window not full".
        IReadOnlyList<Bar> bars = Daily(HandCloses);

        Assert.Null(Ema.Value(bars, 11, 1_000_000_000));
    }

    [Fact]
    [Trait("Req", "SIG-003")]
    public void Value_NullBars_Throws()
    {
        Assert.Throws<ArgumentNullException>("bars", () => Ema.Value(null!, 11, 3));
    }

    private static IReadOnlyList<Bar> Daily(params double[] closes)
    {
        return new BarBuilder(TimeFrame.D1).At(Start).Closes(closes).Build();
    }
}
