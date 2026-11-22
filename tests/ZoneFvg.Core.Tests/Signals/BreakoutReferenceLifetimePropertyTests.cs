using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-027 on the 20 seeded histories of <see cref="BreakoutTrackerPropertyTests"/> (complete D1
/// series, M15 bars Monday 00:00 to Friday 23:45, D1 bars on 00:00 UTC boundaries). Computed from
/// the bars alone: no M15 bar opens 24 hours or more after the first M15 bar that follows the close
/// of the D1 bar before it, so the rule is never the reason for a missing confirmation there, and
/// the property, determinism and mirror tests of SIG-025 on these histories prove that nothing
/// changed on a complete series.
/// </summary>
public class BreakoutReferenceLifetimePropertyTests
{
    private const int TradingDays = 70;

    private static readonly TimeSpan OneDay = TimeSpan.FromHours(24.0);

    private readonly ITestOutputHelper _output;

    public BreakoutReferenceLifetimePropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [MemberData(nameof(BreakoutTrackerPropertyTests.Seeds), MemberType = typeof(BreakoutTrackerPropertyTests))]
    public void CompleteD1Series_NoM15BarOpensADayOrMoreAfterTheFirstBarOfItsReferenceDay(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            int reference = -1;
            DateTime? anchor = null;
            TimeSpan longest = TimeSpan.Zero;
            int measured = 0;
            foreach (Bar bar in history.M15)
            {
                // The reference of the bar: the last D1 bar that closed at or before the bar opened.
                while (reference + 1 < history.D1.Count && history.D1[reference + 1].OpenTimeUtc + OneDay <= bar.OpenTimeUtc)
                {
                    reference++;
                    anchor = null;
                }

                if (reference < 0)
                {
                    continue;
                }

                anchor ??= bar.OpenTimeUtc;
                TimeSpan since = bar.OpenTimeUtc - anchor.Value;
                Assert.True(since < OneDay, "M15 bar opening " + Stamp(bar.OpenTimeUtc) + " is " + since.ToString("c", CultureInfo.InvariantCulture) + " after the first bar " + Stamp(anchor.Value) + " of its reference day");
                longest = since > longest ? since : longest;
                measured++;
            }

            Assert.True(measured > 6000, "only " + measured.ToString(CultureInfo.InvariantCulture) + " M15 bars had a reference");
            Assert.Equal(TimeSpan.FromMinutes((23 * 60) + 45), longest);
            _output.WriteLine("seed " + seed.ToString(CultureInfo.InvariantCulture) + ": " + measured.ToString(CultureInfo.InvariantCulture) + " M15 bars with a reference, longest time after the first bar of the day " + longest.ToString("c", CultureInfo.InvariantCulture));
        });
    }

    private static string Stamp(DateTime timeUtc)
    {
        return timeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
