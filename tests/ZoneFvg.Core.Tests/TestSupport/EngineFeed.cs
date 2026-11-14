using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>One closed bar with its timeframe, as passed to <see cref="SignalEngine.OnBarClosed"/>.</summary>
public sealed record FeedBar(TimeFrame TimeFrame, Bar Bar)
{
    /// <summary>Nominal close time: open time plus the length of the timeframe.</summary>
    public DateTime CloseUtc => Bar.OpenTimeUtc + BarBuilder.LengthOf(TimeFrame);

    /// <summary>0 for D1, 1 for H4, 2 for M15: the order of bars that close at the same instant.</summary>
    public int Rank => TimeFrame switch
    {
        TimeFrame.D1 => 0,
        TimeFrame.H4 => 1,
        _ => 2,
    };
}

/// <summary>What the engine showed right after one bar was fed.</summary>
public sealed record EngineStep(FeedBar Input, IReadOnlyList<Signal> Signals, IReadOnlyList<Zone> Zones, bool IsReady, Bias Bias);

/// <summary>Builds bar feeds in intake order and runs them through an engine.</summary>
public static class EngineFeed
{
    /// <summary>
    /// Merges three bar lists into one feed ordered by close time, and D1, H4, M15 at equal close
    /// times. The order is computed here, independently of the engine under test.
    /// </summary>
    public static IReadOnlyList<FeedBar> Merge(IReadOnlyList<Bar> d1, IReadOnlyList<Bar> h4, IReadOnlyList<Bar> m15)
    {
        var all = new List<FeedBar>(d1.Count + h4.Count + m15.Count);
        all.AddRange(d1.Select(bar => new FeedBar(TimeFrame.D1, bar)));
        all.AddRange(h4.Select(bar => new FeedBar(TimeFrame.H4, bar)));
        all.AddRange(m15.Select(bar => new FeedBar(TimeFrame.M15, bar)));
        return all.OrderBy(item => item.CloseUtc).ThenBy(item => item.Rank).ToList();
    }

    /// <summary>Feeds every bar and records the engine's output and visible state after each one.</summary>
    public static IReadOnlyList<EngineStep> Run(SignalEngine engine, IEnumerable<FeedBar> feed, bool replay = false)
    {
        var steps = new List<EngineStep>();
        foreach (FeedBar item in feed)
        {
            steps.Add(Step(engine, item, replay));
        }

        return steps;
    }

    /// <summary>Feeds one bar and records the engine's output and visible state.</summary>
    public static EngineStep Step(SignalEngine engine, FeedBar item, bool replay = false)
    {
        IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, replay);
        return new EngineStep(item, signals.ToArray(), engine.Zones.ToArray(), engine.IsReady, engine.Bias);
    }

    /// <summary>All signals returned over a run, in order.</summary>
    public static IReadOnlyList<Signal> AllSignals(IEnumerable<EngineStep> steps)
    {
        return steps.SelectMany(step => step.Signals).ToList();
    }

    /// <summary>Asserts that two runs over the same bars returned equal signals and showed equal state at every step.</summary>
    public static void AssertSameSteps(IReadOnlyList<EngineStep> expected, IReadOnlyList<EngineStep> actual)
    {
        Xunit.Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Xunit.Assert.Equal(expected[i].Input, actual[i].Input);
            Xunit.Assert.Equal(expected[i].Signals, actual[i].Signals);
            Xunit.Assert.Equal(expected[i].Zones, actual[i].Zones);
            Xunit.Assert.Equal(expected[i].IsReady, actual[i].IsReady);
            Xunit.Assert.Equal(expected[i].Bias, actual[i].Bias);
        }
    }
}
