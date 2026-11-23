using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Property tests of the session range tracker over 20 seeded random M15 histories (SIG-031 to
/// SIG-033). A history covers 45 calendar days around one of the two daylight saving switches of
/// 2025, with weekend gaps, single missing bars and longer outages, a random bias and random ATR
/// values (some missing), and random reports that the order is no longer pending. The tracker fed
/// bar by bar must give, at every bar, the same answer as a fresh tracker rebuilt from the bars of
/// that trading day alone. A failure names its seed, in the test case name and in the message.
/// </summary>
public class SessionRangeTrackerPropertyTests
{
    private const int CalendarDays = 45;

    private readonly ITestOutputHelper _output;

    public SessionRangeTrackerPropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The 20 seeds: 3001, 3042, 3083, ..., 3780.</summary>
    public static IEnumerable<object[]> Seeds => Enumerable.Range(0, 20).Select(k => new object[] { 3001 + (41 * k) });

    [Theory]
    [Trait("Req", "SIG-031")]
    [Trait("Req", "SIG-033")]
    [MemberData(nameof(Seeds))]
    public void FedBarByBar_AndRebuiltFromTheTradingDaysBars_GiveTheSameAnswers(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            IReadOnlyList<Step> steps = History(seed);
            var tracker = new SessionRangeTracker(new SessionRangeParameters(), 2.0, TimeSpan.FromHours(12.0));
            int dayStart = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0 && TradingDate(steps[i]) != TradingDate(steps[i - 1]))
                {
                    dayStart = i;
                }

                SessionRangeEvent? fed = Apply(tracker, steps[i]);
                var rebuilt = new SessionRangeTracker(new SessionRangeParameters(), 2.0, TimeSpan.FromHours(12.0));
                SessionRangeEvent? again = null;
                for (int j = dayStart; j <= i; j++)
                {
                    again = Apply(rebuilt, steps[j]);
                }

                string at = "bar " + Invariant.Minute(steps[i].Bar.OpenTimeUtc) + ": ";
                Assert.True(Key(fed) == Key(again), at + "fed " + Key(fed) + " but rebuilt " + Key(again));
                Assert.True(Key(tracker.Setup) == Key(rebuilt.Setup), at + "setups differ");
                Assert.True(tracker.TradingDate == rebuilt.TradingDate, at + "trading dates differ");
            }

            _output.WriteLine(Summary(seed, steps));
        });
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [MemberData(nameof(Seeds))]
    public void SameHistoryInTwoFreshTrackers_IdenticalAnswers(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            List<string> first = Answers(History(seed));
            List<string> second = Answers(History(seed));

            Assert.Equal(first, second);
        });
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void Histories_ProduceSetupsOfBothSidesAndVoids()
    {
        // Without setups and voids the properties above would prove nothing.
        var all = new List<string>();
        foreach (object[] row in Seeds)
        {
            int seed = (int)row[0];
            IReadOnlyList<Step> steps = History(seed);
            _output.WriteLine(Summary(seed, steps));
            all.AddRange(Answers(steps));
        }

        Assert.Contains(all, key => key.StartsWith("Setup RH-", StringComparison.Ordinal));
        Assert.Contains(all, key => key.StartsWith("Setup RL-", StringComparison.Ordinal));
        Assert.Contains(all, key => key.StartsWith("Void ", StringComparison.Ordinal));
        Assert.True(all.Count(key => key.StartsWith("Setup ", StringComparison.Ordinal)) >= 40, "too few setups: " + Text(all.Count(key => key.StartsWith("Setup ", StringComparison.Ordinal))));
    }

    private static SessionRangeEvent? Apply(SessionRangeTracker tracker, Step step)
    {
        SessionRangeEvent? answer = tracker.OnM15Closed(step.Bar, step.Bias, step.AtrD1, step.AtrH4);
        if (step.ReportNotPending && tracker.Setup is not null)
        {
            tracker.OrderNoLongerPending(tracker.Setup.Id);
        }

        return answer;
    }

    private static List<string> Answers(IReadOnlyList<Step> steps)
    {
        var tracker = new SessionRangeTracker(new SessionRangeParameters(), 2.0, TimeSpan.FromHours(12.0));
        return steps.Select(step => Key(Apply(tracker, step))).Where(key => key != "-").ToList();
    }

    private static DateTime TradingDate(Step step)
    {
        return NewYorkClock.TradingDate(step.Bar.OpenTimeUtc);
    }

    private static string Key(SessionRangeEvent? answer)
    {
        return answer is null ? "-" : answer.Kind + " " + Key(answer.Setup);
    }

    private static string Key(SessionRangeSetup? setup)
    {
        if (setup is null)
        {
            return "-";
        }

        return setup.Id + " " + setup.Side + " " + Invariant.Text(setup.Entry) + " " + Invariant.Text(setup.Stop) + " " + Invariant.Text(setup.Target) + " "
            + Invariant.Minute(setup.ExpiryUtc) + " " + Invariant.Text(setup.RangeHigh) + " " + Invariant.Text(setup.RangeLow) + " " + Invariant.Minute(setup.TradingDate);
    }

    /// <summary>
    /// A random history. Even seeds start on 20 February 2025 (the March switch is on 9 March),
    /// odd seeds on 13 October 2025 (the November switch is on 2 November). No bar opens on a
    /// Saturday or before 21:00 UTC on a Sunday; 2% of the bars are missing, and with a chance of
    /// 1 in 300 a bar starts an outage of 1 to 40 bars.
    /// </summary>
    private static IReadOnlyList<Step> History(int seed)
    {
        var random = new SeededGenerator(seed);
        DateTime start = seed % 2 == 0 ? new DateTime(2025, 2, 20, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2025, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var steps = new List<Step>();
        long price = 200000;
        int outage = 0;
        var biasByDate = new Dictionary<DateTime, Bias>();
        for (int k = 0; k < CalendarDays * 96; k++)
        {
            DateTime openUtc = start.AddMinutes(15 * k);
            long open = price;
            long close = open + random.Next(121) - 60;
            long high = Math.Max(open, close) + random.Next(40);
            long low = Math.Min(open, close) - random.Next(40);
            price = close;
            bool closed = openUtc.DayOfWeek == DayOfWeek.Saturday || (openUtc.DayOfWeek == DayOfWeek.Sunday && openUtc.Hour < 21);
            if (outage == 0 && random.Next(300) == 0)
            {
                outage = 1 + random.Next(40);
            }

            bool missing = random.Next(50) == 0;
            if (outage > 0)
            {
                outage--;
                continue;
            }

            if (closed || missing)
            {
                continue;
            }

            DateTime tradingDate = NewYorkClock.TradingDate(openUtc);
            if (!biasByDate.TryGetValue(tradingDate, out Bias bias))
            {
                bias = (Bias)random.Next(3);
                biasByDate[tradingDate] = bias;
            }

            var bar = new Bar(openUtc, open / 100.0, high / 100.0, low / 100.0, close / 100.0);
            steps.Add(new Step(bar, bias, Atr(random, 8, 4.0), Atr(random, 12, 3.0), random.Next(25) == 0));
        }

        return steps;
    }

    private static double? Atr(SeededGenerator random, int missingOneIn, double scale)
    {
        if (random.Next(missingOneIn) == 0)
        {
            return null;
        }

        return scale * (1 + random.Next(4));
    }

    private static string Summary(int seed, IReadOnlyList<Step> steps)
    {
        List<string> answers = Answers(steps);
        return "seed " + Text(seed) + ": " + Text(steps.Count) + " M15 bars, "
            + Text(answers.Count(key => key.StartsWith("Setup RH-", StringComparison.Ordinal))) + " buy and "
            + Text(answers.Count(key => key.StartsWith("Setup RL-", StringComparison.Ordinal))) + " sell setups, "
            + Text(answers.Count(key => key.StartsWith("Void ", StringComparison.Ordinal))) + " voids";
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record Step(Bar Bar, Bias Bias, double? AtrD1, double? AtrH4, bool ReportNotPending);
}
