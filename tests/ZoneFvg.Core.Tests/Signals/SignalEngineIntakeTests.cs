using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bar intake order of the engine (SIG-004; owner decision "Bar intake order" of 2026-11-13). The
/// engine accepts a bar only when its pair (close time, rank) is greater than that of the last
/// accepted bar, rank being 0 for D1, 1 for H4 and 2 for M15. Otherwise it throws
/// <see cref="InvalidOperationException"/> and changes nothing.
/// </summary>
/// <remarks>
/// The single bars below close at T = 2026-01-06 00:00 UTC: the D1 bar opened 24 hours earlier, the
/// H4 bar 4 hours earlier and the M15 bar 15 minutes earlier.
/// </remarks>
public class SignalEngineIntakeTests
{
    private static readonly DateTime T = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_SameInstantD1ThenH4ThenM15_AreAccepted()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);

        engine.OnBarClosed(TimeFrame.D1, ClosingAtT(TimeFrame.D1), false);
        engine.OnBarClosed(TimeFrame.H4, ClosingAtT(TimeFrame.H4), false);
        IReadOnlyList<Signal> signals = engine.OnBarClosed(TimeFrame.M15, ClosingAtT(TimeFrame.M15), false);

        Assert.Empty(signals);
        Assert.False(engine.IsReady);
    }

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.D1, TimeFrame.M15)]
    [InlineData(TimeFrame.H4, TimeFrame.M15)]
    [InlineData(TimeFrame.D1, TimeFrame.H4)]
    public void OnBarClosed_TwoSameInstantBarsInOrder_AreAccepted(TimeFrame first, TimeFrame second)
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);

        engine.OnBarClosed(first, ClosingAtT(first), false);
        IReadOnlyList<Signal> signals = engine.OnBarClosed(second, ClosingAtT(second), false);

        Assert.Empty(signals);
    }

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.M15, TimeFrame.H4)]
    [InlineData(TimeFrame.M15, TimeFrame.D1)]
    [InlineData(TimeFrame.H4, TimeFrame.D1)]
    public void OnBarClosed_TwoSameInstantBarsOutOfOrder_SecondThrows(TimeFrame first, TimeFrame second)
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(first, ClosingAtT(first), false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(second, ClosingAtT(second), false));
    }

    [Theory]
    [Trait("Req", "SIG-004")]
    [InlineData(TimeFrame.D1)]
    [InlineData(TimeFrame.H4)]
    [InlineData(TimeFrame.M15)]
    public void OnBarClosed_SameBarTwice_SecondThrows(TimeFrame timeFrame)
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        Bar bar = ClosingAtT(timeFrame);
        engine.OnBarClosed(timeFrame, bar, false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(timeFrame, bar, false));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_H4ClosingBeforeTheLastM15_Throws()
    {
        // M15 from T closes at T + 15 min. The H4 bar from T - 4 h closes at T, 15 minutes earlier.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.H4, Flat(TimeFrame.H4, T.AddHours(-4)), false));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_M15ClosingBeforeTheLastH4_Throws()
    {
        // H4 from T closes at T + 4 h. The M15 bar from T closes at T + 15 min, earlier.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.H4, Flat(TimeFrame.H4, T), false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_D1ClosingBeforeTheLastM15_Throws()
    {
        // M15 from T closes at T + 15 min; the D1 bar from T - 1 day closes at T.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.D1, Flat(TimeFrame.D1, T.AddDays(-1)), false));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_M15OpeningBeforeTheLastM15_Throws()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T.AddMinutes(-15)), false));
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_FirstBarClosingAfterTheLastAccepted_IsAccepted()
    {
        // After the H4 bar closing at T + 4 h: the M15 bar closing at T + 4 h (rank after H4) and
        // then the M15 bar closing at T + 4 h 15 min are accepted.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.H4, Flat(TimeFrame.H4, T), false);

        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T.AddHours(4).AddMinutes(-15)), false);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T.AddHours(4)), false);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_RejectedBar_LeavesTheLastAcceptedPairUnchanged()
    {
        // After M15 (T + 15 min) the H4 bar closing at T is rejected. The next M15 bar is still
        // compared with the M15 bar: the one closing at T + 15 min again is rejected, the one
        // closing at T + 30 min accepted.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false);
        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.H4, Flat(TimeFrame.H4, T.AddHours(-4)), false));

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false));
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T.AddMinutes(15)), false);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_NullBarOrUnknownTimeFrame_ThrowsAndChangesNothing()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T), false);

        Assert.Throws<ArgumentNullException>("bar", () => engine.OnBarClosed(TimeFrame.M15, null!, false));
        Assert.Throws<ArgumentOutOfRangeException>("timeFrame", () => engine.OnBarClosed((TimeFrame)99, Flat(TimeFrame.M15, T.AddHours(1)), false));

        engine.OnBarClosed(TimeFrame.M15, Flat(TimeFrame.M15, T.AddMinutes(15)), false);
    }

    [Fact]
    [Trait("Req", "SIG-004")]
    public void OnBarClosed_RejectionsDuringAHistory_LeaveStateAsBeforeAndTheRunAsWithoutThem()
    {
        // The reference history under Long bias, ready before the touch, followed by the second
        // gap bars. Bad bars are offered at four points; each throws, and right after each one
        // IsReady, Bias and Zones are what they were before it. The run with the rejected bars
        // gives, bar for bar, the same signals and state as a fresh engine that never saw them.
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, 5, 5, 40, false, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);
        IReadOnlyList<EngineStep> clean = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);

        var engine = new SignalEngine(EngineScenarios.Defaults);
        var steps = new List<EngineStep>();
        int rejected = 0;
        foreach (FeedBar item in feed)
        {
            steps.Add(EngineFeed.Step(engine, item));
            foreach (FeedBar bad in BadBarsAfter(item))
            {
                bool readyBefore = engine.IsReady;
                Bias biasBefore = engine.Bias;
                Zone[] zonesBefore = engine.Zones.ToArray();

                Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(bad.TimeFrame, bad.Bar, false));

                Assert.Equal(readyBefore, engine.IsReady);
                Assert.Equal(biasBefore, engine.Bias);
                Assert.Equal(zonesBefore, engine.Zones.ToArray());
                rejected++;
            }
        }

        Assert.Equal(4, rejected);
        EngineFeed.AssertSameSteps(clean, steps);
        Assert.Single(EngineFeed.AllSignals(steps));
    }

    /// <summary>
    /// Bad bars offered right after particular bars of the reference history:
    /// after the last D1 bar, a D1 bar with the same open time that would turn the bias Short;
    /// after the leg (H4, closing at the activation), an H4 bar that closed 4 hours earlier and
    /// would itself be a large bullish leg; after m0, an M15 bar with m0's open time; after m1, a D1
    /// bar closing 15 minutes before m1 closes.
    /// </summary>
    private static IEnumerable<FeedBar> BadBarsAfter(FeedBar accepted)
    {
        DateTime activation = EngineScenarios.Activation;
        if (accepted.TimeFrame == TimeFrame.D1 && accepted.CloseUtc == activation)
        {
            yield return new FeedBar(TimeFrame.D1, BarBuilder.One(TimeFrame.D1, accepted.Bar.OpenTimeUtc, 70.0, 70.0, 1.0, 1.0));
        }

        if (accepted.TimeFrame == TimeFrame.H4 && accepted.CloseUtc == activation)
        {
            yield return new FeedBar(TimeFrame.H4, BarBuilder.One(TimeFrame.H4, activation.AddHours(-8), 100.0, 110.0, 100.0, 110.0));
        }

        if (accepted.TimeFrame == TimeFrame.M15 && accepted.Bar.OpenTimeUtc == EngineScenarios.TailOpen(0))
        {
            yield return new FeedBar(TimeFrame.M15, BarBuilder.One(TimeFrame.M15, activation, 100.6, 100.6, 90.0, 90.0));
        }

        if (accepted.TimeFrame == TimeFrame.M15 && accepted.Bar.OpenTimeUtc == EngineScenarios.TailOpen(1))
        {
            yield return new FeedBar(TimeFrame.D1, BarBuilder.One(TimeFrame.D1, activation.AddDays(-1).AddMinutes(15), 10.0, 10.0, 10.0, 10.0));
        }
    }

    private static Bar ClosingAtT(TimeFrame timeFrame)
    {
        return Flat(timeFrame, T - BarBuilder.LengthOf(timeFrame));
    }

    private static Bar Flat(TimeFrame timeFrame, DateTime openTimeUtc)
    {
        return BarBuilder.One(timeFrame, openTimeUtc, 1.1000, 1.1010, 1.0990, 1.1000);
    }
}
