using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>Shape of a D1 history, which decides the engine's bias (SIG-005, SIG-015, SIG-018).</summary>
public enum BiasHistory
{
    /// <summary>Rising closes: the last close is above its EMA(50). Long.</summary>
    Long,

    /// <summary>Falling closes: the last close is below its EMA(50). Short.</summary>
    Short,

    /// <summary>Every close 50.0: close equal to the EMA. Neutral.</summary>
    Neutral,

    /// <summary>Falling closes (Short), then a last bar closing at 100.0, far above the EMA (Long).</summary>
    ShortThenLong,

    /// <summary>Rising closes (Long), then a last bar closing at 10.0, far below the EMA (Short).</summary>
    LongThenShort,
}

/// <summary>
/// Hand-built engine histories (SIG-004, SIG-014, SIG-015, SIG-018, SIG-020). The engine does not
/// require the timeframes to agree with each other, so D1, H4 and M15 bars are built separately
/// and merged in intake order by <see cref="EngineFeed.Merge"/>.
/// </summary>
/// <remarks>
/// <para><b>H4.</b> <see cref="DemandH4"/> ends with the 31 bars of
/// <see cref="ZoneScenarios.ScenarioA"/> after flat filler bars F = (100.0, 100.5, 99.5, 100.0).
/// The leg bar opens at <see cref="Activation"/> - 4 hours. The engine detects one demand zone,
/// <see cref="ZoneId"/>: proximal 100.2, distal 99.5, ATR at creation 1.0, active from
/// <see cref="Activation"/>. Its base is the three bars before the leg, so the id comes from the
/// bar opening 16 hours before the activation, 2026-03-01 08:00. ATR(H4) as of the leg bar is
/// (13 x 1.0 + 2.0) / 14 = 15 / 14, so a gap's lower edge may be at most 100.2 + 0.5 x 15 / 14 =
/// 100.7357.</para>
/// <para><b>M15.</b> <see cref="M15"/> puts flat bars (100.6, 100.65, 100.55, 100.6), true range
/// 0.1, before the activation; they open before it and are ignored by the zone. The tail starts
/// at the activation. <see cref="TouchAndGap"/> is the reference tail:
/// m0 = (100.6, 100.6, 100.1, 100.2) touches (low 100.1 at or below 100.2, close above 99.5),
/// m1 = (100.2, 100.9, 100.2, 100.85) is bullish, m2 = (100.85, 101.0, 100.75, 100.95) has low
/// 100.75 above high(m0) 100.6. m2 confirms a gap from 100.6 to 100.75, two bars after the touch.</para>
/// <para><b>Reference signal.</b> True ranges as of m2: 11 flat bars 0.1, m0 0.5, m1 0.7,
/// m2 max(0.25, 0.15, 0.1) = 0.25; ATR(M15) = 2.55 / 14 = 0.182142857. Gap 0.15 is at least
/// 0.25 x ATR = 0.0455. Entry = 100.75 - 0.5 x 0.15 = 100.675. The lowest low since the touch is
/// 100.1, above the distal 99.5, so Stop = 99.5 - 0.1 x 2.55 / 14 = 99.481785714. Target =
/// 100.675 + 2 x (100.675 - 99.481785714) = 103.061428571. m2 closes at the activation + 45
/// minutes.</para>
/// <para><b>D1.</b> <see cref="D1"/> builds dojis whose prices depend only on the distance from
/// the last bar, so a longer history ends with the same bars.</para>
/// <para><b>Mirror.</b> Reflecting H4 and M15 around <see cref="Pivot"/> 200.0 gives the supply
/// zone S-20260301-0800 (proximal 99.8, distal 100.5) and a sell signal with every level
/// reflected.</para>
/// </remarks>
public static class EngineScenarios
{
    /// <summary>Activation time of the reference zone: Monday 2026-03-02 00:00 UTC.</summary>
    public static readonly DateTime Activation = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Reflection constant for the mirrored scenarios.</summary>
    public const double Pivot = 200.0;

    /// <summary>Id of the reference demand zone.</summary>
    public const string ZoneId = "D-20260301-0800";

    /// <summary>Id of the mirrored (supply) zone.</summary>
    public const string MirroredZoneId = "S-20260301-0800";

    /// <summary>ATR(M15) as of the confirmation bar of the reference tail: 2.55 / 14.</summary>
    public const double ReferenceAtrM15 = 2.55 / 14.0;

    /// <summary>Entry of the reference signal: 100.75 - 0.5 x 0.15.</summary>
    public const double ReferenceEntry = 100.675;

    /// <summary>Stop of the reference signal: 99.5 - 0.1 x 2.55 / 14 = 99.481785714...</summary>
    public const double ReferenceStop = 99.48178571428571;

    /// <summary>Target of the reference signal: 100.675 + 2 x (100.675 - 99.481785714) = 103.061428571...</summary>
    public const double ReferenceTarget = 103.06142857142857;

    /// <summary>Default parameters.</summary>
    public static readonly SignalParameters Defaults = new();

    /// <summary>Warm-up with default parameters: 201 D1, 248 H4 and 1968 M15 bars.</summary>
    public const int D1Warmup = 201;

    /// <summary>H4 warm-up with default parameters.</summary>
    public const int H4Warmup = 248;

    /// <summary>M15 warm-up with default parameters.</summary>
    public const int M15Warmup = 1968;

    /// <summary>The reference M15 tail: touch bar m0, bullish m1, confirming m2.</summary>
    public static readonly (double Open, double High, double Low, double Close)[] TouchAndGap =
    {
        (100.6, 100.6, 100.1, 100.2),
        (100.2, 100.9, 100.2, 100.85),
        (100.85, 101.0, 100.75, 100.95),
    };

    /// <summary>
    /// Four bars that follow <see cref="TouchAndGap"/> (m3 to m6) and form a second valid gap for
    /// the zone: m3 = (100.95, 100.95, 100.3, 100.35), m4 = (100.35, 100.4, 100.25, 100.3),
    /// m5 = (100.3, 100.8, 100.3, 100.75) bullish, m6 = (100.75, 100.9, 100.6, 100.85).
    /// low(m6) 100.6 is above high(m4) 100.4: a gap of 0.2 whose lower edge 100.4 is within
    /// 100.7357, six bars after the touch.
    /// </summary>
    public static readonly (double Open, double High, double Low, double Close)[] SecondGap =
    {
        (100.95, 100.95, 100.3, 100.35),
        (100.35, 100.4, 100.25, 100.3),
        (100.3, 100.8, 100.3, 100.75),
        (100.75, 100.9, 100.6, 100.85),
    };

    /// <summary>
    /// <see cref="TouchAndGap"/> with m1 bearish, (100.2, 100.9, 100.1, 100.15), so m2 confirms no
    /// gap. Followed by <see cref="SecondGap"/>, the first valid gap is then confirmed by m6.
    /// </summary>
    public static readonly (double Open, double High, double Low, double Close)[] TouchWithoutGap =
    {
        (100.6, 100.6, 100.1, 100.2),
        (100.2, 100.9, 100.1, 100.15),
        (100.85, 101.0, 100.75, 100.95),
    };

    /// <summary>
    /// H4 bars whose last 31 bars are scenario A and whose leg bar opens at
    /// <paramref name="activation"/> - 4 hours. <paramref name="count"/> is the total, at least 31.
    /// </summary>
    public static IReadOnlyList<Bar> DemandH4(int count, DateTime? activation = null)
    {
        DateTime start = (activation ?? Activation).AddHours(-4.0 * count);
        return new BarBuilder(TimeFrame.H4)
            .At(start)
            .Filler(count - 31)
            .Filler(6)
            .Ohlc(100.0, 101.0, 99.5, 100.0)
            .Filler(22)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.0)
            .Build();
    }

    /// <summary>
    /// M15 bars: <paramref name="flatBefore"/> flat bars (100.6, 100.65, 100.55, 100.6) ending
    /// at <see cref="Activation"/>, then the tail bars from the activation on.
    /// </summary>
    public static IReadOnlyList<Bar> M15(int flatBefore, params (double Open, double High, double Low, double Close)[][] tails)
    {
        BarBuilder builder = new BarBuilder(TimeFrame.M15)
            .At(Activation.AddMinutes(-15.0 * flatBefore))
            .Repeat(flatBefore, (b, _) => b.Ohlc(100.6, 100.65, 100.55, 100.6));
        foreach ((double Open, double High, double Low, double Close)[] tail in tails)
        {
            foreach ((double open, double high, double low, double close) in tail)
            {
                builder.Ohlc(open, high, low, close);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// <paramref name="count"/> D1 dojis. The last opens one day before <see cref="Activation"/>
    /// plus <paramref name="closeOffset"/>, so it closes at the activation plus that offset.
    /// </summary>
    public static IReadOnlyList<Bar> D1(BiasHistory kind, int count, TimeSpan? closeOffset = null)
    {
        DateTime start = Activation.AddDays(-count) + (closeOffset ?? TimeSpan.Zero);
        return new BarBuilder(TimeFrame.D1)
            .At(start)
            .Repeat(count, (b, i) => b.Doji(D1Close(kind, count - 1 - i)))
            .Build();
    }

    /// <summary>
    /// The reference history: D1 bars of the given kind, <see cref="DemandH4"/> and
    /// <see cref="M15"/> with the given tail. Each timeframe has its warm-up requirement plus
    /// the extra count by the end of the first tail's third bar (m2) when the tail starts with
    /// three bars: D1 and H4 complete at the activation, M15 at m2.
    /// </summary>
    public static IReadOnlyList<FeedBar> History(
        BiasHistory bias,
        int extraD1,
        int extraH4,
        int extraM15,
        bool mirrored,
        params (double Open, double High, double Low, double Close)[][] tails)
    {
        IReadOnlyList<Bar> h4 = DemandH4(H4Warmup + extraH4);
        IReadOnlyList<Bar> m15 = M15(M15Warmup - 3 + extraM15, tails);
        if (mirrored)
        {
            h4 = h4.Mirror(Pivot);
            m15 = m15.Mirror(Pivot);
        }

        return EngineFeed.Merge(D1(bias, D1Warmup + extraD1), h4, m15);
    }

    /// <summary>Index in a feed of the M15 bar that opens at the given time.</summary>
    public static int IndexOfM15(IReadOnlyList<FeedBar> feed, DateTime openTimeUtc)
    {
        for (int i = 0; i < feed.Count; i++)
        {
            if (feed[i].TimeFrame == TimeFrame.M15 && feed[i].Bar.OpenTimeUtc == openTimeUtc)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No M15 bar opens at that time.");
    }

    /// <summary>Open time of tail bar k (m0 is the touch bar at the activation).</summary>
    public static DateTime TailOpen(int k)
    {
        return Activation.AddMinutes(15.0 * k);
    }

    private static double D1Close(BiasHistory kind, int fromEnd)
    {
        return kind switch
        {
            BiasHistory.Long => 70.0 - (0.1 * fromEnd),
            BiasHistory.Short => 30.0 + (0.1 * fromEnd),
            BiasHistory.Neutral => 50.0,
            BiasHistory.ShortThenLong => fromEnd == 0 ? 100.0 : 30.0 + (0.1 * fromEnd),
            BiasHistory.LongThenShort => fromEnd == 0 ? 10.0 : 70.0 - (0.1 * fromEnd),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown bias history."),
        };
    }
}
