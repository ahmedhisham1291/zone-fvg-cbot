using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034: in SessionRange mode the warm-up covers one trading day of M15 bars (100, the 25-hour
/// day in which summer time ends) besides what the bias and the two ATRs need: D1 at least
/// AtrPeriod + 1 bars for ATR(D1), H4 at least AtrPeriod + 1 bars for ATR(H4).
/// </summary>
public class WarmupSessionRangeTests
{
    private static int[] Values(IReadOnlyDictionary<TimeFrame, int> bars)
    {
        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, bars.Keys.ToArray());
        return bars.Values.ToArray();
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void For_DefaultParameters_SameAsTheOtherModes()
    {
        int[] range = Values(WarmupCalculator.For(new SignalParameters(), null, new SessionRangeParameters()));

        Assert.Equal(new[] { 201, 248, 1968 }, range);
        Assert.Equal(Values(WarmupCalculator.For(new SignalParameters())), range);
        Assert.Equal(Values(WarmupCalculator.For(new SignalParameters(), null)), Values(WarmupCalculator.For(new SignalParameters(), null, null)));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void For_FewM15Bars_OneTradingDayOfM15Bars()
    {
        var parameters = new SignalParameters { BiasEmaPeriod = 5, MaxZoneAgeBars = 1 };

        Assert.Equal(new[] { 21, 129, 64 }, Values(WarmupCalculator.For(parameters, null, null)));
        Assert.Equal(new[] { 21, 129, 100 }, Values(WarmupCalculator.For(parameters, null, new SessionRangeParameters())));
        Assert.Equal(100, WarmupCalculator.M15BarsPerTradingDay);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void For_AtrPeriodLongerThanTheBiasWindow_D1CoversAtrD1()
    {
        var parameters = new SignalParameters { BiasEmaPeriod = 2, AtrPeriod = 30 };

        // Bias window 4 x 2 + 1 = 9 D1 bars; ATR(D1) needs 31. H4 already holds AtrPeriod + 2.
        Assert.Equal(new[] { 9, 264, 1984 }, Values(WarmupCalculator.For(parameters, null, null)));
        Assert.Equal(new[] { 31, 264, 1984 }, Values(WarmupCalculator.For(parameters, null, new SessionRangeParameters())));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void For_BreakoutAndSessionRangeTogether_Refused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => WarmupCalculator.For(new SignalParameters(), new BreakoutParameters(), new SessionRangeParameters()));

        Assert.Equal("sessionRange", error.ParamName);
        Assert.Throws<ArgumentException>(() => new BarStore(new SignalParameters(), new BreakoutParameters(), new SessionRangeParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void For_InvalidSessionRangeParameters_Refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupCalculator.For(new SignalParameters(), null, new SessionRangeParameters { StopAtrMult = 0.0 }));
        Assert.Throws<ArgumentNullException>(() => WarmupCalculator.For(null!, null, new SessionRangeParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void BarStore_SessionRange_SizedForItsWarmup()
    {
        var parameters = new SignalParameters { BiasEmaPeriod = 5, MaxZoneAgeBars = 1 };

        var store = new BarStore(parameters, null, new SessionRangeParameters());

        Assert.Equal(new[] { 21, 129, 100 }, Values(store.WarmupBars));
        Assert.Equal(100 + BarStore.CapacityMargin, store.Capacity(TimeFrame.M15));
        Assert.Equal(64 + BarStore.CapacityMargin, new BarStore(parameters, null).Capacity(TimeFrame.M15));
    }
}
