using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The bars of <see cref="BreakoutRecoveryData"/> carried over the weekend to Monday 2026-03-09,
/// with the D1 series one bar behind (SIG-027). The D1 bars open at 00:00 UTC; Friday's bar (Friday
/// 00:00 to Saturday 00:00) is missing, so the last D1 bar is Thursday's: it opens Thursday
/// 2026-03-05 00:00, has the high 100.5 and closed on Friday 00:00 (breakout id BH-20260305-0000).
/// The Friday break bar opens at 21:00 and closes at 21:15, outside the trading window, so no
/// Friday bar confirms. Monday's bars run from 00:00: H4 bars as on the other days, M15 bars open
/// 100.54, high 100.6, low 100.5, close 100.55, each closing above the high of Thursday's bar.
/// </summary>
public static class BreakoutMondayData
{
    /// <summary>Monday 2026-03-09 00:00 UTC.</summary>
    public static readonly DateTime Monday = BreakoutRecoveryData.Wednesday.AddDays(5.0);

    /// <summary>Open time of the missing Friday D1 bar: Friday 2026-03-06 00:00 UTC.</summary>
    public static readonly DateTime Friday = BreakoutRecoveryData.Wednesday.AddDays(2.0);

    /// <summary>The bar set of the remarks: Monday's H4 bars to 16:00 and M15 bars to 17:00 are added.</summary>
    public static BarSet WithoutFridayD1()
    {
        BarSet bars = BreakoutRecoveryData.Bars(friday: TimeSpan.FromHours(21.0));
        List<Bar> d1 = bars.D1.Where(bar => bar.OpenTimeUtc != Friday).ToList();
        IReadOnlyList<Bar> mondayH4 = new BarBuilder(TimeFrame.H4)
            .At(Monday)
            .Repeat(4, (b, _) => b.Ohlc(100.0, 100.2, 99.8, 100.0))
            .Build();
        IReadOnlyList<Bar> mondayM15 = new BarBuilder(TimeFrame.M15)
            .At(Monday)
            .Repeat(68, (b, _) => b.Ohlc(100.54, 100.6, 100.5, 100.55))
            .Build();
        return new BarSet(d1, bars.H4.Concat(mondayH4).ToList(), bars.M15.Concat(mondayM15).ToList());
    }
}
