using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// A seeded random-walk market history for the property tests (SIG-002, SIG-019, SIG-021): M15 bars
/// on weekdays, and H4 and D1 bars aggregated from them with fixed UTC boundaries.
/// </summary>
public sealed class RandomWalkHistory
{
    /// <summary>Points per price unit: every price is a whole number of points divided by 100,000.</summary>
    public const double PointsPerUnit = 100000.0;

    /// <summary>First M15 bar: Monday 2025-01-06 00:00 UTC.</summary>
    public static readonly DateTime Start = new(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc);

    private RandomWalkHistory(int seed, IReadOnlyList<Bar> m15)
    {
        Seed = seed;
        M15 = m15;
        H4 = BarAggregator.Aggregate(m15, TimeFrame.H4);
        D1 = BarAggregator.Aggregate(m15, TimeFrame.D1);
        Feed = EngineFeed.Merge(D1, H4, M15);
    }

    /// <summary>The seed the history was generated from. Printed by every failing property test.</summary>
    public int Seed { get; }

    /// <summary>M15 bars, oldest first, Monday 00:00 to Friday 23:45 UTC of every week.</summary>
    public IReadOnlyList<Bar> M15 { get; }

    /// <summary>H4 bars aggregated from <see cref="M15"/> on the boundaries 00:00, 04:00, ..., 20:00 UTC.</summary>
    public IReadOnlyList<Bar> H4 { get; }

    /// <summary>D1 bars aggregated from <see cref="M15"/> on 00:00 UTC boundaries.</summary>
    public IReadOnlyList<Bar> D1 { get; }

    /// <summary>All bars in intake order: close time, then D1, H4, M15 at equal close times.</summary>
    public IReadOnlyList<FeedBar> Feed { get; }

    /// <summary>
    /// Generates <paramref name="tradingDays"/> weekdays of M15 bars from <see cref="Start"/>.
    /// Prices are whole points around 1.10000. The walk switches between flat, rising and falling
    /// regimes of random length, adds noise to every bar and wicks on both sides, and gaps at the
    /// Monday open; this produces impulse legs, pullbacks into zones and fair value gaps.
    /// </summary>
    public static RandomWalkHistory Generate(int seed, int tradingDays)
    {
        var random = new SeededGenerator(seed);
        var bars = new List<Bar>(tradingDays * 96);
        long price = 110000;
        int drift = 0;
        int regimeLeft = 0;
        DateTime day = Start;
        for (int d = 0; d < tradingDays; d++)
        {
            while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                day = day.AddDays(1);
            }

            if (day.DayOfWeek == DayOfWeek.Monday && d > 0)
            {
                price += random.Next(61) - 30;
            }

            for (int k = 0; k < 96; k++)
            {
                if (regimeLeft == 0)
                {
                    (drift, regimeLeft) = NextRegime(random);
                }

                regimeLeft--;
                long open = price;
                long close = open + drift + random.Next(31) - 15;
                long high = Math.Max(open, close) + random.Next(6);
                long low = Math.Min(open, close) - random.Next(6);
                bars.Add(new Bar(day.AddMinutes(15 * k), open / PointsPerUnit, high / PointsPerUnit, low / PointsPerUnit, close / PointsPerUnit));
                price = close;
            }

            day = day.AddDays(1);
        }

        return new RandomWalkHistory(seed, bars);
    }

    /// <summary>A copy of this history with every price reflected around <paramref name="pivot"/> (SIG-021).</summary>
    public IReadOnlyList<FeedBar> MirroredFeed(double pivot)
    {
        return EngineFeed.Merge(D1.Mirror(pivot), H4.Mirror(pivot), M15.Mirror(pivot));
    }

    private static (int Drift, int Length) NextRegime(SeededGenerator random)
    {
        int kind = random.Next(10);
        int strength = 4 * (random.Next(3) + 1);
        if (kind < 4)
        {
            return (0, 16 + random.Next(48));
        }

        int length = 8 + random.Next(48);
        return kind < 7 ? (strength, length) : (-strength, length);
    }
}

/// <summary>Aggregates M15 bars into H4 or D1 bars with fixed UTC boundaries.</summary>
public static class BarAggregator
{
    /// <summary>
    /// Groups the M15 bars by the UTC boundary of the target timeframe their open time falls in
    /// (H4: 00:00, 04:00, ..., 20:00; D1: 00:00). Each group becomes one bar that opens at the
    /// boundary: open of the first bar, highest high, lowest low, close of the last bar. Groups
    /// with no M15 bar (weekends) produce no bar.
    /// </summary>
    public static IReadOnlyList<Bar> Aggregate(IReadOnlyList<Bar> m15, TimeFrame target)
    {
        TimeSpan length = BarBuilder.LengthOf(target);
        var result = new List<Bar>();
        int i = 0;
        while (i < m15.Count)
        {
            DateTime boundary = Floor(m15[i].OpenTimeUtc, length);
            double open = m15[i].Open;
            double high = m15[i].High;
            double low = m15[i].Low;
            double close = m15[i].Close;
            i++;
            while (i < m15.Count && Floor(m15[i].OpenTimeUtc, length) == boundary)
            {
                high = Math.Max(high, m15[i].High);
                low = Math.Min(low, m15[i].Low);
                close = m15[i].Close;
                i++;
            }

            result.Add(new Bar(boundary, open, high, low, close));
        }

        return result;
    }

    private static DateTime Floor(DateTime timeUtc, TimeSpan length)
    {
        return new DateTime(timeUtc.Ticks - (timeUtc.Ticks % length.Ticks), DateTimeKind.Utc);
    }
}
