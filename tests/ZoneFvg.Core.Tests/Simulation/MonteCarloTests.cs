using Xunit;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-003: reshuffle the trade order with a seeded generator and report the distribution of the
/// maximum drawdown (in R, and in percent at a given risk per trade) and of the 30-trade average
/// R, with the 1st, 5th, 50th and 95th percentiles (nearest rank).
/// </summary>
/// <remarks>
/// Drawdown in R is the largest fall of the cumulative R from its running peak, the peak starting
/// at 0. Drawdown in percent compounds the risk on the current balance (RSK-001: risk is a
/// percentage of the balance) and measures the fall from the running peak in percent of that
/// peak (as the kill switch does, RSK-008); the peak starts at the initial balance.
/// </remarks>
public class MonteCarloTests
{
    private static readonly double[] FiveTrades = { 2.0, -1.0, -1.0, 3.0, -1.0 };

    [Fact]
    [Trait("Req", "SIM-003")]
    public void MaxDrawdownR_FiveTradesInGivenOrder_IsTwo()
    {
        // Cumulative R: 2, 1, 0, 3, 2. Peak 2 then 0: drawdown 2. Peak 3 then 2: drawdown 1.
        Assert.Equal(2.0, MonteCarlo.MaxDrawdownR(FiveTrades));
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void MaxDrawdownPercent_FiveTradesAtHalfPercent_IsTwoCompoundedLosses()
    {
        // Balance 1.01 after +2R at 0.5%, then x 0.995 twice: drawdown = 1 - 0.995^2 = 0.9975%.
        // After +3R the new peak 1.01492... falls by one loss: 0.5%.
        Assert.Equal(0.9975, MonteCarlo.MaxDrawdownPercent(FiveTrades, 0.5), 12);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void RollingAverages_FiveTradesWindowThree_AreTheThreeWindowMeans()
    {
        // (2 - 1 - 1) / 3 = 0; (-1 - 1 + 3) / 3 = 1/3; (-1 + 3 - 1) / 3 = 1/3.
        IReadOnlyList<double> averages = MonteCarlo.RollingAverages(FiveTrades, 3);

        Assert.Equal(new[] { 0.0, 1.0 / 3.0, 1.0 / 3.0 }, averages);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void RollingAverages_FewerTradesThanTheWindow_IsEmpty()
    {
        Assert.Empty(MonteCarlo.RollingAverages(FiveTrades, 6));
        Assert.Single(MonteCarlo.RollingAverages(FiveTrades, 5));
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_FiveTradesSeed42FourSequences_MatchesTheHandCheckedShuffles()
    {
        // Fisher-Yates with xoshiro256** seeded through splitmix64 from 42 (orders taken from an
        // independent implementation of the published algorithms):
        //   [2, -1, 3, -1, -1]  DD 2R, 1 - 0.995^2 = 0.9975%;   windows 4/3, 1/3, 1/3
        //   [3, -1, -1, 2, -1]  DD 2R, 0.9975%;                 windows 1/3, 0, 0
        //   [2, -1, -1, -1, 3]  DD 3R, 1 - 0.995^3 = 1.4925125%; windows 0, -1, 1/3
        //   [3, -1, -1, -1, 2]  DD 3R, 1.4925125%;              windows 1/3, -1, 0
        // Nearest rank of 4 values: P1 and P5 rank 1, P50 rank 2, P95 rank 4.
        // Of the 12 windows sorted: P1 and P5 rank 1 = -1, P50 rank 6 = 0, P95 rank 12 = 4/3.
        var parameters = new MonteCarloParameters { Sequences = 4, RiskPercent = 0.5, WindowTrades = 3 };

        MonteCarloResult result = MonteCarlo.Run(FiveTrades, parameters, 42UL);

        Assert.Equal(new[] { 2.0, -1.0, 3.0, -1.0, -1.0 }, result.ShuffledSequences[0]);
        Assert.Equal(new[] { 3.0, -1.0, -1.0, 2.0, -1.0 }, result.ShuffledSequences[1]);
        Assert.Equal(new[] { 2.0, -1.0, -1.0, -1.0, 3.0 }, result.ShuffledSequences[2]);
        Assert.Equal(new[] { 3.0, -1.0, -1.0, -1.0, 2.0 }, result.ShuffledSequences[3]);
        Assert.Equal(new PercentileSet(2.0, 2.0, 2.0, 3.0), result.MaxDrawdownR);
        Assert.Equal(0.9975, result.MaxDrawdownPercent.P1, 12);
        Assert.Equal(0.9975, result.MaxDrawdownPercent.P5, 12);
        Assert.Equal(0.9975, result.MaxDrawdownPercent.P50, 12);
        Assert.Equal(1.4925125, result.MaxDrawdownPercent.P95, 12);
        Assert.Equal(new PercentileSet(-1.0, -1.0, 0.0, 4.0 / 3.0), result.RollingAverageR);
        Assert.Equal(12, result.RollingWindowCount);
        Assert.Equal(42UL, result.Seed);
        Assert.Equal(5, result.TradeCount);
        Assert.Equal(4, result.Sequences);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_ConstantPositiveSeries_HasZeroDrawdown()
    {
        double[] constant = Enumerable.Repeat(0.4, 40).ToArray();

        MonteCarloResult result = MonteCarlo.Run(constant, new MonteCarloParameters { Sequences = 50 }, 7UL);

        Assert.Equal(new PercentileSet(0.0, 0.0, 0.0, 0.0), result.MaxDrawdownR);
        Assert.Equal(new PercentileSet(0.0, 0.0, 0.0, 0.0), result.MaxDrawdownPercent);
        Assert.NotNull(result.RollingAverageR);
        Assert.Equal(0.4, result.RollingAverageR!.P1, 12);
        Assert.Equal(0.4, result.RollingAverageR.P95, 12);
        Assert.Equal(50 * 11, result.RollingWindowCount);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_FiveLossesOfOneR_EveryOrderLosesFiveRAndFourPointNinePercent()
    {
        // 1 - 0.99^5 = 0.0490099501 at 1% risk; every window of 3 averages -1.
        double[] losses = { -1.0, -1.0, -1.0, -1.0, -1.0 };

        MonteCarloResult result = MonteCarlo.Run(losses, new MonteCarloParameters { Sequences = 10, RiskPercent = 1.0, WindowTrades = 3 }, 1UL);

        Assert.Equal(new PercentileSet(5.0, 5.0, 5.0, 5.0), result.MaxDrawdownR);
        Assert.Equal(4.90099501, result.MaxDrawdownPercent.P50, 10);
        Assert.Equal(4.90099501, result.MaxDrawdownPercent.P95, 10);
        Assert.Equal(new PercentileSet(-1.0, -1.0, -1.0, -1.0), result.RollingAverageR);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_SameSeed_GivesIdenticalOutput()
    {
        double[] results = SampleSeries(300, 11);

        MonteCarloResult first = MonteCarlo.Run(results, new MonteCarloParameters(), 20261116UL);
        MonteCarloResult second = MonteCarlo.Run(results, new MonteCarloParameters(), 20261116UL);

        Assert.Equal(1000, first.Sequences);
        Assert.Equal(first.MaxDrawdownR, second.MaxDrawdownR);
        Assert.Equal(first.MaxDrawdownPercent, second.MaxDrawdownPercent);
        Assert.Equal(first.RollingAverageR, second.RollingAverageR);
        Assert.Equal(first.ShuffledSequences, second.ShuffledSequences);
        Assert.Equal(first.ToMarkdown(), second.ToMarkdown());
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_DifferentSeeds_GiveDifferentOrders()
    {
        double[] results = SampleSeries(50, 3);

        MonteCarloResult first = MonteCarlo.Run(results, new MonteCarloParameters { Sequences = 5 }, 1UL);
        MonteCarloResult second = MonteCarlo.Run(results, new MonteCarloParameters { Sequences = 5 }, 2UL);

        Assert.NotEqual(first.ShuffledSequences[0], second.ShuffledSequences[0]);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_EverySequence_IsAPermutationOfTheInputAndPercentilesAreOrdered()
    {
        int seed = 5;
        double[] results = SampleSeries(120, seed);
        double[] sorted = results.OrderBy(x => x).ToArray();

        MonteCarloResult result = MonteCarlo.Run(results, new MonteCarloParameters { Sequences = 200 }, (ulong)seed);

        foreach (IReadOnlyList<double> sequence in result.ShuffledSequences)
        {
            Assert.True(sorted.SequenceEqual(sequence.OrderBy(x => x)), "seed " + seed);
        }

        AssertOrdered(result.MaxDrawdownR, seed);
        AssertOrdered(result.MaxDrawdownPercent, seed);
        AssertOrdered(result.RollingAverageR!, seed);
        Assert.True(result.MaxDrawdownR.P1 >= 0.0, "seed " + seed);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_TwentyNineTradesWithDefaultWindow_ReportsNoRollingDistribution()
    {
        MonteCarloResult result = MonteCarlo.Run(SampleSeries(29, 1), new MonteCarloParameters { Sequences = 3 }, 1UL);

        Assert.Null(result.RollingAverageR);
        Assert.Equal(0, result.RollingWindowCount);
        Assert.Contains("not available", result.ToMarkdown());
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_ThousandSequencesOfThousandTrades_IsFast()
    {
        double[] results = SampleSeries(1000, 9);

        MonteCarloResult result = MonteCarlo.Run(results, new MonteCarloParameters(), 9UL);

        Assert.Equal(971 * 1000, result.RollingWindowCount);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void ToMarkdown_StatesAssumptionsSeedAndPercentiles()
    {
        var parameters = new MonteCarloParameters { Sequences = 4, RiskPercent = 0.5, WindowTrades = 3 };

        string text = MonteCarlo.Run(FiveTrades, parameters, 42UL).ToMarkdown();

        Assert.Contains("past trade distributions may not repeat", text);
        Assert.Contains("Seed: 42", text);
        Assert.Contains("| Maximum drawdown (R) | 2.00 | 2.00 | 2.00 | 3.00 |", text);
        Assert.Contains("| Maximum drawdown (% at 0.5% risk) | 1.00 | 1.00 | 1.00 | 1.49 |", text);
        Assert.Contains("| 3-trade average R | -1.000 | -1.000 | 0.000 | 1.333 |", text);
    }

    [Theory]
    [Trait("Req", "SIM-003")]
    [InlineData(0, 0.5, 30)]
    [InlineData(1, 0.0, 30)]
    [InlineData(1, 100.5, 30)]
    [InlineData(1, double.NaN, 30)]
    [InlineData(1, 0.5, 0)]
    public void Run_InvalidParameters_Throw(int sequences, double risk, int window)
    {
        var parameters = new MonteCarloParameters { Sequences = sequences, RiskPercent = risk, WindowTrades = window };

        Assert.Throws<ArgumentOutOfRangeException>(() => MonteCarlo.Run(FiveTrades, parameters, 1UL));
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Run_InvalidResults_Throw()
    {
        var parameters = new MonteCarloParameters { Sequences = 1, RiskPercent = 0.5 };

        Assert.Throws<ArgumentException>(() => MonteCarlo.Run(Array.Empty<double>(), parameters, 1UL));
        Assert.Throws<ArgumentException>(() => MonteCarlo.Run(new[] { 1.0, double.NaN }, parameters, 1UL));
        Assert.Throws<ArgumentException>(() => MonteCarlo.Run(new[] { 1.0, double.PositiveInfinity }, parameters, 1UL));

        // -200R at 0.5% is the whole balance; anything at or below it cannot be compounded.
        Assert.Throws<ArgumentException>(() => MonteCarlo.Run(new[] { 1.0, -200.0 }, parameters, 1UL));
        Assert.Throws<ArgumentNullException>(() => MonteCarlo.Run(null!, parameters, 1UL));
        Assert.Throws<ArgumentNullException>(() => MonteCarlo.Run(FiveTrades, null!, 1UL));
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void NearestRank_HandCheckedRanks()
    {
        // Rank = ceil(p x n / 100). n = 10: P1 and P5 rank 1, P50 rank 5, P95 rank 10.
        double[] ten = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        double[] twenty = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        double[] hundred = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

        Assert.Equal(new PercentileSet(1.0, 1.0, 5.0, 10.0), NearestRank.Set(ten));
        Assert.Equal(new PercentileSet(1.0, 1.0, 10.0, 19.0), NearestRank.Set(twenty));
        Assert.Equal(new PercentileSet(1.0, 5.0, 50.0, 95.0), NearestRank.Set(hundred));
        Assert.Equal(7.0, NearestRank.Percentile(new[] { 7.0 }, 1));
        Assert.Equal(7.0, NearestRank.Percentile(new[] { 7.0 }, 100));
        Assert.Throws<ArgumentException>(() => NearestRank.Percentile(Array.Empty<double>(), 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => NearestRank.Percentile(ten, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NearestRank.Percentile(ten, 101));
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Xoshiro_KnownOutputs_MatchThePublishedAlgorithm()
    {
        // splitmix64 seeding as recommended by the xoshiro authors. First output of splitmix64
        // from 0 is 0xE220A8397B1DCDAF; the generator's first outputs below come from an
        // independent implementation of the reference C code.
        var zero = new Xoshiro256StarStar(0UL);
        var fortyTwo = new Xoshiro256StarStar(42UL);

        Assert.Equal(11091344671253066420UL, zero.NextUInt64());
        Assert.Equal(13793997310169335082UL, zero.NextUInt64());
        Assert.Equal(1900383378846508768UL, zero.NextUInt64());
        Assert.Equal(1546998764402558742UL, fortyTwo.NextUInt64());
        Assert.Equal(42UL, fortyTwo.Seed);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void Xoshiro_NextInt_IsInRangeAndReproducible()
    {
        var generator = new Xoshiro256StarStar(42UL);
        int[] draws = Enumerable.Range(0, 10).Select(_ => generator.NextInt(10)).ToArray();

        Assert.Equal(new[] { 2, 2, 9, 3, 6, 4, 4, 7, 8, 5 }, draws);
        Assert.Equal(0, new Xoshiro256StarStar(3UL).NextInt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Xoshiro256StarStar(3UL).NextInt(0));
    }

    private static void AssertOrdered(PercentileSet set, int seed)
    {
        Assert.True(set.P1 <= set.P5 && set.P5 <= set.P50 && set.P50 <= set.P95, "seed " + seed);
    }

    private static double[] SampleSeries(int count, int seed)
    {
        // Wins of +2R and losses of -1R with a few partial results, from the test generator.
        var generator = new SeededGenerator(seed);
        var results = new double[count];
        for (int i = 0; i < count; i++)
        {
            int draw = generator.Next(10);
            results[i] = draw < 4 ? 2.0 : draw < 9 ? -1.0 : 0.5;
        }

        return results;
    }
}
