using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>One M15 close fed to a tracker: the bias passed in and the result returned.</summary>
public sealed record BreakoutStep(Bar Bar, Bias Bias, BreakoutSetup? Setup);

/// <summary>
/// Drives a <see cref="BreakoutTracker"/> the way the engine will (SIG-025): bars in intake order
/// (<see cref="BarIntakeOrder"/>), each D1 bar to OnD1Closed, each M15 bar to OnM15Closed with the
/// bias of <see cref="BiasCalculator"/> over the D1 bars so far and the ATR values of
/// <see cref="Atr"/> over the M15 and H4 bars so far.
/// </summary>
public static class BreakoutFeed
{
    /// <summary>EMA period of the bias: 5, so that the bias is not always Neutral in a short history.</summary>
    public const int BiasEmaPeriod = 5;

    /// <summary>Feeds every bar to a fresh tracker with default parameters and RewardRisk 2.0 and returns one step per M15 bar.</summary>
    public static IReadOnlyList<BreakoutStep> Run(IEnumerable<FeedBar> feed)
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), 2.0);
        var d1 = new List<Bar>();
        var h4 = new List<Bar>();
        var m15 = new List<Bar>();
        var steps = new List<BreakoutStep>();
        IEnumerable<FeedBar> ordered = feed
            .OrderBy(item => BarIntakeOrder.CloseTimeUtc(item.TimeFrame, item.Bar))
            .ThenBy(item => BarIntakeOrder.Rank(item.TimeFrame));
        foreach (FeedBar item in ordered)
        {
            switch (item.TimeFrame)
            {
                case TimeFrame.D1:
                    d1.Add(item.Bar);
                    tracker.OnD1Closed(item.Bar);
                    break;
                case TimeFrame.H4:
                    h4.Add(item.Bar);
                    break;
                default:
                    m15.Add(item.Bar);
                    Bias bias = BiasCalculator.Compute(d1, BiasEmaPeriod);
                    double? atrM15 = Atr.Value(m15, m15.Count - 1);
                    double? atrH4 = Atr.Value(h4, h4.Count - 1);
                    steps.Add(new BreakoutStep(item.Bar, bias, tracker.OnM15Closed(item.Bar, bias, atrM15, atrH4)));
                    break;
            }
        }

        return steps;
    }

    /// <summary>The results of a run, in order.</summary>
    public static IReadOnlyList<BreakoutSetup> Results(IEnumerable<BreakoutStep> steps)
    {
        var results = new List<BreakoutSetup>();
        foreach (BreakoutStep step in steps)
        {
            if (step.Setup is not null)
            {
                results.Add(step.Setup);
            }
        }

        return results;
    }

    /// <summary>A text key holding every field of the setup, each double by its 64-bit pattern; equal keys mean bit-identical setups.</summary>
    public static string Key(BreakoutSetup setup)
    {
        return string.Join(
            "|",
            setup.Id,
            setup.Side.ToString(),
            Bits(setup.Entry),
            Bits(setup.Stop),
            Bits(setup.Target),
            Bits(setup.Level),
            setup.ConfirmationCloseUtc.Ticks.ToString(CultureInfo.InvariantCulture) + setup.ConfirmationCloseUtc.Kind.ToString(),
            Bits(setup.AtrH4));
    }

    private static string Bits(double value)
    {
        return BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture) + "(" + value.ToString("R", CultureInfo.InvariantCulture) + ")";
    }
}
