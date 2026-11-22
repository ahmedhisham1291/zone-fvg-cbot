using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-026 on a hand-built history: the engine in Breakout mode keeps the warm-up gate and the
/// intake order of ZoneFvg mode and turns the tracker's setups into signals.
/// </summary>
/// <remarks>
/// <para>Parameters: BiasEmaPeriod 5 and MaxZoneAgeBars 1, so the warm-up is 21 D1, 129 H4 and
/// 64 M15 bars. Bars run without gaps from Thursday 2026-01-01 00:00 UTC.</para>
/// <para>D1 bar of day d (opening 2026-01-01 + d days): open 1.0 + 0.001 d, close open + 0.0008,
/// high close + 0.0001, low open - 0.0001; a rising market, so the bias is Long once the EMA
/// window of 20 bars is full. Day 20 has the high 1.0209, day 21 the high 1.0219.</para>
/// <para>H4 bars: open and close 1.0, high 1.002, low 0.998, so ATR(H4) is 0.004. The 129th H4
/// bar closes on day 21 (2026-01-22) at 12:00: the engine becomes ready at that bar.</para>
/// <para>M15 bars: open and close 1.0, high 1.0005, low 0.9995 (true range 0.001), below every
/// reference high. Breakout bars (open 1.0, high 1.03, low 0.9995, close 1.025) open on
/// 2026-01-22 from 11:30 to 12:30 and on 2026-01-23 at 09:00 and 09:15.</para>
/// <para>So the bar 11:30 to 11:45 of 2026-01-22 confirms a buy of the reference day 21 (opening
/// 2026-01-21) while the engine is not ready: no signal, and the reference is Done. The bar
/// 09:00 to 09:15 of 2026-01-23 confirms the reference of day 22 (opening 2026-01-22) when the
/// engine is ready: one signal. ATR(M15) at that bar is (13 x 0.001 + 0.0305) / 14.</para>
/// </remarks>
public class SignalEngineBreakoutTests
{
    private static readonly SignalParameters Short = new() { BiasEmaPeriod = 5, MaxZoneAgeBars = 1 };
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReadyAt = new(2026, 1, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime[] BreakoutOpens =
    {
        new(2026, 1, 22, 11, 30, 0, DateTimeKind.Utc),
        new(2026, 1, 22, 11, 45, 0, DateTimeKind.Utc),
        new(2026, 1, 22, 12, 0, 0, DateTimeKind.Utc),
        new(2026, 1, 22, 12, 15, 0, DateTimeKind.Utc),
        new(2026, 1, 22, 12, 30, 0, DateTimeKind.Utc),
        new(2026, 1, 23, 9, 0, 0, DateTimeKind.Utc),
        new(2026, 1, 23, 9, 15, 0, DateTimeKind.Utc),
    };

    private static IReadOnlyList<FeedBar> Feed()
    {
        var d1 = new List<Bar>();
        for (DateTime open = Start; open + BarBuilder.LengthOf(TimeFrame.D1) <= End; open = open.AddDays(1.0))
        {
            int d = (open - Start).Days;
            double o = 1.0 + (0.001 * d);
            double c = o + 0.0008;
            d1.Add(BarBuilder.One(TimeFrame.D1, open, o, c + 0.0001, o - 0.0001, c));
        }

        var h4 = new List<Bar>();
        for (DateTime open = Start; open + BarBuilder.LengthOf(TimeFrame.H4) <= End; open = open.AddHours(4.0))
        {
            h4.Add(BarBuilder.One(TimeFrame.H4, open, 1.0, 1.002, 0.998, 1.0));
        }

        var m15 = new List<Bar>();
        for (DateTime open = Start; open + BarBuilder.LengthOf(TimeFrame.M15) <= End; open = open.AddMinutes(15.0))
        {
            m15.Add(BreakoutOpens.Contains(open)
                ? BarBuilder.One(TimeFrame.M15, open, 1.0, 1.03, 0.9995, 1.025)
                : BarBuilder.One(TimeFrame.M15, open, 1.0, 1.0005, 0.9995, 1.0));
        }

        return EngineFeed.Merge(d1, h4, m15);
    }

    private static SignalEngine Breakout()
    {
        return new SignalEngine(Short, new BreakoutParameters());
    }

    private static List<(FeedBar Input, IReadOnlyList<Signal> Signals)> Run(SignalEngine engine, IReadOnlyList<FeedBar> feed, bool replay = false)
    {
        var steps = new List<(FeedBar, IReadOnlyList<Signal>)>();
        foreach (FeedBar item in feed)
        {
            steps.Add((item, engine.OnBarClosed(item.TimeFrame, item.Bar, replay).ToArray()));
        }

        return steps;
    }

    private static DateTime Utc(int day, int hour, int minute)
    {
        return new DateTime(2026, 1, day, hour, minute, 0, DateTimeKind.Utc);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void WarmupBars_EqualThoseOfAZoneFvgEngineWithTheSameParameters()
    {
        Assert.Equal(new SignalEngine(Short).WarmupBars.ToArray(), Breakout().WarmupBars.ToArray());
        Assert.Equal(new SignalEngine(new SignalParameters()).WarmupBars.ToArray(), new SignalEngine(new SignalParameters(), new BreakoutParameters()).WarmupBars.ToArray());
        Assert.Equal(new[] { 21, 129, 64 }, Breakout().WarmupBars.Values.ToArray());
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void OnBarClosed_ConfirmationDuringTheWarmup_NoSignalReferenceDoneAndNoSignalForItOnceReady()
    {
        SignalEngine engine = Breakout();
        bool doneSeen = false;
        foreach (FeedBar item in Feed())
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, false);
            bool beforeNextReference = item.CloseUtc < Utc(23, 0, 0);
            Assert.Equal(item.CloseUtc >= ReadyAt, engine.IsReady);
            if (item.TimeFrame == TimeFrame.M15 && item.CloseUtc == Utc(22, 11, 45))
            {
                // The confirmation: the engine is not ready yet, the reference of 2026-01-21 is Done.
                Assert.False(engine.IsReady);
                Assert.NotNull(engine.Reference);
                Assert.Equal(Utc(21, 0, 0), engine.Reference!.DayOpenUtc);
                Assert.False(engine.Reference.Armed);
                BreakoutData.AssertPrice(1.0209, engine.Reference.High, "reference high");
                doneSeen = true;
            }

            if (beforeNextReference)
            {
                Assert.Empty(signals);
            }

            if (doneSeen && beforeNextReference)
            {
                // Ready from 12:00 on, and the bars closing 12:00 to 12:45 are above the high again.
                Assert.False(engine.Reference!.Armed);
                Assert.Equal(Utc(21, 0, 0), engine.Reference.DayOpenUtc);
            }
        }

        Assert.True(doneSeen);
        Assert.True(engine.IsReady);
    }

    [Theory]
    [Trait("Req", "SIG-026")]
    [InlineData(false)]
    [InlineData(true)]
    public void OnBarClosed_ConfirmationWhenReady_ReturnsOneBreakoutSignalBuiltFromTheSetup(bool replay)
    {
        List<(FeedBar Input, IReadOnlyList<Signal> Signals)> steps = Run(Breakout(), Feed(), replay);

        (FeedBar input, IReadOnlyList<Signal> signals) = Assert.Single(steps, step => step.Signals.Count > 0);
        Signal signal = Assert.Single(signals);
        double atrM15 = ((13 * 0.001) + 0.0305) / 14.0;
        double entry = 1.03 + (0.1 * atrM15);
        Assert.Equal(TimeFrame.M15, input.TimeFrame);
        Assert.Equal(Utc(23, 9, 15), input.CloseUtc);
        Assert.Equal(SignalKind.Breakout, signal.Kind);
        Assert.Equal("BH-20260122-0000", signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        Assert.Null(signal.Fvg);
        Assert.NotNull(signal.Level);
        BreakoutData.AssertPrice(1.0219, signal.Level!.Value, "level");
        BreakoutData.AssertPrice(entry, signal.Entry, "entry");
        BreakoutData.AssertPrice(entry - 0.004, signal.Stop, "stop");
        BreakoutData.AssertPrice(entry + 0.008, signal.Target, "target");
        BreakoutData.AssertPrice(0.004, signal.AtrH4, "ATR(H4)");
        Assert.Equal(Utc(23, 9, 15), signal.ConfirmationCloseUtc);
        Assert.Equal(replay, signal.FromReplay);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void OnBarClosed_BreakoutMode_NoZonesNoGapsAndNoZoneActivityAtAnyBar()
    {
        SignalEngine engine = Breakout();
        foreach (FeedBar item in Feed())
        {
            engine.OnBarClosed(item.TimeFrame, item.Bar, false);

            Assert.Empty(engine.Zones);
            Assert.Empty(engine.RecentFvgs);
            Assert.Same(ZoneActivity.None, engine.LastActivity);
        }
    }

    [Theory]
    [Trait("Req", "SIG-026")]
    [InlineData(TimeFrame.M15)]
    [InlineData(TimeFrame.D1)]
    public void OnBarClosed_BarClosingBeforeTheLastAcceptedBar_ThrowsAndChangesNothing(TimeFrame timeFrame)
    {
        // Fed up to the bar closing 2026-01-23 08:00: ready, Long, the reference of 2026-01-22 Armed.
        SignalEngine engine = Breakout();
        foreach (FeedBar item in Feed().Where(item => item.CloseUtc <= Utc(23, 8, 0)))
        {
            engine.OnBarClosed(item.TimeFrame, item.Bar, false);
        }

        BreakoutReference? reference = engine.Reference;
        Bias bias = engine.Bias;
        bool ready = engine.IsReady;
        Bar old = timeFrame == TimeFrame.M15
            ? BarBuilder.One(TimeFrame.M15, Utc(23, 7, 30), 1.0, 1.03, 0.9995, 1.025)
            : BarBuilder.One(TimeFrame.D1, Utc(22, 0, 0), 1.0, 1.06, 0.99, 1.05);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(timeFrame, old, false));

        Assert.True(ready);
        Assert.Equal(Bias.Long, bias);
        Assert.NotNull(reference);
        Assert.True(reference!.Armed);
        Assert.Equal(reference, engine.Reference);
        Assert.Equal(bias, engine.Bias);
        Assert.Equal(ready, engine.IsReady);

        // The rejected bar left no trace: the rest of the history gives the signal as before.
        int signals = 0;
        foreach (FeedBar item in Feed().Where(item => item.CloseUtc > Utc(23, 8, 0)))
        {
            signals += engine.OnBarClosed(item.TimeFrame, item.Bar, false).Count;
        }

        Assert.Equal(1, signals);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void Reference_WithoutBreakoutParameters_IsNullAtEveryBarAndNoSignalIsBreakout()
    {
        var engine = new SignalEngine(Short, null);
        foreach (FeedBar item in Feed())
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, false);

            Assert.Null(engine.Reference);
            Assert.All(signals, signal => Assert.Equal(SignalKind.ZoneFvg, signal.Kind));
        }
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void Reference_BeforeTheFirstD1Bar_IsNull()
    {
        SignalEngine engine = Breakout();
        engine.OnBarClosed(TimeFrame.M15, BarBuilder.One(TimeFrame.M15, Start, 1.0, 1.0005, 0.9995, 1.0), false);

        Assert.Null(engine.Reference);
        engine.OnBarClosed(TimeFrame.D1, BarBuilder.One(TimeFrame.D1, Start, 1.0, 1.0009, 0.9999, 1.0008), false);
        Assert.Equal(new BreakoutReference(Start, 1.0009, 0.9999, true), engine.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void Constructor_InvalidBreakoutParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEngine(Short, new BreakoutParameters { StopAtrMult = 0.0 }));
        Assert.Throws<ArgumentNullException>(() => new SignalEngine(null!, new BreakoutParameters()));
    }
}
