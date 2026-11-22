using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Property tests of the breakout tracker over 20 seeded random-walk histories (SIG-025). Each
/// history has 70 weekdays of M15 bars (6,720 bars) with H4 and D1 bars aggregated from them; the
/// tracker is driven by <see cref="BreakoutFeed"/>. A failure names its seed, both in the test case
/// name and in the message.
/// </summary>
public class BreakoutTrackerPropertyTests
{
    private const int TradingDays = 70;
    private const double MirrorPivot = 2.2;

    private readonly ITestOutputHelper _output;

    public BreakoutTrackerPropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The 20 seeds: 1001, 1038, 1075, ..., 1704.</summary>
    public static IEnumerable<object[]> Seeds => Enumerable.Range(0, 20).Select(k => new object[] { 1001 + (37 * k) });

    [Fact]
    [Trait("Req", "SIG-025")]
    public void Histories_AreLongEnough()
    {
        Assert.True(RandomWalkHistory.Generate(1001, TradingDays).M15.Count >= 6000);
    }

    [Theory]
    [Trait("Req", "SIG-025")]
    [MemberData(nameof(Seeds))]
    public void SameHistoryInTwoFreshTrackers_IdenticalResultsBitForBit(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            // The history is generated twice from the seed, so the trackers share no object.
            IReadOnlyList<BreakoutStep> first = BreakoutFeed.Run(RandomWalkHistory.Generate(seed, TradingDays).Feed);
            IReadOnlyList<BreakoutStep> second = BreakoutFeed.Run(RandomWalkHistory.Generate(seed, TradingDays).Feed);

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Bar, second[i].Bar);
                Assert.Equal(first[i].Bias, second[i].Bias);
                Assert.Equal(first[i].Setup is null ? "-" : BreakoutFeed.Key(first[i].Setup!), second[i].Setup is null ? "-" : BreakoutFeed.Key(second[i].Setup!));
            }

            _output.WriteLine(Summary(seed, first));
        });
    }

    [Fact]
    [Trait("Req", "SIG-025")]
    public void Histories_ProduceResultsInAtLeastTenOfTwenty()
    {
        // Without results the determinism and mirror tests would prove nothing.
        int withResults = 0;
        foreach (object[] row in Seeds)
        {
            int seed = (int)row[0];
            IReadOnlyList<BreakoutStep> steps = BreakoutFeed.Run(RandomWalkHistory.Generate(seed, TradingDays).Feed);
            _output.WriteLine(Summary(seed, steps));
            if (BreakoutFeed.Results(steps).Count > 0)
            {
                withResults++;
            }
        }

        Assert.True(withResults >= 10, Text(withResults) + " of 20 histories produced a result; at least 10 are needed.");
    }

    [Theory]
    [Trait("Req", "SIG-025")]
    [MemberData(nameof(Seeds))]
    public void MirroredHistory_EachResultTurnsIntoTheOtherSideWithReflectedLevels(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<BreakoutStep> original = BreakoutFeed.Run(history.Feed);
            IReadOnlyList<BreakoutStep> mirrored = BreakoutFeed.Run(history.MirroredFeed(MirrorPivot));

            Assert.Equal(original.Count, mirrored.Count);
            for (int i = 0; i < original.Count; i++)
            {
                BreakoutStep a = original[i];
                BreakoutStep b = mirrored[i];
                string at = "M15 bar opening " + Stamp(a.Bar.OpenTimeUtc) + ": ";
                Assert.True(a.Bar.OpenTimeUtc == b.Bar.OpenTimeUtc, at + "bar times differ");
                Assert.True(Mirror(a.Bias) == b.Bias, at + "bias " + a.Bias + " mirrored to " + b.Bias);
                Assert.True((a.Setup is null) == (b.Setup is null), at + "one run has a result and the other has none");
                if (a.Setup is not null)
                {
                    AssertMirrored(a.Setup, b.Setup!, at);
                }
            }

            _output.WriteLine(Summary(seed, original));
        });
    }

    private static void AssertMirrored(BreakoutSetup a, BreakoutSetup b, string at)
    {
        string what = at + "result " + a.Id + " ";
        Assert.True(b.Side == (a.Side == Side.Buy ? Side.Sell : Side.Buy), what + "side");
        Assert.True(MirrorId(a.Id) == b.Id, what + "mirrored id " + b.Id);
        AssertReflected(a.Entry, b.Entry, what + "entry");
        AssertReflected(a.Stop, b.Stop, what + "stop");
        AssertReflected(a.Target, b.Target, what + "target");
        AssertReflected(a.Level, b.Level, what + "level");
        Assert.True(PriceMath.AlmostEqual(a.AtrH4, b.AtrH4, PriceMath.DefaultTolerance), what + "ATR(H4) differs");
        Assert.True(a.ConfirmationCloseUtc == b.ConfirmationCloseUtc, what + "confirmation close differs");
    }

    private static void AssertReflected(double original, double mirrored, string what)
    {
        double expected = MirrorPivot - original;
        Assert.True(PriceMath.AlmostEqual(expected, mirrored, PriceMath.DefaultTolerance), what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + " but was " + mirrored.ToString("R", CultureInfo.InvariantCulture));
    }

    private static Bias Mirror(Bias bias)
    {
        return bias switch
        {
            Bias.Long => Bias.Short,
            Bias.Short => Bias.Long,
            _ => Bias.Neutral,
        };
    }

    private static string MirrorId(string id)
    {
        return (id.StartsWith("BH-", StringComparison.Ordinal) ? "BL-" : "BH-") + id.Substring(3);
    }

    private static string Summary(int seed, IReadOnlyList<BreakoutStep> steps)
    {
        IReadOnlyList<BreakoutSetup> results = BreakoutFeed.Results(steps);
        return "seed " + Text(seed) + ": " + Text(steps.Count) + " M15 bars, " + Text(steps.Count(step => step.Bias != Bias.Neutral)) + " with a bias, "
            + Text(results.Count(setup => setup.Side == Side.Buy)) + " buy and " + Text(results.Count(setup => setup.Side == Side.Sell)) + " sell results";
    }

    private static string Stamp(DateTime timeUtc)
    {
        return timeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
