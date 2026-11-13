using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Building blocks for H4 zone scenarios (SIG-007 to SIG-013). Every price is chosen so that
/// true ranges, and therefore ATR(14), can be worked out by hand.
/// </summary>
/// <remarks>
/// <para>Filler bar F = (open 100.0, high 100.5, low 99.5, close 100.0): a doji with range 1.0.
/// With a previous close inside 99.5 to 100.5 its true range is exactly 1.0.</para>
/// <para>Swing bar SH = (100.0, swingHigh, 99.5, 100.0): with fillers on both sides it is a
/// swing high at <c>swingHigh</c>.</para>
/// <para><see cref="Prefix"/> builds bars 0 to 5 as F, bar 6 as SH and then a number of F bars
/// (22 by default, bars 7 to 28). Bar i opens at start + i x 4 hours. With the default start of
/// Monday 2026-01-05 00:00 UTC, bar 27 opens 2026-01-09 12:00, bar 28 at 16:00, bar 29 at 20:00
/// and bar 30 on 2026-01-10 00:00.</para>
/// </remarks>
public static class ZoneScenarios
{
    /// <summary>Open time of bar 0 in the default scenarios: Monday 2026-01-05 00:00 UTC.</summary>
    public static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Swing strength used by the scenarios (spec default).</summary>
    public const int Strength = 3;

    /// <summary>Bars 0-5 filler, bar 6 a swing high at <paramref name="swingHigh"/>, then filler bars.</summary>
    public static BarBuilder Prefix(double swingHigh = 101.0, int fillerAfterSwing = 22, DateTime? start = null)
    {
        return new BarBuilder(TimeFrame.H4)
            .At(start ?? Start)
            .Filler(6)
            .Ohlc(100.0, swingHigh, 99.5, 100.0)
            .Filler(fillerAfterSwing);
    }

    /// <summary>Appends <paramref name="count"/> filler bars F.</summary>
    public static BarBuilder Filler(this BarBuilder builder, int count)
    {
        return builder.Repeat(count, (b, _) => b.Ohlc(100.0, 100.5, 99.5, 100.0));
    }

    /// <summary>Open time of bar <paramref name="index"/> of a scenario that starts at <see cref="Start"/>.</summary>
    public static DateTime OpenOf(int index)
    {
        return Start.AddHours(4.0 * index);
    }

    /// <summary>
    /// Scenario A, the reference demand case. Bars 0-28 from <see cref="Prefix"/>; bar 29 is the
    /// bearish base bar B = (100.2, 100.5, 99.5, 100.0); bar 30 is the bullish leg (100.0 to
    /// <paramref name="legClose"/>, no wicks).
    /// </summary>
    /// <remarks>
    /// ATR as of bar 29 averages the true ranges of bars 16 to 29: all are 1.0, so ATR = 1.0. With
    /// the default leg close of 102.0 the net move is 2.0 = 2.0 x ATR, exactly the threshold.
    /// </remarks>
    public static IReadOnlyList<Bar> ScenarioA(double legClose = 102.0, double swingHigh = 101.0, DateTime? start = null)
    {
        return Prefix(swingHigh, 22, start)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, legClose)
            .Build();
    }

    /// <summary>
    /// Isolated-base layout: bars 0-27 from <see cref="Prefix"/> (21 fillers after the swing), bar
    /// 28 the wide doji W = (100.0, 101.2, 98.8, 100.0) with range and true range 2.4, bar 29 the
    /// given base bar and bar 30 the given leg bar.
    /// </summary>
    /// <remarks>
    /// When the base bar's true range is 1.0, ATR as of bar 29 = (12 x 1.0 + 2.4 + 1.0) / 14 =
    /// 15.4 / 14 = 1.1, so the leg threshold is 2.2 and the base is only bar 29 (W is wider than
    /// 1.1 and stops the collection).
    /// </remarks>
    public static IReadOnlyList<Bar> IsolatedBase(Bar baseBar, Bar legBar)
    {
        return Prefix(101.0, 21)
            .Ohlc(100.0, 101.2, 98.8, 100.0)
            .Ohlc(baseBar.Open, baseBar.High, baseBar.Low, baseBar.Close)
            .Ohlc(legBar.Open, legBar.High, legBar.Low, legBar.Close)
            .Build();
    }

    /// <summary>A bar used only to carry four prices into <see cref="IsolatedBase"/>; its time is ignored.</summary>
    public static Bar Prices(double open, double high, double low, double close)
    {
        return new Bar(Start, open, high, low, close);
    }
}
