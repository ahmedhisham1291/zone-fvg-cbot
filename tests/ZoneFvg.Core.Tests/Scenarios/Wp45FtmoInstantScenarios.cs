using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// WP45 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>: the trailing emergency guard
/// (RSK-021) and the consistency readout (OPS-012). Data: <see cref="RecoveryData"/>; Wednesday
/// 2026-03-04, CE(S)T day from 2026-03-03 23:00 UTC; RiskEpochDate 2026-01-01; InitialCapital
/// 100,000.
/// </summary>
/// <remarks>
/// The consistency history: +5,000 on 20 January, +6,000 on 10 February and +4,000 on 11 February
/// (manual trades, closed at 10:00 UTC), so the balance is 115,000 and equals the kill switch's
/// peak. Since 1 January: total 15,000, best day 10 February with 6,000, 40%. Since 1 February:
/// total 10,000, 60%.
/// </remarks>
public class Wp45FtmoInstantScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime Nine = A.AddHours(1.0);

    /// <summary>00:00 CET on Thursday 5 March 2026.</summary>
    private static readonly DateTime NextDay = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);

    /// <summary>00:00 CET on 1 February 2026, where a readout from RewardPeriodStartDate 2026-02-01 starts.</summary>
    private static readonly DateTime February = new(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime RewardDate = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ClosedTradeInfo Manual(long id, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(id, RecoveryData.Symbol, "manual", closeUtc.AddHours(-1.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static BotParameters WithRisk(Func<RiskParameters, RiskParameters> change)
    {
        BotParameters parameters = RecoveryData.Parameters();
        return parameters with { Risk = change(parameters.Risk) };
    }

    private static BotHarness ConsistencyHarness(DateTime atUtc)
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(atUtc, ExecutionData.Account(115000.0, 115000.0, 115000.0)));
        h.Broker.AddClosedTrade(Manual(801, new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc), 5000.0, 105000.0));
        h.Broker.AddClosedTrade(Manual(802, new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc), 6000.0, 111000.0));
        h.Broker.AddClosedTrade(Manual(803, new DateTime(2026, 2, 11, 10, 0, 0, DateTimeKind.Utc), 4000.0, 115000.0));
        return h;
    }

    private static List<DateTime> CountHistoryReads(BotHarness h)
    {
        var reads = new List<DateTime>();
        h.Port.History = since =>
        {
            reads.Add(since);
            return h.Broker.GetHistory(since);
        };
        return reads;
    }

    // ------------------------------------------------------------------ RSK-021

    /// <summary>Peak 106,000 on 5 January, back to 98,000 on 2 March: balance and equity 98,000 at the start.</summary>
    private static BotHarness TrailingHarness()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98000.0, 98000.0, 98000.0)));
        h.Broker.AddClosedTrade(Manual(701, new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc), 6000.0, 106000.0));
        h.Broker.AddClosedTrade(Manual(702, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc), -8000.0, 98000.0));
        return h;
    }

    private static RiskParameters Instant(RiskParameters risk, FtmoMaxLossMode mode)
    {
        return risk with { FtmoMaxLossMode = mode, FtmoDailyLossPercent = 3.0, FtmoMaxLossPercent = 10.0, FtmoGuardBufferPercent = 0.5 };
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void TrailingGuard_EquityAtPeakMinus9500OnTheTimer_ClosesAllAndHaltsAndNamesThePeak()
    {
        BotHarness h = TrailingHarness();
        BotCore core = h.Start(Nine, WithRisk(r => Instant(r, FtmoMaxLossMode.TrailingBalance)));
        Assert.Equal(BotState.Running, core.State);

        // Equity 96,500 = 106,000 - 9,500. The daily floor is 98,000 - 2,500 = 95,500, not reached.
        h.Broker.Account = ExecutionData.Account(98000.0, 96500.0, 96500.0);
        h.Timer(Nine.AddMinutes(1.0));

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.EmergencyGuard }, core.HaltCauses);
        string alert = Assert.Single(h.AlertLines(AlertKind.EmergencyGuard));
        Assert.Contains(JournalFormat.Escape("daily floor 95500 overall floor 96500 (trailing, peak 106000);"), alert, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void TrailingGuard_OneCentAboveTheFloor_KeepsRunning()
    {
        BotHarness h = TrailingHarness();
        BotCore core = h.Start(Nine, WithRisk(r => Instant(r, FtmoMaxLossMode.TrailingBalance)));

        h.Broker.Account = ExecutionData.Account(98000.0, 96500.01, 96500.01);
        h.Timer(Nine.AddMinutes(1.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, h.Alerts(AlertKind.EmergencyGuard));
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void StaticGuard_SameAccount_DoesNotFireAt96500()
    {
        // Static mode with the same Instant percents: the floor is 100,000 - 9,500 = 90,500.
        BotHarness h = TrailingHarness();
        BotCore core = h.Start(Nine, WithRisk(r => Instant(r, FtmoMaxLossMode.Static)));

        h.Broker.Account = ExecutionData.Account(98000.0, 96500.0, 96500.0);
        h.Timer(Nine.AddMinutes(1.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, h.Alerts(AlertKind.EmergencyGuard));
    }

    // ------------------------------------------------------------------ OPS-012: when the line is written

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_AtStartAndOnceAtTheFirstTimerOfEachNewCestDay()
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0 }));

        string first = JournalFormat.Time(A) + " consistency since=2026-01-01 total=15000.00 bestDay=2026-02-10 bestDayProfit=6000.00 ratio=40.00 limit=30.00 met=no";
        Assert.Equal(new[] { first }, h.Journal.Lines(ConsistencyReadout.Category));

        h.Timer(A.AddMinutes(1.0));
        h.Timer(NextDay.AddMinutes(-1.0));
        Assert.Single(h.Journal.Lines(ConsistencyReadout.Category));

        h.Timer(NextDay);
        h.Timer(NextDay.AddMinutes(1.0));
        h.Timer(NextDay.AddHours(12.0));

        string second = JournalFormat.Time(NextDay) + " consistency since=2026-01-01 total=15000.00 bestDay=2026-02-10 bestDayProfit=6000.00 ratio=40.00 limit=30.00 met=no";
        Assert.Equal(new[] { first, second }, h.Journal.Lines(ConsistencyReadout.Category));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_FirstBarOfANewCestDay_WritesTheLineOnce()
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0 }));

        h.FeedTo(NextDay.AddMinutes(-15.0));
        Assert.Single(h.Journal.Lines(ConsistencyReadout.Category));

        h.FeedTo(NextDay.AddHours(2.0));

        IReadOnlyList<string> lines = h.Journal.Lines(ConsistencyReadout.Category);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith(JournalFormat.Time(NextDay) + " consistency since=2026-01-01 ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_ReadsTheHistoryOnceADay()
    {
        BotHarness h = ConsistencyHarness(A);
        List<DateTime> reads = CountHistoryReads(h);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0, RewardPeriodStartDate = RewardDate }));
        h.FeedTo(A.AddHours(2.0));
        h.Timer(A.AddHours(2.0).AddMinutes(1.0));
        Assert.Equal(1, reads.Count(since => since == February));

        h.Timer(NextDay);
        h.Timer(NextDay.AddMinutes(1.0));

        Assert.Equal(2, reads.Count(since => since == February));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_LimitZero_NoLineAndNoReadoutHistoryRead()
    {
        BotHarness h = ConsistencyHarness(A);
        List<DateTime> reads = CountHistoryReads(h);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 0.0, RewardPeriodStartDate = RewardDate }));
        h.FeedTo(A.AddHours(2.0));
        h.Timer(NextDay);
        h.Timer(NextDay.AddMinutes(1.0));

        Assert.Empty(h.Journal.Lines(ConsistencyReadout.Category));
        Assert.DoesNotContain(February, reads);
        Assert.Empty(h.Journal.Entries.Where(entry => entry.Line.Contains("consistency", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_CoreDefaults_NoLine()
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A);
        h.Timer(NextDay);

        Assert.Equal(0.0, new RiskParameters().ConsistencyMaxPercent);
        Assert.Null(new RiskParameters().RewardPeriodStartDate);
        Assert.Empty(h.Journal.Lines(ConsistencyReadout.Category));
    }

    // ------------------------------------------------------------------ OPS-012: the start date

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_RewardPeriodStartDateSet_CountsFromThatDate()
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0, RewardPeriodStartDate = RewardDate }));

        string line = Assert.Single(h.Journal.Lines(ConsistencyReadout.Category));
        Assert.Equal(JournalFormat.Time(A) + " consistency since=2026-02-01 total=10000.00 bestDay=2026-02-10 bestDayProfit=6000.00 ratio=60.00 limit=30.00 met=no", line);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_RewardPeriodStartDateEmpty_CountsFromRiskEpochDate()
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0, RewardPeriodStartDate = null }));

        Assert.StartsWith(JournalFormat.Time(A) + " consistency since=2026-01-01 total=15000.00 ", Assert.Single(h.Journal.Lines(ConsistencyReadout.Category)), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ OPS-012: a history failure

    public static IEnumerable<object[]> Failures()
    {
        yield return new object[] { "throws InvalidOperationException" };
        yield return new object[] { "throws TimeoutException" };
        yield return new object[] { "returns null" };
        yield return new object[] { "returns a null entry" };
    }

    [Theory]
    [Trait("Req", "OPS-012")]
    [MemberData(nameof(Failures))]
    public void Readout_HistoryFailure_UnavailableLineNoErrorNoTradingEffect(string failure)
    {
        BotHarness h = ConsistencyHarness(A);
        h.Port.History = since =>
        {
            if (since != February)
            {
                return h.Broker.GetHistory(since);
            }

            return failure switch
            {
                "throws InvalidOperationException" => throw new InvalidOperationException("history unavailable"),
                "throws TimeoutException" => throw new TimeoutException("history timed out"),
                "returns null" => null!,
                _ => new ClosedTradeInfo[] { null! },
            };
        };

        BotCore core = h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = 30.0, RewardPeriodStartDate = RewardDate }));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(new[] { JournalFormat.Time(A) + " consistency since=2026-02-01 status=unavailable" }, h.Journal.Lines(ConsistencyReadout.Category));
        Assert.Empty(h.Journal.Lines("error"));
        Assert.Equal(0, h.Alerts(AlertKind.ErrorStorm));
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
    }

    // ------------------------------------------------------------------ OPS-012: information only

    private static (IReadOnlyList<BrokerRequest> Requests, IReadOnlyList<string> Lines) Run(double limit)
    {
        BotHarness h = ConsistencyHarness(A);
        h.Start(A, WithRisk(r => r with { ConsistencyMaxPercent = limit }));
        h.FeedTo(RecoveryData.Confirmation);
        h.Timer(RecoveryData.Confirmation.AddMinutes(1.0));
        h.FeedTo(A.AddHours(3.0));
        return (h.Broker.Requests.ToList(), h.Journal.Lines(ConsistencyReadout.Category));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Readout_MetOrNotMet_GivesIdenticalOrders()
    {
        // 40% against a limit of 30 is not met; against 50 it is; 0 writes no line.
        (IReadOnlyList<BrokerRequest> notMet, IReadOnlyList<string> notMetLines) = Run(30.0);
        (IReadOnlyList<BrokerRequest> met, IReadOnlyList<string> metLines) = Run(50.0);
        (IReadOnlyList<BrokerRequest> off, IReadOnlyList<string> offLines) = Run(0.0);

        Assert.EndsWith(" met=no", Assert.Single(notMetLines), StringComparison.Ordinal);
        Assert.EndsWith(" met=yes", Assert.Single(metLines), StringComparison.Ordinal);
        Assert.Empty(offLines);
        Assert.Contains(notMet, request => request.Call == BrokerCall.Place);
        Assert.Equal(notMet, met);
        Assert.Equal(notMet, off);
    }
}
