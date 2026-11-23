using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The breakout filters through the engine (SIG-028, SIG-029) on the seeded random-walk histories
/// of the SIG-026 property tests: 70 weekdays, BiasEmaPeriod 5, MaxZoneAgeBars 20. The expected
/// signals are derived from an engine without filters and an independent EMA computation over the
/// D1 bars fed so far, so the engine under test is never its own reference. A failure names its seed.
/// </summary>
public class SignalEngineBreakoutFilterTests
{
    private const int TradingDays = 70;

    private static readonly SignalParameters Short = new() { BiasEmaPeriod = 5, MaxZoneAgeBars = 20 };

    private readonly ITestOutputHelper _output;

    public SignalEngineBreakoutFilterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The 20 seeds of the SIG-026 property tests: 1001, 1038, ..., 1704.</summary>
    public static IEnumerable<object[]> Seeds => Enumerable.Range(0, 20).Select(k => new object[] { 1001 + (37 * k) });

    /// <summary>The 20 seeds, each with a slow period of 8 (even k) or 12 (odd k).</summary>
    public static IEnumerable<object[]> SeedsWithSlowPeriod => Enumerable.Range(0, 20).Select(k => new object[] { 1001 + (37 * k), k % 2 == 0 ? 8 : 12 });

    private sealed record Step(FeedBar Input, IReadOnlyList<Signal> Signals, BreakoutReference? Reference, bool IsReady, Bias Bias, int D1Count);

    private static IReadOnlyList<Step> Run(SignalEngine engine, IEnumerable<FeedBar> feed)
    {
        var steps = new List<Step>();
        int d1 = 0;
        foreach (FeedBar item in feed)
        {
            d1 += item.TimeFrame == TimeFrame.D1 ? 1 : 0;
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, false).ToArray();
            steps.Add(new Step(item, signals, engine.Reference, engine.IsReady, engine.Bias, d1));
        }

        return steps;
    }

    /// <summary>Fast and slow D1 EMA after each step, computed here from the D1 bars of the feed.</summary>
    private static IReadOnlyList<(double? Fast, double? Slow)> Trend(IEnumerable<FeedBar> feed, int fast, int slow)
    {
        var d1 = new List<Bar>();
        var result = new List<(double?, double?)>();
        (double?, double?) current = (null, null);
        foreach (FeedBar item in feed)
        {
            if (item.TimeFrame == TimeFrame.D1)
            {
                d1.Add(item.Bar);
                current = (Ema.Value(d1, d1.Count - 1, fast), Ema.Value(d1, d1.Count - 1, slow));
            }

            result.Add(current);
        }

        return result;
    }

    private static bool TrendAllows(Side side, (double? Fast, double? Slow) trend)
    {
        if (trend.Fast is null || trend.Slow is null)
        {
            return false;
        }

        return side == Side.Buy
            ? PriceMath.GreaterThan(trend.Fast.Value, trend.Slow.Value, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(trend.Fast.Value, trend.Slow.Value, PriceMath.DefaultTolerance);
    }

    private static string Keys(IEnumerable<Signal> signals)
    {
        return string.Join(" / ", signals.Select(ExactRecords.Key));
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ warm-up (SIG-028: the D1 warm-up covers the slow EMA)

    [Fact]
    [Trait("Req", "SIG-028")]
    public void WarmupFor_TrendFilter200_D1Is801AndTheOthersAreUnchanged()
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 200 });

        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, warmup.Keys.ToArray());
        Assert.Equal(801, warmup[TimeFrame.D1]);
        Assert.Equal(248, warmup[TimeFrame.H4]);
        Assert.Equal(1968, warmup[TimeFrame.M15]);
    }

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(50, 20, 201)] // the bias EMA is the larger period
    [InlineData(50, 50, 201)]
    [InlineData(50, 51, 205)]
    [InlineData(5, 8, 33)]
    [InlineData(250, 200, 1001)]
    public void WarmupFor_TrendFilterOn_D1UsesTheLargerOfTheTwoPeriods(int biasPeriod, int slowPeriod, int expected)
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(new SignalParameters { BiasEmaPeriod = biasPeriod }, new BreakoutParameters { TrendSlowEmaPeriod = slowPeriod });

        Assert.Equal(expected, warmup[TimeFrame.D1]);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void WarmupFor_FilterOffOrZoneFvg_EqualsTheWarmupBefore()
    {
        var parameters = new SignalParameters { BiasEmaPeriod = 100 };
        IReadOnlyDictionary<TimeFrame, int> before = WarmupCalculator.For(parameters);

        foreach (BreakoutParameters? breakout in new BreakoutParameters?[] { null, new BreakoutParameters(), new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) } })
        {
            IReadOnlyDictionary<TimeFrame, int> after = WarmupCalculator.For(parameters, breakout);
            Assert.Equal(before.ToArray(), after.ToArray());
        }

        Assert.Equal(401, before[TimeFrame.D1]);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void WarmupFor_InvalidOrNullParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupCalculator.For(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupCalculator.For(new SignalParameters { BiasEmaPeriod = 0 }, new BreakoutParameters()));
        Assert.Throws<ArgumentNullException>(() => WarmupCalculator.For(null!, new BreakoutParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void BarStoreAndEngine_TrendFilter200_HoldTheSlowEmaWindow()
    {
        var store = new BarStore(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 200 });
        var off = new BarStore(new SignalParameters(), null);
        var engine = new SignalEngine(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 200 });

        Assert.Equal(801 + BarStore.CapacityMargin, store.Capacity(TimeFrame.D1));
        Assert.Equal(248 + BarStore.CapacityMargin, store.Capacity(TimeFrame.H4));
        Assert.Equal(201 + BarStore.CapacityMargin, off.Capacity(TimeFrame.D1));
        Assert.Equal(801, engine.WarmupBars[TimeFrame.D1]);
        Assert.Equal(201, new SignalEngine(new SignalParameters()).WarmupBars[TimeFrame.D1]);
        Assert.Equal(201, new SignalEngine(new SignalParameters(), new BreakoutParameters()).WarmupBars[TimeFrame.D1]);
    }

    // ------------------------------------------------------------------ the filters off: the results before, bit for bit

    [Theory]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    [MemberData(nameof(Seeds))]
    public void OnBarClosed_FiltersOffExplicitly_EveryStepEqualsTheEngineWithDefaultParameters(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            var off = new BreakoutParameters { TrendSlowEmaPeriod = 0, LastConfirmUtc = TimeSpan.FromHours(17.0) };
            IReadOnlyList<Step> a = Run(new SignalEngine(Short, off), history.Feed);
            IReadOnlyList<Step> b = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed);

            int signals = 0;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(Equals(b[i].Reference, a[i].Reference), "step " + Text(i) + ": reference differs");
                Assert.Equal(b[i].IsReady, a[i].IsReady);
                Assert.Equal(Keys(b[i].Signals), Keys(a[i].Signals));
                signals += a[i].Signals.Count;
            }

            Assert.True(signals > 0, "no signal was compared");
        });
    }

    // ------------------------------------------------------------------ SIG-028 through the engine

    [Theory]
    [Trait("Req", "SIG-028")]
    [MemberData(nameof(SeedsWithSlowPeriod))]
    public void OnBarClosed_TrendFilterOn_SignalsAreThoseWithoutTheFilterThatTheEmaOrderAllows(int seed, int slowPeriod)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<Step> on = Run(new SignalEngine(Short, new BreakoutParameters { TrendSlowEmaPeriod = slowPeriod }), history.Feed);
            IReadOnlyList<Step> off = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed);
            IReadOnlyList<(double? Fast, double? Slow)> trend = Trend(history.Feed, Short.BiasEmaPeriod, slowPeriod);

            int kept = 0;
            int blocked = 0;
            for (int i = 0; i < on.Count; i++)
            {
                Assert.Equal(off[i].Bias, on[i].Bias);
                if (!on[i].IsReady)
                {
                    Assert.Empty(on[i].Signals);
                    continue;
                }

                List<Signal> expected = off[i].Signals.Where(signal => TrendAllows(signal.Side, trend[i])).ToList();
                Assert.Equal("step " + Text(i) + ": " + Keys(expected), "step " + Text(i) + ": " + Keys(on[i].Signals));
                kept += expected.Count;
                blocked += off[i].Signals.Count - expected.Count;
            }

            _output.WriteLine("seed " + Text(seed) + ", slow " + Text(slowPeriod) + ": " + Text(kept) + " kept, " + Text(blocked) + " blocked");
        });
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnBarClosed_TrendFilterOnOverAllSeeds_KeepsSomeSignalsAndBlocksOthers()
    {
        int kept = 0;
        int blocked = 0;
        foreach (int seed in Seeds.Select(row => (int)row[0]))
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<Step> on = Run(new SignalEngine(Short, new BreakoutParameters { TrendSlowEmaPeriod = 8 }), history.Feed);
            IReadOnlyList<Step> off = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed);
            for (int i = 0; i < on.Count; i++)
            {
                if (on[i].IsReady)
                {
                    kept += on[i].Signals.Count;
                    blocked += off[i].Signals.Count - on[i].Signals.Count;
                }
            }
        }

        _output.WriteLine(Text(kept) + " kept, " + Text(blocked) + " blocked");
        Assert.True(kept > 0, "the filter kept no signal");
        Assert.True(blocked > 0, "the filter blocked no signal");
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnBarClosed_NoSlowEmaYet_NoReferenceIsConfirmedAndEachStaysArmed()
    {
        // Slow period 8: the slow EMA needs 32 D1 bars. Before that the tracker confirms nothing,
        // so every reference stays Armed, while the engine without the filter does confirm.
        int doneWithout = 0;
        foreach (int seed in Seeds.Take(5).Select(row => (int)row[0]))
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<Step> on = Run(new SignalEngine(Short, new BreakoutParameters { TrendSlowEmaPeriod = 8 }), history.Feed);
            IReadOnlyList<Step> off = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed);
            for (int i = 0; i < on.Count && on[i].D1Count < 32; i++)
            {
                Assert.True(on[i].Reference is null || on[i].Reference!.Armed, "seed " + Text(seed) + ", step " + Text(i) + ": the reference is Done without a slow EMA");
                Assert.Empty(on[i].Signals);
                doneWithout += off[i].Reference is { Armed: false } && off[i].Input.TimeFrame == TimeFrame.M15 && (i == 0 || off[i - 1].Reference is { Armed: true }) ? 1 : 0;
            }
        }

        _output.WriteLine(Text(doneWithout) + " confirmations without the filter before the slow EMA exists");
        Assert.True(doneWithout > 0, "without the filter no reference was confirmed in that time, so the test shows nothing");
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void ZoneFvgEngine_IsUnchanged_ItsWarmupAndBiasEqualThoseBefore()
    {
        RandomWalkHistory history = RandomWalkHistory.Generate(1001, TradingDays);
        IReadOnlyList<Step> zoneFvg = Run(new SignalEngine(Short), history.Feed);
        IReadOnlyList<Step> breakout = Run(new SignalEngine(Short, new BreakoutParameters { TrendSlowEmaPeriod = 8 }), history.Feed);

        Assert.Equal(WarmupCalculator.For(Short).ToArray(), new SignalEngine(Short).WarmupBars.ToArray());
        Assert.Equal(WarmupCalculator.For(Short).ToArray(), WarmupCalculator.For(Short, null).ToArray());
        for (int i = 0; i < zoneFvg.Count; i++)
        {
            Assert.Equal(BiasCalculator.Compute(history.D1.Take(zoneFvg[i].D1Count).ToList(), Short.BiasEmaPeriod), zoneFvg[i].Bias);
            Assert.Equal(zoneFvg[i].Bias, breakout[i].Bias);
            Assert.Null(zoneFvg[i].Reference);
            Assert.All(zoneFvg[i].Signals, signal => Assert.Equal(SignalKind.ZoneFvg, signal.Kind));
        }
    }

    // ------------------------------------------------------------------ SIG-029 through the engine

    [Theory]
    [Trait("Req", "SIG-029")]
    [MemberData(nameof(Seeds))]
    public void OnBarClosed_LastConfirm0800_SignalsAreThoseWithoutTheRuleThatCloseBefore0800(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<Step> on = Run(new SignalEngine(Short, new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) }), history.Feed);
            IReadOnlyList<Step> off = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed);

            for (int i = 0; i < on.Count; i++)
            {
                List<Signal> expected = off[i].Signals.Where(signal => signal.ConfirmationCloseUtc.TimeOfDay < TimeSpan.FromHours(8.0)).ToList();
                Assert.Equal("step " + Text(i) + ": " + Keys(expected), "step " + Text(i) + ": " + Keys(on[i].Signals));
                if (on[i].Input.TimeFrame == TimeFrame.M15 && on[i].Input.CloseUtc.TimeOfDay >= TimeSpan.FromHours(8.0) && on[i].Input.CloseUtc.TimeOfDay < TimeSpan.FromHours(17.0) && on[i].Reference is not null && on[i].Input.Bar.OpenTimeUtc >= on[i].Reference!.DayOpenUtc.AddDays(1.0))
                {
                    Assert.False(on[i].Reference!.Armed, "step " + Text(i) + ": a checked bar at or after 08:00 left the reference Armed");
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnBarClosed_LastConfirm0800OverAllSeeds_KeepsSomeSignalsAndRemovesOthers()
    {
        int kept = 0;
        int removed = 0;
        foreach (int seed in Seeds.Select(row => (int)row[0]))
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            int on = Run(new SignalEngine(Short, new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) }), history.Feed).Sum(step => step.Signals.Count);
            int off = Run(new SignalEngine(Short, new BreakoutParameters()), history.Feed).Sum(step => step.Signals.Count);
            kept += on;
            removed += off - on;
        }

        _output.WriteLine(Text(kept) + " kept, " + Text(removed) + " removed");
        Assert.True(kept > 0, "the rule kept no signal");
        Assert.True(removed > 0, "the rule removed no signal");
    }
}
