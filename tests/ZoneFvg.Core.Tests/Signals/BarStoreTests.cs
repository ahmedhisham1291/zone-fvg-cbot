using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bar intake tests (SIG-004), readiness (SIG-020) and "each bar exactly once" (SIG-001).
/// Bar i of a filled series closes at 100 + i, so a close identifies its bar.
/// </summary>
public class BarStoreTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_BarsInIncreasingTimeOrder_AreStoredOldestFirst()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.M15, 3);

        foreach (Bar bar in bars)
        {
            store.Add(TimeFrame.M15, bar);
        }

        Assert.Equal(3, store.Count(TimeFrame.M15));
        Assert.Equal(bars.ToArray(), store.Bars(TimeFrame.M15).ToArray());
        Assert.Same(bars[2], store.Last(TimeFrame.M15, 0));
        Assert.Same(bars[1], store.Last(TimeFrame.M15, 1));
        Assert.Same(bars[0], store.Last(TimeFrame.M15, 2));
        Assert.Same(bars[0], store.Bars(TimeFrame.M15)[0]);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_BarAfterAGapInTime_IsAccepted()
    {
        // Strictly increasing is the only rule: a weekend gap is fine.
        var store = new BarStore(new SignalParameters());
        Bar friday = new(Start.AddDays(4.0).AddHours(20.0), 1.1, 1.1, 1.1, 1.1);
        Bar sunday = new(Start.AddDays(6.0).AddHours(21.0), 1.2, 1.2, 1.2, 1.2);

        store.Add(TimeFrame.H4, friday);
        store.Add(TimeFrame.H4, sunday);

        Assert.Equal(2, store.Count(TimeFrame.H4));
        Assert.Same(sunday, store.Last(TimeFrame.H4, 0));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    [Trait("Req", "SIG-001")]
    public void Add_SameBarTwice_IsRejectedAndStateIsUnchanged()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.M15, 3);
        AddAll(store, TimeFrame.M15, bars);

        var exception = Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.M15, bars[2]));

        Assert.Contains("M15", exception.Message);
        AssertHolds(store, TimeFrame.M15, bars);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_DifferentBarWithTheSameOpenTime_IsRejectedAndStateIsUnchanged()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.H4, 3);
        AddAll(store, TimeFrame.H4, bars);
        var sameTimeOtherPrices = new Bar(bars[2].OpenTimeUtc, 7.0, 9.0, 6.0, 8.0);

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.H4, sameTimeOtherPrices));

        AssertHolds(store, TimeFrame.H4, bars);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_BarOneBarEarlierThanTheNewest_IsRejectedAndStateIsUnchanged()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.M15, 3);
        AddAll(store, TimeFrame.M15, bars);
        var earlier = new Bar(bars[2].OpenTimeUtc.AddMinutes(-15.0), 7.0, 9.0, 6.0, 8.0);

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.M15, earlier));

        AssertHolds(store, TimeFrame.M15, bars);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_BarOneSecondEarlierThanTheNewest_IsRejected()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.M15, 3);
        AddAll(store, TimeFrame.M15, bars);
        var oneSecondEarlier = new Bar(bars[2].OpenTimeUtc.AddSeconds(-1.0), 7.0, 9.0, 6.0, 8.0);
        var oneSecondLater = new Bar(bars[2].OpenTimeUtc.AddSeconds(1.0), 7.0, 9.0, 6.0, 8.0);

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.M15, oneSecondEarlier));
        AssertHolds(store, TimeFrame.M15, bars);

        store.Add(TimeFrame.M15, oneSecondLater);
        Assert.Same(oneSecondLater, store.Last(TimeFrame.M15, 0));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_BarOlderThanEveryStoredBar_IsRejectedAndStateIsUnchanged()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.D1, 3);
        AddAll(store, TimeFrame.D1, bars);
        var muchEarlier = new Bar(Start.AddDays(-30.0), 7.0, 9.0, 6.0, 8.0);

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.D1, muchEarlier));

        AssertHolds(store, TimeFrame.D1, bars);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_RejectionOnOneTimeFrame_LeavesTheOtherTimeFramesUntouched()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> daily = Series(TimeFrame.D1, 2);
        IReadOnlyList<Bar> fourHour = Series(TimeFrame.H4, 4);
        IReadOnlyList<Bar> quarterHour = Series(TimeFrame.M15, 5);
        AddAll(store, TimeFrame.D1, daily);
        AddAll(store, TimeFrame.H4, fourHour);
        AddAll(store, TimeFrame.M15, quarterHour);

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.H4, fourHour[0]));

        AssertHolds(store, TimeFrame.D1, daily);
        AssertHolds(store, TimeFrame.H4, fourHour);
        AssertHolds(store, TimeFrame.M15, quarterHour);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_SameOpenTimeOnDifferentTimeFrames_IsAccepted()
    {
        // The D1, H4 and M15 bars that open at midnight share an open time; order is per timeframe.
        var store = new BarStore(new SignalParameters());
        var bar = new Bar(Start, 1.1, 1.1, 1.1, 1.1);

        store.Add(TimeFrame.D1, bar);
        store.Add(TimeFrame.H4, bar);
        store.Add(TimeFrame.M15, bar);

        Assert.Single(store.Bars(TimeFrame.D1));
        Assert.Single(store.Bars(TimeFrame.H4));
        Assert.Single(store.Bars(TimeFrame.M15));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_RejectedBarOnAFullBuffer_LeavesStateUnchanged()
    {
        // BiasEmaPeriod 1 gives a D1 capacity of 21 (see Capacity test); 30 bars make the buffer wrap.
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 1 });
        IReadOnlyList<Bar> bars = Series(TimeFrame.D1, 30);
        AddAll(store, TimeFrame.D1, bars);
        Bar[] before = store.Bars(TimeFrame.D1).ToArray();

        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.D1, bars[29]));
        Assert.Throws<InvalidOperationException>(() => store.Add(TimeFrame.D1, bars[0]));

        Assert.Equal(before, store.Bars(TimeFrame.D1).ToArray());
        Assert.Equal(21, store.Count(TimeFrame.D1));
        Assert.Same(bars[29], store.Last(TimeFrame.D1, 0));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Add_NullBar_ThrowsAndStoresNothing()
    {
        var store = new BarStore(new SignalParameters());

        Assert.Throws<ArgumentNullException>("bar", () => store.Add(TimeFrame.M15, null!));

        Assert.Empty(store.Bars(TimeFrame.M15));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void Members_UnknownTimeFrame_Throw()
    {
        var store = new BarStore(new SignalParameters());
        var bar = new Bar(Start, 1.1, 1.1, 1.1, 1.1);
        const TimeFrame unknown = (TimeFrame)99;

        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => store.Add(unknown, bar));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => store.Last(unknown, 0));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => store.Bars(unknown));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => store.Count(unknown));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => store.Capacity(unknown));
    }

    [Fact]
    public void Last_IndexOutsideTheStoredBars_Throws()
    {
        var store = new BarStore(new SignalParameters());
        AddAll(store, TimeFrame.H4, Series(TimeFrame.H4, 2));

        Assert.Throws<ArgumentOutOfRangeException>("n", () => store.Last(TimeFrame.H4, -1));
        Assert.Throws<ArgumentOutOfRangeException>("n", () => store.Last(TimeFrame.H4, 2));
        Assert.Throws<ArgumentOutOfRangeException>("n", () => store.Last(TimeFrame.D1, 0));
    }

    [Fact]
    public void Capacity_PerTimeFrame_IsWarmupPlusMargin()
    {
        var store = new BarStore(new SignalParameters());
        int margin = BarStore.CapacityMargin;

        // Default warm-up is 201 D1, 248 H4 and 1968 M15 bars (WarmupCalculatorTests); margin is 16.
        Assert.Equal(16, margin);
        Assert.Equal(217, store.Capacity(TimeFrame.D1));
        Assert.Equal(264, store.Capacity(TimeFrame.H4));
        Assert.Equal(1984, store.Capacity(TimeFrame.M15));
    }

    [Fact]
    public void Add_MoreBarsThanCapacity_DropsTheOldestAndKeepsTheOrder()
    {
        // BiasEmaPeriod 1: D1 warm-up = 4 x 1 + 1 = 5, capacity = 5 + 16 = 21.
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 1 });
        IReadOnlyList<Bar> bars = Series(TimeFrame.D1, 30);

        AddAll(store, TimeFrame.D1, bars);

        // 30 bars into 21 places: bars 0 to 8 are gone, bars 9 to 29 remain, oldest first.
        Assert.Equal(21, store.Capacity(TimeFrame.D1));
        Assert.Equal(21, store.Count(TimeFrame.D1));
        Assert.Equal(bars.Skip(9).ToArray(), store.Bars(TimeFrame.D1).ToArray());
        Assert.Same(bars[29], store.Last(TimeFrame.D1, 0));
        Assert.Same(bars[9], store.Last(TimeFrame.D1, 20));
        Assert.Same(bars[9], store.Bars(TimeFrame.D1)[0]);
        Assert.Same(bars[29], store.Bars(TimeFrame.D1)[20]);
        Assert.Throws<ArgumentOutOfRangeException>("n", () => store.Last(TimeFrame.D1, 21));
    }

    [Fact]
    public void Add_SeveralTimesTheCapacity_KeepsExactlyTheNewestBars()
    {
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 1 });
        IReadOnlyList<Bar> bars = Series(TimeFrame.D1, 100);

        AddAll(store, TimeFrame.D1, bars);

        // 100 bars into 21 places: bars 79 to 99 remain.
        Assert.Equal(bars.Skip(79).ToArray(), store.Bars(TimeFrame.D1).ToArray());
        Assert.Equal(179.0, store.Bars(TimeFrame.D1)[0].Close);
        Assert.Equal(199.0, store.Last(TimeFrame.D1, 0).Close);
    }

    [Fact]
    public void Add_ExactlyCapacityBars_DropsNothing()
    {
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 1 });
        IReadOnlyList<Bar> bars = Series(TimeFrame.D1, 21);

        AddAll(store, TimeFrame.D1, bars);

        Assert.Equal(bars.ToArray(), store.Bars(TimeFrame.D1).ToArray());
    }

    [Fact]
    public void Bars_IsALiveReadOnlyViewOldestFirst()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> view = store.Bars(TimeFrame.H4);
        IReadOnlyList<Bar> bars = Series(TimeFrame.H4, 2);

        Assert.Empty(view);
        store.Add(TimeFrame.H4, bars[0]);
        Assert.Single(view);
        store.Add(TimeFrame.H4, bars[1]);

        Assert.Equal(2, view.Count);
        Assert.Same(bars[0], view[0]);
        Assert.Same(bars[1], view[1]);
        Assert.Throws<ArgumentOutOfRangeException>("index", () => view[2]);
        Assert.Throws<ArgumentOutOfRangeException>("index", () => view[-1]);
    }

    [Fact]
    public void Bars_EnumeratedThroughTheNonGenericInterface_YieldsTheSameBars()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.H4, 3);
        AddAll(store, TimeFrame.H4, bars);
        System.Collections.IEnumerable untyped = store.Bars(TimeFrame.H4);

        var seen = new List<object?>();
        foreach (object? item in untyped)
        {
            seen.Add(item);
        }

        Assert.Equal(3, seen.Count);
        Assert.Same(bars[0], seen[0]);
        Assert.Same(bars[2], seen[2]);
    }

    [Fact]
    public void Bars_EnumerationStartedBeforeAnAdd_IsNotDisturbedByIt()
    {
        var store = new BarStore(new SignalParameters());
        IReadOnlyList<Bar> bars = Series(TimeFrame.H4, 3);
        store.Add(TimeFrame.H4, bars[0]);
        store.Add(TimeFrame.H4, bars[1]);

        using IEnumerator<Bar> enumerator = store.Bars(TimeFrame.H4).GetEnumerator();
        store.Add(TimeFrame.H4, bars[2]);

        var seen = new List<Bar>();
        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current);
        }

        // The enumeration is a snapshot taken when it started: the two bars stored at that time.
        Assert.Equal(2, seen.Count);
        Assert.Same(bars[0], seen[0]);
        Assert.Same(bars[1], seen[1]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void WarmupBars_MatchTheCalculatorForTheSameParameters()
    {
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 100 });

        // D1 = 4 x 100 + 1 = 401; H4 and M15 keep their default requirement.
        Assert.Equal(3, store.WarmupBars.Count);
        Assert.Equal(401, store.WarmupBars[TimeFrame.D1]);
        Assert.Equal(248, store.WarmupBars[TimeFrame.H4]);
        Assert.Equal(1968, store.WarmupBars[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void IsReady_NoBars_IsFalse()
    {
        Assert.False(new BarStore(new SignalParameters()).IsReady);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData(200, 248, 1968)] // D1 one bar short of 201
    [InlineData(201, 247, 1968)] // H4 one bar short of 248
    [InlineData(201, 248, 1967)] // M15 one bar short of 1968
    [InlineData(0, 248, 1968)] // no D1 bars at all
    [InlineData(201, 0, 1968)] // no H4 bars at all
    [InlineData(201, 248, 0)] // no M15 bars at all
    public void IsReady_AnyTimeFrameBelowItsRequirement_IsFalse(int d1Bars, int h4Bars, int m15Bars)
    {
        var store = new BarStore(new SignalParameters());

        AddAll(store, TimeFrame.D1, Series(TimeFrame.D1, d1Bars));
        AddAll(store, TimeFrame.H4, Series(TimeFrame.H4, h4Bars));
        AddAll(store, TimeFrame.M15, Series(TimeFrame.M15, m15Bars));

        Assert.False(store.IsReady);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void IsReady_EveryTimeFrameExactlyAtItsRequirement_IsTrue()
    {
        var store = new BarStore(new SignalParameters());

        AddAll(store, TimeFrame.D1, Series(TimeFrame.D1, 201));
        AddAll(store, TimeFrame.H4, Series(TimeFrame.H4, 248));
        AddAll(store, TimeFrame.M15, Series(TimeFrame.M15, 1968));

        Assert.True(store.IsReady);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData(TimeFrame.D1)]
    [InlineData(TimeFrame.H4)]
    [InlineData(TimeFrame.M15)]
    public void IsReady_TheLastMissingBarArrives_TurnsTrue(TimeFrame lastToFill)
    {
        var store = new BarStore(new SignalParameters());
        foreach (TimeFrame timeFrame in Enum.GetValues<TimeFrame>())
        {
            int needed = store.WarmupBars[timeFrame];
            IReadOnlyList<Bar> bars = Series(timeFrame, needed);
            AddAll(store, timeFrame, timeFrame == lastToFill ? bars.FirstBars(needed - 1) : bars);
        }

        Assert.False(store.IsReady);

        IReadOnlyList<Bar> full = Series(lastToFill, store.WarmupBars[lastToFill]);
        store.Add(lastToFill, full[full.Count - 1]);

        Assert.True(store.IsReady);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void IsReady_AfterTheBuffersOverflow_StaysTrue()
    {
        var store = new BarStore(new SignalParameters());

        // More bars than each capacity (217, 264, 1984): the oldest are dropped, readiness stays.
        AddAll(store, TimeFrame.D1, Series(TimeFrame.D1, 250));
        AddAll(store, TimeFrame.H4, Series(TimeFrame.H4, 300));
        AddAll(store, TimeFrame.M15, Series(TimeFrame.M15, 2100));

        Assert.True(store.IsReady);
        Assert.Equal(217, store.Count(TimeFrame.D1));
        Assert.Equal(264, store.Count(TimeFrame.H4));
        Assert.Equal(1984, store.Count(TimeFrame.M15));
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void IsReady_WithBiasEmaPeriod200_NeedsEightHundredAndOneD1Bars()
    {
        var store = new BarStore(new SignalParameters { BiasEmaPeriod = 200 });
        AddAll(store, TimeFrame.H4, Series(TimeFrame.H4, 248));
        AddAll(store, TimeFrame.M15, Series(TimeFrame.M15, 1968));
        IReadOnlyList<Bar> daily = Series(TimeFrame.D1, 801);

        AddAll(store, TimeFrame.D1, daily.FirstBars(800));
        Assert.False(store.IsReady);

        store.Add(TimeFrame.D1, daily[800]);
        Assert.True(store.IsReady);
    }

    [Fact]
    public void Constructor_NullParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>("parameters", () => new BarStore(null!));
    }

    [Fact]
    public void Constructor_InvalidParameters_ThrowsAndNamesTheParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BarStore(new SignalParameters { MaxZoneAgeBars = 0 }));

        Assert.Equal("MaxZoneAgeBars", exception.ParamName);
    }

    /// <summary>A series of flat bars; bar i opens at Start + i bar lengths and closes at 100 + i.</summary>
    private static IReadOnlyList<Bar> Series(TimeFrame timeFrame, int count)
    {
        return new BarBuilder(timeFrame)
            .At(Start)
            .Repeat(count, (builder, index) => builder.Doji(100.0 + index))
            .Build();
    }

    private static void AddAll(BarStore store, TimeFrame timeFrame, IReadOnlyList<Bar> bars)
    {
        foreach (Bar bar in bars)
        {
            store.Add(timeFrame, bar);
        }
    }

    private static void AssertHolds(BarStore store, TimeFrame timeFrame, IReadOnlyList<Bar> expected)
    {
        Assert.Equal(expected.Count, store.Count(timeFrame));
        Assert.Equal(expected.ToArray(), store.Bars(timeFrame).ToArray());
        Assert.Same(expected[expected.Count - 1], store.Last(timeFrame, 0));
    }
}
